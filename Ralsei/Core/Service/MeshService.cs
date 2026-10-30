using Microsoft.Extensions.Logging;
using Ralsei.Rendering;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Core.Service;

public class MeshService : IDisposable
{
    private readonly IRenderingFactory renderingFactory;
    private readonly List<List<MeshHandle>> meshes = [];

    /// <summary>The unit quad that sprites are drawn with.</summary>
    public MeshHandle SpriteQuadMesh { get; }

    public MeshService(IRenderingFactory renderingFactory, ILogger<MeshService> logger)
    {
        this.renderingFactory = renderingFactory;
        
        logger.LogInformation("Registering meshes");

        SpriteQuadMesh = renderingFactory.CreateMesh(
            [new Vertex(-0.5f, 0.5f, 0f, 0f), new Vertex(0.5f, 0.5f, 1f, 0f), new Vertex(-0.5f, -0.5f, 0f, 1f), new Vertex(0.5f, -0.5f, 1f, 1f)],
            [0, 1, 2, 3, 2, 1]);
        
        meshes.Add([
            renderingFactory.CreateMesh(BuiltinMeshes.SquareFilled.Vertices, BuiltinMeshes.SquareFilled.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.SquareOutline.Vertices, BuiltinMeshes.SquareOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.SquareOutlineThin.Vertices, BuiltinMeshes.SquareOutlineThin.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMesh(BuiltinMeshes.CircleFilled.Vertices, BuiltinMeshes.CircleFilled.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleOutline.Vertices, BuiltinMeshes.CircleOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleHalf.Vertices, BuiltinMeshes.CircleHalf.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleHalfOutline.Vertices, BuiltinMeshes.CircleHalfOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleOutlineThin.Vertices, BuiltinMeshes.CircleOutlineThin.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleQuarter.Vertices, BuiltinMeshes.CircleQuarter.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleQuarterOutline.Vertices, BuiltinMeshes.CircleQuarterOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleHalfQuarter.Vertices, BuiltinMeshes.CircleHalfQuarter.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.CircleHalfQuarterOutline.Vertices, BuiltinMeshes.CircleHalfQuarterOutline.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleFilled.Vertices, BuiltinMeshes.TriangleFilled.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleOutline.Vertices, BuiltinMeshes.TriangleOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleRightFilled.Vertices, BuiltinMeshes.TriangleRightFilled.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleRightOutline.Vertices, BuiltinMeshes.TriangleRightOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleFilledBottomOrigin.Vertices, BuiltinMeshes.TriangleFilledBottomOrigin.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.TriangleOutlineBottomOrigin.Vertices, BuiltinMeshes.TriangleOutlineBottomOrigin.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMesh(BuiltinMeshes.Arrow.Vertices, BuiltinMeshes.Arrow.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.ArrowHead.Vertices, BuiltinMeshes.ArrowHead.Indices),
        ]);

        meshes.Add([]);

        meshes.Add([
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonFilled.Vertices, BuiltinMeshes.HexagonFilled.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonOutline.Vertices, BuiltinMeshes.HexagonOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonOutlineThin.Vertices, BuiltinMeshes.HexagonOutlineThin.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonHalf.Vertices, BuiltinMeshes.HexagonHalf.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonHalfOutline.Vertices, BuiltinMeshes.HexagonHalfOutline.Indices),
            renderingFactory.CreateMesh(BuiltinMeshes.HexagonHalfOutlineThin.Vertices, BuiltinMeshes.HexagonHalfOutlineThin.Indices),
        ]);
    }

    public bool TryGetMeshForShape(int shape, int shapeOption, out MeshHandle mesh)
    {
        if (shape < meshes.Count && shapeOption < meshes[shape].Count)
        {
            mesh = meshes[shape][shapeOption];
            return true;
        }

        mesh = default;
        return false;
    }

    public void Dispose()
    {
        renderingFactory.DestroyMesh(SpriteQuadMesh);
        foreach (var meshList in meshes)
        {
            foreach (var mesh in meshList)
            {
                renderingFactory.DestroyMesh(mesh);
            }
        }
    }
}