using System.Diagnostics;
using Android.Content;
using Android.Views;
using MattiasCibien.Extensions.Logging.Logcat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swag.Android.Library;
using Swag.Android.Platform;
using Swag.Android.Settings;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using Ralsei.Platform.Vulkan;
using Ralsei.Rendering;
using Ralsei.Rendering.OpenGLES;
using Ralsei.Rendering.Vulkan;
using Ralsei.Resources.Compressed;
using Ralsei.Util;
using Uri = Android.Net.Uri;

namespace Swag.Android.Playback;

public enum SessionState
{
    Loading,
    Ready,
    Failed
}

/// <summary>Why the song is held back for a while.</summary>
[Flags]
public enum SuspendReason
{
    /// <summary>The app is not in the foreground.</summary>
    AppPaused = 1,

    /// <summary>Something else is playing sound.</summary>
    FocusLost = 2
}

/// <summary>
/// One level being played: its beatmap and song, and the threads that animate it and draw it. This outlives the
/// window that is drawn to, which the system takes away and gives back as the app goes to the background and returns.
/// </summary>
/// <remarks>Events are raised on a background thread.</remarks>
public sealed class PlaybackSession : IDisposable
{
    // The renderer has handled the requests made when the level was loaded after this many frames,
    // and what the level shows is there to be seen
    private const int WarmupFrames = 2;

    public SessionState State { get; private set; } = SessionState.Loading;
    public LevelInfo Level { get; }
    public PlayerSettings Settings { get; }

    public event Action<string>? ProgressChanged;
    public event Action? Loaded;
    public event Action<string>? LoadFailed;

    /// <summary>The renderer could not be created, or gave up while it ran.</summary>
    public event Action<string>? RenderingFailed;

    /// <summary>The song reached its end, when it is not set to loop.</summary>
    public event Action? Ended;

    public double Position => audio?.Position ?? 0;
    public double Length => audio?.Length ?? 0;
    public bool IsPlaying => audio?.IsPlaying ?? false;
    public bool IsSilent => AudioPlayer.IsSilent;

    /// <summary>Frames drawn since the level started, for working out the frame rate.</summary>
    public long FramesRendered => Interlocked.Read(ref framesRendered);

    private readonly Context context;
    private readonly object gate = new();

    private readonly AndroidSurfaceContext surfaceContext = new();
    private readonly AndroidSurfaceSettings surfaceSettings;

    private ServiceProvider? serviceProvider;
    private IServiceScope? directorScope;
    private AppDirector? director;
    private IFrameQueue? frames;
    private AudioPlayer? audio;

    // A renderer and the thread it lives on. Each has its own stop flag, so that stopping one that is still tearing
    // down is not undone by the start of the next.
    private sealed class RenderWorker
    {
        public required Thread Thread { get; init; }
        public volatile bool StopRequested;
    }

    private Thread? loadThread;
    private Thread? directorThread;
    private RenderWorker? renderWorker;

    // Set while a renderer exists and there is something for the director to draw for
    private readonly ManualResetEventSlim renderingActive = new(false);
    private volatile bool disposed;

    private Surface? pendingSurface;
    private long framesRendered;

    // What the picture of the surface of the running renderer measures (the director draws for that), or nothing while there is none
    private volatile Func<Vector2i>? renderSizeSource;

    // Playing starts when the level is on screen, and again when the app is back after being away
    private volatile bool playWhenRendering = true;
    private volatile int suspendReasons;
    private volatile bool resumeAfterSuspend;
    private volatile bool hasStarted;
    private volatile bool endedRaised;

    public PlaybackSession(Context context, LevelInfo level, PlayerSettings settings)
    {
        this.context = context.ApplicationContext ?? context;
        Level = level;
        Settings = settings;

        surfaceSettings = new AndroidSurfaceSettings
        {
            LockAspectRatio = settings.LockAspectRatio,
            VSync = settings.VSync
        };
    }

    /// <summary>Starts reading the level in the background. <see cref="Loaded"/> or <see cref="LoadFailed"/> follows.</summary>
    public void Start()
    {
        loadThread = new Thread(Load) { Name = "SWAG Loader", IsBackground = true };
        loadThread.Start();
    }

