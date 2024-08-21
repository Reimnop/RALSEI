using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Ralsei.Avalonia.Export;
using Ralsei.Avalonia.Services;
using Ralsei.Avalonia.Views;
using Reimnop.MvvmHelper;
using ShadUI;

namespace Ralsei.Avalonia.ViewModels;

public enum ExportPhase
{
    /// <summary>
    /// Choosing what to make.
    /// </summary>
    Setup,

    /// <summary>
    /// The video is being made.
    /// </summary>
    Running,

    Finished,
    Failed
}

/// <summary>
/// Makes a video of the level that is open.
/// </summary>
[ViewModelView<ExportView>]
public partial class ExportViewModel : ViewModelBase
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".webm"];

    public SettingsViewModel Settings { get; }
    public PlaybackViewModel Playback { get; }

    public event EventHandler? CloseRequested;
    public event EventHandler? FFmpegSettingsRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsRunning), nameof(IsFinished), nameof(IsFailed), nameof(CanStart))]
    public partial ExportPhase Phase { get; set; }

    /// <summary>
    /// Where the video goes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(OutputName), nameof(OutputFolder))]
    public partial string OutputPath { get; set; } = "";

    /// <summary>
    /// What part of the level: the whole of it, or the rest of it from where the player is.
    /// </summary>
    [ObservableProperty]
    public partial int RangeIndex { get; set; }

    public static readonly string[] RangeOptions = ["The whole level", "From where the player is, to the end"];

    /// <summary>
    /// How long the video of that part is.
    /// </summary>
    [ObservableProperty]
    public partial string RangeText { get; set; } = "";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    [ObservableProperty]
    public partial string DetailText { get; set; } = "";

    [ObservableProperty]
    public partial string ResultText { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorDetails { get; set; } = "";

    public bool IsSetup => Phase == ExportPhase.Setup;
    public bool IsRunning => Phase == ExportPhase.Running;
    public bool IsFinished => Phase == ExportPhase.Finished;
    public bool IsFailed => Phase == ExportPhase.Failed;

    public bool HasErrorDetails => ErrorDetails.Length > 0;

    public bool CanStart => Phase == ExportPhase.Setup && Playback.IsReady && OutputPath.Length > 0 && Settings.IsFFmpegFound;

    public string OutputName => Path.GetFileName(OutputPath);
    public string OutputFolder => Path.GetDirectoryName(OutputPath) ?? "";

    /// <summary>
    /// Short text for the title bar while a video is being made.
    /// </summary>
    public string BadgeText => $"{Math.Floor(Progress * 100):0}%";

    private readonly FFmpegExporter exporter;
    private readonly WindowServices windowServices;
    private readonly ToastManager toastManager;
    private readonly ILogger<ExportViewModel> logger;

    private CancellationTokenSource? cancellation;

    public ExportViewModel(
        SettingsViewModel settings,
        PlaybackViewModel playback,
        FFmpegExporter exporter,
        WindowServices windowServices,
        ToastManager toastManager,
        ILogger<ExportViewModel> logger)
    {
        Settings = settings;
        Playback = playback;
        this.exporter = exporter;
        this.windowServices = windowServices;
        this.toastManager = toastManager;
        this.logger = logger;

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.IsFFmpegFound) or nameof(SettingsViewModel.ExportSizeIndex) or nameof(SettingsViewModel.ExportFramerateIndex) or "")
            {
                OnPropertyChanged(nameof(CanStart));
                UpdateSummary();
            }
        };

        Playback.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlaybackViewModel.Phase))
                OnPropertyChanged(nameof(CanStart));
        };
    }

    partial void OnProgressChanged(double value)
        => OnPropertyChanged(nameof(BadgeText));

    partial void OnErrorDetailsChanged(string value)
        => OnPropertyChanged(nameof(HasErrorDetails));

    partial void OnRangeIndexChanged(int value)
        => UpdateSummary();

    /// <summary>
    /// Gets ready to be shown: looks at the level that is open now.
    /// </summary>
    public void Prepare()
    {
        if (Phase == ExportPhase.Running)
            return;

        // What was finished or failed last time is not of interest any more
        Phase = ExportPhase.Setup;

        var level = Playback.Level;
        if (level is null)
            return;

        if (Playback.Duration - Playback.Position < 1)
            RangeIndex = 0;

        if (string.IsNullOrEmpty(OutputPath) || File.Exists(OutputPath) || Path.GetFileNameWithoutExtension(OutputPath) != SafeName(level.Title))
            OutputPath = SuggestPath(level.Title);

        UpdateSummary();
        // FFmpeg may have been installed since it was looked for
        _ = Settings.CheckFFmpegAsync(quiet: true);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var storage = windowServices.StorageProvider;
        if (storage is null)
            return;

        var folder = Path.GetDirectoryName(OutputPath);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the video as",
            SuggestedFileName = Path.GetFileNameWithoutExtension(OutputPath),
            DefaultExtension = "mp4",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = folder is null ? null : await storage.TryGetFolderFromPathAsync(folder),
            FileTypeChoices =
            [
                new FilePickerFileType("Video") { Patterns = VideoExtensions.Select(x => $"*{x}").ToArray() }
            ]
        });

        if (file?.TryGetLocalPath() is { } path)
            OutputPath = Path.HasExtension(path) ? path : path + ".mp4";
    }

    [RelayCommand]
    private void OpenFFmpegSettings()
        => FFmpegSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task StartAsync()
    {
        if (Playback.Level is not { } level)
            return;

        var start = RangeIndex == 1 ? Math.Clamp(Playback.Position, 0, Math.Max(0, Playback.Duration - 1)) : 0;
        var duration = Playback.Duration - start;

        var request = new ExportRequest(
            level,
            OutputPath,
            Settings.ExportSize.Width,
            Settings.ExportSize.Height,
            Settings.ExportFramerate,
            start,
            duration,
            // The video of the level as it plays now: the same randomness
            Playback.RalseiViewModel.RandomSeedService.Seed,
            Settings.EnablePostProcessing,
            Settings.EnableTextRendering,
            Settings.FFmpegPath,
            Settings.ExportArguments);

        Progress = 0;
        ProgressText = "Getting ready...";
        DetailText = "";
        ErrorTitle = "";
        ErrorDetails = "";

        cancellation = new CancellationTokenSource();
        Phase = ExportPhase.Running;

        var clock = Stopwatch.StartNew();
        try
        {
            await exporter.RunAsync(request, new Progress<ExportProgress>(OnProgress), cancellation.Token);

            Settings.LastExportFolder = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));

            var size = new FileInfo(request.OutputPath).Length;
            ResultText = $"{FormatTime(duration)} long, {FormatSize(size)}, made in {FormatTime(clock.Elapsed.TotalSeconds)}";
            Progress = 1;
            Phase = ExportPhase.Finished;

            toastManager.CreateToast("The video is ready")
                .WithContent(Path.GetFileName(request.OutputPath))
                .WithAction("Show in folder", ShowInFolder)
                .WithDelay(10)
                .DismissOnClick()
                .ShowSuccess();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The export was cancelled");
            Phase = ExportPhase.Setup;
            toastManager.CreateToast("The export was cancelled").DismissOnClick().ShowInfo();
        }
        catch (ExportException e)
        {
            Fail(e.Message, e.Details ?? "", e);
        }
        catch (Exception e)
        {
            Fail("The video could not be made", e.Message, e);
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
        }
    }

    [RelayCommand]
    private void Cancel()
        => cancellation?.Cancel();

    [RelayCommand]
    private void Again()
    {
        Phase = ExportPhase.Setup;
        Prepare();
    }

    [RelayCommand]
    private void Close()
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowInFolder()
    {
        if (!File.Exists(OutputPath))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(OutputPath)}\"") { UseShellExecute = true });
            else if (windowServices.Launcher is { } launcher)
                _ = launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(OutputFolder));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The folder of the video could not be shown");
        }
    }

    [RelayCommand]
    private async Task PlayVideoAsync()
    {
        if (windowServices.Launcher is { } launcher && File.Exists(OutputPath))
            await launcher.LaunchFileInfoAsync(new FileInfo(OutputPath));
    }

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        if (windowServices.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(string.IsNullOrEmpty(ErrorDetails) ? ErrorTitle : $"{ErrorTitle}{Environment.NewLine}{ErrorDetails}");
    }

    private void OnProgress(ExportProgress progress)
    {
        if (Phase != ExportPhase.Running)
            return;

        Progress = progress.Fraction;
        ProgressText = $"{Math.Floor(progress.Fraction * 100):0}%  ·  {progress.Frame:N0} of {progress.TotalFrames:N0} frames";

        var speed = progress.FramesPerSecond;
        DetailText = speed <= 0
            ? "Getting the speed..."
            : progress.Remaining is { } remaining
                ? $"{speed:0} frames per second  ·  {FormatRemaining(remaining)}"
                : $"{speed:0} frames per second";
    }

    private void Fail(string title, string details, Exception e)
    {
        logger.LogError(e, "Exporting failed: {Title}", title);

        ErrorTitle = title;
        ErrorDetails = details;
        Phase = ExportPhase.Failed;

        toastManager.CreateToast(title).WithContent("The video was not made.").WithDelay(8).DismissOnClick().ShowError();
    }

    private void UpdateSummary()
    {
        RangeText = RangeIndex == 1
            ? $"From {FormatTime(Playback.Position)}, {FormatTime(Playback.Duration - Playback.Position)} long"
            : $"{FormatTime(Playback.Duration)} long";

        var level = Playback.Level;
        SummaryText = level is null
            ? ""
            : $"{SettingsViewModel.ExportSizeNames[Settings.ExportSizeIndex]} at {SettingsViewModel.ExportFramerateNames[Settings.ExportFramerateIndex]} frames per second";
    }

    private string SuggestPath(string title)
    {
        var folder = Settings.LastExportFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                folder = Path.GetDirectoryName(Playback.Level?.BeatmapPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var name = SafeName(title);
        var path = Path.Combine(folder, name + ".mp4");

        // Not over a video that is there
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}).mp4");

        return path;
    }

    private static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(x => invalid.Contains(x) ? '_' : x).ToArray()).Trim().TrimEnd('.');
        return name.Length == 0 ? "Level" : name;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int) time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    private static string FormatRemaining(TimeSpan remaining)
        => remaining.TotalSeconds < 5 ? "a few seconds left"
            : remaining.TotalMinutes < 1 ? $"{remaining.Seconds} seconds left"
            : remaining.TotalHours < 1 ? $"about {Math.Ceiling(remaining.TotalMinutes):0} minutes left"
            : $"about {remaining.TotalHours:0.#} hours left";

    private static string FormatSize(long bytes)
        => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double) (1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double) (1L << 20):0.0} MB",
            _ => $"{Math.Max(1, bytes / 1024)} KB"
        };
}
