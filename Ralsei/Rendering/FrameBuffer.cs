using Ralsei.Rendering.Data;
using Ralsei.Util;

namespace Ralsei.Rendering;

/// <summary>
/// One frame of draw data on its way to the render thread. Pooled by the rendering factory, which sizes it
/// through <see cref="Resize"/>; the application fills the passes (see <see cref="PassBuffer"/>) and submits it.
/// </summary>
/// <remarks>
/// A frame is made of one or more passes. They are drawn in order, each to a target of its own that is the same size as the
/// frame, with its own camera, clear color, draw commands and post-processing; then each is laid over the passes before it,
/// with the alpha of what it drew (so the first pass is the back, and a pass whose clear color is transparent only covers
/// what it draws).
/// </remarks>
public sealed class FrameBuffer : IDrawDataProvider, IResettable
{
    /// <summary>
    /// The post-processing of the frame as a whole: what is done to the layered result of the passes, after the post-processing of
    /// each pass. Null for none, as when a frame has only one pass, which has its own.
    /// </summary>
    public PostProcessingState? FinalPostProcessingState { get; set; }

    /// <summary>The passes of the frame, in the order they are drawn and layered.</summary>
    public ReadOnlySpan<PassBuffer> Passes => passes.AsSpan(0, passCount);

    // Passes are kept (with their arrays) when the frame goes back to the pool, so that the next frame with that many does not allocate
    private PassBuffer[] passes = [];
    private int passCount;

    /// <summary>
    /// Sets how many passes there are and how big each is. Existing contents are not preserved or cleared.
    /// </summary>
    public void Resize(ReadOnlySpan<PassSize> sizes)
    {
        if (sizes.IsEmpty)
            throw new ArgumentException("A frame needs at least one pass", nameof(sizes));

        if (sizes.Length > passes.Length)
        {
            var oldLength = passes.Length;
            Array.Resize(ref passes, Math.Max(sizes.Length, passes.Length * 2));
            for (var i = oldLength; i < passes.Length; i++)
            {
                passes[i] = new PassBuffer();
                passes[i].Reset();
            }
        }

        passCount = sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
            passes[i].Resize(sizes[i]);
    }

    public void Reset()
    {
        foreach (var pass in passes)
            pass.Reset();
        passCount = 0;
        FinalPostProcessingState = null;
    }

    int IDrawDataProvider.PassCount => passCount;

    DrawData IDrawDataProvider.CreateDrawData(int passIndex)
    {
        if ((uint) passIndex >= (uint) passCount)
            throw new ArgumentOutOfRangeException(nameof(passIndex));

        return passes[passIndex].CreateDrawData();
    }
}
