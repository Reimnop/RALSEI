using Microsoft.Extensions.DependencyInjection;
using Ralsei;
using Ralsei.Core;
using Ralsei.Core.Service;

namespace Swag;

public static class SwagExtension
{
    /// <summary>Adds what the SWAG animator needs on top of the engine: the beatmap, its animation and the director that draws it.</summary>
    public static RalseiBuilder AddSwag(this IServiceCollection services, Action<ResourceSourceFactories>? resourceSourceConfig = null)
    {
        var resourceSourceFactories = new ResourceSourceFactories();
        services.AddSingleton(resourceSourceFactories);
        
        resourceSourceFactories.Add(() => new EmbeddedResourceSource(typeof(SwagExtension).Assembly));
        resourceSourceConfig?.Invoke(resourceSourceFactories);

        services.AddSingleton<ResourceLoader>();

        // These manage rendering resources, so they should be singletons
        services.AddSingleton<MeshService>();
        services.AddSingleton<FontService>();

        services.AddScoped<AppDirector>();
        services.AddScoped<AnimationPipeline>();
        services.AddScoped<PlaybackObjectSortingService>();
        services.AddScoped<Timeline>();
        services.AddScoped<ObjectSourceManager>();
        services.AddScoped<PlaybackObjectContainer>();
        services.AddScoped<ThemeManager>();
        services.AddScoped<PlaybackThemeContainer>();
        services.AddScoped<EventManager>();
        services.AddScoped<RandomSeedService>();
        services.AddScoped<BeatmapService>();
        services.AddScoped<TextShaper>();
        services.AddScoped<MeshCacheService>();
        services.AddScoped<TextCacheService>();

        services.AddTransient<LsbMigration>();
        services.AddTransient<VgdMigration>();

        return new RalseiBuilder(services, resourceSourceFactories);
    }
}
