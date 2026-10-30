using Ralsei.Mathematics;
using Ralsei.Core.Data;

namespace Ralsei.Core.Graphics;

/// <summary>How the <see cref="Canvas"/> maps its drawing space onto the window. Register one to change the defaults.</summary>
public sealed class CanvasOptions
{
    /// <summary>The size of the drawing space. Null (default) is the window size in pixels. Set, all of it is always visible and keeps its proportions, scaled to fit, and the rest of the window is filled with <see cref="ClearColor"/> so nothing drawn outside leaks in.</summary>
    public Vector2i? FixedSize { get; set; }

    /// <summary>What the window is cleared to, and the colour of the bars around a <see cref="FixedSize"/> space. Opaque black by default; alpha 0 draws over a transparent window.</summary>
    public ColorRgba ClearColor { get; set; } = new(0f, 0f, 0f, 1f);
}
