// Draws text, one instance per glyph, with the glyph data in textures. OpenGL ES only.

#include "Common/Prelude.glsl"
#include "Common/Glyph.glsl"
#include "Common/GlyphTextures.glsl"
#include "Common/GlyphCoverage.glsl"
#include "Common/Alpha.glsl"

layout(location = 0) out vec4 oFragColor;

in vec2 vTexCoord;
in vec4 vGlyphColor;
flat in int vShapeEntryIndex;

void main() {
    if (vShapeEntryIndex >= 0) {
        vec4 glyphColor = evaluateGlyph(vShapeEntryIndex, vTexCoord);
        oFragColor = clamp(premultiply(glyphColor * vGlyphColor), 0.0, 1.0);
    } else {
        oFragColor = clamp(premultiply(vGlyphColor), 0.0, 1.0);
    }
}
