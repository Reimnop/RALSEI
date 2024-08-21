// A triangle that covers the whole viewport, for the post-processing passes that are fragment shaders.
// OpenGL ES only.

#include "Common/Prelude.glsl"

const vec2 VERTICES[] = vec2[](
    vec2(0.0, 0.0),
    vec2(2.0, 0.0),
    vec2(0.0, 2.0)
);

out vec2 vUv;

void main() {
    vUv = VERTICES[gl_VertexID];
    gl_Position = vec4(VERTICES[gl_VertexID] * 2.0 - 1.0, 0.0, 1.0);
}
