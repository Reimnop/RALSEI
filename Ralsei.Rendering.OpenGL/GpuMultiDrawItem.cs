using System.Runtime.InteropServices;

namespace Ralsei.Rendering.OpenGL;

// std430 layout
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct GpuMultiDrawItem
{
    [FieldOffset(0)] public int RenderType; // 0 = mesh, 1 = text
    [FieldOffset(4)] public int DrawId;

    /// <summary>Where the draw shows, in pixels of the target: left and top (x in the low 16 bits), see <see cref="ClipBounds.Pack"/>.</summary>
    [FieldOffset(8)] public uint ClipMin;

    /// <summary>Right and bottom, packed the same way. Fragments at or past it are not drawn.</summary>
    [FieldOffset(12)] public uint ClipMax;
}