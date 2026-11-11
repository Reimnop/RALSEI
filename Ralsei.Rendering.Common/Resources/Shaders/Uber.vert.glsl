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
    uint clipMin;
    uint clipMax;
};

struct TextDrawItem {
    mat3x2 mvp;
    float z;
    int glyphOffset;
    vec4 color;
    uint clipMin;
    uint clipMax;
};

struct RenderGlyph {
    vec4 color;
    mat3x2 transform;
    int shapeEntryIndex;
};

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(vec2, uViewportSize)
#ifndef RALSEI_GLES
// Index of the first multi-draw item of the current vkCmdDrawIndexedIndirect / glMultiDrawElementsIndirect call.
// gl_DrawID restarts from 0 for every call, and the draws of a texture page are one, so this offsets it into the shared buffer.
RALSEI_PARAM(uint, uDrawBase)
#endif
RALSEI_PARAMS_END

#ifdef RALSEI_GLES

#include "Common/GlyphTextures.glsl"

// The draw item of the instance, as the host's GpuDrawItem lays it out
layout(location = 2) in vec4 aMvp1;             // the first two columns of the transform
layout(location = 3) in vec4 aMvp2;             // the last column of the transform, the depth and the gradient's rotation
layout(location = 4) in vec4 aColor1;           // for a glyph, the glyph's colour times the text's
layout(location = 5) in vec4 aColor2;           // for a glyph, its transform: m11 m12 m21 m22 (m31 and m32 are in aUvRect)
layout(location = 6) in vec4 aFog;              // the colour in red, green and blue and how much of it to mix in as alpha
layout(location = 7) in vec4 aUvRect;           // the texture region: min u, min v, max u, max v
layout(location = 8) in float aGradientScale;
layout(location = 9) in ivec3 aTextureAndMode;  // the texture slot (a glyph's shape entry), the render mode (0 for no gradient) and the flags
layout(location = 10) in uvec2 aClip;           // where the draw shows, see Clip.glsl

// Set in the flags of the item (the host's GpuDrawItem), with TEXTURE_CRISP
#define GLYPH 2     // the item is a glyph of a text, not a mesh
#define QUAD 4      // the mesh is the sprite quad, and the vertices are those of the builtin quad that it is drawn as an instance of

MultiDrawItem fetchItem() {
    return MultiDrawItem((aTextureAndMode.z & GLYPH) != 0 ? 1 : 0, 0);
}

MeshDrawItem fetchMesh(MultiDrawItem item) {
    MeshDrawItem mesh;
    mesh.mvp = mat3x2(aMvp1.x, aMvp1.y, aMvp1.z,
                      aMvp1.w, aMvp2.x, aMvp2.y);
    mesh.z = aMvp2.z;
    mesh.renderMode = aTextureAndMode.y;
    mesh.color1 = aColor1;
    mesh.color2 = aColor2;
    mesh.gradientRotation = aMvp2.w;
    mesh.gradientScale = aGradientScale;
    mesh.minUV = aUvRect.xy;
    mesh.maxUV = aUvRect.zw;
    mesh.texture0 = aTextureAndMode.x;
    mesh.texture1 = 0;
    mesh.fog = aFog;
    mesh.textureFlags = aTextureAndMode.z & TEXTURE_CRISP;
    mesh.clipMin = aClip.x;
    mesh.clipMax = aClip.y;
    return mesh;
}

void fetchText(MultiDrawItem item, out TextDrawItem text, out RenderGlyph glyph) {
    text.mvp = mat3x2(aMvp1.x, aMvp1.y, aMvp1.z,
                      aMvp1.w, aMvp2.x, aMvp2.y);
    text.z = aMvp2.z;
    text.glyphOffset = 0;
    text.color = aColor1;
    text.clipMin = aClip.x;
    text.clipMax = aClip.y;

    glyph.color = vec4(1.0);
    glyph.transform = mat3x2(aColor2.x, aColor2.y, aColor2.z, aColor2.w, aUvRect.x, aUvRect.y);
    glyph.shapeEntryIndex = aTextureAndMode.x;
}

