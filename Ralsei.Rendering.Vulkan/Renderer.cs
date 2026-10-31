using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Core.Data;
using Ralsei.Data;
using Ralsei.Mathematics;
using Ralsei.Platform;
using Ralsei.Platform.Vulkan;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Ralsei.Rendering.Vulkan.PostProcessing;
using Ralsei.Util;
using BandEntry = Tmpx.Common.BandEntry;
using QuadraticCurve = Tmpx.Common.QuadraticCurve;

namespace Ralsei.Rendering.Vulkan;

internal sealed unsafe class Renderer : IRenderer, IDisposable
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

    /// <summary>The draws of a pass that are drawn with the same textures bound: a range of its indirect commands.</summary>
    private readonly record struct PageDraw(int[] Textures, int FirstCommand, int CommandCount);

    // Must match the push constant block in Uber.vert
    [StructLayout(LayoutKind.Sequential)]
    private struct UberPushConstants
    {
        public Vector2 ViewportSize;
        public uint DrawBase;
    }

    // Frames that can be recorded while the GPU is still busy with earlier ones
    private const int FramesInFlight = 3;

    private const int MsaaSamples = 4;

    // Descriptor set 1 of Uber.frag: all the textures (bindless) or the page a draw is drawn with
    private const int TexturePageSize = 16;

    // How many of the slots of a page are used: all, unless the environment asks for fewer to try what many pages do
    private readonly int usedPageSize = int.TryParse(Environment.GetEnvironmentVariable("RALSEI_TEXTURE_PAGE_SIZE"), out var requested) && requested > 0
        ? Math.Min(TexturePageSize, requested)
        : TexturePageSize;

    // Storage buffer bindings in Uber.vert and Uber.frag
    private const int BindMultiDraw = 0;
    private const int BindMesh = 1;
    private const int BindText = 2;
    private const int BindGlyphs = 3;
    private const int BindCurves = 4;
    private const int BindCurveIndices = 5;
    private const int BindBandEntries = 6;
    private const int BindShapeEntries = 7;
    private const int StorageBufferBindingCount = 8;

    private const VkFormat ColorFormat = VkFormat.FormatR16g16b16a16Sfloat;
    private const VkFormat DepthFormat = VkFormat.FormatD32Sfloat;

    private const VkBufferUsageFlagBits ResizableBufferUsage =
        VkBufferUsageFlagBits.BufferUsageTransferSrcBit | VkBufferUsageFlagBits.BufferUsageTransferDstBit;

    // Storage buffers can't be empty, and these are never read when they hold nothing
    private const ulong MinStorageBufferSize = 16;

    private const int InitialVertexBufferCapacity = 1024;
    private const int InitialIndexBufferCapacity = 1024;

    private readonly PooledSuballocator vertexBufferAllocator = new(InitialVertexBufferCapacity);
    private readonly PooledSuballocator indexBufferAllocator = new(InitialIndexBufferCapacity);

    private readonly List<MeshInfo> meshInfos = [];

    // Textures sampled alike share a sampler
    private sealed record TextureEntry(
        GpuImage Image, GpuSampler Sampler, bool CrispEdges, int BindlessIndex,
        VkImageLayout Layout = VkImageLayout.ImageLayoutShaderReadOnlyOptimal);

    private readonly List<TextureEntry?> textures = [];

    // What passes drew, kept for later passes that use them as textures (TextureHandle.FromPass), by pass index; in the general layout
    private readonly List<TextureEntry?> passImages = [];
    private bool[] passOutputUsed = [];
    private GpuSampler? passSampler;
    private readonly Dictionary<TextureSampling, GpuSampler> samplers = [];

    // What empty page slots are bound to (shaders read every slot's descriptor)
    private GpuImage emptyTexture = null!;
    private GpuSampler emptySampler = null!;

    // Bindless: one descriptor set with every texture; an index is reused only once the frames that might draw it are done
    private bool bindlessTextures;
    private VkDescriptorPool bindlessPool;
    private VkDescriptorSet bindlessSet;
    private readonly Stack<int> freeBindlessIndices = [];
    private int nextBindlessIndex;

    // Otherwise draws are divided into pages, each with a descriptor set of its own
    private readonly TexturePager texturePager = new();

    /// <summary>What a pass of the frame being recorded needs.</summary>
    private sealed class PassRecord
    {
        public ColorRgba ClearColor;
        public float Opacity;
        public ClipRect Viewport;

        /// <summary>The part of the target the pass works on, if only a part (see <see cref="PassRegion"/>).</summary>
        public ClipRect Region;
        public PostProcessingState PostProcessingState;
        public readonly List<PageDraw> OpaquePageDraws = [];
        public readonly List<PageDraw> TransparentPageDraws = [];
    }

    private readonly List<PassRecord> passRecords = [];

    private const int InitialGlyphBufferCapacity = 1024;

    private readonly PooledSuballocator glyphBufferAllocator = new(InitialGlyphBufferCapacity);
    private readonly List<TextInfo> textInfos = [];

    private readonly VulkanContext context;
    private readonly ComputeShared compute;
    private readonly FrameResources[] frames = new FrameResources[FramesInFlight];
    private int frameIndex;

    private readonly LegacyBloom legacyBloom;
    private readonly UniversalBloom universalBloom;
    private readonly Glitch glitch;
    private readonly Grain grain;
    private readonly UberPost uberPost;
    private readonly Blend blend;
    private readonly DepthOfField depthOfField;

    private GpuBuffer vertexBuffer, indexBuffer;
    private GpuBuffer glyphStorageBuffer;

    // TMPX storage buffers, replaced whenever the font set changes
    private GpuBuffer curveBuffer, curveIndexBuffer, bandEntryBuffer, shapeEntryBuffer;

    private VkDescriptorSetLayout uberSetLayout, textureSetLayout;
    private VkPipelineLayout uberPipelineLayout;
    private VkPipeline opaquePipeline, transparentPipeline;

    private Vector2i currentFboSize;
    private GpuImage? msaaColorImage; // null when not multisampling
    private GpuImage msaaDepthImage = null!;
    private GpuImage postProcessImage1 = null!, postProcessImage2 = null!;

    private GpuImage? viewportImage1, viewportImage2;

    // What multiple passes are layered on
    private GpuImage? compositeImage;

    private Swapchain? swapchain;
    private Vector2i swapchainRequestedSize;
    private bool swapchainDirty;

    private GpuImage? captureImage;
    private GpuBuffer? readbackBuffer;

    private readonly List<DrawCommandWithDepth> opaqueDrawCommands = [];
    private readonly List<DrawCommandWithDepth> transparentDrawCommands = [];

    private readonly Buffer<DrawIndexedIndirectCommand> multiDrawIndirectBuffer = new();
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
    private readonly IVulkanSurface surface;
    private readonly IFrameReceiver? frameReceiver;
    private readonly ILogger<Renderer> logger;

    public Renderer(
        RenderingFactory renderingFactory,
        BuiltinRenderingPrimitive primitive,
        IVulkanSurface surface,
        ResourceLoader loader,
        ILogger<Renderer> logger)
    {
        resources = renderingFactory.CreateMirror();
        this.primitive = primitive;
        this.surface = surface;
        this.logger = logger;
        frameReceiver = surface as IFrameReceiver;

        logger.LogInformation("Initializing Vulkan renderer");

        context = new VulkanContext(surface, MsaaSamples, logger);

        try
        {
            compute = new ComputeShared(context);

            for (var i = 0; i < FramesInFlight; i++)
                frames[i] = new FrameResources(context);

            #region Vulkan Data Initialization

            {
                var size = surface.RenderSize;

                vertexBuffer = new GpuBuffer(
                    context, (ulong) (InitialVertexBufferCapacity * Unsafe.SizeOf<Vertex>()),
                    VkBufferUsageFlagBits.BufferUsageVertexBufferBit | ResizableBufferUsage, MemoryUsage.DeviceLocal);

                indexBuffer = new GpuBuffer(
                    context, (ulong) (InitialIndexBufferCapacity * sizeof(int)),
                    VkBufferUsageFlagBits.BufferUsageIndexBufferBit | ResizableBufferUsage, MemoryUsage.DeviceLocal);

                glyphStorageBuffer = new GpuBuffer(
                    context, (ulong) (InitialGlyphBufferCapacity * Unsafe.SizeOf<GpuRenderGlyph>()),
                    VkBufferUsageFlagBits.BufferUsageStorageBufferBit | ResizableBufferUsage, MemoryUsage.DeviceLocal);

                // Filled lazily by UpdateFontData
                curveBuffer = CreateStorageBuffer(MinStorageBufferSize);
                curveIndexBuffer = CreateStorageBuffer(MinStorageBufferSize);
                bandEntryBuffer = CreateStorageBuffer(MinStorageBufferSize);
                shapeEntryBuffer = CreateStorageBuffer(MinStorageBufferSize);

                // Read by texture slots that nothing is bound to
                emptyTexture = GpuImage.CreateGeneral(
                    context, new Vector2i(1, 1), VkFormat.FormatR8g8b8a8Unorm,
                    VkImageUsageFlagBits.ImageUsageSampledBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);
                emptySampler = new GpuSampler(context, TextureSampling.Nearest);

                // Bindless if the device supports it (RALSEI_NO_BINDLESS forces pages)
                bindlessTextures = context.SupportsBindlessTextures && Environment.GetEnvironmentVariable("RALSEI_NO_BINDLESS") is null;
                logger.LogInformation(bindlessTextures ? "Textures are bindless" : "Textures are bound {PageSize} at a time", usedPageSize);

                CreateUberPipelines(loader);

                CreateRenderTargets(size, null);

                legacyBloom = new LegacyBloom(context, compute, loader);
                universalBloom = new UniversalBloom(context, compute, loader);
                glitch = new Glitch(context, compute, loader);
                grain = new Grain(context, compute, loader);
                uberPost = new UberPost(context, compute, loader);
                blend = new Blend(context, compute, loader);
                depthOfField = new DepthOfField(context, compute, loader);
            }

            #endregion
        }
        catch
        {
            Dispose();
            throw;
        }

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

        logger.LogInformation("Disposing Vulkan renderer");

        resources.FontBuffersUpdated -= OnFontBuffersUpdated;
        resources.Meshes.ItemInserted -= OnMeshInserted;
        resources.Meshes.ItemRemoved -= OnMeshRemoved;
        resources.Textures.ItemInserted -= OnTextureInserted;
        resources.Textures.ItemRemoved -= OnTextureRemoved;
        resources.Texts.ItemInserted -= OnTextInserted;
        resources.Texts.ItemRemoved -= OnTextRemoved;
        resources.Dispose();

        // Nothing is left to dispose if the device could not even be created
        if (context.Device.Handle == 0)
        {
            context.Dispose();
            return;
        }

        // Let the GPU finish before destroying anything it might still be using
        context.WaitIdle();

        legacyBloom?.Dispose();
        universalBloom?.Dispose();
        glitch?.Dispose();
        grain?.Dispose();
        uberPost?.Dispose();
        blend?.Dispose();
        depthOfField?.Dispose();

        foreach (var texture in textures)
            texture?.Image.Dispose();
        foreach (var sampler in samplers.Values)
            sampler.Dispose();
        passSampler?.Dispose();
        emptyTexture?.Dispose();
        emptySampler?.Dispose();

        swapchain?.Dispose();
        readbackBuffer?.Dispose();
        captureImage?.Dispose();
        DisposeRenderTargets();

        if (opaquePipeline.Handle != 0)
            Vk.DestroyPipeline(context.Device, opaquePipeline, null);
        if (transparentPipeline.Handle != 0)
            Vk.DestroyPipeline(context.Device, transparentPipeline, null);
        if (uberPipelineLayout.Handle != 0)
            Vk.DestroyPipelineLayout(context.Device, uberPipelineLayout, null);
        if (bindlessPool.Handle != 0)
            Vk.DestroyDescriptorPool(context.Device, bindlessPool, null);
        if (textureSetLayout.Handle != 0)
            Vk.DestroyDescriptorSetLayout(context.Device, textureSetLayout, null);
        if (uberSetLayout.Handle != 0)
            Vk.DestroyDescriptorSetLayout(context.Device, uberSetLayout, null);

        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();
        glyphStorageBuffer?.Dispose();
        curveBuffer?.Dispose();
        curveIndexBuffer?.Dispose();
        bandEntryBuffer?.Dispose();
        shapeEntryBuffer?.Dispose();

        foreach (var frame in frames)
            frame?.Dispose();

        compute?.Dispose();
        context.Dispose();
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

    #region Frame

    public void ProcessFrame(IDrawDataProvider drawDataProvider)
    {
        var renderSize = surface.RenderSize;

        // Nothing to render to, e.g. because the window is minimized
        if (renderSize.X <= 0 || renderSize.Y <= 0)
            return;

        resources.Apply(drawDataProvider.ResourceVersion);

        var framebufferSize = Vector2i.Zero;
        if (!context.IsHeadless)
        {
            framebufferSize = surface.FramebufferSize;
            if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
                return;
        }

        // Wait until the GPU is done with this frame's last use
        var frame = frames[frameIndex];
        frameIndex = (frameIndex + 1) % FramesInFlight;
        frame.WaitForCompletion();

        var imageIndex = 0u;
        if (!context.IsHeadless && !TryAcquireSwapchainImage(frame, framebufferSize, out imageIndex))
            return;

        frame.Begin();

        UpdateVulkanData(frame, renderSize);

        // Gather the data of all the passes in the same buffers, and upload it
        multiDrawIndirectBuffer.Clear();
        multiDrawItemsBuffer.Clear();
        multiDrawMeshItemsBuffer.Clear();
        multiDrawTextItemsBuffer.Clear();

        var currentMeshDrawId = 0;
        var currentTextDrawId = 0;

        var passCount = drawDataProvider.PassCount;
        while (passRecords.Count < passCount)
            passRecords.Add(new PassRecord());

        // Passes whose output a later draw uses get an image before the draws are gathered
        if (passOutputUsed.Length < passCount)
            passOutputUsed = new bool[Math.Max(passCount, passOutputUsed.Length * 2)];
        Array.Clear(passOutputUsed);
        RenderUtil.ValidatePassTextures(drawDataProvider, passOutputUsed);
        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            if (passOutputUsed[passIndex])
                EnsurePassImage(passIndex);
        }

        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            var drawData = drawDataProvider.CreateDrawData(passIndex);
            var record = passRecords[passIndex];
            record.ClearColor = drawData.ClearColor;
            record.Opacity = drawData.Opacity;
            record.Viewport = drawData.Viewport;
            record.Region = PassRegion.Of(drawData, passCount > 1, passOutputUsed[passIndex], currentFboSize);
            record.PostProcessingState = drawData.PostProcessingState;
            record.OpaquePageDraws.Clear();
            record.TransparentPageDraws.Clear();

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
                        Depth = currentDepthInt / (float) (1 << 23),
                        Clip = drawCommand.Clip
                    });
                }
                else
                {
                    opaqueDrawCommands.Add(new DrawCommandWithDepth
                    {
                        DrawType = drawCommand.DrawType,
                        DrawId = drawCommand.DrawId,
                        Depth = currentDepthInt / (float) (1 << 23),
                        Clip = drawCommand.Clip
                    });
                }

                currentDepthInt++;
            }

            // Opaque draws go front to back to avoid overdraw
            opaqueDrawCommands.Reverse();

            var camera = drawData.ViewMatrix;

            AppendDrawData(CollectionsMarshal.AsSpan(opaqueDrawCommands), ref currentMeshDrawId, ref currentTextDrawId, drawData, camera, record.OpaquePageDraws);
            AppendDrawData(CollectionsMarshal.AsSpan(transparentDrawCommands), ref currentMeshDrawId, ref currentTextDrawId, drawData, camera, record.TransparentPageDraws);
        }

        var multiDrawIndirectBufferData = multiDrawIndirectBuffer.DataAsBytes;
        var multiDrawItemsBufferData = multiDrawItemsBuffer.DataAsBytes;
        var multiDrawMeshItemsBufferData = multiDrawMeshItemsBuffer.DataAsBytes;
        var multiDrawTextItemsBufferData = multiDrawTextItemsBuffer.DataAsBytes;

        frame.EnsureDrawBuffers(
            (ulong) multiDrawItemsBufferData.Length,
            (ulong) multiDrawMeshItemsBufferData.Length,
            (ulong) multiDrawTextItemsBufferData.Length,
            (ulong) multiDrawIndirectBufferData.Length);
        multiDrawIndirectBufferData.CopyTo(new Span<byte>(frame.DrawIndirectBuffer!.Mapped, multiDrawIndirectBufferData.Length));
        multiDrawItemsBufferData.CopyTo(new Span<byte>(frame.DrawItemsBuffer!.Mapped, multiDrawItemsBufferData.Length));
        multiDrawMeshItemsBufferData.CopyTo(new Span<byte>(frame.MeshDrawItemBuffer!.Mapped, multiDrawMeshItemsBufferData.Length));
        multiDrawTextItemsBufferData.CopyTo(new Span<byte>(frame.TextDrawItemBuffer!.Mapped, multiDrawTextItemsBufferData.Length));

        // Passes are layered on an image of their own, unless there is only one
        var layered = passCount > 1;
        if (layered)
            ClearCompositeImage(frame);

        var finalImage = postProcessImage1;
        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            var record = passRecords[passIndex];
            RenderScene(frame, record);

            // (Passes of a layered frame with no effects are left as they are)
            // A pass drawn in a part of the target has its effects on that part, as on a target of its own
            var passImage = record.Viewport.Enabled && record.PostProcessingState.HasEffects
                ? ProcessInViewport(frame, record.PostProcessingState, record.Viewport, layered)
                : HandlePostProcessing(frame, record.PostProcessingState, postProcessImage1, postProcessImage2, skipIfNoFinalEffects: layered);
            if (passOutputUsed[passIndex])
                RecordKeepPassOutput(frame, passIndex, passImage);

            if (layered)
                blend.Process(frame, currentFboSize, passImage, compositeImage!, record.Opacity, record.Region);
            else
                finalImage = passImage;
        }

        if (layered)
            finalImage = compositeImage!;

        if (drawDataProvider.FinalPostProcessingState is { } finalState)
        {
            var spareImage = finalImage == postProcessImage1 ? postProcessImage2 : postProcessImage1;
            finalImage = HandlePostProcessing(frame, finalState, finalImage, spareImage);
        }

        // The frame is done, make it available to transfers
        VkUtil.PipelineBarrier(frame.Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit | VkPipelineStageFlagBits2.PipelineStage2ColorAttachmentOutputBit,
            VkAccessFlagBits2.Access2ShaderWriteBit | VkAccessFlagBits2.Access2ColorAttachmentWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit));

        if (!context.IsHeadless)
            RecordPresent(frame, finalImage, renderSize, swapchain!.Images[(int) imageIndex], new ColorRgba());

        if (frameReceiver != null)
            RecordCapture(frame, finalImage, renderSize);

        frame.End();
        Submit(frame, imageIndex);

        if (frameReceiver != null)
        {
            // The receiver reads the frame as soon as we return
            frame.WaitForCompletion();
            frameReceiver.ReceiveFrame(
                new ReadOnlySpan<byte>(readbackBuffer!.Mapped, renderSize.X * renderSize.Y * 4),
                renderSize);
        }

        if (!context.IsHeadless)
            Present(imageIndex);
    }

    private bool TryAcquireSwapchainImage(FrameResources frame, Vector2i framebufferSize, out uint imageIndex)
    {
        imageIndex = 0;

        // A stale swapchain is found out here or when presenting; try again with a new one
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (swapchain == null || swapchainDirty || swapchainRequestedSize != framebufferSize)
                RecreateSwapchain(framebufferSize);

            uint index;
            var result = Vk.AcquireNextImageKHR(context.Device, swapchain!.Handle, ulong.MaxValue, frame.ImageAvailable, default, &index);
            switch (result)
            {
                case VkResult.Success:
                    imageIndex = index;
                    return true;
                case VkResult.SuboptimalKhr:
                    // The image can still be used, but the swapchain should be replaced soon
                    swapchainDirty |= !swapchain.TransformMismatch;
                    imageIndex = index;
                    return true;
                case VkResult.ErrorOutOfDateKhr:
                    swapchainDirty = true;
                    continue;
                default:
                    VkUtil.Check(result, "vkAcquireNextImageKHR");
                    return false;
            }
        }

        return false;
    }

    private void RecreateSwapchain(Vector2i framebufferSize)
    {
        // The old swapchain's images might still be in use
        context.WaitIdle();

        var old = swapchain;
        swapchain = new Swapchain(context, framebufferSize, surface.VSync, surface.LowLatency, surface.Transparent, old);
        old?.Dispose();

        swapchainRequestedSize = framebufferSize;
        swapchainDirty = false;

        logger.LogInformation("Swapchain created, {Width}x{Height}", swapchain.Size.X, swapchain.Size.Y);
    }

    private void Submit(FrameResources frame, uint imageIndex)
    {
        var cmdInfo = new VkCommandBufferSubmitInfo
        {
            sType = VkStructureType.StructureTypeCommandBufferSubmitInfo,
            commandBuffer = frame.Cmd
        };

        var waitInfo = new VkSemaphoreSubmitInfo
        {
            sType = VkStructureType.StructureTypeSemaphoreSubmitInfo,
            semaphore = frame.ImageAvailable,
            // The swapchain image is first touched by the transfer operations at the very end of the frame
            stageMask = VkPipelineStageFlagBits2.PipelineStage2AllTransferBit
        };

        var signalInfo = new VkSemaphoreSubmitInfo
        {
            sType = VkStructureType.StructureTypeSemaphoreSubmitInfo,
            stageMask = VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit
        };

        var submitInfo = new VkSubmitInfo2
        {
            sType = VkStructureType.StructureTypeSubmitInfo2,
            commandBufferInfoCount = 1,
            pCommandBufferInfos = &cmdInfo
        };

        if (!context.IsHeadless)
        {
            signalInfo.semaphore = swapchain!.RenderFinishedSemaphore(imageIndex);

            submitInfo.waitSemaphoreInfoCount = 1;
            submitInfo.pWaitSemaphoreInfos = &waitInfo;
            submitInfo.signalSemaphoreInfoCount = 1;
            submitInfo.pSignalSemaphoreInfos = &signalInfo;
        }

        VkUtil.Check(Vk.QueueSubmit2(context.Queue, 1, &submitInfo, frame.Fence), "vkQueueSubmit2");
    }

    private void Present(uint imageIndex)
    {
        var swapchainHandle = swapchain!.Handle;
        var renderFinished = swapchain.RenderFinishedSemaphore(imageIndex);

        var presentInfo = new VkPresentInfoKHR
        {
            sType = VkStructureType.StructureTypePresentInfoKhr,
            waitSemaphoreCount = 1,
            pWaitSemaphores = &renderFinished,
            swapchainCount = 1,
            pSwapchains = &swapchainHandle,
            pImageIndices = &imageIndex
        };

        var result = Vk.QueuePresentKHR(context.Queue, &presentInfo);
        switch (result)
        {
            case VkResult.Success:
                surface.FramePresented();
                break;
            case VkResult.SuboptimalKhr:
                swapchainDirty |= !swapchain.TransformMismatch;
                break;
            case VkResult.ErrorOutOfDateKhr:
                swapchainDirty = true;
                break;
            default:
                VkUtil.Check(result, "vkQueuePresentKHR");
                break;
        }
    }

    #endregion

    #region Drawing

    // Appends draw data to multi-draw buffers, and says which of the indirect commands are drawn with which textures bound
    private void AppendDrawData(in Span<DrawCommandWithDepth> drawCommands, ref int currentMeshDrawId, ref int currentTextDrawId, in DrawData drawData, in Matrix3x2 camera, List<PageDraw> pageDraws)
    {
        if (drawCommands.IsEmpty)
            return;

        var meshInfosSpan = CollectionsMarshal.AsSpan(meshInfos);
        var textInfosSpan = CollectionsMarshal.AsSpan(textInfos);

        // Without bindless, the draws are divided into pages by texture
        if (!bindlessTextures)
            texturePager.Plan(drawData, drawCommands, usedPageSize);

        var pageCount = bindlessTextures ? 1 : texturePager.Pages.Count;
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = bindlessTextures ? new TexturePage(0, drawCommands.Length, 0, 0) : texturePager.Pages[pageIndex];
            var pageEnd = page.FirstCommand + page.CommandCount;
            var firstIndirectCommand = multiDrawIndirectBuffer.Length;

            // Meshes drawn one after another with the same mesh share an indirect command, as its instances
            var lastMeshDrawId = currentMeshDrawId;
            int[] pageTextures = bindlessTextures ? [] : texturePager.TexturesOf(page).ToArray();
            for (var i = page.FirstCommand; i < pageEnd; i++)
            {
                ref var currentCommand = ref drawCommands[i];

                // The clip is the draw's own, cut in the shader, so draws with different clips are still batched
                // (the draws of all the passes are gathered before any is rendered, so it is the pass's own viewport and not the one being rendered)
                var (clipMin, clipMax) = ClipBounds.Pack(currentCommand.Clip, drawData.Viewport, currentFboSize);

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
                            TextureFlags = texture.Flags,
                            ClipMin = clipMin,
                            ClipMax = clipMax
                        });
                        currentMeshDrawId++;

                        if (i + 1 >= pageEnd ||
                            drawCommands[i + 1].DrawType != DrawType.Mesh ||
                            meshDrawItem.MeshHandle.Id != drawData.MeshDrawItems[drawCommands[i + 1].DrawId].MeshHandle.Id)
                        {
                            multiDrawItemsBuffer.Append(new GpuMultiDrawItem
                            {
                                RenderType = 0,
                                DrawId = lastMeshDrawId,
                            });

                            multiDrawIndirectBuffer.Append(new DrawIndexedIndirectCommand
                            {
                                IndexCount = meshInfo.IndexCount,
                                InstanceCount = currentMeshDrawId - lastMeshDrawId,
                                FirstIndex = meshInfo.IndexBufferAllocation.Offset,
                                VertexOffset = meshInfo.VertexBufferAllocation.Offset,
                                FirstInstance = 0
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
                            DrawId = currentTextDrawId++
                        });

                        multiDrawTextItemsBuffer.Append(new GpuTextDrawItem
                        {
                            Mvp = mvp,
                            Z = currentCommand.Depth,
                            GlyphOffset = textInfo.Allocation.Offset,
                            Color = textDrawItem.Color,
                            ClipMin = clipMin,
                            ClipMax = clipMax
                        });

                        multiDrawIndirectBuffer.Append(new DrawIndexedIndirectCommand
                        {
                            IndexCount = meshInfo.IndexCount,
                            InstanceCount = textInfo.GlyphCount,
                            FirstIndex = meshInfo.IndexBufferAllocation.Offset,
                            VertexOffset = meshInfo.VertexBufferAllocation.Offset,
                            FirstInstance = 0
                        });
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported or invalid data for render type '{currentCommand.DrawType}'");
                }
            }

            if (multiDrawIndirectBuffer.Length > firstIndirectCommand)
                pageDraws.Add(new PageDraw(pageTextures, firstIndirectCommand, multiDrawIndirectBuffer.Length - firstIndirectCommand));
        }
    }

    private void RenderScene(FrameResources frame, PassRecord record)
    {
        // The target holds premultiplied colors, and so does what it is cleared to
        var clearColor = new ColorRgba(record.ClearColor.R * record.ClearColor.A, record.ClearColor.G * record.ClearColor.A, record.ClearColor.B * record.ClearColor.A, record.ClearColor.A);

        var cmd = frame.Cmd;
        var size = currentFboSize;
        passViewport = record.Viewport;

        // A pass that is drawn in a part of the target is cleared to nothing outside the part, and to its colour inside it
        // (unless nothing reads the rest of the target, which is then left alone, see PassRegion)
        var region = record.Region;
        if (passViewport.Enabled && !region.Enabled)
            clearColor = default;

        // The multisampled targets only live for the duration of the pass, so their contents are never needed.
        // The resolved image is in the general layout all the time.
        Span<VkImageMemoryBarrier2> barriers = stackalloc VkImageMemoryBarrier2[2];
        var barrierCount = 0;

        const VkAccessFlagBits2 anyAccess = VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit;
        if (msaaColorImage != null)
        {
            barriers[barrierCount++] = VkUtil.ImageBarrier(
                msaaColorImage.Image,
                VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutColorAttachmentOptimal,
                VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit, anyAccess,
                VkPipelineStageFlagBits2.PipelineStage2ColorAttachmentOutputBit,
                VkAccessFlagBits2.Access2ColorAttachmentReadBit | VkAccessFlagBits2.Access2ColorAttachmentWriteBit);
        }

        barriers[barrierCount++] = VkUtil.ImageBarrier(
            msaaDepthImage.Image,
            VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutDepthAttachmentOptimal,
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit, anyAccess,
            VkPipelineStageFlagBits2.PipelineStage2EarlyFragmentTestsBit | VkPipelineStageFlagBits2.PipelineStage2LateFragmentTestsBit,
            VkAccessFlagBits2.Access2DepthStencilAttachmentReadBit | VkAccessFlagBits2.Access2DepthStencilAttachmentWriteBit,
            VkImageAspectFlagBits.ImageAspectDepthBit);

        VkUtil.PipelineBarrier(
            cmd,
            VkUtil.MemoryBarrier(
                VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit, anyAccess,
                VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit, anyAccess),
            barriers[..barrierCount]);

        // Clear buffers, the depth buffer to 0 as larger values are closer
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            sType = VkStructureType.StructureTypeRenderingAttachmentInfo,
            loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear
        };
        colorAttachment.clearValue.color.float32[0] = clearColor.R;
        colorAttachment.clearValue.color.float32[1] = clearColor.G;
        colorAttachment.clearValue.color.float32[2] = clearColor.B;
        colorAttachment.clearValue.color.float32[3] = clearColor.A;

        if (msaaColorImage != null)
        {
            colorAttachment.imageView = msaaColorImage.View;
            colorAttachment.imageLayout = VkImageLayout.ImageLayoutColorAttachmentOptimal;
            colorAttachment.resolveMode = VkResolveModeFlagBits.ResolveModeAverageBit;
            colorAttachment.resolveImageView = postProcessImage1.View;
            colorAttachment.resolveImageLayout = VkImageLayout.ImageLayoutGeneral;
            colorAttachment.storeOp = VkAttachmentStoreOp.AttachmentStoreOpDontCare;
        }
        else
        {
            colorAttachment.imageView = postProcessImage1.View;
            colorAttachment.imageLayout = VkImageLayout.ImageLayoutGeneral;
            colorAttachment.storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
        }

        var depthAttachment = new VkRenderingAttachmentInfo
        {
            sType = VkStructureType.StructureTypeRenderingAttachmentInfo,
            imageView = msaaDepthImage.View,
            imageLayout = VkImageLayout.ImageLayoutDepthAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear,
            storeOp = VkAttachmentStoreOp.AttachmentStoreOpDontCare
        };
        depthAttachment.clearValue.depthStencil.depth = 0.0f;

        var renderingInfo = new VkRenderingInfo
        {
            sType = VkStructureType.StructureTypeRenderingInfo,
            renderArea = region.Enabled
                ? new VkRect2D
                {
                    offset = new VkOffset2D { x = region.X, y = size.Y - region.Bottom },
                    extent = new VkExtent2D { width = (uint) region.Width, height = (uint) region.Height }
                }
                : new VkRect2D { extent = new VkExtent2D { width = (uint) size.X, height = (uint) size.Y } },
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
            pDepthAttachment = &depthAttachment
        };
        Vk.CmdBeginRendering(cmd, &renderingInfo);

        // Not flipped, so textures have the same orientation as in OpenGL (first row at the bottom), which post-processing depends on. The frame is flipped on present.
        var part = passViewport;
        var viewport = new VkViewport
        {
            x = part.Enabled ? part.X : 0, y = part.Enabled ? size.Y - part.Bottom : 0,
            width = part.Enabled ? part.Width : size.X, height = part.Enabled ? part.Height : size.Y,
            minDepth = 0, maxDepth = 1
        };
        var scissor = renderingInfo.renderArea;
        Vk.CmdSetViewport(cmd, 0, 1, &viewport);
        Vk.CmdSetScissor(cmd, 0, 1, &scissor);

        if (part.Enabled && !region.Enabled)
        {
            var inside = new ColorRgba(record.ClearColor.R * record.ClearColor.A, record.ClearColor.G * record.ClearColor.A, record.ClearColor.B * record.ClearColor.A, record.ClearColor.A);
            var clear = new VkClearAttachment { aspectMask = VkImageAspectFlagBits.ImageAspectColorBit, colorAttachment = 0 };
            clear.clearValue.color.float32[0] = inside.R;
            clear.clearValue.color.float32[1] = inside.G;
            clear.clearValue.color.float32[2] = inside.B;
            clear.clearValue.color.float32[3] = inside.A;
            var rect = new VkClearRect
            {
                rect = new VkRect2D
                {
                    offset = new VkOffset2D { x = Math.Max(part.X, 0), y = Math.Max(size.Y - part.Bottom, 0) },
                    extent = new VkExtent2D { width = (uint) Math.Max(part.Width, 0), height = (uint) Math.Max(part.Height, 0) }
                },
                baseArrayLayer = 0,
                layerCount = 1
            };
            Vk.CmdClearAttachments(cmd, 1, &clear, 1, &rect);
        }

        if (record.OpaquePageDraws.Count + record.TransparentPageDraws.Count > 0)
        {
            var set = frame.AllocateDescriptorSet(uberSetLayout);
            WriteUberDescriptorSet(set, frame);
            Vk.CmdBindDescriptorSets(cmd, VkPipelineBindPoint.PipelineBindPointGraphics, uberPipelineLayout, 0, 1, &set, 0, null);

            if (bindlessTextures)
            {
                var textureSet = bindlessSet;
                Vk.CmdBindDescriptorSets(cmd, VkPipelineBindPoint.PipelineBindPointGraphics, uberPipelineLayout, 1, 1, &textureSet, 0, null);
            }

            var vertexBufferHandle = vertexBuffer.Buffer;
            ulong vertexBufferOffset = 0;
            Vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBufferHandle, &vertexBufferOffset);
            Vk.CmdBindIndexBuffer(cmd, indexBuffer.Buffer, 0, VkIndexType.IndexTypeUint32);

            DrawPages(frame, opaquePipeline, record.OpaquePageDraws);

            DrawPages(frame, transparentPipeline, record.TransparentPageDraws);
        }

        Vk.CmdEndRendering(cmd);

        // The resolved image is read by the post processing passes, or by the transfer if there are none
        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2ColorAttachmentOutputBit, VkAccessFlagBits2.Access2ColorAttachmentWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit | VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2ShaderReadBit | VkAccessFlagBits2.Access2TransferReadBit));
    }

    /// <summary>Makes the image that keeps what a pass drew.</summary>
    private void EnsurePassImage(int passIndex)
    {
        passImages.EnsureCount(passIndex + 1);
        if (passImages[passIndex] != null)
            return;

        var image = GpuImage.CreateGeneral(
            context, currentFboSize, ColorFormat,
            VkImageUsageFlagBits.ImageUsageSampledBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);
        passSampler ??= new GpuSampler(context, TextureSampling.Linear);

        var bindlessIndex = bindlessTextures ? TakeBindlessIndex() : -1;
        var entry = new TextureEntry(image, passSampler, false, bindlessIndex, VkImageLayout.ImageLayoutGeneral);
        if (bindlessIndex >= 0)
            WriteBindlessTexture(bindlessIndex, entry);

        passImages[passIndex] = entry;
    }

    /// <summary>Copies what a pass drew to its kept image, turned over so the first row is the top.</summary>
    private void RecordKeepPassOutput(FrameResources frame, int passIndex, GpuImage source)
    {
        var cmd = frame.Cmd;
        var target = passImages[passIndex]!.Image;

        // Whatever used the image before, in this frame or earlier ones, is done with it, and the pass is done being drawn
        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit | VkAccessFlagBits2.Access2TransferWriteBit));

        var region = new VkImageBlit2
        {
            sType = VkStructureType.StructureTypeImageBlit2,
            srcSubresource = VkUtil.ColorLayers(),
            dstSubresource = VkUtil.ColorLayers()
        };
        region.srcOffsets[0] = new VkOffset3D { x = 0, y = 0, z = 0 };
        region.srcOffsets[1] = new VkOffset3D { x = currentFboSize.X, y = currentFboSize.Y, z = 1 };
        region.dstOffsets[0] = new VkOffset3D { x = 0, y = currentFboSize.Y, z = 0 };
        region.dstOffsets[1] = new VkOffset3D { x = currentFboSize.X, y = 0, z = 1 };

        var blitInfo = new VkBlitImageInfo2
        {
            sType = VkStructureType.StructureTypeBlitImageInfo2,
            srcImage = source.Image,
            srcImageLayout = VkImageLayout.ImageLayoutGeneral,
            dstImage = target.Image,
            dstImageLayout = VkImageLayout.ImageLayoutGeneral,
            regionCount = 1,
            pRegions = &region,
            filter = VkFilter.FilterNearest
        };
        Vk.CmdBlitImage2(cmd, &blitInfo);

        // The draws of the later passes sample it, and the passes after this one write over the image it was copied from
        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit | VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit));
    }

    /// <summary>The texture with an id: a texture of the factory or a pass output.</summary>
    private TextureEntry? FindTexture(int id)
    {
        if (TextureHandle.TryGetPassIndex(id, out var passIndex))
            return passIndex < passImages.Count ? passImages[passIndex] : null;

        return id < textures.Count ? textures[id] : null;
    }

    /// <summary>Makes the layering image exist, cleared to transparent.</summary>
    private void ClearCompositeImage(FrameResources frame)
    {
        compositeImage ??= GpuImage.CreateGeneral(
            context, currentFboSize, ColorFormat,
            GpuImage.ComputeUsage | VkImageUsageFlagBits.ImageUsageTransferSrcBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);

        var cmd = frame.Cmd;

        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit));

        var clearValue = new VkClearColorValue();
        var range = VkUtil.ColorRange();
        Vk.CmdClearColorImage(cmd, compositeImage.Image, VkImageLayout.ImageLayoutGeneral, &clearValue, 1, &range);

        // The compute pass that layers a pass reads and writes it
        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit,
            VkAccessFlagBits2.Access2ShaderReadBit | VkAccessFlagBits2.Access2ShaderWriteBit));
    }

    private void WriteUberDescriptorSet(VkDescriptorSet set, FrameResources frame)
    {
        var bufferInfos = stackalloc VkDescriptorBufferInfo[StorageBufferBindingCount];
        bufferInfos[BindMultiDraw] = new VkDescriptorBufferInfo { buffer = frame.DrawItemsBuffer!.Buffer, range = ulong.MaxValue };
        bufferInfos[BindMesh] = new VkDescriptorBufferInfo { buffer = frame.MeshDrawItemBuffer!.Buffer, range = ulong.MaxValue };
        bufferInfos[BindText] = new VkDescriptorBufferInfo { buffer = frame.TextDrawItemBuffer!.Buffer, range = ulong.MaxValue };
        bufferInfos[BindGlyphs] = new VkDescriptorBufferInfo { buffer = glyphStorageBuffer.Buffer, range = ulong.MaxValue };
        bufferInfos[BindCurves] = new VkDescriptorBufferInfo { buffer = curveBuffer.Buffer, range = ulong.MaxValue };
        bufferInfos[BindCurveIndices] = new VkDescriptorBufferInfo { buffer = curveIndexBuffer.Buffer, range = ulong.MaxValue };
        bufferInfos[BindBandEntries] = new VkDescriptorBufferInfo { buffer = bandEntryBuffer.Buffer, range = ulong.MaxValue };
        bufferInfos[BindShapeEntries] = new VkDescriptorBufferInfo { buffer = shapeEntryBuffer.Buffer, range = ulong.MaxValue };

        var writes = stackalloc VkWriteDescriptorSet[StorageBufferBindingCount];
        for (var i = 0; i < StorageBufferBindingCount; i++)
        {
            writes[i] = new VkWriteDescriptorSet
            {
                sType = VkStructureType.StructureTypeWriteDescriptorSet,
                dstSet = set,
                dstBinding = (uint) i,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.DescriptorTypeStorageBuffer,
                pBufferInfo = &bufferInfos[i]
            };
        }

        Vk.UpdateDescriptorSets(context.Device, StorageBufferBindingCount, writes, 0, null);
    }

    /// <summary>Writes a page's textures to a descriptor set; empty slots get the empty texture.</summary>
    private void WritePageDescriptorSet(VkDescriptorSet set, ReadOnlySpan<int> pageTextures)
    {
        var imageInfos = stackalloc VkDescriptorImageInfo[TexturePageSize];
        for (var i = 0; i < TexturePageSize; i++)
        {
            var entry = i < pageTextures.Length ? FindTexture(pageTextures[i]) : null;

            // The empty texture is kept in the general layout, like the images the compute shaders use
            imageInfos[i] = entry is null
                ? new VkDescriptorImageInfo { sampler = emptySampler.Sampler, imageView = emptyTexture.View, imageLayout = VkImageLayout.ImageLayoutGeneral }
                : new VkDescriptorImageInfo { sampler = entry.Sampler.Sampler, imageView = entry.Image.View, imageLayout = entry.Layout };
        }

        var textureWrite = new VkWriteDescriptorSet
        {
            sType = VkStructureType.StructureTypeWriteDescriptorSet,
            dstSet = set,
            dstBinding = 0,
            descriptorCount = TexturePageSize,
            descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
            pImageInfo = imageInfos
        };
        Vk.UpdateDescriptorSets(context.Device, 1, &textureWrite, 0, null);
    }

    /// <summary>Draws the pages of a pass, binding each page's textures first unless they are bindless.</summary>
    private void DrawPages(FrameResources frame, VkPipeline pipeline, List<PageDraw> pages)
    {
        foreach (var page in pages)
        {
            if (!bindlessTextures)
            {
                var textureSet = frame.AllocateDescriptorSet(textureSetLayout);
                WritePageDescriptorSet(textureSet, page.Textures);
                Vk.CmdBindDescriptorSets(frame.Cmd, VkPipelineBindPoint.PipelineBindPointGraphics, uberPipelineLayout, 1, 1, &textureSet, 0, null);
            }

            DrawIndirect(frame, pipeline, page.FirstCommand, page.CommandCount);
        }
    }

    /// <summary>What a draw item says of its texture: the index or the slot in its page, and the flags the shader reads.</summary>
    private readonly record struct TextureFields(int Texture0, int Texture1, int Flags);

    private TextureFields TextureFieldsOf(TextureHandle texture, int slot)
    {
        if (texture.IsNone || FindTexture(texture.Id) is not { } entry)
            return new TextureFields(-1, 0, 0);

        return new TextureFields(bindlessTextures ? entry.BindlessIndex : slot, 0, entry.CrispEdges ? 1 : 0);
    }

    /// <summary>Draws <paramref name="count"/> queued draw commands from <paramref name="first"/>.</summary>
    private void DrawIndirect(FrameResources frame, VkPipeline pipeline, int first, int count)
    {
        if (count == 0)
            return;

        var cmd = frame.Cmd;
        var stride = (uint) Unsafe.SizeOf<DrawIndexedIndirectCommand>();
        var indirectBuffer = frame.DrawIndirectBuffer!.Buffer;

        Vk.CmdBindPipeline(cmd, VkPipelineBindPoint.PipelineBindPointGraphics, pipeline);

        var pushConstants = new UberPushConstants
        {
            // Set viewport-size uniform for sub-pixel dilation in the text vertex shader
            ViewportSize = passViewport.Enabled ? new Vector2(passViewport.Width, passViewport.Height) : new Vector2(currentFboSize.X, currentFboSize.Y),
            DrawBase = (uint) first
        };

        if (context.SupportsMultiDrawIndirect)
        {
            // gl_DrawID counts from 0 in every draw call, shift it to where this pass starts
            Vk.CmdPushConstants(cmd, uberPipelineLayout, VkShaderStageFlagBits.ShaderStageVertexBit, 0, (uint) sizeof(UberPushConstants), &pushConstants);
            Vk.CmdDrawIndexedIndirect(cmd, indirectBuffer, (ulong) (first * stride), (uint) count, stride);
            return;
        }

        // Without multi draw indirect, draw one by one (gl_DrawID is always 0 then)
        for (var i = 0; i < count; i++)
        {
            pushConstants.DrawBase = (uint) (first + i);
            Vk.CmdPushConstants(cmd, uberPipelineLayout, VkShaderStageFlagBits.ShaderStageVertexBit, 0, (uint) sizeof(UberPushConstants), &pushConstants);
            Vk.CmdDrawIndexedIndirect(cmd, indirectBuffer, (ulong) ((first + i) * stride), 1, stride);
        }
    }

    /// <summary>
    /// Post-processes the part of the target that a pass was drawn in (the first post-processing image) as a picture of its own, and puts the
    /// result back in its place. The effects work in the coordinates of what they are given (the middle of the lens, the edge of the vignette,
    /// the stretch of the glitch), so they are given the part, not the whole target that the part is only a piece of.
    /// </summary>
    private GpuImage ProcessInViewport(FrameResources frame, PostProcessingState state, ClipRect viewport, bool layered)
    {
        var left = Math.Clamp(viewport.X, 0, currentFboSize.X);
        var top = Math.Clamp(viewport.Y, 0, currentFboSize.Y);
        var right = Math.Clamp(viewport.Right, left, currentFboSize.X);
        var bottom = Math.Clamp(viewport.Bottom, top, currentFboSize.Y);
        var size = new Vector2i(right - left, bottom - top);
        if (size.X <= 0 || size.Y <= 0)
            return postProcessImage1;

        if (viewportImage1 == null || viewportImage1.Size != size)
        {
            if (viewportImage1 != null)
            {
                // Earlier frames might still be using them
                frame.Defer(viewportImage1);
                frame.Defer(viewportImage2!);
            }

            const VkImageUsageFlagBits usage =
                GpuImage.ComputeUsage | VkImageUsageFlagBits.ImageUsageTransferSrcBit | VkImageUsageFlagBits.ImageUsageTransferDstBit;
            viewportImage1 = GpuImage.CreateGeneral(context, size, ColorFormat, usage);
            viewportImage2 = GpuImage.CreateGeneral(context, size, ColorFormat, usage);
        }

        // Images count their rows from the bottom
        var y = currentFboSize.Y - bottom;
        RecordBlit(frame, postProcessImage1, new Vector2i(left, y), viewportImage1, Vector2i.Zero, size);
        var result = HandlePostProcessing(frame, state, viewportImage1, viewportImage2!, layered, size);
        RecordBlit(frame, result, Vector2i.Zero, postProcessImage1, new Vector2i(left, y), size);
        return postProcessImage1;
    }

    /// <summary>Copies a part of an image (from <paramref name="from"/>, of <paramref name="size"/>) to a place of another, between the work before it and the work after it.</summary>
    private void RecordBlit(FrameResources frame, GpuImage source, Vector2i from, GpuImage target, Vector2i to, Vector2i size)
    {
        var cmd = frame.Cmd;
        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit | VkAccessFlagBits2.Access2TransferWriteBit));

        var region = new VkImageBlit2
        {
            sType = VkStructureType.StructureTypeImageBlit2,
            srcSubresource = VkUtil.ColorLayers(),
            dstSubresource = VkUtil.ColorLayers()
        };
        region.srcOffsets[0] = new VkOffset3D { x = from.X, y = from.Y, z = 0 };
        region.srcOffsets[1] = new VkOffset3D { x = from.X + size.X, y = from.Y + size.Y, z = 1 };
        region.dstOffsets[0] = new VkOffset3D { x = to.X, y = to.Y, z = 0 };
        region.dstOffsets[1] = new VkOffset3D { x = to.X + size.X, y = to.Y + size.Y, z = 1 };

        var blitInfo = new VkBlitImageInfo2
        {
            sType = VkStructureType.StructureTypeBlitImageInfo2,
            srcImage = source.Image,
            srcImageLayout = VkImageLayout.ImageLayoutGeneral,
            dstImage = target.Image,
            dstImageLayout = VkImageLayout.ImageLayoutGeneral,
            regionCount = 1,
            pRegions = &region,
            filter = VkFilter.FilterNearest
        };
        Vk.CmdBlitImage2(cmd, &blitInfo);

        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit | VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit));
    }

    private GpuImage HandlePostProcessing(FrameResources frame, PostProcessingState state, GpuImage image1, GpuImage image2, bool skipIfNoFinalEffects = false, Vector2i size = default)
    {
        if (size.X == 0 || size.Y == 0)
            size = currentFboSize;

        if (depthOfField.Process(frame, size, state.DepthOfField.Radius, image1, image2))
            Swap(ref image1, ref image2);

        if (legacyBloom.Process(frame, size, state.LegacyBloom.Intensity, state.LegacyBloom.Diffusion, state.LegacyBloom.Color, image1, image2))
            Swap(ref image1, ref image2);

        if (universalBloom.Process(frame, size, state.UniversalBloom.Intensity, state.UniversalBloom.Diffusion, state.UniversalBloom.Color, image1, image2))
            Swap(ref image1, ref image2);

        if (glitch.Process(frame, size, state.Time, state.Glitch.Speed, state.Glitch.Intensity, state.Glitch.Amount, state.Glitch.StretchMultiplier, image1, image2))
            Swap(ref image1, ref image2);

        if (grain.Process(frame, size, state.Time, state.Grain.Colored, state.Grain.Intensity, state.Grain.Size, state.Grain.LuminanceContribution, image1, image2))
            Swap(ref image1, ref image2);

        if ((!skipIfNoFinalEffects || state.HasFinalEffects) && uberPost.Process(
                frame,
                size,
                state.HueShift.Angle,
                state.LensDistortion.Intensity, state.LensDistortion.Center,
                state.ChromaticAberration.Intensity,
                state.Vignette.Center, state.Vignette.Intensity, state.Vignette.Rounded, state.Vignette.Roundness, state.Vignette.Smoothness, state.Vignette.Color, state.Vignette.Mode,
                state.Gradient.Color1, state.Gradient.Color2, state.Gradient.Intensity, state.Gradient.Rotation, state.Gradient.Mode,
                image1, image2))
            Swap(ref image1, ref image2);

        return image1;
    }

    #endregion

    #region Output

    /// <summary>Copies the finished frame, centered, into the swapchain image and prepares it for presentation.</summary>
    private void RecordPresent(FrameResources frame, GpuImage source, Vector2i size, VkImage target, ColorRgba clearColor)
    {
        var cmd = frame.Cmd;
        var framebufferSize = swapchain!.Size;

        // The swapchain image is ours when the acquire semaphore is signaled, which the submission waits for
        // before the first transfer
        VkUtil.PipelineBarrier(cmd, VkUtil.ImageBarrier(
            target,
            VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutTransferDstOptimal,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2None,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit));

        var blitSize = new Vector2i(Math.Min(size.X, framebufferSize.X), Math.Min(size.Y, framebufferSize.Y));
        var offset = (framebufferSize - blitSize) / 2;

        // Clear the swapchain image, which is only needed for the bars around a frame that does not fill it
        if (blitSize != framebufferSize)
        {
            var clearValue = new VkClearColorValue();
            clearValue.float32[0] = clearColor.R;
            clearValue.float32[1] = clearColor.G;
            clearValue.float32[2] = clearColor.B;
            clearValue.float32[3] = clearColor.A;
            var range = VkUtil.ColorRange();
            Vk.CmdClearColorImage(cmd, target, VkImageLayout.ImageLayoutTransferDstOptimal, &clearValue, 1, &range);

            VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
                VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
                VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit));
        }

        var region = new VkImageBlit2
        {
            sType = VkStructureType.StructureTypeImageBlit2,
            srcSubresource = VkUtil.ColorLayers(),
            dstSubresource = VkUtil.ColorLayers()
        };
        region.srcOffsets[0] = new VkOffset3D { x = 0, y = 0, z = 0 };
        region.srcOffsets[1] = new VkOffset3D { x = blitSize.X, y = blitSize.Y, z = 1 };
        // A frame has its first row at the bottom, a swapchain image has it at the top: flip while copying
        region.dstOffsets[0] = new VkOffset3D { x = offset.X, y = offset.Y + blitSize.Y, z = 0 };
        region.dstOffsets[1] = new VkOffset3D { x = offset.X + blitSize.X, y = offset.Y, z = 1 };

        var blitInfo = new VkBlitImageInfo2
        {
            sType = VkStructureType.StructureTypeBlitImageInfo2,
            srcImage = source.Image,
            srcImageLayout = VkImageLayout.ImageLayoutGeneral,
            dstImage = target,
            dstImageLayout = VkImageLayout.ImageLayoutTransferDstOptimal,
            regionCount = 1,
            pRegions = &region,
            filter = VkFilter.FilterNearest
        };
        Vk.CmdBlitImage2(cmd, &blitInfo);

        VkUtil.PipelineBarrier(cmd, VkUtil.ImageBarrier(
            target,
            VkImageLayout.ImageLayoutTransferDstOptimal, VkImageLayout.ImageLayoutPresentSrcKhr,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2None, VkAccessFlagBits2.Access2None));
    }

    /// <summary>Copies the finished frame into a buffer the CPU can read, as 8-bit RGBA.</summary>
    private void RecordCapture(FrameResources frame, GpuImage source, Vector2i size)
    {
        var cmd = frame.Cmd;

        if (captureImage == null || captureImage.Size != size)
        {
            if (captureImage != null)
                frame.Defer(captureImage);
            if (readbackBuffer != null)
                frame.Defer(readbackBuffer);

            captureImage = new GpuImage(
                context, size, VkFormat.FormatR8g8b8a8Unorm,
                VkImageUsageFlagBits.ImageUsageTransferSrcBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);
            readbackBuffer = new GpuBuffer(
                context, (ulong) size.X * (ulong) size.Y * 4,
                VkBufferUsageFlagBits.BufferUsageTransferDstBit, MemoryUsage.Readback);
        }

        VkUtil.PipelineBarrier(cmd, VkUtil.ImageBarrier(
            captureImage.Image,
            VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutTransferDstOptimal,
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit));

        // Converts to 8 bits per channel, and turns the frame over: it has its first row at the bottom, and the receiver is given raster order
        var region = new VkImageBlit2
        {
            sType = VkStructureType.StructureTypeImageBlit2,
            srcSubresource = VkUtil.ColorLayers(),
            dstSubresource = VkUtil.ColorLayers()
        };
        region.srcOffsets[1] = new VkOffset3D { x = size.X, y = size.Y, z = 1 };
        region.dstOffsets[0] = new VkOffset3D { x = 0, y = size.Y, z = 0 };
        region.dstOffsets[1] = new VkOffset3D { x = size.X, y = 0, z = 1 };

        var blitInfo = new VkBlitImageInfo2
        {
            sType = VkStructureType.StructureTypeBlitImageInfo2,
            srcImage = source.Image,
            srcImageLayout = VkImageLayout.ImageLayoutGeneral,
            dstImage = captureImage.Image,
            dstImageLayout = VkImageLayout.ImageLayoutTransferDstOptimal,
            regionCount = 1,
            pRegions = &region,
            filter = VkFilter.FilterNearest
        };
        Vk.CmdBlitImage2(cmd, &blitInfo);

        VkUtil.PipelineBarrier(cmd, VkUtil.ImageBarrier(
            captureImage.Image,
            VkImageLayout.ImageLayoutTransferDstOptimal, VkImageLayout.ImageLayoutTransferSrcOptimal,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferReadBit));

        var copy = new VkBufferImageCopy
        {
            imageSubresource = VkUtil.ColorLayers(),
            imageExtent = new VkExtent3D { width = (uint) size.X, height = (uint) size.Y, depth = 1 }
        };
        Vk.CmdCopyImageToBuffer(cmd, captureImage.Image, VkImageLayout.ImageLayoutTransferSrcOptimal, readbackBuffer!.Buffer, 1, &copy);

        VkUtil.PipelineBarrier(cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2HostBit, VkAccessFlagBits2.Access2HostReadBit));
    }

    #endregion

    #region Resources

    private void UpdateVulkanData(FrameResources frame, Vector2i size)
    {
        UpdateMeshData(frame);
        UpdateTextureData(frame);
        UpdateFontData(frame);
        UpdateTextData(frame);
        frame.FlushBufferUploads();
        UpdateFboData(frame, size);
    }

    private void UpdateMeshData(FrameResources frame)
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
                        vertexBuffer = GrowBuffer(frame, vertexBuffer, (ulong) (newCapacity * Unsafe.SizeOf<Vertex>()));

                        logger.LogInformation("Mesh vertex buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });

                    var idxAlloc = indexBufferAllocator.Allocate(indices.Length, (oldCapacity, newCapacity) =>
                    {
                        indexBuffer = GrowBuffer(frame, indexBuffer, (ulong) (newCapacity * sizeof(int)));

                        logger.LogInformation("Mesh index buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });

                    frame.UploadToBuffer(vertexBuffer, (ulong) (vtxAlloc.Offset * Unsafe.SizeOf<Vertex>()), MemoryMarshal.AsBytes(vertices));
                    frame.UploadToBuffer(indexBuffer, (ulong) (idxAlloc.Offset * sizeof(int)), MemoryMarshal.AsBytes(indices));

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

    private void UpdateTextureData(FrameResources frame)
    {
        while (textureLifecycleCommands.TryDequeue(out var command))
        {
            switch (command.Type)
            {
                case LifecycleCommandType.Create:
                {
                    Debug.Assert(command.Data.HasValue);

                    var (width, height, pixelData, sampling) = command.Data.Value;

                    var image = new GpuImage(
                        context, new Vector2i(width, height), VkFormat.FormatR8g8b8a8Unorm,
                        VkImageUsageFlagBits.ImageUsageSampledBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);
                    frame.UploadTexture(image, pixelData);

                    if (!samplers.TryGetValue(sampling, out var sampler))
                        samplers[sampling] = sampler = new GpuSampler(context, sampling);

                    var bindlessIndex = bindlessTextures ? TakeBindlessIndex() : -1;
                    var entry = new TextureEntry(image, sampler, sampling.IsCrisp, bindlessIndex);
                    if (bindlessIndex >= 0)
                        WriteBindlessTexture(bindlessIndex, entry);

                    textures.EnsureCount(command.Id + 1);
                    textures[command.Id] = entry;
                    break;
                }
                case LifecycleCommandType.Destroy:
                {
                    // Earlier frames might still be sampling it, and be reading its index
                    var entry = textures[command.Id];
                    if (entry != null)
                    {
                        frame.Defer(entry.Image);
                        if (entry.BindlessIndex >= 0)
                            frame.Defer(new BindlessIndexRelease(this, entry.BindlessIndex));
                        textures[command.Id] = null;
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Gives an index back to the texture array once the frames that might have drawn the texture that had it are done.</summary>
    private sealed class BindlessIndexRelease(Renderer renderer, int index) : IDisposable
    {
        public void Dispose() => renderer.freeBindlessIndices.Push(index);
    }

    private int TakeBindlessIndex()
    {
        if (freeBindlessIndices.TryPop(out var index))
            return index;

        if (nextBindlessIndex >= context.BindlessTextureCapacity)
            throw new InvalidOperationException($"There are more than {context.BindlessTextureCapacity} textures");

        return nextBindlessIndex++;
    }

    /// <summary>Writes a texture to its index of the array of all of them. Draws that do not name it do not read the descriptor.</summary>
    private void WriteBindlessTexture(int index, TextureEntry entry)
    {
        var imageInfo = new VkDescriptorImageInfo
        {
            sampler = entry.Sampler.Sampler,
            imageView = entry.Image.View,
            imageLayout = entry.Layout
        };
        var write = new VkWriteDescriptorSet
        {
            sType = VkStructureType.StructureTypeWriteDescriptorSet,
            dstSet = bindlessSet,
            dstBinding = 0,
            dstArrayElement = (uint) index,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
            pImageInfo = &imageInfo
        };
        Vk.UpdateDescriptorSets(context.Device, 1, &write, 0, null);
    }

    private void UpdateFontData(FrameResources frame)
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

        ReplaceStorageBuffer(frame, ref curveBuffer, MemoryMarshal.AsBytes(curves));
        ReplaceStorageBuffer(frame, ref curveIndexBuffer, MemoryMarshal.AsBytes(curveIndices));
        ReplaceStorageBuffer(frame, ref bandEntryBuffer, MemoryMarshal.AsBytes(bandEntries));
        ReplaceStorageBuffer(frame, ref shapeEntryBuffer, MemoryMarshal.AsBytes(gpuShapeEntries));

        logger.LogInformation(
            "Font buffers updated, registered {Curves} curves, {CurveIndices} curve indices, {Bands} band entries, {Shapes} shape entries",
            resources.FontCurves.Length,
            resources.FontCurveIndices.Length,
            resources.FontBandEntries.Length,
            resources.FontShapeEntries.Length);
    }

    private void UpdateTextData(FrameResources frame)
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
                        glyphStorageBuffer = GrowBuffer(frame, glyphStorageBuffer, (ulong) (newCapacity * Unsafe.SizeOf<GpuRenderGlyph>()));

                        logger.LogInformation("Glyph buffer reallocated from {OldCapacity} to {NewCapacity}", oldCapacity, newCapacity);
                    });

                    frame.UploadToBuffer(
                        glyphStorageBuffer,
                        (ulong) (glyphAlloc.Offset * Unsafe.SizeOf<GpuRenderGlyph>()),
                        MemoryMarshal.AsBytes<GpuRenderGlyph>(renderGlyphs));

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

    private void UpdateFboData(FrameResources frame, Vector2i size)
    {
        if (size == currentFboSize)
            return;

        if (size.X * size.Y == 0)
            return;

        // The old targets might still be in use by earlier frames
        CreateRenderTargets(size, frame);

        logger.LogInformation("Framebuffer size updated, now at {Width}x{Height}", currentFboSize.X, currentFboSize.Y);
    }

    /// <summary>Creates the render targets, replacing existing ones. The old ones go to <paramref name="frame"/> to be destroyed when the GPU is done, or are destroyed at once if there is none.</summary>
    private void CreateRenderTargets(Vector2i size, FrameResources? frame)
    {
        if (frame != null)
            DeferRenderTargets(frame);
        else
            DisposeRenderTargets();

        // Multisampled color and depth targets, the color resolves to the first post processing image
        msaaColorImage = context.MsaaSamples == VkSampleCountFlagBits.SampleCount1Bit
            ? null
            : new GpuImage(context, size, ColorFormat, VkImageUsageFlagBits.ImageUsageColorAttachmentBit, context.MsaaSamples);

        msaaDepthImage = new GpuImage(
            context, size, DepthFormat, VkImageUsageFlagBits.ImageUsageDepthStencilAttachmentBit,
            context.MsaaSamples, VkImageAspectFlagBits.ImageAspectDepthBit);

        const VkImageUsageFlagBits postProcessUsage =
            GpuImage.ComputeUsage |
            VkImageUsageFlagBits.ImageUsageColorAttachmentBit |
            VkImageUsageFlagBits.ImageUsageTransferSrcBit |
            VkImageUsageFlagBits.ImageUsageTransferDstBit;
        postProcessImage1 = GpuImage.CreateGeneral(context, size, ColorFormat, postProcessUsage);
        postProcessImage2 = GpuImage.CreateGeneral(context, size, ColorFormat, postProcessUsage);

        currentFboSize = size;
    }

    private void DeferRenderTargets(FrameResources frame)
    {
        if (msaaColorImage != null)
            frame.Defer(msaaColorImage);
        frame.Defer(msaaDepthImage);
        frame.Defer(postProcessImage1);
        frame.Defer(postProcessImage2);
        if (compositeImage != null)
            frame.Defer(compositeImage);
        compositeImage = null;

        // Earlier frames might still be sampling what the passes drew, and be reading the indices of the images
        foreach (var passImage in passImages)
        {
            if (passImage == null)
                continue;

            frame.Defer(passImage.Image);
            if (passImage.BindlessIndex >= 0)
                frame.Defer(new BindlessIndexRelease(this, passImage.BindlessIndex));
        }

        passImages.Clear();
    }

    private void DisposeRenderTargets()
    {
        msaaColorImage?.Dispose();
        msaaDepthImage?.Dispose();
        postProcessImage1?.Dispose();
        postProcessImage2?.Dispose();
        viewportImage1?.Dispose();
        viewportImage2?.Dispose();
        viewportImage1 = viewportImage2 = null;
        compositeImage?.Dispose();
        compositeImage = null;

        foreach (var passImage in passImages)
            passImage?.Image.Dispose();
        passImages.Clear();
    }

    private GpuBuffer CreateStorageBuffer(ulong size)
        => new(context, size, VkBufferUsageFlagBits.BufferUsageStorageBufferBit | ResizableBufferUsage, MemoryUsage.DeviceLocal);

    /// <summary>Makes a bigger copy of the buffer with the same contents; the old one is destroyed once the frame is done.</summary>
    private GpuBuffer GrowBuffer(FrameResources frame, GpuBuffer buffer, ulong newSize)
    {
        var usage = buffer == vertexBuffer
            ? VkBufferUsageFlagBits.BufferUsageVertexBufferBit
            : buffer == indexBuffer
                ? VkBufferUsageFlagBits.BufferUsageIndexBufferBit
                : VkBufferUsageFlagBits.BufferUsageStorageBufferBit;
        var newBuffer = new GpuBuffer(context, newSize, usage | ResizableBufferUsage, MemoryUsage.DeviceLocal);

        // The old buffer might have been written to earlier in this frame, which has to finish before it is copied
        frame.TransferBarrier();
        frame.CopyBuffer(buffer, newBuffer, buffer.Size);
        // Later uploads might touch the part that was copied
        frame.TransferBarrier();
        frame.Defer(buffer);

        return newBuffer;
    }

    /// <summary>Replaces a storage buffer with a new one that holds <paramref name="data"/>.</summary>
    private void ReplaceStorageBuffer(FrameResources frame, ref GpuBuffer buffer, ReadOnlySpan<byte> data)
    {
        frame.Defer(buffer);
        buffer = CreateStorageBuffer(Math.Max((ulong) data.Length, MinStorageBufferSize));
        frame.UploadToBuffer(buffer, 0, data);
    }

    #endregion

    #region Pipelines

    private void CreateUberPipelines(ResourceLoader loader)
    {
        var bindings = stackalloc VkDescriptorSetLayoutBinding[StorageBufferBindingCount];
        for (var i = 0; i < StorageBufferBindingCount; i++)
        {
            bindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = (uint) i,
                descriptorType = VkDescriptorType.DescriptorTypeStorageBuffer,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlagBits.ShaderStageVertexBit | VkShaderStageFlagBits.ShaderStageFragmentBit
            };
        }

        var setLayoutInfo = new VkDescriptorSetLayoutCreateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorSetLayoutCreateInfo,
            bindingCount = StorageBufferBindingCount,
            pBindings = bindings
        };
        VkDescriptorSetLayout setLayout;
        VkUtil.Check(Vk.CreateDescriptorSetLayout(context.Device, &setLayoutInfo, null, &setLayout), "vkCreateDescriptorSetLayout");
        uberSetLayout = setLayout;

        // Descriptor set 1 has the textures: one array of all of them (bindless) or the page's
        var textureBinding = new VkDescriptorSetLayoutBinding
        {
            binding = 0,
            descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
            descriptorCount = bindlessTextures ? (uint) context.BindlessTextureCapacity : TexturePageSize,
            stageFlags = VkShaderStageFlagBits.ShaderStageFragmentBit
        };
        var bindingFlags = VkDescriptorBindingFlagBits.DescriptorBindingPartiallyBoundBit | VkDescriptorBindingFlagBits.DescriptorBindingUpdateAfterBindBit;
        var bindingFlagsInfo = new VkDescriptorSetLayoutBindingFlagsCreateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorSetLayoutBindingFlagsCreateInfo,
            bindingCount = 1,
            pBindingFlags = &bindingFlags
        };
        var textureSetLayoutInfo = new VkDescriptorSetLayoutCreateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorSetLayoutCreateInfo,
            pNext = bindlessTextures ? &bindingFlagsInfo : null,
            flags = bindlessTextures ? VkDescriptorSetLayoutCreateFlagBits.DescriptorSetLayoutCreateUpdateAfterBindPoolBit : 0,
            bindingCount = 1,
            pBindings = &textureBinding
        };
        VkDescriptorSetLayout texturesLayout;
        VkUtil.Check(Vk.CreateDescriptorSetLayout(context.Device, &textureSetLayoutInfo, null, &texturesLayout), "vkCreateDescriptorSetLayout");
        textureSetLayout = texturesLayout;

        if (bindlessTextures)
        {
            var poolSize = new VkDescriptorPoolSize
            {
                type = VkDescriptorType.DescriptorTypeCombinedImageSampler,
                descriptorCount = (uint) context.BindlessTextureCapacity
            };
            var poolInfo = new VkDescriptorPoolCreateInfo
            {
                sType = VkStructureType.StructureTypeDescriptorPoolCreateInfo,
                flags = VkDescriptorPoolCreateFlagBits.DescriptorPoolCreateUpdateAfterBindBit,
                maxSets = 1,
                poolSizeCount = 1,
                pPoolSizes = &poolSize
            };
            VkDescriptorPool pool;
            VkUtil.Check(Vk.CreateDescriptorPool(context.Device, &poolInfo, null, &pool), "vkCreateDescriptorPool");
            bindlessPool = pool;

            var allocInfo = new VkDescriptorSetAllocateInfo
            {
                sType = VkStructureType.StructureTypeDescriptorSetAllocateInfo,
                descriptorPool = pool,
                descriptorSetCount = 1,
                pSetLayouts = &texturesLayout
            };
            VkDescriptorSet set;
            VkUtil.Check(Vk.AllocateDescriptorSets(context.Device, &allocInfo, &set), "vkAllocateDescriptorSets");
            bindlessSet = set;
        }

        var setLayouts = stackalloc VkDescriptorSetLayout[] { setLayout, texturesLayout };
        var pushRange = new VkPushConstantRange
        {
            stageFlags = VkShaderStageFlagBits.ShaderStageVertexBit,
            offset = 0,
            size = (uint) sizeof(UberPushConstants)
        };
        var layoutInfo = new VkPipelineLayoutCreateInfo
        {
            sType = VkStructureType.StructureTypePipelineLayoutCreateInfo,
            setLayoutCount = 2,
            pSetLayouts = setLayouts,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange
        };
        VkPipelineLayout pipelineLayout;
        VkUtil.Check(Vk.CreatePipelineLayout(context.Device, &layoutInfo, null, &pipelineLayout), "vkCreatePipelineLayout");
        uberPipelineLayout = pipelineLayout;

        var vertexModule = ShaderUtil.CreateModule(context, loader, "Uber.vert");
        var fragmentModule = ShaderUtil.CreateModule(context, loader, bindlessTextures ? "Uber.frag" : "Uber.paged.frag");
        try
        {
            opaquePipeline = CreateUberPipeline(vertexModule, fragmentModule, transparent: false);
            transparentPipeline = CreateUberPipeline(vertexModule, fragmentModule, transparent: true);
        }
        finally
        {
            Vk.DestroyShaderModule(context.Device, vertexModule, null);
            Vk.DestroyShaderModule(context.Device, fragmentModule, null);
        }
    }

    private VkPipeline CreateUberPipeline(VkShaderModule vertexModule, VkShaderModule fragmentModule, bool transparent)
    {
        fixed (byte* entryPoint = "main"u8)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[]
            {
                new VkPipelineShaderStageCreateInfo
                {
                    sType = VkStructureType.StructureTypePipelineShaderStageCreateInfo,
                    stage = VkShaderStageFlagBits.ShaderStageVertexBit,
                    module = vertexModule,
                    pName = entryPoint
                },
                new VkPipelineShaderStageCreateInfo
                {
                    sType = VkStructureType.StructureTypePipelineShaderStageCreateInfo,
                    stage = VkShaderStageFlagBits.ShaderStageFragmentBit,
                    module = fragmentModule,
                    pName = entryPoint
                }
            };

            var vertexBindings = stackalloc VkVertexInputBindingDescription[1];
            vertexBindings[0] = new VkVertexInputBindingDescription 
            {
                binding = 0,
                stride = (uint) Unsafe.SizeOf<Vertex>(),
                inputRate = VkVertexInputRate.VertexInputRateVertex
            }; // one vertex buffer binding
            
            var vertexAttributes = stackalloc VkVertexInputAttributeDescription[2];
            vertexAttributes[0] = new VkVertexInputAttributeDescription 
            {
                location = 0,
                binding = 0,
                format = VkFormat.FormatR32g32Sfloat,
                offset = 0
            }; // aPos
            
            vertexAttributes[1] = new VkVertexInputAttributeDescription 
            {
                location = 1,
                binding = 0,
                format = VkFormat.FormatR32g32Sfloat,
                offset = 8
            }; // aUv
            
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = vertexBindings,
                vertexAttributeDescriptionCount = 2,
                pVertexAttributeDescriptions = vertexAttributes
            };

            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineInputAssemblyStateCreateInfo,
                topology = VkPrimitiveTopology.PrimitiveTopologyTriangleList
            };

            // Viewport and scissor are set when drawing
            var viewportState = new VkPipelineViewportStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineViewportStateCreateInfo,
                viewportCount = 1,
                scissorCount = 1
            };

            var rasterization = new VkPipelineRasterizationStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineRasterizationStateCreateInfo,
                polygonMode = VkPolygonMode.PolygonModeFill,
                cullMode = VkCullModeFlagBits.CullModeNone,
                frontFace = VkFrontFace.FrontFaceCounterClockwise,
                lineWidth = 1.0f
            };

            var multisample = new VkPipelineMultisampleStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineMultisampleStateCreateInfo,
                rasterizationSamples = context.MsaaSamples
            };

            // Larger depth is closer. The transparent pass tests against the opaque geometry but doesn't write
            var depthStencil = new VkPipelineDepthStencilStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineDepthStencilStateCreateInfo,
                depthTestEnable = VkUtil.True,
                depthWriteEnable = transparent ? VkUtil.False : VkUtil.True,
                depthCompareOp = VkCompareOp.CompareOpGreater
            };

            var blendAttachment = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = transparent ? VkUtil.True : VkUtil.False,
                // The shaders output premultiplied alpha
                srcColorBlendFactor = VkBlendFactor.BlendFactorOne,
                dstColorBlendFactor = VkBlendFactor.BlendFactorOneMinusSrcAlpha,
                colorBlendOp = VkBlendOp.BlendOpAdd,
                srcAlphaBlendFactor = VkBlendFactor.BlendFactorOne,
                dstAlphaBlendFactor = VkBlendFactor.BlendFactorOneMinusSrcAlpha,
                alphaBlendOp = VkBlendOp.BlendOpAdd,
                colorWriteMask =
                    VkColorComponentFlagBits.ColorComponentRBit | VkColorComponentFlagBits.ColorComponentGBit |
                    VkColorComponentFlagBits.ColorComponentBBit | VkColorComponentFlagBits.ColorComponentABit
            };
            var colorBlend = new VkPipelineColorBlendStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineColorBlendStateCreateInfo,
                attachmentCount = 1,
                pAttachments = &blendAttachment
            };

            var dynamicStates = stackalloc VkDynamicState[]
            {
                VkDynamicState.DynamicStateViewport,
                VkDynamicState.DynamicStateScissor
            };
            var dynamicState = new VkPipelineDynamicStateCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineDynamicStateCreateInfo,
                dynamicStateCount = 2,
                pDynamicStates = dynamicStates
            };

            // There are no render passes, the formats of the attachments are all the pipeline needs to know
            var colorFormat = ColorFormat;
            var rendering = new VkPipelineRenderingCreateInfo
            {
                sType = VkStructureType.StructureTypePipelineRenderingCreateInfo,
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &colorFormat,
                depthAttachmentFormat = DepthFormat
            };

            var createInfo = new VkGraphicsPipelineCreateInfo
            {
                sType = VkStructureType.StructureTypeGraphicsPipelineCreateInfo,
                pNext = &rendering,
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &rasterization,
                pMultisampleState = &multisample,
                pDepthStencilState = &depthStencil,
                pColorBlendState = &colorBlend,
                pDynamicState = &dynamicState,
                layout = uberPipelineLayout
            };

            VkPipeline pipeline;
            VkUtil.Check(
                Vk.CreateGraphicsPipelines(context.Device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines");
            return pipeline;
        }
    }

    #endregion

    private static void Swap<T>(ref T a, ref T b)
    {
        (a, b) = (b, a);
    }
}
