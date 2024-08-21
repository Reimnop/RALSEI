// Draws text, one instance per glyph, with the glyph data in textures. OpenGL ES only.

#include "Common/Prelude.glsl"
#include "Common/Geometry.glsl"
#include "Common/Glyph.glsl"
#include "Common/GlyphVertex.glsl"
#include "Common/GlyphTextures.glsl"

const vec2 VERTICES[6] = vec2[](
    vec2(0.0, 1.0),
    vec2(1.0, 1.0),
    vec2(0.0, 0.0),
    vec2(1.0, 0.0),
    vec2(0.0, 0.0),
    vec2(1.0, 1.0)
);

layout(location = 0) in vec2 aTransformC0;
layout(location = 1) in vec2 aTransformC1;
layout(location = 2) in vec2 aTransformC2;
layout(location = 3) in vec4 aColor;
layout(location = 4) in int aShapeEntryIndex;

uniform mat3x2 uMvp;
uniform float uZ;
uniform vec4 uBaseColor;

// Viewport size for dynamic dilation
uniform vec2 uViewportSize;

out vec2 vTexCoord;
out vec4 vGlyphColor;
flat out int vShapeEntryIndex;

void main() {
    vec2 corner = VERTICES[gl_VertexID];

    mat3x2 instanceTransform = mat3x2(aTransformC0, aTransformC1, aTransformC2);
    mat3x2 finalMvp = mult3x2(uMvp, instanceTransform);

    vGlyphColor = aColor * uBaseColor;
    vShapeEntryIndex = aShapeEntryIndex;

    vec2 clipPos;
    if (aShapeEntryIndex >= 0) {
        placeGlyphCorner(getShapeEntry(aShapeEntryIndex), finalMvp, corner, uViewportSize, clipPos, vTexCoord);
    } else {
        // Mark rectangle: the glyph's transform alone maps the unit quad to world.
        clipPos = vec2(finalMvp * vec3(corner, 1.0));
        vTexCoord = corner;
    }

    gl_Position = vec4(clipPos, toDepth(uZ), 1.0);
}
