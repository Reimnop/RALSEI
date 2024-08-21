using System.Runtime.InteropServices;
using Android.Runtime;
using Android.Views;
using Ralsei.Mathematics;

namespace Ralsei.Android.Platform;

/// <summary>
/// Thrown when the window that is rendered to is not there any more, e.g. because the app was sent to the background.
/// </summary>
public sealed class SurfaceLostException(string message) : Exception(message);

/// <summary>
/// The native side of a <see cref="Surface"/>, which is what EGL and Vulkan create their surfaces from.
/// Holds a reference to the window until disposed.
/// </summary>
public sealed class NativeWindow : IDisposable
{
    private const string LibAndroid = "libandroid.so";

    public IntPtr Handle { get; private set; }

    public Vector2i Size => new(ANativeWindow_getWidth(Handle), ANativeWindow_getHeight(Handle));

    private NativeWindow(IntPtr handle)
    {
        Handle = handle;
    }

    public static NativeWindow FromSurface(Surface surface)
    {
        var handle = ANativeWindow_fromSurface(JNIEnv.Handle, surface.Handle);
        if (handle == IntPtr.Zero)
            throw new SurfaceLostException("The surface does not have a native window");

        return new NativeWindow(handle);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero)
            return;

        ANativeWindow_release(Handle);
        Handle = IntPtr.Zero;
    }

    [DllImport(LibAndroid)]
    private static extern IntPtr ANativeWindow_fromSurface(IntPtr env, IntPtr surface);

    [DllImport(LibAndroid)]
    private static extern void ANativeWindow_release(IntPtr window);

    [DllImport(LibAndroid)]
    private static extern int ANativeWindow_getWidth(IntPtr window);

    [DllImport(LibAndroid)]
    private static extern int ANativeWindow_getHeight(IntPtr window);
}
