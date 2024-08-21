#include "Common/PostProcessing.glsl"

const float EPSILON = 0.0001;

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uThreshold)
RALSEI_PARAM(float, uKnee)
RALSEI_PARAMS_END

// "Borrowed" from Unity URP
vec3 applyThreshold(vec3 color, float threshold, float knee) {
    float brightness = max(max(color.r, color.g), color.b);
    float softness = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
    softness = softness * softness / (4.0 * knee + EPSILON);
    float multiplier = max(brightness - threshold, softness) / max(brightness, EPSILON);
    color *= multiplier;
    return color;
}

// Unity blooms in gamma space, then composites it with the linear space scene texture.
// This makes zero sense whatsoever but it is what it is.        
vec4 postProcess(vec2 uv, vec4 previous) {
    vec3 color = texture(uSourceSampler, uv).rgb;
    color = applyThreshold(color, uThreshold, uKnee);

    // Negative values would break the blur
    return vec4(max(color, vec3(0.0)), 1.0);
}

#include "Common/PostProcessingMain.glsl"
