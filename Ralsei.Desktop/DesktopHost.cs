using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ralsei.Core;
using Ralsei.Core.Audio;
using Ralsei.Core.Game;
using Ralsei.Core.Input;
using Ralsei.Core.Service;
using Ralsei.Core.Text;
using Ralsei.Platform.OpenGL;
using Ralsei.Platform.Window;
using Ralsei.Rendering;
using Ralsei.Rendering.Common;

namespace Ralsei.Desktop;

/// <summary>Runs the app: the main window and the windows scenes open beside it. Each window is a whole engine (its own renderer on its own render thread, frames and scenes, made from the same services; the resources frames draw are shared), all advanced by this one thread so scenes of different windows can share state without locks.</summary>
public sealed class DesktopHost(IServiceCollection services) : IWindowManager
{
    private sealed class Instance
    {
        public required ServiceProvider? OwnedProvider { get; init; }
        public required IServiceProvider Provider { get; init; }
        public required DesktopWindow Window { get; init; }
        public required GameDirector Director { get; init; }
        public required IFrameQueue Queue { get; init; }
        public required bool Primary { get; init; }
        public required Handle? Handle { get; init; }
        public Thread? RenderThread { get; set; }
        public CancellationTokenSource Stop { get; } = new();
        public double LastFrame { get; set; }
        public int Frames { get; set; }
    }

    // A window that is not on the screen is kept for being shown again, and moves on only to make its scene
    private const int WarmUpFrames = 3;

    private sealed class Handle(DesktopHost host, string title) : IWindowHandle
    {
        public Instance? Instance { get; set; }

        public bool IsOpen { get; set; } = true;

        public event Action? Closed;

        public IWindowControl Control => Instance?.Window.WindowControl ?? new NullWindowControl();

        public string Title
        {
            get => Instance?.Window.WindowControl.Title ?? title;
            set
            {
                title = value;
                if (Instance is { } instance)
                    instance.Window.WindowControl.Title = value;
            }
        }

        public void Close()
        {
            if (IsOpen && Instance is { } instance)
                instance.Window.WindowControl.ForceClose();
        }

        public void RaiseClosed()
        {
            IsOpen = false;
            Closed?.Invoke();
        }
    }

    // Shared by the windows: the sound device, and the resources frames draw (made once; each window's renderer mirrors them in its own context)
    private static readonly Type[] SharedServices =
    [
        typeof(IAudioPlayer), typeof(ITrackPlayer),
        typeof(RenderingFactory), typeof(IRenderingFactory), typeof(ResourceLoader), typeof(MeshService), typeof(FontService), typeof(TextShaper),
    ];

    // Made again for each window (the input is the window's, so it is made first and the services are given it)
    private static readonly Type[] PerWindowServices = [typeof(DesktopWindow), typeof(IGameStartup), typeof(InputManager)];

    private readonly List<Instance> instances = [];
    private readonly List<Instance> opened = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private IServiceProvider? root;

    public bool IsSupported => true;

    /// <summary>Runs until the main window is closed.</summary>
    public void Run(IServiceProvider rootProvider)
    {
        root = rootProvider;
        FramePacing.RaiseTimerResolution();
        var main = Start(rootProvider, null, rootProvider.GetRequiredService<DesktopWindow>(), true, null);
        instances.Add(main);

        while (true)
        {
            DesktopWindow.PumpEvents();

            if (opened.Count > 0)
            {
                instances.AddRange(opened);
                opened.Clear();
            }

            // A window that is closed goes; the main window closing is the end
            var ended = false;
            for (var i = instances.Count - 1; i >= 0; i--)
            {
                var instance = instances[i];
                if (!instance.Window.ShouldClose)
                    continue;

                if (instance.Primary)
                {
                    ended = true;
                    continue;
                }

                instances.RemoveAt(i);
                Stop(instance);
            }

            if (ended)
                break;

            var progressed = false;
            foreach (var instance in instances.ToArray())
            {
                instance.Window.CompleteReveal();

                // Waiting for the render thread keeps a frame from being old when shown, and so does taking in input late: after the wait, not before
                if (instance.Queue.FreeFrameCount <= 0)
                    continue;

                // A window that is minimized has no picture to make
                var size = instance.Window.FramebufferSize;
                if (size.X <= 0 || size.Y <= 0)
                {
                    instance.LastFrame = clock.Elapsed.TotalSeconds;
                    continue;
                }

                if (!instance.Primary && instance.Frames >= WarmUpFrames && !instance.Window.WindowControl.Visible)
                {
                    instance.LastFrame = clock.Elapsed.TotalSeconds;
                    continue;
                }

                instance.Frames++;

                // Built as late as the display allows, with input taken in just before
                if (instance.Primary && FramePacing.Enabled && DesktopSettings.SwapInterval == 1)
                {
                    // Vulkan shows the newest frame at each refresh, so frames are made a few times for each (and each is of the latest input)
                    if (instance.Window.IsVulkan && DesktopSettings.LowLatency)
                        instance.Window.Pacing.WaitForRate(instance.Window.RefreshRate * DesktopSettings.VulkanRate);
                    else
                        instance.Window.Pacing.WaitForTurn();
                }

                DesktopWindow.PumpEvents();
                instance.Window.PlayScript();

                var now = clock.Elapsed.TotalSeconds;
                var dt = (float)(now - instance.LastFrame);
                instance.LastFrame = now;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                instance.Window.BeginFrameStamp();
                instance.Director.ProcessFrame(dt, size);
                instance.Window.Pacing.Built(System.Diagnostics.Stopwatch.GetTimestamp() - started);
                if (DesktopStats.Enabled && instance.Primary)
                    DesktopStats.RecordMain(System.Diagnostics.Stopwatch.GetTimestamp() - started);

                progressed = true;
            }

            // Nothing could be built: wait for a frame to come back, but not for long, as other windows and the close button need looking at
            if (!progressed)
            {
                var waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                instances[0].Queue.WaitForFreeFrame(2);

                // Frames are free, so what held the loop up was a window with no picture to make (minimized): do not spin
                if (instances[0].Queue.FreeFrameCount > 0)
                    Thread.Sleep(2);
                if (DesktopStats.Enabled)
                    DesktopStats.RecordWait(System.Diagnostics.Stopwatch.GetTimestamp() - waitStart);
            }

            if (DesktopStats.Enabled)
                DesktopStats.Report();
        }

        foreach (var instance in instances.Where(i => !i.Primary).ToArray())
            Stop(instance);

        Stop(instances[0]);
    }

