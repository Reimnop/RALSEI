using Ralsei.Core.Data;

namespace Ralsei.Rendering.Data;

/// <summary>
/// How a texture is read: the filter for when it is shrunk and for when it is magnified, and what is read outside it. It is a
/// property of the texture, given when it is created.
/// </summary>
public readonly record struct TextureSampling(
    TextureFilteringMode MinificationFilter,
    TextureFilteringMode MagnificationFilter,
    TextureAddressMode WrapModeU,
    TextureAddressMode WrapModeV,
    ColorRgba BorderColor = default)
{
    public static TextureSampling Nearest { get; } = Uniform(TextureFilteringMode.Nearest);
    public static TextureSampling Linear { get; } = Uniform(TextureFilteringMode.Linear);

    /// <summary>See <see cref="TextureFilteringMode.CrispEdges"/>.</summary>
    public static TextureSampling CrispEdges { get; } = Uniform(TextureFilteringMode.CrispEdges);

    /// <summary>Whether the shaders should blend only across the width of a screen pixel at the edges of texels.</summary>
    public bool IsCrisp => MinificationFilter == TextureFilteringMode.CrispEdges || MagnificationFilter == TextureFilteringMode.CrispEdges;

    private static TextureSampling Uniform(TextureFilteringMode filter)
        => new(filter, filter, TextureAddressMode.ClampToEdge, TextureAddressMode.ClampToEdge);
}
