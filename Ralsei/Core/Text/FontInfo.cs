using Tmpx.Common;
using Tmpx.Shaping;

namespace Ralsei.Core.Text;

public class FontInfo(string name, FontStyle style, Font font, int globalShapeEntryBase)
{
    public string Name => name;
    public FontStyle Style => style;
    public Font Font => font;
    
    /// <summary>Offset into the global ShapeEntries buffer where this font's entries begin; shaper-emitted local indices are offset by it before going into <see cref="ShapedTextGlyph.ShapeEntryIndex"/>.</summary>
    public int GlobalShapeEntryBase => globalShapeEntryBase;
}
