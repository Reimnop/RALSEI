#pragma once

#include "Prelude.glsl"

// How a draw says which texture it samples, which the vertex and the fragment shaders share (the sampling is in Texturing.glsl).
//
// A draw says which texture it samples with a RalseiTexture, which depends on how the backend can have textures at its disposal:
//
//  * Bindless (RALSEI_BINDLESS), where any texture can be read by any draw. In OpenGL (GL_ARB_bindless_texture) a texture is the
//    64-bit handle of the texture, in a uvec2. In Vulkan (descriptor indexing) it is the index of the texture in the one array
//    of all the textures, in an int.
//  * Paged, where only RALSEI_TEXTURE_PAGE_SIZE textures can be bound together. A texture is the slot (an int) it has in the page
//    the draw is drawn with: the host divides the draws into pages, and binds the page's textures to the slots before it draws it.
//
// Either way a draw without a texture has RALSEI_NO_TEXTURE, and its texture flags (TEXTURE_CRISP) come from the texture's sampling.

// Set in the flags of a texture that is sampled with crisp edges: the sampler blends linearly, but only across the width of one
// screen pixel at the edge between two texels, so pixel art stays sharp and its edges stay smooth at any scale.
#define TEXTURE_CRISP 1

#if defined(RALSEI_BINDLESS) && defined(RALSEI_OPENGL)

#define RalseiTexture uvec2
#define RALSEI_NO_TEXTURE uvec2(0u)

// The two ints of a draw item are the handle's low and high halves
RalseiTexture textureRef(int first, int second) { return uvec2(uint(first), uint(second)); }

#else

#ifndef RALSEI_BINDLESS
#ifndef RALSEI_TEXTURE_PAGE_SIZE
#error "RALSEI_TEXTURE_PAGE_SIZE must be defined (at most 16), or RALSEI_BINDLESS"
#endif
#endif

#define RalseiTexture int
#define RALSEI_NO_TEXTURE (-1)

RalseiTexture textureRef(int first, int second) { return first; }

#endif
