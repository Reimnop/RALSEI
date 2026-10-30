using System;
using Avalonia.VisualTree;
using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Swag.Avalonia.Integration;

public class SwagSurface(SwagView view, SwagRenderSettings renderSettings) : IOpenGLSurface, IDisposable
{
    /// <summary>The size of the view in pixels.</summary>
    public Vector2i ViewSize
    {
        get
        {
            var size = view.Bounds.Size;
            var scaling = view.GetPresentationSource()?.RenderScaling ?? 1.0;
            return new Vector2i(
                Math.Max(1, (int)(size.Width * scaling)),
                Math.Max(1, (int)(size.Height * scaling)));
        }
    }

    public Vector2i RenderSize
    {
        get
        {
            var size = ViewSize;
            var scale = Math.Clamp(renderSettings.RenderScale, 0.1, 1.0);
            return new Vector2i(
                Math.Max(1, (int)(size.X * scale)),
                Math.Max(1, (int)(size.Y * scale)));
        }
    }

    // Avalonia does not "lose" the context
    public bool IsContextLost => false;
    public int TargetFramebufferHandle { get; set; }

    // The renderer draws at the render size, which can be smaller than the view: it is stretched over the view when it is shown
    public int Framebuffer => frame.Get(RenderSize);

    public Vector2i FramebufferSize => RenderSize;

    private readonly RenderTarget frame = new();

    /// <summary>Stretches the frame over the view. At a smaller size than the view it looks better smoothed than as blocks.</summary>
    public void SwapBuffers()
    {
        var size = RenderSize;
        var dstSize = ViewSize;
        renderSettings.RenderWidth = size.X;
        renderSettings.RenderHeight = size.Y;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, frame.Get(size));
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, TargetFramebufferHandle);

        GL.Disable(EnableCap.ScissorTest);
        GL.Viewport(0, 0, dstSize.X, dstSize.Y);

        var filter = size == dstSize ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear;
        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            0, 0, dstSize.X, dstSize.Y,
            ClearBufferMask.ColorBufferBit, filter);
    }

    public void Dispose()
    {
        frame.Dispose();
    }

    /// <summary>A framebuffer of the render size, that is made again when the size changes.</summary>
    private sealed class RenderTarget : IDisposable
    {
        private int framebuffer, renderbuffer;
        private Vector2i size;

        /// <summary>The framebuffer, as big as <paramref name="wanted"/>.</summary>
        public int Get(Vector2i wanted)
        {
            if (framebuffer != 0 && size == wanted)
                return framebuffer;

            Dispose();

            framebuffer = GL.GenFramebuffer();
            renderbuffer = GL.GenRenderbuffer();
            size = wanted;

            // The frame is converted to 8 bits per channel on the way, the textures of the renderer do not have to be
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, wanted.X, wanted.Y);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, renderbuffer);

            return framebuffer;
        }

        public void Dispose()
        {
            if (framebuffer != 0)
                GL.DeleteFramebuffer(framebuffer);
            if (renderbuffer != 0)
                GL.DeleteRenderbuffer(renderbuffer);

            framebuffer = renderbuffer = 0;
        }
    }
}
