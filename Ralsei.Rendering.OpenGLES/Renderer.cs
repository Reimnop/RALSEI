using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGLES2;
using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core;
using Ralsei.Core.Data;
using Ralsei.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Ralsei.Rendering.OpenGLES.PostProcessing;
using Ralsei.Util;

namespace Ralsei.Rendering.OpenGLES;

public class Renderer : IRenderer, IDisposable
{
    private enum LifecycleCommandType
    {
        Create,
        Destroy
    }
    
    private readonly record struct MeshLifecycleCommand(int Id, LifecycleCommandType Type, (Vertex[] Vertices, int[] Indices)? Data = null);
    private readonly record struct TextLifecycleCommand(int Id, LifecycleCommandType Type, RenderGlyph[]? Data = null);
    private readonly record struct TextureLifecycleCommand(int Id, LifecycleCommandType Type, (int Width, int Height, byte[] Rgba8PixelData, TextureSampling Sampling)? Data = null);
    
    private const int MsaaSamples = 4;
    private const int FontTextureWidth = 2048;

    // Only so many textures can be bound at once, and a draw has the slot its texture is in. The texture units of the slots start
    // after the four of the font's.
    private const int FirstTextureSlotUnit = 4;
    private const int MaxTexturePageSize = 16;

    private struct MeshInfo
    {
        public Allocation VertexBufferAllocation;
        public Allocation IndexBufferAllocation;
        public int VertexCount; // not currently used
        public int IndexCount;
    }

    private struct TextInfo
    {
        public Allocation Allocation;
        public int GlyphCount;
    }

    private struct TextureInfo
    {
        public int Handle;
        public bool CrispEdges;
    }
    
    private readonly IOpenGLSurface surface;

    private readonly int programHandle;
    
    private readonly int glyphProgramHandle;
    private readonly int glyphMvpUniformLocation, glyphZUniformLocation, glyphBaseColorUniformLocation;
    private readonly int glyphViewportSizeUniformLocation;

    // TMPX global font data as texture buffers (populated by UpdateFontData)
    private int fontCurvesTextureHandle;
    private int fontCurveIndicesTextureHandle;
    private int fontBandEntriesTextureHandle;
    private int fontShapeEntriesTextureHandle;

    private Vector2i currentFboSize;
    private int fboColorBufferHandle, fboDepthBufferHandle;
    private readonly int fboHandle;
    private int postProcessTextureHandle1, postProcessTextureHandle2;
    private readonly int postProcessFboHandle;

    // What the passes of a frame are layered on, when there are more than one. Made when a frame needs it
    private int compositeTextureHandle;

    private readonly int emptyVao;
    
    // Post-processing
    private readonly LegacyBloom legacyBloom;
    private readonly UniversalBloom universalBloom;
    private readonly Glitch glitch;
    private readonly Grain grain;
    private readonly UberPost uberPost;
    private readonly Blend blend;
    private readonly DepthOfField depthOfField;
    
    // Temporary draw data lists
    private readonly List<DrawCommandWithDepth> opaqueDrawCommands = [];
    private readonly List<DrawCommandWithDepth> transparentDrawCommands = [];

    private readonly int mainVertexArrayHandle;
    private int mainVertexBufferHandle, mainIndexBufferHandle;

    private readonly int textVertexArrayHandle;
    private int textInstanceBufferHandle;
    
    // Unlit instancing buffer
    private const int InitialUnlitInstanceBufferCapacity = 1024;
    
    private GpuUnlitDrawItem[] unlitDrawItems = new GpuUnlitDrawItem[InitialUnlitInstanceBufferCapacity];
    private int unlitInstanceBuffer;
    private int unlitInstanceBufferCapacity = InitialUnlitInstanceBufferCapacity;
    
    // Mesh rendering data
    private const int InitialVertexBufferCapacity = 1024;
    private const int InitialIndexBufferCapacity = 1024;

    private readonly PooledSuballocator vertexBufferAllocator = new(InitialVertexBufferCapacity);
    private readonly PooledSuballocator indexBufferAllocator = new(InitialIndexBufferCapacity);

    private readonly List<MeshInfo> meshInfos = [];

    // Texture data
    private readonly List<TextureInfo?> textureInfos = [];
    private readonly int texturePageSize;
    private readonly TexturePager texturePager = new();
    
    // Text rendering data
    private const int InitialGlyphBufferCapacity = 1024;
    
    private readonly PooledSuballocator glyphBufferAllocator = new(InitialGlyphBufferCapacity);
    private readonly List<TextInfo> textInfos = [];
    
    // Lifecycle command lists
    private readonly Queue<MeshLifecycleCommand> meshLifecycleCommands = [];
    private readonly Queue<TextLifecycleCommand> textLifecycleCommands = [];
    private readonly Queue<TextureLifecycleCommand> textureLifecycleCommands = [];

    // Dirty flags
    private bool fontBuffersDirty = true;
    
    // Injected dependencies
    private readonly RenderingFactory renderingFactory;
    private readonly ILogger<Renderer> logger;
    
