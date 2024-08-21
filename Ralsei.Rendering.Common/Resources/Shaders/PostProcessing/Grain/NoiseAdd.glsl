#define RALSEI_POST_READS_OUTPUT
#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(int, uChannel)
RALSEI_PARAMS_END

vec4 postProcess(vec2 uv, vec4 previous) {
    vec3 color = previous.rgb;
    float noise = texture(uSourceSampler, uv).r;
    color[uChannel] = noise;

    return vec4(color, 1.0);
}

#include "Common/PostProcessingMain.glsl"
