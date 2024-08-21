using Android.Views;

namespace Ralsei.Android.Platform;

/// <summary>
/// Hands the window to render to over to the renderer's surface. The window changes whenever the system recreates
/// it (the app returns from the background, for instance), so this is set before each renderer is created.
/// </summary>
public class AndroidSurfaceContext
{
    public Surface? Surface { get; set; }
}