    #region Controls

    public void Play()
    {
        if (audio is null)
            return;

        playWhenRendering = false;
        hasStarted = true;
        endedRaised = false;
        audio.Play();
    }

    public void Pause()
    {
        playWhenRendering = false;
        resumeAfterSuspend = false;
        audio?.Pause();
    }

    public void Toggle()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    public void Seek(double seconds)
    {
        if (audio is null)
            return;

        audio.Position = seconds;

        // The song starts again from the middle, if it had ended
        endedRaised = false;
        if (audio.HasEnded && hasStarted)
            audio.Play();
    }

    public void SeekBy(double seconds)
        => Seek(Position + seconds);

    /// <summary>
    /// Holds the song back while the level can not be enjoyed (the app is in the background, a call is coming in).
    /// It goes on once nothing holds it back any more, if it was playing.
    /// </summary>
    public void Suspend(SuspendReason reason)
    {
        suspendReasons |= (int) reason;

        if (IsPlaying)
        {
            audio!.Pause();
            resumeAfterSuspend = true;
        }
    }

    public void Resume(SuspendReason reason)
    {
        suspendReasons &= ~(int) reason;

        if (suspendReasons == 0 && resumeAfterSuspend)
        {
            resumeAfterSuspend = false;
            playWhenRendering = true;
        }
    }

    public void SetSpeed(float speed)
    {
        Settings.PlaybackSpeed = speed;
        if (audio is not null)
            audio.Speed = speed;
    }

    public void SetVolume(float volume)
    {
        Settings.Volume = volume;
        if (audio is not null)
            audio.Volume = volume;
    }

    public void SetPostProcessing(bool enabled)
    {
        Settings.PostProcessing = enabled;
        if (director is not null)
            director.EnablePostProcessing = enabled;
    }

    public void SetTextRendering(bool enabled)
    {
        Settings.TextRendering = enabled;
        if (director is not null)
            director.EnableTextRendering = enabled;
    }

    public void SetLockAspectRatio(bool locked)
    {
        Settings.LockAspectRatio = locked;
        surfaceSettings.LockAspectRatio = locked;
    }

    #endregion

    #region Loading

