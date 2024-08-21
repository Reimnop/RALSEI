using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Ralsei.Core.Text;
using Ralsei.Data;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Ralsei.Util;
using Tmpx.Common;

namespace Ralsei.Rendering.Common;

public class RenderingFactory(ILogger<RenderingFactory> logger) : IRenderingFactory
{
    // Resource requests and frames share one queue so that they run in the order they were made in.
    // This is what keeps a destroyed resource around for the frames that were submitted before it was destroyed.
    private readonly record struct WorkItem(Action? Request, FrameBuffer? Frame);

    private const int RenderAheadLimit = 3;

    public event EventHandler? FontBuffersUpdated;

    // Everything below is only touched on the render thread
    public ObservableSparseSet<Mesh> Meshes { get; } = new();
    public ObservableSparseSet<Texture> Textures { get; } = new();
    public ObservableSparseSet<Text> Texts { get; } = new();

    // Global font buffers
    public QuadraticCurve[] FontCurves { get; private set; } = [];
    public int[] FontCurveIndices { get; private set; } = [];
    public BandEntry[] FontBandEntries { get; private set; } = [];
    public ShapeEntry[] FontShapeEntries { get; private set; } = [];

    public int FreeFrameCount => framePool.FreeCount;
    public int QueuedFrameCount => queuedFrameCount;

    private readonly ConcurrentQueue<WorkItem> workQueue = [];
    private readonly ConcurrentMemoryPool<FrameBuffer> framePool = new(RenderAheadLimit, () => new FrameBuffer());

    // Raised when work is queued / a frame is given back, so that the threads on either end can sleep instead of spin
    private readonly ManualResetEventSlim workSignal = new(false);
    private readonly ManualResetEventSlim frameSignal = new(false);

    private int queuedFrameCount;

    #region Resources

    public Task<MeshHandle> CreateMeshAsync(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<int> indices)
    {
        var mesh = new Mesh(vertices.ToArray(), indices.ToArray());
        return EnqueueAsync(() => CreateMeshOnRenderThread(mesh));
    }

    public void DestroyMesh(MeshHandle handle)
        => Enqueue(() => DestroyMeshOnRenderThread(handle));

    public Task<TextureHandle> CreateTextureAsync(int width, int height, ReadOnlySpan<byte> rgba8PixelData, TextureSampling sampling)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A texture needs at least one pixel");
        if (rgba8PixelData.Length != width * height * 4)
            throw new ArgumentException($"A {width}x{height} texture needs {width * height * 4} bytes of pixel data, got {rgba8PixelData.Length}", nameof(rgba8PixelData));

        var texture = new Texture(width, height, rgba8PixelData.ToArray(), sampling);
        return EnqueueAsync(() => CreateTextureOnRenderThread(texture));
    }

    public void DestroyTexture(TextureHandle handle)
        => Enqueue(() => DestroyTextureOnRenderThread(handle));

    public Task SetFontBuffersAsync(
        ReadOnlySpan<QuadraticCurve> curves,
        ReadOnlySpan<int> curveIndices,
        ReadOnlySpan<BandEntry> bandEntries,
        ReadOnlySpan<ShapeEntry> shapeEntries)
    {
        var curvesCopy = curves.ToArray();
        var curveIndicesCopy = curveIndices.ToArray();
        var bandEntriesCopy = bandEntries.ToArray();
        var shapeEntriesCopy = shapeEntries.ToArray();
        return EnqueueAsync(() =>
        {
            FontCurves = curvesCopy;
            FontCurveIndices = curveIndicesCopy;
            FontBandEntries = bandEntriesCopy;
            FontShapeEntries = shapeEntriesCopy;
            FontBuffersUpdated?.Invoke(this, EventArgs.Empty);
        });
    }

    public Task<TextHandle> CreateTextAsync(ShapedRichText richText)
    {
        var renderGlyphs = richText.Glyphs.Select(x => new RenderGlyph
        {
            Transform = x.Transform,
            Color = x.Color,
            ShapeEntryIndex = x.ShapeEntryIndex,
        }).ToArray();
        var text = new Text(renderGlyphs);
        return EnqueueAsync(() => CreateTextOnRenderThread(text));
    }

    public void DestroyText(TextHandle handle)
        => Enqueue(() => DestroyTextOnRenderThread(handle));

    /// <summary>
    /// Creates a mesh right away. For the render thread's own use, e.g. a renderer that needs a mesh
    /// before it can draw anything and cannot wait for the queue.
    /// </summary>
    public MeshHandle CreateMeshOnRenderThread(Mesh mesh)
    {
        var id = Meshes.Insert(mesh);
        return new MeshHandle(id);
    }

    private TextureHandle CreateTextureOnRenderThread(Texture texture)
    {
        var id = Textures.Insert(texture);
        return new TextureHandle(id);
    }

    private void DestroyTextureOnRenderThread(TextureHandle handle)
    {
        if (handle.IsNone || !Textures.Remove(handle.Id, out _))
            throw new ArgumentException($"Invalid texture handle '{handle.Id}'", nameof(handle));
    }

    private void DestroyMeshOnRenderThread(MeshHandle handle)
    {
        if (!Meshes.Remove(handle.Id, out _))
            throw new ArgumentException($"Invalid mesh handle '{handle.Id}'", nameof(handle));
    }

    private TextHandle CreateTextOnRenderThread(Text text)
    {
        var id = Texts.Insert(text);
        return new TextHandle(id);
    }

    private void DestroyTextOnRenderThread(TextHandle handle)
    {
        if (!Texts.Remove(handle.Id, out _))
            throw new ArgumentException($"Invalid text handle '{handle.Id}'", nameof(handle));
    }

    #endregion

    #region Frames

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
        Interlocked.Increment(ref queuedFrameCount);
        Enqueue(new WorkItem(null, frame));
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
        if (!workQueue.IsEmpty)
            return true;

        // Reset, then look again: work queued before the reset must not be missed
        workSignal.Reset();
        if (!workQueue.IsEmpty)
            return true;

        workSignal.Wait(millisecondsTimeout);
        return !workQueue.IsEmpty;
    }

    public bool FlushFrame(IRenderer renderer)
    {
        while (workQueue.TryDequeue(out var work))
        {
            if (work.Frame is not { } frame)
            {
                RunRequest(work.Request!);
                continue;
            }

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

        return false;
    }

    #endregion

    private void Enqueue(Action request)
        => Enqueue(new WorkItem(request, null));

    private void Enqueue(WorkItem work)
    {
        workQueue.Enqueue(work);
        workSignal.Set();
    }

    private void RunRequest(Action request)
    {
        // Requests that return a task report errors through it, anything else must not take the render thread down
        try
        {
            request();
        }
        catch (Exception e)
        {
            logger.LogError(e, "A rendering request failed");
        }
    }

    private Task EnqueueAsync(Action request)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(() =>
        {
            try
            {
                request();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        }, null));
        return tcs.Task;
    }

    private Task<T> EnqueueAsync<T>(Func<T> request)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(() =>
        {
            try
            {
                tcs.SetResult(request());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        }, null));
        return tcs.Task;
    }
}
