using System.Numerics;
using Ralsei.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Core.Graphics;

/// <summary>A GPU texture page that many small images have been packed into.</summary>
public sealed class TextureAtlas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>The page's texture, or <see cref="TextureHandle.None"/> until the page is built.</summary>
    public TextureHandle Texture { get; set; }

    /// <summary>The handle of the page's texture, if it has one.</summary>
    public bool TryGetTexture(out TextureHandle texture)
    {
        texture = Texture;
        return !texture.IsNone;
    }
}

/// <summary>A rectangle inside a <see cref="TextureAtlas"/>, in both pixel and UV space.</summary>
public readonly record struct AtlasRegion(TextureAtlas Atlas, int X, int Y, int Width, int Height)
{
    public Vector2 MinUV => new(X / (float)Atlas.Width, Y / (float)Atlas.Height);
    public Vector2 MaxUV => new((X + Width) / (float)Atlas.Width, (Y + Height) / (float)Atlas.Height);

    /// <summary>A sub-rectangle given relative to this region's top-left corner.</summary>
    public AtlasRegion Sub(int x, int y, int width, int height)
        => new(Atlas, X + x, Y + y, width, height);
}
