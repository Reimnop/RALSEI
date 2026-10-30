using System.Numerics;
using Ralsei.Core.Data;
using Ralsei.Rendering.Data;
using Ralsei.Util;

namespace Ralsei.Rendering;

/// <summary>How many elements a pass of a frame has.</summary>
public readonly record struct PassSize(int DrawCommandCount, int MeshDrawItemCount, int TextDrawItemCount);

/// <summary>One pass of a <see cref="FrameBuffer"/>: everything drawn to one target, with its own view matrix, clear colour, post-processing and draws. Pooled and sized with the frame; the application fills the spans.</summary>
public sealed class PassBuffer : IResettable
{
    /// <summary>Maps the pass's world to clip space: x and y run -1 to 1 over the target (or its <see cref="Viewport"/>), y up. The application works it out (see <see cref="RenderUtil.GetViewMatrix"/>); the renderer only multiplies by it.</summary>
    public Matrix3x2 ViewMatrix { get; set; } = Matrix3x2.Identity;
    public PostProcessingState PostProcessingState { get; set; }

    /// <summary>What the target is cleared to. Its alpha says how much of the passes behind shows through where nothing is drawn: 1 for a usual background, 0 for a pure overlay.</summary>
    public ColorRgba ClearColor { get; set; }

    /// <summary>How much of the pass shows when laid over the earlier ones, 0 to 1: everything it drew, clear colour included, is scaled by it, fading a group as one without overlaps showing through. Only matters with more than one pass.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Where in the target the pass is drawn, if only a part (a small view in a window): a pixel rectangle. The pass is drawn as if the target were that rectangle (camera and clips relative to its top-left), and nothing shows outside it. <c>default</c> is the whole target.</summary>
    public ClipRect Viewport { get; set; }

    public Span<DrawCommand> DrawCommands => drawCommands.AsSpan(0, drawCommandCount);
    public Span<MeshDrawItem> MeshDrawItems => meshDrawItems.AsSpan(0, meshDrawItemCount);
    public Span<TextDrawItem> TextDrawItems => textDrawItems.AsSpan(0, textDrawItemCount);

    private DrawCommand[] drawCommands = new DrawCommand[1000];
    private MeshDrawItem[] meshDrawItems = new MeshDrawItem[1000];
    private TextDrawItem[] textDrawItems = new TextDrawItem[1000];

    private int drawCommandCount;
    private int meshDrawItemCount;
    private int textDrawItemCount;

    /// <summary>Sets how many elements the spans expose. Contents are not preserved or cleared.</summary>
    public void Resize(PassSize size)
    {
        EnsureCapacity(ref drawCommands, size.DrawCommandCount);
        EnsureCapacity(ref meshDrawItems, size.MeshDrawItemCount);
        EnsureCapacity(ref textDrawItems, size.TextDrawItemCount);

        drawCommandCount = size.DrawCommandCount;
        meshDrawItemCount = size.MeshDrawItemCount;
        textDrawItemCount = size.TextDrawItemCount;
    }

    public void Reset()
    {
        ViewMatrix = Matrix3x2.Identity;
        PostProcessingState = default;
        ClearColor = new ColorRgba(0.0f, 0.0f, 0.0f, 1.0f);
        Opacity = 1f;
        Viewport = default;
        drawCommandCount = 0;
        meshDrawItemCount = 0;
        textDrawItemCount = 0;
    }

    internal DrawData CreateDrawData()
        => new()
        {
            ViewMatrix = ViewMatrix,
            PostProcessingState = PostProcessingState,
            ClearColor = ClearColor,
            Opacity = Opacity,
            Viewport = Viewport,
            DrawCommands = DrawCommands,
            MeshDrawItems = MeshDrawItems,
            TextDrawItems = TextDrawItems
        };

    private static void EnsureCapacity<T>(ref T[] arr, int count) where T : struct
    {
        if (count <= arr.Length) return;

        // Contents are not preserved, the caller overwrites everything it asked for
        arr = new T[Math.Max(arr.Length * 2, count)];
    }
}
