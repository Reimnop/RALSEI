#include "Common/Color.glsl"
#include "Common/Alpha.glsl"
#include "Common/PostProcessing.glsl"

const float PI = 3.14159265359;

RALSEI_POST_OUTPUT(rgba16f)
RALSEI_SAMPLER2D(uTexture, 1)
        
RALSEI_PARAMS_BEGIN
RALSEI_PARAM_AT(0, float, uHueShiftAngle)

RALSEI_PARAM_AT(4, float, uLensDistortionIntensity)
RALSEI_PARAM_AT(8, vec2, uLensDistortionCenter)

RALSEI_PARAM_AT(16, float, uChromaticAberrationIntensity)

RALSEI_PARAM_AT(20, float, uVignetteIntensity)
RALSEI_PARAM_AT(24, vec2, uVignetteCenter)
RALSEI_PARAM_AT(32, float, uVignetteRoundness)
RALSEI_PARAM_AT(36, float, uVignetteSmoothness)
RALSEI_PARAM_AT(40, int, uVignetteMode) // 0 = rounded, 1 = roundness

RALSEI_PARAM_AT(44, int, uGradientMode)
RALSEI_PARAM_AT(48, vec3, uVignetteColor)
RALSEI_PARAM_AT(60, float, uGradientIntensity)
RALSEI_PARAM_AT(64, vec3, uGradientColor1)
RALSEI_PARAM_AT(80, vec3, uGradientColor2)
RALSEI_PARAM_AT(96, mat2, uGradientRotation)
RALSEI_PARAMS_END

// pow(x, y) is undefined in GLSL when x == 0 and y <= 0, and drivers disagree on what it returns
// (pow(0, 0) is 0 on some, 1 on others). Give those cases the values IEEE 754 and C give them.
float safePow(float x, float y) {
    if (y == 0.0)
        return 1.0;
    if (x == 0.0)
        return y > 0.0 ? 0.0 : 3.402823e38;
    return pow(x, y);
}

vec2 safePow(vec2 x, float y) {
    return vec2(safePow(x.x, y), safePow(x.y, y));
}

// "Borrowed" from https://gist.github.com/mairod/a75e7b44f68110e1576d77419d608786
vec3 hueShift(vec3 color, float hueAdjust) {
    const vec3 kRGBToYPrime = vec3(0.299, 0.587, 0.114);
    const vec3 kRGBToI      = vec3(0.596, -0.275, -0.321);
    const vec3 kRGBToQ      = vec3(0.212, -0.523, 0.311);

    const vec3 kYIQToR = vec3(1.0, 0.956, 0.621);
    const vec3 kYIQToG = vec3(1.0, -0.272, -0.647);
    const vec3 kYIQToB = vec3(1.0, -1.107, 1.704);

    float YPrime  = dot(color, kRGBToYPrime);
    float I       = dot(color, kRGBToI);
    float Q       = dot(color, kRGBToQ);
    // atan(0, 0) is undefined, and grey has no hue (its chroma is 0, so the hue doesn't matter)
    float hue     = (I == 0.0 && Q == 0.0) ? 0.0 : atan(Q, I);
    float chroma  = sqrt(I * I + Q * Q);

    hue += hueAdjust;

    Q = chroma * sin(hue);
    I = chroma * cos(hue);

    vec3 yIQ = vec3(YPrime, I, Q);

    return vec3(dot(yIQ, kYIQToR), dot(yIQ, kYIQToG), dot(yIQ, kYIQToB));
}

// "Borrowed" from Unity's PostProcessing package
vec2 distortLens(vec2 uv) {
    float intensity = uLensDistortionIntensity;
    vec2 center = uLensDistortionCenter;

    if (intensity == 0.0)
        return uv;

    float amount = 1.6 * max(abs(intensity), 1.0);
    float theta = min(160.0, amount) * PI / 180.0;
    float sigma = 2.0 * tan(theta * 0.5);

    vec2 ruv = uv - vec2(0.5) - center;
    float ru = length(ruv);

    // The center is not distorted, and the divisions by ru below are undefined there
    if (ru == 0.0)
        return uv;

    if (intensity > 0.0) {
        float wu = ru * theta;
        ru = tan(wu) * (1.0 / (ru * sigma));
        uv = uv + ruv * (ru - 1.0);
    } else {
        ru = (1.0 / ru) * (1.0 / theta) * atan(ru * sigma);
        uv = uv + ruv * (ru - 1.0);
    }

    return uv;
}

