namespace Swag.Android.Settings;

public enum RenderingBackend
{
    OpenGLES,
    Vulkan
}

/// <summary>
/// Everything the user can tune. Changed on the home screen, and (for the ones that make sense while a level
/// is playing) from the player.
/// </summary>
public sealed class PlayerSettings
{
    public static readonly float[] RenderScales = [1f, 0.75f, 0.5f];
    public static readonly int[] FrameRateLimits = [0, 30, 60];
    public static readonly float[] PlaybackSpeeds = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f];

    public RenderingBackend Backend { get; set; } = RenderingBackend.OpenGLES;
    public bool LockAspectRatio { get; set; } = true;
    public bool PostProcessing { get; set; } = true;
    public bool TextRendering { get; set; } = true;
    public bool VSync { get; set; } = true;

    /// <summary>The fraction of the screen resolution the level is rendered at. The system scales it up to the screen.</summary>
    public float RenderScale { get; set; } = 1f;

    /// <summary>Frames per second to cap at, 0 for the refresh rate of the display.</summary>
    public int FrameRateLimit { get; set; }

    public float PlaybackSpeed { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool RandomSeed { get; set; } = true;
    public ulong Seed { get; set; } = 1;

    public bool ShowStats { get; set; }
}
