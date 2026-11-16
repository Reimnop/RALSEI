using System.Runtime.InteropServices;
using OpenTK.Graphics.Egl;

namespace Swag.Wasm;

// WASM doesn't like resolving EGL functions at runtime
internal static unsafe partial class PInvokeEgl
{
    [LibraryImport("libEGL", EntryPoint = "eglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetProcAddress(string name);

    [DllImport("libEGL", EntryPoint = "eglGetDisplay")] public static extern EGLDisplay GetDisplay(IntPtr nativeDisplay);
    [DllImport("libEGL", EntryPoint = "eglInitialize")] private static extern int Initialize(EGLDisplay display, int* major, int* minor);
    [DllImport("libEGL", EntryPoint = "eglTerminate")] private static extern int Terminate_(EGLDisplay display);
    [DllImport("libEGL", EntryPoint = "eglChooseConfig")] private static extern int ChooseConfig(EGLDisplay display, int* attribs, EGLConfig* configs, int configSize, int* numConfigs);
    [DllImport("libEGL", EntryPoint = "eglCreateContext")] private static extern EGLContext CreateContext(EGLDisplay display, EGLConfig config, EGLContext share, int* attribs);
    [DllImport("libEGL", EntryPoint = "eglDestroyContext")] private static extern int DestroyContext_(EGLDisplay display, EGLContext context);
    [DllImport("libEGL", EntryPoint = "eglCreateWindowSurface")] public static extern EGLSurface CreateWindowSurface(EGLDisplay display, EGLConfig config, IntPtr window, int* attribs);
    [DllImport("libEGL", EntryPoint = "eglDestroySurface")] private static extern int DestroySurface_(EGLDisplay display, EGLSurface surface);
    [DllImport("libEGL", EntryPoint = "eglMakeCurrent")] private static extern int MakeCurrent_(EGLDisplay display, EGLSurface draw, EGLSurface read, EGLContext context);
    [DllImport("libEGL", EntryPoint = "eglSwapBuffers")] private static extern int SwapBuffers_(EGLDisplay display, EGLSurface surface);
    [DllImport("libEGL", EntryPoint = "eglQuerySurface")] private static extern int QuerySurface(EGLDisplay display, EGLSurface surface, int attribute, int* value);
    [DllImport("libEGL", EntryPoint = "eglGetCurrentContext")] public static extern EGLContext GetCurrentContext();

    public static bool Initialize(EGLDisplay display, out int major, out int minor)
    {
        int a = 0, b = 0;
        var ok = Initialize(display, &a, &b) != 0;
        major = a;
        minor = b;
        return ok;
    }

    public static bool Terminate(EGLDisplay display) => Terminate_(display) != 0;

    public static bool ChooseConfig(EGLDisplay display, int[] attribs, EGLConfig[] configs, int configSize, int[] numConfigs)
    {
        fixed (int* a = attribs)
        fixed (EGLConfig* c = configs)
        fixed (int* n = numConfigs)
            return ChooseConfig(display, a, c, configSize, n) != 0;
    }

    public static EGLContext CreateContext(EGLDisplay display, EGLConfig config, EGLContext share, int[] attribs)
    {
        fixed (int* a = attribs)
            return CreateContext(display, config, share, a);
    }

    public static bool DestroyContext(EGLDisplay display, EGLContext context) => DestroyContext_(display, context) != 0;
    public static bool DestroySurface(EGLDisplay display, EGLSurface surface) => DestroySurface_(display, surface) != 0;
    public static bool MakeCurrent(EGLDisplay display, EGLSurface draw, EGLSurface read, EGLContext context) => MakeCurrent_(display, draw, read, context) != 0;
    public static bool SwapBuffers(EGLDisplay display, EGLSurface surface) => SwapBuffers_(display, surface) != 0;

    public static bool QuerySurface(EGLDisplay display, EGLSurface surface, SurfaceQueryAttribute attribute, ref int value)
    {
        fixed (int* v = &value)
            return QuerySurface(display, surface, (int) attribute, v) != 0;
    }
}
