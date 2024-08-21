#pragma once

#include "Glyph.glsl"

// Coverage of a glyph's curves at a point, after the Slug algorithm.
//
// The includer defines how the glyph data is read before including this file, as expressions
// (a buffer in the desktop backends, textures in GL ES):
//
//   RALSEI_FETCH_SHAPE_ENTRY(i)   ShapeEntry
//   RALSEI_FETCH_BAND_ENTRY(i)    BandEntry
//   RALSEI_FETCH_CURVE_INDEX(i)   int
//   RALSEI_FETCH_CURVE(i)         QuadraticCurve

uint calcRootCode(float y1, float y2, float y3) {
    uint i1 = floatBitsToUint(y1) >> 31u;
    uint i2 = floatBitsToUint(y2) >> 30u;
    uint i3 = floatBitsToUint(y3) >> 29u;
    uint shift = (i2 & 2u) | (i1 & ~2u);
    shift = (i3 & 4u) | (shift & ~4u);
    return (0x2E74u >> shift) & 0x0101u;
}

vec2 solveHoriz(vec2 p0, vec2 p1, vec2 p2) {
    float ay = p0.y - 2.0 * p1.y + p2.y;
    float by = p0.y - p1.y;
    float ax = p0.x - 2.0 * p1.x + p2.x;
    float bx = p0.x - p1.x;

    float d = sqrt(max(by * by - ay * p0.y, 0.0));
    float t1 = abs(ay) < 1.0/65536.0 ? p0.y / (2.0 * by) : (by - d) / ay;
    float t2 = abs(ay) < 1.0/65536.0 ? t1                : (by + d) / ay;

    return vec2(
        (ax * t1 - 2.0 * bx) * t1 + p0.x,
        (ax * t2 - 2.0 * bx) * t2 + p0.x);
}

vec2 solveVert(vec2 p0, vec2 p1, vec2 p2) {
    float ax = p0.x - 2.0 * p1.x + p2.x;
    float bx = p0.x - p1.x;
    float ay = p0.y - 2.0 * p1.y + p2.y;
    float by = p0.y - p1.y;

    float d = sqrt(max(bx * bx - ax * p0.x, 0.0));
    float t1 = abs(ax) < 1.0/65536.0 ? p0.x / (2.0 * bx) : (bx - d) / ax;
    float t2 = abs(ax) < 1.0/65536.0 ? t1                : (bx + d) / ax;

    return vec2(
        (ay * t1 - 2.0 * by) * t1 + p0.y,
        (ay * t2 - 2.0 * by) * t2 + p0.y);
}

float calcCoverage(float xcov, float ycov, float xwgt, float ywgt) {
    float coverage = max(
        abs(xcov * xwgt + ycov * ywgt) / max(xwgt + ywgt, 1.0/65536.0),
        min(abs(xcov), abs(ycov)));
    return clamp(coverage, 0.0, 1.0);
}

vec4 evaluateGlyph(int shapeIndex, vec2 texCoord) {
    ShapeEntry shapeEntry = RALSEI_FETCH_SHAPE_ENTRY(shapeIndex);

    vec2 emsPerPixel = fwidth(texCoord);
    vec2 pixelsPerEm = 1.0 / emsPerPixel;

    int bandX = clamp(int(texCoord.x * shapeEntry.verticalBandScale   + shapeEntry.verticalBandOffset),   0, shapeEntry.verticalBandEntryCount   - 1);
    int bandY = clamp(int(texCoord.y * shapeEntry.horizontalBandScale + shapeEntry.horizontalBandOffset), 0, shapeEntry.horizontalBandEntryCount - 1);

    float xcov = 0.0;
    float xwgt = 0.0;

    BandEntry hband = RALSEI_FETCH_BAND_ENTRY(shapeEntry.horizontalBandEntryBaseIndex + bandY);
    for (int i = 0; i < hband.curveIndexCount; i++) {
        int curveIdx = RALSEI_FETCH_CURVE_INDEX(hband.curveIndexBaseIndex + i);
        QuadraticCurve c = RALSEI_FETCH_CURVE(curveIdx);

        vec2 p0 = c.p0 - texCoord;
        vec2 p1 = c.p1 - texCoord;
        vec2 p2 = c.p2 - texCoord;

        if (max(max(p0.x, p1.x), p2.x) * pixelsPerEm.x < -0.5) break;

        uint code = calcRootCode(p0.y, p1.y, p2.y);
        if (code != 0u) {
            vec2 r = solveHoriz(p0, p1, p2) * pixelsPerEm.x;
            if ((code & 1u) != 0u) {
                xcov += clamp(r.x + 0.5, 0.0, 1.0);
                xwgt = max(xwgt, clamp(1.0 - abs(r.x) * 2.0, 0.0, 1.0));
            }
            if (code > 1u) {
                xcov -= clamp(r.y + 0.5, 0.0, 1.0);
                xwgt = max(xwgt, clamp(1.0 - abs(r.y) * 2.0, 0.0, 1.0));
            }
        }
    }

    float ycov = 0.0;
    float ywgt = 0.0;

    BandEntry vband = RALSEI_FETCH_BAND_ENTRY(shapeEntry.verticalBandEntryBaseIndex + bandX);
    for (int i = 0; i < vband.curveIndexCount; i++) {
        int curveIdx = RALSEI_FETCH_CURVE_INDEX(vband.curveIndexBaseIndex + i);
        QuadraticCurve c = RALSEI_FETCH_CURVE(curveIdx);

        vec2 p0 = c.p0 - texCoord;
        vec2 p1 = c.p1 - texCoord;
        vec2 p2 = c.p2 - texCoord;

        if (max(max(p0.y, p1.y), p2.y) * pixelsPerEm.y < -0.5) break;

        uint code = calcRootCode(p0.x, p1.x, p2.x);
        if (code != 0u) {
            vec2 r = solveVert(p0, p1, p2) * pixelsPerEm.y;
            if ((code & 1u) != 0u) {
                ycov -= clamp(r.x + 0.5, 0.0, 1.0);
                ywgt = max(ywgt, clamp(1.0 - abs(r.x) * 2.0, 0.0, 1.0));
            }
            if (code > 1u) {
                ycov += clamp(r.y + 0.5, 0.0, 1.0);
                ywgt = max(ywgt, clamp(1.0 - abs(r.y) * 2.0, 0.0, 1.0));
            }
        }
    }

    float coverage = calcCoverage(xcov, ycov, xwgt, ywgt);

    // Per-shape color from the font's own ShapeEntry (RGBA8 packed as uint)
    vec4 shapeColor = vec4(
        float((shapeEntry.color       ) & 0xFFu) / 255.0,
        float((shapeEntry.color >>  8u) & 0xFFu) / 255.0,
        float((shapeEntry.color >> 16u) & 0xFFu) / 255.0,
        float((shapeEntry.color >> 24u) & 0xFFu) / 255.0);

    return vec4(1.0, 1.0, 1.0, coverage) * shapeColor;
}

