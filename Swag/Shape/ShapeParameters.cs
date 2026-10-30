namespace Swag.Shape;

/// <summary>Authored description of a custom polygon shape.</summary>
/// <param name="sides">Side count, clamped to 3..32 on generation.</param>
/// <param name="roundness">Corner rounding, clamped to 0..1.</param>
/// <param name="thickness">Ring width as a fraction of the radius, clamped to 0..1 (1 is solid).</param>
/// <param name="sliceCount">Number of polygon sides kept; a negative value (or one covering all sides) keeps the whole shape.</param>
public class ShapeParameters(int sides, float roundness, float thickness, int sliceCount)
{
    public int Sides => sides;
    public float Roundness => roundness;
    public float Thickness => thickness;
    public int SliceCount => sliceCount;
}
