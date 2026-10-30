// OpenGL and Vulkan only

#include "Common/Prelude.glsl"
#include "Common/Geometry.glsl"
#include "Common/Glyph.glsl"
#include "Common/TextureRef.glsl"
#include "Common/GlyphVertex.glsl"

layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;

RALSEI_LOCATION(0) out vec4 vColor1;
RALSEI_LOCATION(1) out vec4 vColor2;
RALSEI_LOCATION(2) out vec2 vTexCoord;
RALSEI_LOCATION(3) flat out int vRenderMode;
RALSEI_LOCATION(4) flat out int vRenderType;
RALSEI_LOCATION(5) flat out int vShapeIndex;
RALSEI_LOCATION(6) flat out RalseiTexture vTexture;
RALSEI_LOCATION(7) flat out vec4 vFog;
RALSEI_LOCATION(8) flat out int vTextureFlags;
RALSEI_LOCATION(9) flat out uvec2 vClip;

struct MultiDrawItem {
    int renderType;
    int drawId;
    // The part of the target the draw shows in, in pixels from the corner the target's rows start at: left and top, then
    // right and bottom, each pair as two 16-bit halves of one uint (x in the low half). Fragments outside it are not drawn.
    uint clipMin;
    uint clipMax;
};

struct MeshDrawItem {
    mat3x2 mvp;
    float z;
    int renderMode;
    vec4 color1;
    vec4 color2;
    float gradientRotation;
    float gradientScale;
    vec2 minUV;
    vec2 maxUV;
    int texture0;
    int texture1;
    vec4 fog;
    int textureFlags;
};

struct TextDrawItem {
    mat3x2 mvp;
    float z;
    int glyphOffset;
    vec4 color;
};

struct RenderGlyph {
    vec4 color;
    mat3x2 transform;
    int shapeEntryIndex;
};

RALSEI_STORAGE_BUFFER(0) MultiDrawBuffer {
    MultiDrawItem multiDrawItems[];
};

RALSEI_STORAGE_BUFFER(1) MeshDrawBuffer {
    MeshDrawItem meshDrawItems[];
};

RALSEI_STORAGE_BUFFER(2) TextDrawBuffer {
    TextDrawItem textDrawItems[];
};

RALSEI_STORAGE_BUFFER(3) GlyphBuffer {
    RenderGlyph glyphs[];
};

RALSEI_STORAGE_BUFFER(7) ShapeEntryBuffer {
    ShapeEntry shapeEntries[];
};

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(vec2, uViewportSize)
// Index of the first multi-draw item of the current vkCmdDrawIndexedIndirect / glMultiDrawElementsIndirect call.
// gl_DrawID restarts from 0 for every call, and the draws of a texture page are one, so this offsets it into the shared buffer.
RALSEI_PARAM(uint, uDrawBase)
RALSEI_PARAMS_END

#define RALSEI_DRAW_INDEX (int(uDrawBase) + gl_DrawID)

void main() {
    MultiDrawItem item = multiDrawItems[RALSEI_DRAW_INDEX];

    vColor1 = vec4(0.0);
    vColor2 = vec4(0.0);
    vTexCoord = vec2(0.0);
    vRenderMode = 0;
    vShapeIndex = -1;
    vRenderType = item.renderType;
    vTexture = RALSEI_NO_TEXTURE;
    vFog = vec4(0.0);
    vTextureFlags = 0;
    vClip = uvec2(item.clipMin, item.clipMax);

    if (item.renderType == 1) { // text path
        TextDrawItem text = textDrawItems[item.drawId];
        RenderGlyph glyph = glyphs[text.glyphOffset + RALSEI_INSTANCE_INDEX];

        vColor1 = text.color * glyph.color;
        vShapeIndex = glyph.shapeEntryIndex;

        mat3x2 finalMvp = mult3x2(text.mvp, glyph.transform);

        vec2 corner = aUv;

        if (glyph.shapeEntryIndex >= 0) {
            // Real glyph: place the unit quad over the shape entry's local em-bounds.
            vec2 clipPos;
            placeGlyphCorner(shapeEntries[glyph.shapeEntryIndex], finalMvp, corner, uViewportSize, clipPos, vTexCoord);

            gl_Position = vec4(clipPos, toDepth(text.z), 1.0);
        } else {
            // Mark rectangle: the glyph's transform alone maps the unit quad to world.
            vec2 clipPos = vec2(finalMvp * vec3(corner, 1.0));
            vTexCoord = corner;
            gl_Position = vec4(clipPos, toDepth(text.z), 1.0);
        }
    } else { // mesh path
        MeshDrawItem mesh = meshDrawItems[item.drawId + RALSEI_INSTANCE_INDEX];
        if (mesh.renderMode != 0) {
            // A gradient runs over the mesh's own coordinates, turned and stretched as the object asks.
            vTexCoord = gradientUv(aPos, mesh.gradientRotation, mesh.gradientScale);
        } else {
            vTexCoord = mix(mesh.minUV, mesh.maxUV, aUv);
            vTexture = textureRef(mesh.texture0, mesh.texture1);
            vFog = mesh.fog;
            vTextureFlags = mesh.textureFlags;
        }

        vColor1 = mesh.color1;
        vColor2 = mesh.color2;
        vRenderMode = mesh.renderMode;

        gl_Position = vec4(vec2(mesh.mvp * vec3(aPos, 1.0)), toDepth(mesh.z), 1.0);
    }
}
