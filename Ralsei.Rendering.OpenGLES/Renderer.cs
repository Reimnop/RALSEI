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
using Ralsei.Platform;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Ralsei.Rendering.OpenGLES.PostProcessing;
using Ralsei.Util;

namespace Ralsei.Rendering.OpenGLES;

internal class Renderer : IRenderer, IDisposable
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

    // Only so many textures can be bound at once; a draw has the slot its texture is in. Slot units start after the font's four.
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
    private readonly IFrameReceiver? frameReceiver;
    private FrameReadback? frameReadback;
    private int presentFramebufferHandle;

    private readonly int programHandle;

    // The part of the target the pass that is being drawn is in (see PassBuffer.Viewport)
    private ClipRect passViewport;

    private readonly int glyphProgramHandle;
    private readonly int glyphMvpUniformLocation, glyphZUniformLocation, glyphBaseColorUniformLocation;
    private readonly int glyphViewportSizeUniformLocation, glyphClipUniformLocation;

    private int fontCurvesTextureHandle;
    private int fontCurveIndicesTextureHandle;
    private int fontBandEntriesTextureHandle;
    private int fontShapeEntriesTextureHandle;

    private Vector2i currentFboSize;
    private int fboColorBufferHandle, fboDepthBufferHandle;
    private readonly int fboHandle;
    private int postProcessTextureHandle1, postProcessTextureHandle2;
    private readonly int postProcessFboHandle;

    // What multiple passes are layered on
    private int compositeTextureHandle;

    // What passes drew, kept for later passes that use them as textures (TextureHandle.FromPass), by pass index
    private readonly List<TextureInfo?> passTextures = [];
    private bool[] passOutputUsed = [];
    private int passCopyReadFramebufferHandle, passCopyDrawFramebufferHandle;

    private readonly int emptyVao;
    
    private readonly LegacyBloom legacyBloom;
    private readonly UniversalBloom universalBloom;
    private readonly Glitch glitch;
    private readonly Grain grain;
    private readonly UberPost uberPost;
    private readonly Blend blend;
    private readonly DepthOfField depthOfField;
    
    private readonly List<DrawCommandWithDepth> opaqueDrawCommands = [];
    private readonly List<DrawCommandWithDepth> transparentDrawCommands = [];

    private readonly int mainVertexArrayHandle;
    private int mainVertexBufferHandle, mainIndexBufferHandle;

    private readonly int textVertexArrayHandle;
    private int textInstanceBufferHandle;
    
    private const int InitialUnlitInstanceBufferCapacity = 1024;
    
    private GpuUnlitDrawItem[] unlitDrawItems = new GpuUnlitDrawItem[InitialUnlitInstanceBufferCapacity];
    private int unlitInstanceBuffer;
    private int unlitInstanceBufferCapacity = InitialUnlitInstanceBufferCapacity;
    
    private const int InitialVertexBufferCapacity = 1024;
    private const int InitialIndexBufferCapacity = 1024;

    private readonly PooledSuballocator vertexBufferAllocator = new(InitialVertexBufferCapacity);
    private readonly PooledSuballocator indexBufferAllocator = new(InitialIndexBufferCapacity);

    private readonly List<MeshInfo> meshInfos = [];

    private readonly List<TextureInfo?> textureInfos = [];
    private readonly int texturePageSize;
    private readonly TexturePager texturePager = new();
    
    private const int InitialGlyphBufferCapacity = 1024;
    
    private readonly PooledSuballocator glyphBufferAllocator = new(InitialGlyphBufferCapacity);
    private readonly List<TextInfo> textInfos = [];
    
    private readonly Queue<MeshLifecycleCommand> meshLifecycleCommands = [];
    private readonly Queue<TextLifecycleCommand> textLifecycleCommands = [];
    private readonly Queue<TextureLifecycleCommand> textureLifecycleCommands = [];

    private bool fontBuffersDirty = true;
    
    private readonly ResourceMirror resources;
    private bool disposed;
    private readonly ILogger<Renderer> logger;
    
    public Renderer(
        RenderingFactory renderingFactory,
        IOpenGLSurface surface,
        ResourceLoader loader,
        ILogger<Renderer> logger)
    {
        resources = renderingFactory.CreateMirror();
        this.surface = surface;
        frameReceiver = surface as IFrameReceiver;
        this.logger = logger;
        
        logger.LogInformation("Initializing OpenGL ES renderer");

        if (!surface.TryMakeCurrent())
            throw new InvalidOperationException("Failed to make OpenGL context current");
        
        logger.LogInformation("OpenGL ES: {Version}", GL.GetString(StringName.Version));
        logger.LogInformation("Renderer: {Renderer}", GL.GetString(StringName.Renderer));
        logger.LogInformation("Vendor: {Vendor}", GL.GetString(StringName.Vendor));
        logger.LogInformation("Shading language: {ShadingLanguage}", GL.GetString(StringName.ShadingLanguageVersion));
        
        #region OpenGL Data Initialization

        {
            var initialSize = this.surface.RenderSize;
            
            // How many textures a page can use together: what a fragment shader can sample, and what fits in the units after the font's
            texturePageSize = Math.Clamp(
                Math.Min(GL.GetInteger(GetPName.MaxTextureImageUnits), GL.GetInteger(GetPName.MaxCombinedTextureImageUnits) - FirstTextureSlotUnit),
                1, MaxTexturePageSize);
            if (int.TryParse(Environment.GetEnvironmentVariable("RALSEI_TEXTURE_PAGE_SIZE"), out var requestedPageSize) && requestedPageSize > 0)
                texturePageSize = Math.Min(texturePageSize, requestedPageSize); // to try what many pages do
            logger.LogInformation("Textures bound at once: {PageSize}", texturePageSize);
            var pageSizeDefine = new KeyValuePair<string, string?>("RALSEI_TEXTURE_PAGE_SIZE", texturePageSize.ToString(CultureInfo.InvariantCulture));

            programHandle = LoaderUtil.LoadShaderProgram(loader, "Unlit.vert", "Unlit.frag", pageSizeDefine);

            var texturesUniformLocation = GL.GetUniformLocation(programHandle, "uTextures");
            GL.UseProgram(programHandle);
            for (var i = 0; i < texturePageSize; i++)
                GL.Uniform1i(texturesUniformLocation + i, i + FirstTextureSlotUnit);

            glyphProgramHandle = LoaderUtil.LoadShaderProgram(loader, "Text.vert", "Text.frag");

            glyphMvpUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uMvp");
            glyphZUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uZ");
            glyphBaseColorUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uBaseColor");
            glyphViewportSizeUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uViewportSize");
            glyphClipUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uClip");

            var curvesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uCurves");
            var curveIndicesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uCurveIndices");
            var bandEntriesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uBandEntries");
            var shapeEntriesUniformLocation = GL.GetUniformLocation(glyphProgramHandle, "uShapeEntries");

            GL.UseProgram(glyphProgramHandle);
            GL.Uniform1i(curvesUniformLocation, 0);
            GL.Uniform1i(curveIndicesUniformLocation, 1);
            GL.Uniform1i(bandEntriesUniformLocation, 2);
            GL.Uniform1i(shapeEntriesUniformLocation, 3);
            
            unlitInstanceBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, unlitInstanceBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, InitialUnlitInstanceBufferCapacity * Unsafe.SizeOf<GpuUnlitDrawItem>(), IntPtr.Zero, BufferUsage.DynamicDraw);

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
            GL.EnableVertexAttribArray(10);
            
            GL.VertexAttribDivisor(2, 1);
            GL.VertexAttribDivisor(3, 1);
            GL.VertexAttribDivisor(4, 1);
            GL.VertexAttribDivisor(5, 1);
            GL.VertexAttribDivisor(6, 1);
            GL.VertexAttribDivisor(7, 1);
            GL.VertexAttribDivisor(8, 1);
            GL.VertexAttribDivisor(9, 1);
            GL.VertexAttribDivisor(10, 1);
            
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

            postProcessTextureHandle1 = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle1);
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, initialSize.X, initialSize.Y);

            postProcessTextureHandle2 = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, postProcessTextureHandle2);
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, initialSize.X, initialSize.Y);

            postProcessFboHandle = GL.GenFramebuffer();
            
            currentFboSize = initialSize;
            
            emptyVao = GL.GenVertexArray();

            var vertexShader = LoaderUtil.LoadPostProcessVertexShader(loader);

            legacyBloom = new LegacyBloom(loader, vertexShader);
            universalBloom = new UniversalBloom(loader, vertexShader);
            glitch = new Glitch(loader, vertexShader);
            grain = new Grain(loader, vertexShader);
            uberPost = new UberPost(loader, vertexShader);
            blend = new Blend(loader, vertexShader);
            depthOfField = new DepthOfField(loader, vertexShader);

            GL.DeleteShader(vertexShader);
        }

        #endregion
        
        // The mirror starts with everything that exists and hears of the rest as frames are drawn
        resources.FontBuffersUpdated += OnFontBuffersUpdated;
        resources.Meshes.ItemInserted += OnMeshInserted;
        resources.Meshes.ItemRemoved += OnMeshRemoved;
        resources.Textures.ItemInserted += OnTextureInserted;
        resources.Textures.ItemRemoved += OnTextureRemoved;
        resources.Texts.ItemInserted += OnTextInserted;
        resources.Texts.ItemRemoved += OnTextRemoved;
    }
    
    public void Dispose()
    {
        // Disposed once whoever asks (the scope that made it, the host); a second pass would delete what is gone
        if (disposed)
            return;
        disposed = true;

        logger.LogInformation("Disposing OpenGL ES renderer");
        
        resources.FontBuffersUpdated -= OnFontBuffersUpdated;
        resources.Meshes.ItemInserted -= OnMeshInserted;
        resources.Meshes.ItemRemoved -= OnMeshRemoved;
        resources.Textures.ItemInserted -= OnTextureInserted;
        resources.Textures.ItemRemoved -= OnTextureRemoved;
        resources.Texts.ItemInserted -= OnTextInserted;
        resources.Texts.ItemRemoved -= OnTextRemoved;
        resources.Dispose();
        
        // Context already lost, no need to continue disposing
        if (surface.IsContextLost || !surface.TryMakeCurrent())
            return;
        
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
        
        frameReadback?.Dispose();
        GL.DeleteFramebuffer(presentFramebufferHandle);
        GL.DeleteVertexArray(emptyVao);
        
        legacyBloom.Dispose();
        universalBloom.Dispose();
        glitch.Dispose();
        grain.Dispose();
        uberPost.Dispose();
        blend.Dispose();
        depthOfField.Dispose();
        
        GL.DeleteTexture(compositeTextureHandle);
        DeletePassTextures();
        GL.DeleteFramebuffer(passCopyReadFramebufferHandle);
        GL.DeleteFramebuffer(passCopyDrawFramebufferHandle);
    }

    private void DeletePassTextures()
    {
        foreach (var passTexture in passTextures)
        {
            if (passTexture.HasValue)
                GL.DeleteTexture(passTexture.Value.Handle);
        }

        passTextures.Clear();
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
        if (!surface.TryMakeCurrent())
            return;

        resources.Apply(drawDataProvider.ResourceVersion);

        var renderSize = surface.RenderSize;

        UpdateOpenGlData(renderSize);

        // Passes are layered on a target of their own, unless there is only one
        var passCount = drawDataProvider.PassCount;

        // Passes that later draws use
        if (passOutputUsed.Length < passCount)
            passOutputUsed = new bool[Math.Max(passCount, passOutputUsed.Length * 2)];
        Array.Clear(passOutputUsed);
        RenderUtil.ValidatePassTextures(drawDataProvider, passOutputUsed);
        var layered = passCount > 1;
        if (layered)
        {
            if (compositeTextureHandle == 0)
            {
                compositeTextureHandle = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2d, compositeTextureHandle);
                GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, currentFboSize.X, currentFboSize.Y);
            }

            blend.Clear(currentFboSize, compositeTextureHandle);
        }

        var finalTexture = postProcessTextureHandle1;
        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            var passData = drawDataProvider.CreateDrawData(passIndex);
            var region = PassRegion.Of(passData, layered, passOutputUsed[passIndex], currentFboSize);
            var passTexture = RenderPass(passData, renderSize, layered, region);
            if (passOutputUsed[passIndex])
                KeepPassOutput(passIndex, passTexture);

            if (layered)
                blend.Process(currentFboSize, passTexture, compositeTextureHandle, passData.Opacity, region);
            else
                finalTexture = passTexture;
        }

        if (layered)
            finalTexture = compositeTextureHandle;

        if (drawDataProvider.FinalPostProcessingState is { } finalState)
        {
            var spareTexture = finalTexture == postProcessTextureHandle1 ? postProcessTextureHandle2 : postProcessTextureHandle1;
            finalTexture = HandlePostProcessing(finalState, finalTexture, spareTexture);
        }

        if (frameReceiver != null)
        {
            frameReadback ??= new FrameReadback();
            frameReceiver.ReceiveFrame(frameReadback.Read(finalTexture, renderSize), renderSize);
        }

        Present(finalTexture, renderSize);
    }

    /// <summary>Copies what a pass drew to a texture of its own for later passes, turned over so the first row is the top.</summary>
    private void KeepPassOutput(int passIndex, int sourceTexture)
    {
        passTextures.EnsureCount(passIndex + 1);
        if (passTextures[passIndex] is not { } info)
        {
            var handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, handle);
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba16f, currentFboSize.X, currentFboSize.Y);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int) All.Linear);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int) All.Linear);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int) All.ClampToEdge);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int) All.ClampToEdge);

            passTextures[passIndex] = info = new TextureInfo { Handle = handle };
        }

        if (passCopyReadFramebufferHandle == 0)
        {
            passCopyReadFramebufferHandle = GL.GenFramebuffer();
            passCopyDrawFramebufferHandle = GL.GenFramebuffer();
        }

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, passCopyReadFramebufferHandle);
        GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, sourceTexture, 0);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, passCopyDrawFramebufferHandle);
        GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, info.Handle, 0);

        // (A clip of the pass would cut the copy)
        GL.Disable(EnableCap.ScissorTest);
        GL.BlitFramebuffer(
            0, 0, currentFboSize.X, currentFboSize.Y,
            0, currentFboSize.Y, currentFboSize.X, 0,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
    }

    /// <summary>Puts the frame in the surface's framebuffer, centered, and has the surface show it.</summary>
    private void Present(int texture, Vector2i size)
    {
        if (presentFramebufferHandle == 0)
            presentFramebufferHandle = GL.GenFramebuffer();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, presentFramebufferHandle);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, texture, 0);

        var framebufferSize = surface.FramebufferSize;
        var offset = (framebufferSize - size) / 2;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, presentFramebufferHandle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, surface.Framebuffer);

        // (The clip of the last pass would clip this as well)
        GL.Disable(EnableCap.ScissorTest);

        if (framebufferSize != size)
        {
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Viewport(0, 0, framebufferSize.X, framebufferSize.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            offset.X, offset.Y, offset.X + size.X, offset.Y + size.Y,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        surface.SwapBuffers();
    }

    /// <summary>Draws a pass and post-processes it. Returns the texture with the result (one of the post-processing textures).</summary>
    /// <param name="region">The part of the target that the pass works on, if only a part (see <see cref="PassRegion"/>); the rest is left as it is.</param>
    private int RenderPass(in DrawData drawData, Vector2i renderSize, bool layered, ClipRect region)
    {
        var camera = drawData.ViewMatrix;

        // A pass that is drawn in a part of the target is drawn as if the target were that part
        var viewport = drawData.Viewport;
        passViewport = viewport;

        opaqueDrawCommands.Clear();
        transparentDrawCommands.Clear();

        var currentDepthInt = 0;
        foreach (var drawCommand in drawData.DrawCommands)
        {
            if (RenderUtil.ShouldUseTransparentDrawList(drawCommand, drawData))
            {
                transparentDrawCommands.Add(new DrawCommandWithDepth
                {
                    DrawType = drawCommand.DrawType,
                    DrawId = drawCommand.DrawId,
                    Depth = currentDepthInt / (float)(1 << 23),
                    Clip = drawCommand.Clip
                });
            }
            else
            {
                opaqueDrawCommands.Add(new DrawCommandWithDepth
                {
                    DrawType = drawCommand.DrawType,
                    DrawId = drawCommand.DrawId,
                    Depth = currentDepthInt / (float)(1 << 23),
                    Clip = drawCommand.Clip
                });
            }

            currentDepthInt++;
        }

        // Opaque draws are reversed to avoid overdraw
        opaqueDrawCommands.Reverse();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);

        GL.BindSampler(0, 0);
        GL.BindSampler(1, 0);
        GL.BindSampler(2, 0);
        GL.BindSampler(3, 0);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, fontCurvesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2d, fontCurveIndicesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2d, fontBandEntriesTextureHandle);

        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2d, fontShapeEntriesTextureHandle);

        // Clear the screen. The target holds premultiplied colors, and so does what it is cleared to
        var clearColor = drawData.ClearColor;
        GL.ClearDepthf(0.0f);
        GL.Disable(EnableCap.ScissorTest);
        if (region.Enabled)
        {
            // Nothing reads what is outside the part, so only the part is cleared
            GL.Viewport(viewport.X, currentFboSize.Y - viewport.Bottom, viewport.Width, viewport.Height);
            ApplyClip(default);
            GL.ClearColor(clearColor.R * clearColor.A, clearColor.G * clearColor.A, clearColor.B * clearColor.A, clearColor.A);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Disable(EnableCap.ScissorTest);
        }
        else if (viewport.Enabled)
        {
            GL.Viewport(viewport.X, currentFboSize.Y - viewport.Bottom, viewport.Width, viewport.Height);

            // What is outside the part is nothing, and the part is what it is cleared to
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            ApplyClip(default);
            GL.ClearColor(clearColor.R * clearColor.A, clearColor.G * clearColor.A, clearColor.B * clearColor.A, clearColor.A);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Disable(EnableCap.ScissorTest);
        }
        else
        {
            GL.Viewport(0, 0, renderSize.X, renderSize.Y);
            GL.ClearColor(clearColor.R * clearColor.A, clearColor.G * clearColor.A, clearColor.B * clearColor.A, clearColor.A);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        GL.DepthFunc(DepthFunction.Greater);

        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);

        RenderDrawDataList(
            CollectionsMarshal.AsSpan(opaqueDrawCommands),
            drawData,
            camera);

        // Transparent draws blend; the shaders output premultiplied alpha
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);

        RenderDrawDataList(
            CollectionsMarshal.AsSpan(transparentDrawCommands),
            drawData,
            camera);

        GL.DepthMask(true);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, postProcessFboHandle);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, postProcessTextureHandle1, 0);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboHandle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, postProcessFboHandle);
        var (blitX, blitY, blitWidth, blitHeight) = region.Enabled ? PassRegion.ToImageRect(region, currentFboSize) : (0, 0, renderSize.X, renderSize.Y);
        GL.BlitFramebuffer(
            blitX, blitY,
            blitX + blitWidth, blitY + blitHeight,
            blitX, blitY,
            blitX + blitWidth, blitY + blitHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // (Passes of a layered frame with no effects are left as they are)
        return HandlePostProcessing(drawData.PostProcessingState, postProcessTextureHandle1, postProcessTextureHandle2, skipIfNoFinalEffects: layered);
    }

    private int HandlePostProcessing(PostProcessingState state, int texture1, int texture2, bool skipIfNoFinalEffects = false)
    {
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

        // Draws that can be drawn with the same bound textures make a page
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
                        var (clipMin, clipMax) = ClipBounds.Pack(drawCommand.Clip, passViewport, currentFboSize);
                        GL.Uniform2ui(glyphClipUniformLocation, clipMin, clipMax);
                        GL.Uniform2f(glyphViewportSizeUniformLocation, passViewport.Enabled ? passViewport.Width : currentFboSize.X, passViewport.Enabled ? passViewport.Height : currentFboSize.Y);

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

        GL.Disable(EnableCap.ScissorTest);
    }

    /// <summary>Cuts what is drawn next to <paramref name="clip"/>, or stops cutting if there is none.</summary>
    private void ApplyClip(ClipRect clip)
    {
        // The part of the target the pass is in: its clip is in pixels of that part, and nothing shows outside it
        var viewport = passViewport;
        if (!clip.Enabled)
        {
            if (!viewport.Enabled)
            {
                GL.Disable(EnableCap.ScissorTest);
                return;
            }

            clip = new ClipRect(0, 0, viewport.Width, viewport.Height);
        }

        var originX = viewport.Enabled ? viewport.X : 0;
        var originY = viewport.Enabled ? viewport.Y : 0;
        var width = viewport.Enabled ? viewport.Width : currentFboSize.X;
        var height = viewport.Enabled ? viewport.Height : currentFboSize.Y;
        var left = Math.Clamp(clip.X, 0, width);
        var top = Math.Clamp(clip.Y, 0, height);
        var right = Math.Clamp(clip.Right, left, width);
        var bottom = Math.Clamp(clip.Bottom, top, height);

        // The origin of the scissor box is the bottom-left corner
        GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(originX + left, currentFboSize.Y - (originY + bottom), right - left, bottom - top);
    }

    /// <summary>Binds the textures of a page to their slots' units.</summary>
    private void BindTexturePage(in TexturePage page)
    {
        var textures = texturePager.TexturesOf(page);
        for (var slot = 0; slot < textures.Length; slot++)
        {
            GL.ActiveTexture((TextureUnit) ((int) TextureUnit.Texture0 + FirstTextureSlotUnit + slot));
            var info = FindTexture(textures[slot]);
            GL.BindTexture(TextureTarget.Texture2d, info?.Handle ?? 0);
        }
    }

    /// <summary>Puts the draw item of every mesh in the instance buffer, in draw order.</summary>
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

            var textured = TexturedInfoOf(meshDrawItem.RenderMode == RenderMode.Normal ? meshDrawItem.Texture : TextureHandle.None, out var crispEdges);
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
            (unlitDrawItem.ClipMin, unlitDrawItem.ClipMax) = ClipBounds.Pack(drawCommand.Clip, passViewport, currentFboSize);
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

    /// <summary>The texture with an id: a texture of the factory or a pass output.</summary>
    private TextureInfo? FindTexture(int id)
    {
        if (TextureHandle.TryGetPassIndex(id, out var passIndex))
            return passIndex < passTextures.Count ? passTextures[passIndex] : null;

        return id < textureInfos.Count ? textureInfos[id] : null;
    }

    /// <summary>Whether the mesh draw samples a texture the renderer has, and whether with crisp edges.</summary>
    private bool TexturedInfoOf(TextureHandle texture, out bool crispEdges)
    {
        if (texture.IsNone || FindTexture(texture.Id) is not { } info)
        {
            crispEdges = false;
            return false;
        }

        crispEdges = info.CrispEdges;
        return true;
    }

    /// <summary>Draws instances of one mesh from the draw items in the instance buffer starting at <paramref name="firstInstance"/>.</summary>
    private void RenderMeshBatch(int firstInstance, int instanceCount, in MeshInfo meshInfo)
    {
        GL.UseProgram(programHandle);
        GL.BindVertexArray(mainVertexArrayHandle);

        PointInstanceAttributes(firstInstance);

        GL.DrawElementsInstanced(
            PrimitiveType.Triangles,
            meshInfo.IndexCount,
            DrawElementsType.UnsignedInt,
            meshInfo.IndexBufferAllocation.Offset * sizeof(uint),
            instanceCount);
    }

    /// <summary>Binds the main vertex array and points its instance attributes at the items in the instance buffer from <paramref name="firstInstance"/>.</summary>
    private void PointInstanceAttributes(int firstInstance)
    {
        GL.BindVertexArray(mainVertexArrayHandle);

        // The pointers take the buffer bound now
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
        GL.VertexAttribIPointer(10, 2, VertexAttribIType.UnsignedInt, stride, baseOffset + 112);       // ClipMin and ClipMax
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
        if (mainVertexBufferHandle == 0)
        {
            mainVertexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, mainVertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexBufferAllocator.Capacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, BufferUsage.DynamicDraw);
            
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
                        var newBufferHandle = GL.GenBuffer();
                        GL.BindBuffer(BufferTarget.ArrayBuffer, newBufferHandle);
                        GL.BufferData(BufferTarget.ArrayBuffer, newCapacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, BufferUsage.DynamicDraw);
                        
                        GL.BindBuffer(BufferTarget.CopyReadBuffer, mainVertexBufferHandle);
                        GL.BindBuffer(BufferTarget.CopyWriteBuffer, newBufferHandle);
                        GL.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, IntPtr.Zero, IntPtr.Zero, oldCapacity * Unsafe.SizeOf<Vertex>());
                        GL.DeleteBuffer(mainVertexBufferHandle);
                        mainVertexBufferHandle = newBufferHandle;
                        
                        GL.BindVertexArray(mainVertexArrayHandle);
                        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0);
                        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 8);
                        
                        logger.LogInformation("Mesh vertex buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });
                    
                    var idxAlloc = indexBufferAllocator.Allocate(indexCount, (oldCapacity, newCapacity) =>
                    {
                        // Make sure we don't randomly bind another VAO to this index buffer
                        GL.BindVertexArray(mainVertexArrayHandle);
                        
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
                    
                    for (var i = 0; i < indices.Length; i++)
                        indices[i] += vtxAlloc.Offset;
                    
                    GL.BindBuffer(BufferTarget.ArrayBuffer, mainVertexBufferHandle);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, vtxAlloc.Offset * Unsafe.SizeOf<Vertex>(), vertexCount * Unsafe.SizeOf<Vertex>(), vertices);
                    
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
        
        if (fontCurvesTextureHandle != 0)
        {
            GL.DeleteTexture(fontCurvesTextureHandle);
            fontCurvesTextureHandle = 0;
        }
        
        if (resources.FontCurves.Length > 0)
        {
            fontCurvesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontCurvesTextureHandle);
            
            // each curve takes exactly 3 texels
            var texWidth = FontTextureWidth;
            var texHeight = (resources.FontCurves.Length * 3 + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rg32f, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            var bufSize = texWidth * texHeight * 2 * sizeof(float);
            var buf = new byte[bufSize];
            var fontCurvesSpan = MemoryMarshal.AsBytes(resources.FontCurves);
            fontCurvesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.Rg, PixelType.Float, buf); 
        }
        
        if (fontCurveIndicesTextureHandle != 0)
        {
            GL.DeleteTexture(fontCurveIndicesTextureHandle);
            fontCurveIndicesTextureHandle = 0;
        }

        if (resources.FontCurveIndices.Length > 0)
        {
            fontCurveIndicesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontCurveIndicesTextureHandle);

            // each curve index takes exactly 1 texel
            var texWidth = FontTextureWidth;
            var texHeight = (resources.FontCurveIndices.Length + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.R32i, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var bufSize = texWidth * texHeight * sizeof(int);
            var buf = new byte[bufSize];
            var fontCurveIndicesSpan = MemoryMarshal.AsBytes(resources.FontCurveIndices);
            fontCurveIndicesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.RedInteger, PixelType.Int, buf);
        }
        
        if (fontBandEntriesTextureHandle != 0)
        {
            GL.DeleteTexture(fontBandEntriesTextureHandle);
            fontBandEntriesTextureHandle = 0;
        }
        
        if (resources.FontBandEntries.Length > 0)
        {
            fontBandEntriesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontBandEntriesTextureHandle);
            
            // each band entry takes exactly 1 texel
            var texWidth = FontTextureWidth;
            var texHeight = (resources.FontBandEntries.Length + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rg32i, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var bufSize = texWidth * texHeight * 2 * sizeof(int);
            var buf = new byte[bufSize];
            var fontBandEntriesSpan = MemoryMarshal.AsBytes(resources.FontBandEntries);
            fontBandEntriesSpan.CopyTo(buf);
            
            GL.TexSubImage2D(TextureTarget.Texture2d, 0, 0, 0, texWidth, texHeight, PixelFormat.RgInteger, PixelType.Int, buf);
        }
        
        if (fontShapeEntriesTextureHandle != 0)
        {
            GL.DeleteTexture(fontShapeEntriesTextureHandle);
            fontShapeEntriesTextureHandle = 0;
        }

        if (resources.FontShapeEntries.Length > 0)
        {
            fontShapeEntriesTextureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2d, fontShapeEntriesTextureHandle);

            // each shape entry takes exactly 4 texel
            var texWidth = FontTextureWidth;
            var texHeight = (resources.FontShapeEntries.Length * 4 + FontTextureWidth - 1) / FontTextureWidth; // Round up division
            GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgba32ui, texWidth, texHeight);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            
            var gpuShapeEntries = resources.FontShapeEntries.Select(x => new GpuShapeEntry
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
            resources.FontCurves.Length,
            resources.FontCurveIndices.Length,
            resources.FontBandEntries.Length,
            resources.FontShapeEntries.Length);
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

                    GL.BindBuffer(BufferTarget.ArrayBuffer, textInstanceBufferHandle);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, glyphAlloc.Offset * Unsafe.SizeOf<RenderGlyph>(), renderGlyphs.Length * Unsafe.SizeOf<RenderGlyph>(), renderGlyphs);
                    
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
        
        GL.DeleteRenderbuffer(fboColorBufferHandle);
        GL.DeleteRenderbuffer(fboDepthBufferHandle);
        
        fboColorBufferHandle = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.Rgba16f, size.X, size.Y);
        
        fboDepthBufferHandle = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, MsaaSamples, InternalFormat.DepthComponent32f, size.X, size.Y);
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);        
        
        GL.DeleteTexture(postProcessTextureHandle1);
        GL.DeleteTexture(postProcessTextureHandle2);
        
        // The composite texture and the textures of the passes are made again when they are needed
        GL.DeleteTexture(compositeTextureHandle);
        compositeTextureHandle = 0;
        DeletePassTextures();
        
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