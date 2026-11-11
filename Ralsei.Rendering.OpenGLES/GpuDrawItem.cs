using System.Numerics;
using System.Runtime.InteropServices;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.OpenGLES;

/// <summary>One instance of a draw, a mesh's or a glyph's, as the per-instance vertex attributes of Uber.vert.glsl.</summary>
[StructLayout(LayoutKind.Explicit, Size = 120)]
internal struct GpuDrawItem
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

    /// <summary>What the item is, and whether its texture is sampled with <see cref="CrispFlag"/>: any of the flags below.</summary>
    [FieldOffset(108)] public int TextureFlags;

    /// <summary>The texture is sampled with crisp edges (see TEXTURE_CRISP in TextureRef.glsl).</summary>
    public const int CrispFlag = 1;

    /// <summary>
    /// The item is a glyph of a text: <see cref="Mvp1"/>, <see cref="Mvp2"/> and the clip are the text's, <see cref="Color1"/> is the glyph's
    /// colour times the text's, <see cref="Color2"/> and the first two of <see cref="UvRect"/> are the glyph's own transform (M11, M12, M21, M22,
    /// then M31, M32), and <see cref="TextureSlotIndex"/> is its shape entry.
    /// </summary>
    public const int GlyphFlag = 2;

    /// <summary>
    /// The item is a sprite's quad, drawn as an instance of the builtin quad (<see cref="BuiltinRenderingPrimitive"/>) that the glyphs are:
    /// the shader makes the sprite quad's corners of the builtin quad's.
    /// </summary>
    public const int QuadFlag = 4;

    /// <summary>Where the draw shows, in pixels of the target: left and top (x in the low 16 bits), see <see cref="Common.ClipBounds.Pack"/>.</summary>
    [FieldOffset(112)] public uint ClipMin;

    /// <summary>Right and bottom, packed the same way. Fragments at or past it are not drawn.</summary>
    [FieldOffset(116)] public uint ClipMax;
}
