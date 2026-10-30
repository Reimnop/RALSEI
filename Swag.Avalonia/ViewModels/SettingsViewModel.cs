using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Swag.Avalonia.Export;
using Swag.Avalonia.Models;
using Swag.Avalonia.Services;
using Swag.Avalonia.Views;
using Reimnop.MvvmHelper;

namespace Swag.Avalonia.ViewModels;

/// <summary>
/// Every option of the app. This is what the player bar, the keyboard shortcuts and the settings dialog all work on, so
/// that they can not disagree, and what is changed here is written to disk.
/// </summary>
[ViewModelView<SettingsView>]
public partial class SettingsViewModel : ViewModelBase
{
    public static readonly string[] ThemeNames = ["Follow the system", "Dark", "Light"];
    public static readonly string[] PlaybackSpeedNames = AppSettings.PlaybackSpeeds.Select(FormatSpeed).ToArray();
    public static readonly string[] RenderScaleNames = ["100%", "75%", "50%", "25%"];
    public static readonly string[] ExportSizeNames = ["1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160"];
    public static readonly string[] ExportFramerateNames = ["24", "30", "60"];

    private static readonly (int Width, int Height)[] ExportSizes = [(1280, 720), (1920, 1080), (2560, 1440), (3840, 2160)];
    private static readonly int[] ExportFramerates = [24, 30, 60];

    public const int MinVolumeStepPercent = 5;

    public LogsViewModel Logs { get; }

    public ShortcutGroup[] ShortcutGroups => ShortcutGroup.All;

    public string VersionText { get; } = $"Version {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public event EventHandler? Closing;

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    private readonly SettingsService service;
    private readonly WindowServices windowServices;
    private readonly AppSettings settings;

    private CancellationTokenSource? ffmpegCheck;

    public SettingsViewModel(SettingsService service, WindowServices windowServices, LogsViewModel logs)
    {
        this.service = service;
        this.windowServices = windowServices;
        settings = service.Settings;
        Logs = logs;

        _ = CheckFFmpegAsync();
    }

    #region Appearance

    public int ThemeIndex
    {
        get => (int) settings.Theme;
        set => Set(ThemeIndex, value, v => settings.Theme = (ThemeChoice) v);
    }

    #endregion

    #region Rendering

    public bool EnablePostProcessing
    {
        get => settings.EnablePostProcessing;
        set => Set(EnablePostProcessing, value, v => settings.EnablePostProcessing = v);
    }

    public bool EnableTextRendering
    {
        get => settings.EnableTextRendering;
        set => Set(EnableTextRendering, value, v => settings.EnableTextRendering = v);
    }

    public bool LockAspectRatio
    {
        get => settings.LockAspectRatio;
        set => Set(LockAspectRatio, value, v => settings.LockAspectRatio = v);
    }

    public int RenderScaleIndex
    {
        get => Math.Max(0, Array.IndexOf(AppSettings.RenderScales, settings.RenderScale));
        set => Set(RenderScaleIndex, value, v => settings.RenderScale = AppSettings.RenderScales[Math.Clamp(v, 0, AppSettings.RenderScales.Length - 1)]);
    }

    public double RenderScale => settings.RenderScale;

    public bool ShowStats
    {
        get => settings.ShowStats;
        set => Set(ShowStats, value, v => settings.ShowStats = v);
    }

    #endregion

    #region Playback

    /// <summary>From 0 to 1.</summary>
    public double Volume
    {
        get => settings.Volume;
        set => Set(Volume, Math.Clamp(value, 0, 1), v => settings.Volume = v, extra: [nameof(VolumeText), nameof(VolumeIconKind)]);
    }

    public bool IsMuted
    {
        get => settings.Muted;
        set => Set(IsMuted, value, v => settings.Muted = v, extra: [nameof(VolumeIconKind)]);
    }

    public string VolumeText => IsMuted ? "Muted" : $"{Math.Round(Volume * 100)}%";

    public double PlaybackSpeed
    {
        get => settings.PlaybackSpeed;
        set => Set(PlaybackSpeed, value, v => settings.PlaybackSpeed = v, extra: [nameof(PlaybackSpeedText), nameof(PlaybackSpeedIndex)]);
    }

    public string PlaybackSpeedText => FormatSpeed(PlaybackSpeed);

    public int PlaybackSpeedIndex
    {
        get => Math.Max(0, Array.FindIndex(AppSettings.PlaybackSpeeds, x => Math.Abs(x - PlaybackSpeed) < 0.001));
        set => PlaybackSpeed = AppSettings.PlaybackSpeeds[Math.Clamp(value, 0, AppSettings.PlaybackSpeeds.Length - 1)];
    }

    public bool Loop
    {
        get => settings.Loop;
        set => Set(Loop, value, v => settings.Loop = v);
    }

    public bool AutoPlay
    {
        get => settings.AutoPlay;
        set => Set(AutoPlay, value, v => settings.AutoPlay = v);
    }

    public bool RandomSeed
    {
        get => settings.RandomSeed;
        set => Set(RandomSeed, value, v => settings.RandomSeed = v, extra: [nameof(IsSeedEditable)]);
    }

    public bool IsSeedEditable => !RandomSeed;

