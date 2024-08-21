#pragma once

#include "Geometry.glsl"
#include "Glyph.glsl"

// Places one corner of a glyph's quad over its shape's local em-bounds.
//
// The quad is dilated by half a pixel in clip space, with the texture coordinate adjusted by the Jacobian
// of the transform, so that the curve coverage in the fragment shader anti-aliases cleanly at the silhouette.
//
//   shapeEntry    the glyph's shape
//   finalMvp      transform from the shape's em space to clip space
//   corner        the corner of the unit quad, (0, 0) to (1, 1)
//   viewportSize  size of the viewport in pixels
//   clipPos       the clip space position of the corner
//   texCoord      the corner's position in em space, which the fragment shader evaluates the shape at
void placeGlyphCorner(ShapeEntry shapeEntry, mat3x2 finalMvp, vec2 corner, vec2 viewportSize, out vec2 clipPos, out vec2 texCoord) {
    vec2 localPos = mix(shapeEntry.min, shapeEntry.max, corner);
    vec2 normal = (corner - vec2(0.5)) * 2.0;       // outward diagonal (-1,-1)..(1,1)

    mat2 linear = mat2(finalMvp[0], finalMvp[1]);
    // normalize is undefined for a zero vector, which a glyph that is scaled to nothing produces
    vec2 linearNormal = linear * normal;
    vec2 clipNormal = dot(linearNormal, linearNormal) > 0.0 ? normalize(linearNormal) : vec2(0.0);
    vec2 pixelSize = 2.0 / viewportSize;
    vec2 dilation = clipNormal * pixelSize * 0.5;

    clipPos = vec2(finalMvp * vec3(localPos, 1.0)) + dilation;

    mat2 j = jacobian(linear);
    texCoord = localPos + j * dilation;
}
