using System.Collections.Concurrent;
using Ralsei.Util;

namespace Ralsei.Rendering.Common;

/// <summary>The frames of one window on their way to its render thread, each stamped with the <see cref="IRenderingFactory.Version"/> of the resources when submitted.</summary>
public sealed class FrameQueue(IRenderingFactory factory) : IFrameQueue
{
    // How many frames the main thread may have given the render thread that are not shown yet. Every one of them is time between a key
    // and its picture; two (one being drawn while the next is made) is the least that keeps both threads busy. RALSEI_RENDER_AHEAD changes it.
    private static readonly int RenderAheadLimit = int.TryParse(Environment.GetEnvironmentVariable("RALSEI_RENDER_AHEAD"), out var ahead) && ahead > 0 ? ahead : 2;

    private readonly ConcurrentQueue<FrameBuffer> queue = [];
    private readonly ConcurrentMemoryPool<FrameBuffer> framePool = new(RenderAheadLimit, () => new FrameBuffer());

    // Raised when a frame is submitted / given back, so that the threads on either end can sleep instead of spin
    private readonly ManualResetEventSlim workSignal = new(false);
    private readonly ManualResetEventSlim frameSignal = new(false);

    private int queuedFrameCount;

    public int FreeFrameCount => framePool.FreeCount;
    public int QueuedFrameCount => queuedFrameCount;

    public FrameBuffer RequestFrameBuffer(ReadOnlySpan<PassSize> passes)
    {
        // The render thread is more than RenderAheadLimit frames behind, which the hosts avoid by waiting
        // for FreeFrameCount. Make a new one rather than stall in case one doesn't.
        if (!framePool.TryRent(out var frame))
            frame = new FrameBuffer();

        frame.Resize(passes);
        return frame;
    }

    public void SubmitFrame(FrameBuffer frame)
    {
        // A mistake of the frame is the caller's to hear about, not the render thread's
        try
        {
            RenderUtil.ValidatePassTextures(frame);
        }
        catch
        {
            framePool.Return(frame);
            throw;
        }

        frame.ResourceVersion = factory.Version;
        Interlocked.Increment(ref queuedFrameCount);
        queue.Enqueue(frame);
        workSignal.Set();
    }

    public bool WaitForFreeFrame(int millisecondsTimeout)
    {
        if (FreeFrameCount > 0)
            return true;

        // Reset, then look again: a frame given back before the reset must not be missed
        frameSignal.Reset();
        if (FreeFrameCount > 0)
            return true;

        frameSignal.Wait(millisecondsTimeout);
        return FreeFrameCount > 0;
    }

    public bool WaitForWork(int millisecondsTimeout)
    {
        if (!queue.IsEmpty)
            return true;

        // Reset, then look again: a frame submitted before the reset must not be missed
        workSignal.Reset();
        if (!queue.IsEmpty)
            return true;

        workSignal.Wait(millisecondsTimeout);
        return !queue.IsEmpty;
    }

    public bool FlushFrame(IRenderer renderer)
    {
        if (!queue.TryDequeue(out var frame))
            return false;

        Interlocked.Decrement(ref queuedFrameCount);
        try
        {
            renderer.ProcessFrame(frame);
        }
        finally
        {
            framePool.Return(frame);
            frameSignal.Set();
        }

        return true;
    }
}
