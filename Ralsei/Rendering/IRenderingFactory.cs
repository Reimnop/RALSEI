using Ralsei.Core.Text;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Tmpx.Common;

namespace Ralsei.Rendering;

/// <summary>Makes the resources frames draw: meshes, textures, texts and font buffers. Safe from any thread, never waits for a render thread, and has no request queue: a handle is given at once and any later-submitted frame can draw it. Every renderer mirrors the resources in its own context, so one factory serves many windows (each with its own <see cref="IFrameQueue"/>).</summary>
public interface IRenderingFactory
{
    /// <summary>The number of the latest change. A frame is stamped with it on submit and drawn with the changes up to it, so a resource destroyed after submission is still there for that frame.</summary>
    long Version { get; }

    /// <summary>Makes a mesh. The data is copied, so the spans only need to be valid for this call.</summary>
    MeshHandle CreateMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<int> indices);

    /// <summary>Destroys a mesh. Frames submitted before this call can still draw it.</summary>
    void DestroyMesh(MeshHandle handle);

    /// <summary>Makes a texture. The data is copied, so the span only needs to be valid for this call.</summary>
    /// <param name="rgba8PixelData">The pixels, first row first (v = 0), premultiplied by alpha: shaders blend with <c>One, OneMinusSrcAlpha</c>.</param>
    /// <param name="sampling">How the texture is read. It belongs to the texture; draws name the texture and nothing else.</param>
    TextureHandle CreateTexture(int width, int height, ReadOnlySpan<byte> rgba8PixelData, TextureSampling sampling);

    /// <summary>Destroys a texture. Frames submitted before this call can still draw it.</summary>
    void DestroyTexture(TextureHandle handle);

    /// <summary>Replaces the global font buffers. The data is copied, so the spans only need to be valid for this call.</summary>
    void SetFontBuffers(
        ReadOnlySpan<QuadraticCurve> curves,
        ReadOnlySpan<int> curveIndices,
        ReadOnlySpan<BandEntry> bandEntries,
        ReadOnlySpan<ShapeEntry> shapeEntries);

    /// <summary>Makes a text. The glyphs are copied, so <paramref name="richText"/> can be changed afterwards.</summary>
    TextHandle CreateText(ShapedRichText richText);

    /// <summary>Destroys a text. Frames submitted before this call can still draw it.</summary>
    void DestroyText(TextHandle handle);
}
