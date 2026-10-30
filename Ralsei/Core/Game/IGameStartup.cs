namespace Ralsei.Core.Game;

/// <summary>Registered by a game module to say which scene runs first.</summary>
public interface IGameStartup
{
    IScene CreateInitialScene();
}
