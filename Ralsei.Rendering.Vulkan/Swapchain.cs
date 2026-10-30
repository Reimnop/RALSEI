using OpenTK.Graphics.Vulkan;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan;

/// <summary>The images a window shows. Tied to the window's size, so a new one is created whenever that changes.</summary>
internal sealed unsafe class Swapchain : IDisposable
{
    public VkSwapchainKHR Handle { get; private set; }
    public VkFormat Format { get; }
    public Vector2i Size { get; }
    public IReadOnlyList<VkImage> Images => images;

    /// <summary>
    /// Whether the display is rotated in a way this swapchain does not account for, so that the presentation engine
    /// has to rotate the images (Android does this and reports every present as suboptimal, which is not a reason to
    /// recreate the swapchain: it would just end up in the same state).
    /// </summary>
    public bool TransformMismatch { get; }

    private readonly VulkanContext context;
    private readonly VkImage[] images;

    // Per image, not per frame in flight: the presentation engine releases the semaphore when it is done with the image.
    private readonly VkSemaphore[] renderFinished;

    public Swapchain(VulkanContext context, Vector2i size, bool vsync, bool lowLatency, bool transparent, Swapchain? previous)
    {
        this.context = context;

        VkSurfaceCapabilitiesKHR capabilities;
        VkUtil.Check(
            Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(context.PhysicalDevice, context.Surface, &capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        var surfaceFormat = ChooseSurfaceFormat();
        Format = surfaceFormat.format;

        // The surface decides the size unless it says (with 0xFFFFFFFF) that the swapchain does
        if (capabilities.currentExtent.width != uint.MaxValue)
        {
            Size = new Vector2i((int) capabilities.currentExtent.width, (int) capabilities.currentExtent.height);
        }
        else
        {
            Size = new Vector2i(
                Math.Clamp(size.X, (int) capabilities.minImageExtent.width, (int) capabilities.maxImageExtent.width),
                Math.Clamp(size.Y, (int) capabilities.minImageExtent.height, (int) capabilities.maxImageExtent.height));
        }

        // One image more than the least lets the display and the renderer work without waiting for each other; the least is a shorter
        // line of pictures waiting to be shown, which is the lower delay (RALSEI_LOW_LATENCY=0 for the first)
        var presentMode = ChoosePresentMode(vsync, lowLatency);
        var imageCount = Math.Max(capabilities.minImageCount, 2u) + (Environment.GetEnvironmentVariable("RALSEI_LOW_LATENCY") == "0" ? 1u : 0u);

        // A frame that takes the place of the one waiting needs an image that is neither shown nor waiting
        if (presentMode == VkPresentModeKHR.PresentModeMailboxKhr)
            imageCount = Math.Max(imageCount, 3u);

        if (capabilities.maxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);

        // The frame is blitted into the swapchain images
        const VkImageUsageFlagBits usage = VkImageUsageFlagBits.ImageUsageTransferDstBit;
        if ((capabilities.supportedUsageFlags & usage) != usage)
            throw new VulkanException("The surface does not support presenting images that are blitted to");

        // The frames are not rendered pre-rotated, so ask the presentation engine to rotate for us if the display
        // is rotated (e.g. an app locked to landscape on a phone that is portrait by nature)
        var preTransform = (capabilities.supportedTransforms & VkSurfaceTransformFlagBitsKHR.SurfaceTransformIdentityBitKhr) != 0
            ? VkSurfaceTransformFlagBitsKHR.SurfaceTransformIdentityBitKhr
            : capabilities.currentTransform;
        TransformMismatch = preTransform != capabilities.currentTransform;

        // A transparent window shows the alpha of the picture. The frames hold premultiplied colors, so that is the one mode that shows
        // them right; the mode for straight colors would brighten every soft edge, so a system that only offers that leaves the window opaque
        var compositeAlpha = (capabilities.supportedCompositeAlpha & VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr) != 0
            ? VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr
            : VkCompositeAlphaFlagBitsKHR.CompositeAlphaInheritBitKhr;
        if (transparent && (capabilities.supportedCompositeAlpha & VkCompositeAlphaFlagBitsKHR.CompositeAlphaPreMultipliedBitKhr) != 0)
            compositeAlpha = VkCompositeAlphaFlagBitsKHR.CompositeAlphaPreMultipliedBitKhr;

        var createInfo = new VkSwapchainCreateInfoKHR
        {
            sType = VkStructureType.StructureTypeSwapchainCreateInfoKhr,
            surface = context.Surface,
            minImageCount = imageCount,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = new VkExtent2D { width = (uint) Size.X, height = (uint) Size.Y },
            imageArrayLayers = 1,
            imageUsage = usage,
            imageSharingMode = VkSharingMode.SharingModeExclusive,
            preTransform = preTransform,
            compositeAlpha = compositeAlpha,
            presentMode = presentMode,
            clipped = VkUtil.True,
            oldSwapchain = previous?.Handle ?? default
        };
        VkSwapchainKHR swapchain;
        VkUtil.Check(Vk.CreateSwapchainKHR(context.Device, &createInfo, null, &swapchain), "vkCreateSwapchainKHR");
        Handle = swapchain;

        uint count;
        VkUtil.Check(Vk.GetSwapchainImagesKHR(context.Device, Handle, &count, null), "vkGetSwapchainImagesKHR");
        images = new VkImage[count];
        fixed (VkImage* pImages = images)
            VkUtil.Check(Vk.GetSwapchainImagesKHR(context.Device, Handle, &count, pImages), "vkGetSwapchainImagesKHR");

        renderFinished = new VkSemaphore[count];
        var semaphoreInfo = new VkSemaphoreCreateInfo { sType = VkStructureType.StructureTypeSemaphoreCreateInfo };
        for (var i = 0; i < count; i++)
        {
            VkSemaphore semaphore;
            VkUtil.Check(Vk.CreateSemaphore(context.Device, &semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
            renderFinished[i] = semaphore;
        }
    }

    public VkSemaphore RenderFinishedSemaphore(uint imageIndex) => renderFinished[imageIndex];

    public void Dispose()
    {
        foreach (var semaphore in renderFinished)
            Vk.DestroySemaphore(context.Device, semaphore, null);

        if (Handle.Handle != 0)
        {
            Vk.DestroySwapchainKHR(context.Device, Handle, null);
            Handle = default;
        }
    }

    private VkSurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint count;
        VkUtil.Check(
            Vk.GetPhysicalDeviceSurfaceFormatsKHR(context.PhysicalDevice, context.Surface, &count, null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR");
        var formats = new VkSurfaceFormatKHR[count];
        fixed (VkSurfaceFormatKHR* pFormats = formats)
            VkUtil.Check(
                Vk.GetPhysicalDeviceSurfaceFormatsKHR(context.PhysicalDevice, context.Surface, &count, pFormats),
                "vkGetPhysicalDeviceSurfaceFormatsKHR");

        // The frame already holds the final (gamma encoded) colors, so an UNORM format that doesn't
        // convert on write is the one to use, the same as the default framebuffer in OpenGL.
        foreach (var preferred in (ReadOnlySpan<VkFormat>) [VkFormat.FormatB8g8r8a8Unorm, VkFormat.FormatR8g8b8a8Unorm])
        {
            foreach (var format in formats)
            {
                if (format.format == preferred && format.colorSpace == VkColorSpaceKHR.ColorSpaceSrgbNonlinearKhr)
                    return format;
            }
        }

        return formats[0];
    }

    private VkPresentModeKHR ChoosePresentMode(bool vsync, bool lowLatency)
    {
        // FIFO is the only mode that is guaranteed to exist, and it waits for vertical blanking
        if (vsync && !lowLatency)
            return VkPresentModeKHR.PresentModeFifoKhr;

        uint count;
        VkUtil.Check(
            Vk.GetPhysicalDeviceSurfacePresentModesKHR(context.PhysicalDevice, context.Surface, &count, null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR");
        var modes = new VkPresentModeKHR[count];
        fixed (VkPresentModeKHR* pModes = modes)
            VkUtil.Check(
                Vk.GetPhysicalDeviceSurfacePresentModesKHR(context.PhysicalDevice, context.Surface, &count, pModes),
                "vkGetPhysicalDeviceSurfacePresentModesKHR");

        // With vertical sync the mode that shows the newest frame at each blanking; without it, the one that shows frames at once
        var preferences = vsync
            ? (ReadOnlySpan<VkPresentModeKHR>) [VkPresentModeKHR.PresentModeMailboxKhr]
            : [VkPresentModeKHR.PresentModeImmediateKhr, VkPresentModeKHR.PresentModeMailboxKhr];
        foreach (var preferred in preferences)
        {
            if (Array.IndexOf(modes, preferred) >= 0)
                return preferred;
        }

        return VkPresentModeKHR.PresentModeFifoKhr;
    }
}
