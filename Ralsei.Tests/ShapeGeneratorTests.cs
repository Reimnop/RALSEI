using System.Numerics;
using Ralsei.Core.Shape;

namespace Ralsei.Tests;

public class ShapeGeneratorTests
{
    // ---- Geometric definitions taken from the specification ----

    private static double Radius(int n) => n switch
    {
        3 => 0.575,
        4 => Math.Sqrt(0.5),
        _ => 0.5
    };

    private static double PolygonArea(int n) => n / 2.0 * Radius(n) * Radius(n) * Math.Sin(2 * Math.PI / n);

    // Corner angle such that one corner points up (n != 4) or the square is axis aligned (n == 4).
    private static (double X, double Y) Corner(int n, int j)
    {
        var offset = n % 2 == 0 && n != 4 ? 0.0 : Math.PI / n;
        var angle = -Math.PI / 2 + offset + 2 * Math.PI * j / n;
        return (Radius(n) * Math.Cos(angle), Radius(n) * Math.Sin(angle));
    }

    private static double TangentDistance(int n, double r)
    {
        if (n > 12)
            return 0.0;
        return r * (0.5 - 0.25 * (n - 3) / 9.0) * Radius(n);
    }

    private static double RemovedPerCorner(int n, double d) => d * d * Math.Sin(2 * Math.PI / n) / 6.0;

    // ---- Mesh helpers ----

    private readonly record struct Tri(Vector2 A, Vector2 B, Vector2 C)
    {
        public double SignedArea =>
            0.5 * ((double) (B.X - A.X) * (C.Y - A.Y) - (double) (C.X - A.X) * (B.Y - A.Y));
    }

    private static ShapeMesh Gen(int n, double r, double t, int k)
    {
        return ShapeGenerator.Generate(new ShapeParameters(n, (float) r, (float) t, k));
    }

    private static List<Tri> Triangles(ShapeMesh mesh)
    {
        var result = new List<Tri>();
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            result.Add(new Tri(
                mesh.Vertices[mesh.Indices[i]],
                mesh.Vertices[mesh.Indices[i + 1]],
                mesh.Vertices[mesh.Indices[i + 2]]));
        }

