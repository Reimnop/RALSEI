namespace Ralsei.Rendering;

/// <summary>The frames of one window on their way to its render thread: the main thread requests a buffer, fills and submits it, and the render thread draws them in order. The resources frames draw are the <see cref="IRenderingFactory"/>'s, which windows can share.</summary>
public interface IFrameQueue
{
    /// <summary>Number of frames that can be requested before the render thread has to catch up.</summary>
    int FreeFrameCount { get; }

    /// <summary>Number of submitted frames that the render thread has not picked up yet.</summary>
    int QueuedFrameCount { get; }

    /// <summary>Blocks until <see cref="FreeFrameCount"/> is above zero or the timeout runs out, so hosts needn't spin.</summary>
    /// <returns>Whether a frame can be requested.</returns>
    bool WaitForFreeFrame(int millisecondsTimeout);

    /// <summary>Render thread. Blocks until a frame is submitted or the timeout runs out, so render loops needn't spin.</summary>
    /// <returns>Whether there is a frame to draw.</returns>
    bool WaitForWork(int millisecondsTimeout);

    /// <summary>Requests a buffer for one frame, with a pass for each given size (at least one) and room for exactly that many elements. Contents start undefined; fill it and hand it over with <see cref="SubmitFrame"/>.</summary>
    FrameBuffer RequestFrameBuffer(ReadOnlySpan<PassSize> passes);

    /// <summary>Hands a filled buffer to the render thread, stamped with the current resource version. Don't touch it afterwards.</summary>
    /// <exception cref="InvalidOperationException">A draw uses the output of a pass that is not before its own (see <see cref="Handle.TextureHandle.FromPass"/>); the frame is dropped.</exception>
    void SubmitFrame(FrameBuffer frame);

    /// <summary>Render thread. Draws the next submitted frame with <paramref name="renderer"/>.</summary>
    /// <returns>Whether a frame was drawn.</returns>
    bool FlushFrame(IRenderer renderer);
}
