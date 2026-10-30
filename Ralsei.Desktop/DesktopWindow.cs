using Ico.Reader;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Mathematics;
using Ralsei.Platform;
using Ralsei.Platform.OpenGL;
using Ralsei.Platform.Vulkan;
using Ralsei.Platform.Window;
using ColorRgba = Ralsei.Core.Data.ColorRgba;
using CursorShape = Ralsei.Platform.Window.CursorShape;
using GlfwMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using InputManager = Ralsei.Core.Input.InputManager;
using Keycode = Ralsei.Platform.Input.Keycode;
using RalseiMouseButton = Ralsei.Platform.Input.MouseButton;
using ReFuel.Stb;

namespace Ralsei.Desktop;

/// <summary>One window of the app: its input (told to the <see cref="InputManager"/>), clipboard, cursors and size, and what the renderer draws to: the window's OpenGL context (<see cref="OpenGLSurface"/>) or a Vulkan surface (<see cref="VulkanSurface"/>).</summary>
public unsafe class DesktopWindow : IDisposable
{
    private class GlSurface(DesktopWindow window) : IOpenGLSurface
    {
        private Thread? current;
        private bool applied;

        public int Framebuffer => 0;

        public Vector2i FramebufferSize => window.FramebufferSize;

        public Vector2i RenderSize => window.FramebufferSize;

        public bool IsContextLost => window.disposed;

        public bool TryMakeCurrent()
        {
            if (window.disposed)
                return false;

            // The window is made on the main thread, and drawn to on the render thread, which asks before each frame
            if (current != Thread.CurrentThread)
            {
                GLFW.MakeContextCurrent(window.windowPtr);
                current = Thread.CurrentThread;
            }

            if (!applied)
            {
                // Once, on the thread that draws: how long a frame waits for the display
                applied = true;
                GLFW.SwapInterval(DesktopSettings.SwapInterval);
            }

            return true;
        }

        public void SwapBuffers()
        {
            if (window.disposed)
                return;

            // With the frame drawn (the pacing needs to know how long that takes, the display needs it done), the swap waits for the refresh
            var drawn = System.Diagnostics.Stopwatch.GetTimestamp();
            if (DesktopSettings.LowLatency || FramePacing.Enabled)
                GL.Finish();

            window.Pacing.PrepareTicks = drawn - window.Pacing.FrameStarted;
            GLFW.SwapBuffers(window.windowPtr);

            // The driver would let the render thread run ahead by a few frames, each of them time between a key and its picture; this
            // makes it wait until the one it gave was shown
            if (DesktopSettings.LowLatency)
                GL.Finish();

            window.Pacing.Presented(window.Pacing.PrepareTicks);
            window.FramePresented();
        }
    }

    private class VkSurface(DesktopWindow window) : IVulkanSurface
    {
        public Vector2i RenderSize => window.FramebufferSize;

        public Vector2i FramebufferSize => window.FramebufferSize;

        public bool VSync => DesktopSettings.SwapInterval != 0;

        public bool LowLatency => DesktopSettings.LowLatency && DesktopSettings.SwapInterval == 1;

        public void FramePresented() => window.FramePresented();

        public bool IsHeadless => false;

        public bool Transparent => window.transparent;

        public IReadOnlyList<string> GetRequiredInstanceExtensions()
        {
            if (!GLFW.VulkanSupported())
                throw new InvalidOperationException("Vulkan is not supported on this system");

            return GLFW.GetRequiredInstanceExtensions();
        }

        public ulong CreateSurface(ulong instance)
        {
            var result = GLFW.CreateWindowSurface(new VkHandle { Handle = instance }, window.windowPtr, null, out VkHandle surface);
            if (result != 0)
                throw new InvalidOperationException($"Failed to create a Vulkan surface for the window (VkResult {result})");

            return surface.Handle;
        }
    }