        return result;
    }

    private static double TotalArea(ShapeMesh mesh) => Triangles(mesh).Sum(t => Math.Abs(t.SignedArea));

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(ShapeMesh mesh)
    {
        return (
            mesh.Vertices.Min(v => (double) v.X), mesh.Vertices.Min(v => (double) v.Y),
            mesh.Vertices.Max(v => (double) v.X), mesh.Vertices.Max(v => (double) v.Y));
    }

    private static (double X, double Y) Centroid(ShapeMesh mesh)
    {
        double area = 0, cx = 0, cy = 0;
        foreach (var tri in Triangles(mesh))
        {
            var a = Math.Abs(tri.SignedArea);
            area += a;
            cx += a * (tri.A.X + tri.B.X + tri.C.X) / 3.0;
            cy += a * (tri.A.Y + tri.B.Y + tri.C.Y) / 3.0;
        }

        return (cx / area, cy / area);
    }

    private static bool Covers(ShapeMesh mesh, double x, double y, double eps = 1e-9)
    {
        foreach (var tri in Triangles(mesh))
        {
            var area = tri.SignedArea;
            if (Math.Abs(area) < 1e-12)
                continue;

            double Edge(Vector2 p, Vector2 q) => (q.X - p.X) * (y - p.Y) - (q.Y - p.Y) * (x - p.X);
            var s0 = Edge(tri.A, tri.B) * Math.Sign(area);
            var s1 = Edge(tri.B, tri.C) * Math.Sign(area);
            var s2 = Edge(tri.C, tri.A) * Math.Sign(area);
            if (s0 >= -eps && s1 >= -eps && s2 >= -eps)
                return true;
        }

        return false;
    }

    private static (long, long) Key(Vector2 v) => ((long) Math.Round(v.X * 1e6), (long) Math.Round(v.Y * 1e6));

    // Edges used by exactly one triangle, merged by position.
    private static List<(Vector2 A, Vector2 B)> BoundaryEdges(ShapeMesh mesh)
    {
        var counts = new Dictionary<((long, long), (long, long)), (int Count, Vector2 A, Vector2 B)>();
        foreach (var tri in Triangles(mesh))
        {
            if (Math.Abs(tri.SignedArea) < 1e-12)
                continue;

            foreach (var (p, q) in new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) })
            {
                var kp = Key(p);
                var kq = Key(q);
                var key = kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp);
                counts[key] = counts.TryGetValue(key, out var entry)
                    ? (entry.Count + 1, entry.A, entry.B)
                    : (1, p, q);
            }
        }

        return counts.Values.Where(e => e.Count == 1).Select(e => (e.A, e.B)).ToList();
    }

    private static double DistanceToSegment(double px, double py, Vector2 a, Vector2 b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        var len2 = abx * abx + aby * aby;
        var u = len2 > 0 ? Math.Clamp(((px - a.X) * abx + (py - a.Y) * aby) / len2, 0, 1) : 0;
        var dx = px - (a.X + u * abx);
        var dy = py - (a.Y + u * aby);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToOutline(IEnumerable<(Vector2 A, Vector2 B)> edges, double x, double y)
    {
        return edges.Min(e => DistanceToSegment(x, y, e.A, e.B));
    }

    private static bool Overlaps(Tri a, Tri b)
    {
        const double eps = 1e-6;
        Vector2[] pa = [a.A, a.B, a.C];
        Vector2[] pb = [b.A, b.B, b.C];
        foreach (var poly in new[] { pa, pb })
        {
            for (var i = 0; i < 3; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % 3];
                double nx = q.Y - p.Y, ny = p.X - q.X;
                var len = Math.Sqrt(nx * nx + ny * ny);
                nx /= len;
                ny /= len;

                double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
                foreach (var v in pa)
                {
                    var d = v.X * nx + v.Y * ny;
                    minA = Math.Min(minA, d);
                    maxA = Math.Max(maxA, d);
                }

                foreach (var v in pb)
                {
                    var d = v.X * nx + v.Y * ny;
                    minB = Math.Min(minB, d);
                    maxB = Math.Max(maxB, d);
                }

                if (Math.Min(maxA, maxB) - Math.Max(minA, minB) <= eps)
                    return false;
            }
        }

        return true;
    }

    private static string? CheckCommon(ShapeMesh mesh, ShapeMesh again, bool deep = true)
    {
        if (mesh is null || mesh.Vertices is null || mesh.Indices is null)
            return "null mesh";
        if (mesh.Indices.Length % 3 != 0)
            return "index count not a multiple of 3";
        if (mesh.Indices.Length < 3)
            return "fewer than 3 indices";
        if (mesh.Vertices.Length > 2048 || mesh.Indices.Length > 6144)
            return "over the size budget";
        if (mesh.Indices.Any(i => i < 0 || i >= mesh.Vertices.Length))
            return "index out of range";
        if (mesh.Vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)))
            return "non-finite coordinate";
        if (!mesh.Vertices.SequenceEqual(again.Vertices) || !mesh.Indices.SequenceEqual(again.Indices))
            return "not deterministic";
        if (ReferenceEquals(mesh.Vertices, again.Vertices) || ReferenceEquals(mesh.Indices, again.Indices))
            return "arrays are shared between calls";

        var tris = Triangles(mesh);
        var signs = tris.Select(t => t.SignedArea).Where(a => Math.Abs(a) > 1e-12).Select(Math.Sign).Distinct().ToList();
        if (signs.Count > 1)
            return "mixed winding";

        if (!deep)
            return null;

        var solid = tris.Where(t => Math.Abs(t.SignedArea) > 1e-12).ToList();
        for (var i = 0; i < solid.Count; i++)
        {
            var a = solid[i];
            var minAx = Math.Min(a.A.X, Math.Min(a.B.X, a.C.X));
            var maxAx = Math.Max(a.A.X, Math.Max(a.B.X, a.C.X));
            var minAy = Math.Min(a.A.Y, Math.Min(a.B.Y, a.C.Y));
            var maxAy = Math.Max(a.A.Y, Math.Max(a.B.Y, a.C.Y));
            for (var j = i + 1; j < solid.Count; j++)
            {
                var b = solid[j];
                if (Math.Min(b.A.X, Math.Min(b.B.X, b.C.X)) > maxAx ||
                    Math.Max(b.A.X, Math.Max(b.B.X, b.C.X)) < minAx ||
                    Math.Min(b.A.Y, Math.Min(b.B.Y, b.C.Y)) > maxAy ||
                    Math.Max(b.A.Y, Math.Max(b.B.Y, b.C.Y)) < minAy)
                    continue;

                if (Overlaps(a, b))
                    return $"triangles {i} and {j} overlap";
            }
        }

        return null;
    }

    private static ShapeMesh GenChecked(int n, double r, double t, int k)
    {
        var mesh = Gen(n, r, t, k);
        var error = CheckCommon(mesh, Gen(n, r, t, k));
        Assert.True(error is null, $"n={n} r={r} t={t} k={k}: {error}");
        return mesh;
    }

    // ---- 1. Whole unrounded solid ----

    [Theory]
    [MemberData(nameof(AllSides))]
    public void WholeSolid_AreaBoundsCentroid(int n)
    {
        var mesh = GenChecked(n, 0, 1, -1);
        var expected = PolygonArea(n);
        Assert.InRange(TotalArea(mesh), expected * 0.995, expected * 1.005);

        var corners = Enumerable.Range(0, n).Select(j => Corner(n, j)).ToList();
        var (minX, minY, maxX, maxY) = Bounds(mesh);
        Assert.InRange(minX, corners.Min(c => c.X) - 0.002, corners.Min(c => c.X) + 0.002);
        Assert.InRange(maxX, corners.Max(c => c.X) - 0.002, corners.Max(c => c.X) + 0.002);
        Assert.InRange(minY, corners.Min(c => c.Y) - 0.002, corners.Min(c => c.Y) + 0.002);
        Assert.InRange(maxY, corners.Max(c => c.Y) - 0.002, corners.Max(c => c.Y) + 0.002);

        var (cx, cy) = Centroid(mesh);
        Assert.InRange(cx, -0.002, 0.002);
        Assert.InRange(cy, -0.002, 0.002);
    }

    [Fact]
    public void WholeSolid_ReferenceValues()
    {
        Assert.InRange(TotalArea(Gen(3, 0, 1, -1)), 0.4295 * 0.995, 0.4295 * 1.005);
        Assert.InRange(TotalArea(Gen(4, 0, 1, -1)), 0.995, 1.005);
        Assert.InRange(TotalArea(Gen(6, 0, 1, -1)), 0.6495 * 0.995, 0.6495 * 1.005);
        Assert.InRange(TotalArea(Gen(32, 0, 1, -1)), 0.7804 * 0.995, 0.7804 * 1.005);

        var tri = Bounds(Gen(3, 0, 1, -1));
        Assert.Equal(-0.49797, tri.MinX, 0.002);
        Assert.Equal(0.49797, tri.MaxX, 0.002);
        Assert.Equal(-0.2875, tri.MinY, 0.002);
        Assert.Equal(0.575, tri.MaxY, 0.002);

        var square = Bounds(Gen(4, 0, 1, -1));
        Assert.Equal(-0.5, square.MinX, 0.002);
        Assert.Equal(0.5, square.MaxX, 0.002);
        Assert.Equal(-0.5, square.MinY, 0.002);
        Assert.Equal(0.5, square.MaxY, 0.002);

        var hex = Bounds(Gen(6, 0, 1, -1));
        Assert.Equal(-0.4330, hex.MinX, 0.002);
        Assert.Equal(0.4330, hex.MaxX, 0.002);
        Assert.Equal(-0.5, hex.MinY, 0.002);
        Assert.Equal(0.5, hex.MaxY, 0.002);

        var pent = Bounds(Gen(5, 0, 1, -1));
        Assert.Equal(0.5, pent.MaxY, 0.002);
        Assert.Equal(-0.4045, pent.MinY, 0.002);
        Assert.Equal(0.4755, pent.MaxX, 0.002);
        Assert.Equal(-0.4755, pent.MinX, 0.002);

        var oct = Bounds(Gen(8, 0, 1, -1));
        Assert.Equal(0.5, oct.MaxX, 0.002);
        Assert.Equal(-0.5, oct.MinX, 0.002);
        Assert.Equal(0.5, oct.MaxY, 0.002);
        Assert.Equal(-0.5, oct.MinY, 0.002);
    }

    [Theory]
    [MemberData(nameof(AllSides))]
    public void WholeSolid_HasCornersAtSpecifiedAngles(int n)
    {
        var mesh = Gen(n, 0, 1, -1);
        for (var j = 0; j < n; j++)
        {
            var (x, y) = Corner(n, j);
            Assert.Contains(mesh.Vertices, v => Math.Abs(v.X - x) < 1e-4 && Math.Abs(v.Y - y) < 1e-4);
        }

        // A corner points straight up for every n except 4.
        if (n != 4)
            Assert.Contains(mesh.Vertices, v => Math.Abs(v.X) < 1e-4 && Math.Abs(v.Y - Radius(n)) < 1e-4);
    }

    // ---- 2. Rounded solid ----

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RoundedSolid_AreaAndContainment(double r)
    {
        for (var n = 3; n <= 12; n++)
        {
            var mesh = GenChecked(n, r, 1, -1);
            var d = TangentDistance(n, r);
            var unrounded = PolygonArea(n);
            var expected = unrounded - n * RemovedPerCorner(n, d);
            Assert.True(Math.Abs(TotalArea(mesh) - expected) <= 0.015 * unrounded, $"n={n} area");

            var corners = Enumerable.Range(0, n).Select(j => Corner(n, j)).ToList();
            var (minX, minY, maxX, maxY) = Bounds(mesh);
            Assert.True(minX >= corners.Min(c => c.X) - 1e-4 && maxX <= corners.Max(c => c.X) + 1e-4, $"n={n} x bounds");
            Assert.True(minY >= corners.Min(c => c.Y) - 1e-4 && maxY <= corners.Max(c => c.Y) + 1e-4, $"n={n} y bounds");

            foreach (var v in mesh.Vertices)
            {
                for (var j = 0; j < n; j++)
                {
                    var a = corners[j];
                    var b = corners[(j + 1) % n];
                    var cross = (b.X - a.X) * (v.Y - a.Y) - (b.Y - a.Y) * (v.X - a.X);
                    var edgeLength = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                    Assert.True(cross / edgeLength >= -1e-4, $"n={n} vertex outside the polygon");
                }
            }
        }
    }

    [Fact]
    public void Rounding_DisabledFor13PlusSides()
    {
        for (var n = 13; n <= 32; n++)
        {
            var mesh = GenChecked(n, 1, 1, -1);
            var expected = PolygonArea(n);
            Assert.InRange(TotalArea(mesh), expected * 0.995, expected * 1.005);
        }
    }

    // ---- 3. Edge midpoints on the outline, corners cut off ----

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RoundedSolid_OutlineMatchesDepth(double r)
    {
        for (var n = 3; n <= 12; n++)
        {
            var mesh = GenChecked(n, r, 1, -1);
            var edges = BoundaryEdges(mesh);
            var d = TangentDistance(n, r);
            var depth = d * Math.Sin(Math.PI / n) / 2;
            for (var j = 0; j < n; j++)
            {
                var a = Corner(n, j);
                var b = Corner(n, (j + 1) % n);
                var mid = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                Assert.True(DistanceToOutline(edges, mid.Item1, mid.Item2) < 1e-3, $"n={n} midpoint {j}");

                var nearest = DistanceToOutline(edges, a.X, a.Y);
                Assert.True(nearest >= 0.9 * depth && nearest <= 1.1 * depth,
                    $"n={n} corner {j}: {nearest} vs depth {depth}");
            }
        }
    }

    // ---- 4. Rings ----

    [Theory]
    [MemberData(nameof(AllSides))]
    public void Ring_AreaHoleAndInnerRadius(int n)
    {
        foreach (var t in new[] { 0.25, 0.5, 0.9 })
        {
            var mesh = GenChecked(n, 0, t, -1);
            var expected = PolygonArea(n) * (1 - Math.Pow(1 - t, 2));
            Assert.InRange(TotalArea(mesh), expected * 0.995, expected * 1.005);
            Assert.False(Covers(mesh, 0, 0), $"n={n} t={t} origin covered");

            for (var j = 0; j < n; j++)
            {
                var (x, y) = Corner(n, j);
                var ix = x * (1 - t);
                var iy = y * (1 - t);
                Assert.Contains(mesh.Vertices, v => Math.Abs(v.X - ix) < 5e-4 && Math.Abs(v.Y - iy) < 5e-4);
            }
        }

        Assert.True(Covers(Gen(n, 0, 1, -1), 0, 0));
    }

    [Fact]
    public void Ring_ZeroAndClampedThickness()
    {
        for (var n = 3; n <= 32; n++)
        {
            var empty = GenChecked(n, 0, 0, -1);
            Assert.True(TotalArea(empty) < 1e-6);

            AssertSame(Gen(n, 0, 1.5, -1), Gen(n, 0, 1, -1));
            AssertSame(Gen(n, 0, -0.3, -1), Gen(n, 0, 0, -1));
        }
    }

    // ---- 5. Rounded rings ----

    [Fact]
    public void RoundedRing_AreaAndNesting()
    {
        for (var n = 3; n <= 12; n++)
        {
            foreach (var r in new[] { 0.5, 1.0 })
            {
                foreach (var t in new[] { 0.25, 0.5, 0.9 })
                {
                    var mesh = GenChecked(n, r, t, -1);
                    var d = TangentDistance(n, r);
                    var unrounded = PolygonArea(n);
                    var f = 1 - Math.Pow(1 - t, 2);
                    var removed = n * RemovedPerCorner(n, d);

                    // The inner outline is a scaled copy of the outer one, so it is rounded the same way.
                    var expected = f * (unrounded - removed);
                    Assert.True(Math.Abs(TotalArea(mesh) - expected) <= 0.01 * unrounded, $"n={n} r={r} t={t} area");

                    // The area formula quoted in the specification (inner rounding ignored) holds when the band is wide.
                    if (t >= 0.5)
                    {
                        var quoted = unrounded * f - removed;
                        Assert.True(Math.Abs(TotalArea(mesh) - quoted) <= 0.03 * unrounded, $"n={n} r={r} t={t} quoted");
                    }

                    AssertInnerOutlineInsideOuter(mesh);
                }
            }
        }
    }

    // Splits the boundary into connected loops and checks the loop around the origin lies inside the outer one.
    private static void AssertInnerOutlineInsideOuter(ShapeMesh mesh)
    {
        var edges = BoundaryEdges(mesh);
        var parent = new Dictionary<(long, long), (long, long)>();

        (long, long) Find((long, long) x)
        {
            while (parent[x] != x)
                x = parent[x] = parent[parent[x]];
            return x;
        }

        foreach (var (a, b) in edges)
        {
            parent.TryAdd(Key(a), Key(a));
            parent.TryAdd(Key(b), Key(b));
            parent[Find(Key(a))] = Find(Key(b));
        }

        var groups = edges.GroupBy(e => Find(Key(e.A))).ToList();
        Assert.Equal(2, groups.Count);

        var outer = groups.OrderByDescending(g => g.Max(e => Math.Max(e.A.Length(), e.B.Length()))).First().ToList();
        var inner = groups.First(g => g.Key != Find(Key(outer[0].A))).ToList();
        foreach (var (a, _) in inner)
        {
            var crossings = 0;
            foreach (var (p, q) in outer)
            {
                if ((p.Y > a.Y) == (q.Y > a.Y))
                    continue;
                var x = p.X + (a.Y - p.Y) / (q.Y - p.Y) * (q.X - p.X);
                if (x > a.X)
                    crossings++;
            }

            Assert.True(crossings % 2 == 1, "inner vertex outside the outer outline");
        }
    }

    // ---- 6. Slices ----

    private static IEnumerable<int> SliceSides => [3, 4, 5, 6, 8, 12];

    [Fact]
    public void Slices_AreaAndCutEdges()
    {
        foreach (var n in SliceSides)
        {
            for (var k = 1; k < n; k++)
            {
                var mesh = GenChecked(n, 0, 1, k);
                var expected = (double) k / n * PolygonArea(n);
                Assert.InRange(TotalArea(mesh), expected * 0.995, expected * 1.005);
                AssertWedgeBoundary(mesh, n, k);
            }
        }
    }

    [Fact]
    public void Slices_RoundedAreaAndCutEdges()
    {
        foreach (var n in SliceSides)
        {
            foreach (var r in new[] { 0.5, 1.0 })
            {
                var d = TangentDistance(n, r);
                var unrounded = PolygonArea(n);

                // A single side has no interior corner, so rounding changes nothing.
                var single = GenChecked(n, r, 1, 1);
                Assert.True(Math.Abs(TotalArea(single) - unrounded / n) <= 0.005 * unrounded / n);
                AssertWedgeBoundary(single, n, 1);

                for (var k = 2; k < n; k++)
                {
                    var mesh = GenChecked(n, r, 1, k);
                    var expected = (double) k / n * unrounded - (k - 1) * RemovedPerCorner(n, d);
                    Assert.True(Math.Abs(TotalArea(mesh) - expected) <= 0.015 * unrounded, $"n={n} r={r} k={k}");
                    AssertWedgeBoundary(mesh, n, k);
                }
            }
        }
    }

    private static void AssertWedgeBoundary(ShapeMesh mesh, int n, int k)
    {
        var edges = BoundaryEdges(mesh);
        Assert.True(DistanceToOutline(edges, 0, 0) < 1e-3, $"n={n} k={k}: origin not on boundary");
        foreach (var corner in new[] { 0, k })
        {
            var (x, y) = Corner(n, corner);
            for (var s = 0; s <= 20; s++)
            {
                var f = s / 20.0;
                Assert.True(DistanceToOutline(edges, x * f, y * f) < 1e-3, $"n={n} k={k}: cut line to corner {corner}");
            }
        }
    }

    // ---- 7. Sentinel and clamping ----

    [Fact]
    public void SliceSentinelAndClamping()
    {
        for (var n = 3; n <= 32; n++)
        {
            var whole = Gen(n, 0.5f, 0.6f, -1);
            AssertSame(whole, Gen(n, 0.5f, 0.6f, n));
            AssertSame(whole, Gen(n, 0.5f, 0.6f, n + 5));
            AssertSame(whole, Gen(n, 0.5f, 0.6f, int.MinValue));
            AssertSame(whole, Gen(n, 0.5f, 0.6f, int.MaxValue));
        }
    }

    [Fact]
    public void SidesAndParameterClamping()
    {
        AssertSame(Gen(2, 0.5, 0.5, -1), Gen(3, 0.5, 0.5, -1));
        AssertSame(Gen(-100, 0.5, 0.5, -1), Gen(3, 0.5, 0.5, -1));
        AssertSame(Gen(40, 0.5, 0.5, -1), Gen(32, 0.5, 0.5, -1));
        AssertSame(Gen(int.MaxValue, 0.5, 0.5, 7), Gen(32, 0.5, 0.5, 7));
        foreach (var n in new[] { 3, 5, 12, 20 })
        {
            AssertSame(Gen(n, 2, 0.5, -1), Gen(n, 1, 0.5, -1));
            AssertSame(Gen(n, -1, 0.5, -1), Gen(n, 0, 0.5, -1));
            AssertSame(Gen(n, 0.5, 0.5, 100), Gen(n, 0.5, 0.5, n));
        }
    }

    [Fact]
    public void RoundnessIgnoredFor13PlusSides()
    {
        for (var n = 13; n <= 32; n++)
        {
            AssertSame(Gen(n, 1, 1, -1), Gen(n, 0, 1, -1));
            AssertSame(Gen(n, 0.6, 0.4, 5), Gen(n, 0, 0.4, 5));
        }
    }

    [Fact]
    public void NonFiniteInputsAreSafe()
    {
        float[] values =
            [float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, -0.0f, 1e-30f];
        foreach (var r in values)
        {
            foreach (var t in values)
            {
                foreach (var n in new[] { int.MinValue, 0, 3, 7, int.MaxValue })
                {
                    foreach (var k in new[] { int.MinValue, -1, 0, 2, int.MaxValue })
                    {
                        var p = new ShapeParameters(n, r, t, k);
                        var mesh = ShapeGenerator.Generate(p);
                        var error = CheckCommon(mesh, ShapeGenerator.Generate(p));
                        Assert.True(error is null, $"n={n} r={r} t={t} k={k}: {error}");
                    }
                }
            }
        }
    }

    // ---- 8. Sliced rings ----

    [Fact]
    public void SlicedRing_Area()
    {
        foreach (var n in SliceSides)
        {
            for (var k = 1; k < n; k++)
            {
                foreach (var t in new[] { 0.25, 0.5, 0.9 })
                {
                    var mesh = GenChecked(n, 0, t, k);
                    var expected = (double) k / n * PolygonArea(n) * (1 - Math.Pow(1 - t, 2));
                    Assert.InRange(TotalArea(mesh), expected * 0.995, expected * 1.005);
                    Assert.False(Covers(mesh, 0, 0), $"n={n} k={k} t={t}");
                }
            }
        }
    }

    // ---- 9. Sweep ----

    [Fact]
    public void FullParameterSweep()
    {
        var failures = new List<string>();
        for (var n = 3; n <= 32; n++)
        {
            foreach (var r in new[] { 0.0, 0.5, 1.0 })
            {
                foreach (var t in new[] { 0.0, 0.1, 0.5, 1.0 })
                {
                    foreach (var k in new[] { -1, 1, Math.Min(n - 1, n / 2 + 1), n })
                    {
                        var mesh = Gen(n, r, t, k);
                        var error = CheckCommon(mesh, Gen(n, r, t, k));
                        if (error is null && t > 0)
                            error = CheckQuality(mesh);
                        if (error is not null)
                            failures.Add($"n={n} r={r} t={t} k={k}: {error}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    // Quality rules: no slivers, no duplicate vertices, no T-junction cracks.
    private static string? CheckQuality(ShapeMesh mesh)
    {
        foreach (var tri in Triangles(mesh))
        {
            if (Math.Abs(tri.SignedArea) < 1e-9)
                return $"degenerate triangle ({tri.SignedArea})";

            Vector2[] p = [tri.A, tri.B, tri.C];
            for (var i = 0; i < 3; i++)
            {
                var u = p[(i + 1) % 3] - p[i];
                var v = p[(i + 2) % 3] - p[i];
                var angle = Math.Atan2(Math.Abs(u.X * v.Y - u.Y * v.X), Vector2.Dot(u, v)) * 180 / Math.PI;
                if (angle < 0.1)
                    return $"sliver angle {angle}";
            }
        }

        var seen = new HashSet<(long, long)>();
        foreach (var v in mesh.Vertices)
        {
            if (!seen.Add(((long) Math.Round(v.X * 1e5), (long) Math.Round(v.Y * 1e5))))
                return $"duplicate vertex at {v}";
        }

        // No vertex lies in the interior of a boundary edge of a different triangle (watertightness).
        foreach (var (a, b) in BoundaryEdges(mesh))
        {
            foreach (var v in mesh.Vertices)
            {
                if (Vector2.Distance(v, a) < 1e-5 || Vector2.Distance(v, b) < 1e-5)
                    continue;
                if (DistanceToSegment(v.X, v.Y, a, b) < 1e-6)
                    return $"T-junction at {v}";
            }
        }

        // Every interior edge must be shared by exactly two triangles.
        var counts = new Dictionary<((long, long), (long, long)), int>();
        foreach (var tri in Triangles(mesh))
        {
            foreach (var (p, q) in new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) })
            {
                var kp = Key(p);
                var kq = Key(q);
                var key = kp.CompareTo(kq) <= 0 ? (kp, kq) : (kq, kp);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts.Values.Any(c => c > 2) ? "edge shared by more than two triangles" : null;
    }

    // ---- 10. Smoothness ----

    [Fact]
    public void RoundedTriangle_OutlineTurnsGradually()
    {
        foreach (var r in new[] { 0.2, 0.5, 1.0 })
        {
            var mesh = GenChecked(3, r, 1, -1);
            var edges = BoundaryEdges(mesh);

            // Walk the closed outline.
            var next = new Dictionary<(long, long), List<Vector2>>();
            var points = new Dictionary<(long, long), Vector2>();
            foreach (var (a, b) in edges)
            {
                next.TryAdd(Key(a), []);
                next.TryAdd(Key(b), []);
                next[Key(a)].Add(b);
                next[Key(b)].Add(a);
                points[Key(a)] = a;
                points[Key(b)] = b;
            }

            Assert.All(next.Values, list => Assert.Equal(2, list.Count));

            var start = edges[0].A;
            var loop = new List<Vector2> { start };
            var previous = start;
            var current = next[Key(start)][0];
            while (Key(current) != Key(start))
            {
                loop.Add(current);
                var options = next[Key(current)];
                var candidate = Key(options[0]) == Key(previous) ? options[1] : options[0];
                previous = current;
                current = candidate;
            }

            Assert.Equal(edges.Count, loop.Count);

            var maxTurn = 0.0;
            var maxDeviation = 0.0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[(i + loop.Count - 1) % loop.Count];
                var b = loop[i];
                var c = loop[(i + 1) % loop.Count];
                var u = b - a;
                var v = c - b;
                var turn = Math.Atan2(u.X * v.Y - u.Y * v.X, Vector2.Dot(u, v)) * 180 / Math.PI;
                maxTurn = Math.Max(maxTurn, Math.Abs(turn));

                // Distance from a vertex to the chord through its neighbours bounds how far a chord strays from the
                // smooth curve (roughly four times the per-segment sagitta).
                var chord = c - a;
                var dist = Math.Abs(chord.X * (b.Y - a.Y) - chord.Y * (b.X - a.X)) / chord.Length();
                maxDeviation = Math.Max(maxDeviation, dist);
            }

            Assert.True(maxTurn <= 20.0, $"r={r}: max turn {maxTurn}");
            Assert.True(maxDeviation <= 0.008, $"r={r}: max deviation {maxDeviation}");
        }
    }

    // ---- Misc ----

    [Fact]
    public void ZeroSliceCountIsAccepted()
    {
        for (var n = 3; n <= 32; n++)
            GenChecked(n, 0.5, 0.5, 0);
    }

    [Fact]
    public void CombinationEdgeCases()
    {
        // n = 3, r = 1, k = 2: one rounded interior corner, two sharp ends.
        var mesh = GenChecked(3, 1, 1, 2);
        var d = TangentDistance(3, 1);
        var expected = 2.0 / 3.0 * PolygonArea(3) - RemovedPerCorner(3, d);
        Assert.True(Math.Abs(TotalArea(mesh) - expected) <= 0.015 * PolygonArea(3));

        // n = 3, r = 1, t = 1, whole: rounded triangle.
        var rounded = GenChecked(3, 1, 1, 3);
        Assert.True(Math.Abs(TotalArea(rounded) - (PolygonArea(3) - 3 * RemovedPerCorner(3, d))) <= 0.015 * PolygonArea(3));
    }

    [Fact]
    public void ReturnedArraysAreOwnedByCaller()
    {
        var a = Gen(5, 0.5, 0.5, -1);
        var b = Gen(5, 0.5, 0.5, -1);
        Assert.NotSame(a.Vertices, b.Vertices);
        Assert.NotSame(a.Indices, b.Indices);
        a.Vertices[0] = new Vector2(100, 100);
        Assert.NotEqual(a.Vertices[0], b.Vertices[0]);
    }

    [Fact]
    public void ConcurrentGenerationIsDeterministic()
    {
        var reference = Gen(7, 0.7, 0.3, 4);
        Parallel.For(0, 64, _ =>
        {
            var mesh = Gen(7, 0.7, 0.3, 4);
            Assert.True(reference.Vertices.SequenceEqual(mesh.Vertices) && reference.Indices.SequenceEqual(mesh.Indices));
        });
    }

    private static void AssertSame(ShapeMesh expected, ShapeMesh actual)
    {
        Assert.True(expected.Vertices.SequenceEqual(actual.Vertices), "vertices differ");
        Assert.True(expected.Indices.SequenceEqual(actual.Indices), "indices differ");
    }

    public static IEnumerable<object[]> AllSides() => Enumerable.Range(3, 30).Select(n => new object[] { n });
}
