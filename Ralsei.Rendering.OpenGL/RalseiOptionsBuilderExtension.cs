using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.OpenGL;

public static class RalseiOptionsBuilderExtension
{
    public static RalseiBuilder UseOpenGLRenderer(this RalseiBuilder builder)
    {
        var services = builder.Services;
        
        // One factory, found by class (renderers) or interface (apps). It holds no graphics resources, so a host can share it between windows; the frame queue is per window.
        services.AddSingleton<RenderingFactory>();
        services.AddSingleton<IRenderingFactory>(x => x.GetRequiredService<RenderingFactory>());
        services.AddSingleton<IFrameQueue, FrameQueue>();
        services.AddSingleton<BuiltinRenderingPrimitive>();
        services.AddScoped<IRenderer, Renderer>();

        services.AddSingleton(new OpenGLSettings
        {
            MajorVersion = 4,
            MinorVersion = 6,
            IsES = false
        });
        
        builder.UseSharedShaders();
        
        return builder;
    }
}