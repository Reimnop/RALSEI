#pragma once

// What every shader needs to be written once and compiled for OpenGL 4.6, OpenGL ES 3.0 and Vulkan.
//
// The host defines exactly one of RALSEI_OPENGL, RALSEI_GLES and RALSEI_VULKAN, and the #version, when it
// compiles a shader (see ShaderLoader). Shaders therefore have no #version of their own.
//
// The RALSEI_* macros below that declare something carry their own semicolon.

#if defined(RALSEI_OPENGL) + defined(RALSEI_GLES) + defined(RALSEI_VULKAN) != 1
#error Define exactly one of RALSEI_OPENGL, RALSEI_GLES and RALSEI_VULKAN
#endif

// Textures that draws name without the host binding them (see Texturing.glsl), if the backend has them
#ifdef RALSEI_BINDLESS
#ifdef RALSEI_OPENGL
#extension GL_ARB_bindless_texture : require
#elif defined(RALSEI_VULKAN)
#extension GL_EXT_nonuniform_qualifier : require
#endif
#endif

#ifdef RALSEI_GLES
// GL ES has no default precision for floats in fragment shaders, or for samplers in any shader
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp isampler2D;
precision highp usampler2D;
#endif

// Explicit locations for varyings, which Vulkan needs and GL ES 3.0 doesn't allow
#ifdef RALSEI_GLES
#define RALSEI_LOCATION(n)
#else
#define RALSEI_LOCATION(n) layout(location = n)
#endif

// A read-only storage buffer, bound to a binding point (and descriptor set 0 in Vulkan)
#ifdef RALSEI_VULKAN
#define RALSEI_STORAGE_BUFFER(n) layout(std430, set = 0, binding = n) readonly buffer
#else
#define RALSEI_STORAGE_BUFFER(n) layout(std430, binding = n) readonly buffer
#endif

// A texture sampler. Only Vulkan binds them in the shader; the GL hosts set the texture unit of the uniform
#ifdef RALSEI_VULKAN
#define RALSEI_SAMPLER2D(name, n) layout(set = 0, binding = n) uniform sampler2D name;
#else
#define RALSEI_SAMPLER2D(name, n) uniform sampler2D name;
#endif

// Small per-draw parameters: push constants in Vulkan, plain uniforms elsewhere. Vulkan lays the block
// out in declaration order (or at RALSEI_PARAM_AT's offset), which the host mirrors; GL looks the uniforms up by name.
#ifdef RALSEI_VULKAN
#define RALSEI_PARAMS_BEGIN layout(push_constant) uniform PushConstants {
#define RALSEI_PARAMS_END };
#define RALSEI_PARAM(type, name) type name;
#define RALSEI_PARAM_AT(at, type, name) layout(offset = at) type name;
#else
#define RALSEI_PARAMS_BEGIN
#define RALSEI_PARAMS_END
#define RALSEI_PARAM(type, name) uniform type name;
#define RALSEI_PARAM_AT(at, type, name) uniform type name;
#endif

// The index of the current instance, counted from 0 for each draw. Vulkan's includes the draw's first instance.
#ifdef RALSEI_VULKAN
#define RALSEI_INSTANCE_INDEX gl_InstanceIndex
#else
#define RALSEI_INSTANCE_INDEX gl_InstanceID
#endif
