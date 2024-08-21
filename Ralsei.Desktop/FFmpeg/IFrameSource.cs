using Ralsei.Mathematics;

namespace Ralsei.Desktop.FFmpeg;

/// <summary>
/// Gives access to the last frame that was rendered, as tightly packed RGBA8 with the bottom row first.
/// </summary>
public interface IFrameSource
{
    Vector2i RenderSize { get; }
    ReadOnlySpan<byte> FrameData { get; }
}
