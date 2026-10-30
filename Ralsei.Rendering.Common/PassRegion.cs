using Ralsei.Mathematics;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

public static class PassRegion
{
    /// <summary>
    /// The part of the target that the work of a pass is limited to (clearing it, resolving it, laying it over the passes before it),
    /// so that a pass drawn in a small <see cref="DrawData.Viewport"/> costs what its viewport does, not what the whole target does.
    /// Everything else of the target is left as it was, which nothing reads: that is only so of a pass in a layered frame whose output
    /// is not kept for later draws and has no effects (they look at the whole target). Otherwise there is no such part, and the
    /// result is <c>default</c>, which is all of the target.
    /// </summary>
    public static ClipRect Of(in DrawData pass, bool layered, bool outputUsed, Vector2i size)
    {
        var viewport = pass.Viewport;
        if (!viewport.Enabled || !layered || outputUsed || pass.PostProcessingState.HasEffects)
            return default;

        var left = Math.Clamp(viewport.X, 0, size.X);
        var top = Math.Clamp(viewport.Y, 0, size.Y);
        var right = Math.Clamp(viewport.Right, left, size.X);
        var bottom = Math.Clamp(viewport.Bottom, top, size.Y);
        if (right <= left || bottom <= top)
            return default;

        return new ClipRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// <paramref name="region"/> (a part of a target of <paramref name="size"/>, or <c>default</c> for all of it) as the images and
    /// framebuffers of the backends count it: x, y and the width and height, from the bottom-left corner, where the first row is the bottom.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ToImageRect(ClipRect region, Vector2i size)
        => region.Enabled
            ? (region.X, size.Y - region.Bottom, region.Width, region.Height)
            : (0, 0, size.X, size.Y);
}
