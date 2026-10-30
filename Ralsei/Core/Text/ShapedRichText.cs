using System.Numerics;
using Ralsei.Core.Data;

namespace Ralsei.Core.Text;

/// <summary>One placed glyph (or a mark rectangle when <see cref="ShapeEntryIndex"/> is -1) from the shaper. FontService remaps local indices to global buffer space during shaping, so the renderer never needs to know about fonts.</summary>
public struct ShapedTextGlyph(Matrix3x2 transform, ColorRgba color, int shapeEntryIndex)
{
    public Matrix3x2 Transform = transform;
    public ColorRgba Color = color;

    /// <summary>Global index into the renderer's shape-entry buffer. -1 = mark rectangle.</summary>
    public int ShapeEntryIndex = shapeEntryIndex;
}

public class ShapedRichText
{
    public List<ShapedTextGlyph> Glyphs { get; set; } = [];
}
