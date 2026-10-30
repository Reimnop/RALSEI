using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Swag.Avalonia.ViewModels;

namespace Swag.Avalonia.Views;

public partial class MainWindow : ShadUI.Window
{
    private const int ShortcutsTab = 4;

    private MainWindowViewModel? viewModel;
    private WindowState stateBeforeFullScreen = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (viewModel is not null)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        viewModel = DataContext as MainWindowViewModel;

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.WindowServices.TopLevel = this;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // A level can be given on the command line, e.g. when something was opened with the app
        var paths = App.StartupArgs.Where(x => !x.StartsWith('-')).ToList();
        if (paths.Count > 0 && viewModel is not null)
            _ = viewModel.PlaybackViewModel.OpenPathsAsync(paths);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsFullScreen):
                SetFullScreen(viewModel.IsFullScreen);
                break;
            case nameof(MainWindowViewModel.IsAnyOverlayOpen) when !viewModel.IsAnyOverlayOpen:
                // An overlay that faded away is still there, and so is the focus that was in it:
                // the keys would go to it instead of the player
                FocusManager?.Focus(null);
                break;
        }
    }

    private void SetFullScreen(bool fullScreen)
    {
        if (fullScreen)
        {
            if (WindowState != WindowState.FullScreen)
                stateBeforeFullScreen = WindowState;

            WindowState = WindowState.FullScreen;
        }
        else if (WindowState == WindowState.FullScreen)
        {
            WindowState = stateBeforeFullScreen;
        }
    }

    #region Keys

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || viewModel is null)
            return;

        if (HandleKey(viewModel, e))
            e.Handled = true;
    }

    private static bool HandleKey(MainWindowViewModel vm, KeyEventArgs e)
    {
        var playback = vm.PlaybackViewModel;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (alt)
            return false;

        if (e.Key == Key.Escape)
            return vm.OnEscape();

        // With the settings open, the keys are for the settings
        if (vm.IsAnyOverlayOpen)
            return false;

        if (control)
        {
            switch (e.Key)
            {
                case Key.O:
                    (shift ? playback.OpenFilesCommand : playback.OpenFolderCommand).Execute(null);
                    return true;
                case Key.OemComma:
                    vm.OpenSettingsCommand.Execute(null);
                    return true;
                case Key.E:
                    vm.OpenExportCommand.Execute(null);
                    return true;
                case Key.L when playback.IsReady:
                    playback.ToggleLoopCommand.Execute(null);
                    return true;
                default:
                    return false;
            }
        }

        if (e.Key == Key.F1)
        {
            vm.OpenSettingsCommand.Execute(ShortcutsTab);
            return true;
        }

        if (e.Key is Key.F or Key.F11)
        {
            playback.ToggleFullScreenCommand.Execute(null);
            return true;
        }

        // The rest is for a level that is playing
        if (!playback.IsReady)
            return false;

        switch (e.Key)
        {
            case Key.Space or Key.K:
                playback.PlayPauseDisplayIconCommand.Execute(null);
                return true;
            case Key.Left:
                playback.SeekByCommand.Execute(-5.0);
                return true;
            case Key.Right:
                playback.SeekByCommand.Execute(5.0);
                return true;
            case Key.J:
                playback.SeekByCommand.Execute(-10.0);
                return true;
            case Key.L:
                playback.SeekByCommand.Execute(10.0);
                return true;
            case Key.Home:
                playback.RestartCommand.Execute(null);
                return true;
            case Key.Up:
                playback.ChangeVolumeCommand.Execute(0.05);
                return true;
            case Key.Down:
                playback.ChangeVolumeCommand.Execute(-0.05);
                return true;
            case Key.M:
                playback.ToggleMuteCommand.Execute(null);
                return true;
            case Key.OemOpenBrackets:
                playback.ChangeSpeedCommand.Execute(-1);
                return true;
            case Key.OemCloseBrackets:
                playback.ChangeSpeedCommand.Execute(1);
                return true;
            case Key.I:
                playback.ToggleStatsCommand.Execute(null);
                return true;
            case Key.P:
                playback.TogglePostProcessingCommand.Execute(null);
                return true;
            case Key.R:
                playback.ReseedCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    #endregion

    #region Drag and drop

    private void OnDragEnter(object? sender, DragEventArgs e)
        => UpdateDrag(e);

    private void OnDragOver(object? sender, DragEventArgs e)
        => UpdateDrag(e);

    private void OnDragLeave(object? sender, DragEventArgs e)
        => DropOverlay.IsVisible = false;

    private void UpdateDrag(DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);

        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = hasFiles;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;

        if (viewModel is null)
            return;

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(x => x.TryGetLocalPath())
            .OfType<string>()
            .ToList();

        if (paths is { Count: > 0 })
        {
            // Not from inside the handler: the drag is not over until it returns
            Dispatcher.UIThread.Post(() => _ = viewModel.PlaybackViewModel.OpenPathsAsync(paths));
        }
    }

    #endregion
}
