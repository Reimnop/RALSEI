// Draws meshes, many instances of one mesh at a time, with the draw items as per-instance vertex attributes. OpenGL ES only.

#include "Common/Prelude.glsl"
#include "Common/Geometry.glsl"

layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;

out vec2 vUv;
flat out vec4 vColor1;
flat out vec4 vColor2;
flat out vec4 vFog;
flat out int vTexture;
flat out int vTextureFlags;
flat out int vRenderMode;
flat out uvec2 vClip;

// The draw item of the instance, as GpuUnlitDrawItem lays it out
layout(location = 2) in vec4 aMvp1;             // the first two columns of the transform
layout(location = 3) in vec4 aMvp2;             // the last column of the transform, the depth and the gradient's rotation
layout(location = 4) in vec4 aColor1;
layout(location = 5) in vec4 aColor2;
layout(location = 6) in vec4 aFog;              // the colour in red, green and blue and how much of it to mix in as alpha
layout(location = 7) in vec4 aUvRect;           // the texture region: min u, min v, max u, max v
layout(location = 8) in float aGradientScale;
layout(location = 9) in ivec3 aTextureAndMode;  // the texture slot, the render mode (0 for no gradient) and the texture flags
layout(location = 10) in uvec2 aClip;           // where the draw shows, see Clip.glsl

void main() {
    int renderMode = aTextureAndMode.y;

    mat3x2 mvp = mat3x2(aMvp1.x, aMvp1.y, aMvp1.z,
                        aMvp1.w, aMvp2.x, aMvp2.y);

    if (renderMode != 0) {
        // A gradient runs over the mesh's own coordinates, turned and stretched as the object asks.
        vUv = gradientUv(aPos, aMvp2.w, aGradientScale);
    } else {
        vUv = mix(aUvRect.xy, aUvRect.zw, aUv);
    }

    vColor1 = aColor1;
    vColor2 = aColor2;
    vFog = aFog;
    vTexture = aTextureAndMode.x;
    vTextureFlags = aTextureAndMode.z;
    vRenderMode = renderMode;
    vClip = aClip;

    gl_Position = vec4(vec2(mvp * vec3(aPos, 1.0)), toDepth(aMvp2.z), 1.0);
}
