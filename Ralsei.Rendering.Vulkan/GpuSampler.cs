using OpenTK.Graphics.Vulkan;
using Ralsei.Core.Data;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// A VkSampler. The textures that are sampled the same way share one, and it lasts as long as the renderer.
/// </summary>
internal sealed unsafe class GpuSampler : IDisposable
{
    public VkSampler Sampler { get; private set; }

    private readonly VulkanContext context;

    public GpuSampler(
        VulkanContext context,
        TextureSampling sampling)
    {
        this.context = context;

        var createInfo = new VkSamplerCreateInfo
        {
            sType = VkStructureType.StructureTypeSamplerCreateInfo,
            minFilter = GetFilter(sampling.MinificationFilter),
            magFilter = GetFilter(sampling.MagnificationFilter),
            // The textures have no mipmaps
            mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest,
            addressModeU = GetWrapMode(sampling.WrapModeU),
            addressModeV = GetWrapMode(sampling.WrapModeV),
            addressModeW = VkSamplerAddressMode.SamplerAddressModeClampToEdge,
            borderColor = GetBorderColor(sampling.BorderColor),
            minLod = 0,
            maxLod = 0
        };
        VkSampler sampler;
        VkUtil.Check(Vk.CreateSampler(context.Device, &createInfo, null, &sampler), "vkCreateSampler");
        Sampler = sampler;
    }

    public void Dispose()
    {
        if (Sampler.Handle == 0)
            return;

        Vk.DestroySampler(context.Device, Sampler, null);
        Sampler = default;
    }

    private static VkFilter GetFilter(TextureFilteringMode filteringMode)
        => filteringMode switch
        {
            TextureFilteringMode.Nearest => VkFilter.FilterNearest,
            TextureFilteringMode.Linear or TextureFilteringMode.CrispEdges => VkFilter.FilterLinear,
            _ => throw new ArgumentOutOfRangeException(nameof(filteringMode), filteringMode, null)
        };

    private static VkSamplerAddressMode GetWrapMode(TextureAddressMode wrapMode)
        => wrapMode switch
        {
            TextureAddressMode.ClampToEdge => VkSamplerAddressMode.SamplerAddressModeClampToEdge,
            TextureAddressMode.ClampToBorder => VkSamplerAddressMode.SamplerAddressModeClampToBorder,
            TextureAddressMode.MirroredRepeat => VkSamplerAddressMode.SamplerAddressModeMirroredRepeat,
            TextureAddressMode.Repeat => VkSamplerAddressMode.SamplerAddressModeRepeat,
            _ => throw new ArgumentOutOfRangeException(nameof(wrapMode), wrapMode, null)
        };

    /// <summary>Vulkan has three border colours where OpenGL has any; picks the closest.</summary>
    private static VkBorderColor GetBorderColor(ColorRgba color)
    {
        if (color.A < 0.5f)
            return VkBorderColor.BorderColorFloatTransparentBlack;

        return (color.R + color.G + color.B) / 3f < 0.5f
            ? VkBorderColor.BorderColorFloatOpaqueBlack
            : VkBorderColor.BorderColorFloatOpaqueWhite;
    }
}
