// Lays a source image over what the target has: the passes of a frame are layered with this, the first on an empty target.
// Both are premultiplied, so this is the "over" operator with the source's alpha.

#define RALSEI_POST_READS_OUTPUT
#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uOpacity) // how much of the source shows: 1 for all of it
RALSEI_PARAMS_END

// OpenGL ES can not read the target, so there previous is 0 and the result is what the host blends into the target with
// One, OneMinusSrcAlpha, which comes to the same.
vec4 postProcess(vec2 uv, vec4 previous) {
    vec4 source = texture(uSourceSampler, uv) * uOpacity;
    return source + previous * (1.0 - source.a);
}

#include "Common/PostProcessingMain.glsl"
