using System.Runtime.InteropServices;

namespace Ralsei.Rendering.OpenGL;

// std430 layout
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct GpuMultiDrawItem
{
    [FieldOffset(0)] public int RenderType; // 0 = mesh, 1 = text
    [FieldOffset(4)] public int DrawId;
}