namespace Ralsei.Core.Graphics;

/// <summary>Fits a fixed-size virtual screen into a window of any shape: all of it is always visible with its proportions kept, and the rest of the window becomes bars so nothing drawn outside leaks in.</summary>
public static class VirtualViewport
{
    /// <summary>The renderer's camera scale: half the visible height in virtual pixels (a wider window fits by height, a narrower one by width).</summary>
    public static float CameraScale(int screenWidth, int screenHeight, int windowWidth, int windowHeight)
    {
        var halfWidth = screenWidth / 2f;
        var halfHeight = screenHeight / 2f;

        if (windowWidth <= 0 || windowHeight <= 0)
            return halfHeight;

        var aspect = windowWidth / (float)windowHeight;
        return MathF.Max(halfHeight, halfWidth / aspect);
    }

    /// <summary>The bars covering everything outside the virtual screen, in the renderer's world units (origin at the screen centre, Y up): left, right, top, bottom. Empty ones have zero size.</summary>
    public static IReadOnlyList<(float X, float Y, float Width, float Height)> Bars(int screenWidth, int screenHeight, int windowWidth, int windowHeight)
    {
        var halfWidth = screenWidth / 2f;
        var halfHeight = screenHeight / 2f;

        var scale = CameraScale(screenWidth, screenHeight, windowWidth, windowHeight);
        var aspect = windowWidth > 0 && windowHeight > 0 ? windowWidth / (float)windowHeight : screenWidth / (float)screenHeight;

        // A little more than the visible extent, so rounding never leaves a sliver at the window's edge.
        var visibleHalfWidth = scale * aspect + 2f;
        var visibleHalfHeight = scale + 2f;

        var sideWidth = MathF.Max(visibleHalfWidth - halfWidth, 0f);
        var capHeight = MathF.Max(visibleHalfHeight - halfHeight, 0f);
        var fullHeight = visibleHalfHeight * 2f;
        var fullWidth = visibleHalfWidth * 2f;

        return
        [
            (-halfWidth - sideWidth, -visibleHalfHeight, sideWidth, fullHeight),
            (halfWidth, -visibleHalfHeight, sideWidth, fullHeight),
            (-visibleHalfWidth, halfHeight, fullWidth, capHeight),
            (-visibleHalfWidth, -halfHeight - capHeight, fullWidth, capHeight)
        ];
    }
}
