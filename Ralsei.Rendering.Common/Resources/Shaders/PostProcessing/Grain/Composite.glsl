#include "Common/Alpha.glsl"
#include "Common/PostProcessing.glsl"

// The OpenGL version used to declare this image as r16f while binding an rgba16f texture to it, which
// GL leaves undefined. The result is a full color, so it is declared as rgba16f to match the bound image.
RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uSourceSampler, 1)
RALSEI_SAMPLER2D(uNoiseSampler, 2)

RALSEI_PARAMS_BEGIN
RALSEI_PARAM(vec2, uScale)
RALSEI_PARAM(vec2, uOffset)
RALSEI_PARAM(int, uColored)
RALSEI_PARAM(float, uLuminanceContribution)
RALSEI_PARAM(float, uIntensity)
RALSEI_PARAMS_END

vec3 gammaToLinear(vec3 color) {
    return color * color;
}

vec3 linearToGamma(vec3 color) {
    // sqrt is undefined for negative values, which a negative grain intensity produces
    return sqrt(max(color, vec3(0.0)));
}

float luminance(vec3 color) {
    return dot(color, vec3(0.2126729, 0.7151522, 0.0721750));
}

vec4 postProcess(vec2 uv, vec4 previous) {
    vec4 source = texture(uSourceSampler, uv);
    vec3 color = gammaToLinear(unpremultiplyRgb(source));

    vec4 noiseRaw = texture(uNoiseSampler, uv * uScale + uOffset);
    vec3 grain = uColored != 0 ? noiseRaw.rgb : vec3(noiseRaw.r);

    float lum = 1.0 - sqrt(luminance(clamp(color, 0.0, 1.0)));
    lum = mix(1.0, lum, uLuminanceContribution);

    color += color * grain * lum * uIntensity;

    color = linearToGamma(color);

    return vec4(color * source.a, source.a);
}

#include "Common/PostProcessingMain.glsl"
