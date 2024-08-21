namespace Ralsei.Rendering.Common;

/// <summary>A draw command of a frame, with the depth the renderer has given it.</summary>
public struct DrawCommandWithDepth
{
    public DrawType DrawType;
    public int DrawId;
    public float Depth;
}
