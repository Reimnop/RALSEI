using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.OpenGL;

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct GpuTextDrawItem
{
    [FieldOffset(0)] public Matrix3x2 Mvp;
    [FieldOffset(24)] public float Z;
    [FieldOffset(28)] public int GlyphOffset;
    [FieldOffset(32)] public ColorRgba Color;

    /// <summary>Where the draw shows, in pixels of the pass viewport: left and bottom (x in the low 16 bits), see <see cref="Common.ClipBounds.Pack"/>.</summary>
    [FieldOffset(48)] public uint ClipMin;

    /// <summary>Right and top, packed the same way. Pixels at or past it are not drawn.</summary>
    [FieldOffset(52)] public uint ClipMax;
}