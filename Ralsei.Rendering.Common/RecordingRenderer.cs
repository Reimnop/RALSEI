using System.Numerics;
using Ralsei.Core.Data;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Common;

/// <summary>One pass of a frame, as <see cref="RecordingRenderer"/> copied it.</summary>
public sealed record RecordedPass(
    Matrix3x2 ViewMatrix,
    PostProcessingState PostProcessingState,
    ColorRgba ClearColor,
    float Opacity,
    DrawCommand[] DrawCommands,
    MeshDrawItem[] MeshDrawItems,
    TextDrawItem[] TextDrawItems,
    ClipRect Viewport = default);

/// <summary>A frame, as <see cref="RecordingRenderer"/> copied it.</summary>
public sealed record RecordedFrame(PostProcessingState? FinalPostProcessingState, IReadOnlyList<RecordedPass> Passes);

/// <summary>A renderer that draws nothing and records the frames it is given, for tests and tools. With no render thread, call <see cref="IFrameQueue.FlushFrame"/> with it to take the next frame.</summary>
public sealed class RecordingRenderer : IRenderer
{
    public List<RecordedFrame> Frames { get; } = [];

    /// <summary>The frame that was recorded last.</summary>
    public RecordedFrame Last => Frames[^1];

    public void ProcessFrame(IDrawDataProvider drawDataProvider)
    {
        var passes = new List<RecordedPass>();
        for (var i = 0; i < drawDataProvider.PassCount; i++)
        {
            var data = drawDataProvider.CreateDrawData(i);
            passes.Add(new RecordedPass(
                data.ViewMatrix,
                data.PostProcessingState,
                data.ClearColor,
                data.Opacity,
                data.DrawCommands.ToArray(),
                data.MeshDrawItems.ToArray(),
                data.TextDrawItems.ToArray(),
                data.Viewport));
        }

        Frames.Add(new RecordedFrame(drawDataProvider.FinalPostProcessingState, passes));
    }
}
