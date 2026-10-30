using Ralsei.Mathematics;

namespace Ralsei.Platform.OpenGL;

/// <summary>Where an OpenGL or OpenGL ES renderer puts its frames: a host framebuffer in the host's context. The renderer blits the frame into <see cref="Framebuffer"/> (centered if smaller) and then asks the host to <see cref="SwapBuffers"/>.</summary>
public interface IOpenGLSurface
{
    /// <summary>The framebuffer the frames are blitted into: 0 for the default one of the window, or one the host made.</summary>
    int Framebuffer { get; }

    /// <summary>The size of <see cref="Framebuffer"/> in pixels.</summary>
    Vector2i FramebufferSize { get; }

    /// <summary>The size in pixels the renderer draws a frame at (a part of the framebuffer, or the size of it).</summary>
    Vector2i RenderSize { get; }

    bool IsContextLost { get; }

    /// <summary>Makes the context current on the drawing thread, asked at creation and before every frame. Nothing to do for a single-thread context. False if the context is gone.</summary>
    bool TryMakeCurrent() => true;

    /// <summary>The frame is in <see cref="Framebuffer"/>: shows it. Nothing to do if something else shows the framebuffer.</summary>
    void SwapBuffers()
    {
    }
}
