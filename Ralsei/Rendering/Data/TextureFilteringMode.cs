namespace Ralsei.Rendering.Data;

public enum TextureFilteringMode
{
    Nearest,
    Linear,

    /// <summary>Pixel art scaled without blur: sampled linearly, but blended only across one screen pixel at texel edges, so texels stay crisp blocks with smooth edges. Needs padded neighbours (the atlas builder does).</summary>
    CrispEdges
}
