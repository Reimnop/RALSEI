using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;

namespace Swag.Desktop;

public unsafe class DesktopSurface : DesktopWindow, IOpenGLSurface
{
    public bool IsContextLost { get; set; }

    public int Framebuffer => 0;

    public DesktopSurface(DesktopSurfaceSettings surfaceSettings, GlfwService glfw) : base(surfaceSettings, glfw)
    {
        GLFW.MakeContextCurrent(WindowPtr);
        GLFW.SwapInterval(surfaceSettings.VSync ? 1 : 0);
    }

    public override void Dispose()
    {
        base.Dispose();
        IsContextLost = true;
    }

    public void SwapBuffers()
    {
        GLFW.SwapBuffers(WindowPtr);
    }

    public IntPtr GetProcAddress(string procName)
        => GLFW.GetProcAddress(procName);
}
