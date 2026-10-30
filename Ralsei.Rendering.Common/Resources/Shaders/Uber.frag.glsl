// OpenGL and Vulkan only

#include "Common/Prelude.glsl"
#include "Common/Color.glsl"
#include "Common/Glyph.glsl"
#include "Common/Alpha.glsl"
#include "Common/Texturing.glsl"

layout(location = 0) out vec4 oFragColor;

RALSEI_LOCATION(0) in vec4 vColor1;
RALSEI_LOCATION(1) in vec4 vColor2;
RALSEI_LOCATION(2) in vec2 vTexCoord;
RALSEI_LOCATION(3) flat in int vRenderMode;
RALSEI_LOCATION(4) flat in int vRenderType;
RALSEI_LOCATION(5) flat in int vShapeIndex;
RALSEI_LOCATION(6) flat in RalseiTexture vTexture;
RALSEI_LOCATION(7) flat in vec4 vFog;
RALSEI_LOCATION(8) flat in int vTextureFlags;
RALSEI_LOCATION(9) flat in uvec2 vClip;

RALSEI_STORAGE_BUFFER(4) CurveBuffer { QuadraticCurve curves[]; };
RALSEI_STORAGE_BUFFER(5) CurveIndexBuffer { int curveIndices[]; };
RALSEI_STORAGE_BUFFER(6) BandEntryBuffer { BandEntry bandEntries[]; };
RALSEI_STORAGE_BUFFER(7) ShapeEntryBuffer { ShapeEntry shapeEntries[]; };

#define RALSEI_FETCH_SHAPE_ENTRY(i) shapeEntries[i]
#define RALSEI_FETCH_BAND_ENTRY(i) bandEntries[i]
#define RALSEI_FETCH_CURVE_INDEX(i) curveIndices[i]
#define RALSEI_FETCH_CURVE(i) curves[i]
#include "Common/GlyphCoverage.glsl"

void main() {
    // The clip of the draw (draws with different clips are one multi-draw, so it is not the scissor's)
    uvec2 pixel = uvec2(gl_FragCoord.xy);
    if (pixel.x < (vClip.x & 0xFFFFu) || pixel.y < (vClip.x >> 16) || pixel.x >= (vClip.y & 0xFFFFu) || pixel.y >= (vClip.y >> 16))
        discard;

    if (vRenderType == 1) {
        // Text path: glyph (vShapeIndex >= 0) or mark rectangle (-1)
        if (vShapeIndex >= 0) {
            vec4 glyphColor = evaluateGlyph(vShapeIndex, vTexCoord);
            oFragColor = premultiply(glyphColor * vColor1);
        } else {
            oFragColor = premultiply(vColor1);
        }
    } else if (vRenderMode != 0) {
        oFragColor = premultiply(getColor(vColor1, vColor2, vRenderMode, vTexCoord));
    } else {
        oFragColor = shadeTexture(vColor1, vFog, vTexture, vTextureFlags, vTexCoord);
    }

    oFragColor = clamp(oFragColor, 0.0, 1.0);
}