    /// <summary>The seed, as it is typed. Anything that is not a number is not taken.</summary>
    public string SeedText
    {
        get => settings.Seed.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                Set(settings.Seed, seed, v => settings.Seed = v, nameof(SeedText));
        }
    }

    #endregion

    #region Export

    public string FFmpegPath
    {
        get => settings.FFmpegPath;
        set
        {
            Set(FFmpegPath, value ?? "", v => settings.FFmpegPath = v);
            _ = CheckFFmpegAsync();
        }
    }

    /// <summary>What is known about the FFmpeg that would make the videos.</summary>
    [ObservableProperty]
    public partial string FFmpegStatus { get; set; } = "";

    /// <summary>Where that FFmpeg is.</summary>
    [ObservableProperty]
    public partial string FFmpegLocation { get; set; } = "";

    [ObservableProperty]
    public partial bool IsFFmpegFound { get; set; }

    /// <summary>The folder the last video went to.</summary>
    public string? LastExportFolder
    {
        get => settings.LastExportFolder;
        set => Set(LastExportFolder, value, v => settings.LastExportFolder = v);
    }

    public int ExportSizeIndex
    {
        get => Math.Max(0, Array.FindIndex(ExportSizes, x => x.Width == settings.ExportWidth && x.Height == settings.ExportHeight));
        set => Set(ExportSizeIndex, value, v =>
        {
            var size = ExportSizes[Math.Clamp(v, 0, ExportSizes.Length - 1)];
            settings.ExportWidth = size.Width;
            settings.ExportHeight = size.Height;
        });
    }

    public int ExportFramerateIndex
    {
        get => Math.Max(0, Array.IndexOf(ExportFramerates, settings.ExportFramerate));
        set => Set(ExportFramerateIndex, value, v => settings.ExportFramerate = ExportFramerates[Math.Clamp(v, 0, ExportFramerates.Length - 1)]);
    }

    public (int Width, int Height) ExportSize => ExportSizes[ExportSizeIndex];

    public int ExportFramerate => ExportFramerates[ExportFramerateIndex];

    public string ExportArguments
    {
        get => settings.ExportArguments;
        set => Set(ExportArguments, value ?? "", v => settings.ExportArguments = v);
    }

    #endregion

    public global::Lucide.Avalonia.LucideIconKind VolumeIconKind
        => IsMuted || Volume <= 0 ? global::Lucide.Avalonia.LucideIconKind.VolumeX
            : Volume < 0.5 ? global::Lucide.Avalonia.LucideIconKind.Volume1
            : global::Lucide.Avalonia.LucideIconKind.Volume2;

    [RelayCommand]
    private async Task BrowseFFmpegAsync()
    {
        var storage = windowServices.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Where is FFmpeg?",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("FFmpeg") { Patterns = [OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            FFmpegPath = path;
    }

    /// <summary>Looks for FFmpeg again, for when it was installed while the app was open.</summary>
    [RelayCommand]
    public Task CheckFFmpegAsync()
        => CheckFFmpegAsync(false);

    /// <param name="quiet">Keeps what is shown while looking, for when nothing is likely to have changed.</param>
    public Task CheckFFmpegAsync(bool quiet)
    {
        ffmpegCheck?.Cancel();
        var check = ffmpegCheck = new CancellationTokenSource();
        return RunFFmpegCheckAsync(quiet, check.Token);
    }

    private async Task RunFFmpegCheckAsync(bool quiet, CancellationToken cancellationToken)
    {
        var path = FFmpegLocator.Find(FFmpegPath);
        if (path is null)
        {
            IsFFmpegFound = false;
            FFmpegLocation = "";
            FFmpegStatus = string.IsNullOrWhiteSpace(FFmpegPath)
                ? "FFmpeg was not found on this computer. Install it, or choose where it is."
                : "There is no FFmpeg at that place.";
            return;
        }

        if (!quiet || !IsFFmpegFound)
        {
            IsFFmpegFound = false;
            FFmpegLocation = "";
            FFmpegStatus = "Looking at FFmpeg...";
        }

        string? version;
        try
        {
            // The path is typed one letter at a time
            await Task.Delay(250, cancellationToken);
            version = await FFmpegLocator.GetVersionAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        IsFFmpegFound = version is not null;
        FFmpegLocation = version is null ? "" : path;
        FFmpegStatus = version is null ? $"{path} is not FFmpeg." : $"FFmpeg {version}";
    }

    [RelayCommand]
    private void RandomizeSeed()
    {
        SeedText = ((ulong) Random.Shared.NextInt64(1, long.MaxValue)).ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettings
        {
            // What belongs to the machine and not to taste stays
            FFmpegPath = settings.FFmpegPath,
            LastExportFolder = settings.LastExportFolder
        };

        settings.CopyFrom(defaults);
        service.Save();

        // Everything may have changed
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    public void Close()
    {
        Closing?.Invoke(this, EventArgs.Empty);
    }

    public static string FormatSpeed(double speed)
        => $"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×";

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? name = null, string[]? extra = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        assign(value);
        service.Save();

        OnPropertyChanged(name);
        if (extra is not null)
            foreach (var other in extra)
                OnPropertyChanged(other);
    }
}
