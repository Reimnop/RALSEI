using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.Vulkan;

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct GpuTextDrawItem
{
    [FieldOffset(0)] public Matrix3x2 Mvp;
    [FieldOffset(24)] public float Z;
    [FieldOffset(28)] public int GlyphOffset;
    [FieldOffset(32)] public ColorRgba Color;

    /// <summary>Where the draw shows, in pixels of the target: left and top (x in the low 16 bits), see <see cref="Common.ClipBounds.Pack"/>.</summary>
    [FieldOffset(48)] public uint ClipMin;

    /// <summary>Right and bottom, packed the same way. Fragments at or past it are not drawn.</summary>
    [FieldOffset(52)] public uint ClipMax;
}