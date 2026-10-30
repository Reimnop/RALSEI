using Ralsei.Mathematics;

namespace Ralsei.Platform.Vulkan;

/// <summary>The windowing side of the Vulkan renderer: the renderer owns the instance, device and swapchain; the surface provides what is host-specific. Handles are raw integers so this project needs no Vulkan bindings.</summary>
public interface IVulkanSurface
{
    /// <summary>The size frames render at; smaller than <see cref="FramebufferSize"/> when letterboxed (the frame is centered).</summary>
    Vector2i RenderSize { get; }

    /// <summary>The size of the presentation target. Ignored if <see cref="IsHeadless"/>.</summary>
    Vector2i FramebufferSize { get; }

    /// <summary>Whether to wait for vertical blanking when presenting.</summary>
    bool VSync { get; }

    /// <summary>Whether to show the newest frame at each blanking (<c>MAILBOX</c>, where available) instead of queueing frames (<c>FIFO</c>), for a shorter delay from key to picture. Only with <see cref="VSync"/>.</summary>
    bool LowLatency => false;

    /// <summary>Whether the picture's alpha is shown (the window lets the screen through), as premultiplied colour, where the system can.</summary>
    bool Transparent => false;

    /// <summary>A frame was handed to the display.</summary>
    void FramePresented()
    {
    }

    /// <summary>If true there is no window and no presentation; frames can still be retrieved through <see cref="IFrameReceiver"/>.</summary>
    bool IsHeadless { get; }

    /// <summary>The instance extensions needed to create a surface (VK_KHR_surface and the platform one). Empty if <see cref="IsHeadless"/>.</summary>
    IReadOnlyList<string> GetRequiredInstanceExtensions();

    /// <summary>Creates a VkSurfaceKHR for the VkInstance and returns its handle. The caller owns it and destroys it before the instance.</summary>
    ulong CreateSurface(ulong instance);
}
