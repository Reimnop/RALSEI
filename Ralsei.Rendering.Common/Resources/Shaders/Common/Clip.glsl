#pragma once

// A draw's clip, which the fragment shaders cut it to. Draws with different clips are drawn together, so it is not the scissor's.
//
// The clip is the part of the target the draw shows in, in pixels counted from the corner the target's rows start at (as
// gl_FragCoord is): left and top, then right and bottom, each pair as two 16-bit halves of one uint (x in the low half). The host
// packs it (ClipBounds.Pack); a draw with no clip is shown everywhere.

bool isOutsideClip(uvec2 clip) {
    uvec2 pixel = uvec2(gl_FragCoord.xy);
    return pixel.x < (clip.x & 0xFFFFu) || pixel.y < (clip.x >> 16) || pixel.x >= (clip.y & 0xFFFFu) || pixel.y >= (clip.y >> 16);
}
