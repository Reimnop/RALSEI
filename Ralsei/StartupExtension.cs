using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Core.Text;
using Ralsei.Rendering;

namespace Ralsei;

public static class StartupExtension
{
    public static RalseiBuilder AddPAS(this IServiceCollection services, Action<ResourceSourceFactories>? resourceSourceConfig = null)
    {
        var resourceSourceFactories = new ResourceSourceFactories();
        services.AddSingleton(resourceSourceFactories);
        
        // Add our own resource loader
        resourceSourceFactories.Add(() => new EmbeddedResourceSource(typeof(StartupExtension).Assembly));
        resourceSourceConfig?.Invoke(resourceSourceFactories);

        // Add resource loader
        services.AddSingleton<ResourceLoader>();

        // These manage rendering resources, so they should be singletons
        services.AddSingleton<MeshService>();
        services.AddSingleton<FontService>();

        // Add main services
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

        // Add migrations
        services.AddTransient<LsbMigration>();
        services.AddTransient<VgdMigration>();

        return new RalseiBuilder(services, resourceSourceFactories);
    }
}
