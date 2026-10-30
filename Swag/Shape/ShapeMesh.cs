using System.Numerics;

namespace Swag.Shape;

/// <summary>Position-only triangle mesh in object-local units.</summary>
public class ShapeMesh(Vector2[] vertices, int[] indices)
{
    public Vector2[] Vertices => vertices;
    public int[] Indices => indices;
}
