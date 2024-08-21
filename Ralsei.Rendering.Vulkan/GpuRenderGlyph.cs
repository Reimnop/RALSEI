using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.Vulkan;

// std430 layout, matches RenderGlyph in Uber.vert
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct GpuRenderGlyph
{
    [FieldOffset(0)] public ColorRgba Color; // 0 - 16 (4 floats)
    [FieldOffset(16)] public Matrix3x2 Transform; // 16 - 40 (6 floats)
    [FieldOffset(40)] public int ShapeEntryIndex; // 40 - 44
}