// The corner of the sprite quad that a vertex of the builtin quad is: the quad is at -0.5 to 0.5, and its texture's v runs the other way
void meshCorner(out vec2 position, out vec2 uv) {
    if ((aTextureAndMode.z & QUAD) != 0) {
        position = aUv - vec2(0.5);
        uv = vec2(aUv.x, 1.0 - aUv.y);
    } else {
        position = aPos;
        uv = aUv;
    }
}

#else

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

#define RALSEI_DRAW_INDEX (int(uDrawBase) + gl_DrawID)

#define RALSEI_FETCH_SHAPE_ENTRY(i) shapeEntries[i]

MultiDrawItem fetchItem() {
    return multiDrawItems[RALSEI_DRAW_INDEX];
}

MeshDrawItem fetchMesh(MultiDrawItem item) {
    return meshDrawItems[item.drawId + RALSEI_INSTANCE_INDEX];
}

void fetchText(MultiDrawItem item, out TextDrawItem text, out RenderGlyph glyph) {
    text = textDrawItems[item.drawId];
    glyph = glyphs[text.glyphOffset + RALSEI_INSTANCE_INDEX];
}

void meshCorner(out vec2 position, out vec2 uv) {
    position = aPos;
    uv = aUv;
}

#endif

void main() {
    MultiDrawItem item = fetchItem();

    vColor1 = vec4(0.0);
    vColor2 = vec4(0.0);
    vTexCoord = vec2(0.0);
    vRenderMode = 0;
    vShapeIndex = -1;
    vRenderType = item.renderType;
    vTexture = RALSEI_NO_TEXTURE;
    vFog = vec4(0.0);
    vTextureFlags = 0;
    vClip = uvec2(0u, 0xFFFFFFFFu);

    if (item.renderType == 1) { // text path
        TextDrawItem text;
        RenderGlyph glyph;
        fetchText(item, text, glyph);

        vColor1 = text.color * glyph.color;
        vShapeIndex = glyph.shapeEntryIndex;
        vClip = uvec2(text.clipMin, text.clipMax);

        mat3x2 finalMvp = mult3x2(text.mvp, glyph.transform);

        vec2 corner = aUv;

        if (glyph.shapeEntryIndex >= 0) {
            // Real glyph: place the unit quad over the shape entry's local em-bounds.
            vec2 clipPos;
            placeGlyphCorner(RALSEI_FETCH_SHAPE_ENTRY(glyph.shapeEntryIndex), finalMvp, corner, uViewportSize, clipPos, vTexCoord);

            gl_Position = vec4(clipPos, toDepth(text.z), 1.0);
        } else {
            // Mark rectangle: the glyph's transform alone maps the unit quad to world.
            vec2 clipPos = vec2(finalMvp * vec3(corner, 1.0));
            vTexCoord = corner;
            gl_Position = vec4(clipPos, toDepth(text.z), 1.0);
        }
    } else { // mesh path
        MeshDrawItem mesh = fetchMesh(item);

        vec2 position, uv;
        meshCorner(position, uv);

        if (mesh.renderMode != 0) {
            // A gradient runs over the mesh's own coordinates, turned and stretched as the object asks.
            vTexCoord = gradientUv(position, mesh.gradientRotation, mesh.gradientScale);
        } else {
            vTexCoord = mix(mesh.minUV, mesh.maxUV, uv);
            vTexture = textureRef(mesh.texture0, mesh.texture1);
            vFog = mesh.fog;
            vTextureFlags = mesh.textureFlags;
        }

        vColor1 = mesh.color1;
        vColor2 = mesh.color2;
        vRenderMode = mesh.renderMode;
        vClip = uvec2(mesh.clipMin, mesh.clipMax);

        gl_Position = vec4(vec2(mesh.mvp * vec3(position, 1.0)), toDepth(mesh.z), 1.0);
    }
}
