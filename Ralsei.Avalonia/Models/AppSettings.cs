namespace Ralsei.Avalonia.Models;

public enum ThemeChoice
{
    System,
    Dark,
    Light
}

/// <summary>
/// Everything that is remembered between runs.
/// </summary>
public sealed class AppSettings
{
    public static readonly double[] RenderScales = [1.0, 0.75, 0.5, 0.25];
    public static readonly double[] PlaybackSpeeds = [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    // Appearance
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;

    // Rendering
    public bool EnablePostProcessing { get; set; } = true;
    public bool EnableTextRendering { get; set; } = true;
    public bool LockAspectRatio { get; set; } = true;

    /// <summary>
    /// The fraction of the size of the view that the level is rendered at.
    /// </summary>
    public double RenderScale { get; set; } = 1.0;

    public bool ShowStats { get; set; }

    // Playback
    public double Volume { get; set; } = 1.0;
    public bool Muted { get; set; }
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool Loop { get; set; }
    public bool AutoPlay { get; set; } = true;
    public bool RandomSeed { get; set; } = true;
    public ulong Seed { get; set; } = 1;

    // Export
    public string FFmpegPath { get; set; } = "";
    public int ExportWidth { get; set; } = 1920;
    public int ExportHeight { get; set; } = 1080;
    public int ExportFramerate { get; set; } = 60;
    public string ExportArguments { get; set; } = "-c:v libx264 -pix_fmt yuv420p -preset medium -crf 18 -c:a aac -b:a 192k";
    public string? LastExportFolder { get; set; }

    /// <summary>
    /// Takes over every setting of another. Written out, as reflection does not survive trimming.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        Theme = other.Theme;
        EnablePostProcessing = other.EnablePostProcessing;
        EnableTextRendering = other.EnableTextRendering;
        LockAspectRatio = other.LockAspectRatio;
        RenderScale = other.RenderScale;
        ShowStats = other.ShowStats;
        Volume = other.Volume;
        Muted = other.Muted;
        PlaybackSpeed = other.PlaybackSpeed;
        Loop = other.Loop;
        AutoPlay = other.AutoPlay;
        RandomSeed = other.RandomSeed;
        Seed = other.Seed;
        FFmpegPath = other.FFmpegPath;
        ExportWidth = other.ExportWidth;
        ExportHeight = other.ExportHeight;
        ExportFramerate = other.ExportFramerate;
        ExportArguments = other.ExportArguments;
        LastExportFolder = other.LastExportFolder;
    }
}
