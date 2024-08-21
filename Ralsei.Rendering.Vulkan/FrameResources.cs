using OpenTK.Graphics.Vulkan;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// Everything that is needed to record and submit one frame, and that can't be shared between
/// frames that are in flight at the same time. The renderer cycles through a few of these.
/// </summary>
internal sealed unsafe class FrameResources : IDisposable
{
    private const uint MaxDescriptorSets = 512;

    public VulkanContext Context { get; }

    public VkCommandBuffer Cmd { get; }
    public VkFence Fence { get; }

    /// <summary>Signaled when the swapchain image for this frame is ready to be rendered to.</summary>
    public VkSemaphore ImageAvailable { get; }

    public StagingArena Staging { get; }

    // Per-frame draw data, written by the CPU and read by the vertex shader.
    public GpuBuffer? DrawItemsBuffer { get; private set; }
    public GpuBuffer? MeshDrawItemBuffer { get; private set; }
    public GpuBuffer? TextDrawItemBuffer { get; private set; }

    /// <summary>Per-frame draw commands (VkDrawIndexedIndirectCommand[]).</summary>
    public GpuBuffer? DrawIndirectBuffer { get; private set; }

    /// <summary>True if buffer data has been copied from staging memory this frame.</summary>
    public bool HasPendingBufferUploads { get; private set; }

    private readonly VkCommandPool commandPool;
    private readonly VkDescriptorPool descriptorPool;
    private readonly List<IDisposable> garbage = [];

