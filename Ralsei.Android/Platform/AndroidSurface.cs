using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.Egl;
using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Ralsei.Android.Platform;

/// <summary>
/// The OpenGL ES side of the window: an EGL context, with a surface on the window the renderer draws to.
/// </summary>
public sealed class AndroidSurface : IOpenGLSurface, IDisposable
{
    private class BindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => Egl.GetProcAddress(procName);
    }

    public Vector2i RenderSize => surfaceSettings.GetRenderSize(FramebufferSize);

    public Vector2i FramebufferSize
    {
        get
        {
            if (!Egl.QuerySurface(eglDisplay, eglSurface, Egl.WIDTH, out var width) ||
                !Egl.QuerySurface(eglDisplay, eglSurface, Egl.HEIGHT, out var height))
                throw new SurfaceLostException("The size of the EGL surface can not be queried any more");

            return new Vector2i(width, height);
        }
    }

    // The context is destroyed together with this surface, which frees everything that was created in it. Deleting the
    // objects one by one first is wasted work (and has to wait for the GPU), so the renderer is told there is no point.
    public bool IsContextLost => abandoned || Egl.GetCurrentContext() != eglContext;

    private readonly AndroidSurfaceSettings surfaceSettings;
    private readonly NativeWindow nativeWindow;

    private readonly IntPtr eglDisplay;
    private readonly IntPtr eglContext;
    private IntPtr eglSurface;

    private readonly int framebuffer;
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
            // Initialize EGL
            eglDisplay = Egl.GetDisplay(Egl.DEFAULT_DISPLAY);
            if (eglDisplay == IntPtr.Zero || !Egl.Initialize(eglDisplay, out _, out _))
                throw new InvalidOperationException("Failed to initialize EGL");

            int[] configAttribs =
            [
                Egl.RENDERABLE_TYPE, glSettings.MajorVersion == 3 ? Egl.OPENGL_ES3_BIT : Egl.OPENGL_ES2_BIT,
                Egl.SURFACE_TYPE,    Egl.WINDOW_BIT,
                Egl.RED_SIZE,        8,
                Egl.GREEN_SIZE,      8,
                Egl.BLUE_SIZE,       8,
                Egl.ALPHA_SIZE,      0,
                Egl.NONE
            ];

            var configs = new IntPtr[1];
            if (!Egl.ChooseConfig(eglDisplay, configAttribs, configs, 1, out var configCount) || configCount < 1 || configs[0] == IntPtr.Zero)
                throw new InvalidOperationException("Failed to choose an EGL config");

            var config = configs[0];

            int[] contextAttribs =
            [
                Egl.CONTEXT_CLIENT_VERSION, glSettings.MajorVersion,
                Egl.NONE
            ];

            eglContext = Egl.CreateContext(eglDisplay, config, IntPtr.Zero, contextAttribs);
            if (eglContext == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create an EGL context");

            eglSurface = Egl.CreateWindowSurface(eglDisplay, config, nativeWindow.Handle, IntPtr.Zero);
            if (eglSurface == IntPtr.Zero)
                throw new SurfaceLostException("Failed to create an EGL surface for the window");

            GLLoader.LoadBindings(new BindingsContext());

            if (!Egl.MakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext))
                throw new InvalidOperationException("Failed to make the EGL context current");

            // Without this, frames are swapped as soon as they are done instead of when the display is ready for them
            Egl.SwapInterval(eglDisplay, surfaceSettings.VSync ? 1 : 0);

            framebuffer = GL.GenFramebuffer();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Present(int texture, Vector2i size, ColorRgba clearColor)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, texture, 0);

        var framebufferSize = FramebufferSize;
        var offset = (framebufferSize - size) / 2;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);

        // Clear the default framebuffer
        GL.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        GL.Viewport(0, 0, framebufferSize.X, framebufferSize.Y);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // Blit the framebuffer to the default framebuffer
        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            offset.X, offset.Y, offset.X + size.X, offset.Y + size.Y,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        if (!Egl.SwapBuffers(eglDisplay, eglSurface))
            throw new SurfaceLostException("Failed to swap the buffers of the EGL surface");
    }

    /// <summary>
    /// Says that the window and the context are going away, so that the renderer does not clean up after itself.
    /// </summary>
    public void Abandon()
    {
        abandoned = true;
    }

    public void Dispose()
    {
        // We don't need to dispose the framebuffer because
        // it will be deleted automatically when the context is destroyed
        if (eglDisplay != IntPtr.Zero)
        {
            Egl.MakeCurrent(eglDisplay, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (eglSurface != IntPtr.Zero)
            {
                Egl.DestroySurface(eglDisplay, eglSurface);
                eglSurface = IntPtr.Zero;
            }

            if (eglContext != IntPtr.Zero)
                Egl.DestroyContext(eglDisplay, eglContext);

            Egl.Terminate(eglDisplay);
        }

        nativeWindow.Dispose();
    }
}
