using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
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
using Ralsei.Rendering.OpenGL.PostProcessing;
using Ralsei.Util;
using BandEntry = Tmpx.Common.BandEntry;
using QuadraticCurve = Tmpx.Common.QuadraticCurve;

namespace Ralsei.Rendering.OpenGL;

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

        /// <summary>The handle shaders read the texture by when bindless; zero otherwise.</summary>
        public ulong BindlessHandle;

        public bool CrispEdges;
    }

    /// <summary>What a draw item says of its texture: the handle (two ints) or the page slot, and the flags.</summary>
    private readonly record struct TextureFields(int Texture0, int Texture1, int Flags);

    /// <summary>The indirect commands of one texture page, drawn together.</summary>
    private readonly record struct PageDraw(int PageIndex, int FirstCommand, int CommandCount);
    private const int MsaaSamples = 4;
    private const int MaxOverlays = 10;

    // Textures: bindless, shaders read any texture by handle. Otherwise only so many can be bound at once, and draws are divided into pages by texture; bound to the first texture units.
    private const int MaxTexturePageSize = 16;

    // SSBO binding points (must match shader layout(binding=N))
    private const int BindMultiDraw = 0;
    private const int BindMesh = 1;
    private const int BindText = 2;
    private const int BindGlyphs = 3;
    private const int BindCurves = 4;
    private const int BindCurveIndices = 5;
    private const int BindBandEntries = 6;
    private const int BindShapeEntries = 7;
    
    private const int InitialVertexBufferCapacity = 1024;
    private const int InitialIndexBufferCapacity = 1024;

    private readonly PooledSuballocator vertexBufferAllocator = new(InitialVertexBufferCapacity);
    private readonly PooledSuballocator indexBufferAllocator = new(InitialIndexBufferCapacity);

    private readonly List<MeshInfo> meshInfos = [];

    private readonly List<TextureInfo?> textureInfos = [];
    private readonly bool bindlessTextures;
    private readonly int texturePageSize;
    private readonly TexturePager texturePager = new();
    private readonly List<PageDraw> pageDraws = [];

    private const int InitialGlyphBufferCapacity = 1024;
    
    private readonly PooledSuballocator glyphBufferAllocator = new(InitialGlyphBufferCapacity);
    private readonly List<TextInfo> textInfos = [];
    
    private readonly LegacyBloom legacyBloom;
    private readonly UniversalBloom universalBloom;
    private readonly Glitch glitch;
    private readonly Grain grain;
    private readonly UberPost uberPost;
    private readonly Blend blend;
    private readonly DepthOfField depthOfField;
    
    private readonly int vertexArrayHandle;
    private int vertexBufferHandle, indexBufferHandle;
    private int glyphStorageBufferHandle;
    
    private readonly int multiDrawIndirectBufferHandle;
    private int multiDrawIndirectBufferSize;
    
    private readonly int multiDrawItemsBufferHandle;
    private int multiDrawItemsBufferSize;

    private readonly int multiDrawMeshItemsBufferHandle;
    private int multiDrawMeshItemsBufferSize;
    
    private readonly int multiDrawTextItemsBufferHandle;
    private int multiDrawTextItemsBufferSize;

    
    private readonly int programHandle;

    private readonly int curveSsboHandle;
    private readonly int curveIndexSsboHandle;
    private readonly int bandEntrySsboHandle;
    private readonly int shapeEntrySsboHandle;
    private readonly int viewportSizeUniformLocation, drawBaseUniformLocation;
    
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

    private readonly List<DrawCommandWithDepth> opaqueDrawCommands = [];
    private readonly List<DrawCommandWithDepth> transparentDrawCommands = [];
    
    private readonly Buffer<DrawElementsIndirectCommand> multiDrawIndirectBuffer = new();
    private readonly Buffer<GpuMultiDrawItem> multiDrawItemsBuffer = new();
    private readonly Buffer<GpuMeshDrawItem> multiDrawMeshItemsBuffer = new();
    private readonly Buffer<GpuTextDrawItem> multiDrawTextItemsBuffer = new();

    // The part of the target the current pass draws in (PassBuffer.Viewport)
    private ClipRect passViewport;
    
    private readonly Queue<MeshLifecycleCommand> meshLifecycleCommands = [];
    private readonly Queue<TextLifecycleCommand> textLifecycleCommands = [];
    private readonly Queue<TextureLifecycleCommand> textureLifecycleCommands = [];
    
    private bool fontBuffersDirty = true;

    private readonly ResourceMirror resources;
    private bool disposed;
    private readonly BuiltinRenderingPrimitive primitive;
    private readonly IOpenGLSurface surface;
    private readonly IFrameReceiver? frameReceiver;
    private FrameReadback? frameReadback;
    private int presentFramebufferHandle;
    private readonly ILogger<Renderer> logger;

    public Renderer(
        RenderingFactory renderingFactory,
        BuiltinRenderingPrimitive primitive,
        IOpenGLSurface surface,
        ResourceLoader loader,
        ILogger<Renderer> logger)
    {
        resources = renderingFactory.CreateMirror();
        this.primitive = primitive;
        this.surface = surface;
        frameReceiver = surface as IFrameReceiver;
        this.logger = logger;
        
        logger.LogInformation("Initializing OpenGL renderer");

        if (!surface.TryMakeCurrent())
            throw new InvalidOperationException("Failed to make OpenGL context current");
        
        GL.Enable(EnableCap.Multisample);
        
        logger.LogInformation("OpenGL: {Version}", GL.GetString(StringName.Version));
        logger.LogInformation("Renderer: {Renderer}", GL.GetString(StringName.Renderer));
        logger.LogInformation("Vendor: {Vendor}", GL.GetString(StringName.Vendor));
        logger.LogInformation("Shading language: {ShadingLanguage}", GL.GetString(StringName.ShadingLanguageVersion));
        
        #region OpenGL Data Initialization

        {
            var size = surface.RenderSize;
            
            vertexArrayHandle = GL.CreateVertexArray();
            
            vertexBufferHandle = GL.CreateBuffer();
            GL.NamedBufferData(vertexBufferHandle, InitialVertexBufferCapacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);
            
            indexBufferHandle = GL.CreateBuffer();
            GL.NamedBufferData(indexBufferHandle, InitialIndexBufferCapacity * sizeof(int), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);
            
            GL.VertexArrayVertexBuffer(vertexArrayHandle, 0, vertexBufferHandle, IntPtr.Zero, Unsafe.SizeOf<Vertex>());
            GL.VertexArrayElementBuffer(vertexArrayHandle, indexBufferHandle);
            
            GL.VertexArrayAttribFormat(vertexArrayHandle, 0, 2, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vertexArrayHandle, 0, 0);
            
            GL.VertexArrayAttribFormat(vertexArrayHandle, 1, 2, VertexAttribType.Float, false, 8);
            GL.VertexArrayAttribBinding(vertexArrayHandle, 1, 0);
            
            GL.EnableVertexArrayAttrib(vertexArrayHandle, 0);
            GL.EnableVertexArrayAttrib(vertexArrayHandle, 1);
            
            glyphStorageBufferHandle = GL.CreateBuffer();
            GL.NamedBufferData(glyphStorageBufferHandle, InitialGlyphBufferCapacity * Unsafe.SizeOf<GpuRenderGlyph>(), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);

            multiDrawIndirectBufferHandle = GL.CreateBuffer();
            multiDrawItemsBufferHandle = GL.CreateBuffer();
            multiDrawMeshItemsBufferHandle = GL.CreateBuffer();
            multiDrawTextItemsBufferHandle = GL.CreateBuffer();

            // The shaders read textures one of two ways, told with a define
            bindlessTextures = SupportsExtension("GL_ARB_bindless_texture") &&
                               Environment.GetEnvironmentVariable("RALSEI_NO_BINDLESS") is null;
            if (bindlessTextures)
            {
                logger.LogInformation("Textures are bindless");
                programHandle = LoaderUtil.LoadShaderProgram(loader, "Uber.vert", "Uber.frag",
                    new KeyValuePair<string, string?>("RALSEI_BINDLESS", null));
            }
            else
            {
                texturePageSize = Math.Clamp(GL.GetInteger(GetPName.MaxTextureImageUnits), 1, MaxTexturePageSize);
                if (int.TryParse(Environment.GetEnvironmentVariable("RALSEI_TEXTURE_PAGE_SIZE"), out var requestedPageSize) && requestedPageSize > 0)
                    texturePageSize = Math.Min(texturePageSize, requestedPageSize); // to try what many pages do
                logger.LogInformation("Textures are bound {PageSize} at a time", texturePageSize);
                programHandle = LoaderUtil.LoadShaderProgram(loader, "Uber.vert", "Uber.frag",
                    new KeyValuePair<string, string?>("RALSEI_TEXTURE_PAGE_SIZE", texturePageSize.ToString(CultureInfo.InvariantCulture)));

                var texturesUniformLocation = GL.GetUniformLocation(programHandle, "uTextures");
                GL.UseProgram(programHandle);
                for (var i = 0; i < texturePageSize; i++)
                    GL.Uniform1i(texturesUniformLocation + i, i);
            }

            viewportSizeUniformLocation = GL.GetUniformLocation(programHandle, "uViewportSize");
            drawBaseUniformLocation = GL.GetUniformLocation(programHandle, "uDrawBase");

            // Filled lazily by UpdateFontData
            curveSsboHandle = GL.CreateBuffer();
            curveIndexSsboHandle = GL.CreateBuffer();
            bandEntrySsboHandle = GL.CreateBuffer();
            shapeEntrySsboHandle = GL.CreateBuffer();

            fboColorBufferHandle = GL.CreateRenderbuffer();
            GL.NamedRenderbufferStorageMultisample(fboColorBufferHandle, MsaaSamples, InternalFormat.Rgba16f, size.X, size.Y);

            fboDepthBufferHandle = GL.CreateRenderbuffer();
            GL.NamedRenderbufferStorageMultisample(fboDepthBufferHandle, MsaaSamples, InternalFormat.DepthComponent32f, size.X, size.Y);

            fboHandle = GL.CreateFramebuffer();
            GL.NamedFramebufferRenderbuffer(fboHandle, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
            GL.NamedFramebufferRenderbuffer(fboHandle, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);

            postProcessTextureHandle1 = GL.CreateTexture(TextureTarget.Texture2d);
            GL.TextureStorage2D(postProcessTextureHandle1, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);

            postProcessTextureHandle2 = GL.CreateTexture(TextureTarget.Texture2d);
            GL.TextureStorage2D(postProcessTextureHandle2, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);

            postProcessFboHandle = GL.CreateFramebuffer();

            currentFboSize = size;
            
            legacyBloom = new LegacyBloom(loader);
            universalBloom = new UniversalBloom(loader);
            glitch = new Glitch(loader);
            grain = new Grain(loader);
            uberPost = new UberPost(loader);
            blend = new Blend(loader);
            depthOfField = new DepthOfField(loader);
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

        logger.LogInformation("Disposing OpenGL renderer");
        
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
        
        GL.DeleteFramebuffer(fboHandle);
        GL.DeleteRenderbuffer(fboColorBufferHandle);
        GL.DeleteRenderbuffer(fboDepthBufferHandle);
        GL.DeleteFramebuffer(postProcessFboHandle);
        GL.DeleteTexture(postProcessTextureHandle1);
        GL.DeleteTexture(postProcessTextureHandle2);
        
        GL.DeleteBuffer(vertexBufferHandle);
        GL.DeleteBuffer(indexBufferHandle);
        GL.DeleteVertexArray(vertexArrayHandle);
        GL.DeleteBuffer(glyphStorageBufferHandle);
        
        GL.DeleteBuffer(multiDrawIndirectBufferHandle);
        GL.DeleteBuffer(multiDrawItemsBufferHandle);
        GL.DeleteBuffer(multiDrawMeshItemsBufferHandle);
        GL.DeleteBuffer(multiDrawTextItemsBufferHandle);
        
        GL.DeleteProgram(programHandle);

        foreach (var textureInfo in textureInfos.Where(x => x.HasValue).Select(x => x!.Value))
            DeleteTexture(textureInfo);

        GL.DeleteBuffer(curveSsboHandle);
        GL.DeleteBuffer(curveIndexSsboHandle);
        GL.DeleteBuffer(bandEntrySsboHandle);
        GL.DeleteBuffer(shapeEntrySsboHandle);
        
        legacyBloom.Dispose();
        universalBloom.Dispose();
        glitch.Dispose();
        grain.Dispose();
        uberPost.Dispose();
        blend.Dispose();
        depthOfField.Dispose();
        frameReadback?.Dispose();
        GL.DeleteFramebuffer(presentFramebufferHandle);
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
                DeleteTexture(passTexture.Value);
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
                compositeTextureHandle = GL.CreateTexture(TextureTarget.Texture2d);
                GL.TextureStorage2D(compositeTextureHandle, 1, SizedInternalFormat.Rgba16f, currentFboSize.X, currentFboSize.Y);
            }

            GL.ClearTexImage(compositeTextureHandle, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        }

        var finalTexture = postProcessTextureHandle1;
        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            var passData = drawDataProvider.CreateDrawData(passIndex);
            var passTexture = RenderPass(passData, renderSize, layered);
            if (passOutputUsed[passIndex])
                KeepPassOutput(passIndex, passTexture);

            if (layered)
                blend.Process(currentFboSize, passTexture, compositeTextureHandle, passData.Opacity);
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
            var handle = GL.CreateTexture(TextureTarget.Texture2d);
            GL.TextureStorage2D(handle, 1, SizedInternalFormat.Rgba16f, currentFboSize.X, currentFboSize.Y);
            GL.TextureParameteri(handle, TextureParameterName.TextureMinFilter, (int) All.Linear);
            GL.TextureParameteri(handle, TextureParameterName.TextureMagFilter, (int) All.Linear);
            GL.TextureParameteri(handle, TextureParameterName.TextureWrapS, (int) All.ClampToEdge);
            GL.TextureParameteri(handle, TextureParameterName.TextureWrapT, (int) All.ClampToEdge);

            var bindlessHandle = 0UL;
            if (bindlessTextures)
            {
                bindlessHandle = GL.ARB.GetTextureHandleARB(handle);
                GL.ARB.MakeTextureHandleResidentARB(bindlessHandle);
            }

            passTextures[passIndex] = info = new TextureInfo { Handle = handle, BindlessHandle = bindlessHandle };
        }

        if (passCopyReadFramebufferHandle == 0)
        {
            passCopyReadFramebufferHandle = GL.CreateFramebuffer();
            passCopyDrawFramebufferHandle = GL.CreateFramebuffer();
        }

        GL.NamedFramebufferTexture(passCopyReadFramebufferHandle, FramebufferAttachment.ColorAttachment0, sourceTexture, 0);
        GL.NamedFramebufferTexture(passCopyDrawFramebufferHandle, FramebufferAttachment.ColorAttachment0, info.Handle, 0);

        // (A clip of the pass would cut the copy)
        GL.Disable(EnableCap.ScissorTest);
        GL.BlitNamedFramebuffer(
            passCopyReadFramebufferHandle, passCopyDrawFramebufferHandle,
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
    private int RenderPass(in DrawData drawData, Vector2i renderSize, bool layered)
    {
        opaqueDrawCommands.Clear();
        transparentDrawCommands.Clear();

        var currentDepthInt = 1;
        foreach (ref var drawCommand in drawData.DrawCommands)
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

        // Opaque draws go front to back to avoid overdraw
        opaqueDrawCommands.Reverse();

        var camera = drawData.ViewMatrix;

        var viewport = drawData.Viewport;
        passViewport = viewport;

        if (viewport.Enabled)
            GL.Viewport(viewport.X, currentFboSize.Y - viewport.Bottom, viewport.Width, viewport.Height);
        else
            GL.Viewport(0, 0, currentFboSize.X, currentFboSize.Y);

        // Clear buffers. The target holds premultiplied colors, and so does what it is cleared to
        var drawClearColor = drawData.ClearColor;
        var clearColor = new ColorRgba(drawClearColor.R * drawClearColor.A, drawClearColor.G * drawClearColor.A, drawClearColor.B * drawClearColor.A, drawClearColor.A);
        var depth = 0.0f;
        GL.Disable(EnableCap.ScissorTest);
        if (viewport.Enabled)
        {
            // What is outside the part is nothing, and the part is what it is cleared to
            var nothing = new ColorRgba(0f, 0f, 0f, 0f);
            GL.ClearNamedFramebufferf(fboHandle, OpenTK.Graphics.OpenGL.Buffer.Color, 0, in nothing.R);
            GL.ClearNamedFramebufferf(fboHandle, OpenTK.Graphics.OpenGL.Buffer.Depth, 0, in depth);
            ApplyClip(default);
            GL.ClearNamedFramebufferf(fboHandle, OpenTK.Graphics.OpenGL.Buffer.Color, 0, in clearColor.R);
            GL.Disable(EnableCap.ScissorTest);
        }
        else
        {
            GL.ClearNamedFramebufferf(fboHandle, OpenTK.Graphics.OpenGL.Buffer.Color, 0, in clearColor.R);
            GL.ClearNamedFramebufferf(fboHandle, OpenTK.Graphics.OpenGL.Buffer.Depth, 0, in depth);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);

        GL.UseProgram(programHandle);

        // Set viewport-size uniform for sub-pixel dilation in the text vertex shader
        GL.Uniform2f(viewportSizeUniformLocation, viewport.Enabled ? viewport.Width : currentFboSize.X, viewport.Enabled ? viewport.Height : currentFboSize.Y);

        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, multiDrawIndirectBufferHandle);

        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindMultiDraw, multiDrawItemsBufferHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindMesh, multiDrawMeshItemsBufferHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindText, multiDrawTextItemsBufferHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindGlyphs, glyphStorageBufferHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindCurves, curveSsboHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindCurveIndices, curveIndexSsboHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindBandEntries, bandEntrySsboHandle);
        GL.BindBufferBase(BufferTarget.ShaderStorageBuffer, BindShapeEntries, shapeEntrySsboHandle);

        GL.BindVertexArray(vertexArrayHandle);

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

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        GL.NamedFramebufferTexture(postProcessFboHandle, FramebufferAttachment.ColorAttachment0, postProcessTextureHandle1, 0);

        GL.BlitNamedFramebuffer(
            fboHandle, postProcessFboHandle,
            0, 0,
            renderSize.X, renderSize.Y,
            0, 0,
            renderSize.X, renderSize.Y,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // (Passes of a layered frame with no effects are left as they are)
        return HandlePostProcessing(drawData.PostProcessingState, postProcessTextureHandle1, postProcessTextureHandle2, skipIfNoFinalEffects: layered);
    }

    private void RenderDrawDataList(in Span<DrawCommandWithDepth> drawCommands, in DrawData drawData, in Matrix3x2 camera)
    {
        if (drawCommands.Length == 0)
            return;

        multiDrawIndirectBuffer.Clear();
        multiDrawItemsBuffer.Clear();
        multiDrawMeshItemsBuffer.Clear();
        multiDrawTextItemsBuffer.Clear();

        var meshInfosSpan = CollectionsMarshal.AsSpan(meshInfos);
        var textInfosSpan = CollectionsMarshal.AsSpan(textInfos);

        // Draws that can be drawn with the same bound textures make a page, a multi-draw of its own. Bindless needs no binding: one page.
        if (!bindlessTextures)
            texturePager.Plan(drawData, drawCommands, texturePageSize);

        pageDraws.Clear();
        var currentMeshDrawId = 0;
        var currentTextDrawId = 0;
        var pageCount = bindlessTextures ? 1 : texturePager.Pages.Count;
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = bindlessTextures ? new TexturePage(0, drawCommands.Length, 0, 0) : texturePager.Pages[pageIndex];
            var pageEnd = page.FirstCommand + page.CommandCount;
            var firstIndirectCommand = multiDrawIndirectBuffer.Length;

            // Meshes drawn one after another with the same mesh share an indirect command, as its instances
            var lastMeshDrawId = currentMeshDrawId;
            for (var i = page.FirstCommand; i < pageEnd; i++)
            {
                ref var currentCommand = ref drawCommands[i];

                // The clip is the draw's own, cut in the shader, so a change of it does not split the multi-draw (only the batch of one mesh)
                var (clipMin, clipMax) = ClipBounds.Pack(currentCommand.Clip, passViewport, currentFboSize);

                switch (currentCommand.DrawType)
                {
                    case DrawType.Mesh:
                    {
                        ref var meshDrawItem = ref drawData.MeshDrawItems[currentCommand.DrawId];
                        ref var meshInfo = ref meshInfosSpan[meshDrawItem.MeshHandle.Id];

                        var mvp = meshDrawItem.Transform * camera;
                        var texture = TextureFieldsOf(meshDrawItem.RenderMode == RenderMode.Normal ? meshDrawItem.Texture : TextureHandle.None, bindlessTextures ? -1 : texturePager.SlotOf(i));

                        multiDrawMeshItemsBuffer.Append(new GpuMeshDrawItem
                        {
                            Mvp = mvp,
                            Z = currentCommand.Depth,
                            RenderMode = (int) meshDrawItem.RenderMode,
                            Color1 = meshDrawItem.Color1,
                            Color2 = meshDrawItem.Color2,
                            GradientRotation = meshDrawItem.GradientRotation,
                            GradientScale = meshDrawItem.GradientScale,
                            MinUV = meshDrawItem.MinUV,
                            MaxUV = meshDrawItem.MaxUV,
                            Texture0 = texture.Texture0,
                            Texture1 = texture.Texture1,
                            Fog = meshDrawItem.Fog,
                            TextureFlags = texture.Flags
                        });
                        currentMeshDrawId++;

                        if (i + 1 >= pageEnd ||
                            drawCommands[i + 1].DrawType != DrawType.Mesh ||
                            meshDrawItem.MeshHandle.Id != drawData.MeshDrawItems[drawCommands[i + 1].DrawId].MeshHandle.Id ||
                            drawCommands[i + 1].Clip != currentCommand.Clip)
                        {
                            multiDrawItemsBuffer.Append(new GpuMultiDrawItem
                            {
                                RenderType = 0,
                                DrawId = lastMeshDrawId,
                                ClipMin = clipMin,
                                ClipMax = clipMax
                            });

                            multiDrawIndirectBuffer.Append(new DrawElementsIndirectCommand
                            {
                                Count = meshInfo.IndexCount,
                                InstanceCount = currentMeshDrawId - lastMeshDrawId,
                                FirstIndex = meshInfo.IndexBufferAllocation.Offset,
                                BaseVertex = meshInfo.VertexBufferAllocation.Offset,
                                BaseInstance = 0
                            });

                            lastMeshDrawId = currentMeshDrawId;
                        }
                        break;
                    }
                    case DrawType.Text:
                    {
                        ref var textDrawItem = ref drawData.TextDrawItems[currentCommand.DrawId];
                        ref var textInfo = ref textInfosSpan[textDrawItem.TextHandle.Id];

                        if (textInfo.GlyphCount == 0)
                            break;

                        ref var meshInfo = ref meshInfosSpan[primitive.GlyphMeshHandle.Id];

                        var mvp = textDrawItem.Transform * camera;

                        multiDrawItemsBuffer.Append(new GpuMultiDrawItem
                        {
                            RenderType = 1,
                            DrawId = currentTextDrawId++,
                            ClipMin = clipMin,
                            ClipMax = clipMax
                        });

                        multiDrawTextItemsBuffer.Append(new GpuTextDrawItem
                        {
                            Mvp = mvp,
                            Z = currentCommand.Depth,
                            GlyphOffset = textInfo.Allocation.Offset,
                            Color = textDrawItem.Color
                        });

                        multiDrawIndirectBuffer.Append(new DrawElementsIndirectCommand
                        {
                            Count = meshInfo.IndexCount,
                            InstanceCount = textInfo.GlyphCount,
                            FirstIndex = meshInfo.IndexBufferAllocation.Offset,
                            BaseVertex = meshInfo.VertexBufferAllocation.Offset,
                            BaseInstance = 0
                        });
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported or invalid data for render type '{currentCommand.DrawType}'");
                }
            }

            if (multiDrawIndirectBuffer.Length > firstIndirectCommand)
                pageDraws.Add(new PageDraw(pageIndex, firstIndirectCommand, multiDrawIndirectBuffer.Length - firstIndirectCommand));
        }

        var multiDrawIndirectBufferData = multiDrawIndirectBuffer.DataAsBytes;
        var multiDrawItemsBufferData = multiDrawItemsBuffer.DataAsBytes;
        var multiDrawMeshItemsBufferData = multiDrawMeshItemsBuffer.DataAsBytes;
        var multiDrawTextItemsBufferData = multiDrawTextItemsBuffer.DataAsBytes;
        
        if (multiDrawIndirectBufferData.Length > 0)
        {
            if (multiDrawIndirectBufferData.Length > multiDrawIndirectBufferSize)
            {
                multiDrawIndirectBufferSize = multiDrawIndirectBufferData.Length;
                GL.NamedBufferData(multiDrawIndirectBufferHandle, multiDrawIndirectBufferData.Length, multiDrawIndirectBufferData, VertexBufferObjectUsage.DynamicDraw);
            }
            else
                GL.NamedBufferSubData(multiDrawIndirectBufferHandle, IntPtr.Zero, multiDrawIndirectBufferData.Length, multiDrawIndirectBufferData);
        }

        if (multiDrawItemsBufferData.Length > 0)
        {
            if (multiDrawItemsBufferData.Length > multiDrawItemsBufferSize)
            {
                multiDrawItemsBufferSize = multiDrawItemsBufferData.Length;
                GL.NamedBufferData(multiDrawItemsBufferHandle, multiDrawItemsBufferData.Length, multiDrawItemsBufferData, VertexBufferObjectUsage.DynamicDraw);
            }
            else
                GL.NamedBufferSubData(multiDrawItemsBufferHandle, IntPtr.Zero, multiDrawItemsBufferData.Length, multiDrawItemsBufferData);
        }
        
        if (multiDrawMeshItemsBufferData.Length > 0)
        {
            if (multiDrawMeshItemsBufferData.Length > multiDrawMeshItemsBufferSize)
            {
                multiDrawMeshItemsBufferSize = multiDrawMeshItemsBufferData.Length;
                GL.NamedBufferData(multiDrawMeshItemsBufferHandle, multiDrawMeshItemsBufferData.Length, multiDrawMeshItemsBufferData, VertexBufferObjectUsage.DynamicDraw);
            }
            else
                GL.NamedBufferSubData(multiDrawMeshItemsBufferHandle, IntPtr.Zero, multiDrawMeshItemsBufferData.Length, multiDrawMeshItemsBufferData);
        }
        
        if (multiDrawTextItemsBufferData.Length > 0)
        {
            if (multiDrawTextItemsBufferData.Length > multiDrawTextItemsBufferSize)
            {
                multiDrawTextItemsBufferSize = multiDrawTextItemsBufferData.Length;
                GL.NamedBufferData(multiDrawTextItemsBufferHandle, multiDrawTextItemsBufferData.Length, multiDrawTextItemsBufferData, VertexBufferObjectUsage.DynamicDraw);
            }
            else
                GL.NamedBufferSubData(multiDrawTextItemsBufferHandle, IntPtr.Zero, multiDrawTextItemsBufferData.Length, multiDrawTextItemsBufferData);
        }

        // Draw, a page at a time
        foreach (var (pageIndex, firstCommand, commandCount) in pageDraws)
        {
            if (!bindlessTextures)
            {
                var textures = texturePager.TexturesOf(texturePager.Pages[pageIndex]);
                for (var slot = 0; slot < textures.Length; slot++)
                    GL.BindTextureUnit((uint) slot, FindTexture(textures[slot])?.Handle ?? 0);
            }

            // gl_DrawID starts from 0 in every multi-draw
            GL.Uniform1ui(drawBaseUniformLocation, (uint) firstCommand);
            GL.MultiDrawElementsIndirect(
                PrimitiveType.Triangles,
                DrawElementsType.UnsignedInt,
                new IntPtr(firstCommand * Unsafe.SizeOf<DrawElementsIndirectCommand>()),
                commandCount,
                0);
        }
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

    private int HandlePostProcessing(PostProcessingState state, int texture1, int texture2, bool skipIfNoFinalEffects = false)
    {
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
        while (meshLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    Debug.Assert(command.Data.HasValue);
                    
                    var (vertices, indices) = command.Data.Value;

                    var vtxAlloc = vertexBufferAllocator.Allocate(vertices.Length, (oldCapacity, newCapacity) =>
                    {
                        var newBufferHandle = GL.CreateBuffer();
                        GL.NamedBufferData(newBufferHandle, newCapacity * Unsafe.SizeOf<Vertex>(), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);
                        GL.CopyNamedBufferSubData(vertexBufferHandle, newBufferHandle, IntPtr.Zero, IntPtr.Zero, oldCapacity * Unsafe.SizeOf<Vertex>());
                        GL.DeleteBuffer(vertexBufferHandle);
                        vertexBufferHandle = newBufferHandle;
                        
                        GL.VertexArrayVertexBuffer(vertexArrayHandle, 0, vertexBufferHandle, IntPtr.Zero, Unsafe.SizeOf<Vertex>());
                        
                        logger.LogInformation("Mesh vertex buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });
                    
                    var idxAlloc = indexBufferAllocator.Allocate(indices.Length, (oldCapacity, newCapacity) =>
                    {
                        var newBufferHandle = GL.CreateBuffer();
                        GL.NamedBufferData(newBufferHandle, newCapacity * sizeof(int), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);
                        GL.CopyNamedBufferSubData(indexBufferHandle, newBufferHandle, IntPtr.Zero, IntPtr.Zero, oldCapacity * sizeof(int));
                        GL.DeleteBuffer(indexBufferHandle);
                        indexBufferHandle = newBufferHandle;
                        
                        GL.VertexArrayElementBuffer(vertexArrayHandle, indexBufferHandle);
                        
                        logger.LogInformation("Mesh index buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });
                    
                    GL.NamedBufferSubData(vertexBufferHandle, vtxAlloc.Offset * Unsafe.SizeOf<Vertex>(), vertices.Length * Unsafe.SizeOf<Vertex>(), vertices);
                    GL.NamedBufferSubData(indexBufferHandle, idxAlloc.Offset * sizeof(int), indices.Length * sizeof(int), indices);
                    
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
                    var meshInfo = meshInfos[command.Id];
                    vertexBufferAllocator.Free(meshInfo.VertexBufferAllocation);
                    indexBufferAllocator.Free(meshInfo.IndexBufferAllocation);
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

                    var textureHandle = GL.CreateTexture(TextureTarget.Texture2d);
                    GL.TextureStorage2D(textureHandle, 1, SizedInternalFormat.Rgba8, width, height);
                    GL.TextureSubImage2D(textureHandle, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixelData);

                    // The sampling is the texture's own (and has to be set before a bindless handle is made)
                    GL.TextureParameteri(textureHandle, TextureParameterName.TextureMinFilter, GetFilter(sampling.MinificationFilter));
                    GL.TextureParameteri(textureHandle, TextureParameterName.TextureMagFilter, GetFilter(sampling.MagnificationFilter));
                    GL.TextureParameteri(textureHandle, TextureParameterName.TextureWrapS, GetWrapMode(sampling.WrapModeU));
                    GL.TextureParameteri(textureHandle, TextureParameterName.TextureWrapT, GetWrapMode(sampling.WrapModeV));
                    var border = sampling.BorderColor;
                    GL.TextureParameterf(textureHandle, TextureParameterName.TextureBorderColor, in border.R);

                    var bindlessHandle = 0UL;
                    if (bindlessTextures)
                    {
                        bindlessHandle = GL.ARB.GetTextureHandleARB(textureHandle);
                        GL.ARB.MakeTextureHandleResidentARB(bindlessHandle);
                    }

                    textureInfos.EnsureCount(command.Id + 1);
                    textureInfos[command.Id] = new TextureInfo
                    {
                        Handle = textureHandle,
                        BindlessHandle = bindlessHandle,
                        CrispEdges = sampling.IsCrisp
                    };
                    break;
                }
                case LifecycleCommandType.Destroy:
                {
                    var textureInfo = textureInfos[command.Id];
                    if (textureInfo.HasValue)
                    {
                        DeleteTexture(textureInfo.Value);
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

    private static void DeleteTexture(in TextureInfo texture)
    {
        if (texture.BindlessHandle != 0)
            GL.ARB.MakeTextureHandleNonResidentARB(texture.BindlessHandle);

        GL.DeleteTexture(texture.Handle);
    }

    private TextureFields TextureFieldsOf(TextureHandle texture, int slot)
    {
        if (texture.IsNone || FindTexture(texture.Id) is not { } info)
            return new TextureFields(bindlessTextures ? 0 : -1, 0, 0);

        var flags = info.CrispEdges ? 1 : 0;
        return bindlessTextures
            ? new TextureFields((int) (uint) info.BindlessHandle, (int) (uint) (info.BindlessHandle >> 32), flags)
            : new TextureFields(slot, 0, flags);
    }

    /// <summary>The texture with an id: a texture of the factory or a pass output.</summary>
    private TextureInfo? FindTexture(int id)
    {
        if (TextureHandle.TryGetPassIndex(id, out var passIndex))
            return passIndex < passTextures.Count ? passTextures[passIndex] : null;

        return id < textureInfos.Count ? textureInfos[id] : null;
    }

    private static bool SupportsExtension(string name)
    {
        var count = GL.GetInteger(GetPName.NumExtensions);
        for (var i = 0; i < count; i++)
        {
            if (GL.GetStringi(StringName.Extensions, (uint) i) == name)
                return true;
        }

        return false;
    }

    private void UpdateFontData()
    {
        if (!fontBuffersDirty)
            return;

        fontBuffersDirty = false;
        
        var curves = resources.FontCurves.AsSpan();
        var curveIndices = resources.FontCurveIndices.AsSpan();
        var bandEntries = resources.FontBandEntries.AsSpan();
        
        var shapeEntries = resources.FontShapeEntries;
        var gpuShapeEntries = shapeEntries.Select(x => new GpuShapeEntry
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
        
        GL.NamedBufferData(curveSsboHandle, curves.Length * Unsafe.SizeOf<QuadraticCurve>(), curves, VertexBufferObjectUsage.DynamicDraw);
        GL.NamedBufferData(curveIndexSsboHandle, curveIndices.Length * sizeof(int), curveIndices, VertexBufferObjectUsage.DynamicDraw);
        GL.NamedBufferData(bandEntrySsboHandle, bandEntries.Length * Unsafe.SizeOf<BandEntry>(), bandEntries, VertexBufferObjectUsage.DynamicDraw);
        GL.NamedBufferData(shapeEntrySsboHandle, gpuShapeEntries.Length * Unsafe.SizeOf<GpuShapeEntry>(), gpuShapeEntries, VertexBufferObjectUsage.DynamicDraw);
        
        logger.LogInformation(
            "Font buffers updated, registered {Curves} curves, {CurveIndices} curve indices, {Bands} band entries, {Shapes} shape entries",
            resources.FontCurves.Length,
            resources.FontCurveIndices.Length,
            resources.FontBandEntries.Length,
            resources.FontShapeEntries.Length);
    }

    private void UpdateTextData()
    {
        while (textLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    var renderGlyphsData = command.Data;
                    Debug.Assert(renderGlyphsData != null);
                    
                    var renderGlyphs = renderGlyphsData.Select(x => new GpuRenderGlyph
                    {
                        Color = x.Color,
                        Transform = x.Transform,
                        ShapeEntryIndex = x.ShapeEntryIndex,
                    }).ToArray();

                    var glyphAlloc = glyphBufferAllocator.Allocate(renderGlyphs.Length, (oldCapacity, newCapacity) =>
                    {
                        var newBufferHandle = GL.CreateBuffer();
                        GL.NamedBufferData(newBufferHandle, newCapacity * Unsafe.SizeOf<GpuRenderGlyph>(), IntPtr.Zero, VertexBufferObjectUsage.DynamicDraw);
                        GL.CopyNamedBufferSubData(glyphStorageBufferHandle, newBufferHandle, IntPtr.Zero, IntPtr.Zero, oldCapacity * Unsafe.SizeOf<GpuRenderGlyph>());
                        GL.DeleteBuffer(glyphStorageBufferHandle);
                        glyphStorageBufferHandle = newBufferHandle;

                        logger.LogInformation("Glyph buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });

                    // Upload glyph data
                    GL.NamedBufferSubData(glyphStorageBufferHandle, glyphAlloc.Offset * Unsafe.SizeOf<GpuRenderGlyph>(), renderGlyphs.Length * Unsafe.SizeOf<GpuRenderGlyph>(), renderGlyphs);

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
        GL.DeleteTexture(postProcessTextureHandle1);
        GL.DeleteTexture(postProcessTextureHandle2);

        // The composite texture and the textures of the passes are made again when they are needed
        GL.DeleteTexture(compositeTextureHandle);
        compositeTextureHandle = 0;
        DeletePassTextures();

        fboColorBufferHandle = GL.CreateRenderbuffer();
        GL.NamedRenderbufferStorageMultisample(fboColorBufferHandle, MsaaSamples, InternalFormat.Rgba16f, size.X, size.Y);
        
        fboDepthBufferHandle = GL.CreateRenderbuffer();
        GL.NamedRenderbufferStorageMultisample(fboDepthBufferHandle, MsaaSamples, InternalFormat.DepthComponent32f, size.X, size.Y);
        
        postProcessTextureHandle1 = GL.CreateTexture(TextureTarget.Texture2d);
        GL.TextureStorage2D(postProcessTextureHandle1, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);
        
        postProcessTextureHandle2 = GL.CreateTexture(TextureTarget.Texture2d);
        GL.TextureStorage2D(postProcessTextureHandle2, 1, SizedInternalFormat.Rgba16f, size.X, size.Y);
        
        GL.NamedFramebufferRenderbuffer(fboHandle, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, fboColorBufferHandle);
        GL.NamedFramebufferRenderbuffer(fboHandle, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, fboDepthBufferHandle);

        currentFboSize = size;
        
        logger.LogInformation("Framebuffer size updated, now at {Width}x{Height}", currentFboSize.X, currentFboSize.Y);
    }

    private static void Swap<T>(ref T a, ref T b)
    {
        (a, b) = (b, a);
    }
}