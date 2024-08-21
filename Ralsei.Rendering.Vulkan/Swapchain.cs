using OpenTK.Graphics.Vulkan;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// The images a window shows. A swapchain is tied to the size of the window, so a new one has to be
/// created whenever that changes.
/// </summary>
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

    // Signaled when rendering to the image with the same index is done, and presenting can begin.
    // These have to be per image (not per frame in flight), as the presentation engine releases
    // the semaphore only when it is done with the image, which is not tied to when a frame finishes.
    private readonly VkSemaphore[] renderFinished;

    public Swapchain(VulkanContext context, Vector2i size, bool vsync, Swapchain? previous)
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

        var imageCount = capabilities.minImageCount + 1;
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

        var compositeAlpha = (capabilities.supportedCompositeAlpha & VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr) != 0
            ? VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr
            : VkCompositeAlphaFlagBitsKHR.CompositeAlphaInheritBitKhr;

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
            presentMode = ChoosePresentMode(vsync),
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

    private VkPresentModeKHR ChoosePresentMode(bool vsync)
    {
        // FIFO is the only mode that is guaranteed to exist, and it waits for vertical blanking
        if (vsync)
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

        foreach (var preferred in (ReadOnlySpan<VkPresentModeKHR>) [VkPresentModeKHR.PresentModeImmediateKhr, VkPresentModeKHR.PresentModeMailboxKhr])
        {
            if (Array.IndexOf(modes, preferred) >= 0)
                return preferred;
        }

        return VkPresentModeKHR.PresentModeFifoKhr;
    }
}
