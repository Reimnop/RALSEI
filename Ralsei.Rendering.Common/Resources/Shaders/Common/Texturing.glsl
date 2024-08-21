#pragma once

#include "Prelude.glsl"
#include "Alpha.glsl"
#include "TextureRef.glsl"

// Sampling of the textures that draws name (see TextureRef.glsl).

#if defined(RALSEI_BINDLESS) && defined(RALSEI_OPENGL)

#define RALSEI_SAMPLER(texture) sampler2D(texture)

#elif defined(RALSEI_BINDLESS)

// Every texture there is, by index (binding 0 of descriptor set 1 in Vulkan, see the Vulkan renderer)
layout(set = 1, binding = 0) uniform sampler2D uTextures[];

#define RALSEI_SAMPLER(texture) uTextures[nonuniformEXT(texture)]

#else

// The textures of the page. The GL hosts set the texture units of the uniform array, Vulkan binds them all at once
// (binding 0 of descriptor set 1, see the Vulkan renderer)
#ifdef RALSEI_VULKAN
layout(set = 1, binding = 0) uniform sampler2D uTextures[RALSEI_TEXTURE_PAGE_SIZE];
#else
uniform sampler2D uTextures[RALSEI_TEXTURE_PAGE_SIZE];
#endif

#endif

vec4 sampleTexture(sampler2D tex, vec2 uv, int flags) {
    if ((flags & TEXTURE_CRISP) == 0)
        return texture(tex, uv);

    vec2 size = vec2(textureSize(tex, 0));
    vec2 texel = uv * size;

    // How many screen pixels a texel covers, along each axis.
    vec2 scale = 1.0 / max(fwidth(texel), vec2(1.0 / 4096.0));

    // Stay on the texel's centre for as long as it is wider than the blend, and blend across one screen pixel at its edge.
    vec2 region = max(vec2(0.5) - 0.5 / max(scale, vec2(1.0)), vec2(0.0));
    vec2 centreDistance = fract(texel) - 0.5;
    vec2 offset = (centreDistance - clamp(centreDistance, -region, region)) * scale + 0.5;
    return texture(tex, (floor(texel) + clamp(offset, 0.0, 1.0)) / size);
}

// The colour of a texture at a point, or white for a draw that has none.
vec4 getTextureColor(RalseiTexture tex, int flags, vec2 uv) {
#ifdef RALSEI_BINDLESS
    if (tex == RALSEI_NO_TEXTURE)
        return vec4(1.0);

    return sampleTexture(RALSEI_SAMPLER(tex), uv, flags);
#else
    switch (tex) {
#if RALSEI_TEXTURE_PAGE_SIZE > 0
        case 0: return sampleTexture(uTextures[0], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 1
        case 1: return sampleTexture(uTextures[1], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 2
        case 2: return sampleTexture(uTextures[2], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 3
        case 3: return sampleTexture(uTextures[3], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 4
        case 4: return sampleTexture(uTextures[4], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 5
        case 5: return sampleTexture(uTextures[5], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 6
        case 6: return sampleTexture(uTextures[6], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 7
        case 7: return sampleTexture(uTextures[7], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 8
        case 8: return sampleTexture(uTextures[8], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 9
        case 9: return sampleTexture(uTextures[9], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 10
        case 10: return sampleTexture(uTextures[10], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 11
        case 11: return sampleTexture(uTextures[11], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 12
        case 12: return sampleTexture(uTextures[12], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 13
        case 13: return sampleTexture(uTextures[13], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 14
        case 14: return sampleTexture(uTextures[14], uv, flags);
#endif
#if RALSEI_TEXTURE_PAGE_SIZE > 15
        case 15: return sampleTexture(uTextures[15], uv, flags);
#endif
        default: return vec4(1.0);
    }
#endif
}

// The colour of a textured mesh. The textures are premultiplied, so the tint is too.
//
// Fog floods the colour while the shape (the alpha) is left alone: a flat silhouette, or a flash that fades.
// It is the colour in red, green and blue and how much of it to mix in as alpha: 0 for none, 1 for a flat silhouette.
vec4 shadeTexture(vec4 color, vec4 fog, RalseiTexture tex, int textureFlags, vec2 uv) {
    vec4 shaded = premultiply(color) * getTextureColor(tex, textureFlags, uv);
    return vec4(mix(shaded.rgb, fog.rgb * shaded.a, fog.a), shaded.a);
}
