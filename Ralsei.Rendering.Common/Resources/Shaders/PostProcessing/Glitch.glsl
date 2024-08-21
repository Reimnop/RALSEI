#include "Common/Alpha.glsl"
#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uTexture, 1)
RALSEI_SAMPLER2D(uNoiseTexture, 2)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uIntensity)
RALSEI_PARAM(float, uColorIntensity)
RALSEI_PARAMS_END

vec4 postProcess(vec2 uv, vec4 previous) {
    vec3 glitch = texture(uNoiseTexture, uv).rgb;
    // pow is undefined for a negative base
    float activeAmount = pow(max(uIntensity, 0.0), 1.8);
    float thresh = 1.0 - activeAmount;
    float w_c = step(thresh * 0.99, glitch.z);
    float w_d = step(thresh, glitch.z);
    float scrambleScale = mix(0.04, 0.30, uIntensity);
    vec2 offset = (glitch.xy - 0.5) * scrambleScale;
    vec2 uv2 = fract(uv + offset * w_d);
    vec4 source = texture(uTexture, uv2);
    vec3 color = unpremultiplyRgb(source);
    vec3 neg = clamp(color.grb + (1.0 - dot(color, vec3(1.0))) * 0.1, 0.0, 1.0);
    color = mix(color, neg, w_c);

    return vec4(color * source.a, source.a);
}

#include "Common/PostProcessingMain.glsl"
