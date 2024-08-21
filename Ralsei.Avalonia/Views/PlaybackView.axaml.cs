using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lucide.Avalonia;
using Ralsei.Avalonia.Controls;
using Ralsei.Avalonia.ViewModels;

namespace Ralsei.Avalonia.Views;

public partial class PlaybackView : UserControl
{
    // How long nothing has to move for the bars to go away, while playing
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(2500);

    // A click is a click and not the first half of a double click, after this long
    private static readonly TimeSpan DoubleClickDelay = TimeSpan.FromMilliseconds(230);

    private readonly PlayPauseAnimation playPauseAnim;
    private readonly DispatcherTimer idleTimer;
    private readonly DispatcherTimer clickTimer;

    private PlaybackViewModel? currentViewModel;
    private Point lastPointerPosition;
    private int pressedClickCount;

    public PlaybackView()
    {
        InitializeComponent();

        playPauseAnim = new PlayPauseAnimation(PlayPauseIconContainer);

        idleTimer = new DispatcherTimer { Interval = IdleDelay };
        idleTimer.Tick += (_, _) =>
        {
            idleTimer.Stop();
            UpdateIdle();
        };

        clickTimer = new DispatcherTimer { Interval = DoubleClickDelay };
        clickTimer.Tick += (_, _) =>
        {
            clickTimer.Stop();
            currentViewModel?.PlayPauseDisplayIcon();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (currentViewModel is not null)
        {
            currentViewModel.PlayPauseIconDisplayRequested -= OnPlayPauseIconDisplayRequested;
            currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        currentViewModel = DataContext as PlaybackViewModel;

        if (currentViewModel is not null)
        {
            currentViewModel.PlayPauseIconDisplayRequested += OnPlayPauseIconDisplayRequested;
            currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The bars come back when there is nothing playing, and wait to go away again when it starts
        if (e.PropertyName is nameof(PlaybackViewModel.IsPlaying) or nameof(PlaybackViewModel.Phase) or nameof(PlaybackViewModel.IsFullScreen))
            Wake();
    }

    private void OnPlayPauseIconDisplayRequested(object? sender, LucideIconKind e)
    {
        PlayPauseIcon.Kind = e;
        _ = playPauseAnim.Trigger();
    }

    private void Scrubber_OnSeekRequested(object? sender, SeekRequestedEventArgs e)
    {
        currentViewModel?.SeekTo(e.Position);
    }

    private void Root_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // A touch of the mouse that is not a move (the layout changing under it) does not count
        var position = e.GetPosition(this);
        if ((position - lastPointerPosition).X is > -2 and < 2 && (position - lastPointerPosition).Y is > -2 and < 2)
            return;

        lastPointerPosition = position;
        Wake();
    }

    private void InteractionLayer_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        pressedClickCount = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e.ClickCount : 0;
    }

    private void InteractionLayer_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (currentViewModel is null || e.InitialPressMouseButton != MouseButton.Left)
            return;

        e.Handled = true;

        if (pressedClickCount >= 2)
        {
            // The second half of a double click: no play or pause, full screen instead
            clickTimer.Stop();
            currentViewModel.ToggleFullScreen();
            return;
        }

        // Wait to see if another click follows
        clickTimer.Stop();
        clickTimer.Start();
    }

    /// <summary>
    /// Brings the bars back, and starts waiting for them to go.
    /// </summary>
    private void Wake()
    {
        Root.Classes.Set("Idle", false);

        idleTimer.Stop();
        idleTimer.Start();
    }

    private void UpdateIdle()
    {
        if (currentViewModel is not { IsReady: true, IsPlaying: true })
            return;

        // Not while somebody is using them
        if (TopBar.IsPointerOver || BottomBar.IsPointerOver)
        {
            idleTimer.Start();
            return;
        }

        Root.Classes.Set("Idle", true);
    }
}
