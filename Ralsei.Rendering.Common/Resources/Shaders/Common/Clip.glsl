#pragma once

// A draw's clip. Draws with different clips are drawn together, so it is not the scissor's.
//
// The clip is the part of the pass's viewport the draw shows in, in pixels counted from its bottom-left corner: left and bottom, then
// right and top, each pair as two 16-bit halves of one uint (x in the low half). The host packs it (ClipBounds.Pack); a draw with no
// clip is shown everywhere.
//
// With RALSEI_SHADER_CLIP_DISTANCE (defined by the host where the API has clip distances) the vertex shader cuts to it by writing clip
// distances. Without, it passes the pixel along and the fragment shader discards.

// The clip as left, bottom, right, top
vec4 unpackClip(uvec2 clip) {
    return vec4(clip.x & 0xFFFFu, clip.x >> 16, clip.y & 0xFFFFu, clip.y >> 16);
}

// Where a position (w is 1) is in the viewport, in pixels
vec2 viewportPixel(vec4 position, vec2 viewportSize) {
    return (position.xy * 0.5 + 0.5) * viewportSize;
}
