#include "Common/Color.glsl"
#include "Common/Alpha.glsl"
#include "Common/PostProcessing.glsl"

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)
RALSEI_SAMPLER2D(uBloomSampler, 2)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(vec3, uTint)
RALSEI_PARAMS_END

vec4 postProcess(vec2 uv, vec4 previous) {
    vec4 src = texture(uSourceSampler, uv);
    vec3 srcColor = srgbToLinear(unpremultiplyRgb(src));

    vec3 bloomColor = texture(uBloomSampler, uv).rgb;
    // bloomColor = srgbToLinear(bloomColor); 

    vec3 glow = bloomColor * uTint;

    // The glow is light, which reaches past what the source covers: it adds to the premultiplied color, and covers what it lights
    // (over the source's own coverage), so that the result is still a premultiplied pixel, whose color has nothing outside its alpha.
    // Where the source is opaque the alpha stays 1 and this is the source plus the glow.
    vec3 emitted = srcColor * src.a + glow;
    // (A source that is opaque stays opaque whatever the glow is, even a glow that is not a number.)
    float alpha = src.a >= 1.0 ? 1.0 : min(1.0, src.a + (1.0 - src.a) * clamp(max(glow.r, max(glow.g, glow.b)), 0.0, 1.0));
    vec3 color = alpha > 0.0 ? emitted / alpha : vec3(0.0);

    // Convert back to gamma space
    color = linearToSrgb(color);

    return vec4(color * alpha, alpha);
}

#include "Common/PostProcessingMain.glsl"
