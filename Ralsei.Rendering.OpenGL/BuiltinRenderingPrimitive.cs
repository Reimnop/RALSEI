using System.Numerics;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Rendering.OpenGL;

internal class BuiltinRenderingPrimitive : IDisposable
{
    public MeshHandle GlyphMeshHandle { get; }

    private readonly RenderingFactory renderingFactory;
    private bool disposed;
    
    // The renderer creates this on the render thread, where waiting for the factory's queue would never finish
    public BuiltinRenderingPrimitive(RenderingFactory renderingFactory)
    {
        this.renderingFactory = renderingFactory;
        
        Vertex[] vertices =
        [
            new(0f, 1f),
            new(1f, 1f),
            new(0f, 0f),
            new(1f, 0f)
        ];
            
        int[] indices =
        [
            0, 1, 2,
            3, 2, 1
        ];
            
        GlyphMeshHandle = renderingFactory.CreateMesh(vertices, indices);
    }

    public void Dispose()
    {
        // The container and the renderer may both dispose it, and a mesh can only be destroyed once
        if (disposed)
            return;
        disposed = true;

        renderingFactory.DestroyMesh(GlyphMeshHandle);
    }
}