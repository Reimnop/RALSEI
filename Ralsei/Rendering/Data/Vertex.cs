using System.Numerics;
using System.Runtime.InteropServices;

namespace Ralsei.Rendering.Data;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector2 position, Vector2 uv)
{
    public Vector2 Position = position;
    public Vector2 UV = uv;
    
    public Vertex(Vector2 position) : this(position, position)
    {
    }
    
    public Vertex(float x, float y, float u, float v) : this(new Vector2(x, y), new Vector2(u, v))
    {
    }
    
    public Vertex(float x, float y) : this(x, y, x, y)
    {
    }
}
