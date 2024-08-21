using OpenTK.Graphics.Vulkan;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// A VkBuffer with its own dedicated memory allocation. Disposing destroys it immediately, so a buffer
/// that the GPU might still be using has to be handed to <see cref="FrameResources.Defer"/> instead.
/// </summary>
internal sealed unsafe class GpuBuffer : IDisposable
{
    public VkBuffer Buffer { get; private set; }
    public ulong Size { get; }

    /// <summary>Persistently mapped pointer, or null if the memory is not host-visible.</summary>
    public byte* Mapped { get; private set; }

    private readonly VulkanContext context;
    private VkDeviceMemory memory;

    public GpuBuffer(VulkanContext context, ulong size, VkBufferUsageFlagBits usage, MemoryUsage memoryUsage)
    {
        this.context = context;
        Size = size;

        var createInfo = new VkBufferCreateInfo
        {
            sType = VkStructureType.StructureTypeBufferCreateInfo,
            size = size,
            usage = usage,
            sharingMode = VkSharingMode.SharingModeExclusive
        };
        VkBuffer buffer;
        VkUtil.Check(Vk.CreateBuffer(context.Device, &createInfo, null, &buffer), "vkCreateBuffer");
        Buffer = buffer;

        try
        {
            VkMemoryRequirements requirements;
            Vk.GetBufferMemoryRequirements(context.Device, Buffer, &requirements);

            memory = context.AllocateMemory(requirements, memoryUsage);
            VkUtil.Check(Vk.BindBufferMemory(context.Device, Buffer, memory, 0), "vkBindBufferMemory");

            if (memoryUsage != MemoryUsage.DeviceLocal)
            {
                void* mapped;
                VkUtil.Check(Vk.MapMemory(context.Device, memory, 0, size, 0, &mapped), "vkMapMemory");
                Mapped = (byte*) mapped;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Buffer.Handle != 0)
        {
            Vk.DestroyBuffer(context.Device, Buffer, null);
            Buffer = default;
        }

        if (memory.Handle != 0)
        {
            // Freeing the memory also unmaps it
            Vk.FreeMemory(context.Device, memory, null);
            memory = default;
            Mapped = null;
        }
    }
}
