using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ralsei.Mathematics;
using Ralsei.Platform.Vulkan;

namespace Ralsei.Desktop.FFmpeg;

/// <summary>
/// Renders without a window, and gives FFmpeg the pixels of the last frame.
/// </summary>
public sealed unsafe class VulkanFFmpegSurface(DesktopSurfaceSettings surfaceSettings) : IVulkanSurface, IVulkanFrameReceiver, IFrameSource
{
    public Vector2i RenderSize => surfaceSettings.Size;
    public Vector2i FramebufferSize => surfaceSettings.Size;
    public bool VSync => false;
    public bool IsHeadless => true;

    // Points straight at the renderer's readback memory instead of copying out of it, which takes about as long as
    // rendering a lightweight frame does. The renderer keeps the memory around until it renders the next frame.
    public ReadOnlySpan<byte> FrameData => new((void*) frameAddress, frameLength);

    private nint frameAddress;
    private int frameLength;

    public IReadOnlyList<string> GetRequiredInstanceExtensions() => [];

    public ulong CreateSurface(ulong instance)
        => throw new NotSupportedException("A headless surface has nothing to present to");

    public void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)
    {
        frameAddress = (nint) Unsafe.AsPointer(ref MemoryMarshal.GetReference(rgba));
        frameLength = rgba.Length;
    }
}
