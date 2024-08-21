using System.Runtime.InteropServices;
using Ralsei.Core.Model;
using Ralsei.Wasm.Interop.Data;

namespace Ralsei.Wasm.Interop;

public static class InteropBeatmapData
{
    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getObjects")]
    public static IntPtr GetObjects(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var objects = beatmapData.Objects;
        var wrapper = new IdContainerInteropWrapper<BeatmapObject>(objects);
        return InteropHelper.ObjectToIntPtr(wrapper);
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getPrefabInstances")]
    public static IntPtr GetPrefabInstances(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var prefabInstances = beatmapData.PrefabInstances;
        var wrapper = new IdContainerInteropWrapper<BeatmapPrefabInstance>(prefabInstances);
        return InteropHelper.ObjectToIntPtr(wrapper);
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getPrefabs")]
    public static IntPtr GetPrefabs(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var prefabs = beatmapData.Prefabs;
        var wrapper = new IdContainerInteropWrapper<BeatmapPrefab>(prefabs);
        return InteropHelper.ObjectToIntPtr(wrapper);
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getThemes")]
    public static IntPtr GetThemes(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var themes = beatmapData.Themes;
        var wrapper = new IdContainerInteropWrapper<BeatmapTheme>(themes);
        return InteropHelper.ObjectToIntPtr(wrapper);
    }

    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getEvents")]
    public static IntPtr GetEvents(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var events = beatmapData.Events;
        return InteropHelper.ObjectToIntPtr(events);
    }
    
    [UnmanagedCallersOnly(EntryPoint = "beatmapData_getParallax")]
    public static IntPtr GetParallax(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        var parallax = beatmapData.Parallax;
        return InteropHelper.ObjectToIntPtr(parallax);
    }
    
    [UnmanagedCallersOnly(EntryPoint = "beatmapData_clear")]
    public static void Clear(IntPtr ptr)
    {
        var beatmapData = InteropHelper.IntPtrToObject<BeatmapData>(ptr);
        beatmapData.Clear();
    }
}