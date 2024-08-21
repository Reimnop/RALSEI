#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uRadius) // in pixels
RALSEI_PARAMS_END

const int MAX_SAMPLES = 128;
const float GOLDEN_ANGLE = 2.39996323;

vec4 postProcess(vec2 uv, vec4 previous) {
    vec2 texel = 1.0 / vec2(RALSEI_POST_OUTPUT_SIZE());
        
    int count = clamp(int(uRadius * uRadius * 0.9) + 8, 8, MAX_SAMPLES);

    vec4 sum = vec4(0.0);
    for (int i = 0; i < MAX_SAMPLES; i++) {
        if (i >= count)
            break;

        float distance = sqrt((float(i) + 0.5) / float(count)) * uRadius;
        float angle = float(i) * GOLDEN_ANGLE;
        vec2 offset = vec2(cos(angle), sin(angle)) * distance * texel;
        sum += texture(uSourceSampler, uv + offset);
    }

    return sum / float(count);
}

#include "Common/PostProcessingMain.glsl"
