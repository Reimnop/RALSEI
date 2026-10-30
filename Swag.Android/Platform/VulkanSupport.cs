using Android.Content;
using Android.Content.PM;

namespace Swag.Android.Platform;

public static class VulkanSupport
{
    // VK_MAKE_API_VERSION(0, 1, 3, 0), what the renderer needs: dynamic rendering and synchronization2 are core in it
    private const int RequiredVersion = (1 << 22) | (3 << 12);

    /// <summary>
    /// Whether the device has a Vulkan driver that is new enough for the renderer. Android lists this as a feature
    /// of the device, so nothing has to be loaded to find out.
    /// </summary>
    public static bool IsSupported(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            return false;

        return context.PackageManager!.HasSystemFeature(PackageManager.FeatureVulkanHardwareVersion, RequiredVersion);
    }
}
