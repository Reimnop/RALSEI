using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swag.Avalonia.Services;
using Swag.Avalonia.ViewModels;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.OpenGLES;
using Ralsei.Resources.Compressed;
using Reimnop.MvvmHelper;

namespace Swag.Avalonia.Integration;

/// <summary>The level, and the renderer that draws it into <see cref="SwagView"/>.</summary>
[ViewModelView<SwagView>]
public class SwagViewModel : ViewModelBase, IDisposable
{
    /// <summary>Asked for the time of the level, once for every frame that is made.</summary>
    public Func<float>? TickCallback { get; set; }

    /// <summary>Frames are only made while this is set. The level is not to be touched while it is being replaced.</summary>
    public bool IsActive { get; set; }

    public IServiceProvider ServiceProvider { get; }

    public BeatmapService BeatmapService { get; }
    public RandomSeedService RandomSeedService { get; }

    public AppDirector Director { get; }

    public SwagRenderSettings RenderSettings { get; } = new();

    public SwagViewModel(LogStore logStore)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddProvider(logStore);
        });

        services.AddSwag(x => x.AddCompressedResources())
            .UseOpenGLESRenderer();

        services.AddSingleton(RenderSettings);
        services.AddSingleton<SwagViewHolder>();
        services.AddScoped<IOpenGLSurface, SwagSurface>(x =>
        {
            var holder = x.GetRequiredService<SwagViewHolder>();
            if (holder.View is null)
                throw new NullReferenceException("View is null");
            return new SwagSurface(holder.View, x.GetRequiredService<SwagRenderSettings>());
        });

        ServiceProvider = services.BuildServiceProvider();

        var directorScope = ServiceProvider.CreateScope();
        var directorSp = directorScope.ServiceProvider;

        Director = directorSp.GetRequiredService<AppDirector>();
        BeatmapService = directorSp.GetRequiredService<BeatmapService>();
        RandomSeedService = directorSp.GetRequiredService<RandomSeedService>();
    }

    /// <summary>Makes the next frame, if there is a level to make one of.</summary>
    public void ProcessFrame(Vector2i renderSize)
    {
        if (!IsActive)
            return;

        Director.SubmitFrame(TickCallback?.Invoke() ?? 0f, renderSize);
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();
    }
}
