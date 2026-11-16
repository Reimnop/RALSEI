using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.Egl;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using All = OpenTK.Graphics.Egl.All;

namespace Swag.Wasm;

public class WasmSurface : IOpenGLSurface, IDisposable
{
    private class BindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => PInvokeEgl.GetProcAddress(procName);
    }
    
    public int Framebuffer => 0;

    public Vector2i FramebufferSize => RenderSize;

    public Vector2i RenderSize
    {
        get
        {
            int width = 0, height = 0;

            if (!PInvokeEgl.QuerySurface(display, surface, SurfaceQueryAttribute.Width, ref width) ||
                !PInvokeEgl.QuerySurface(display, surface, SurfaceQueryAttribute.Height, ref height))
                throw new InvalidOperationException("The size of the EGL surface can not be queried any more");

            return new Vector2i(width, height);
        }
    }

    public bool IsContextLost => PInvokeEgl.GetCurrentContext() != context;

    private readonly EGLDisplay display;
    private readonly EGLContext context;
    private EGLSurface surface;
    
    public WasmSurface(OpenGLSettings glSettings)
    {
        if (!glSettings.IsES)
            throw new InvalidOperationException("Only OpenGL ES is supported on WebAssembly");
        
        display = PInvokeEgl.GetDisplay((IntPtr) All.DefaultDisplay);
        if (display.Value == IntPtr.Zero)
            throw new InvalidOperationException("Failed to get EGL display");
        
        if (!PInvokeEgl.Initialize(display, out _, out _))
            throw new InvalidOperationException("Failed to initialize EGL");
        
        int[] configAttribs =
        {
            (int) ConfigAttribute.RenderableType, glSettings.MajorVersion == 3
                ? (int) RenderableTypeMask.OpenglEs3Bit
                : (int) RenderableTypeMask.OpenglEs2Bit,
            (int) ConfigAttribute.SurfaceType, (int) SurfaceTypeMask.WindowBit,
            (int) ConfigAttribute.RedSize, 8,
            (int) ConfigAttribute.GreenSize, 8,
            (int) ConfigAttribute.BlueSize, 8,
            (int) ConfigAttribute.AlphaSize, 0,
            (int) ConfigAttribute.None
        };

        var configs = new EGLConfig[1];
        var configCounts = new int[1];
        if (!PInvokeEgl.ChooseConfig(display, configAttribs, configs, 1, configCounts) ||
            configCounts[0] < 1 ||
            configs[0].Value == IntPtr.Zero)
            throw new InvalidOperationException("Failed to choose EGL config");

        var config = configs[0];

        int[] contextAttribs =
        {
            (int) ContextAttribute.ContextClientVersion, glSettings.MajorVersion,
            (int) ContextAttribute.None
        };

        context = PInvokeEgl.CreateContext(display, config, EGLContext.NoContext, contextAttribs);
        if (context.Value == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create EGL context");

        unsafe
        {
            surface = PInvokeEgl.CreateWindowSurface(display, config, IntPtr.Zero, (int*)0);
        }
        if (surface.Value == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create EGL surface");

        if (!PInvokeEgl.MakeCurrent(display, surface, surface, context))
            throw new InvalidOperationException("Failed to make context current");

        GLLoader.LoadBindings(new BindingsContext());
    }

    public void SwapBuffers()
    {
        if (!PInvokeEgl.SwapBuffers(display, surface))
            throw new InvalidOperationException("Failed to swap the buffers of the EGL surface");
    }

    public void Dispose()
    {
        if (display.Value != IntPtr.Zero)
        {
            PInvokeEgl.MakeCurrent(display, EGLSurface.NoSurface, EGLSurface.NoSurface, EGLContext.NoContext);

            if (surface.Value != IntPtr.Zero)
            {
                PInvokeEgl.DestroySurface(display, surface);
                surface = EGLSurface.NoSurface;
            }

            if (context.Value != IntPtr.Zero)
                PInvokeEgl.DestroyContext(display, context);

            PInvokeEgl.Terminate(display);
        }
    }
}