using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

public class Mesh(Vertex[] vertices, int[] indices)
{
    public Vertex[] Vertices => vertices;
    public int[] Indices => indices;
}