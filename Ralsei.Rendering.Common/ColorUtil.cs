using Ralsei.Core.Data;

namespace Ralsei.Rendering.Common;

public static class ColorUtil
{
    public static float SrgbToLinear(float c) 
    {
        var linearRGBLo = c / 12.92f;
        var linearRGBHi = MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return c <= 0.04045f ? linearRGBLo : linearRGBHi;
    }

    public static float LinearToSrgb(float c) 
    {
        var srgbLo = c * 12.92f;
        var srgbHi = 1.055f * MathF.Pow(c, 1.0f / 2.4f) - 0.055f;
        return c <= 0.0031308f ? srgbLo : srgbHi;
    }
    
    public static ColorRgb LinearToSrgb(ColorRgb linearColor) 
    {
        return new ColorRgb(
            LinearToSrgb(linearColor.R),
            LinearToSrgb(linearColor.G),
            LinearToSrgb(linearColor.B)
        );
    }
    
    public static ColorRgb SrgbToLinear(ColorRgb srgbColor) 
    {
        return new ColorRgb(
            SrgbToLinear(srgbColor.R),
            SrgbToLinear(srgbColor.G),
            SrgbToLinear(srgbColor.B)
        );
    }
    
    public static ColorRgba LinearToSrgb(ColorRgba linearColor) 
    {
        return new ColorRgba(
            LinearToSrgb(linearColor.R),
            LinearToSrgb(linearColor.G),
            LinearToSrgb(linearColor.B),
            linearColor.A
        );
    }
    
    public static ColorRgba SrgbToLinear(ColorRgba srgbColor) 
    {
        return new ColorRgba(
            SrgbToLinear(srgbColor.R),
            SrgbToLinear(srgbColor.G),
            SrgbToLinear(srgbColor.B),
            srgbColor.A
        );
    }
}
