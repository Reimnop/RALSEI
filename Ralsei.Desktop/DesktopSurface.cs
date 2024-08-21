using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Ralsei.Desktop;

public unsafe class DesktopSurface : DesktopWindow, IOpenGLSurface
{
    public bool IsContextLost { get; set; }

    private readonly int framebuffer;

    public DesktopSurface(DesktopSurfaceSettings surfaceSettings, GlfwService glfw) : base(surfaceSettings, glfw)
    {
        GLFW.MakeContextCurrent(WindowPtr);
        GLFW.SwapInterval(surfaceSettings.VSync ? 1 : 0);

        framebuffer = GL.GenFramebuffer();
    }

    public override void Dispose()
    {
        // We don't need to dispose the framebuffer because
        // it will be deleted automatically when context is lost

        base.Dispose();
        IsContextLost = true;
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

        // The contents of the back buffer are undefined once it has been swapped, so anything
        // that needs the finished frame has to get it before that
        OnFrameRendered(framebufferSize);

        GLFW.SwapBuffers(WindowPtr);
    }

    public IntPtr GetProcAddress(string procName)
        => GLFW.GetProcAddress(procName);

    /// <summary>
    /// Called when the frame is complete in the default framebuffer, right before it is shown.
    /// </summary>
    protected virtual void OnFrameRendered(Vector2i framebufferSize)
    {
    }
}
