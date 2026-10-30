namespace Ralsei.Core.Graphics;

public readonly record struct BitmapGlyph(AtlasRegion Region, int Offset, int Advance);

/// <summary>A fixed-size raster font made of glyph rectangles inside one atlas image.</summary>
public sealed class BitmapFont(string name, int lineHeight, IReadOnlyDictionary<int, BitmapGlyph> glyphs)
{
    public string Name { get; } = name;
    public int LineHeight { get; } = lineHeight;

    public bool TryGetGlyph(char c, out BitmapGlyph glyph)
        => glyphs.TryGetValue(c, out glyph) || glyphs.TryGetValue('?', out glyph);

    public float MeasureWidth(string text, float scale = 1f)
    {
        float widest = 0, line = 0;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                widest = Math.Max(widest, line);
                line = 0;
                continue;
            }

            if (TryGetGlyph(c, out var glyph))
                line += glyph.Advance * scale;
        }

        return Math.Max(widest, line);
    }

    public float MeasureHeight(string text, float scale = 1f)
        => (text.Count(c => c == '\n') + 1) * LineHeight * scale;
}
