using System.Runtime.InteropServices;
using Ralsei.Core.Data;
using Ralsei.Core.Model;

namespace Ralsei.Wasm.Interop;

public static class InteropBeatmapThemeColorList
{
    [UnmanagedCallersOnly(EntryPoint = "beatmapThemeColorList_getCount")]
    public static int GetCount(IntPtr ptr)
    {
        var list = InteropHelper.IntPtrToObject<BeatmapThemeColorList>(ptr);
        return list.Count;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "beatmapThemeColorList_fetchAt")]
    public static unsafe void FetchAt(IntPtr ptr, int index, float* colorPtr)
    {
        var list = InteropHelper.IntPtrToObject<BeatmapThemeColorList>(ptr);
        var color = list[index];
        colorPtr[0] = color.R;
        colorPtr[1] = color.G;
        colorPtr[2] = color.B;
    }
    
    [UnmanagedCallersOnly(EntryPoint = "beatmapThemeColorList_setAt")]
    public static unsafe void SetAt(IntPtr ptr, int index, float* colorPtr)
    {
        var list = InteropHelper.IntPtrToObject<BeatmapThemeColorList>(ptr);
        list[index] = new ColorRgb(colorPtr[0], colorPtr[1], colorPtr[2]);
    }
}