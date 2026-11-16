using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.Egl;
using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using All = OpenTK.Graphics.Egl.All;

namespace Swag.Android.Platform;

/// <summary>The OpenGL ES side of the window: an EGL context, with a surface on the window the renderer draws to.</summary>
public sealed class AndroidSurface : IOpenGLSurface, IDisposable
{
    private class BindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => Egl.GetProcAddress(procName);
    }

    public int Framebuffer => 0;

    public Vector2i RenderSize => surfaceSettings.GetRenderSize(FramebufferSize);

    public Vector2i FramebufferSize
    {
        get
        {
            int width = 0, height = 0;
            
            if (!Egl.QuerySurface(eglDisplay, eglSurface, SurfaceQueryAttribute.Width, ref width) ||
                !Egl.QuerySurface(eglDisplay, eglSurface, SurfaceQueryAttribute.Height, ref height))
                throw new SurfaceLostException("The size of the EGL surface can not be queried any more");

            return new Vector2i(width, height);
        }
    }

    // The context is destroyed together with this surface, which frees everything that was created in it. Deleting the
    // objects one by one first is wasted work (and has to wait for the GPU), so the renderer is told there is no point.
    public bool IsContextLost => abandoned || Egl.GetCurrentContext() != eglContext;

    private readonly AndroidSurfaceSettings surfaceSettings;
    private readonly NativeWindow nativeWindow;

    private readonly EGLDisplay eglDisplay;
    private readonly EGLContext eglContext;
    private EGLSurface eglSurface;

    private volatile bool abandoned;

    public AndroidSurface(OpenGLSettings glSettings, AndroidSurfaceSettings surfaceSettings, AndroidSurfaceContext surfaceContext)
    {
        this.surfaceSettings = surfaceSettings;

        if (!glSettings.IsES)
            throw new NotSupportedException("Desktop OpenGL is not supported on Android, use OpenGL ES instead");

        var surface = surfaceContext.Surface;
        if (surface is null || !surface.IsValid)
            throw new SurfaceLostException("There is no surface to render to");

        nativeWindow = NativeWindow.FromSurface(surface);

        try
        {
            eglDisplay = Egl.GetDisplay((IntPtr) All.DefaultDisplay);
            if (eglDisplay.Value == IntPtr.Zero || !Egl.Initialize(eglDisplay, out _, out _))
                throw new InvalidOperationException("Failed to initialize EGL");

            int[] configAttribs =
            [
                (int) ConfigAttribute.RenderableType, glSettings.MajorVersion == 3 
                    ? (int) RenderableTypeMask.OpenglEs3Bit
                    : (int) RenderableTypeMask.OpenglEs2Bit,
                (int) ConfigAttribute.SurfaceType,    (int) SurfaceTypeMask.WindowBit,
                (int) ConfigAttribute.RedSize,        8,
                (int) ConfigAttribute.GreenSize,      8,
                (int) ConfigAttribute.BlueSize,       8,
                (int) ConfigAttribute.AlphaSize,      0,
                (int) ConfigAttribute.None
            ];

            var configs = new EGLConfig[1];
            var configCounts = new int[1];
            if (!Egl.ChooseConfig(eglDisplay, configAttribs, configs, 1, configCounts) || 
                configCounts[0] < 1 || 
                configs[0].Value == IntPtr.Zero)
                throw new InvalidOperationException("Failed to choose an EGL config");

            var config = configs[0];

            int[] contextAttribs =
            [
                (int) ContextAttribute.ContextClientVersion, glSettings.MajorVersion,
                (int) ContextAttribute.None
            ];

            eglContext = Egl.CreateContext(eglDisplay, config, EGLContext.NoContext, contextAttribs);
            if (eglContext.Value == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create an EGL context");

            unsafe
            {
                eglSurface = Egl.CreateWindowSurface(eglDisplay, config, nativeWindow.Handle, (int*)0);
                if (eglSurface.Value == IntPtr.Zero)
                    throw new SurfaceLostException("Failed to create an EGL surface for the window");
            }

            GLLoader.LoadBindings(new BindingsContext());

            if (!Egl.MakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext))
                throw new InvalidOperationException("Failed to make the EGL context current");

            // Without this, frames are swapped as soon as they are done instead of when the display is ready for them
            Egl.SwapInterval(eglDisplay, surfaceSettings.VSync ? 1 : 0);

        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SwapBuffers()
    {
        if (!Egl.SwapBuffers(eglDisplay, eglSurface))
            throw new SurfaceLostException("Failed to swap the buffers of the EGL surface");
    }

    /// <summary>Says that the window and the context are going away, so that the renderer does not clean up after itself.</summary>
    public void Abandon()
    {
        abandoned = true;
    }

    public void Dispose()
    {
        if (eglDisplay.Value != IntPtr.Zero)
        {
            Egl.MakeCurrent(eglDisplay, EGLSurface.NoSurface, EGLSurface.NoSurface, EGLContext.NoContext);

            if (eglSurface.Value != IntPtr.Zero)
            {
                Egl.DestroySurface(eglDisplay, eglSurface);
                eglSurface = EGLSurface.NoSurface;
            }

            if (eglContext.Value != IntPtr.Zero)
                Egl.DestroyContext(eglDisplay, eglContext);

            Egl.Terminate(eglDisplay);
        }

        nativeWindow.Dispose();
    }
}
