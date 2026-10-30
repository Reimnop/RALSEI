using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.OpenGLES;

public static class RalseiOptionsBuilderExtension
{
    public static RalseiBuilder UseOpenGLESRenderer(this RalseiBuilder builder)
    {
        var services = builder.Services;
        
        // One factory, found by class (renderers) or interface (apps). It holds no graphics resources, so a host can share it between windows; the frame queue is per window.
        services.AddSingleton<RenderingFactory>();
        services.AddSingleton<IRenderingFactory>(x => x.GetRequiredService<RenderingFactory>());
        services.AddSingleton<IFrameQueue, FrameQueue>();
        services.AddScoped<IRenderer, Renderer>();

        services.AddSingleton(new OpenGLSettings
        {
            MajorVersion = 3,
            MinorVersion = 0,
            IsES = true
        });
        
        builder.UseSharedShaders();
        
        return builder;
    }
}