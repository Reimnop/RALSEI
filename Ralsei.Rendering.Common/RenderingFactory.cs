using Microsoft.Extensions.Logging;
using Ralsei.Core.Text;
using Ralsei.Data;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using Tmpx.Common;

namespace Ralsei.Rendering.Common;

/// <summary>The resources frames draw, kept as data with no queue of requests: a creation or destruction takes effect at once, is numbered (<see cref="Version"/>) and posted to every renderer's <see cref="ResourceMirror"/>, which makes it in its own context when it draws a frame submitted after it. So one factory can serve any number of windows.</summary>
public class RenderingFactory(ILogger<RenderingFactory> logger) : IRenderingFactory
{
    private readonly Lock gate = new();
    private readonly SparseSet<Mesh> meshes = new();
    private readonly SparseSet<Texture> textures = new();
    private readonly SparseSet<Text> texts = new();
    private FontBuffers fontBuffers = FontBuffers.Empty;

    // Replaced, never changed
    private ResourceMirror[] mirrors = [];
    private long version;

    public long Version => Volatile.Read(ref version);

    /// <summary>Makes the mirror of a renderer: it gets everything that exists, and every change from now on.</summary>
    public ResourceMirror CreateMirror()
    {
        lock (gate)
        {
            var mirror = new ResourceMirror(this);

            // Version 0 is before every frame
            foreach (var (id, mesh) in meshes)
                mirror.Post(new ResourceMirror.Change(0, ResourceMirror.ChangeKind.MeshCreated, id, mesh));
            foreach (var (id, texture) in textures)
                mirror.Post(new ResourceMirror.Change(0, ResourceMirror.ChangeKind.TextureCreated, id, texture));
            foreach (var (id, text) in texts)
                mirror.Post(new ResourceMirror.Change(0, ResourceMirror.ChangeKind.TextCreated, id, text));
            if (!ReferenceEquals(fontBuffers, FontBuffers.Empty))
                mirror.Post(new ResourceMirror.Change(0, ResourceMirror.ChangeKind.FontBuffers, 0, fontBuffers));

            mirrors = [.. mirrors, mirror];
            return mirror;
        }
    }

    internal void Remove(ResourceMirror mirror)
    {
        lock (gate)
            mirrors = mirrors.Where(x => x != mirror).ToArray();
    }

    public MeshHandle CreateMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<int> indices)
    {
        var mesh = new Mesh(vertices.ToArray(), indices.ToArray());
        lock (gate)
        {
            var id = meshes.Insert(mesh);
            Publish(ResourceMirror.ChangeKind.MeshCreated, id, mesh);
            return new MeshHandle(id);
        }
    }

    public void DestroyMesh(MeshHandle handle)
    {
        lock (gate)
        {
            if (!meshes.Remove(handle.Id, out var mesh))
            {
                logger.LogError("Invalid mesh handle '{Id}'", handle.Id);
                return;
            }

            Publish(ResourceMirror.ChangeKind.MeshDestroyed, handle.Id, mesh);
        }
    }

    public TextureHandle CreateTexture(int width, int height, ReadOnlySpan<byte> rgba8PixelData, TextureSampling sampling)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A texture needs at least one pixel");
        if (rgba8PixelData.Length != width * height * 4)
            throw new ArgumentException($"A {width}x{height} texture needs {width * height * 4} bytes of pixel data, got {rgba8PixelData.Length}", nameof(rgba8PixelData));

        var texture = new Texture(width, height, rgba8PixelData.ToArray(), sampling);
        lock (gate)
        {
            var id = textures.Insert(texture);
            Publish(ResourceMirror.ChangeKind.TextureCreated, id, texture);
            return new TextureHandle(id);
        }
    }

    public void DestroyTexture(TextureHandle handle)
    {
        lock (gate)
        {
            if (handle.IsNone || handle.IsPass || !textures.Remove(handle.Id, out var texture))
            {
                logger.LogError("Invalid texture handle '{Id}'", handle.Id);
                return;
            }

            Publish(ResourceMirror.ChangeKind.TextureDestroyed, handle.Id, texture);
        }
    }

    public void SetFontBuffers(
        ReadOnlySpan<QuadraticCurve> curves,
        ReadOnlySpan<int> curveIndices,
        ReadOnlySpan<BandEntry> bandEntries,
        ReadOnlySpan<ShapeEntry> shapeEntries)
    {
        var buffers = new FontBuffers(curves.ToArray(), curveIndices.ToArray(), bandEntries.ToArray(), shapeEntries.ToArray());
        lock (gate)
        {
            fontBuffers = buffers;
            Publish(ResourceMirror.ChangeKind.FontBuffers, 0, buffers);
        }
    }

    public TextHandle CreateText(ShapedRichText richText)
    {
        var renderGlyphs = richText.Glyphs.Select(x => new RenderGlyph
        {
            Transform = x.Transform,
            Color = x.Color,
            ShapeEntryIndex = x.ShapeEntryIndex,
        }).ToArray();
        var text = new Text(renderGlyphs);
        lock (gate)
        {
            var id = texts.Insert(text);
            Publish(ResourceMirror.ChangeKind.TextCreated, id, text);
            return new TextHandle(id);
        }
    }

    public void DestroyText(TextHandle handle)
    {
        lock (gate)
        {
            if (!texts.Remove(handle.Id, out var text))
            {
                logger.LogError("Invalid text handle '{Id}'", handle.Id);
                return;
            }

            Publish(ResourceMirror.ChangeKind.TextDestroyed, handle.Id, text);
        }
    }

    // Under the lock, so that the mirrors get the changes in the order of their numbers, and a frame stamped with a number
    // is stamped after everything up to it was posted
    private void Publish(ResourceMirror.ChangeKind kind, int id, object item)
    {
        var next = version + 1;
        var change = new ResourceMirror.Change(next, kind, id, item);
        foreach (var mirror in mirrors)
            mirror.Post(change);

        Volatile.Write(ref version, next);
    }
}
