#pragma once

#include "Prelude.glsl"

vec4 getColor(vec4 color1, vec4 color2, int mode, vec2 uv) {
    // mode 0: color1
    // mode 1: gradient 2 to 1 on x
    // mode 2: gradient 1 to 2 on x
    // mode 3: circular gradient outwards
    // mode 4: circular gradient inwards
    // mode 5: a ring; color2 is (inner radius, outer radius, fade) as numbers, in fractions of the half-size
    if (mode == 0) {
        return color1;
    } else if (mode == 1) {
        return mix(color2, color1, uv.x);
    } else if (mode == 2) {
        return mix(color1, color2, uv.x);
    } else if (mode == 3) {
        float dist = min(distance(uv, vec2(0.5)) * 2.0, 1.0);
        return mix(color2, color1, dist);
    } else if (mode == 4) {
        float dist = min(distance(uv, vec2(0.5)) * 2.0, 1.0);
        return mix(color1, color2, dist);
    } else if (mode == 5) {
        float dist = distance(uv, vec2(0.5)) * 2.0;
        float fade = max(color2.z, 0.0001);
        float outside = smoothstep(color2.y, color2.y + fade, dist);
        float inside = smoothstep(color2.x - fade, color2.x, dist);
        return vec4(color1.rgb, color1.a * (inside - outside));
    }
    return color1;
}

float srgbToLinear(float c) {
    float linearRGBLo = c / 12.92;
    float linearRGBHi = pow((c + 0.055) / 1.055, 2.4);
    return (c <= 0.04045) ? linearRGBLo : linearRGBHi;
}

float linearToSrgb(float c) {
    float srgbLo = c * 12.92;
    float srgbHi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return (c <= 0.0031308) ? srgbLo : srgbHi;
}

vec3 srgbToLinear(vec3 c) {
    return vec3(srgbToLinear(c.r), srgbToLinear(c.g), srgbToLinear(c.b));
}

vec3 linearToSrgb(vec3 c) {
    return vec3(linearToSrgb(c.r), linearToSrgb(c.g), linearToSrgb(c.b));
}
