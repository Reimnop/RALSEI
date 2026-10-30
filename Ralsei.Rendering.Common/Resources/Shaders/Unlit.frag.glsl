// OpenGL ES only

#include "Common/Prelude.glsl"
#include "Common/Color.glsl"
#include "Common/Alpha.glsl"
#include "Common/Texturing.glsl"
#include "Common/Clip.glsl"

layout(location = 0) out vec4 oFragColor;

in vec2 vUv;
flat in vec4 vColor1;
flat in vec4 vColor2;
flat in vec4 vFog;
flat in int vTexture;
flat in int vTextureFlags;
flat in int vRenderMode;
flat in uvec2 vClip;

void main() {
    if (isOutsideClip(vClip))
        discard;

    if (vRenderMode != 0) {
        oFragColor = clamp(premultiply(getColor(vColor1, vColor2, vRenderMode, vUv)), 0.0, 1.0);
        return;
    }

    oFragColor = clamp(shadeTexture(vColor1, vFog, vTexture, vTextureFlags, vUv), 0.0, 1.0);
}
