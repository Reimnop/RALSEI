namespace Ralsei.Rendering.Data;

public enum RenderMode
{
    Normal,
    RightToLeftGradient,
    LeftToRightGradient,
    OutwardsGradient,
    InwardsGradient,

    /// <summary>A ring with soft edges over the mesh's unit square: <see cref="MeshDrawItem.Color1"/> is its colour and the red, green and blue of <see cref="MeshDrawItem.Color2"/> are numbers: inner radius, outer radius and edge fade, as fractions of the half-size.</summary>
    Ring
}