    public IWindowHandle? Open(WindowRequest request)
    {
        if (root is null)
            return null;

        var config = root.GetService<OpenGLSettings>();
        var input = new InputManager();
        var window = new DesktopWindow(input, config, false, request.Title, request.Width, request.Height, request);

        var collection = new ServiceCollection();
        foreach (var descriptor in services)
        {
            if (Array.IndexOf(PerWindowServices, descriptor.ServiceType) < 0 && Array.IndexOf(SharedServices, descriptor.ServiceType) < 0)
                collection.Add(descriptor);
        }

        collection.AddSingleton(input);
        collection.AddSingleton(window);
        collection.AddSingleton<IGameStartup>(sp => new SceneStartup(sp, request.CreateScene));
        foreach (var shared in SharedServices)
            collection.AddSingleton(shared, root.GetRequiredService(shared));

        var provider = collection.BuildServiceProvider();
        var handle = new Handle(this, request.Title);
        var instance = Start(provider, provider, window, false, handle);
        handle.Instance = instance;
        opened.Add(instance);
        return handle;
    }

    private Instance Start(IServiceProvider provider, ServiceProvider? owned, DesktopWindow window, bool primary, Handle? handle)
    {
        var instance = new Instance
        {
            OwnedProvider = owned,
            Provider = provider,
            Window = window,
            Director = provider.GetRequiredService<GameDirector>(),
            Queue = provider.GetRequiredService<IFrameQueue>(),
            Primary = primary,
            Handle = handle,
            LastFrame = clock.Elapsed.TotalSeconds
        };

        instance.RenderThread = new Thread(() => RunRenderThread(instance)) { Name = primary ? "Render" : "Render (window)" };
        instance.RenderThread.Start();
        return instance;
    }

    private static void RunRenderThread(Instance instance)
    {
        using var scope = instance.Provider.CreateScope();

        // The renderer is made here, on the render thread, which is where it draws
        var renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();

        // Runs what is asked of the render thread, and draws the frames, in the order they were handed over
        var token = instance.Stop.Token;
        while (!token.IsCancellationRequested)
        {
            if (instance.Queue.WaitForWork(10))
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                instance.Window.Pacing.FrameStarted = started;
                var drew = instance.Queue.FlushFrame(renderer);
                if (DesktopStats.Enabled && drew && instance.Primary)
                    DesktopStats.RecordRender(System.Diagnostics.Stopwatch.GetTimestamp() - started);
            }
        }
    }

    private static void Stop(Instance instance)
    {
        instance.Stop.Cancel();
        instance.RenderThread?.Join();

        // The scenes go with the services (the window of the main instance goes with the services it was made by)
        instance.OwnedProvider?.Dispose();
        if (instance.Handle is not null)
        {
            instance.Window.Dispose();
            instance.Handle.RaiseClosed();
        }
    }

    private sealed class SceneStartup(IServiceProvider services, Func<IServiceProvider, IScene> create) : IGameStartup
    {
        public IScene CreateInitialScene() => create(services);
    }
}
