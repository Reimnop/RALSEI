using Ralsei.Mathematics;
using Ralsei.Rendering;

namespace Ralsei.Core.Game;

/// <summary>A scene that builds its frames itself instead of drawing through the sprite <see cref="Graphics.ICanvas"/>: it asks the frame queue for a buffer (passes with their own camera, clear colour, post-processing), fills it and submits it. For scenes not made of sprites. Runs on the main thread once per rendered frame, after <see cref="IScene.Update"/>.</summary>
public interface IFrameScene : IScene
{
    /// <summary>Builds this frame's picture and submits it (<see cref="IFrameQueue.SubmitFrame"/>); <paramref name="targetSize"/> is the window size.</summary>
    void Submit(IFrameQueue frames, Vector2i targetSize);

    /// <summary>A scene that builds its frames has no sprite canvas drawing to do.</summary>
    void IScene.Draw(Graphics.ICanvas canvas)
    {
    }
}
