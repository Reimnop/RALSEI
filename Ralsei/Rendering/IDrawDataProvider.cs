using System.Numerics;
using Ralsei.Core.Data;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;

namespace Ralsei.Rendering;

public enum DrawType
{
    Mesh,
    Text
}

public struct DrawCommand
{
    public DrawType DrawType;
    public int DrawId;
}

public struct MeshDrawItem
{
    public MeshHandle MeshHandle;
    public Matrix3x2 Transform;
    public ColorRgba Color1;
    public ColorRgba Color2;
    public RenderMode RenderMode;
    public float GradientRotation;
    public float GradientScale;

    /// <summary>
    /// The texture to paint the mesh with, or <see cref="TextureHandle.None"/> (what <c>default</c> is) for a flat colour.
    /// The mesh's UV coordinates (expected in 0 to 1) are mapped onto the region <see cref="MinUV"/> to <see cref="MaxUV"/>
    /// of the texture. A mesh with a gradient <see cref="RenderMode"/> is painted with the gradient and has no texture.
    /// The texture is multiplied with <see cref="Color1"/>.
    /// </summary>
    public TextureHandle Texture;
    public Vector2 MinUV;
    public Vector2 MaxUV;

    /// <summary>
    /// Fog replaces the colour of every visible pixel while keeping its transparency: the red, green and blue are the
    /// fog colour and the alpha says how much of it to mix in (0 for none, 1 for a flat silhouette). Only applies to a texture.
    /// </summary>
    public ColorRgba Fog;
}

public struct TextDrawItem
{
    public TextHandle TextHandle;
    public Matrix3x2 Transform;
    public ColorRgba Color;
}

/// <summary>
/// What one pass of a frame draws.
/// </summary>
public ref struct DrawData
{
    public CameraState CameraState;
    public PostProcessingState PostProcessingState;
    public ColorRgba ClearColor;
    public Span<DrawCommand> DrawCommands;
    public Span<MeshDrawItem> MeshDrawItems;
    public Span<TextDrawItem> TextDrawItems;
}

/// <summary>
/// A frame, as the renderer reads it: one or more passes, which are drawn in order and layered (see <see cref="FrameBuffer"/>).
/// </summary>
public interface IDrawDataProvider
{
    int PassCount { get; }

    /// <summary>
    /// What is done to the passes once they are layered, if anything: the post-processing of the frame as a whole, after that of each pass.
    /// </summary>
    PostProcessingState? FinalPostProcessingState { get; }

    DrawData CreateDrawData(int passIndex);
}