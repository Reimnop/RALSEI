using Microsoft.Extensions.Logging;
using Ralsei.Rendering;
using Ralsei.Rendering.Handle;

namespace Ralsei.Core.Service;

public class MeshService : IDisposable
{
    private readonly IRenderingFactory renderingFactory;
    private readonly List<List<Task<MeshHandle>>> meshes = [];

    public MeshService(IRenderingFactory renderingFactory, ILogger<MeshService> logger)
    {
        this.renderingFactory = renderingFactory;
        
        logger.LogInformation("Registering meshes");
        
        meshes.Add([
            renderingFactory.CreateMeshAsync(BuiltinMeshes.SquareFilled.Vertices, BuiltinMeshes.SquareFilled.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.SquareOutline.Vertices, BuiltinMeshes.SquareOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.SquareOutlineThin.Vertices, BuiltinMeshes.SquareOutlineThin.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleFilled.Vertices, BuiltinMeshes.CircleFilled.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleOutline.Vertices, BuiltinMeshes.CircleOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleHalf.Vertices, BuiltinMeshes.CircleHalf.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleHalfOutline.Vertices, BuiltinMeshes.CircleHalfOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleOutlineThin.Vertices, BuiltinMeshes.CircleOutlineThin.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleQuarter.Vertices, BuiltinMeshes.CircleQuarter.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleQuarterOutline.Vertices, BuiltinMeshes.CircleQuarterOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleHalfQuarter.Vertices, BuiltinMeshes.CircleHalfQuarter.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.CircleHalfQuarterOutline.Vertices, BuiltinMeshes.CircleHalfQuarterOutline.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleFilled.Vertices, BuiltinMeshes.TriangleFilled.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleOutline.Vertices, BuiltinMeshes.TriangleOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleRightFilled.Vertices, BuiltinMeshes.TriangleRightFilled.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleRightOutline.Vertices, BuiltinMeshes.TriangleRightOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleFilledBottomOrigin.Vertices, BuiltinMeshes.TriangleFilledBottomOrigin.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.TriangleOutlineBottomOrigin.Vertices, BuiltinMeshes.TriangleOutlineBottomOrigin.Indices),
        ]);

        meshes.Add([
            renderingFactory.CreateMeshAsync(BuiltinMeshes.Arrow.Vertices, BuiltinMeshes.Arrow.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.ArrowHead.Vertices, BuiltinMeshes.ArrowHead.Indices),
        ]);

        meshes.Add([]);

        meshes.Add([
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonFilled.Vertices, BuiltinMeshes.HexagonFilled.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonOutline.Vertices, BuiltinMeshes.HexagonOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonOutlineThin.Vertices, BuiltinMeshes.HexagonOutlineThin.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonHalf.Vertices, BuiltinMeshes.HexagonHalf.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonHalfOutline.Vertices, BuiltinMeshes.HexagonHalfOutline.Indices),
            renderingFactory.CreateMeshAsync(BuiltinMeshes.HexagonHalfOutlineThin.Vertices, BuiltinMeshes.HexagonHalfOutlineThin.Indices),
        ]);
    }

    public bool TryGetMeshForShape(int shape, int shapeOption, out MeshHandle mesh)
    {
        if (shape < meshes.Count && shapeOption < meshes[shape].Count)
            // Not available until the render thread has created the mesh
            return meshes[shape][shapeOption].TryGetHandle(out mesh);

        mesh = default;
        return false;
    }

    public void Dispose()
    {
        foreach (var meshList in meshes)
        {
            foreach (var mesh in meshList)
            {
                renderingFactory.DestroyMesh(mesh);
            }
        }
    }
}