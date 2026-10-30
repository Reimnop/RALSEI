namespace Ralsei.Core.Game;

/// <summary>Owns the running scene. A scene can request a switch from <see cref="IScene.Update"/>; it is applied between ticks.</summary>
public sealed class SceneHost : IDisposable
{
    public IScene? Current { get; private set; }

    private IScene? pending;

    public void Switch(IScene scene)
        => pending = scene;

    public void ApplyPendingSwitch()
    {
        if (pending is null)
            return;

        Current?.Dispose();
        Current = pending;
        pending = null;
    }

    public void Dispose()
    {
        Current?.Dispose();
        pending?.Dispose();
        Current = null;
        pending = null;
    }
}
