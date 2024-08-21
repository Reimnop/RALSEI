#pragma once

#include "Glyph.glsl"

// Glyph data in textures, for GL ES 3.0, which has no storage buffers. Defines how to read the data for
// GlyphCoverage.glsl, and the sampler uniforms that the host sets.

uniform sampler2D uCurves;
uniform isampler2D uCurveIndices;
uniform isampler2D uBandEntries;
uniform usampler2D uShapeEntries;

const int TEX_WIDTH = 2048;

QuadraticCurve getCurve(int index) {
    int tIndex = index * 3; // 3 texels per curve

    ivec2 coord0 = ivec2(tIndex % TEX_WIDTH, tIndex / TEX_WIDTH);
    vec2 p0 = texelFetch(uCurves, coord0, 0).xy;

    ivec2 coord1 = ivec2((tIndex + 1) % TEX_WIDTH, (tIndex + 1) / TEX_WIDTH);
    vec2 p1 = texelFetch(uCurves, coord1, 0).xy;

    ivec2 coord2 = ivec2((tIndex + 2) % TEX_WIDTH, (tIndex + 2) / TEX_WIDTH);
    vec2 p2 = texelFetch(uCurves, coord2, 0).xy;

    return QuadraticCurve(p0, p1, p2);
}

int getCurveIndex(int index) {
    ivec2 coord = ivec2(index % TEX_WIDTH, index / TEX_WIDTH);
    return texelFetch(uCurveIndices, coord, 0).r;
}

BandEntry getBandEntry(int index) {
    ivec2 coord = ivec2(index % TEX_WIDTH, index / TEX_WIDTH);
    ivec2 data = texelFetch(uBandEntries, coord, 0).xy;
    return BandEntry(data.x, data.y);
}

ShapeEntry getShapeEntry(int index) {
    int tIndex = index * 4; // 4 texels per shape entry

    ivec2 coord0 = ivec2(tIndex % TEX_WIDTH, tIndex / TEX_WIDTH);
    uvec4 se0 = texelFetch(uShapeEntries, coord0, 0);

    ivec2 coord1 = ivec2((tIndex + 1) % TEX_WIDTH, (tIndex + 1) / TEX_WIDTH);
    uvec4 se1 = texelFetch(uShapeEntries, coord1, 0);

    ivec2 coord2 = ivec2((tIndex + 2) % TEX_WIDTH, (tIndex + 2) / TEX_WIDTH);
    uvec4 se2 = texelFetch(uShapeEntries, coord2, 0);

    ivec2 coord3 = ivec2((tIndex + 3) % TEX_WIDTH, (tIndex + 3) / TEX_WIDTH);
    uvec4 se3 = texelFetch(uShapeEntries, coord3, 0);

    return ShapeEntry(
        int(se0.x),
        int(se0.y),
        uintBitsToFloat(se0.z),
        uintBitsToFloat(se0.w),
        int(se1.x),
        int(se1.y),
        uintBitsToFloat(se1.z),
        uintBitsToFloat(se1.w),
        uintBitsToFloat(se2.xy),
        uintBitsToFloat(se2.zw),
        se3.x);
}

#define RALSEI_FETCH_SHAPE_ENTRY(i) getShapeEntry(i)
#define RALSEI_FETCH_BAND_ENTRY(i) getBandEntry(i)
#define RALSEI_FETCH_CURVE_INDEX(i) getCurveIndex(i)
#define RALSEI_FETCH_CURVE(i) getCurve(i)
