using System.Runtime.InteropServices;

namespace Swag.Data;

[StructLayout(LayoutKind.Sequential)]
public struct BeatmapObjectIndexedColor
{
    public int ColorIndex1;
    public int ColorIndex2;
    public float Opacity;
}