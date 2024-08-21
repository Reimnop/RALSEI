using System;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGLES2;
using Ralsei.Avalonia.Integration;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Ralsei.Avalonia.Export;

/// <summary>
/// Somewhere to render a video to: an OpenGL ES context of its own that does not show anywhere, with the frame that was
/// drawn read back into memory. It belongs to the thread that creates it.
/// </summary>
public sealed class OffscreenSurface : IOpenGLSurface, IDisposable
{
    private class BindingsContext(EglApi egl) : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => egl.GetProcAddress(procName);
    }

    public Vector2i RenderSize { get; }

    // The context is only gone when this is disposed, and that is after everything that used it
    public bool IsContextLost => false;

    /// <summary>
    /// Where the next frame goes: tightly packed RGBA8 with the bottom row first.
    /// </summary>
    public byte[]? Target { get; set; }

    private readonly EglApi? egl;
    private readonly IntPtr display;
    private readonly IntPtr context;
    private readonly IntPtr surface;

    private int readFramebuffer;
    private int drawFramebuffer;
    private int renderbuffer;
    private bool disposed;

    public OffscreenSurface(OpenGLSettings glSettings, ExportSurfaceSettings settings)
    {
        RenderSize = settings.Size;

        try
        {
            egl = EglApi.Get();

            display = egl.GetDisplay();
            if (display == IntPtr.Zero || !egl.Initialize(display))
                throw new InvalidOperationException("OpenGL could not be started (EGL)");

            int[] configAttributes =
            [
                EglApi.RenderableType, glSettings.MajorVersion == 3 ? EglApi.OpenGLES3Bit : EglApi.OpenGLES2Bit,
                EglApi.SurfaceType,    EglApi.PbufferBit,
                EglApi.RedSize,        8,
                EglApi.GreenSize,      8,
                EglApi.BlueSize,       8,
                EglApi.AlphaSize,      8,
                EglApi.None
            ];

            if (!egl.ChooseConfig(display, configAttributes, out var config))
                throw new InvalidOperationException("OpenGL could not be started (no configuration)");

            int[] contextAttributes =
            [
                EglApi.ContextClientVersion, glSettings.MajorVersion,
                EglApi.None
            ];

            context = egl.CreateContext(display, config, contextAttributes);
            if (context == IntPtr.Zero)
                throw new InvalidOperationException($"OpenGL could not be started (no context, error 0x{egl.GetError():X})");

            // Everything is drawn into textures of the size of the video, this is only here to have something to make
            // the context current with
            surface = egl.CreatePbufferSurface(display, config, [EglApi.Width, 1, EglApi.Height, 1, EglApi.None]);
            if (surface == IntPtr.Zero)
                throw new InvalidOperationException("OpenGL could not be started (no surface)");

            if (!egl.MakeCurrent(display, surface, surface, context))
                throw new InvalidOperationException("OpenGL could not be started (the context can not be used)");

            // The bindings are the same for every context. The ones that are there are not replaced while the player
            // is drawing with them.
            if (!GlBindingsState.IsLoaded)
            {
                GLLoader.LoadBindings(new BindingsContext(egl));
                GlBindingsState.IsLoaded = true;
            }

            readFramebuffer = GL.GenFramebuffer();
            drawFramebuffer = GL.GenFramebuffer();
            renderbuffer = GL.GenRenderbuffer();

            // The frame is converted to bytes on the way, the textures of the renderer do not have to be
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, RenderSize.X, RenderSize.Y);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, drawFramebuffer);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, renderbuffer);

            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferStatus.FramebufferComplete)
                throw new InvalidOperationException($"A video of {RenderSize.X} × {RenderSize.Y} is too big for this computer");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Present(int texture, Vector2i size, ColorRgba clearColor)
    {
        var target = Target;
        if (target is null)
            return;

        if (target.Length < size.X * size.Y * 4)
            throw new InvalidOperationException("The buffer for the frame is too small");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, readFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, texture, 0);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);

        GL.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        GL.Viewport(0, 0, size.X, size.Y);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            0, 0, size.X, size.Y,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, drawFramebuffer);
        GL.PixelStorei(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, target.AsSpan());
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, readFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, 0, 0);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (egl is null || display == IntPtr.Zero)
            return;

        if (context != IntPtr.Zero && egl.GetCurrentContext() == context)
        {
            if (readFramebuffer != 0)
                GL.DeleteFramebuffer(readFramebuffer);
            if (drawFramebuffer != 0)
                GL.DeleteFramebuffer(drawFramebuffer);
            if (renderbuffer != 0)
                GL.DeleteRenderbuffer(renderbuffer);

            readFramebuffer = drawFramebuffer = renderbuffer = 0;
        }

        egl.MakeNothingCurrent(display);

        if (surface != IntPtr.Zero)
            egl.DestroySurface(display, surface);
        if (context != IntPtr.Zero)
            egl.DestroyContext(display, context);

        // The display is not terminated: it is shared with whatever else in the process uses EGL
    }
}

/// <summary>
/// What the surface of an export needs to know.
/// </summary>
public sealed record ExportSurfaceSettings(Vector2i Size);
