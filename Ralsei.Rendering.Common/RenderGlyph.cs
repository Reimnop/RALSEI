using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.Common;

[StructLayout(LayoutKind.Sequential)]
public struct RenderGlyph
{
    public Matrix3x2 Transform; // 0 - 24 (6 floats)
    public ColorRgba Color; // 24 - 40 (4 floats)
    public int ShapeEntryIndex; // 40 - 44
}
