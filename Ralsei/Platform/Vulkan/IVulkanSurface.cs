using Ralsei.Mathematics;

namespace Ralsei.Platform.Vulkan;

/// <summary>
/// The windowing-system side of the Vulkan renderer. The renderer owns the instance, device and
/// swapchain; the surface only provides what is specific to the host (window or no window).
/// Handles are passed as raw integers so that this project does not depend on any Vulkan bindings.
/// </summary>
public interface IVulkanSurface
{
    /// <summary>
    /// The size frames are rendered at. This can be smaller than <see cref="FramebufferSize"/>
    /// when the output is letterboxed; the frame is centered within the framebuffer.
    /// </summary>
    Vector2i RenderSize { get; }

    /// <summary>
    /// The size of the presentation target. Ignored if <see cref="IsHeadless"/> is true.
    /// </summary>
    Vector2i FramebufferSize { get; }

    /// <summary>
    /// Whether to wait for vertical blanking when presenting.
    /// </summary>
    bool VSync { get; }

    /// <summary>
    /// If true, there is no window to present to and no presentation takes place.
    /// Frames can still be retrieved through <see cref="IVulkanFrameReceiver"/>.
    /// </summary>
    bool IsHeadless { get; }

    /// <summary>
    /// The instance extensions required to create a surface, e.g. VK_KHR_surface and the
    /// platform-specific one. Empty if <see cref="IsHeadless"/> is true.
    /// </summary>
    IReadOnlyList<string> GetRequiredInstanceExtensions();

    /// <summary>
    /// Creates a VkSurfaceKHR for the given VkInstance and returns its handle.
    /// The caller takes ownership and destroys the surface before the instance.
    /// </summary>
    ulong CreateSurface(ulong instance);
}

/// <summary>
/// Implemented by a <see cref="IVulkanSurface"/> that wants to receive the pixels of every rendered
/// frame, e.g. to encode them into a video.
/// </summary>
public interface IVulkanFrameReceiver
{
    /// <summary>
    /// Called after a frame has been rendered. The frame is tightly packed RGBA8 with the bottom row
    /// first (the same layout as glReadPixels). It is not copied: the data is memory owned by the renderer,
    /// which stays valid until the renderer starts on the next frame.
    /// </summary>
    void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size);
}