    // Reading every frame back is only for taking a picture, so these are the surfaces only when one is asked for
    private sealed class GlCaptureSurface(DesktopWindow window) : GlSurface(window), IFrameReceiver
    {
        public void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)
        {
            if (window.disposed)
                return;

            window.CaptureFrameIfAsked(rgba, size);
        }
    }

    private sealed class VkCaptureSurface(DesktopWindow window) : VkSurface(window), IFrameReceiver
    {
        public void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)
        {
            if (window.disposed)
                return;

            window.CaptureFrameIfAsked(rgba, size);
        }
    }

    private class DClipboard(DesktopWindow dpm) : IClipboard
    {
        public string? GetText() => dpm.disposed ? null : GLFW.GetClipboardString(dpm.windowPtr);

        public void SetText(string text)
        {
            if (!dpm.disposed)
                GLFW.SetClipboardString(dpm.windowPtr, text);
        }
    }

    private class DWindowControl(DesktopWindow dpm) : IWindowControl
    {
        private string title = "RALSEI";

        public string Title
        {
            get => title;
            set
            {
                title = value;
                if (!dpm.disposed)
                    GLFW.SetWindowTitle(dpm.windowPtr, value);
            }
        }

        public float ContentScale
        {
            get
            {
                if (float.TryParse(Environment.GetEnvironmentVariable("RALSEI_CONTENT_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var forced))
                    return forced;

                if (dpm.disposed)
                    return 1f;

                GLFW.GetWindowContentScale(dpm.windowPtr, out var x, out _);
                return x > 0f ? x : 1f;
            }
        }

        public Func<bool>? CloseGuard { get; set; }

        public void RequestClose()
        {
            if (!dpm.disposed)
                GLFW.SetWindowShouldClose(dpm.windowPtr, true);
        }

        public void ForceClose()
        {
            dpm.forceClose = true;
            RequestClose();
        }

        public void SetCursor(CursorShape shape) => dpm.ApplyCursor(shape);

        public void Resize(int width, int height)
        {
            if (!dpm.disposed)
                GLFW.SetWindowSize(dpm.windowPtr, width, height);
        }

        public (int X, int Y) Position
        {
            get
            {
                if (dpm.disposed)
                    return (0, 0);

                GLFW.GetWindowPos(dpm.windowPtr, out var x, out var y);
                return (x, y);
            }
            set
            {
                if (!dpm.disposed)
                    GLFW.SetWindowPos(dpm.windowPtr, value.X, value.Y);
            }
        }

        public (float X, float Y) CursorScreenPosition
        {
            get
            {
                if (dpm.disposed)
                    return (0f, 0f);

                GLFW.GetWindowPos(dpm.windowPtr, out var x, out var y);
                GLFW.GetCursorPos(dpm.windowPtr, out var cursorX, out var cursorY);
                return ((float)(x + cursorX), (float)(y + cursorY));
            }
        }

        public (int Width, int Height) ClientSize
        {
            get
            {
                if (dpm.disposed)
                    return (0, 0);

                GLFW.GetWindowSize(dpm.windowPtr, out var width, out var height);
                return (width, height);
            }
        }

        public float PixelRatio
        {
            get
            {
                if (dpm.disposed)
                    return 1f;

                GLFW.GetWindowSize(dpm.windowPtr, out var width, out _);
                GLFW.GetFramebufferSize(dpm.windowPtr, out var framebufferWidth, out _);
                return width > 0 ? framebufferWidth / (float)width : 1f;
            }
        }

        // A window that is shown has a picture of what it is to show first (what it showed the last time is not wanted): it is shown
        // when a frame was made for it, and until then it is as good as shown (the host makes frames for those)
        public bool Visible
        {
            get => !dpm.disposed && (dpm.revealPending || GLFW.GetWindowAttrib(dpm.windowPtr, WindowAttributeGetBool.Visible));
            set
            {
                if (dpm.disposed)
                    return;

                if (!value)
                {
                    dpm.revealPending = false;
                    dpm.focusOnReveal = false;
                    GLFW.HideWindow(dpm.windowPtr);
                }
                else if (!Visible)
                {
                    dpm.revealAfter = Volatile.Read(ref dpm.presented) + 1;
                    dpm.revealDeadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 10;
                    dpm.revealPending = true;
                }
            }
        }

        public bool Focused => !dpm.disposed && GLFW.GetWindowAttrib(dpm.windowPtr, WindowAttributeGetBool.Focused);

        public void Focus()
        {
            if (dpm.disposed)
                return;

            if (dpm.revealPending)
                dpm.focusOnReveal = true;
            else
                GLFW.FocusWindow(dpm.windowPtr);
        }

        public (int X, int Y, int Width, int Height)? WorkAreaAt(int x, int y)
        {
            var monitors = GLFW.GetMonitorsRaw(out var count);
            (int, int, int, int)? best = null;
            for (var i = 0; i < count; i++)
            {
                GLFW.GetMonitorWorkarea(monitors[i], out var areaX, out var areaY, out var areaWidth, out var areaHeight);
                if (x >= areaX && y >= areaY && x < areaX + areaWidth && y < areaY + areaHeight)
                    return (areaX, areaY, areaWidth, areaHeight);

                best ??= (areaX, areaY, areaWidth, areaHeight);
            }

            return best;
        }
    }

    public IClipboard Clipboard => clipboard;
    public IWindowControl WindowControl => windowControl;

    /// <summary>What an OpenGL (or OpenGL ES) renderer draws to. Only the window of an OpenGL run has a context.</summary>
    public IOpenGLSurface OpenGLSurface => openGLSurface;

    /// <summary>What a Vulkan renderer draws to; only a Vulkan run's window has one. With <c>RALSEI_CAPTURE_PATH</c> it also saves the asked-for frame.</summary>
    public IVulkanSurface VulkanSurface => vulkanSurface;

    /// <summary>The size of the picture of the window, in pixels.</summary>
    public Vector2i FramebufferSize
    {
        get
        {
            if (disposed)
                return default;

            GLFW.GetFramebufferSize(windowPtr, out var width, out var height);
            return new Vector2i(width, height);
        }
    }

    /// <summary>The window is to close. A close the scene asks to think over (<see cref="IWindowControl.CloseGuard"/>) is taken back, once per ask.</summary>
    public bool ShouldClose
    {
        get
        {
            if (!GLFW.WindowShouldClose(windowPtr))
                return false;

            if (!forceClose && windowControl.CloseGuard?.Invoke() == true)
            {
                GLFW.SetWindowShouldClose(windowPtr, false);
                return false;
            }

            return true;
        }
    }

    private readonly Window* windowPtr;
    private readonly InputManager input;

    private readonly GlSurface openGLSurface;
    private readonly VkSurface vulkanSurface;
    private readonly DClipboard clipboard;
    private readonly DWindowControl windowControl;

    // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
    // Intentionally leaked here to avoid the delegate getting GC'd
    private readonly GLFWCallbacks.KeyCallback keyCallback;
    private readonly GLFWCallbacks.CursorPosCallback cursorPosCallback;
    private readonly GLFWCallbacks.MouseButtonCallback mouseButtonCallback;
    private readonly GLFWCallbacks.ScrollCallback scrollCallback;
    private readonly GLFWCallbacks.CharCallback charCallback;
    private readonly GLFWCallbacks.DropCallback dropCallback;
    // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

    private readonly bool vulkanWindow;
    private readonly bool transparent;
    private bool disposed;
    private bool revealPending;
    private bool focusOnReveal;
    private int revealAfter;
    private long revealDeadline;
    private int presented;

    /// <summary>The size the window opens at: 800 by 600, or what <c>RALSEI_WINDOW_SIZE</c> says (<c>1280x720</c>).</summary>
    private static (int Width, int Height) InitialWindowSize()
    {
        var parts = Environment.GetEnvironmentVariable("RALSEI_WINDOW_SIZE")?.Split('x', 'X');
        return parts is { Length: 2 } && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width > 0 && height > 0
            ? (width, height)
            : (800, 600);
    }

    // A way to look at what is drawn without a person at the window: with RALSEI_CAPTURE_PATH set, the frame number
    // RALSEI_CAPTURE_FRAME (default 120) is saved there as a PNG, and the app closes.
    private readonly string? capturePath;
    private readonly int captureFrame;
    private int framesPresented;
    private bool forceClose;
    private readonly bool primary;
    private static int windowCount;
    private static bool bindingsLoaded;

    /// <param name="input">Told what the user does to the window.</param>
    /// <param name="contextConfig">The OpenGL context to make, or nothing for a window that is made for Vulkan, which has no context.</param>
    /// <param name="primary">The first window of the app, which the test tools (picture, scripted input) are for.</param>
    public DesktopWindow(InputManager input, OpenGLSettings? contextConfig = null, bool primary = true, string title = "RALSEI", int width = 0, int height = 0, WindowRequest? request = null)
    {
        this.input = input;
        this.primary = primary;
        vulkanWindow = contextConfig is null;
        if (primary)
        {
            capturePath = Environment.GetEnvironmentVariable("RALSEI_CAPTURE_PATH");
            captureFrame = int.TryParse(Environment.GetEnvironmentVariable("RALSEI_CAPTURE_FRAME"), out var frame) ? frame : 120;
            if (Environment.GetEnvironmentVariable("RALSEI_INPUT_SCRIPT") is { Length: > 0 } script && File.Exists(script))
                scriptedInput = new ScriptedInput(script);
        }
        else if (Environment.GetEnvironmentVariable("RALSEI_CAPTURE_PATH") is { Length: > 0 } picture)
        {
            // The other windows are pictured too, beside the first one's: name.png, name.window.png
            capturePath = Path.ChangeExtension(picture, ".window" + Path.GetExtension(picture));
            captureFrame = int.TryParse(Environment.GetEnvironmentVariable("RALSEI_CAPTURE_FRAME_WINDOW") ?? Environment.GetEnvironmentVariable("RALSEI_CAPTURE_FRAME"), out var secondaryFrame) ? secondaryFrame : 120;
        }

        if (windowCount == 0 && !GLFW.Init())
            throw new Exception("Failed to initialize GLFW");

        // The hints are what the last window left them as
        GLFW.DefaultWindowHints();
        if (request is not null)
        {
            GLFW.WindowHint(WindowHintBool.Decorated, request.Decorated);
            GLFW.WindowHint(WindowHintBool.Floating, request.Floating);

            // (The system only lets the screen through a window that has no frame)
            transparent = request.Transparent && !request.Decorated;
            GLFW.WindowHint(WindowHintBool.TransparentFramebuffer, transparent);
            GLFW.WindowHint(WindowHintBool.Visible, request.Visible);
            GLFW.WindowHint(WindowHintBool.Resizable, request.Resizable);
            GLFW.WindowHint(WindowHintBool.FocusOnShow, request.TakeFocus);
            GLFW.WindowHint(WindowHintBool.Focused, request.TakeFocus);
        }

        if (contextConfig is null)
        {
            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.NoApi);
        }
        else
        {
            if (!bindingsLoaded)
            {
                GLLoader.LoadBindings(new GLFWBindingsContext());
                bindingsLoaded = true;
            }

            if (contextConfig.IsES)
            {
                GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlEsApi);
            }
            else
            {
                GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
                GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            }

            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, contextConfig.MajorVersion);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, contextConfig.MinorVersion);
        }

        var (windowWidth, windowHeight) = width > 0 && height > 0 ? (width, height) : InitialWindowSize();
        windowPtr = GLFW.CreateWindow(windowWidth, windowHeight, title, null, null);
        if (windowPtr == null)
            throw new Exception("Failed to create GLFW window");

        windowCount++;
        if (request?.Position is { } at)
            GLFW.SetWindowPos(windowPtr, at.X, at.Y);

        LoadWindowIcon();

        keyCallback = OnKeyEvent;
        cursorPosCallback = OnCursorPos;
        mouseButtonCallback = OnMouseButton;
        scrollCallback = OnScroll;
        charCallback = OnChar;
        dropCallback = OnDrop;

        GLFW.SetKeyCallback(windowPtr, keyCallback);
        GLFW.SetCursorPosCallback(windowPtr, cursorPosCallback);
        GLFW.SetMouseButtonCallback(windowPtr, mouseButtonCallback);
        GLFW.SetScrollCallback(windowPtr, scrollCallback);
        GLFW.SetCharCallback(windowPtr, charCallback);
        GLFW.SetDropCallback(windowPtr, dropCallback);

        openGLSurface = capturePath is null ? new GlSurface(this) : new GlCaptureSurface(this);
        vulkanSurface = capturePath is null ? new VkSurface(this) : new VkCaptureSurface(this);
        clipboard = new DClipboard(this);
        windowControl = new DWindowControl(this);
    }

    /// <summary>Gives the window the icon the executable carries, every size it has (the icon is optional; a window without one is fine).</summary>
    private void LoadWindowIcon()
    {
        using var iconStream = typeof(DesktopWindow).Assembly.GetManifestResourceStream("Ralsei.Desktop.icon.ico");
        if (iconStream is null)
            return;

        var iconData = new IcoReader().Read(iconStream);
        if (iconData is null || iconData.Groups.Count == 0)
            return;

        var group = iconData.Groups[0];
        var images = new List<StbImage>();
        try
        {
            for (var i = 0; i < group.DirectoryEntries.Length; i++)
                images.Add(StbImage.Load(iconData.GetImage(group.Name, i), StbiImageFormat.Rgba));

            var glfwImages = images
                .Select(image => new OpenTK.Windowing.GraphicsLibraryFramework.Image
                {
                    Width = image.Width,
                    Height = image.Height,
                    Pixels = (byte*) image.ImagePointer
                })
                .ToArray();

            GLFW.SetWindowIcon(windowPtr, glfwImages);
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
        }
    }

    private void OnCursorPos(Window* window, double x, double y)
    {
        // The pointer is in screen coordinates, and the picture is in pixels, which can be more of them on a high-DPI display
        GLFW.GetWindowSize(window, out var windowWidth, out var windowHeight);
        GLFW.GetFramebufferSize(window, out var framebufferWidth, out var framebufferHeight);
        var scaleX = windowWidth > 0 ? framebufferWidth / (float)windowWidth : 1f;
        var scaleY = windowHeight > 0 ? framebufferHeight / (float)windowHeight : 1f;
        MouseMoved((float)x * scaleX, (float)y * scaleY);
    }

    private void OnMouseButton(Window* window, GlfwMouseButton button, InputAction action, KeyModifiers mods)
    {
        var mapped = button switch
        {
            GlfwMouseButton.Left => RalseiMouseButton.Left,
            GlfwMouseButton.Right => RalseiMouseButton.Right,
            GlfwMouseButton.Middle => RalseiMouseButton.Middle,
            GlfwMouseButton.Button4 => RalseiMouseButton.Extra1,
            GlfwMouseButton.Button5 => RalseiMouseButton.Extra2,
            _ => (RalseiMouseButton?)null
        };

        if (mapped is { } known && action != InputAction.Repeat)
            MouseButton(known, action == InputAction.Press);
    }

    private void OnScroll(Window* window, double offsetX, double offsetY)
        => input.Scroll((float)offsetX, (float)offsetY);

    private void OnDrop(Window* window, int count, byte** paths)
    {
        var dropped = new string[count];
        for (var i = 0; i < count; i++)
            dropped[i] = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)paths[i]) ?? string.Empty;

        input.FilesDropped(dropped);
    }

    private readonly Dictionary<CursorShape, nint> cursors = new();
    private CursorShape? appliedCursor;

    private void ApplyCursor(CursorShape shape)
    {
        if (disposed || appliedCursor == shape)
            return;

        appliedCursor = shape;
        if (!cursors.TryGetValue(shape, out var cursor))
        {
            cursor = (nint)GLFW.CreateStandardCursor(shape switch
            {
                CursorShape.IBeam => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.IBeam,
                CursorShape.Crosshair => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.Crosshair,
                CursorShape.PointingHand => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.PointingHand,
                CursorShape.ResizeHorizontal => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.ResizeEW,
                CursorShape.ResizeVertical => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.ResizeNS,
                CursorShape.Move => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.ResizeAll,
                CursorShape.ResizeNorthWestSouthEast => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.ResizeNWSE,
                CursorShape.ResizeNorthEastSouthWest => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.ResizeNESW,
                CursorShape.NotAllowed => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.NotAllowed,
                _ => OpenTK.Windowing.GraphicsLibraryFramework.CursorShape.Arrow
            });
            cursors[shape] = cursor;
        }

        // A cursor the system can not make leaves the one that was there
        if (cursor != 0)
            GLFW.SetCursor(windowPtr, (OpenTK.Windowing.GraphicsLibraryFramework.Cursor*)cursor);
    }

    private void OnChar(Window* window, uint codepoint)
    {
        if (codepoint is >= 0xD800 and <= 0xDFFF or > 0x10FFFF)
            return;

        input.TextInput(char.ConvertFromUtf32((int)codepoint));
    }

    private void OnKeyEvent(Window* window, Keys key, int scanCode, InputAction action, KeyModifiers mods)
    {
        if (key == Keys.Unknown)
            return;

        switch (action)
        {
            case InputAction.Repeat:
                StampInput();
                input.KeyRepeat((Keycode)key);
                break;
            case InputAction.Press:
                StampInput();
                input.KeyDown((Keycode)key);
                break;
            case InputAction.Release:
                input.KeyUp((Keycode)key);
                break;
        }
    }

    private void MouseMoved(float x, float y)
    {
        StampInput();
        input.MouseMove(x, y);
    }

    private void MouseButton(RalseiMouseButton button, bool pressed)
    {
        StampInput();
        if (pressed)
            input.MouseDown(button);
        else
            input.MouseUp(button);
    }

    /// <summary>The renderer hands over the frame it drew, which can be smaller than the window.</summary>
    private void CaptureFrameIfAsked(ReadOnlySpan<byte> rgba, Vector2i size)
    {
        if (capturePath is null || ++framesPresented != captureFrame)
            return;

        SaveCapture(rgba.ToArray(), size.X, size.Y);
    }

    /// <param name="pixels">Tightly packed RGBA8, the top row first.</param>
    private void SaveCapture(byte[] pixels, int width, int height)
    {
        using var file = File.Create(capturePath);
        StbImage.WritePng<byte>(pixels, width, height, StbiImageFormat.Rgba, file, false);

        Console.WriteLine($"Captured frame {framesPresented} to {capturePath}");
        GLFW.SetWindowShouldClose(windowPtr, true);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var cursor in cursors.Values)
            GLFW.DestroyCursor((OpenTK.Windowing.GraphicsLibraryFramework.Cursor*)cursor);

        GLFW.DestroyWindow(windowPtr);
        if (--windowCount == 0)
            GLFW.Terminate();
    }

    /// <summary>Whether the window is drawn to by Vulkan (it has no context of its own).</summary>
    public bool IsVulkan => vulkanWindow;

    /// <summary>How many times a second the display shows a picture, or 0 if that is not known.</summary>
    public int RefreshRate
    {
        get
        {
            var monitor = GLFW.GetPrimaryMonitor();
            return monitor == null ? 0 : GLFW.GetVideoMode(monitor)->RefreshRate;
        }
    }

    /// <summary>When frames are built and shown (see <see cref="FramePacing"/>).</summary>
    public FramePacing Pacing { get; } = new();

    private long inputStamp;
    private readonly System.Collections.Concurrent.ConcurrentQueue<long> frameStamps = new();

    private void StampInput()
    {
        if (DesktopStats.Enabled)
            Interlocked.CompareExchange(ref inputStamp, System.Diagnostics.Stopwatch.GetTimestamp(), 0);
    }

    /// <summary>Called before a frame is built: the first thing the user did since the frame before belongs to it.</summary>
    public void BeginFrameStamp()
    {
        if (DesktopStats.Enabled)
            frameStamps.Enqueue(Interlocked.Exchange(ref inputStamp, 0));
    }

    /// <summary>Shows a window that was asked to be shown, once a frame was made for it (call it from the main thread, once in a while).</summary>
    public void CompleteReveal()
    {
        // (The Vulkan renderer does not say when it shows a frame: a window of that is shown after a moment)
        if (!revealPending || disposed || (Volatile.Read(ref presented) < revealAfter && System.Diagnostics.Stopwatch.GetTimestamp() < revealDeadline))
            return;

        revealPending = false;
        GLFW.ShowWindow(windowPtr);
        if (focusOnReveal)
            GLFW.FocusWindow(windowPtr);

        focusOnReveal = false;
    }

    private void FramePresented()
    {
        Interlocked.Increment(ref presented);
        if (DesktopStats.Enabled && frameStamps.TryDequeue(out var stamp) && stamp != 0)
            DesktopStats.RecordLatency(System.Diagnostics.Stopwatch.GetTimestamp() - stamp);
    }

    /// <summary>Takes in what the user did to every window (each window's own callbacks are told). Call it from the main thread, as late as possible before it is used.</summary>
    public static void PumpEvents() => GLFW.PollEvents();

    /// <summary>What this window plays from <c>RALSEI_INPUT_SCRIPT</c>, once a frame.</summary>
    public void PlayScript()
    {
        if (disposed)
            return;

        scriptedInput?.Play(++scriptFrame,
            (key, down) =>
            {
                if (down)
                    input.KeyDown(key);
                else
                    input.KeyUp(key);
            },
            MouseMoved, MouseButton, input.Scroll, input.TextInput);
    }

    private readonly ScriptedInput? scriptedInput;

    private int scriptFrame;
}
