using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

public class Texture(int width, int height, byte[] rgba8PixelData, TextureSampling sampling)
{
    public int Width => width;
    public int Height => height;
    public byte[] Rgba8PixelData => rgba8PixelData;
    public TextureSampling Sampling => sampling;
}
