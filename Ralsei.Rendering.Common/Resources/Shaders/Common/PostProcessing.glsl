#pragma once

// Post-processing passes, written once for the backends that do them differently:
//
//   OpenGL and Vulkan  compute shaders that write an image
//   OpenGL ES          fragment shaders that draw a triangle covering the target (PostProcess.vert.glsl)
//
// A pass includes this file first, declares what it uses with RALSEI_POST_OUTPUT, RALSEI_SAMPLER2D and RALSEI_PARAM,
// defines
//
//   vec4 postProcess(vec2 uv, vec4 previous)
//
// that returns the color of the pixel at uv, and ends with an include of PostProcessingMain.glsl.
// RALSEI_POST_OUTPUT_SIZE() is the size of the target in pixels.
//
// previous is the color that is in the target already, but only for a pass that defines RALSEI_POST_READS_OUTPUT
// before including this file. Everywhere else it is 0. OpenGL ES can not read its target, so such a pass returns
// what to add to it instead, and the host blends its result into the target with additive blending.
//
// Samplers use binding 1 and up, the output is binding 0.

#include "Prelude.glsl"

#ifdef RALSEI_GLES

in vec2 vUv;
layout(location = 0) out vec4 oFragColor;

#define RALSEI_POST_OUTPUT(format)

// There is no image to ask for its size, so the host says how large the target is
uniform ivec2 uOutputSize;

#define RALSEI_POST_OUTPUT_SIZE() uOutputSize

#else

layout(local_size_x = 8, local_size_y = 8) in;

#ifdef RALSEI_VULKAN
#ifdef RALSEI_POST_READS_OUTPUT
#define RALSEI_POST_OUTPUT(format) layout(set = 0, binding = 0, format) uniform image2D uOutputImage;
#else
#define RALSEI_POST_OUTPUT(format) layout(set = 0, binding = 0, format) uniform writeonly image2D uOutputImage;
#endif
#else
#define RALSEI_POST_OUTPUT(format) layout(format, binding = 0) uniform image2D uOutputImage;
#endif

// The size of the target, as an ivec2. A macro because RALSEI_POST_OUTPUT declares the image after this.
#define RALSEI_POST_OUTPUT_SIZE() imageSize(uOutputImage)

#endif
