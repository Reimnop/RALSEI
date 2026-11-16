using Ralsei.Mathematics;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

/// <summary>Where on its target a draw shows, as the multi-draw shaders read it.</summary>
public static class ClipBounds
{
    /// <summary>
    /// The part of a pass's viewport that <paramref name="clip"/> leaves (<paramref name="part"/> is the part of a target of
    /// <paramref name="size"/> the pass is drawn in, the whole target if disabled), counting from the viewport's bottom-left corner: left and
    /// bottom, and right and top, each pair in the two halves of a uint (x in the low half). Clips are in pixels of the viewport.
    /// </summary>
    public static (uint Min, uint Max) Pack(ClipRect clip, ClipRect part, Vector2i size)
    {
        var width = part.Enabled ? part.Width : size.X;
        var height = part.Enabled ? part.Height : size.Y;
        var left = 0;
        var top = 0;
        var right = width;
        var bottom = height;
        if (clip.Enabled)
        {
            left = Math.Clamp(clip.X, 0, width);
            top = Math.Clamp(clip.Y, 0, height);
            right = Math.Clamp(clip.Right, left, width);
            bottom = Math.Clamp(clip.Bottom, top, height);
        }

        // The rows start at the bottom
        var x0 = Math.Clamp(left, 0, 0xFFFF);
        var x1 = Math.Clamp(right, 0, 0xFFFF);
        var y0 = Math.Clamp(height - bottom, 0, 0xFFFF);
        var y1 = Math.Clamp(height - top, 0, 0xFFFF);
        return ((uint) x0 | (uint) y0 << 16, (uint) x1 | (uint) y1 << 16);
    }
}
