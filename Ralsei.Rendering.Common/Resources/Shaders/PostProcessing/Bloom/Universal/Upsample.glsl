#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uLowMipSampler, 1)
RALSEI_SAMPLER2D(uHighMipSampler, 2)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uScatter)
RALSEI_PARAMS_END

vec4 postProcess(vec2 uv, vec4 previous) {
    // Sample colors
    vec3 lowMipColor = texture(uLowMipSampler, uv).rgb;
    vec3 highMipColor = texture(uHighMipSampler, uv).rgb;

    // Mix colors based on scatter
    vec3 color = mix(highMipColor, lowMipColor, uScatter);

    return vec4(color, 1.0);
}

#include "Common/PostProcessingMain.glsl"
