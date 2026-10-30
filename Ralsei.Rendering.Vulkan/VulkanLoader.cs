using System.Runtime.InteropServices;
using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;

namespace Ralsei.Rendering.Vulkan;

internal static unsafe class VulkanLoader
{
    /// <summary>Loads the Vulkan library and the function all the others are looked up with.</summary>
    public static void Init()
    {
        // OpenTK finds the library by operating system, and does not know that Android has one as well
        if (!OperatingSystem.IsAndroid())
        {
            VKLoader.Init();
            return;
        }

        VKLoader.VulkanHandle = NativeLibrary.Load("libvulkan.so");
        if (!NativeLibrary.TryGetExport(VKLoader.VulkanHandle, "vkGetInstanceProcAddr", out var address))
            throw new EntryPointNotFoundException("Could not load vkGetInstanceProcAddr.");

        VkPointers._vkGetInstanceProcAddr_fnptr = (delegate* unmanaged<VkInstance, byte*, nint>) address;
    }
}
