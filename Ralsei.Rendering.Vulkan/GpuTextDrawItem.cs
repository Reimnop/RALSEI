using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.Vulkan;

[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct GpuTextDrawItem
{
    [FieldOffset(0)] public Matrix3x2 Mvp;
    [FieldOffset(24)] public float Z;
    [FieldOffset(28)] public int GlyphOffset;
    [FieldOffset(32)] public ColorRgba Color;
}