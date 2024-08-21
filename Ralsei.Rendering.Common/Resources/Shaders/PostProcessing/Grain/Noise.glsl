#include "Common/PostProcessing.glsl"

const vec3 NOISE_PARAMS = vec3(12.9898, 78.233, 43758.5453);

RALSEI_POST_OUTPUT(r16f)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uPhase)
RALSEI_PARAMS_END

float noise(vec2 uv, float phase) {
    vec2 n = uv + phase;
    return fract(sin(dot(n, NOISE_PARAMS.xy)) * NOISE_PARAMS.z);
}

vec4 postProcess(vec2 uv, vec4 previous) {
    float n = noise(uv, uPhase);

    return vec4(vec3(n), 1.0);
}

#include "Common/PostProcessingMain.glsl"
