using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ralsei.Avalonia.Models;
using Ralsei.Avalonia.Services;
using Ralsei.Avalonia.Views;
using Reimnop.MvvmHelper;
using ShadUI;

namespace Ralsei.Avalonia.ViewModels;

[ViewModelView<MainWindow>]
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public PlaybackViewModel PlaybackViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public ExportViewModel ExportViewModel { get; }
    public ToastManager ToastManager { get; }
    public WindowServices WindowServices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyOverlayOpen))]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyOverlayOpen))]
    public partial bool IsExportOpen { get; set; }

    /// <summary>
    /// Whether the level fills the screen, without the title bar.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    public bool IsAnyOverlayOpen => IsSettingsOpen || IsExportOpen;

    /// <summary>
    /// There is something to make a video of, or a video that is being made to look at.
    /// </summary>
    public bool CanOpenExport => PlaybackViewModel.IsReady || ExportViewModel.IsRunning;

    public string WindowTitle => PlaybackViewModel.WindowTitle;

    public MainWindowViewModel(
        PlaybackViewModel playbackViewModel,
        SettingsViewModel settingsViewModel,
        ExportViewModel exportViewModel,
        ToastManager toastManager,
        WindowServices windowServices)
    {
        PlaybackViewModel = playbackViewModel;
        SettingsViewModel = settingsViewModel;
        ExportViewModel = exportViewModel;
        ToastManager = toastManager;
        WindowServices = windowServices;

        SettingsViewModel.Closing += (_, _) => IsSettingsOpen = false;
        SettingsViewModel.PropertyChanged += OnSettingsChanged;
        PlaybackViewModel.PropertyChanged += OnPlaybackChanged;
        ExportViewModel.CloseRequested += (_, _) => IsExportOpen = false;
        ExportViewModel.FFmpegSettingsRequested += (_, _) => OpenSettings(ExportSettingsTab);
        ExportViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExportViewModel.Phase))
                OnPropertyChanged(nameof(CanOpenExport));
        };
        PlaybackViewModel.FullScreenToggleRequested += (_, _) => IsFullScreen = !IsFullScreen;

        ApplyTheme();
    }

    private const int ExportSettingsTab = 2;

    partial void OnIsFullScreenChanged(bool value)
        => PlaybackViewModel.IsFullScreen = value;

    /// <summary>
    /// Opens the settings, at one of its tabs.
    /// </summary>
    [RelayCommand]
    private void OpenSettings(int? tab)
    {
        if (tab is { } index)
            SettingsViewModel.SelectedTabIndex = index;

        IsSettingsOpen = true;
    }

    /// <summary>
    /// Opens the dialog to make a video of the level.
    /// </summary>
    [RelayCommand]
    private void OpenExport()
    {
        if (!CanOpenExport)
            return;

        ExportViewModel.Prepare();
        IsExportOpen = true;
    }

    /// <summary>
    /// What the Escape key does: gets out of whatever is in the way, the innermost first.
    /// </summary>
    public bool OnEscape()
    {
        if (IsSettingsOpen)
        {
            IsSettingsOpen = false;
            return true;
        }

        if (IsExportOpen)
        {
            IsExportOpen = false;
            return true;
        }

        if (IsFullScreen)
        {
            IsFullScreen = false;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        PlaybackViewModel.Dispose();
    }

    private void OnPlaybackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.WindowTitle))
            OnPropertyChanged(nameof(WindowTitle));
        else if (e.PropertyName == nameof(PlaybackViewModel.Phase))
            OnPropertyChanged(nameof(CanOpenExport));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.ThemeIndex))
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = (ThemeChoice) SettingsViewModel.ThemeIndex switch
        {
            ThemeChoice.Dark => ThemeVariant.Dark,
            ThemeChoice.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