    public FrameResources(VulkanContext context)
    {
        Context = context;
        Staging = new StagingArena(context);

        var poolInfo = new VkCommandPoolCreateInfo
        {
            sType = VkStructureType.StructureTypeCommandPoolCreateInfo,
            queueFamilyIndex = context.QueueFamily
        };
        VkCommandPool pool;
        VkUtil.Check(Vk.CreateCommandPool(context.Device, &poolInfo, null, &pool), "vkCreateCommandPool");
        commandPool = pool;

        var allocInfo = new VkCommandBufferAllocateInfo
        {
            sType = VkStructureType.StructureTypeCommandBufferAllocateInfo,
            commandPool = commandPool,
            level = VkCommandBufferLevel.CommandBufferLevelPrimary,
            commandBufferCount = 1
        };
        VkCommandBuffer cmd;
        VkUtil.Check(Vk.AllocateCommandBuffers(context.Device, &allocInfo, &cmd), "vkAllocateCommandBuffers");
        Cmd = cmd;

        // Created signaled so that the first wait doesn't block
        var fenceInfo = new VkFenceCreateInfo
        {
            sType = VkStructureType.StructureTypeFenceCreateInfo,
            flags = VkFenceCreateFlagBits.FenceCreateSignaledBit
        };
        VkFence fence;
        VkUtil.Check(Vk.CreateFence(context.Device, &fenceInfo, null, &fence), "vkCreateFence");
        Fence = fence;

        var semaphoreInfo = new VkSemaphoreCreateInfo { sType = VkStructureType.StructureTypeSemaphoreCreateInfo };
        VkSemaphore semaphore;
        VkUtil.Check(Vk.CreateSemaphore(context.Device, &semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
        ImageAvailable = semaphore;

        var poolSizes = stackalloc VkDescriptorPoolSize[]
        {
            new VkDescriptorPoolSize { type = VkDescriptorType.DescriptorTypeCombinedImageSampler, descriptorCount = MaxDescriptorSets * 16 },
            new VkDescriptorPoolSize { type = VkDescriptorType.DescriptorTypeStorageImage, descriptorCount = MaxDescriptorSets },
            new VkDescriptorPoolSize { type = VkDescriptorType.DescriptorTypeStorageBuffer, descriptorCount = 64 }
        };
        var descriptorPoolInfo = new VkDescriptorPoolCreateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorPoolCreateInfo,
            maxSets = MaxDescriptorSets,
            poolSizeCount = 3,
            pPoolSizes = poolSizes
        };
        VkDescriptorPool descriptorPoolHandle;
        VkUtil.Check(Vk.CreateDescriptorPool(context.Device, &descriptorPoolInfo, null, &descriptorPoolHandle), "vkCreateDescriptorPool");
        descriptorPool = descriptorPoolHandle;
    }

    public void Dispose()
    {
        var device = Context.Device;

        ReleaseGarbage();
        Staging.Dispose();
        DrawItemsBuffer?.Dispose();
        MeshDrawItemBuffer?.Dispose();
        TextDrawItemBuffer?.Dispose();
        DrawIndirectBuffer?.Dispose();

        Vk.DestroyDescriptorPool(device, descriptorPool, null);
        Vk.DestroySemaphore(device, ImageAvailable, null);
        Vk.DestroyFence(device, Fence, null);
        Vk.DestroyCommandPool(device, commandPool, null);
    }

    /// <summary>
    /// Blocks until the GPU has finished the last submission of this frame. Once this returns,
    /// everything the frame used can be recycled.
    /// </summary>
    public void WaitForCompletion()
    {
        var fence = Fence;
        VkUtil.Check(Vk.WaitForFences(Context.Device, 1, &fence, VkUtil.True, ulong.MaxValue), "vkWaitForFences");
    }

    /// <summary>
    /// Recycles the frame's resources and starts recording its command buffer. Must only be called
    /// after <see cref="WaitForCompletion"/>, and only if the frame is going to be submitted.
    /// </summary>
    public void Begin()
    {
        var fence = Fence;
        VkUtil.Check(Vk.ResetFences(Context.Device, 1, &fence), "vkResetFences");

        ReleaseGarbage();
        Staging.Reset();
        HasPendingBufferUploads = false;

        VkUtil.Check(Vk.ResetDescriptorPool(Context.Device, descriptorPool, 0), "vkResetDescriptorPool");
        VkUtil.Check(Vk.ResetCommandPool(Context.Device, commandPool, 0), "vkResetCommandPool");

        var beginInfo = new VkCommandBufferBeginInfo
        {
            sType = VkStructureType.StructureTypeCommandBufferBeginInfo,
            flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit
        };
        VkUtil.Check(Vk.BeginCommandBuffer(Cmd, &beginInfo), "vkBeginCommandBuffer");

        // Frames are executed in submission order, but nothing else orders them: this makes everything
        // this frame does wait for everything the previous frames did (and so the other way around for
        // the resources they share, such as the post-processing images).
        VkUtil.PipelineBarrier(Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
            VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit));
    }

    public void End()
    {
        VkUtil.Check(Vk.EndCommandBuffer(Cmd), "vkEndCommandBuffer");
    }

    /// <summary>
    /// Queues a resource for destruction once the GPU is done with this frame. Resources that earlier
    /// frames might still be using must be released this way, as it can take several frames to get here.
    /// </summary>
    public void Defer(IDisposable resource)
    {
        garbage.Add(resource);
    }

    private void ReleaseGarbage()
    {
        foreach (var resource in garbage)
            resource.Dispose();
        garbage.Clear();
    }

    #region Per-frame draw buffers

