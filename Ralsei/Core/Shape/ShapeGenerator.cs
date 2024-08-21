using System.Numerics;

namespace Ralsei.Core.Shape;

/// <summary>
/// Builds triangle meshes for regular polygons with optional rounded corners, a hole (ring) and a wedge cut.
/// </summary>
public static class ShapeGenerator
{
    private const int MinSides = 3;
    private const int MaxSides = 32;

    // Beyond this side count the corners are never rounded.
    private const int MaxRoundedSides = 12;

    // Rounding smaller than this is invisible and would only produce sliver triangles.
    private const float MinTangentDistance = 0.005f;

    // An inner scale below this is treated as a solid shape.
    private const float MinInnerScale = 1e-4f;

    // Target turn between consecutive segments on a rounded corner, in degrees.
    private const float CurveStepDegrees = 8.0f;
    private const float CoarseCurveStepDegrees = 15.0f;
    private const float CoarseCurveTangentDistance = 0.03f;

    public static ShapeMesh Generate(ShapeParameters parameters)
    {
        var sides = Math.Clamp(parameters.Sides, MinSides, MaxSides);
        var roundness = Sanitize(parameters.Roundness, 0.0f);
        var thickness = Sanitize(parameters.Thickness, 1.0f);

        // 0 and negative values mean "no slicing"; anything covering all sides is whole as well.
        var sliceCount = parameters.SliceCount;
        var whole = sliceCount <= 0 || sliceCount >= sides;
        var sideCount = whole ? sides : sliceCount;

        if (thickness <= 0.0f)
            return CreateEmpty();

        var contour = BuildContour(sides, roundness, sideCount, whole);
        var innerScale = 1.0f - thickness;
        return innerScale < MinInnerScale
            ? TriangulateSolid(contour, whole)
            : TriangulateRing(contour, innerScale, whole);
    }

    private static float Sanitize(float value, float fallback)
    {
        return float.IsNaN(value) ? fallback : Math.Clamp(value, 0.0f, 1.0f);
    }

    private static float GetCircumradius(int sides)
    {
        return sides switch
        {
            3 => 0.575f,
            4 => MathF.Sqrt(0.5f),
            _ => 0.5f
        };
    }

    private static float GetCornerAngle(int sides, int index)
    {
        // Corner 0 is straight down for even side counts (other than 4), otherwise half a step to the right of it.
        var offset = sides % 2 == 0 && sides != 4 ? 0.0f : MathF.PI / sides;
        return -MathF.PI / 2.0f + offset + 2.0f * MathF.PI * index / sides;
    }

    private static Vector2 GetCorner(int sides, int index)
    {
        var angle = GetCornerAngle(sides, index);
        var radius = GetCircumradius(sides);
        return new Vector2(radius * MathF.Cos(angle), radius * MathF.Sin(angle));
    }

    private static float GetTangentDistance(int sides, float roundness)
    {
        if (sides > MaxRoundedSides || roundness <= 0.0f)
            return 0.0f;

        var fraction = 0.5f - 0.25f * (sides - 3) / 9.0f;
        var distance = roundness * fraction * GetCircumradius(sides);
        return distance < MinTangentDistance ? 0.0f : distance;
    }

    // Outer outline in counter-clockwise order starting at corner 0. Closed when whole, an open chain otherwise.
    private static List<Vector2> BuildContour(int sides, float roundness, int sideCount, bool whole)
    {
        var tangentDistance = GetTangentDistance(sides, roundness);
        var cornerCount = whole ? sides : sideCount + 1;
        var contour = new List<Vector2>();

        for (var i = 0; i < cornerCount; i++)
        {
            var corner = GetCorner(sides, i);

            // The ends of a cut stay sharp.
            var rounded = tangentDistance > 0.0 && (whole || (i > 0 && i < sideCount));
            if (!rounded)
            {
                contour.Add(corner);
                continue;
            }

            var previous = GetCorner(sides, i - 1);
            var next = GetCorner(sides, i + 1);
            AddRoundedCorner(contour, sides, corner, previous, next, tangentDistance);
        }

        return contour;
    }

    // Replaces a corner with a quadratic curve whose control point is the corner. The curve is sampled so that the
    // tangent direction turns by equal steps, which keeps the angle between consecutive segments uniform.
    private static void AddRoundedCorner(
        List<Vector2> contour,
        int sides,
        Vector2 corner,
        Vector2 previous,
        Vector2 next,
        float tangentDistance)
    {
        var start = corner + SafeNormalize(previous - corner) * tangentDistance;
        var end = corner + SafeNormalize(next - corner) * tangentDistance;

        var turn = 2.0f * MathF.PI / sides;
        var stepDegrees = tangentDistance < CoarseCurveTangentDistance ? CoarseCurveStepDegrees : CurveStepDegrees;
        var segmentCount = Math.Max(2, (int) Math.Ceiling(turn * 180.0f / MathF.PI / stepDegrees));
        var halfTurn = turn / 2.0f;

        for (var j = 0; j <= segmentCount; j++)
        {
            Vector2 point;
            if (j == 0)
            {
                point = start;
            }
            else if (j == segmentCount)
            {
                point = end;
            }
            else
            {
                var phi = -halfTurn + turn * j / segmentCount;
                var t = 0.5f * (1.0f + MathF.Tan(phi) / MathF.Tan(halfTurn));
                var u = 1.0f - t;
                point = start * (u * u) + corner * (2.0f * t * u) + end * (t * t);
            }

            contour.Add(point);
        }
    }

    private static ShapeMesh CreateEmpty()
    {
        // Zero thickness has no visible area; keep a valid degenerate triangle.
        return new ShapeMesh(new Vector2[3], [0, 1, 2]);
    }

    private static ShapeMesh TriangulateSolid(List<Vector2> contour, bool whole)
    {
        var count = contour.Count;
        var vertices = new Vector2[count + 1];
        for (var i = 0; i < count; i++)
            vertices[i] = contour[i];

        // The origin is the last vertex; every triangle fans out from it.
        var center = count;
        var triangleCount = whole ? count : count - 1;
        var indices = new int[triangleCount * 3];
        for (var i = 0; i < triangleCount; i++)
        {
            indices[i * 3] = center;
            indices[i * 3 + 1] = (i + 1) % count;
            indices[i * 3 + 2] = i;
        }

        return new ShapeMesh(vertices, indices);
    }

    private static ShapeMesh TriangulateRing(List<Vector2> contour, float innerScale, bool whole)
    {
        var count = contour.Count;
        var vertices = new Vector2[count * 2];
        for (var i = 0; i < count; i++)
        {
            vertices[i] = contour[i];
            vertices[count + i] = contour[i] * innerScale;
        }

        // The inner outline is a scaled copy of the outer one, so every band segment is a trapezoid.
        var quadCount = whole ? count : count - 1;
        var indices = new int[quadCount * 6];
        for (var i = 0; i < quadCount; i++)
        {
            var next = (i + 1) % count;
            var outer0 = i;
            var outer1 = next;
            var inner0 = count + i;
            var inner1 = count + next;

            var offset = i * 6;
            indices[offset] = outer1;
            indices[offset + 1] = outer0;
            indices[offset + 2] = inner1;
            indices[offset + 3] = outer0;
            indices[offset + 4] = inner0;
            indices[offset + 5] = inner1;
        }

        return new ShapeMesh(vertices, indices);
    }
    
    private static Vector2 SafeNormalize(Vector2 vector)
    {
        var length = vector.Length();
        return length > 0.0f ? vector / length : Vector2.Zero;
    }
}
