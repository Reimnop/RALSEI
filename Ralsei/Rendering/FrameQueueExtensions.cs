namespace Ralsei.Rendering;

public static class FrameQueueExtensions
{
    /// <summary>Requests a buffer for a frame of a single pass.</summary>
    public static FrameBuffer RequestFrameBuffer(this IFrameQueue frames, int drawCommandCount, int meshDrawItemCount, int textDrawItemCount)
        => frames.RequestFrameBuffer([new PassSize(drawCommandCount, meshDrawItemCount, textDrawItemCount)]);
}
