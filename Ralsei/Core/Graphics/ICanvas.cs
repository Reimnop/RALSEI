using System.Numerics;
using Ralsei.Core.Data;

namespace Ralsei.Core.Graphics;

/// <summary>Immediate-mode 2D drawing in a space of <see cref="Width"/> by <see cref="Height"/> units, origin top-left, Y down: the window size in pixels unless <see cref="CanvasOptions.FixedSize"/> says otherwise. Calls are painted in order.</summary>
public interface ICanvas
{
    /// <summary>The width of the drawing space, as of the frame being drawn.</summary>
    int Width { get; }

    /// <summary>The height of the drawing space, as of the frame being drawn.</summary>
    int Height { get; }

    /// <summary>Added to the position of everything drawn afterwards. Used for screen shake.</summary>
    Vector2 Offset { get; set; }

    /// <summary>Draws a sprite frame so that the sprite's origin lands on (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="rotation">Counter-clockwise rotation about the origin, in degrees.</param>
    /// <param name="blend">Multiplies the sprite's colours (an image blend).</param>
    /// <param name="fog">Floods the sprite's visible pixels with a colour, keeping transparency (silhouettes, flashes): red, green and blue are the colour, alpha how much to mix in.</param>
    void DrawSprite(
        Sprite sprite, int frame, float x, float y,
        float scaleX = 1f, float scaleY = 1f, float rotation = 0f, float alpha = 1f, ColorRgba? blend = null, ColorRgba? fog = null);

    /// <summary>Draws a part of an atlas image with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    void DrawRegion(AtlasRegion region, float x, float y, float width, float height, ColorRgba color, ColorRgba? fog = null);

    void FillRect(float x, float y, float width, float height, ColorRgba color);

    void StrokeRect(float x, float y, float width, float height, float thickness, ColorRgba color);

    /// <summary>Draws <paramref name="text"/> with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    void DrawText(BitmapFont font, string text, float x, float y, ColorRgba color, float scale = 1f)
        => DrawText(font, text, x, y, color, scale, scale);

    /// <summary>Draws text stretched independently on each axis, like a transformed text.</summary>
    void DrawText(BitmapFont font, string text, float x, float y, ColorRgba color, float scaleX, float scaleY);
}
