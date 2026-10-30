using Ralsei.Mathematics;

namespace Ralsei.Platform;

/// <summary>Implemented by a renderer surface that wants the pixels of every rendered frame (to encode a video, save a picture). Renderers read frames back only for a surface that has this.</summary>
public interface IFrameReceiver
{
    /// <summary>Called on the render thread after a frame is rendered and before it is presented. The frame is tightly packed RGBA8, top row first, gamma encoded and premultiplied. It is renderer-owned memory, valid until the next frame starts.</summary>
    void ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size);
}