    public Renderer(
        IRenderingFactory renderingFactory,
        IOpenGLSurface surface,
        ResourceLoader loader,
        ILogger<Renderer> logger)
    {
        this.renderingFactory = (RenderingFactory) renderingFactory;
        this.surface = surface;
        this.logger = logger;
        
        logger.LogInformation("Initializing OpenGL ES renderer");
        
        // Log OpenGL information
        logger.LogInformation("OpenGL ES: {Version}", GL.GetString(StringName.Version));
        logger.LogInformation("Renderer: {Renderer}", GL.GetString(StringName.Renderer));
        logger.LogInformation("Vendor: {Vendor}", GL.GetString(StringName.Vendor));
        logger.LogInformation("Shading language: {ShadingLanguage}", GL.GetString(StringName.ShadingLanguageVersion));
        
        #region OpenGL Data Initialization

        {
            var initialSize = this.surface.RenderSize;
            
            // How many textures the draws of a page can use together: what a fragment shader can sample from, and what fits in the units
            // after the font's
            texturePageSize = Math.Clamp(
                Math.Min(GL.GetInteger(GetPName.MaxTextureImageUnits), GL.GetInteger(GetPName.MaxCombinedTextureImageUnits) - FirstTextureSlotUnit),
                1, MaxTexturePageSize);
            if (int.TryParse(Environment.GetEnvironmentVariable("RALSEI_TEXTURE_PAGE_SIZE"), out var requestedPageSize) && requestedPageSize > 0)
                texturePageSize = Math.Min(texturePageSize, requestedPageSize); // to try what many pages do
            logger.LogInformation("Textures bound at once: {PageSize}", texturePageSize);
            var pageSizeDefine = new KeyValuePair<string, string?>("RALSEI_TEXTURE_PAGE_SIZE", texturePageSize.ToString(CultureInfo.InvariantCulture));

            // Create main program handle
            programHandle = LoaderUtil.LoadShaderProgram(loader, "Unlit.vert", "Unlit.frag", pageSizeDefine);

            // Set texture units
            var texturesUniformLocation = GL.GetUniformLocation(programHandle, "uTextures");
            GL.UseProgram(programHandle);
            for (var i = 0; i < texturePageSize; i++)
                GL.Uniform1i(texturesUniformLocation + i, i + FirstTextureSlotUnit);

            // Create glyph program handle
            glyphProgramHandle = LoaderUtil.LoadShaderProgram(loader, "Text.vert", "Text.frag");

            // Get glyph uniform locations
            glyphMvpUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uMvp");
            glyphZUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uZ");
            glyphBaseColorUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uBaseColor");
            glyphViewportSizeUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uViewportSize");

            // Bind texture sampler uniforms to fixed texture units
            var curvesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uCurves");
            var curveIndicesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uCurveIndices");
            var bandEntriesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uBandEntries");
            var shapeEntriesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uShapeEntries");

            GL.UseProgram(glyphProgramHandle);
            GL.Uniform1i(curvesUniformLocation, 0);
            GL.Uniform1i(curveIndicesUniformLocation, 1);
            GL.Uniform1i(bandEntriesUniformLocation, 2);
            GL.Uniform1i(shapeEntriesUniformLocation, 3);
            
            // Create unlit instancing buffer
            unlitInstanceBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, unlitInstanceBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, InitialUnlitInstanceBufferCapacity * Unsafe.SizeOf<GpuUnlitDrawItem>(), IntPtr.Zero, BufferUsage.DynamicDraw);

            // Create main VAO
            mainVertexArrayHandle = GL.GenVertexArray();
            GL.BindVertexArray(mainVertexArrayHandle);
            
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);
            GL.EnableVertexAttribArray(3);
            GL.EnableVertexAttribArray(4);
            GL.EnableVertexAttribArray(5);
            GL.EnableVertexAttribArray(6);
            GL.EnableVertexAttribArray(7);
            GL.EnableVertexAttribArray(8);
            GL.EnableVertexAttribArray(9);
            
            GL.VertexAttribDivisor(2, 1);
            GL.VertexAttribDivisor(3, 1);
            GL.VertexAttribDivisor(4, 1);
            GL.VertexAttribDivisor(5, 1);
            GL.VertexAttribDivisor(6, 1);
            GL.VertexAttribDivisor(7, 1);
            GL.VertexAttribDivisor(8, 1);
            GL.VertexAttribDivisor(9, 1);
            
            // Create text instance VAO
            textVertexArrayHandle = GL.GenVertexArray();
            GL.BindVertexArray(textVertexArrayHandle);

            // All attributes are per-instance (divisor=1); the 6 quad vertices come from gl_VertexID.
            GL.EnableVertexAttribArray(0);  // transform col 0
            GL.EnableVertexAttribArray(1);  // transform col 1
            GL.EnableVertexAttribArray(2);  // transform col 2
            GL.EnableVertexAttribArray(3);  // color
            GL.EnableVertexAttribArray(4);  // shapeEntryIndex

            GL.VertexAttribDivisor(0, 1);
            GL.VertexAttribDivisor(1, 1);
            GL.VertexAttribDivisor(2, 1);
            GL.VertexAttribDivisor(3, 1);
            GL.VertexAttribDivisor(4, 1);
            
