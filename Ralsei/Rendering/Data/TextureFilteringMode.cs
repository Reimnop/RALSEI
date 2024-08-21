namespace Ralsei.Rendering.Data;

public enum TextureFilteringMode
{
    Nearest,
    Linear,

    /// <summary>
    /// Pixel art scaled without blurring it: the texture is sampled linearly, but the shaders only blend across the width of one screen pixel at
    /// the edge between two texels, so every texel stays a crisp block and its edges are smooth. Needs the texture's neighbours to be
    /// padded (the atlas builder does).
    /// </summary>
    CrispEdges
}
