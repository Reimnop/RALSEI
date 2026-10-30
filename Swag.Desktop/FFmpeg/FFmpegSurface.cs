using Ralsei.Mathematics;
using Ralsei.Platform;

namespace Swag.Desktop.FFmpeg;

public class FFmpegSurface(DesktopSurfaceSettings surfaceSettings, GlfwService glfw) : DesktopSurface(surfaceSettings, glfw), IFrameReceiver, IFrameSource
{
    public ReadOnlySpan<byte> FrameData => frameData;

    private readonly byte[] frameData = new byte[surfaceSettings.Size.X * surfaceSettings.Size.Y * 4]; // RGBA8

    public void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)
    {
        if (rgba.Length != frameData.Length)
            throw new InvalidOperationException($"The frame is {size.X} × {size.Y}, not the {surfaceSettings.Size.X} × {surfaceSettings.Size.Y} of the video");

        rgba.CopyTo(frameData);
    }
}
