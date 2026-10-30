using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.OpenGL;

// std430 layout
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct GpuMeshDrawItem
{
    [FieldOffset(0)] public Matrix3x2 Mvp;
    [FieldOffset(24)] public float Z;
    [FieldOffset(28)] public int RenderMode;
    [FieldOffset(32)] public ColorRgba Color1;
    [FieldOffset(48)] public ColorRgba Color2;
    [FieldOffset(64)] public float GradientRotation;
    [FieldOffset(68)] public float GradientScale;
    [FieldOffset(72)] public Vector2 MinUV;
    [FieldOffset(80)] public Vector2 MaxUV;

    /// <summary>
    /// The texture: the slot it has in the page of textures the draw is drawn with, or -1 if it has none. With bindless textures, the
    /// low half of its handle (the high half is <see cref="Texture1"/>), or the index in the array of all textures in Vulkan.
    /// </summary>
    [FieldOffset(88)] public int Texture0;
    [FieldOffset(92)] public int Texture1;

    /// <summary>Fog colour in red, green and blue, and how much of it to mix in as alpha.</summary>
    [FieldOffset(96)] public ColorRgba Fog;

    /// <summary>Whether the texture is sampled with crisp edges (1) or not (0).</summary>
    [FieldOffset(112)] public int TextureFlags;

    /// <summary>Where the draw shows, in pixels of the target: left and top (x in the low 16 bits), see <see cref="Common.ClipBounds.Pack"/>.</summary>
    [FieldOffset(116)] public uint ClipMin;

    /// <summary>Right and bottom, packed the same way. Fragments at or past it are not drawn.</summary>
    [FieldOffset(120)] public uint ClipMax;
}
