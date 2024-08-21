#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(r16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(vec2, uParams) // x = b, y = c
RALSEI_PARAMS_END

float applyFilter(vec2 uv, vec2 texelSize, float b, float c) {
    return (1.0 / (4.0 + b * 4.0 + abs(c))) * (
        texture(uSourceSampler, uv + vec2(-1.0, -1.0) * texelSize).r +
        texture(uSourceSampler, uv + vec2( 0.0, -1.0) * texelSize).r * b +
        texture(uSourceSampler, uv + vec2( 1.0, -1.0) * texelSize).r +
        texture(uSourceSampler, uv + vec2(-1.0,  0.0) * texelSize).r * b +
        texture(uSourceSampler, uv + vec2( 0.0,  0.0) * texelSize).r * c +
        texture(uSourceSampler, uv + vec2( 1.0,  0.0) * texelSize).r * b +
        texture(uSourceSampler, uv + vec2(-1.0,  1.0) * texelSize).r +
        texture(uSourceSampler, uv + vec2( 0.0,  1.0) * texelSize).r * b +
        texture(uSourceSampler, uv + vec2( 1.0,  1.0) * texelSize).r
    );
}

vec4 postProcess(vec2 uv, vec4 previous) {
    vec2 texelSize = 1.0 / vec2(textureSize(uSourceSampler, 0));
    float n = applyFilter(uv, texelSize, uParams.x, uParams.y);

    return vec4(vec3(n), 1.0);
}

#include "Common/PostProcessingMain.glsl"
