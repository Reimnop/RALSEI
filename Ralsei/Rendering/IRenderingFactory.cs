using System.Numerics;
using Ralsei.Core.Text;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Tmpx.Common;

namespace Ralsei.Rendering;

/// <summary>
/// The application's single entry point to the render thread. Everything here is safe to call from any thread
/// and never blocks on the render thread: work is queued, and the render thread executes it, in order, when it
/// calls <see cref="FlushFrame"/>.
/// </summary>
public interface IRenderingFactory
{
    /// <summary>
    /// Number of frames that can be requested before the render thread has to catch up.
    /// </summary>
    int FreeFrameCount { get; }

    /// <summary>
    /// Number of submitted frames that the render thread has not picked up yet.
    /// </summary>
    int QueuedFrameCount { get; }

    /// <summary>
    /// Blocks until <see cref="FreeFrameCount"/> is above zero, or the timeout runs out, for hosts that
    /// would otherwise spin while waiting for the render thread.
    /// </summary>
    /// <returns>Whether a frame can be requested.</returns>
    bool WaitForFreeFrame(int millisecondsTimeout);

    /// <summary>
    /// Called on the render thread. Blocks until <see cref="FlushFrame"/> has something to do (a queued
    /// resource request or frame), or the timeout runs out, for render loops that would otherwise spin.
    /// </summary>
    /// <returns>Whether there is something to flush.</returns>
    bool WaitForWork(int millisecondsTimeout);

    #region Resources

    /// <summary>
    /// Queues the creation of a mesh. The data is copied, so the spans only need to be valid for this call.
    /// The task completes on the render thread with a handle that can be drawn right away.
    /// </summary>
    Task<MeshHandle> CreateMeshAsync(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<int> indices);

    /// <summary>
    /// Queues the destruction of a mesh. Frames submitted before this call can still draw it.
    /// </summary>
    void DestroyMesh(MeshHandle handle);

    /// <summary>
    /// Queues the creation of a texture. The data is copied, so the span only needs to be valid for this call.
    /// The task completes on the render thread with a handle that can be drawn right away.
    /// </summary>
    /// <param name="rgba8PixelData">
    /// The pixels, first row first (which is at a texture coordinate v of 0), with the colour of each already multiplied by its
    /// alpha (premultiplied alpha): the shaders blend with <c>One, OneMinusSrcAlpha</c>.
    /// </param>
    /// <param name="sampling">How the texture is read. It belongs to the texture; draws name the texture and nothing else.</param>
    Task<TextureHandle> CreateTextureAsync(int width, int height, ReadOnlySpan<byte> rgba8PixelData, TextureSampling sampling);

    /// <summary>
    /// Queues the destruction of a texture. Frames submitted before this call can still draw it.
    /// </summary>
    void DestroyTexture(TextureHandle handle);

    /// <summary>
    /// Queues a replacement of the global font buffers. The data is copied, so the spans only need to be valid for this call.
    /// </summary>
    Task SetFontBuffersAsync(
        ReadOnlySpan<QuadraticCurve> curves,
        ReadOnlySpan<int> curveIndices,
        ReadOnlySpan<BandEntry> bandEntries,
        ReadOnlySpan<ShapeEntry> shapeEntries);

    /// <summary>
    /// Queues the creation of a text. The glyphs are copied, so <paramref name="richText"/> can be changed afterwards.
    /// The task completes on the render thread with a handle that can be drawn right away.
    /// </summary>
    Task<TextHandle> CreateTextAsync(ShapedRichText richText);

    /// <summary>
    /// Queues the destruction of a text. Frames submitted before this call can still draw it.
    /// </summary>
    void DestroyText(TextHandle handle);

    #endregion

    #region Frames

    /// <summary>
    /// Requests a buffer for one frame of draw data, with a pass for each of the given sizes (at least one),
    /// with room for exactly the given number of elements.
    /// Fill it (the contents start out undefined), then hand it over with <see cref="SubmitFrame"/>.
    /// </summary>
    FrameBuffer RequestFrameBuffer(ReadOnlySpan<PassSize> passes);

    /// <summary>
    /// Hands a filled buffer over to the render thread. The buffer must not be touched afterwards.
    /// </summary>
    void SubmitFrame(FrameBuffer frame);

    #endregion

    /// <summary>
    /// Called on the render thread. Executes the queued resource requests, in order, up to and including the next
    /// submitted frame, which is drawn with <paramref name="renderer"/>.
    /// </summary>
    /// <returns>Whether a frame was drawn. Resource requests are executed either way.</returns>
    bool FlushFrame(IRenderer renderer);
}
