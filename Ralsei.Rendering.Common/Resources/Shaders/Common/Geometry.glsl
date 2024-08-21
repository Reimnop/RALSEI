#pragma once

#include "Prelude.glsl"

mat3x2 mult3x2(mat3x2 a, mat3x2 b) {
    return mat3x2(
        a[0] * b[0].x + a[1] * b[0].y,
        a[0] * b[1].x + a[1] * b[1].y,
        a[0] * b[2].x + a[1] * b[2].y + a[2]);
}

mat2 jacobian(mat2 m) {
    float det = determinant(m);
    if (abs(det) < 1.0/65536.0) return mat2(1.0);
    return mat2(m[1][1], -m[0][1], -m[1][0], m[0][0]) / det;
}

// Maps the item's depth value from [0, 1) to what gl_Position.z needs. OpenGL's clip space is [-1, 1],
// which its default depth range transform maps to [0, 1] (z * 0.5 + 0.5), so z is used as is. Vulkan's is already
// [0, 1], so do the same mapping here to land in the upper half of the depth range like OpenGL does. This keeps
// the very first draw (z == 0) distinguishable from the cleared depth buffer when using a GREATER depth test.
float toDepth(float z) {
#ifdef RALSEI_VULKAN
    return z * 0.5 + 0.5;
#else
    return z;
#endif
}

// The texture coordinate of a mesh vertex, which the gradient colors are mapped over.
vec2 gradientUv(vec2 position, float rotation, float scale) {
    float c = cos(rotation) * scale;
    float s = sin(rotation) * scale;
    mat2 uvTransform = mat2(c, -s, s, c);

    return uvTransform * position + vec2(0.5);
}
