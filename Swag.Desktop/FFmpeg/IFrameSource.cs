using Ralsei.Mathematics;

namespace Swag.Desktop.FFmpeg;

/// <summary>Gives access to the last frame that was rendered, as tightly packed RGBA8 with the top row first.</summary>
public interface IFrameSource
{
    Vector2i RenderSize { get; }
    ReadOnlySpan<byte> FrameData { get; }
}
