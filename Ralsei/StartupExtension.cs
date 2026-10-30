using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ralsei.Core;
using Ralsei.Core.Audio;
using Ralsei.Core.Game;
using Ralsei.Core.Graphics;
using Ralsei.Core.Input;
using Ralsei.Core.Service;
using Ralsei.Core.Text;
using Ralsei.Platform.Window;
using Ralsei.Rendering;

namespace Ralsei;

public static class StartupExtension
{
    /// <summary>Adds what a game needs of the engine: resources, meshes and fonts, scenes and the director that runs them, input and the canvas (window-sized unless a <see cref="CanvasOptions"/> says otherwise). What the host can do (sound, clipboard, windows) it registers itself, or nothing happens. A game registers an <see cref="IGameStartup"/> for its first scene, and the host runs a <see cref="GameDirector"/>.</summary>
    public static RalseiBuilder AddRalsei(this IServiceCollection services, Action<ResourceSourceFactories>? resourceSourceConfig = null)
    {
        var resourceSourceFactories = new ResourceSourceFactories();
        services.AddSingleton(resourceSourceFactories);

        resourceSourceFactories.Add(() => new EmbeddedResourceSource(typeof(StartupExtension).Assembly));
        resourceSourceConfig?.Invoke(resourceSourceFactories);
        services.AddSingleton<ResourceLoader>();

        // These manage rendering resources, so they should be singletons
        services.AddSingleton<MeshService>();
        services.AddSingleton<FontService>();
        services.AddSingleton<TextShaper>();

        services.AddSingleton<SceneHost>();
        services.AddSingleton<InputManager>();
        services.TryAddSingleton(new CanvasOptions());
        services.AddSingleton<Canvas>();
        services.AddSingleton<ICanvas>(x => x.GetRequiredService<Canvas>());
        services.AddTransient<AtlasBuilder>();
        services.AddSingleton<GameDirector>();
        services.TryAddSingleton<IAudioPlayer, NullAudioPlayer>();
        services.TryAddSingleton<ITrackPlayer, NullTrackPlayer>();
        services.TryAddSingleton<IClipboard, MemoryClipboard>();
        services.TryAddSingleton<IWindowControl, NullWindowControl>();
        services.TryAddSingleton<IWindowManager, NullWindowManager>();

        return new RalseiBuilder(services, resourceSourceFactories);
    }
}
