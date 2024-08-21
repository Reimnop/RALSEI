# Parametric Polygon Shape Generator: Requirements Specification

Namespace `Ralsei.Core.Shape`. Audience: the implementer, who has only this document.

## 1. Purpose and scope

An animation player lets authors attach a "custom shape" to a scene object. The author supplies four numbers (sides, roundness, thickness, slice count) and the player must show a filled 2D regular polygon, optionally with rounded corners, optionally hollowed into a ring, optionally cut to a partial wedge or arc. This component turns those four numbers into a triangle mesh. It generates geometry only: no GPU resources, no colour, no transform, no caching (the caller caches).

Out of scope: parsing the numbers from files, hashing/caching, uploading to the GPU, text, built-in fixed shapes.

## 2. Interface contract

```
namespace Ralsei.Core.Shape
ShapeParameters(int sides, float roundness, float thickness, int sliceCount)
    read-only: int Sides, float Roundness, float Thickness, int SliceCount
ShapeMesh(Vector2[] vertices, int[] indices)          // System.Numerics.Vector2
    read-only: Vector2[] Vertices, int[] Indices
static class ShapeGenerator { static ShapeMesh Generate(ShapeParameters parameters); }
```

- `ShapeParameters` and `ShapeMesh` are plain immutable carriers; they perform no validation and no generation. `ShapeMesh` stores the arrays it is given (no copy required).
- `Generate` is pure and deterministic: the same four values always yield byte-identical output (same vertex and index arrays, same order), on any thread, any number of times. No mutable static state, no randomness, no time/culture dependence. Safe to call concurrently.
- `Generate` never throws for any int/float input (including NaN, infinities, negatives, huge values). It never returns null arrays, never returns NaN/infinite coordinates.
- The returned arrays are newly allocated and owned by the caller. Apart from them and short-lived temporaries, no allocation is expected. Do not return shared/static arrays.
- Performance: invoked once per distinct parameter set (then cached for the session). Budget: under 1 ms typical, under 5 ms worst case on a desktop CPU; a few thousand triangles at most. No need for micro-optimisation.

## 3. Input domain

| Parameter | Authored range | Handling outside range |
|---|---|---|
| sides | integer 3..32 | clamp to 3..32 |
| roundness | 0..1 | clamp to 0..1 |
| thickness | 0..1 | clamp to 0..1 |
| sliceCount | integer 1..sides | any negative value means "no slicing, whole shape" (sentinel; -1 is what authors/tools send). Positive values greater than the (clamped) sides are clamped to sides. A value equal to sides is also the whole shape. |

- Values are clamped, never wrapped or normalised.
- Slicing is counted in polygon sides, not degrees: sliceCount k keeps k consecutive sides' worth of the shape (see 5.4). The relation to sides is therefore k <= n; k == n means "whole".
- Non-finite roundness or thickness must be sanitised to a finite value inside 0..1 (choice free, see section 10).
- sliceCount of 0: see section 10.

## 4. Output conventions

- Coordinates: 2D, +x right, +y up, abstract object-local units. Origin (0,0) is the shape's centre; every shape is centred on the circumcentre of the underlying regular polygon, even when sliced (a sliced shape is NOT re-centred on its own bounding box). The renderer scales/rotates/translates the mesh by the object transform, so units are "object units" where 1.0 is a nominal unit box.
- The renderer derives texture/gradient coordinates directly from vertex positions (UV equals position). Therefore positions are part of the visual contract: the origin must remain the gradient centre and extents must match section 6. No separate UV output exists.
- Orientation of the regular polygon (corners lie on a circle of radius R around the origin, R from section 6): for every side count other than 4, the corner angles are 90 degrees + 360 degrees x k / n (k integer), so one corner always points straight up: triangle apex up (flat base at the bottom), pentagon point up, hexagon pointy-top (corners at top and bottom, vertical flat sides left/right), octagon with a corner at top, etc. Even counts therefore also have a corner straight down. For 4 sides the corners lie at 45 degrees + 90 degrees x k, i.e. an axis-aligned square (not a diamond).
- Index buffer: `int[]` (32-bit), length a multiple of 3, every index in [0, Vertices.Length). Index counts are not limited to 16-bit range, but must stay within: at most 2048 vertices and 6144 indices for any input.
- Triangle winding: all triangles must share one orientation. Preferred: clockwise when +y is up. Back-face culling is currently disabled in all renderers, so winding is not functionally relied upon, but it must be uniform (no mixed winding, as culling may be enabled later).
- Depth: the mesh is flat (2D); no z, no depth ordering within the mesh. Triangles must not overlap (overlap would double-blend translucent colours): the union of triangles equals the shape region, each point covered at most once (shared edges are fine).
- The mesh never contains a degenerate "all vertices unused" state: Indices.Length >= 3 always, even when the visible area is zero.

