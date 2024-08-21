using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Platform.Vulkan;

namespace Ralsei.Desktop;

public sealed unsafe class VulkanDesktopSurface(DesktopSurfaceSettings surfaceSettings, GlfwService glfw)
    : DesktopWindow(surfaceSettings, glfw), IVulkanSurface
{
    public bool VSync => SurfaceSettings.VSync;

    public bool IsHeadless => false;

    public IReadOnlyList<string> GetRequiredInstanceExtensions()
    {
        if (!GLFW.VulkanSupported())
            throw new InvalidOperationException("Vulkan is not supported on this system");

        return GLFW.GetRequiredInstanceExtensions();
    }

    public ulong CreateSurface(ulong instance)
    {
        VkHandle surface;
        var result = GLFW.CreateWindowSurface(new VkHandle { Handle = instance }, WindowPtr, null, out surface);
        if (result != 0)
            throw new InvalidOperationException($"Failed to create a Vulkan surface for the window (VkResult {result})");

        return surface.Handle;
    }
}
