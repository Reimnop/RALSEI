using System.Diagnostics;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.Button;
using Google.Android.Material.Chip;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.TextView;
using Org.Json;
using Swag.Android.Library;
using Swag.Android.Playback;
using Swag.Android.Settings;
using Swag.Android.UI;
using Activity = Android.App.Activity;
using Uri = Android.Net.Uri;

namespace Swag.Android.Activities;

/// <summary>Plays a level: the level fills the screen, and a touch brings up the controls.</summary>
[Activity(
    Name = "com.reimnop.swag.PlayerActivity",
    Label = "@string/app_name",
    Theme = "@style/player_theme",
    // Debug builds can be started from adb to try a level without going through the file picker
    Exported = IsDebugBuild,
    // The window is resized by the system (rotating, multi-window) and the level follows without being restarted
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.Density | ConfigChanges.UiMode |
                           ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation |
                           ConfigChanges.LayoutDirection | ConfigChanges.Locale | ConfigChanges.FontScale,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ResizeableActivity = true)]
public class PlayerActivity : Activity
{
#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    public const string ExtraLevel = "level";

    private const long ControlsHideDelayMs = 3000;
    private const long TickDelayMs = 100;
    private const double SeekStepSeconds = 10;

    private SettingsStore settingsStore = null!;
    private PlayerSettings settings = null!;
    private LevelInfo level = null!;
    private PlaybackSession? session;
    private AudioFocusController? audioFocus;

    private GraphicsSurfaceView surfaceView = null!;
    private View controls = null!;
    private View loadingOverlay = null!;
    private View errorOverlay = null!;
    private MaterialTextView loadingText = null!;
    private MaterialTextView errorTitle = null!;
    private MaterialTextView errorMessage = null!;
    private MaterialButton errorAction = null!;
    private View statsContainer = null!;
    private MaterialTextView statsText = null!;
    private MaterialTextView seekHint = null!;
    private MaterialTextView timeCurrent = null!;
    private MaterialTextView timeTotal = null!;
    private SeekBar seekBar = null!;
    private ImageButton playPauseButton = null!;

    private readonly Handler handler = new(Looper.MainLooper!);
    private Java.Lang.Runnable? tickRunnable;
    private Java.Lang.Runnable? hideControlsRunnable;
    private BottomSheetDialog? settingsSheet;

    // Set once the screen is built. A level that can not even be read ends the activity before that.
    private bool initialized;
    private bool controlsVisible;
    private bool isSeeking;
    private bool isLoaded;
    private bool failed;
    private int surfaceWidth;
    private int surfaceHeight;
    private Action? errorActionHandler;

    // Frame rate is measured over the time between two updates of the stats
    private readonly Stopwatch statsClock = Stopwatch.StartNew();
    private long statsLastFrames;
    private long statsLastTicks;
    private double fps;


    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (!TryReadLevel(out var levelFromIntent))
        {
            Toast.MakeText(this, Resource.String.error_load_title, ToastLength.Long)!.Show();
            Finish();
            return;
        }

        level = levelFromIntent;
        settingsStore = new SettingsStore(this);
        settings = settingsStore.Load();
#if DEBUG
        ApplyDebugOverrides();
#endif

