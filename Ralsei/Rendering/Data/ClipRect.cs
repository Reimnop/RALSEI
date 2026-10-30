namespace Ralsei.Rendering.Data;

/// <summary>A rectangle in pixels of the pass's target (origin top-left) that a draw is cut to. <c>default</c> is no clip.</summary>
public readonly struct ClipRect : IEquatable<ClipRect>
{
    public ClipRect(int x, int y, int width, int height)
    {
        Enabled = true;
        X = x;
        Y = y;
        Width = Math.Max(width, 0);
        Height = Math.Max(height, 0);
    }

    /// <summary>Whether the draw is cut at all.</summary>
    public bool Enabled { get; }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>The part of this rectangle that is also in <paramref name="other"/> (a disabled one cuts nothing).</summary>
    public ClipRect Intersect(ClipRect other)
    {
        if (!Enabled)
            return other;
        if (!other.Enabled)
            return this;

        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return new ClipRect(left, top, right - left, bottom - top);
    }

    public bool Equals(ClipRect other)
        => Enabled == other.Enabled && (!Enabled || (X == other.X && Y == other.Y && Width == other.Width && Height == other.Height));

    public override bool Equals(object? obj) => obj is ClipRect other && Equals(other);

    public override int GetHashCode() => Enabled ? HashCode.Combine(X, Y, Width, Height) : 0;

    public static bool operator ==(ClipRect left, ClipRect right) => left.Equals(right);

    public static bool operator !=(ClipRect left, ClipRect right) => !left.Equals(right);

    public override string ToString() => Enabled ? $"({X}, {Y}, {Width}x{Height})" : "none";
}
