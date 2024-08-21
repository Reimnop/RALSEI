#define RALSEI_POST_READS_OUTPUT
#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(float, uSampleScale)
RALSEI_PARAMS_END

vec3 sampleTent(sampler2D mip, vec2 uv, float sampleScale) {
    vec2 pxSize = 1.0 / vec2(textureSize(mip, 0));
    pxSize *= sampleScale;

    // A B C
    // D E F
    // G H I
    vec3 a = texture(mip, uv + vec2(-1.0, -1.0) * pxSize).rgb;
    vec3 b = texture(mip, uv + vec2( 0.0, -1.0) * pxSize).rgb;
    vec3 c = texture(mip, uv + vec2( 1.0, -1.0) * pxSize).rgb;
    vec3 d = texture(mip, uv + vec2(-1.0,  0.0) * pxSize).rgb;
    vec3 e = texture(mip, uv + vec2( 0.0,  0.0) * pxSize).rgb;
    vec3 f = texture(mip, uv + vec2( 1.0,  0.0) * pxSize).rgb;
    vec3 g = texture(mip, uv + vec2(-1.0,  1.0) * pxSize).rgb;
    vec3 h = texture(mip, uv + vec2( 0.0,  1.0) * pxSize).rgb;
    vec3 i = texture(mip, uv + vec2( 1.0,  1.0) * pxSize).rgb;

    vec3 color =
        (a + c + g + i) * 0.0625 +
        (b + d + f + h) * 0.125 +
        e * 0.25;

    return color;
}

vec4 postProcess(vec2 uv, vec4 previous) {
    vec3 srcColor = sampleTent(uSourceSampler, uv, uSampleScale);

    // Additive blend with current mip level
    vec3 color = previous.rgb + srcColor;

    return vec4(color, 1.0);
}

#include "Common/PostProcessingMain.glsl"
