using Tmpx.Common;

namespace Ralsei.Rendering.Common;

/// <summary>The global font buffers as of one change: the glyph outlines of every font, and where each glyph is in them.</summary>
public sealed record FontBuffers(QuadraticCurve[] Curves, int[] CurveIndices, BandEntry[] BandEntries, ShapeEntry[] ShapeEntries)
{
    public static FontBuffers Empty { get; } = new([], [], [], []);
}