            // Initialize FBO
            fboColorBufferHandle = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.Rgba16f, initialSize.X, initialSize.Y);

            fboDepthBufferHandle = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.DepthComponent32f, initialSize.X, initialSize.Y);

            fboHandle = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);

            // Initialize post-process FBO
            postProcessTextureHandle1 = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle1);
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, initialSize.X, initialSize.Y);

            postProcessTextureHandle2 = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle2);
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, initialSize.X, initialSize.Y);

            // Allocate post-process FBO
            postProcessFboHandle = GL.GenFramebuffer();
            
            currentFboSize = initialSize;
            
            // Create empty VAO
            emptyVao = GL.GenVertexArray();

            // Initialize post-processing
            var vertexShader = LoaderUtil.LoadPostProcessVertexShader(loader);

            legacyBloom = new LegacyBloom(loader, vertexShader);
            universalBloom = new UniversalBloom(loader, vertexShader);
            glitch = new Glitch(loader, vertexShader);
            grain = new Grain(loader, vertexShader);
            uberPost = new UberPost(loader, vertexShader);
            blend = new Blend(loader, vertexShader);
            depthOfField = new DepthOfField(loader, vertexShader);

            // Clean up
            GL.DeleteShader(vertexShader);
        }

        #endregion
        
        // Push all current meshes into the lifecycle command list so that they are created in the next frame
        foreach (var (id, mesh) in this.renderingFactory.Meshes)
            meshLifecycleCommands.Enqueue(new MeshLifecycleCommand(id, LifecycleCommandType.Create, (mesh.Vertices, mesh.Indices)));
        
        // Push all current textures and texts into the lifecycle command lists so that they are created in the next frame
        foreach (var (id, texture) in this.renderingFactory.Textures)
            textureLifecycleCommands.Enqueue(new TextureLifecycleCommand(id, LifecycleCommandType.Create, (texture.Width, texture.Height, texture.Rgba8PixelData, texture.Sampling)));

        foreach (var (id, text) in this.renderingFactory.Texts)
            textLifecycleCommands.Enqueue(new TextLifecycleCommand(id, LifecycleCommandType.Create, text.Glyphs));
        
        // Subscribe to events
        this.renderingFactory.FontBuffersUpdated += OnFontBuffersUpdated;
        this.renderingFactory.Meshes.ItemInserted += OnMeshInserted;
        this.renderingFactory.Meshes.ItemRemoved += OnMeshRemoved;
        this.renderingFactory.Textures.ItemInserted += OnTextureInserted;
        this.renderingFactory.Textures.ItemRemoved += OnTextureRemoved;
        this.renderingFactory.Texts.ItemInserted += OnTextInserted;
        this.renderingFactory.Texts.ItemRemoved += OnTextRemoved;
    }
    
    public void Dispose()
    {
        logger.LogInformation("Disposing OpenGL ES renderer");
        
        // Unsubscribe from events
        renderingFactory.FontBuffersUpdated -= OnFontBuffersUpdated;
        renderingFactory.Meshes.ItemInserted -= OnMeshInserted;
        renderingFactory.Meshes.ItemRemoved -= OnMeshRemoved;
        renderingFactory.Textures.ItemInserted -= OnTextureInserted;
        renderingFactory.Textures.ItemRemoved -= OnTextureRemoved;
        renderingFactory.Texts.ItemInserted -= OnTextInserted;
        renderingFactory.Texts.ItemRemoved -= OnTextRemoved;
        
        // Context already lost, no need to continue disposing
        if (surface.IsContextLost)
            return;
        
        // Delete GL resources
        GL.DeleteProgram(programHandle);
        GL.DeleteProgram(glyphProgramHandle);
        
        GL.DeleteBuffer(mainVertexBufferHandle);
        GL.DeleteBuffer(mainIndexBufferHandle);
        GL.DeleteVertexArray(mainVertexArrayHandle);
        
        GL.DeleteBuffer(unlitInstanceBuffer);

        foreach (var textureInfo in textureInfos.Where(x => x.HasValue).Select(x => x!.Value))
            GL.DeleteTexture(textureInfo.Handle);

        GL.DeleteBuffer(textInstanceBufferHandle);
        GL.DeleteVertexArray(textVertexArrayHandle);
        
        GL.DeleteTexture(fontCurvesTextureHandle);
        GL.DeleteTexture(fontCurveIndicesTextureHandle);
        GL.DeleteTexture(fontBandEntriesTextureHandle);
        GL.DeleteTexture(fontShapeEntriesTextureHandle);

        GL.DeleteRenderbuffer(fboColorBufferHandle);
        GL.DeleteRenderbuffer(fboDepthBufferHandle);
        GL.DeleteFramebuffer(fboHandle);
        
        GL.DeleteTexture(postProcessTextureHandle1);
        GL.DeleteTexture(postProcessTextureHandle2);
        GL.DeleteFramebuffer(postProcessFboHandle);
        
        GL.DeleteVertexArray(emptyVao);
        
        legacyBloom.Dispose();
        universalBloom.Dispose();
        glitch.Dispose();
        grain.Dispose();
        uberPost.Dispose();
        blend.Dispose();
        depthOfField.Dispose();
        
        GL.DeleteTexture(compositeTextureHandle);
    }

    private void OnFontBuffersUpdated(object? sender, EventArgs e)
    {
        fontBuffersDirty = true;
    }

    private void OnTextInserted(object? sender, ObservableSparseSetEventArgs<Text> e)
    {
        textLifecycleCommands.Enqueue(new TextLifecycleCommand(e.Id, LifecycleCommandType.Create, e.Item.Glyphs));
    }

    private void OnTextRemoved(object? sender, ObservableSparseSetEventArgs<Text> e)
    {
        textLifecycleCommands.Enqueue(new TextLifecycleCommand(e.Id, LifecycleCommandType.Destroy));
    }
    
    private void OnTextureInserted(object? sender, ObservableSparseSetEventArgs<Texture> e)
    {
        textureLifecycleCommands.Enqueue(new TextureLifecycleCommand(e.Id, LifecycleCommandType.Create, (e.Item.Width, e.Item.Height, e.Item.Rgba8PixelData, e.Item.Sampling)));
    }

    private void OnTextureRemoved(object? sender, ObservableSparseSetEventArgs<Texture> e)
    {
        textureLifecycleCommands.Enqueue(new TextureLifecycleCommand(e.Id, LifecycleCommandType.Destroy));
    }

    private void OnMeshInserted(object? sender, ObservableSparseSetEventArgs<Mesh> e)
    {
        meshLifecycleCommands.Enqueue(new MeshLifecycleCommand(e.Id, LifecycleCommandType.Create, (e.Item.Vertices, e.Item.Indices)));
    }

    private void OnMeshRemoved(object? sender, ObservableSparseSetEventArgs<Mesh> e)
    {
        meshLifecycleCommands.Enqueue(new MeshLifecycleCommand(e.Id, LifecycleCommandType.Destroy));
    }
    
    public void ProcessFrame(IDrawDataProvider drawDataProvider)
    {
        var renderSize = surface.RenderSize;

        // Update OpenGL data
        UpdateOpenGlData(renderSize);

        // The passes are layered on a target of their own, unless there is only one
        var passCount = drawDataProvider.PassCount;
        var layered = passCount > 1;
        if (layered)
        {
            if (compositeTextureHandle == 0)
            {
                compositeTextureHandle = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2d, compositeTextureHandle);
                GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, currentFboSize.X, currentFboSize.Y);
            }

            // Nothing is there to start with
            blend.Clear(currentFboSize, compositeTextureHandle);
        }

        var finalTexture = postProcessTextureHandle1;
        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            var passTexture = RenderPass(drawDataProvider.CreateDrawData(passIndex), renderSize, layered);
            if (layered)
                blend.Process(currentFboSize, passTexture, compositeTextureHandle);
            else
                finalTexture = passTexture;
        }

        if (layered)
            finalTexture = compositeTextureHandle;

        // The post-processing of the frame as a whole, on what the passes came to
        if (drawDataProvider.FinalPostProcessingState is { } finalState)
        {
            var spareTexture = finalTexture == postProcessTextureHandle1 ? postProcessTextureHandle2 : postProcessTextureHandle1;
            finalTexture = HandlePostProcessing(finalState, finalTexture, spareTexture);
        }

        // Present to window
        surface.Present(finalTexture, renderSize, new ColorRgba());
    }

    /// <summary>
    /// Draws a pass and does its post-processing. Returns the texture that has the result, which is one of the post-processing textures.
    /// </summary>
    private int RenderPass(in DrawData drawData, Vector2i renderSize, bool layered)
    {
        // Get camera matrix (view and projection)
        var camera = RenderUtil.GetCameraMatrix(drawData.CameraState, renderSize);

        // Split draw list into opaque and transparent
        opaqueDrawCommands.Clear();
        transparentDrawCommands.Clear();

        var currentDepthInt = 0;
        foreach (var drawCommand in drawData.DrawCommands)
        {
            // Add to appropriate list
            if (RenderUtil.ShouldUseTransparentDrawList(drawCommand, drawData))
            {
                transparentDrawCommands.Add(new DrawCommandWithDepth
                {
                    DrawType = drawCommand.DrawType,
                    DrawId = drawCommand.DrawId,
                    Depth = currentDepthInt / (float)(1 << 23)
                });
            }
            else
            {
                opaqueDrawCommands.Add(new DrawCommandWithDepth
                {
                    DrawType = drawCommand.DrawType,
                    DrawId = drawCommand.DrawId,
                    Depth = currentDepthInt / (float)(1 << 23)
                });
            }

            currentDepthInt++;
        }

        // Reverse opaque draw data list so that it is drawn
        // from back to front to avoid overdraw
        opaqueDrawCommands.Reverse();

        // Bind FBO
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);

        // Get rid of samplers
        GL.BindSampler(0, 0);
        GL.BindSampler(1, 0);
        GL.BindSampler(2, 0);
        GL.BindSampler(3, 0);

        // Bind the four TMPX texture buffers to texture units 0-3
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, fontCurvesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2d, fontCurveIndicesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2d, fontBandEntriesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2d, fontShapeEntriesTextureHandle);

        // Clear the screen. The target holds premultiplied colors, and so does what it is cleared to
        GL.Viewport(0, 0, renderSize.X, renderSize.Y);
        var clearColor = drawData.ClearColor;
        GL.ClearColor(clearColor.R * clearColor.A, clearColor.G * clearColor.A, clearColor.B * clearColor.A, clearColor.A);
        GL.ClearDepthf(0.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Set depth function
        GL.DepthFunc(DepthFunction.Greater);

        // Opaque pass, disable blending, enable depth testing
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);

        // Draw each mesh
        RenderDrawDataList(
            CollectionsMarshal.AsSpan(opaqueDrawCommands),
            drawData,
            camera);

        // Transparent pass, enable blending, disable depth write
        // The shaders output premultiplied alpha
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);

        // Draw each mesh
        RenderDrawDataList(
            CollectionsMarshal.AsSpan(transparentDrawCommands),
            drawData,
            camera);

        // Restore depth write state
        GL.DepthMask(true);

        // Bind texture 1 to post-process FBO
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, postProcessFboHandle);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, postProcessTextureHandle1, 0);

        // Blit FBO to post-process FBO
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboHandle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, postProcessFboHandle);
        GL.BlitFramebuffer(
            0, 0,
            renderSize.X, renderSize.Y,
            0, 0,
            renderSize.X, renderSize.Y,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // Process post-process effects
        // The passes of a layered frame that have no effects to finish with are left as they are, as the frame has them
        return HandlePostProcessing(drawData.PostProcessingState, postProcessTextureHandle1, postProcessTextureHandle2, skipIfNoFinalEffects: layered);
    }

    private int HandlePostProcessing(PostProcessingState state, int texture1, int texture2, bool skipIfNoFinalEffects = false)
    {
        // Disable depth testing and blending
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);

        if (depthOfField.Process(currentFboSize, state.DepthOfField.Radius, texture1, texture2))
            Swap(ref texture1, ref texture2);

        if (legacyBloom.Process(currentFboSize, state.LegacyBloom.Intensity, state.LegacyBloom.Diffusion, state.LegacyBloom.Color, texture1, texture2))
            Swap(ref texture1, ref texture2);
        
        if (universalBloom.Process(currentFboSize, state.UniversalBloom.Intensity, state.UniversalBloom.Diffusion, state.UniversalBloom.Color, texture1, texture2))
            Swap(ref texture1, ref texture2);
        
        if (glitch.Process(currentFboSize, state.Time, state.Glitch.Speed, state.Glitch.Intensity, state.Glitch.Amount, state.Glitch.StretchMultiplier, texture1, texture2))
            Swap(ref texture1, ref texture2);
        
        if (grain.Process(currentFboSize, state.Time, state.Grain.Colored, state.Grain.Intensity, state.Grain.Size, state.Grain.LuminanceContribution, texture1, texture2))
            Swap(ref texture1, ref texture2);
        
        if ((!skipIfNoFinalEffects || state.HasFinalEffects) && uberPost.Process(
                currentFboSize,
                state.HueShift.Angle,
                state.LensDistortion.Intensity, state.LensDistortion.Center,
                state.ChromaticAberration.Intensity,
                state.Vignette.Center, state.Vignette.Intensity, state.Vignette.Rounded, state.Vignette.Roundness, state.Vignette.Smoothness, state.Vignette.Color, state.Vignette.Mode,
                state.Gradient.Color1, state.Gradient.Color2, state.Gradient.Intensity, state.Gradient.Rotation, state.Gradient.Mode,
                texture1, texture2))
            Swap(ref texture1, ref texture2);
        
        return texture1;
    }
    
    private void RenderDrawDataList(in Span<DrawCommandWithDepth> drawCommands, in DrawData drawData, in Matrix3x2 camera)
    {
        if (drawCommands.IsEmpty)
            return;

        var meshInfosSpan = CollectionsMarshal.AsSpan(meshInfos);
        var textInfosSpan = CollectionsMarshal.AsSpan(textInfos);

        // The draws that can be drawn with the textures bound together make a page
        texturePager.Plan(drawData, drawCommands, texturePageSize);
        UploadMeshInstances(drawData, drawCommands, camera);

        // Where the next batch's items start in the instance buffer, counted in items
        var instanceOffset = 0;

        foreach (var page in texturePager.Pages)
        {
            BindTexturePage(page);
            var pageEnd = page.FirstCommand + page.CommandCount;

            for (var i = page.FirstCommand; i < pageEnd; i++)
            {
                ref var drawCommand = ref drawCommands[i];

                switch (drawCommand.DrawType)
                {
                    case DrawType.Mesh:
                    {
                        // Take the draws that follow of the same mesh along, in order, so that they are drawn as instances
                        var meshHandleId = drawData.MeshDrawItems[drawCommand.DrawId].MeshHandle.Id;
                        var batchEnd = i + 1;
                        while (batchEnd < pageEnd &&
                               drawCommands[batchEnd].DrawType == DrawType.Mesh &&
                               drawData.MeshDrawItems[drawCommands[batchEnd].DrawId].MeshHandle.Id == meshHandleId)
                            batchEnd++;

                        var instanceCount = batchEnd - i;
                        RenderMeshBatch(instanceOffset, instanceCount, meshInfosSpan[meshHandleId]);
                        instanceOffset += instanceCount;
                        i = batchEnd - 1;
                        break;
                    }
                    case DrawType.Text:
                    {
                        ref var textDrawItem = ref drawData.TextDrawItems[drawCommand.DrawId];
                        ref var textInfo = ref textInfosSpan[textDrawItem.TextHandle.Id];

                        if (textInfo.GlyphCount == 0)
                            break;

                        var mvp = textDrawItem.Transform * camera;

                        GL.UseProgram(glyphProgramHandle);

                        unsafe
                        {
                            GL.UniformMatrix3x2fv(glyphMvpUniformLocation, 1, false, (float*)&mvp);
                        }
                        GL.Uniform1f(glyphZUniformLocation, drawCommand.Depth);
                        GL.Uniform4f(glyphBaseColorUniformLocation,
                            textDrawItem.Color.R, textDrawItem.Color.G,
                            textDrawItem.Color.B, textDrawItem.Color.A);
                        GL.Uniform2f(glyphViewportSizeUniformLocation, currentFboSize.X, currentFboSize.Y);

                        GL.BindVertexArray(textVertexArrayHandle);
                        GL.BindBuffer(BufferTarget.ArrayBuffer, textInstanceBufferHandle);

                        var stride = Unsafe.SizeOf<RenderGlyph>();
                        var baseOff = textInfo.Allocation.Offset * stride;

                        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, baseOff);
                        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, baseOff + 8);
                        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, baseOff + 16);
                        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, baseOff + 24);
                        GL.VertexAttribIPointer(4, 1, VertexAttribIType.Int, stride, baseOff + 40);

                        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, textInfo.GlyphCount);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Binds the textures of a page to their slots' units.
    /// </summary>
    private void BindTexturePage(in TexturePage page)
    {
        var textures = texturePager.TexturesOf(page);
        for (var slot = 0; slot < textures.Length; slot++)
        {
            GL.ActiveTexture((TextureUnit) ((int) TextureUnit.Texture0 + FirstTextureSlotUnit + slot));
            var info = textures[slot] < textureInfos.Count ? textureInfos[textures[slot]] : null;
            GL.BindTexture(TextureTarget.Texture2d, info?.Handle ?? 0);
        }
    }

    /// <summary>
    /// Puts the draw item of every mesh of the list in the instance buffer, in the order they are drawn.
    /// </summary>
    private void UploadMeshInstances(in DrawData drawData, in Span<DrawCommandWithDepth> drawCommands, in Matrix3x2 camera)
    {
        var count = 0;
        for (var commandIndex = 0; commandIndex < drawCommands.Length; commandIndex++)
        {
            ref var drawCommand = ref drawCommands[commandIndex];
            if (drawCommand.DrawType != DrawType.Mesh)
                continue;

            if (count >= unlitDrawItems.Length)
                Array.Resize(ref unlitDrawItems, Math.Max(count + 1, unlitDrawItems.Length * 2));

            ref var meshDrawItem = ref drawData.MeshDrawItems[drawCommand.DrawId];
            ref var unlitDrawItem = ref unlitDrawItems[count++];

            var textured = TexturedInfoOf(meshDrawItem, out var crispEdges);
            var mvp = meshDrawItem.Transform * camera;
            unlitDrawItem.Mvp1 = new Vector4(mvp.M11, mvp.M12, mvp.M21, mvp.M22);
            unlitDrawItem.Mvp2 = new Vector4(mvp.M31, mvp.M32, drawCommand.Depth, meshDrawItem.GradientRotation);
            unlitDrawItem.Color1 = meshDrawItem.Color1;
            unlitDrawItem.Color2 = meshDrawItem.Color2;
            unlitDrawItem.Fog = meshDrawItem.Fog;
            unlitDrawItem.UvRect = new Vector4(meshDrawItem.MinUV, meshDrawItem.MaxUV.X, meshDrawItem.MaxUV.Y);
            unlitDrawItem.GradientScale = meshDrawItem.GradientScale;
            unlitDrawItem.TextureSlotIndex = textured ? texturePager.SlotOf(commandIndex) : -1;
            unlitDrawItem.GradientMode = (int) meshDrawItem.RenderMode;
            unlitDrawItem.TextureFlags = crispEdges ? 1 : 0;
        }

        if (count == 0)
            return;

        var itemSize = Unsafe.SizeOf<GpuUnlitDrawItem>();
        GL.BindBuffer(BufferTarget.ArrayBuffer, unlitInstanceBuffer);
        if (unlitDrawItems.Length > unlitInstanceBufferCapacity)
        {
            // A buffer of the new size, as big as the array, so that this is not done again until the array grows
            unlitInstanceBufferCapacity = unlitDrawItems.Length;
            GL.BufferData(BufferTarget.ArrayBuffer, unlitInstanceBufferCapacity * itemSize, unlitDrawItems, BufferUsage.DynamicDraw);
        }
        else
        {
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * itemSize, unlitDrawItems);
        }
    }

    /// <summary>
    /// Whether the mesh draw samples a texture that the renderer has, and whether that is with crisp edges.
    /// </summary>
    private bool TexturedInfoOf(in MeshDrawItem item, out bool crispEdges)
    {
        var texture = item.Texture;
        if (item.RenderMode != RenderMode.Normal ||
            texture.IsNone || texture.Id >= textureInfos.Count || textureInfos[texture.Id] is not { } info)
        {
            crispEdges = false;
            return false;
        }

        crispEdges = info.CrispEdges;
        return true;
    }

    /// <summary>
    /// Draws instances of one mesh, with the draw items that are in the instance buffer from the item at <paramref name="firstInstance"/>.
    /// </summary>
    private void RenderMeshBatch(int firstInstance, int instanceCount, in MeshInfo meshInfo)
    {
        GL.UseProgram(programHandle);
        GL.BindVertexArray(mainVertexArrayHandle);

        // Point the instance attributes at the batch's items (the pointers take the buffer bound now)
        var stride = Unsafe.SizeOf<GpuUnlitDrawItem>();
        var baseOffset = firstInstance * stride;
        GL.BindBuffer(BufferTarget.ArrayBuffer, unlitInstanceBuffer);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, baseOffset);        // Mvp1
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, baseOffset + 16);   // Mvp2
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, baseOffset + 32);   // Color1
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, stride, baseOffset + 48);   // Color2
        GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, stride, baseOffset + 64);   // Fog
        GL.VertexAttribPointer(7, 4, VertexAttribPointerType.Float, false, stride, baseOffset + 80);   // UvRect
        GL.VertexAttribPointer(8, 1, VertexAttribPointerType.Float, false, stride, baseOffset + 96);   // GradientScale
        GL.VertexAttribIPointer(9, 3, VertexAttribIType.Int, stride, baseOffset + 100);                // TextureSlotIndex, GradientMode and TextureFlags

        GL.DrawElementsInstanced(
            PrimitiveType.Triangles,
            meshInfo.IndexCount,
            DrawElementsType.UnsignedInt,
            meshInfo.IndexBufferAllocation.Offset * sizeof(uint),
            instanceCount);
    }

    private void UpdateOpenGlData(Vector2i size)
    {
        UpdateMeshData();
        UpdateTextureData();
        UpdateFontData();
        UpdateTextData();
        UpdateFboData(size);
    }
    
    private void UpdateMeshData()
    {
        // Initialize vertex and index buffers if needed
        if (mainVertexBufferHandle == 0)
        {
            mainVertexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, mainVertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexBufferAllocator.Capacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, BufferUsage.DynamicDraw);
            
            // Bind vertex buffer to vertex array
            GL.BindVertexArray(mainVertexArrayHandle);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 8);
        }
        
        if (mainIndexBufferHandle == 0)
        {
            // Make sure we don't randomly bind another VAO to this index buffer
            GL.BindVertexArray(mainVertexArrayHandle);
            
            mainIndexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, mainIndexBufferHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indexBufferAllocator.Capacity * sizeof(int), IntPtr.Zero, BufferUsage.DynamicDraw);
        }

        while (meshLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    Debug.Assert(command.Data.HasValue);
                    
                    var (vertices, indices) = command.Data.Value;
                    indices = indices.ToArray(); // Ensure indices are a copy so we can modify them
                    var vertexCount = vertices.Length;
                    var indexCount = indices.Length;

                    var vtxAlloc = vertexBufferAllocator.Allocate(vertexCount, (oldCapacity, newCapacity) =>
                    {
                        // Recreate vertex buffer with new capacity
                        var newBufferHandle = GL.GenBuffer();
                        GL.BindBuffer(BufferTarget.ArrayBuffer, newBufferHandle);
                        GL.BufferData(BufferTarget.ArrayBuffer, newCapacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, BufferUsage.DynamicDraw);
                        
                        GL.BindBuffer(BufferTarget.CopyReadBuffer, mainVertexBufferHandle);
                        GL.BindBuffer(BufferTarget.CopyWriteBuffer, newBufferHandle);
                        GL.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, IntPtr.Zero, IntPtr.Zero, oldCapacity * Unsafe.SizeOf<Vertex>());
                        GL.DeleteBuffer(mainVertexBufferHandle);
                        mainVertexBufferHandle = newBufferHandle;
                        
                        // Bind new vertex buffer to vertex array
                        GL.BindVertexArray(mainVertexArrayHandle);
                        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0);
                        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 8);
                        
                        logger.LogInformation("Mesh vertex buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });
                    
                    var idxAlloc = indexBufferAllocator.Allocate(indexCount, (oldCapacity, newCapacity) =>
                    {
                        // Make sure we don't randomly bind another VAO to this index buffer
                        GL.BindVertexArray(mainVertexArrayHandle);
                        
                        // Recreate index buffer with new capacity
                        var newBufferHandle = GL.GenBuffer();
                        GL.BindBuffer(BufferTarget.ElementArrayBuffer, newBufferHandle);
                        GL.BufferData(BufferTarget.ElementArrayBuffer, newCapacity * sizeof(int), IntPtr.Zero, BufferUsage.DynamicDraw);
                        
                        GL.BindBuffer(BufferTarget.CopyReadBuffer, mainIndexBufferHandle);
                        GL.BindBuffer(BufferTarget.CopyWriteBuffer, newBufferHandle);
                        GL.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, IntPtr.Zero, IntPtr.Zero, oldCapacity * sizeof(int));
                        GL.DeleteBuffer(mainIndexBufferHandle);
                        mainIndexBufferHandle = newBufferHandle;
                        
                        logger.LogInformation("Mesh index buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });
                    
                    // Offset indices
                    for (var i = 0; i < indices.Length; i++)
                        indices[i] += vtxAlloc.Offset;
                    
                    // Upload vertex data
                    GL.BindBuffer(BufferTarget.ArrayBuffer, mainVertexBufferHandle);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, vtxAlloc.Offset * Unsafe.SizeOf<Vertex>(), vertexCount * Unsafe.SizeOf<Vertex>(), vertices);
                    
                    // Upload index data
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, mainIndexBufferHandle);
                    GL.BufferSubData(BufferTarget.ElementArrayBuffer, idxAlloc.Offset * sizeof(int), indexCount * sizeof(int), indices);

                    meshInfos.EnsureCount(command.Id + 1);
                    meshInfos[command.Id] = new MeshInfo
                    {
                        VertexBufferAllocation = vtxAlloc,
                        IndexBufferAllocation = idxAlloc,
                        VertexCount = vertices.Length,
                        IndexCount = indices.Length
                    };
                    break;
                }
                case LifecycleCommandType.Destroy:
                {
                    var meshInfoToDestroy = meshInfos[command.Id];
                    vertexBufferAllocator.Free(meshInfoToDestroy.VertexBufferAllocation);
                    indexBufferAllocator.Free(meshInfoToDestroy.IndexBufferAllocation);
                    meshInfos[command.Id] = default;
                    break;
                }
            }
        }
    }
    
    private void UpdateTextureData()
    {
        while (textureLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    Debug.Assert(command.Data.HasValue);
                    var (width, height, pixelData, sampling) = command.Data.Value;

                    var textureHandle = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2d, textureHandle);
                    GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba8, width, height);
                    GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixelData);

                    // The sampling is the texture's own
                    GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, GetFilter(sampling.MinificationFilter));
                    GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, GetFilter(sampling.MagnificationFilter));
                    GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, GetWrapMode(sampling.WrapModeU));
                    GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, GetWrapMode(sampling.WrapModeV));
                    if (sampling.WrapModeU == TextureAddressMode.ClampToBorder || sampling.WrapModeV == TextureAddressMode.ClampToBorder)
                    {
                        var border = sampling.BorderColor;
                        GL.TexParameterf(TextureTarget.Texture2d, TextureParameterName.TextureBorderColor, in border.R);
                    }

                    textureInfos.EnsureCount(command.Id + 1);
                    textureInfos[command.Id] = new TextureInfo
                    {
                        Handle = textureHandle,
                        CrispEdges = sampling.IsCrisp
                    };
                    break;
                }
                case LifecycleCommandType.Destroy:
                {
                    var textureInfoToDestroy = textureInfos[command.Id];
                    if (textureInfoToDestroy.HasValue)
                    {
                        GL.DeleteTexture(textureInfoToDestroy.Value.Handle);
                        textureInfos[command.Id] = null;
                    }
                    break;
                }
            }
        }

        return;

        int GetFilter(TextureFilteringMode filteringMode)
            => filteringMode switch
            {
                TextureFilteringMode.Nearest => (int) All.Nearest,
                TextureFilteringMode.Linear or TextureFilteringMode.CrispEdges => (int) All.Linear,
                _ => throw new ArgumentOutOfRangeException(nameof(filteringMode), filteringMode, null)
            };

        int GetWrapMode(TextureAddressMode wrapMode)
            => wrapMode switch
            {
                TextureAddressMode.ClampToEdge => (int) All.ClampToEdge,
                TextureAddressMode.ClampToBorder => (int) All.ClampToBorder,
                TextureAddressMode.MirroredRepeat => (int) All.MirroredRepeat,
                TextureAddressMode.Repeat => (int) All.Repeat,
                _ => throw new ArgumentOutOfRangeException(nameof(wrapMode), wrapMode, null)
            };
    }

    private void UpdateFontData()
    {
        if (!fontBuffersDirty)
            return;
        
        fontBuffersDirty = false;
        
        // Upload curves buffer
        if (fontCurvesTextureHandle != 0)
        {
            GL.DeleteTexture(fontCurvesTextureHandle);
            fontCurvesTextureHandle = 0;
        }
        
        if (renderingFactory.FontCurves.Length > 0)
        {
            fontCurvesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontCurvesTextureHandle);
            
            // each curve takes exactly 3 texels
            var texWidth = FontTextureWidth;
            var texHeight = (renderingFactory.FontCurves.Length * 3 + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rg32f, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            var bufSize = texWidth * texHeight * 2 * sizeof(float);
            var buf = new byte[bufSize];
            var fontCurvesSpan = MemoryMarshal.AsBytes(renderingFactory.FontCurves);
            fontCurvesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.Rg, PixelType.Float, buf); 
        }
        
        // Upload curve indices buffer
        if (fontCurveIndicesTextureHandle != 0)
        {
            GL.DeleteTexture(fontCurveIndicesTextureHandle);
            fontCurveIndicesTextureHandle = 0;
        }

        if (renderingFactory.FontCurveIndices.Length > 0)
        {
            fontCurveIndicesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontCurveIndicesTextureHandle);

            // each curve index takes exactly 1 texel
            var texWidth = FontTextureWidth;
            var texHeight = (renderingFactory.FontCurveIndices.Length + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.R32i, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var bufSize = texWidth * texHeight * sizeof(int);
            var buf = new byte[bufSize];
            var fontCurveIndicesSpan = MemoryMarshal.AsBytes(renderingFactory.FontCurveIndices);
            fontCurveIndicesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.RedInteger, PixelType.Int, buf);
        }
        
        // Upload band entries buffer
        if (fontBandEntriesTextureHandle != 0)
        {
            GL.DeleteTexture(fontBandEntriesTextureHandle);
            fontBandEntriesTextureHandle = 0;
        }
        
        if (renderingFactory.FontBandEntries.Length > 0)
        {
            fontBandEntriesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontBandEntriesTextureHandle);
            
            // each band entry takes exactly 1 texel
            var texWidth = FontTextureWidth;
            var texHeight = (renderingFactory.FontBandEntries.Length + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rg32i, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var bufSize = texWidth * texHeight * 2 * sizeof(int);
            var buf = new byte[bufSize];
            var fontBandEntriesSpan = MemoryMarshal.AsBytes(renderingFactory.FontBandEntries);
            fontBandEntriesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.RgInteger, PixelType.Int, buf);
        }
        
        // Upload shape entries buffer
        if (fontShapeEntriesTextureHandle != 0)
        {
            GL.DeleteTexture(fontShapeEntriesTextureHandle);
            fontShapeEntriesTextureHandle = 0;
        }

        if (renderingFactory.FontShapeEntries.Length > 0)
        {
            fontShapeEntriesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontShapeEntriesTextureHandle);

            // each shape entry takes exactly 4 texel
            var texWidth = FontTextureWidth;
            var texHeight = (renderingFactory.FontShapeEntries.Length * 4 + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba32ui, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var gpuShapeEntries = renderingFactory.FontShapeEntries.Select(x => new GpuShapeEntry
            {
                HorizontalBandEntryBaseIndex = x.HorizontalBandEntryBaseIndex,
                HorizontalBandEntryCount = x.HorizontalBandEntryCount,
                HorizontalBandScale = x.HorizontalBandScale,
                HorizontalBandOffset = x.HorizontalBandOffset,
                VerticalBandEntryBaseIndex = x.VerticalBandEntryBaseIndex,
                VerticalBandEntryCount = x.VerticalBandEntryCount,
                VerticalBandScale = x.VerticalBandScale,
                VerticalBandOffset = x.VerticalBandOffset,
                Min = x.Min,
                Max = x.Max,
                Color = x.Color
            }).ToArray();
            
            var bufSize = texWidth * texHeight * 4 * sizeof(uint);
            var buf = new byte[bufSize];
            var fontShapeEntriesSpan = MemoryMarshal.AsBytes(gpuShapeEntries);
            fontShapeEntriesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.RgbaInteger, PixelType.UnsignedInt, buf);
        }

        logger.LogInformation(
            "Font buffers updated, registered {Curves} curves, {CurveIndices} curve indices, {Bands} band entries, {Shapes} shape entries",
            renderingFactory.FontCurves.Length,
            renderingFactory.FontCurveIndices.Length,
            renderingFactory.FontBandEntries.Length,
            renderingFactory.FontShapeEntries.Length);
    }

    private void UpdateTextData()
    {
        if (textInstanceBufferHandle == 0)
        {
            textInstanceBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, textInstanceBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, glyphBufferAllocator.Capacity * Unsafe.SizeOf<RenderGlyph>(), IntPtr.Zero, BufferUsage.DynamicDraw);
        }
        
        while (textLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    var renderGlyphs = command.Data;
                    Debug.Assert(renderGlyphs != null);

                    var glyphAlloc = glyphBufferAllocator.Allocate(renderGlyphs.Length, (oldCapacity, newCapacity) =>
                    {
                        // Recreate glyph buffer with new capacity
                        var newBufferHandle = GL.GenBuffer();
                        GL.BindBuffer(BufferTarget.ArrayBuffer, newBufferHandle);
                        GL.BufferData(BufferTarget.ArrayBuffer, newCapacity * Unsafe.SizeOf<RenderGlyph>(), IntPtr.Zero, BufferUsage.DynamicDraw);
                        
                        GL.BindBuffer(BufferTarget.CopyReadBuffer, textInstanceBufferHandle);
                        GL.BindBuffer(BufferTarget.CopyWriteBuffer, newBufferHandle);
                        GL.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, IntPtr.Zero, IntPtr.Zero, oldCapacity * Unsafe.SizeOf<RenderGlyph>());
                        
                        GL.DeleteBuffer(textInstanceBufferHandle);
                        textInstanceBufferHandle = newBufferHandle;

                        logger.LogInformation("Glyph buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });

                    // Upload glyph data
                    GL.BindBuffer(BufferTarget.ArrayBuffer, textInstanceBufferHandle);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, glyphAlloc.Offset * Unsafe.SizeOf<RenderGlyph>(), renderGlyphs.Length * Unsafe.SizeOf<RenderGlyph>(), renderGlyphs);
                    
                    // Store text info
                    textInfos.EnsureCount(command.Id + 1);
                    textInfos[command.Id] = new TextInfo
                    {
                        Allocation = glyphAlloc,
                        GlyphCount = renderGlyphs.Length
                    };
                    break;
                }
                case LifecycleCommandType.Destroy:
                {
                    var textInfo = textInfos[command.Id];
                    glyphBufferAllocator.Free(textInfo.Allocation);
                    textInfos[command.Id] = default;
                    break;
                }
            }
        }
    }

    private void UpdateFboData(Vector2i size)
    {
        if (size == currentFboSize)
            return;
        
        if (size.X * size.Y == 0)
            return;
        
        // Delete old FBO data
        GL.DeleteRenderbuffer(fboColorBufferHandle);
        GL.DeleteRenderbuffer(fboDepthBufferHandle);
        
        // Create new FBO data
        fboColorBufferHandle = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.Rgba16f, size.X, size.Y);
        
        fboDepthBufferHandle = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.DepthComponent32f, size.X, size.Y);
        
        // Bind new data to FBO
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);        
        
        // Delete old post-process FBO data
        GL.DeleteTexture(postProcessTextureHandle1);
        GL.DeleteTexture(postProcessTextureHandle2);
        
        // The composite texture is made again when it is needed
        GL.DeleteTexture(compositeTextureHandle);
        compositeTextureHandle = 0;
        
        // Create new post-process FBO data
        postProcessTextureHandle1 = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle1);
        GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);
        
        postProcessTextureHandle2 = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle2);
        GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);
        
        currentFboSize = size;
        
        logger.LogInformation("OpenGL framebuffer size updated, now at {Width}x{Height}", currentFboSize.X, currentFboSize.Y);
    }
    
    private static void Swap<T>(ref T a, ref T b)
    {
        (a, b) = (b, a);
    }
}