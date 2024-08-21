#pragma once

// The main function of a post-processing pass, which calls the pass's postProcess. See PostProcessing.glsl.

#ifdef RALSEI_GLES

void main() {
    oFragColor = postProcess(vUv, vec4(0.0));
}

#else

void main() {
    ivec2 coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(uOutputImage);

    // Skip if out of bounds
    if (coords.x >= size.x || coords.y >= size.y)
        return;

    vec2 uv = vec2(coords.x + 0.5, coords.y + 0.5) / vec2(size);

#ifdef RALSEI_POST_READS_OUTPUT
    vec4 previous = imageLoad(uOutputImage, coords);
#else
    vec4 previous = vec4(0.0);
#endif

    imageStore(uOutputImage, coords, postProcess(uv, previous));
}

#endif
