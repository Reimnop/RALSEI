using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Rendering.Vulkan;

public class BuiltinRenderingPrimitive : IDisposable
{
    public MeshHandle GlyphMeshHandle { get; }

    private readonly IRenderingFactory renderingFactory;

    // The renderer creates this on the render thread, where waiting for the factory's queue would never finish
    public BuiltinRenderingPrimitive(IRenderingFactory renderingFactory)
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

        GlyphMeshHandle = ((RenderingFactory) renderingFactory).CreateMeshOnRenderThread(new Mesh(vertices, indices));
    }

    public void Dispose()
    {
        renderingFactory.DestroyMesh(GlyphMeshHandle);
    }
}
