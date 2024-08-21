using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ralsei.Avalonia.Services;
using Ralsei.Avalonia.ViewModels;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.OpenGLES;
using Ralsei.Resources.Compressed;
using Reimnop.MvvmHelper;

namespace Ralsei.Avalonia.Integration;

/// <summary>
/// The level, and the renderer that draws it into <see cref="RalseiView"/>.
/// </summary>
[ViewModelView<RalseiView>]
public class RalseiViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Asked for the time of the level, once for every frame that is made.
    /// </summary>
    public Func<float>? TickCallback { get; set; }

    /// <summary>
    /// Frames are only made while this is set. The level is not to be touched while it is being replaced.
    /// </summary>
    public bool IsActive { get; set; }

    public IServiceProvider ServiceProvider { get; }

    public BeatmapService BeatmapService { get; }
    public RandomSeedService RandomSeedService { get; }

    public AppDirector Director { get; }

    public RalseiRenderSettings RenderSettings { get; } = new();

    public RalseiViewModel(LogStore logStore)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddProvider(logStore);
        });

        services.AddPAS(x => x.AddCompressedResources())
            .UseOpenGLESRenderer();

        services.AddSingleton(RenderSettings);
        services.AddSingleton<RalseiViewHolder>();
        services.AddScoped<IOpenGLSurface, RalseiSurface>(x =>
        {
            var holder = x.GetRequiredService<RalseiViewHolder>();
            if (holder.View is null)
                throw new NullReferenceException("View is null");
            return new RalseiSurface(holder.View, x.GetRequiredService<RalseiRenderSettings>());
        });

        ServiceProvider = services.BuildServiceProvider();

        var directorScope = ServiceProvider.CreateScope();
        var directorSp = directorScope.ServiceProvider;

        Director = directorSp.GetRequiredService<AppDirector>();
        BeatmapService = directorSp.GetRequiredService<BeatmapService>();
        RandomSeedService = directorSp.GetRequiredService<RandomSeedService>();
    }

    /// <summary>
    /// Makes the next frame, if there is a level to make one of.
    /// </summary>
    public void ProcessFrame()
    {
        if (!IsActive)
            return;

        Director.SubmitFrame(TickCallback?.Invoke() ?? 0f);
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();
    }
}