vec3 applyVignetteRounded(vec3 color, vec2 uv, vec2 center, float aspect, float intensity, float roundedness, float smoothness, vec3 vignetteColor) {
    vec2 dist = abs(uv - center) * intensity;
    dist.x *= mix(1.0, aspect, roundedness);
    float vFactor = safePow(clamp(1.0 - dot(dist, dist), 0.0, 1.0), smoothness);
    return color * mix(vignetteColor, vec3(1.0), vFactor);
}

vec3 applyVignetteRoundness(vec3 color, vec2 uv, vec2 center, float intensity, float roundness, float smoothness, vec3 vignetteColor) {
    vec2 dist = abs(uv - center) * intensity;
    dist = safePow(clamp(dist, 0.0, 1.0), roundness);
    float vFactor = safePow(clamp(1.0 - dot(dist, dist), 0.0, 1.0), smoothness);
    return color * mix(vignetteColor, vec3(1.0), vFactor);
}

vec3 applyGradient(vec3 color, vec2 uv, vec3 color1, vec3 color2, float intensity, mat2 rotation, int mode) {
    if (intensity == 0.0)
        return color;

    vec2 uvNormalized = uv - 0.5;
    uvNormalized = rotation * uvNormalized;
    float gradient = uvNormalized.y + 0.5;
    vec3 gradientColor = mix(color2, color1, gradient);

    if (mode == 0) // linear
        return mix(color, gradientColor, intensity);
    else if (mode == 1) // additive
        return color + gradientColor * intensity;
    else if (mode == 2) // multiplicative
        return color * mix(vec3(1.0), gradientColor, intensity);
    else if (mode == 3) // screen
        return vec3(1.0) - (vec3(1.0) - color) * (vec3(1.0) - gradientColor * intensity);
    else // default to linear
        return mix(color, gradientColor, intensity);
}

// The color (linear, without the alpha folded in) of a pixel of the source, and its alpha.
vec4 sampleTexture(sampler2D tex, vec2 uv) {
    vec4 texel = texture(tex, uv);
    vec3 color = unpremultiplyRgb(texel);
    color = applyGradient(color, uv, uGradientColor1, uGradientColor2, uGradientIntensity, uGradientRotation, uGradientMode);

    // gradient should be processed in gamma space, other effects in linear
    color = srgbToLinear(color);
    return vec4(color, texel.a);
}

vec4 postProcess(vec2 uv, vec4 previous) {
    ivec2 size = RALSEI_POST_OUTPUT_SIZE();

    // Apply lens distortion
    vec2 uvDistorted = distortLens(uv);

    // Apply chromatic aberration
    vec3 color;
    float alpha;
    if (uChromaticAberrationIntensity == 0.0) {
        vec4 texel = sampleTexture(uTexture, uvDistorted);
        color = texel.rgb;
        alpha = texel.a;
    } else {
        vec2 coords = uv * 2.0 - 1.0;
        vec2 end = uv - coords * dot(coords, coords) * uChromaticAberrationIntensity;
        vec2 delta = (end - uv) / 3.0;

        float r = sampleTexture(uTexture, uvDistorted).r;
        vec4 green = sampleTexture(uTexture, distortLens(uv + delta));
        float b = sampleTexture(uTexture, distortLens(uv + delta * 2.0)).b;

        color = vec3(r, green.g, b);
        alpha = green.a;
    }

    // Apply vignette
    if (uVignetteIntensity != 0.0) {
        if (uVignetteMode == 0) {
            color = applyVignetteRounded(color, uvDistorted, uVignetteCenter, float(size.x) / float(size.y), uVignetteIntensity, uVignetteRoundness, uVignetteSmoothness, uVignetteColor);
        } else if (uVignetteMode == 1) {
            color = applyVignetteRoundness(color, uvDistorted, uVignetteCenter, uVignetteIntensity, uVignetteRoundness, uVignetteSmoothness, uVignetteColor);
        }
    }

    // Hue shift
    if (uHueShiftAngle != 0.0) {
        color = hueShift(color, uHueShiftAngle);
    }

    color = linearToSrgb(color);

    // Clamp final color
    color = clamp(color, 0.0, 1.0);

    // The effects change the color of what is there, not how much of it there is
    return vec4(color * alpha, alpha);
}

#include "Common/PostProcessingMain.glsl"