## 5. Visual behaviour

### 5.1 Sides
n regular polygon, n = 3..32, corners on a circle of radius R(n) (section 6) with orientation as in section 4. With n large (e.g. 32) it is visually a near-circle. Filled by default (thickness 1, whole, roundness 0): a solid polygon covering exactly the region bounded by the straight edges between consecutive corners.

### 5.2 Roundness
- Roundness r in 0..1 blends from sharp corners (r = 0, razor-sharp, straight edges meeting at the exact polygon corners) to maximally rounded (r = 1).
- A rounded corner looks like the sharp corner replaced by a smooth, convex, symmetric curve that is tangent to both adjoining edges; the curve starts on each edge at distance d from the original corner. The straight part of every edge shrinks by d at each end; the edge's original midpoint region stays straight. The rounded outline is continuous and encloses less area than the sharp polygon; the curve never leaves the sharp polygon.
- The tangent distance d grows linearly with r (see section 6): d = r x D(n), where D(n) depends on side count. Fewer sides allow relatively larger rounding; at r = 1 a triangle has visibly bulbous corners, and a 12-gon has just softened corners. For n from 13 through 32, rounding is disabled: roundness has no effect and corners stay sharp (the shape is already close to round). The change between n = 12 and n = 13 is abrupt by design.
- At no n does the maximum rounding reach half an edge length, so adjoining corner curves never touch or overlap.
- Rounding applies identically to all corners of the outer contour (except the two cut-end corners of sliced shapes, see 5.4) and to the inner contour of rings.

### 5.3 Thickness
- Thickness t in 0..1 is the ring width as a fraction of the radius. t = 1: solid, no hole. t between 0 and 1: a ring (hollow polygon) whose inner contour is the outer unrounded polygon uniformly scaled toward the origin by factor (1 - t): inner corners lie on the same rays from the centre as outer corners, at (1 - t) times the distance. Hence ring width measured radially along a corner ray is t x R, and measured perpendicular to an edge it is t x apothem.
- t = 0: inner contour coincides with outer: the shape has zero area and renders nothing. Output must still be well-formed (section 4: at least 3 indices, in range; zero-area triangles are acceptable here).
- t just below 1 gives a tiny hole at the centre; t just above 0 gives a hair-thin outline. Shape must remain valid and non-overlapping across the whole range.
- With rounding the inner contour is also rounded, following the same corners (see section 10 for how strongly).
- Thickness equal to (or clamped to) 1 means solid: no hole, no inner contour.

### 5.4 Slice count
- Slice count k < n keeps a contiguous angular part of the shape spanning exactly k polygon sides, proceeding counter-clockwise (increasing angle) from a fixed start corner.
  Corner j sits at angle -90 degrees + 360 degrees x j / n for even n other than 4, and at -90 degrees + 180 degrees / n + 360 degrees x j / n for odd n and for n = 4. So corner 0 is: straight down for even n other than 4; the corner just right of straight down for odd n; the lower-right corner (-45 degrees) for n = 4. The kept region spans the angular interval from corner 0 to corner k, counter-clockwise (k x 360/n degrees), bounded by the radial cut lines through the origin and those two corners.
- Cut edges: straight, radial (lie on the rays from the origin through corner 0 and corner k). In a solid shape the result is a pie wedge with apex at the origin (k = 1 is a single triangle with one straight outer edge). In a ring, the cut edges are the radial end caps of a partial band running between the inner and outer contour.
- Rounding in sliced shapes: the k - 1 interior corners (corners 1..k-1) are rounded exactly like in a whole shape; the two end corners 0 and k, where the outer contour meets a cut edge, stay sharp (cut ends are crisp, not rounded). The inner contour behaves likewise.
- k >= n: whole shape (identical to the sentinel). k = n - 1 leaves exactly one sector missing, bounded by the two radial cut lines.
- A sliced shape with t = 1 is a pie wedge touching the centre; with t = 0 it is zero area.

### 5.5 Combination edge cases (all must be valid output)
- r = 0 and any t, k: perfectly straight edges, no extra curve artefacts.
- n = 3, r = 1, k = 1: a single sharp-cornered wedge (no interior corners exist, so rounding has no effect).
- n = 3, r = 1, k = 2: one rounded interior corner (corner 1), two sharp end corners.
- n = 32 (or any n >= 13) with r > 0: identical to r = 0.
- n = 3, t = 1, r = 1, k = n: smoothly rounded solid triangle.
- Ring with extreme rounding and thick band (t close to 1): inner and outer must not cross; inner contour always lies inside the outer one.
- Slices spanning more than half the polygon (non-convex wedges): still triangulated without overlap or gaps.

## 6. Compatibility constants

Authored content is built against these; changing them would visibly shift or resize existing scenes.

