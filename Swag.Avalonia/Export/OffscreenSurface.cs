using System;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGLES2;
using Swag.Avalonia.Integration;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform;
using Ralsei.Platform.OpenGL;

namespace Swag.Avalonia.Export;

/// <summary>
/// Somewhere to render a video to: an OpenGL ES context of its own that does not show anywhere, with the frame that was
/// drawn read back into memory. It belongs to the thread that creates it.
/// </summary>
public sealed class OffscreenSurface : IOpenGLSurface, IFrameReceiver, IDisposable
{
    private class BindingsContext(EglApi egl) : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => egl.GetProcAddress(procName);
    }

    public Vector2i RenderSize { get; }

    // The frames are drawn into a target of the size of the video, and read from there
    public int Framebuffer => drawFramebuffer;

    public Vector2i FramebufferSize => RenderSize;

    // The context is only gone when this is disposed, and that is after everything that used it
    public bool IsContextLost => false;

    /// <summary>Where the next frame goes: tightly packed RGBA8 with the top row first.</summary>
    public byte[]? Target { get; set; }

    private readonly EglApi? egl;
    private readonly IntPtr display;
    private readonly IntPtr context;
    private readonly IntPtr surface;

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

    public void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)
    {
        var target = Target;
        if (target is null)
            return;

        if (target.Length < rgba.Length)
            throw new InvalidOperationException("The buffer for the frame is too small");

        rgba.CopyTo(target);
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
            if (drawFramebuffer != 0)
                GL.DeleteFramebuffer(drawFramebuffer);
            if (renderbuffer != 0)
                GL.DeleteRenderbuffer(renderbuffer);

            drawFramebuffer = renderbuffer = 0;
        }

        egl.MakeNothingCurrent(display);

        if (surface != IntPtr.Zero)
            egl.DestroySurface(display, surface);
        if (context != IntPtr.Zero)
            egl.DestroyContext(display, context);

        // The display is not terminated: it is shared with whatever else in the process uses EGL
    }
}

/// <summary>What the surface of an export needs to know.</summary>
public sealed record ExportSurfaceSettings(Vector2i Size);
