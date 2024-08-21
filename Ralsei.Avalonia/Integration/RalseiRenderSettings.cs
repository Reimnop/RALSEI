namespace Ralsei.Avalonia.Integration;

/// <summary>
/// How the level is drawn into the view. Changed from the UI, read while rendering.
/// </summary>
public sealed class RalseiRenderSettings
{
    /// <summary>
    /// The fraction of the size of the view that the level is rendered at. What is rendered is scaled to fill the
    /// view, so that a smaller one is for machines that can not keep up at the full size.
    /// </summary>
    public double RenderScale { get; set; } = 1.0;

    /// <summary>
    /// The size the level was last rendered at, in pixels.
    /// </summary>
    public int RenderWidth { get; set; }

    public int RenderHeight { get; set; }
}
