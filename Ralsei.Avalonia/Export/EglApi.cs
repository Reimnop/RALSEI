using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ralsei.Avalonia.Export;

/// <summary>
/// The little of EGL that an export needs, loaded the way Avalonia does it. The EGL of Avalonia on Windows is ANGLE, which
/// has one function to find all the others with, instead of the functions themselves, so OpenTK can not be used for it.
/// </summary>
internal sealed unsafe class EglApi
{
    public const int None = 0x3038;
    public const int SurfaceType = 0x3033;
    public const int PbufferBit = 0x0001;
    public const int RenderableType = 0x3040;
    public const int OpenGLES2Bit = 0x0004;
    public const int OpenGLES3Bit = 0x0040;
    public const int RedSize = 0x3024;
    public const int GreenSize = 0x3023;
    public const int BlueSize = 0x3022;
    public const int AlphaSize = 0x3021;
    public const int Width = 0x3057;
    public const int Height = 0x3056;
    public const int ContextClientVersion = 0x3098;

    private static readonly object Lock = new();
    private static EglApi? instance;

    private readonly Func<string, IntPtr> getProcAddress;

    private readonly delegate* unmanaged<IntPtr, IntPtr> getDisplay;
    private readonly delegate* unmanaged<IntPtr, int*, int*, uint> initialize;
    private readonly delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, uint> chooseConfig;
    private readonly delegate* unmanaged<IntPtr, IntPtr, IntPtr, int*, IntPtr> createContext;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr> createPbufferSurface;
    private readonly delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint> makeCurrent;
    private readonly delegate* unmanaged<IntPtr, IntPtr, uint> destroyContext;
    private readonly delegate* unmanaged<IntPtr, IntPtr, uint> destroySurface;
    private readonly delegate* unmanaged<IntPtr> getCurrentContext;
    private readonly delegate* unmanaged<int> getError;

    private EglApi(Func<string, IntPtr> getProcAddress)
    {
        this.getProcAddress = getProcAddress;

        getDisplay = (delegate* unmanaged<IntPtr, IntPtr>) Require("eglGetDisplay");
        initialize = (delegate* unmanaged<IntPtr, int*, int*, uint>) Require("eglInitialize");
        chooseConfig = (delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, uint>) Require("eglChooseConfig");
        createContext = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int*, IntPtr>) Require("eglCreateContext");
        createPbufferSurface = (delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr>) Require("eglCreatePbufferSurface");
        makeCurrent = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint>) Require("eglMakeCurrent");
        destroyContext = (delegate* unmanaged<IntPtr, IntPtr, uint>) Require("eglDestroyContext");
        destroySurface = (delegate* unmanaged<IntPtr, IntPtr, uint>) Require("eglDestroySurface");
        getCurrentContext = (delegate* unmanaged<IntPtr>) Require("eglGetCurrentContext");
        getError = (delegate* unmanaged<int>) Require("eglGetError");
    }

    /// <summary>
    /// EGL, or an exception that says there is none.
    /// </summary>
    public static EglApi Get()
    {
        lock (Lock)
            return instance ??= new EglApi(FindGetProcAddress());
    }

    /// <summary>
    /// Finds an EGL or OpenGL ES function, which is the same for every context.
    /// </summary>
    public IntPtr GetProcAddress(string name)
        => getProcAddress(name);

    public IntPtr GetDisplay()
        => getDisplay(IntPtr.Zero);

    public bool Initialize(IntPtr display)
    {
        int major, minor;
        return initialize(display, &major, &minor) != 0;
    }

    public bool ChooseConfig(IntPtr display, ReadOnlySpan<int> attributes, out IntPtr config)
    {
        var chosen = IntPtr.Zero;
        int count;

        fixed (int* attributesPointer = attributes)
        {
            var ok = chooseConfig(display, attributesPointer, &chosen, 1, &count) != 0;
            config = chosen;
            return ok && count > 0 && chosen != IntPtr.Zero;
        }
    }

    public IntPtr CreateContext(IntPtr display, IntPtr config, ReadOnlySpan<int> attributes)
    {
        fixed (int* attributesPointer = attributes)
            return createContext(display, config, IntPtr.Zero, attributesPointer);
    }

    public IntPtr CreatePbufferSurface(IntPtr display, IntPtr config, ReadOnlySpan<int> attributes)
    {
        fixed (int* attributesPointer = attributes)
            return createPbufferSurface(display, config, attributesPointer);
    }

    public bool MakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context)
        => makeCurrent(display, draw, read, context) != 0;

    public bool MakeNothingCurrent(IntPtr display)
        => makeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0;

    public void DestroyContext(IntPtr display, IntPtr context)
        => destroyContext(display, context);

    public void DestroySurface(IntPtr display, IntPtr surface)
        => destroySurface(display, surface);

    public IntPtr GetCurrentContext()
        => getCurrentContext();

    public int GetError()
        => getError();

    private IntPtr Require(string name)
    {
        var address = getProcAddress(name);
        if (address == IntPtr.Zero)
            throw new InvalidOperationException($"OpenGL could not be started ({name} is missing)");

        return address;
    }

    private static Func<string, IntPtr> FindGetProcAddress()
    {
        if (OperatingSystem.IsWindows())
        {
            // ANGLE as it comes with Avalonia: its only exported EGL function is a renamed eglGetProcAddress
            if (NativeLibrary.TryLoad("av_libGLESv2.dll", out var angle) &&
                NativeLibrary.TryGetExport(angle, "EGL_GetProcAddress", out var angleExport))
                return Wrap((delegate* unmanaged<byte*, IntPtr>) angleExport);
        }

        string[] libraries = OperatingSystem.IsMacOS()
            ? ["libEGL.dylib", "libEGL"]
            : ["libEGL.so.1", "libEGL.so", "libEGL", "libEGL.dll"];

        foreach (var library in libraries)
        {
            if (NativeLibrary.TryLoad(library, out var handle) &&
                NativeLibrary.TryGetExport(handle, "eglGetProcAddress", out var export))
                return Wrap((delegate* unmanaged<byte*, IntPtr>) export);
        }

        throw new InvalidOperationException("OpenGL ES (EGL) was not found on this computer");
    }

    private static Func<string, IntPtr> Wrap(delegate* unmanaged<byte*, IntPtr> function)
        => name =>
        {
            Span<byte> bytes = stackalloc byte[name.Length * 3 + 1];
            var length = Encoding.UTF8.GetBytes(name, bytes);
            bytes[length] = 0;

            fixed (byte* pointer = bytes)
                return function(pointer);
        };
}
