using Ralsei.Core.Graphics;

namespace Ralsei.Core.Game;

/// <summary>A self-contained piece of game state (battle, menu, editor...). The <see cref="GameDirector"/> advances it once per rendered frame by the time that passed, then asks it to draw.</summary>
public interface IScene : IDisposable
{
    /// <summary>Advances the scene by <paramref name="deltaSeconds"/> (scaled by <see cref="GameDirector.TimeScale"/>, at most <see cref="GameDirector.MaxFrameTime"/>). A scene that wants fixed steps accumulates the time itself.</summary>
    void Update(float deltaSeconds);

    /// <summary>Records this frame's draw calls. Called once per rendered frame, on the main thread.</summary>
    void Draw(ICanvas canvas);
}