| Quantity | Value | Tolerance | Why it matters |
|---|---|---|---|
| Circumradius R, n = 3 | 0.575 (apex y = +0.575, base y = -0.2875, half-width 0.49797) | +-0.001 | Triangle has to align with the built-in fixed triangle in size and centroid-centring. |
| Circumradius R, n = 4 | 0.7071 (square with side 1, extents +-0.5) | +-0.001 | Square must match the unit box and the built-in square. |
| Circumradius R, every other n (5..32) | 0.5 | +-0.001 | Hexagon must match built-in hexagon (pointy top/bottom, y extent +-0.5, x extent +-0.4330); circle-like shapes fit in the 1x1 unit box. |
| Corner orientation | corners at 90 deg + 360 deg k/n (n != 4); 45 deg + 90 deg k (n = 4) | +-0.01 deg | Authored rotations assume apex-up/flat-bottom triangles and axis-aligned squares. |
| Inner contour scale | exactly (1 - t) times outer contour about origin | +-0.0005 absolute on (1 - t) | Ring width as authored; used for thin outlines. |
| Rounding tangent distance at r = 1, as a fraction of the actual R(n) in use: D(n)/R = 0.5 - 0.25 x (n - 3)/9 for n = 3..12 (n=3: 0.5, 4: 0.4722, 5: 0.4444, 6: 0.4167, 7: 0.3889, 8: 0.3611, 9: 0.3333, 10: 0.3056, 11: 0.2778, 12: 0.25) | linear in r (distance = r x D(n)) | +-3% relative | Authored roundness values assume this extent per side count. |
| Rounding for n >= 13 | none | exact | 13+ sided shapes stay sharp regardless of r. |
| Rounding depth: distance from the original corner to the rounded outline measured along the corner's bisector toward the centre | d x sin(pi/n) / 2, where d = r x D(n) x R | +-10% | Fixes how "round" the curve feels; the curve is shallower (flatter, parabola-like) than a true circular arc tangent at the same points (which would cut deeper), so a circular fillet of the same tangent distance is NOT acceptable. |
| Sliced shapes: end corners (0 and k) sharp | exact | n/a | Cut edges stay crisp; changes silhouette of every partial shape. |
| Cut-start corner (0) location per n (section 5.4) and ccw direction | exact | +-0.01 deg | Authored slice counts expect the missing/kept part on a known side. |

## 7. Quality requirements (free implementation choice)

- Rounded corners: the polyline approximating a rounded outline deviates from the ideal smooth curve by at most 0.002 object units, and the angle between consecutive segments on a rounded corner is at most 20 degrees. (These are minimums; finer is fine within the vertex budget of section 4.)
- No zero-area (degenerate) triangles unless unavoidable (thickness 0 case). Triangles have positive area with area >= 1e-9 units squared and no sliver triangles with an interior angle below 0.1 degree, except where geometry forces it.
- No duplicate vertices at identical positions (attributes are position only, so no seam is needed), except where unavoidable at thickness 0 or t = 1 wedge apex.
- Straight (unrounded) edges need no intermediate vertices.
- Vertex positions are float-accurate to 1e-5 of their ideal location.
- No T-junctions that produce cracks: the union of triangles must be watertight inside the shape (adjacent triangles share edge endpoints).

## 8. Acceptance tests (black-box)

For every test: mesh is non-null; Indices.Length % 3 == 0; every index in range; all coordinates finite; Indices.Length >= 3; Vertices.Length <= 2048; Indices.Length <= 6144; generating twice gives identical arrays (determinism); all triangles have the same signed-area sign (uniform winding) other than zero-area triangles; no two triangles overlap (sum of triangle areas equals area of the union within 1e-4; checkable via rasterisation or pairwise test).

Let A(n) = (n/2) R(n)^2 sin(2 pi/n) be the unrounded solid polygon area.

1. Whole unrounded solid (r = 0, t = 1, k = -1):
   - Total area = A(n) within 0.5%. Reference values: n=3: 0.4295; n=4: 1.0; n=6: 0.6495; n=32: 0.7804.
   - Bounding box equals the bounding box of the corners (section 4 orientation) within 0.002: n=3: x in [-0.49797, 0.49797], y in [-0.2875, 0.575]; n=4: [-0.5, 0.5] x [-0.5, 0.5]; n=6: x in [-0.4330, 0.4330], y in [-0.5, 0.5]; n=5: y max 0.5, y min -0.5 cos36 = -0.4045, x in +-0.4755; n=8: [-0.5,0.5] square-ish with corners at top, bottom, left, right.
   - Centroid within 0.002 of the origin (every regular n).
