using System;
using Avalonia.VisualTree;
using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Ralsei.Avalonia.Integration;

public class RalseiSurface(RalseiView view, RalseiRenderSettings renderSettings) : IOpenGLSurface, IDisposable
{
    /// <summary>
    /// The size of the view in pixels.
    /// </summary>
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

    private readonly int framebuffer = GL.GenFramebuffer();

    public void Present(int texture, Vector2i size, ColorRgba clearColor)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, texture, 0);

        var dstSize = ViewSize;
        renderSettings.RenderWidth = size.X;
        renderSettings.RenderHeight = size.Y;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, TargetFramebufferHandle);

        // Clear the default framebuffer
        GL.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        GL.Viewport(0, 0, dstSize.X, dstSize.Y);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // Blit the framebuffer to the default framebuffer. At a smaller size than the view it is stretched, which
        // looks better smoothed than as blocks.
        var filter = size == dstSize ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear;
        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            0, 0, dstSize.X, dstSize.Y,
            ClearBufferMask.ColorBufferBit, filter);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(framebuffer);
    }
}
