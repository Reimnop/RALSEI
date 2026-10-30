using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.OpenGLES;

/// <summary>One instance of a mesh draw, as the per-instance vertex attributes of Unlit.vert.glsl.</summary>
[StructLayout(LayoutKind.Explicit, Size = 112)]
internal struct GpuUnlitDrawItem
{
    /// <summary>The first two columns of the transform: M11, M12, M21, M22.</summary>
    [FieldOffset(0)] public Vector4 Mvp1;

    /// <summary>The last column of the transform, M31 and M32, then the depth and the gradient's rotation.</summary>
    [FieldOffset(16)] public Vector4 Mvp2;

    [FieldOffset(32)] public ColorRgba Color1;
    [FieldOffset(48)] public ColorRgba Color2;

    /// <summary>Fog colour in red, green and blue, and how much of it to mix in as alpha.</summary>
    [FieldOffset(64)] public ColorRgba Fog;

    /// <summary>The texture region: min u, min v, max u, max v.</summary>
    [FieldOffset(80)] public Vector4 UvRect;

    [FieldOffset(96)] public float GradientScale;
    /// <summary>The slot the texture has in the page of textures the draw is drawn with, or -1 if it has none.</summary>
    [FieldOffset(100)] public int TextureSlotIndex;

    /// <summary>0 for no gradient; otherwise the <see cref="Ralsei.Rendering.Data.RenderMode"/> value.</summary>
    [FieldOffset(104)] public int GradientMode;

    /// <summary>Whether the texture is sampled with crisp edges (1) or not (0).</summary>
    [FieldOffset(108)] public int TextureFlags;
}
