using Ralsei.Mathematics;
using Ralsei.Rendering;

namespace Swag.Android.Platform;

/// <summary>What the surfaces need to know about how to present, shared by the OpenGL ES and Vulkan ones.</summary>
public class AndroidSurfaceSettings
{
    private const float LockedAspectRatio = 16f / 9f;

    // Read by the render thread, changed from the UI
    public volatile bool LockAspectRatio = true;
    public bool VSync { get; set; } = true;

    /// <summary>The size to render at inside a window of the given size: all of it, or the largest 16:9 area that fits.</summary>
    public Vector2i GetRenderSize(Vector2i windowSize)
    {
        RenderUtil.GetRenderSize(windowSize, LockAspectRatio ? LockedAspectRatio : null, out var renderSize);
        return renderSize;
    }
}
