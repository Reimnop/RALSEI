using OpenTK.Graphics.Vulkan;
using Ralsei.Mathematics;
using Ralsei.Platform.Vulkan;

namespace Ralsei.Android.Platform;

/// <summary>
/// The Vulkan side of the window: gives the renderer a VkSurfaceKHR for it, and says how big it is.
/// </summary>
public sealed unsafe class AndroidVulkanSurface : IVulkanSurface, IDisposable
{
    public Vector2i RenderSize => surfaceSettings.GetRenderSize(FramebufferSize);

    public Vector2i FramebufferSize => nativeWindow.Size;

    public bool VSync => surfaceSettings.VSync;

    public bool IsHeadless => false;

    private readonly AndroidSurfaceSettings surfaceSettings;
    private readonly NativeWindow nativeWindow;

    public AndroidVulkanSurface(AndroidSurfaceSettings surfaceSettings, AndroidSurfaceContext surfaceContext)
    {
        this.surfaceSettings = surfaceSettings;

        var surface = surfaceContext.Surface;
        if (surface is null || !surface.IsValid)
            throw new SurfaceLostException("There is no surface to render to");

        nativeWindow = NativeWindow.FromSurface(surface);
    }

    public IReadOnlyList<string> GetRequiredInstanceExtensions()
        => [Vk.KhrSurfaceExtensionName, Vk.KhrAndroidSurfaceExtensionName];

    public ulong CreateSurface(ulong instance)
    {
        var createInfo = new VkAndroidSurfaceCreateInfoKHR
        {
            sType = VkStructureType.StructureTypeAndroidSurfaceCreateInfoKhr,
            window = nativeWindow.Handle
        };

        VkSurfaceKHR surface;
        var result = Vk.CreateAndroidSurfaceKHR(new VkInstance { Handle = instance }, &createInfo, null, &surface);
        if (result != VkResult.Success)
            throw new InvalidOperationException($"Failed to create a Vulkan surface for the window (VkResult {result})");

        return (ulong) surface.Handle;
    }

    // The renderer destroys the VkSurfaceKHR before this, as it is created before the renderer
    public void Dispose()
    {
        nativeWindow.Dispose();
    }
}
