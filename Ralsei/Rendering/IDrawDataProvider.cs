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

    /// <summary>The index of the item this command draws, in the pass's items of the command's type.</summary>
    public int DrawId;

    /// <summary>What the draw is cut to. The default is no cut.</summary>
    public ClipRect Clip;
}

public struct MeshDrawItem
{
    public MeshHandle MeshHandle;
    public Matrix3x2 Transform;
    public ColorRgba Color1;

    /// <summary>The second colour of a gradient, used when <see cref="RenderMode"/> is not <see cref="Data.RenderMode.Normal"/>.</summary>
    public ColorRgba Color2;

    public RenderMode RenderMode;
    public float GradientRotation;
    public float GradientScale;

    /// <summary>The texture to paint the mesh with, or <see cref="TextureHandle.None"/> (what <c>default</c> is) for a flat colour. UVs (0 to 1) map onto the region <see cref="MinUV"/> to <see cref="MaxUV"/>, and the texture is multiplied with <see cref="Color1"/>. A gradient <see cref="RenderMode"/> has no texture. Can be an earlier pass's output (<see cref="TextureHandle.FromPass"/>); an error for a pass that is not after it.</summary>
    public TextureHandle Texture;
    public Vector2 MinUV;
    public Vector2 MaxUV;

    /// <summary>Fog replaces the colour of every visible pixel, keeping its transparency: red, green and blue are the colour, alpha how much to mix in (0 none, 1 a flat silhouette). Only applies to a texture.</summary>
    public ColorRgba Fog;
}

public struct TextDrawItem
{
    public TextHandle TextHandle;
    public Matrix3x2 Transform;
    public ColorRgba Color;
}

/// <summary>What one pass of a frame draws.</summary>
public ref struct DrawData
{
    /// <summary>Maps the world the pass draws in to clip space (see <see cref="PassBuffer.ViewMatrix"/>).</summary>
    public Matrix3x2 ViewMatrix;
    public PostProcessingState PostProcessingState;
    public ColorRgba ClearColor;

    /// <summary>How much of the pass shows when it is laid over the passes before it: 1 for all of it.</summary>
    public float Opacity;

    /// <summary>The part of the target the pass is drawn in (see <see cref="PassBuffer.Viewport"/>); <c>default</c> is all of it.</summary>
    public ClipRect Viewport;

    public Span<DrawCommand> DrawCommands;
    public Span<MeshDrawItem> MeshDrawItems;
    public Span<TextDrawItem> TextDrawItems;
}

/// <summary>A frame, as the renderer reads it: one or more passes, drawn in order and layered (see <see cref="FrameBuffer"/>).</summary>
public interface IDrawDataProvider
{
    int PassCount { get; }

    /// <summary>The <see cref="IRenderingFactory.Version"/> the frame was submitted at: the renderer applies the changes up to it, and no later ones, before drawing.</summary>
    long ResourceVersion { get; }

    /// <summary>The post-processing of the layered passes, after each pass's own, if any.</summary>
    PostProcessingState? FinalPostProcessingState { get; }

    DrawData CreateDrawData(int passIndex);
}
