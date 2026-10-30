using Ralsei.Mathematics;

namespace Swag.Desktop;

public class DesktopSurfaceSettings
{
    public required Vector2i Size { get; init; }
    public required bool VSync { get; init; }
    public required bool UseEgl { get; init; }
    public required bool LockAspectRatio { get; init; }
}