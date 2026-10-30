using System.Diagnostics;
using System.Numerics;
using Ralsei.Mathematics;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering;

public static class RenderUtil
{
    public static void GetRenderSize(Vector2i size, float? aspectRatio, out Vector2i renderSize)
    {
        var newSize = size;
        if (aspectRatio.HasValue)
        {
            var targetAspectRatio = aspectRatio.Value;
            var screenAspectRatio = size.X / (float) size.Y;
            if (targetAspectRatio < screenAspectRatio)
            {
                newSize.X = (int)MathF.Ceiling(size.Y * targetAspectRatio);
            }
            else
            {
                newSize.Y = (int)MathF.Ceiling(size.X / targetAspectRatio);
            }
        }

        renderSize = new Vector2i(
            Math.Min(size.X, newSize.X),
            Math.Min(size.Y, newSize.Y));
    }
    
    /// <summary>The view matrix of a camera looking at a world through a target of <paramref name="size"/> pixels; its scale is half the height it sees.</summary>
    public static Matrix3x2 GetViewMatrix(in CameraState camera, Vector2i size)
    {
        if (camera.Scale == 0.0f)
            return new Matrix3x2();
        
        var aspectRatio = size.X / (float) size.Y;
        var inversionResult = Matrix3x2.Invert(
            Matrix3x2.CreateScale(Vector2.One * camera.Scale) *
            Matrix3x2.CreateRotation(camera.Rotation) *
            Matrix3x2.CreateTranslation(camera.Position), out var view);
        Debug.Assert(inversionResult);
        
        var projection = Matrix3x2.CreateScale(1.0f / aspectRatio, 1.0f);
        return view * projection;
    }
    
    /// <summary>Throws if a mesh draw uses the output of a pass that is not before its own. Marks passes whose output is used in <paramref name="used"/>, if given (one place per pass, not cleared).</summary>
    public static void ValidatePassTextures(IDrawDataProvider frame, Span<bool> used = default)
    {
        for (var passIndex = 0; passIndex < frame.PassCount; passIndex++)
        {
            var drawData = frame.CreateDrawData(passIndex);
            foreach (ref var drawCommand in drawData.DrawCommands)
            {
                if (drawCommand.DrawType != DrawType.Mesh)
                    continue;

                ref var meshDrawItem = ref drawData.MeshDrawItems[drawCommand.DrawId];
                if (meshDrawItem.RenderMode == RenderMode.Normal && meshDrawItem.Texture.IsPass && meshDrawItem.Texture.PassIndex >= passIndex)
                {
                    throw new InvalidOperationException(
                        $"A draw in pass {passIndex} uses the output of pass {meshDrawItem.Texture.PassIndex}, but a pass can only use the output of a pass before it");
                }

                if (!used.IsEmpty && meshDrawItem.RenderMode == RenderMode.Normal && meshDrawItem.Texture.IsPass)
                    used[meshDrawItem.Texture.PassIndex] = true;
            }
        }
    }

    public static bool ShouldUseTransparentDrawList(in DrawCommand drawCommand, in DrawData drawData)
    {
        if (drawCommand.DrawType == DrawType.Text)
            return true; // Text is always transparent
        
        ref var meshDrawItem = ref drawData.MeshDrawItems[drawCommand.DrawId];
        if (meshDrawItem.RenderMode == RenderMode.Normal)
        {
            if (!meshDrawItem.Texture.IsNone)
                return true; // Textures are always transparent, as their pixels can be

            return meshDrawItem.Color1.A < 1f; // We only use Color1 in normal render mode
        }

        // A ring is as see-through as its edges are soft; its second colour is not a colour
        if (meshDrawItem.RenderMode == RenderMode.Ring)
            return true;

        return meshDrawItem.Color1.A < 1f || meshDrawItem.Color2.A < 1f;
    }
}