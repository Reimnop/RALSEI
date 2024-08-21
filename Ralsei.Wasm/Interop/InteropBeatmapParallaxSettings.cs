using System.Runtime.InteropServices;
using Ralsei.Core.Model;

namespace Ralsei.Wasm.Interop;

public static class InteropBeatmapParallaxSettings
{
    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_getIsDepthOfFieldEnabled")]
    public static int GetIsDepthOfFieldEnabled(IntPtr ptr)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        return parallax.IsDepthOfFieldEnabled ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_setIsDepthOfFieldEnabled")]
    public static void SetIsDepthOfFieldEnabled(IntPtr ptr, int value)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        parallax.IsDepthOfFieldEnabled = value != 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_getDepthOfField")]
    public static float GetDepthOfField(IntPtr ptr)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        return parallax.DepthOfField;
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_setDepthOfField")]
    public static void SetDepthOfField(IntPtr ptr, float value)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        parallax.DepthOfField = value;
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_getLayerCount")]
    public static int GetLayerCount(IntPtr ptr)
        => BeatmapParallaxSettings.LayerCount;

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_getLayerDepth")]
    public static int GetLayerDepth(IntPtr ptr, int layerIndex)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        return parallax.GetLayerDepth(layerIndex);
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapParallaxSettings_setLayerDepth")]
    public static void SetLayerDepth(IntPtr ptr, int layerIndex, int depth)
    {
        var parallax = InteropHelper.IntPtrToObject<BeatmapParallaxSettings>(ptr);
        if ((uint) layerIndex >= BeatmapParallaxSettings.LayerCount)
            return;

        parallax.SetLayerDepth(layerIndex, depth);
    }
}
