namespace Ralsei.Core.Graphics;

/// <summary>An inclusive pixel rectangle inside a sprite, used as its collision bounds.</summary>
public readonly record struct SpriteBounds(int Left, int Right, int Top, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
}

/// <summary>A named, possibly animated image with an origin and collision bounds.</summary>
public sealed class Sprite
{
    public Sprite(string name, int width, int height, int originX, int originY, IReadOnlyList<AtlasRegion> frames, SpriteBounds? bounds = null)
    {
        Name = name;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        Frames = frames;
        Bounds = bounds ?? new SpriteBounds(0, width - 1, 0, height - 1);
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public IReadOnlyList<AtlasRegion> Frames { get; }
    public SpriteBounds Bounds { get; }
    public int FrameCount => Frames.Count;

    /// <summary>Returns the frame for <paramref name="index"/>, wrapping around for animations.</summary>
    public AtlasRegion GetFrame(int index)
        => Frames[((index % Frames.Count) + Frames.Count) % Frames.Count];

    public override string ToString() => Name;
}