2. Rounded solid (t = 1, k = -1, 0 < r <= 1, 3 <= n <= 12): area = (n sin(2 pi/n)/2) x (R^2 - d^2/3) with d = r x D(n) x R, tolerance 1.5% of the unrounded area A(n). Bounding box contained in the unrounded one; no vertex outside the unrounded polygon (tolerance 1e-4). For n >= 13, output area equals the unrounded one (+-0.5%) for any r.
3. Rounded solid, r > 0, 3 <= n <= 12: the midpoint of each original edge is on the outline (within 1e-3); the original corner point is not on the outline, and the nearest outline point lies at distance 0.9 to 1.1 times d x sin(pi/n)/2 from it.
4. Ring (r = 0, k = -1, 0 < t < 1): area = A(n) x (1 - (1 - t)^2) within 0.5%; t = 0.25, 0.5, 0.9 sampled; the point (0,0) is not covered when t < 1 (t=0.25..0.9), is covered when t = 1; sampled inner-contour vertex distance to the origin is (1 - t) x R (+-0.0005) at corner rays; t = 0 -> total area < 1e-6; t clamped: t = 1.5 gives the same output as t = 1, t = -0.3 same as t = 0.
5. Rounded ring (0 < r <= 1, 3 <= n <= 12, 0 < t < 1): when the inner contour is a proportionally scaled copy of the outer one, area = (1 - (1 - t)^2) x (A(n) - removed corner area of the outer contour) within 1% of A(n); the inner outline entirely inside the outer outline.
6. Slices (r = 0, t = 1): kept area = (k/n) x A(n) within 0.5% for every 1 <= k < n at n = 3, 4, 5, 6, 8, 12; the origin lies on the boundary of the wedge; the shape's boundary contains the segments from the origin to corner 0 and corner k (section 5.4 locations) within 1e-3. With r > 0 and 2 <= k <= n-1: area = (k/n)A(n) minus (k - 1) x d^2 sin(2 pi/n)/6, tolerance 1.5% of A(n). For k = 1 rounding has no effect on the shape (area unchanged).
7. Slice sentinel and clamping: k = -1 and k = n and k = n + 5 produce identical output; sides = 2 and sides = 3 identical; sides = 40 and sides = 32 identical; roundness = 2 identical to 1; roundness = -1 identical to 0; n = 13, r = 1 identical to n = 13, r = 0 (and for any n from 13..32).
8. Sliced ring (r = 0): area = (k/n) x A(n) x (1 - (1 - t)^2) within 0.5%.
9. Vertex/index bounds for the full parameter sweep n = 3..32, r in {0, 0.5, 1}, t in {0, 0.1, 0.5, 1}, k in {-1, 1, mid, n}: all pass the common checks above.
10. Rounding smoothness: for n = 3, r = 1, solid: consecutive outline segments on rounded corners turn by at most 20 degrees each.

## 9. Explicit non-requirements

The implementer is free to choose: tessellation scheme (how interior and ring are divided into triangles); vertex order and which vertex is index 0; index layout; number of vertices; how curved corners are approximated, including the specific curve family as long as the section 6 depth/tangent figures hold; whether the solid, ring, rounded and sliced variants share code or use separate code paths; internal structure, helper classes, naming; whether vertices are shared or duplicated between triangles (a seam is never needed); whether the generator caches internally; floating-point evaluation order (outputs need only agree within the tolerances stated).

## 10. Open questions and unspecified quirks (implementer's choice, must still satisfy sections 7-8)

1. sliceCount = 0: a missing slice value in input data arrives as 0. Choose between treating it as one slice or as the whole shape; either is acceptable. No test depends on it (sentinel is negative; 1..n are specified).
2. Strength of rounding on the inner contour of a ring: authored content does not clearly depend on it. The inner corner tangent distance must lie between r x D(n) x R x (1 - t)^2 and r x D(n) x R x (1 - t) (between quadratic and linear scaling in (1 - t)); inner rounding must keep the inner contour convex and inside the outer contour. A proportionally scaled copy of the outer contour is the cleanest choice.
3. Non-finite roundness/thickness inputs: any finite in-range substitute (suggest 0 for roundness; suggest 1 for thickness) is acceptable.
4. Thickness = 0: the shape is invisible. Whether the returned geometry is a minimal degenerate triangle or a full zero-width ring is free, but at least 3 valid in-range indices must be returned.
5. Ring width at corners vs edges: with the contour scaling prescribed in 5.3 the band is slightly thinner perpendicular to edges (apothem-based) than along corner rays, by factor cos(pi/n); this is accepted and not to be "corrected".
6. Abrupt rounding cut-off between n = 12 and n = 13 and non-monotonic absolute rounding between n = 3 and n = 4 (because R differs) look like artefacts but are part of the authored-content contract (section 6); do not smooth them.
7. At the cut ends of rounded sliced shapes the outer contour's end corners remain sharp while neighbouring interior corners are rounded; this asymmetry is intentional compatibility behaviour, but if the first or last interior corner's curve interacts oddly with a very small k (e.g. k = 2), any visually sensible result inside the tolerances of section 6 is acceptable.
