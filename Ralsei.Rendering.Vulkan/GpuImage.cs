using OpenTK.Graphics.Vulkan;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// A 2D VkImage with its own dedicated memory allocation and a view covering the whole image.
/// Disposing destroys it immediately, so an image that the GPU might still be using has to be
/// handed to <see cref="FrameResources.Defer"/> instead.
/// </summary>
internal sealed unsafe class GpuImage : IDisposable
{
    public VkImage Image { get; private set; }
    public VkImageView View { get; private set; }
    public VkFormat Format { get; }
    public Vector2i Size { get; }

    private readonly VulkanContext context;
    private VkDeviceMemory memory;

    public GpuImage(
        VulkanContext context, Vector2i size, VkFormat format, VkImageUsageFlagBits usage,
        VkSampleCountFlagBits samples = VkSampleCountFlagBits.SampleCount1Bit,
        VkImageAspectFlagBits aspect = VkImageAspectFlagBits.ImageAspectColorBit)
    {
        this.context = context;
        Format = format;
        Size = size;

        var createInfo = new VkImageCreateInfo
        {
            sType = VkStructureType.StructureTypeImageCreateInfo,
            imageType = VkImageType.ImageType2d,
            format = format,
            extent = new VkExtent3D { width = (uint) size.X, height = (uint) size.Y, depth = 1 },
            mipLevels = 1,
            arrayLayers = 1,
            samples = samples,
            tiling = VkImageTiling.ImageTilingOptimal,
            usage = usage,
            sharingMode = VkSharingMode.SharingModeExclusive,
            initialLayout = VkImageLayout.ImageLayoutUndefined
        };
        VkImage image;
        VkUtil.Check(Vk.CreateImage(context.Device, &createInfo, null, &image), "vkCreateImage");
        Image = image;

        try
        {
            VkMemoryRequirements requirements;
            Vk.GetImageMemoryRequirements(context.Device, Image, &requirements);

            memory = context.AllocateMemory(requirements, MemoryUsage.DeviceLocal);
            VkUtil.Check(Vk.BindImageMemory(context.Device, Image, memory, 0), "vkBindImageMemory");

            // Images that are only ever copied to and from can't have a view
            const VkImageUsageFlagBits viewUsages =
                VkImageUsageFlagBits.ImageUsageSampledBit |
                VkImageUsageFlagBits.ImageUsageStorageBit |
                VkImageUsageFlagBits.ImageUsageColorAttachmentBit |
                VkImageUsageFlagBits.ImageUsageDepthStencilAttachmentBit;
            if ((usage & viewUsages) == 0)
                return;

            var viewInfo = new VkImageViewCreateInfo
            {
                sType = VkStructureType.StructureTypeImageViewCreateInfo,
                image = Image,
                viewType = VkImageViewType.ImageViewType2d,
                format = format,
                components = new VkComponentMapping
                {
                    r = VkComponentSwizzle.ComponentSwizzleIdentity,
                    g = VkComponentSwizzle.ComponentSwizzleIdentity,
                    b = VkComponentSwizzle.ComponentSwizzleIdentity,
                    a = VkComponentSwizzle.ComponentSwizzleIdentity
                },
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = aspect,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1
                }
            };
            VkImageView view;
            VkUtil.Check(Vk.CreateImageView(context.Device, &viewInfo, null, &view), "vkCreateImageView");
            View = view;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// The usage of an image that is both read and written by compute shaders.
    /// </summary>
    public const VkImageUsageFlagBits ComputeUsage =
        VkImageUsageFlagBits.ImageUsageStorageBit | VkImageUsageFlagBits.ImageUsageSampledBit;

    /// <summary>
    /// Creates an image and moves it to <see cref="VkImageLayout.ImageLayoutGeneral"/>. Images used by
    /// compute shaders are always kept in that layout, which lets them be sampled, stored to, copied
    /// to and from without any further layout transitions.
    /// </summary>
    public static GpuImage CreateGeneral(VulkanContext context, Vector2i size, VkFormat format, VkImageUsageFlagBits usage)
    {
        var image = new GpuImage(context, size, format, usage);

        context.SubmitImmediate(cmd =>
        {
            var barrier = VkUtil.ImageBarrier(
                image.Image,
                VkImageLayout.ImageLayoutUndefined, VkImageLayout.ImageLayoutGeneral,
                VkPipelineStageFlagBits2.PipelineStage2None, VkAccessFlagBits2.Access2None,
                VkPipelineStageFlagBits2.PipelineStage2AllCommandsBit,
                VkAccessFlagBits2.Access2MemoryReadBit | VkAccessFlagBits2.Access2MemoryWriteBit);
            VkUtil.PipelineBarrier(cmd, barrier);
        });

        return image;
    }

    public void Dispose()
    {
        if (View.Handle != 0)
        {
            Vk.DestroyImageView(context.Device, View, null);
            View = default;
        }

        if (Image.Handle != 0)
        {
            Vk.DestroyImage(context.Device, Image, null);
            Image = default;
        }

        if (memory.Handle != 0)
        {
            Vk.FreeMemory(context.Device, memory, null);
            memory = default;
        }
    }
}