        EdgeToEdge.Enable(this);
        SetContentView(Resource.Layout.activity_player);
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);

        FindViews();
        SetUpSurface();
        SetUpControls();

        titleView().Text = level.Title;
        subtitleView().Text = level.Artist ?? level.Creator ?? "";
        subtitleView().Visibility = string.IsNullOrEmpty(subtitleView().Text) ? ViewStates.Gone : ViewStates.Visible;

        audioFocus = new AudioFocusController(this, OnAudioFocusLost, OnAudioFocusGained);

        tickRunnable = new Java.Lang.Runnable(Tick);
        hideControlsRunnable = new Java.Lang.Runnable(HideControlsIfIdle);

        initialized = true;
        StartSession();
    }

    private MaterialTextView titleView() => FindViewById<MaterialTextView>(Resource.Id.title)!;
    private MaterialTextView subtitleView() => FindViewById<MaterialTextView>(Resource.Id.subtitle)!;

    protected override void OnResume()
    {
        base.OnResume();

        if (!initialized)
            return;

        // The window can still be there from before, when the system did not take it away while the app was paused.
        // If it was, this is the only thing that tells the session. A new window announces itself.
        if (surfaceView.Holder?.Surface is { IsValid: true } surface)
            session?.AttachSurface(surface);

        HideSystemBars();
        session?.Resume(SuspendReason.AppPaused);
        handler.Post(tickRunnable!);
    }

    protected override void OnPause()
    {
        base.OnPause();

        if (!initialized)
            return;

        // Nobody is looking, so the song waits until somebody is
        session?.Suspend(SuspendReason.AppPaused);
        handler.RemoveCallbacks(tickRunnable!);

        // The renderer is taken down now, while the window is still whole. Once the system has started to destroy
        // the window (which it does soon after this), the graphics driver makes everything that touches it wait
        // until that is done, and the UI thread would have to wait along if the renderer were still around then.
        session?.DetachSurface();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (!initialized)
            return;

        handler.RemoveCallbacksAndMessages(null);
        settingsSheet?.Dismiss();
        audioFocus?.Abandon();

        session?.Dispose();
        session = null;
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
            HideSystemBars();
    }

    #region Setup

    private bool TryReadLevel(out LevelInfo result)
    {
        result = null!;

        var json = Intent?.GetStringExtra(ExtraLevel);
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            if (LevelInfo.FromJson(new JSONObject(json)) is not { } parsed)
                return false;

            result = parsed;
            return true;
        }
        catch (JSONException)
        {
            return false;
        }
    }

    private void FindViews()
    {
        controls = FindViewById<View>(Resource.Id.controls)!;
        loadingOverlay = FindViewById<View>(Resource.Id.loading)!;
        errorOverlay = FindViewById<View>(Resource.Id.error)!;
        loadingText = FindViewById<MaterialTextView>(Resource.Id.loading_text)!;
        errorTitle = FindViewById<MaterialTextView>(Resource.Id.error_title)!;
        errorMessage = FindViewById<MaterialTextView>(Resource.Id.error_message)!;
        errorAction = FindViewById<MaterialButton>(Resource.Id.error_action)!;
        statsContainer = FindViewById<View>(Resource.Id.stats_container)!;
        statsText = FindViewById<MaterialTextView>(Resource.Id.stats)!;
        seekHint = FindViewById<MaterialTextView>(Resource.Id.seek_hint)!;
        timeCurrent = FindViewById<MaterialTextView>(Resource.Id.time_current)!;
        timeTotal = FindViewById<MaterialTextView>(Resource.Id.time_total)!;
        seekBar = FindViewById<SeekBar>(Resource.Id.seek_bar)!;
        playPauseButton = FindViewById<ImageButton>(Resource.Id.play_pause_button)!;

        // Keep what is on top of the level out from under the system bars and the cutout
        EdgeToEdge.PadForSystemBars(FindViewById<View>(Resource.Id.top_bar)!, bottom: false);
        EdgeToEdge.PadForSystemBars(FindViewById<View>(Resource.Id.bottom_bar)!, top: false);
        EdgeToEdge.PadForSystemBars(statsContainer);
    }

    private void SetUpSurface()
    {
        surfaceView = new GraphicsSurfaceView(this) { RenderScale = settings.RenderScale };

        surfaceView.SurfaceCreatedCallback = holder =>
        {
            if (holder.Surface is { } surface)
                session?.AttachSurface(surface);
        };

        // The renderer was told to stop when the app was paused, which comes first. This is for when it did not.
        surfaceView.SurfaceDestroyedCallback = _ => session?.DetachSurface();

        surfaceView.SurfaceChangedCallback = (_, _, width, height) =>
        {
            surfaceWidth = width;
            surfaceHeight = height;
        };

        var root = FindViewById<FrameLayout>(Resource.Id.player_root)!;
        root.AddView(surfaceView, 0, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var gestures = new GestureDetector(this, new TapListener(this));
        root.Touch += (_, e) => e.Handled = gestures.OnTouchEvent(e.Event!);
    }

    private void SetUpControls()
    {
        FindViewById<View>(Resource.Id.close_button)!.Click += (_, _) => Finish();
        FindViewById<View>(Resource.Id.settings_button)!.Click += (_, _) => ShowSettingsSheet();
        FindViewById<View>(Resource.Id.error_back)!.Click += (_, _) => Finish();
        errorAction.Click += (_, _) => errorActionHandler?.Invoke();

        playPauseButton.Click += (_, _) =>
        {
            session?.Toggle();
            RefreshControls();
            ScheduleControlsHide();
        };

        FindViewById<View>(Resource.Id.seek_back_button)!.Click += (_, _) => SeekBy(-SeekStepSeconds);
        FindViewById<View>(Resource.Id.seek_forward_button)!.Click += (_, _) => SeekBy(SeekStepSeconds);

        seekBar.StartTrackingTouch += (_, _) =>
        {
            isSeeking = true;
            handler.RemoveCallbacks(hideControlsRunnable!);
        };

        seekBar.StopTrackingTouch += (_, _) =>
        {
            session?.Seek(seekBar.Progress / (double) seekBar.Max * (session?.Length ?? 0));
            isSeeking = false;
            ScheduleControlsHide();
        };

        // The time follows the thumb while it is dragged
        seekBar.ProgressChanged += (_, e) =>
        {
            if (e.FromUser && session is not null)
                timeCurrent.Text = FormatTime(e.Progress / (double) seekBar.Max * session.Length);
        };
    }

    #endregion

    #region Session

    private void StartSession()
    {
        loadingOverlay.Visibility = ViewStates.Visible;

        session = new PlaybackSession(this, level, settings);
        session.ProgressChanged += progress => RunOnUiThread(() => loadingText.Text = progress);
        session.Loaded += () => RunOnUiThread(OnLoaded);
        session.LoadFailed += message => RunOnUiThread(() => ShowFailure(Resource.String.error_load_title, message, retryable: true));
        session.RenderingFailed += message => RunOnUiThread(() => OnRenderingFailed(message));
        session.Ended += () => RunOnUiThread(OnEnded);
        session.Start();
    }

    private void OnLoaded()
    {
        if (failed)
            return;

        isLoaded = true;
        loadingOverlay.Visibility = ViewStates.Gone;

        // It played, so it is worth finding again
        var history = new LevelHistory(this);
        history.AddRecent(level);
        history.Current = level;

        audioFocus?.Request();

        timeTotal.Text = FormatTime(session!.Length);

        if (session.IsSilent)
            Toast.MakeText(this, Resource.String.silent_notice, ToastLength.Long)!.Show();

#if DEBUG
        if (Intent?.Extras is { } extras && extras.ContainsKey("start"))
            session.Seek(extras.GetFloat("start"));
#endif

        SetControlsVisible(true);
    }

    private void OnRenderingFailed(string message)
    {
        if (settings.Backend == RenderingBackend.Vulkan)
        {
            // Remembered, so that the home screen can say why Vulkan is not in use
            settingsStore.VulkanFailure = message;
            ShowFailure(Resource.String.error_vulkan_title, message, retryable: false, fallbackToGles: true);
        }
        else
        {
            ShowFailure(Resource.String.error_render_title, message, retryable: true);
        }
    }

    private void OnEnded()
    {
        SetControlsVisible(true);
        RefreshControls();
    }

    private void ShowFailure(int titleResource, string message, bool retryable, bool fallbackToGles = false)
    {
        failed = true;
        session?.Pause();

        loadingOverlay.Visibility = ViewStates.Gone;
        controls.Visibility = ViewStates.Gone;
        errorOverlay.Visibility = ViewStates.Visible;

        errorTitle.SetText(titleResource);
        errorMessage.Text = message;

        if (fallbackToGles)
        {
            errorAction.SetText(Resource.String.use_gles);
            errorAction.Visibility = ViewStates.Visible;
            errorActionHandler = () =>
            {
                settings.Backend = RenderingBackend.OpenGLES;
                settingsStore.Save(settings);
                Recreate();
            };
        }
        else if (retryable)
        {
            errorAction.SetText(Resource.String.retry);
            errorAction.Visibility = ViewStates.Visible;
            errorActionHandler = Recreate;
        }
        else
        {
            errorAction.Visibility = ViewStates.Gone;
            errorActionHandler = null;
        }
    }

    private void OnAudioFocusLost()
        => session?.Suspend(SuspendReason.FocusLost);

    private void OnAudioFocusGained()
        => session?.Resume(SuspendReason.FocusLost);

    #endregion

    #region Controls

    // Called every so often, to keep what is on screen up to date with the level
    private void Tick()
    {
        RefreshControls();
        RefreshStats();

        handler.PostDelayed(tickRunnable!, TickDelayMs);
    }

    private void RefreshControls()
    {
        if (session is null || !isLoaded)
            return;

        var length = session.Length;
        if (controlsVisible && !isSeeking)
        {
            var position = Math.Clamp(session.Position, 0, length);
            seekBar.Progress = length > 0 ? (int) Math.Round(position / length * seekBar.Max) : 0;
            timeCurrent.Text = FormatTime(position);
        }

        var icon = session.IsPlaying ? Resource.Drawable.ic_pause
            : session.Position >= length - 0.05 && length > 0 ? Resource.Drawable.ic_replay
            : Resource.Drawable.ic_play;
        playPauseButton.SetImageResource(icon);
    }

    private void RefreshStats()
    {
        statsText.Visibility = settings.ShowStats ? ViewStates.Visible : ViewStates.Gone;
        if (!settings.ShowStats || session is null)
            return;

        var ticks = statsClock.ElapsedTicks;
        var elapsed = (ticks - statsLastTicks) / (double) Stopwatch.Frequency;
        if (elapsed >= 0.5)
        {
            var frames = session.FramesRendered;
            fps = (frames - statsLastFrames) / elapsed;
            statsLastFrames = frames;
            statsLastTicks = ticks;
        }

        statsText.Text =
            $"{fps,5:0.0} fps  {(fps > 0 ? 1000 / fps : 0),5:0.0} ms\n" +
            $"{settings.Backend}  {surfaceWidth}x{surfaceHeight}\n" +
            $"{FormatTime(session.Position)} / {FormatTime(session.Length)}";
    }

    private void ToggleControls()
    {
        if (!isLoaded || failed)
            return;

        SetControlsVisible(!controlsVisible);
    }

    private void SetControlsVisible(bool visible)
    {
        controlsVisible = visible;
        handler.RemoveCallbacks(hideControlsRunnable!);

        // The stats make room for the title bar
        statsContainer.Animate()!.TranslationY(visible ? 64 * Resources!.DisplayMetrics!.Density : 0).SetDuration(150).Start();

        if (visible)
        {
            RefreshControls();
            controls.Visibility = ViewStates.Visible;
            controls.Animate()!.Alpha(1f).SetDuration(150).Start();
            ScheduleControlsHide();
        }
        else
        {
            controls.Animate()!.Alpha(0f).SetDuration(150)
                .WithEndAction(new Java.Lang.Runnable(() =>
                {
                    if (!controlsVisible)
                        controls.Visibility = ViewStates.Gone;
                }));
        }
    }

    private void ScheduleControlsHide()
    {
        handler.RemoveCallbacks(hideControlsRunnable!);
        handler.PostDelayed(hideControlsRunnable!, ControlsHideDelayMs);
    }

    private void HideControlsIfIdle()
    {
        // They stay while there is something to do with them
        if (session is { IsPlaying: true } && !isSeeking && settingsSheet is not { IsShowing: true })
            SetControlsVisible(false);
        else
            ScheduleControlsHide();
    }

    private void SeekBy(double seconds)
    {
        session?.SeekBy(seconds);
        ShowSeekHint(seconds);
        RefreshControls();
        ScheduleControlsHide();
    }

    private void ShowSeekHint(double seconds)
    {
        seekHint.Text = seconds > 0 ? $"+{seconds:0} s" : $"−{-seconds:0} s";
        seekHint.Animate()!.Cancel();
        seekHint.Alpha = 1f;
        seekHint.Animate()!.Alpha(0f).SetStartDelay(400).SetDuration(300).Start();
    }

    private void OnDoubleTap(float x)
    {
        if (!isLoaded || failed)
            return;

        // Left and right third seek like in a video player, the middle pauses
        var third = surfaceView.Width / 3f;
        if (x < third)
            SeekBy(-SeekStepSeconds);
        else if (x > 2 * third)
            SeekBy(SeekStepSeconds);
        else
            session?.Toggle();

        RefreshControls();
    }

    private void HideSystemBars()
    {
        var window = Window!;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var controller = window.InsetsController;
            if (controller is null)
                return;

            controller.Hide(WindowInsets.Type.SystemBars());
            controller.SystemBarsBehavior = (int) WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            return;
        }

#pragma warning disable CA1422
        window.DecorView.SystemUiFlags =
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.ImmersiveSticky;
#pragma warning restore CA1422
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int) time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    #endregion

    #region Quick settings

    private void ShowSettingsSheet()
    {
        if (session is null)
            return;

        var sheet = new BottomSheetDialog(this);
        var content = LayoutInflater.Inflate(Resource.Layout.sheet_player_settings, null)!;
        sheet.SetContentView(content);

        var speedGroup = content.FindViewById<ChipGroup>(Resource.Id.speed_group)!;
        foreach (var speed in PlayerSettings.PlaybackSpeeds)
        {
            var chip = (Chip) LayoutInflater.Inflate(Resource.Layout.chip_choice, speedGroup, false)!;
            chip.Id = View.GenerateViewId();
            chip.Text = $"{speed:0.##}×";
            chip.Checked = Math.Abs(speed - settings.PlaybackSpeed) < 0.001f;
            speedGroup.AddView(chip);

            var chosen = speed;
            chip.Click += (_, _) =>
            {
                session?.SetSpeed(chosen);
                settingsStore.Save(settings);
            };
        }

        var volume = content.FindViewById<SeekBar>(Resource.Id.volume_bar)!;
        volume.Progress = (int) Math.Round(settings.Volume * volume.Max);
        volume.ProgressChanged += (_, e) =>
        {
            if (!e.FromUser)
                return;

            session?.SetVolume(e.Progress / (float) volume.Max);
            settingsStore.Save(settings);
        };

        Bind(content, Resource.Id.sheet_post_processing, settings.PostProcessing, on => session?.SetPostProcessing(on));
        Bind(content, Resource.Id.sheet_text, settings.TextRendering, on => session?.SetTextRendering(on));
        Bind(content, Resource.Id.sheet_lock_aspect, settings.LockAspectRatio, on => session?.SetLockAspectRatio(on));
        Bind(content, Resource.Id.sheet_loop, settings.Loop, on => settings.Loop = on);
        Bind(content, Resource.Id.sheet_stats, settings.ShowStats, on =>
        {
            settings.ShowStats = on;
            RefreshStats();
        });

        sheet.DismissEvent += (_, _) =>
        {
            settingsSheet = null;
            HideSystemBars();
            ScheduleControlsHide();
        };

        // Landscape is short, so a collapsed sheet only shows its title. It opens all the way and scrolls.
        sheet.Behavior.SkipCollapsed = true;
        sheet.Behavior.State = BottomSheetBehavior.StateExpanded;

        settingsSheet = sheet;
        sheet.Show();
    }

    private void Bind(View content, int id, bool value, Action<bool> changed)
    {
        var toggle = content.FindViewById<MaterialSwitch>(id)!;
        toggle.Checked = value;
        toggle.CheckedChange += (_, e) =>
        {
            changed(e.IsChecked);
            settingsStore.Save(settings);
        };
    }

    #endregion

    #region Debug

