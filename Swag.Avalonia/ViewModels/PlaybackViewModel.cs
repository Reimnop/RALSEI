using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;
using Microsoft.Extensions.Logging;
using Swag.Avalonia.Integration;
using Swag.Avalonia.Models;
using Swag.Avalonia.Services;
using Swag.Avalonia.Views;
using Swag.Levels;
using Reimnop.MvvmHelper;
using ShadUI;

namespace Swag.Avalonia.ViewModels;

public enum PlaybackPhase
{
    /// <summary>There is no level, the library is shown.</summary>
    Empty,

    /// <summary>A level is being read.</summary>
    Loading,

    /// <summary>A level is there to play.</summary>
    Ready
}

[ViewModelView<PlaybackView>]
public partial class PlaybackViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan OsdDuration = TimeSpan.FromMilliseconds(1400);

    public event EventHandler<LucideIconKind>? PlayPauseIconDisplayRequested;

    /// <summary>Raised when the window should go to full screen, or leave it.</summary>
    public event EventHandler? FullScreenToggleRequested;

    public SwagViewModel SwagViewModel { get; }
    public SettingsViewModel Settings { get; }
    public LibraryViewModel Library { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsLoading), nameof(IsReady), nameof(WindowTitle))]
    public partial PlaybackPhase Phase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(LevelSubtitle), nameof(HasSubtitle))]
    public partial LevelInfo? Level { get; set; }

    [ObservableProperty]
    public partial string LoadingText { get; set; } = "";

    [ObservableProperty]
    public partial AudioPlayer? AudioPlayer { get; set; }

    /// <summary>In seconds.</summary>
    [ObservableProperty]
    public partial double Position { get; set; }

    /// <summary>In seconds.</summary>
    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    public partial bool IsPlaying { get; set; }

    /// <summary>The song reached its end and is waiting there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    public partial bool HasEnded { get; set; }

    [ObservableProperty]
    public partial int Fps { get; set; }

    [ObservableProperty]
    public partial double FrameTimeMs { get; set; }

    [ObservableProperty]
    public partial string RenderSizeText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    /// <summary>Whether the message over the level (the new volume, the seek) is shown.</summary>
    [ObservableProperty]
    public partial bool IsOsdVisible { get; set; }

    [ObservableProperty]
    public partial string OsdText { get; set; } = "";

    [ObservableProperty]
    public partial LucideIconKind OsdIcon { get; set; } = LucideIconKind.Info;

    public bool IsEmpty => Phase == PlaybackPhase.Empty;
    public bool IsLoading => Phase == PlaybackPhase.Loading;
    public bool IsReady => Phase == PlaybackPhase.Ready;

    public string WindowTitle => Level is null ? "SWAG" : $"{Level.Title} - SWAG";
    public string LevelSubtitle => string.Join("  ·  ", new[] { Level?.Artist, Level?.Creator is { } c ? $"by {c}" : null }.Where(x => x is not null));
    public bool HasSubtitle => LevelSubtitle.Length > 0;

    public LucideIconKind PlayPauseIconKind
        => IsPlaying ? LucideIconKind.Pause : HasEnded ? LucideIconKind.RotateCcw : LucideIconKind.Play;

    private readonly RecentLevelsService recents;
    private readonly WindowServices windowServices;
    private readonly ToastManager toastManager;
    private readonly ILogger<PlaybackViewModel> logger;

    private readonly DispatcherTimer fpsTimer;
    private readonly DispatcherTimer osdTimer;
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private long lastFrameTicks;
    private int countingFps;
    private bool endHandled;

    public PlaybackViewModel(
        SwagViewModel swagViewModel,
        SettingsViewModel settings,
        LibraryViewModel library,
        RecentLevelsService recents,
        WindowServices windowServices,
        ToastManager toastManager,
        ILogger<PlaybackViewModel> logger)
    {
        SwagViewModel = swagViewModel;
        Settings = settings;
        Library = library;
        this.recents = recents;
        this.windowServices = windowServices;
        this.toastManager = toastManager;
        this.logger = logger;

        SwagViewModel.TickCallback = OnTick;

        Settings.PropertyChanged += (_, e) => ApplySettings(e.PropertyName);
        ApplySettings(null);

        Library.OpenFolderRequested += (_, _) => _ = OpenFolderAsync();
        Library.OpenFilesRequested += (_, _) => _ = OpenFilesAsync();
        Library.LevelRequested += (_, level) => _ = OpenLevelAsync(level);

        fpsTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        fpsTimer.Tick += (_, _) =>
        {
            Fps = countingFps;
            countingFps = 0;
        };
        fpsTimer.Start();

        osdTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = OsdDuration };
        osdTimer.Tick += (_, _) =>
        {
            osdTimer.Stop();
            IsOsdVisible = false;
        };
    }

    public void Dispose()
    {
        fpsTimer.Stop();
        osdTimer.Stop();

        SwagViewModel.IsActive = false;
        AudioPlayer?.Dispose();
        SwagViewModel.Dispose();
    }

    #region Opening

    [RelayCommand]
    public async Task OpenFolderAsync()
    {
        var storage = windowServices.StorageProvider;
        if (storage is null)
            return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open the folder of a level",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await OpenPathsAsync([path]);
    }

    [RelayCommand]
    public async Task OpenFilesAsync()
    {
        var storage = windowServices.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a beatmap, and its song if it is not next to it",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Beatmaps and songs") { Patterns = ["*.vgd", "*.lsb", "*.ogg", "*.mp3", "*.wav"] },
                new FilePickerFileType("Beatmap") { Patterns = ["*.vgd", "*.lsb"] },
                new FilePickerFileType("Song") { Patterns = ["*.ogg", "*.mp3", "*.wav"] }
            ]
        });

        var paths = files.Select(x => x.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
            await OpenPathsAsync(paths);
    }

    /// <summary>
    /// Opens what was dropped on the window, passed on the command line or picked: a folder with a level in it,
    /// or a beatmap (and its song).
    /// </summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var result = LevelResolver.Resolve(paths);

        if (result.NeedsAudio)
            result = await AskForAudioAsync(result.BeatmapNeedingAudio!);

        if (result.Level is null)
        {
            if (result.Problem is not null)
                Fail("The level could not be opened", result.Problem);
            return;
        }

        await LoadAsync(result.Level);
    }

    public Task OpenLevelAsync(LevelInfo level)
    {
        if (!level.FilesExist)
        {
            recents.Remove(level);
            Fail("The level is gone", $"The files of '{level.Title}' were moved or deleted.");
            return Task.CompletedTask;
        }

        return LoadAsync(level);
    }

    private async Task<LevelResolver.Result> AskForAudioAsync(string beatmapPath)
    {
        var storage = windowServices.StorageProvider;
        if (storage is null)
            return new LevelResolver.Result(null, "The song of the level was not found.");

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Where is the song of {Path.GetFileName(beatmapPath)}?",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Song") { Patterns = ["*.ogg", "*.mp3", "*.wav"] }]
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } audioPath)
            return new LevelResolver.Result(null, null);

        return LevelResolver.FromFiles(beatmapPath, audioPath);
    }

    #endregion

    #region Loading

    /// <summary>Replaces the level. The reading happens off the UI thread, and no frames are made while it does.</summary>
    public async Task LoadAsync(LevelInfo level)
    {
        if (Phase == PlaybackPhase.Loading)
        {
            Notify("Still loading", "Wait for the level that is loading.");
            return;
        }

        logger.LogInformation("Opening '{Title}' ({Path})", level.Title, level.BeatmapPath);

        // Nothing touches the level until it is replaced
        SwagViewModel.IsActive = false;
        AudioPlayer?.Stop();
        AudioPlayer?.Dispose();
        AudioPlayer = null;
        Level = null;

        LoadingText = level.Title;
        Phase = PlaybackPhase.Loading;
        Position = 0;
        Duration = 0;
        IsPlaying = false;
        HasEnded = false;
        endHandled = false;

        SwagViewModel.RandomSeedService.Seed = Settings.RandomSeed
            ? (ulong) Random.Shared.NextInt64(1, long.MaxValue)
            : Settings.SeedText is { } text && ulong.TryParse(text, out var seed) ? seed : 1;

        AudioPlayer? audio = null;
        try
        {
            audio = await Task.Run(() =>
            {
                var player = AudioPlayer.Load(level.AudioPath);
                try
                {
                    using var stream = new BufferedStream(File.OpenRead(level.BeatmapPath), 1 << 20);
                    SwagViewModel.BeatmapService.LoadBeatmap(stream, level.Format);
                    return player;
                }
                catch
                {
                    player.Dispose();
                    throw;
                }
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Opening '{Title}' failed", level.Title);

            // The beatmap that was there is cleared by now, there is nothing to go back to
            Phase = PlaybackPhase.Empty;
            Fail("The level could not be opened", Describe(e));
            return;
        }

        AudioPlayer = audio;
        ApplyAudioSettings();

        Level = level;
        Duration = audio.Length;
        Phase = PlaybackPhase.Ready;
        SwagViewModel.IsActive = true;

        recents.Add(level);

        if (AudioPlayer.IsSilent)
            Notify("No sound", "There is no audio output, the level plays without sound.");

        if (Settings.AutoPlay)
            audio.Play();
    }

    [RelayCommand]
    public void CloseLevel()
    {
        if (Phase != PlaybackPhase.Ready)
            return;

        SwagViewModel.IsActive = false;
        AudioPlayer?.Stop();
        AudioPlayer?.Dispose();
        AudioPlayer = null;

        Level = null;
        Phase = PlaybackPhase.Empty;
        IsPlaying = false;
        HasEnded = false;
        Position = 0;
        Duration = 0;
    }

    private static string Describe(Exception e)
    {
        var inner = e;
        while (inner.InnerException is not null && inner is AggregateException)
            inner = inner.InnerException;

        return inner switch
        {
            JsonException => "The beatmap is not valid, or it is in a format this version does not understand.",
            FileNotFoundException => "A file of the level could not be found. It might have been moved or deleted.",
            _ => string.IsNullOrWhiteSpace(inner.Message) ? inner.GetType().Name : inner.Message
        };
    }

    #endregion

    #region Controls

    [RelayCommand]
    public void PlayPause()
    {
        if (AudioPlayer is null)
            return;

        if (AudioPlayer.Playing)
            AudioPlayer.Pause();
        else
            AudioPlayer.Play();

        HasEnded = false;
        endHandled = false;
        IsPlaying = AudioPlayer.Playing;
    }

    /// <summary>Plays or pauses, and flashes the icon of what happened over the level.</summary>
    [RelayCommand]
    public void PlayPauseDisplayIcon()
    {
        if (AudioPlayer is null)
            return;

        var willPlay = !AudioPlayer.Playing;
        PlayPause();
        PlayPauseIconDisplayRequested?.Invoke(this, willPlay ? LucideIconKind.Play : LucideIconKind.Pause);
    }

    [RelayCommand]
    public void SeekBy(double seconds)
    {
        if (AudioPlayer is null)
            return;

        SeekTo(AudioPlayer.Position + seconds);
        ShowOsd(seconds > 0 ? LucideIconKind.FastForward : LucideIconKind.Rewind, $"{(seconds > 0 ? "+" : "-")}{Math.Abs(seconds):0} s");
    }

    /// <summary>Moves the song to a time. Also what the timeline does, as it is dragged.</summary>
    public void SeekTo(double seconds)
    {
        if (AudioPlayer is null)
            return;

        AudioPlayer.Position = seconds;
        Position = AudioPlayer.Position;

        // Seeking back from the end goes on playing from there
        if (HasEnded)
        {
            HasEnded = false;
            endHandled = false;
            AudioPlayer.Play();
        }
    }

    [RelayCommand]
    public void Restart()
    {
        if (AudioPlayer is null)
            return;

        SeekTo(0);
        if (!AudioPlayer.Playing)
            AudioPlayer.Play();
        ShowOsd(LucideIconKind.RotateCcw, "From the start");
    }

    [RelayCommand]
    public void ChangeVolume(double delta)
    {
        Settings.IsMuted = false;
        Settings.Volume += delta;
        ShowOsd(Settings.VolumeIconKind, Settings.VolumeText);
    }

    [RelayCommand]
    public void ToggleMute()
    {
        Settings.IsMuted = !Settings.IsMuted;
        ShowOsd(Settings.VolumeIconKind, Settings.VolumeText);
    }

    [RelayCommand]
    public void ChangeSpeed(int direction)
    {
        var speeds = AppSettings.PlaybackSpeeds;
        var current = Array.FindIndex(speeds, x => Math.Abs(x - Settings.PlaybackSpeed) < 0.001);
        if (current < 0)
            current = Array.IndexOf(speeds, 1.0);

        Settings.PlaybackSpeed = speeds[Math.Clamp(current + direction, 0, speeds.Length - 1)];
        ShowOsd(LucideIconKind.Gauge, $"Speed {Settings.PlaybackSpeedText}");
    }

    [RelayCommand]
    public void SetSpeed(double speed)
    {
        Settings.PlaybackSpeed = speed;
        ShowOsd(LucideIconKind.Gauge, $"Speed {Settings.PlaybackSpeedText}");
    }

    [RelayCommand]
    public void ToggleLoop()
    {
        Settings.Loop = !Settings.Loop;
        ShowOsd(LucideIconKind.Repeat, Settings.Loop ? "Loop on" : "Loop off");
    }

    [RelayCommand]
    public void ToggleStats()
    {
        Settings.ShowStats = !Settings.ShowStats;
    }

    [RelayCommand]
    public void TogglePostProcessing()
    {
        Settings.EnablePostProcessing = !Settings.EnablePostProcessing;
        ShowOsd(LucideIconKind.Sparkles, Settings.EnablePostProcessing ? "Post-processing on" : "Post-processing off");
    }

    [RelayCommand]
    public void ToggleFullScreen()
    {
        FullScreenToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Draws the randomness of the level again, e.g. where its particles go.</summary>
    [RelayCommand]
    public void Reseed()
    {
        if (!IsReady)
            return;

        SwagViewModel.RandomSeedService.Seed = (ulong) Random.Shared.NextInt64(1, long.MaxValue);
        ShowOsd(LucideIconKind.Dices, "New random seed");
    }

    #endregion

    #region Frames

    /// <summary>Called for every frame, on the UI thread: this moves the level along.</summary>
    public float OnTick()
    {
        var audio = AudioPlayer;
        var time = audio?.Position ?? 0;

        if (audio is not null)
            HandleEnd(audio);

        if (Math.Abs(time - Position) > 0.0005)
            Position = time;

        var playing = audio?.Playing ?? false;
        if (playing != IsPlaying)
            IsPlaying = playing;

        countingFps++;

        var renderSettings = SwagViewModel.RenderSettings;
        var renderSize = $"{renderSettings.RenderWidth} × {renderSettings.RenderHeight}";
        if (renderSize != RenderSizeText)
            RenderSizeText = renderSize;

        var now = frameClock.ElapsedTicks;
        if (lastFrameTicks != 0)
        {
            var milliseconds = (now - lastFrameTicks) * 1000.0 / Stopwatch.Frequency;

            // Smoothed, or it would be unreadable
            FrameTimeMs = FrameTimeMs == 0 ? milliseconds : FrameTimeMs * 0.9 + milliseconds * 0.1;
        }
        lastFrameTicks = now;

        return (float) time;
    }

    private void HandleEnd(AudioPlayer audio)
    {
        if (!audio.HasEnded)
        {
            endHandled = false;
            return;
        }

        if (Settings.Loop)
        {
            audio.Position = 0;
            audio.Play();
            return;
        }

        if (endHandled)
            return;

        endHandled = true;
        HasEnded = true;
        IsPlaying = false;
    }

    #endregion

    #region Settings

    private void ApplySettings(string? name)
    {
        // The name is empty when everything changed
        var all = string.IsNullOrEmpty(name);

        if (all || name is nameof(SettingsViewModel.Volume) or nameof(SettingsViewModel.IsMuted) or nameof(SettingsViewModel.PlaybackSpeed))
            ApplyAudioSettings();

        if (all || name == nameof(SettingsViewModel.EnablePostProcessing))
            SwagViewModel.Director.EnablePostProcessing = Settings.EnablePostProcessing;

        if (all || name == nameof(SettingsViewModel.EnableTextRendering))
            SwagViewModel.Director.EnableTextRendering = Settings.EnableTextRendering;

        if (all || name == nameof(SettingsViewModel.RenderScaleIndex))
            SwagViewModel.RenderSettings.RenderScale = Settings.RenderScale;
    }

    private void ApplyAudioSettings()
    {
        if (AudioPlayer is null)
            return;

        AudioPlayer.Volume = Settings.IsMuted ? 0 : Settings.Volume;
        AudioPlayer.Speed = Settings.PlaybackSpeed;
    }

    #endregion

    #region Messages

    public void ShowOsd(LucideIconKind icon, string text)
    {
        OsdIcon = icon;
        OsdText = text;
        IsOsdVisible = true;

        osdTimer.Stop();
        osdTimer.Start();
    }

    private void Notify(string title, string message)
    {
        toastManager.CreateToast(title).WithContent(message).DismissOnClick().ShowInfo();
    }

    private void Fail(string title, string message)
    {
        toastManager.CreateToast(title).WithContent(message).WithDelay(8).DismissOnClick().ShowError();
    }

    #endregion
}