    public void EnsureDrawBuffers(ulong multiDrawItemBytes, ulong meshItemBytes, ulong textItemBytes, ulong indirectBytes)
    {
        // These are only ever recorded into the command buffer after this call, and the GPU
        // is done with the previous contents, so replacing them in place is safe.
        if (DrawItemsBuffer == null || DrawItemsBuffer.Size < multiDrawItemBytes)
        {
            DrawItemsBuffer?.Dispose();
            DrawItemsBuffer = new GpuBuffer(
                Context, GrowSize(multiDrawItemBytes, DrawItemsBuffer?.Size ?? 0),
                VkBufferUsageFlagBits.BufferUsageStorageBufferBit, MemoryUsage.Dynamic);
        }
        
        if (MeshDrawItemBuffer == null || MeshDrawItemBuffer.Size < meshItemBytes)
        {
            MeshDrawItemBuffer?.Dispose();
            MeshDrawItemBuffer = new GpuBuffer(
                Context, GrowSize(meshItemBytes, MeshDrawItemBuffer?.Size ?? 0),
                VkBufferUsageFlagBits.BufferUsageStorageBufferBit, MemoryUsage.Dynamic);
        }
        
        if (TextDrawItemBuffer == null || TextDrawItemBuffer.Size < textItemBytes)
        {
            TextDrawItemBuffer?.Dispose();
            TextDrawItemBuffer = new GpuBuffer(
                Context, GrowSize(textItemBytes, TextDrawItemBuffer?.Size ?? 0),
                VkBufferUsageFlagBits.BufferUsageStorageBufferBit, MemoryUsage.Dynamic);
        }

        if (DrawIndirectBuffer == null || DrawIndirectBuffer.Size < indirectBytes)
        {
            DrawIndirectBuffer?.Dispose();
            DrawIndirectBuffer = new GpuBuffer(
                Context, GrowSize(indirectBytes, DrawIndirectBuffer?.Size ?? 0),
                VkBufferUsageFlagBits.BufferUsageIndirectBufferBit, MemoryUsage.Dynamic);
        }
    }

    private static ulong GrowSize(ulong required, ulong current)
        => Math.Max(Math.Max(required, current * 2), 64 * 1024);

    #endregion

    #region Uploads

    public void UploadToBuffer(GpuBuffer destination, ulong destinationOffset, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var staging = Staging.Allocate((ulong) data.Length);
        data.CopyTo(new Span<byte>(staging.Pointer, data.Length));

        var region = new VkBufferCopy
        {
            srcOffset = staging.Offset,
            dstOffset = destinationOffset,
            size = (ulong) data.Length
        };
        Vk.CmdCopyBuffer(Cmd, staging.Buffer, destination.Buffer, 1, &region);
        HasPendingBufferUploads = true;
    }

    /// <summary>
    /// Copies the contents of <paramref name="source"/> into <paramref name="destination"/>, which
    /// has to be larger than or as large as <paramref name="byteCount"/>.
    /// </summary>
    public void CopyBuffer(GpuBuffer source, GpuBuffer destination, ulong byteCount)
    {
        var region = new VkBufferCopy { srcOffset = 0, dstOffset = 0, size = byteCount };
        Vk.CmdCopyBuffer(Cmd, source.Buffer, destination.Buffer, 1, &region);
        HasPendingBufferUploads = true;
    }

