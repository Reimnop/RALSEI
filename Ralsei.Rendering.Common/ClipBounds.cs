using Ralsei.Mathematics;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

/// <summary>Where on its target a draw shows, as the multi-draw shaders read it.</summary>
public static class ClipBounds
{
    /// <summary>
    /// The part of a target of <paramref name="size"/> that <paramref name="clip"/> leaves of a pass drawn in <paramref name="part"/> (a
    /// disabled one is the whole target), counting rows from the bottom as the scissor does: left and top, and right and bottom, each pair
    /// in the two halves of a uint (x in the low half). Clips are in pixels of the part of the target the pass is in, and nothing shows outside it.
    /// </summary>
    public static (uint Min, uint Max) Pack(ClipRect clip, ClipRect part, Vector2i size)
    {
        var originX = part.Enabled ? part.X : 0;
        var originY = part.Enabled ? part.Y : 0;
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
        var x0 = Math.Clamp(originX + left, 0, 0xFFFF);
        var x1 = Math.Clamp(originX + right, 0, 0xFFFF);
        var y0 = Math.Clamp(size.Y - (originY + bottom), 0, 0xFFFF);
        var y1 = Math.Clamp(size.Y - (originY + top), 0, 0xFFFF);
        return ((uint) x0 | (uint) y0 << 16, (uint) x1 | (uint) y1 << 16);
    }
}
