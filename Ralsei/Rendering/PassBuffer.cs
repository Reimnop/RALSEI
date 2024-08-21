using Ralsei.Core.Data;
using Ralsei.Rendering.Data;
using Ralsei.Util;

namespace Ralsei.Rendering;

/// <summary>
/// How many elements a pass of a frame has.
/// </summary>
public readonly record struct PassSize(int DrawCommandCount, int MeshDrawItemCount, int TextDrawItemCount);

/// <summary>
/// One pass of a <see cref="FrameBuffer"/>: everything that is drawn to one target, with its own camera, clear color,
/// post-processing and draw commands. Pooled with the frame, which sizes it; the application fills the spans (a plain copy does the job).
/// </summary>
public sealed class PassBuffer : IResettable
{
    public CameraState CameraState { get; set; }
    public PostProcessingState PostProcessingState { get; set; }

    /// <summary>
    /// What the pass's target is cleared to. Its alpha says how much of what is behind the pass shows through where the pass draws nothing:
    /// it is 1 for the usual background, and 0 for a pass that is only laid over the passes before it.
    /// </summary>
    public ColorRgba ClearColor { get; set; }

    public Span<DrawCommand> DrawCommands => drawCommands.AsSpan(0, drawCommandCount);
    public Span<MeshDrawItem> MeshDrawItems => meshDrawItems.AsSpan(0, meshDrawItemCount);
    public Span<TextDrawItem> TextDrawItems => textDrawItems.AsSpan(0, textDrawItemCount);

    private DrawCommand[] drawCommands = new DrawCommand[1000];
    private MeshDrawItem[] meshDrawItems = new MeshDrawItem[1000];
    private TextDrawItem[] textDrawItems = new TextDrawItem[1000];

    private int drawCommandCount;
    private int meshDrawItemCount;
    private int textDrawItemCount;

    /// <summary>
    /// Sets how many elements the spans expose. Existing contents are not preserved or cleared.
    /// </summary>
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
        CameraState = new CameraState { Scale = 10f };
        PostProcessingState = default;
        ClearColor = new ColorRgba(0.0f, 0.0f, 0.0f, 1.0f);
        drawCommandCount = 0;
        meshDrawItemCount = 0;
        textDrawItemCount = 0;
    }

    internal DrawData CreateDrawData()
        => new()
        {
            CameraState = CameraState,
            PostProcessingState = PostProcessingState,
            ClearColor = ClearColor,
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
