using Ralsei.Core.Data;
using Ralsei.Mathematics;

namespace Ralsei.Platform.OpenGL;

public interface IOpenGLSurface
{
    Vector2i RenderSize { get; }
    bool IsContextLost { get; }
    
    void Present(int texture, Vector2i size, ColorRgba clearColor);
}