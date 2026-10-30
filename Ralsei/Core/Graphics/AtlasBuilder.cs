using Ralsei.Rendering;
using Ralsei.Rendering.Data;

namespace Ralsei.Core.Graphics;

/// <summary>An image queued for packing. <see cref="Region"/> becomes valid after <see cref="AtlasBuilder.Build"/>.</summary>
public sealed class AtlasEntry
{
    internal AtlasEntry(int width, int height, byte[] rgba, bool extrudeEdges)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        ExtrudeEdges = extrudeEdges;
    }

    public int Width { get; }
    public int Height { get; }
    internal byte[] Rgba { get; }
    internal bool ExtrudeEdges { get; }

    private AtlasRegion? region;

    public AtlasRegion Region
        => region ?? throw new InvalidOperationException("The atlas has not been built yet.");

    internal void Resolve(AtlasRegion resolved) => region = resolved;
}

/// <summary>Packs RGBA images into texture pages with a shelf packer. The renderer binds few textures per frame, so game code packs its art up front.</summary>
public sealed class AtlasBuilder(IRenderingFactory renderingFactory, int pageSize = 2048)
{
    private const int Padding = 1;
    private const int BytesPerPixel = 4;

    private readonly List<AtlasEntry> entries = [];

    /// <param name="extrudeEdges">Copies the outermost pixels into the padding, so images laid edge to edge don't show a seam when sampling a hair outside (crisp-edges sampling reads half a texel outside, so every image is extruded anyway).</param>
    public AtlasEntry Add(int width, int height, byte[] rgba, bool extrudeEdges = false)
    {
        if (width + Padding * 2 > pageSize || height + Padding * 2 > pageSize)
            throw new ArgumentException($"A {width}x{height} image does not fit into a {pageSize}px atlas page.");
        if (rgba.Length != width * height * BytesPerPixel)
            throw new ArgumentException("Pixel data does not match the image size.", nameof(rgba));

        var entry = new AtlasEntry(width, height, rgba, extrudeEdges);
        entries.Add(entry);
        return entry;
    }

    /// <summary>Multiplies each pixel's colour by its alpha. Pages are premultiplied so texels blend with neighbours without transparent pixels leaking colour.</summary>
    private static void PremultiplyRow(byte[] pixels, int offset, int width)
    {
        for (var i = 0; i < width; i++)
        {
            var p = offset + i * BytesPerPixel;
            var alpha = pixels[p + 3];
            if (alpha == 255)
                continue;

            pixels[p] = (byte)((pixels[p] * alpha + 127) / 255);
            pixels[p + 1] = (byte)((pixels[p + 1] * alpha + 127) / 255);
            pixels[p + 2] = (byte)((pixels[p + 2] * alpha + 127) / 255);
        }
    }

    /// <summary>Fills the one-pixel border around an image on the page with copies of the image's own edge pixels.</summary>
    private void Extrude(AtlasEntry entry, byte[] pixels, int x, int y)
    {
        void Copy(int fromX, int fromY, int toX, int toY)
            => Buffer.BlockCopy(pixels, ((fromY * pageSize) + fromX) * BytesPerPixel, pixels, ((toY * pageSize) + toX) * BytesPerPixel, BytesPerPixel);

        var right = x + entry.Width - 1;
        var bottom = y + entry.Height - 1;

        for (var column = x; column <= right; column++)
        {
            Copy(column, y, column, y - 1);
            Copy(column, bottom, column, bottom + 1);
        }

        for (var row = y - 1; row <= bottom + 1; row++)
        {
            var source = Math.Clamp(row, y, bottom);
            Copy(x, source, x - 1, row);
            Copy(right, source, right + 1, row);
        }
    }

    public IReadOnlyList<TextureAtlas> Build()
    {
        var pages = new List<TextureAtlas>();
        var pagePixels = new List<byte[]>();

        TextureAtlas? page = null;
        byte[]? pixels = null;
        int cursorX = 0, cursorY = 0, shelfHeight = 0;

        void StartPage()
        {
            page = new TextureAtlas(pageSize, pageSize);
            pixels = new byte[pageSize * pageSize * BytesPerPixel];
            pages.Add(page);
            pagePixels.Add(pixels);
            cursorX = cursorY = shelfHeight = 0;
        }

        // Tall images first keeps shelves tight.
        foreach (var entry in entries.OrderByDescending(x => x.Height).ThenByDescending(x => x.Width))
        {
            var slotW = entry.Width + Padding * 2;
            var slotH = entry.Height + Padding * 2;

            if (page is null)
                StartPage();

            if (cursorX + slotW > pageSize)
            {
                cursorX = 0;
                cursorY += shelfHeight;
                shelfHeight = 0;
            }

            if (cursorY + slotH > pageSize)
                StartPage();

            var x = cursorX + Padding;
            var y = cursorY + Padding;

            for (var row = 0; row < entry.Height; row++)
            {
                Buffer.BlockCopy(
                    entry.Rgba, row * entry.Width * BytesPerPixel,
                    pixels!, ((y + row) * pageSize + x) * BytesPerPixel,
                    entry.Width * BytesPerPixel);

                PremultiplyRow(pixels!, ((y + row) * pageSize + x) * BytesPerPixel, entry.Width);
            }

            Extrude(entry, pixels!, x, y);

            entry.Resolve(new AtlasRegion(page!, x, y, entry.Width, entry.Height));

            cursorX += slotW;
            shelfHeight = Math.Max(shelfHeight, slotH);
        }

        for (var i = 0; i < pages.Count; i++)
        {
            var atlas = pages[i];
            var data = pagePixels[i];
            atlas.Texture = renderingFactory.CreateTexture(atlas.Width, atlas.Height, data, TextureSampling.CrispEdges);
        }

        entries.Clear();
        return pages;
    }
}