    /// <summary>
    /// Replaces the whole contents of an image that is kept in the general layout, and makes the
    /// new contents visible to compute shaders.
    /// </summary>
    public void UploadToImage(GpuImage destination, ReadOnlySpan<byte> data)
    {
        var staging = Staging.Allocate((ulong) data.Length);
        data.CopyTo(new Span<byte>(staging.Pointer, data.Length));

        var region = new VkBufferImageCopy
        {
            bufferOffset = staging.Offset,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = VkUtil.ColorLayers(),
            imageOffset = default,
            imageExtent = new VkExtent3D { width = (uint) destination.Size.X, height = (uint) destination.Size.Y, depth = 1 }
        };
        Vk.CmdCopyBufferToImage(Cmd, staging.Buffer, destination.Image, VkImageLayout.ImageLayoutGeneral, 1, &region);

        VkUtil.PipelineBarrier(Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit, VkAccessFlagBits2.Access2ShaderReadBit));
    }

    /// <summary>
    /// Fills a texture that was just created, and leaves it in the layout that fragment shaders sample it in.
    /// </summary>
    public void UploadTexture(GpuImage destination, ReadOnlySpan<byte> data)
    {
        var staging = Staging.Allocate((ulong) data.Length);
        data.CopyTo(new Span<byte>(staging.Pointer, data.Length));

        VkUtil.PipelineBarrier(Cmd, VkUtil.ImageBarrier(
            destination.Image,
            VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutTransferDstOptimal,
            VkPipelineStageFlagBits2.PipelineStage2None, VkAccessFlagBits2.Access2None,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit));

        var region = new VkBufferImageCopy
        {
            bufferOffset = staging.Offset,
            imageSubresource = VkUtil.ColorLayers(),
            imageExtent = new VkExtent3D { width = (uint) destination.Size.X, height = (uint) destination.Size.Y, depth = 1 }
        };
        Vk.CmdCopyBufferToImage(Cmd, staging.Buffer, destination.Image, VkImageLayout.ImageLayoutTransferDstOptimal, 1, &region);

        VkUtil.PipelineBarrier(Cmd, VkUtil.ImageBarrier(
            destination.Image,
            VkImageLayout.ImageLayoutTransferDstOptimal, VkImageLayout.ImageLayoutShaderReadOnlyOptimal,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2FragmentShaderBit, VkAccessFlagBits2.Access2ShaderReadBit));
    }

    /// <summary>
    /// Makes all the buffer copies recorded so far visible to the stages that read meshes, text and fonts.
    /// </summary>
    public void FlushBufferUploads()
    {
        if (!HasPendingBufferUploads)
            return;

        VkUtil.PipelineBarrier(Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2VertexInputBit |
            VkPipelineStageFlagBits2.PipelineStage2VertexShaderBit |
            VkPipelineStageFlagBits2.PipelineStage2FragmentShaderBit,
            VkAccessFlagBits2.Access2VertexAttributeReadBit |
            VkAccessFlagBits2.Access2IndexReadBit |
            VkAccessFlagBits2.Access2ShaderReadBit));
        HasPendingBufferUploads = false;
    }

    /// <summary>
    /// Orders buffer copies against copies that come after them, for when they might touch the same bytes.
    /// </summary>
    public void TransferBarrier()
    {
        VkUtil.PipelineBarrier(Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit, VkAccessFlagBits2.Access2TransferWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2AllTransferBit,
            VkAccessFlagBits2.Access2TransferReadBit | VkAccessFlagBits2.Access2TransferWriteBit));
    }

    #endregion

    #region Compute

    /// <summary>
    /// The equivalent of glMemoryBarrier between compute passes: results written by the previous
    /// pass can be read (through textures or images) by the next one.
    /// </summary>
    /// <remarks>
    /// This deliberately uses the general SHADER_READ and SHADER_WRITE accesses. The more specific
    /// SHADER_STORAGE_WRITE as the source access did not make the writes of one pass visible to the next one reliably
    /// (on both NVIDIA and AMD, the later passes read stale data, differently on every run), while the general
    /// accesses, which include it, do.
    /// </remarks>
    public void ComputeBarrier()
    {
        VkUtil.PipelineBarrier(Cmd, VkUtil.MemoryBarrier(
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit, VkAccessFlagBits2.Access2ShaderWriteBit,
            VkPipelineStageFlagBits2.PipelineStage2ComputeShaderBit,
            VkAccessFlagBits2.Access2ShaderReadBit | VkAccessFlagBits2.Access2ShaderWriteBit));
    }

    public VkDescriptorSet AllocateDescriptorSet(VkDescriptorSetLayout layout)
    {
        var allocInfo = new VkDescriptorSetAllocateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorSetAllocateInfo,
            descriptorPool = descriptorPool,
            descriptorSetCount = 1,
            pSetLayouts = &layout
        };
        VkDescriptorSet set;
        VkUtil.Check(Vk.AllocateDescriptorSets(Context.Device, &allocInfo, &set), "vkAllocateDescriptorSets");
        return set;
    }

    #endregion
}
