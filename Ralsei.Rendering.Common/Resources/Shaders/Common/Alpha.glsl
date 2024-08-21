#pragma once

#include "Prelude.glsl"

vec4 premultiply(vec4 color) {
    return vec4(color.rgb * color.a, color.a);
}

vec3 unpremultiplyRgb(vec4 color) {
    return color.a > 0.0 ? color.rgb / color.a : vec3(0.0);
}
