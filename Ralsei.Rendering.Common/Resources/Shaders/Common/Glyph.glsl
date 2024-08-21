#pragma once

#include "Prelude.glsl"

struct ShapeEntry {
    int horizontalBandEntryBaseIndex;
    int horizontalBandEntryCount;
    float horizontalBandScale;
    float horizontalBandOffset;
    int verticalBandEntryBaseIndex;
    int verticalBandEntryCount;
    float verticalBandScale;
    float verticalBandOffset;
    vec2 min;
    vec2 max;
    uint color;
};

struct QuadraticCurve {
    vec2 p0;
    vec2 p1;
    vec2 p2;
};

struct BandEntry {
    int curveIndexBaseIndex;
    int curveIndexCount;
};