    private void Load()
    {
        try
        {
            Report("Reading the song");
            var audioData = ReadAll(Uri.Parse(Level.AudioUri)!);

            var player = AudioPlayer.Load(audioData);
            player.Speed = Settings.PlaybackSpeed;
            player.Volume = Settings.Volume;
            audio = player;

            if (disposed)
                return;

            Report("Starting up");
            serviceProvider = BuildServices();
            directorScope = serviceProvider.CreateScope();

            var scopeServices = directorScope.ServiceProvider;
            var appDirector = scopeServices.GetRequiredService<AppDirector>();
            appDirector.EnablePostProcessing = Settings.PostProcessing;
            appDirector.EnableTextRendering = Settings.TextRendering;

            scopeServices.GetRequiredService<RandomSeedService>().Seed = Settings.RandomSeed
                ? NumberUtil.SplitMix64((ulong) DateTimeOffset.Now.ToUnixTimeSeconds())
                : Settings.Seed;

            Report("Loading the beatmap");
            var beatmapService = scopeServices.GetRequiredService<BeatmapService>();
            using (var stream = OpenBuffered(Uri.Parse(Level.BeatmapUri)!))
                beatmapService.LoadBeatmap(stream, Level.Format);

            director = appDirector;
            frames = serviceProvider.GetRequiredService<IFrameQueue>();

            lock (gate)
            {
                if (disposed)
                    return;

                State = SessionState.Ready;
                directorThread = new Thread(DirectorLoop) { Name = "SWAG Director", IsBackground = true };
                directorThread.Start();
            }

            Loaded?.Invoke();

            // The window might have been there for some time already
            lock (gate)
                StartRenderThreadIfPossible();
        }
        catch (Exception e)
        {
            if (disposed)
                return;

            Log("Failed to load the level", e);
            State = SessionState.Failed;
            LoadFailed?.Invoke(Describe(e));
        }
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddLogcat("Swag");
        });

        services.AddSingleton(surfaceContext);
        services.AddSingleton(surfaceSettings);

        var builder = services.AddSwag(x => x.AddCompressedResources());
        switch (Settings.Backend)
        {
            case RenderingBackend.Vulkan:
                services.AddScoped<IVulkanSurface, AndroidVulkanSurface>();
                builder.UseVulkanRenderer();
                break;
            default:
                services.AddScoped<IOpenGLSurface, AndroidSurface>();
                builder.UseOpenGLESRenderer();
                break;
        }

        return services.BuildServiceProvider();
    }

    private Stream OpenBuffered(Uri uri)
    {
        var stream = context.ContentResolver!.OpenInputStream(uri)
            ?? throw new IOException("The file could not be opened. It might have been moved or deleted.");

        // Every read is a call into Java, so that big files are read in big pieces
        return new BufferedStream(stream, 256 * 1024);
    }

    private byte[] ReadAll(Uri uri)
    {
        using var stream = OpenBuffered(uri);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private void Report(string progress)
    {
        Log(progress);
        ProgressChanged?.Invoke(progress);
    }

    #endregion

    #region Rendering

    /// <summary>Gives the session a window to draw to. Called when the system has created it.</summary>
    public void AttachSurface(Surface surface)
    {
        lock (gate)
        {
            if (disposed)
                return;

            pendingSurface = surface;
            StartRenderThreadIfPossible();
        }
    }

    /// <summary>
    /// Stops drawing to the window that was attached. This does not wait for the renderer to be gone: the graphics
    /// driver can hold the renderer up while the system is busy with the window, and the UI thread that calls this
    /// is one of the things the system is busy with. The next renderer waits for this one instead.
    /// </summary>
    public void DetachSurface()
    {
        lock (gate)
        {
            pendingSurface = null;

            if (renderWorker is { } worker)
                worker.StopRequested = true;
        }
    }

    // Needs the gate
    private void StartRenderThreadIfPossible()
    {
        if (disposed || State != SessionState.Ready || pendingSurface is null)
            return;

        // Already drawing to a window that is still wanted
        if (renderWorker is { StopRequested: false })
            return;

        var previous = renderWorker;
        surfaceContext.Surface = pendingSurface;

        RenderWorker? worker = null;
        var thread = new Thread(() => RenderLoop(worker!, previous)) { Name = "SWAG Renderer", IsBackground = true };
        worker = new RenderWorker { Thread = thread };
        renderWorker = worker;
        thread.Start();
    }

    private void RenderLoop(RenderWorker worker, RenderWorker? previous)
    {
        var queue = frames!;

        try
        {
            // Two renderers do not exist at the same time, the old one has to be gone first
            previous?.Thread.Join();

            if (worker.StopRequested || disposed)
                return;

            using (var scope = serviceProvider!.CreateScope())
            {
                var renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();
                var glSurface = scope.ServiceProvider.GetService<IOpenGLSurface>();
                var vulkanSurface = scope.ServiceProvider.GetService<IVulkanSurface>();
                renderSizeSource = () => glSurface?.RenderSize ?? vulkanSurface?.RenderSize ?? default;
                renderingActive.Set();

                try
                {
                    while (!worker.StopRequested)
                    {
                        if (queue.FlushFrame(renderer))
                            Interlocked.Increment(ref framesRendered);
                        else
                            queue.WaitForWork(20);
                    }
                }
                finally
                {
                    renderingActive.Reset();
                    renderSizeSource = null;
                }

                // The context goes with the window, so what is in it does not need to be cleaned up one by one
                (scope.ServiceProvider.GetService<IOpenGLSurface>() as AndroidSurface)?.Abandon();

                // The renderer and surface are disposed with the scope
            }
        }
        catch (Exception e)
        {
            // The window going away takes the renderer down with it, which is no failure
            if (worker.StopRequested || disposed)
                return;

            Log("The renderer failed", e);
            renderingActive.Reset();
            RenderingFailed?.Invoke(Describe(e));
        }
    }

    #endregion

    #region Director

    private void DirectorLoop()
    {
        var queue = frames!;
        var appDirector = director!;
        var audioPlayer = audio!;

        var clock = Stopwatch.StartNew();
        long nextFrameTicks = 0;

        while (!disposed)
        {
            try
            {
                // Nobody is looking. Frames that are not made are not left half-way in the GPU when the window goes
                if ((suspendReasons & (int) SuspendReason.AppPaused) != 0)
                {
                    Thread.Sleep(20);
                    continue;
                }

                // There is nothing to draw to
                if (!renderingActive.Wait(100))
                    continue;

                if (!queue.WaitForFreeFrame(50))
                    continue;

                if (playWhenRendering && suspendReasons == 0 && FramesRendered >= WarmupFrames)
                    Play();

                HandleEnd(audioPlayer);

                // Stay under the frame rate that was asked for
                var limit = Settings.FrameRateLimit;
                if (limit > 0)
                {
                    var frameTicks = Stopwatch.Frequency / limit;
                    var now = clock.ElapsedTicks;
                    if (now < nextFrameTicks)
                    {
                        var wait = TimeSpan.FromSeconds((nextFrameTicks - now) / (double) Stopwatch.Frequency);
                        if (wait > TimeSpan.FromMilliseconds(1))
                            Thread.Sleep(wait - TimeSpan.FromMilliseconds(1));
                        while (clock.ElapsedTicks < nextFrameTicks && !disposed)
                            Thread.SpinWait(50);
                    }

                    // Do not try to catch up on frames that were skipped
                    nextFrameTicks = Math.Max(nextFrameTicks + frameTicks, clock.ElapsedTicks);
                }

                if (renderSizeSource is not { } renderSizeOf)
                    continue;

                appDirector.SubmitFrame((float) audioPlayer.Position, renderSizeOf());
            }
            catch (Exception e)
            {
                if (disposed)
                    return;

                Log("The level failed", e);
                RenderingFailed?.Invoke(Describe(e));
                return;
            }
        }
    }

    private void HandleEnd(AudioPlayer audioPlayer)
    {
        if (!hasStarted || !audioPlayer.HasEnded)
            return;

        if (Settings.Loop)
        {
            audioPlayer.Position = 0;
            audioPlayer.Play();
        }
        else if (!endedRaised)
        {
            endedRaised = true;
            Ended?.Invoke();
        }
    }

    #endregion

    /// <summary>Stops everything. The song stops at once, the rest is cleaned up in the background.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        audio?.Stop();

        DetachSurface();

        Task.Run(() =>
        {
            try
            {
                // Whatever is still being set up is finished (it checks for this) before it is taken apart
                loadThread?.Join();
                directorThread?.Join();
                renderWorker?.Thread.Join();

                audio?.Dispose();
                directorScope?.Dispose();
                serviceProvider?.Dispose();
            }
            catch (Exception e)
            {
                Log("Cleaning up failed", e);
            }
        });
    }

    private static string Describe(Exception e)
    {
        // Whatever is wrapped is more useful than the wrapper
        var inner = e;
        while (inner.InnerException is not null && inner is AggregateException or TypeInitializationException or System.Reflection.TargetInvocationException)
            inner = inner.InnerException;

        // The ones that people run into have a message that says what to do. The details are in the log.
        if (inner is System.Text.Json.JsonException)
            return "The beatmap file is not valid, or it is in a format this version does not understand.";
        if (inner is Java.IO.FileNotFoundException)
            return "A file of the level could not be found. It might have been moved or deleted.";

        // Messages of the runtime can be a bare key for a text that is not there, which says nothing
        return string.IsNullOrWhiteSpace(inner.Message) || !inner.Message.Contains(' ')
            ? $"{inner.GetType().Name} ({inner.Message})"
            : inner.Message;
    }

    private static void Log(string message, Exception? exception = null)
    {
        if (exception is null)
            global::Android.Util.Log.Info("Swag", message);
        else
            global::Android.Util.Log.Error("Swag", $"{message}: {exception}");
    }
}