#if DEBUG
    // adb shell am start -n <package>/com.reimnop.swag.PlayerActivity --es level '<json>' --es backend vulkan --ef scale 0.5 --ez stats true --ef start 20
    private void ApplyDebugOverrides()
    {
        var extras = Intent?.Extras;
        if (extras is null)
            return;

        if (extras.GetString("backend") is { } backend)
            settings.Backend = backend.Equals("vulkan", StringComparison.OrdinalIgnoreCase) ? RenderingBackend.Vulkan : RenderingBackend.OpenGLES;
        if (extras.ContainsKey("scale"))
            settings.RenderScale = extras.GetFloat("scale");
        if (extras.ContainsKey("stats"))
            settings.ShowStats = extras.GetBoolean("stats");
        if (extras.ContainsKey("fps"))
            settings.FrameRateLimit = extras.GetInt("fps");
        if (extras.ContainsKey("vsync"))
            settings.VSync = extras.GetBoolean("vsync");
        if (extras.ContainsKey("post"))
            settings.PostProcessing = extras.GetBoolean("post");
    }
#endif

    #endregion

    #region Listeners

    private class TapListener(PlayerActivity activity) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e) => true;

        public override bool OnSingleTapConfirmed(MotionEvent e)
        {
            activity.ToggleControls();
            return true;
        }

        public override bool OnDoubleTap(MotionEvent e)
        {
            activity.OnDoubleTap(e.GetX());
            return true;
        }
    }

    #endregion
}
