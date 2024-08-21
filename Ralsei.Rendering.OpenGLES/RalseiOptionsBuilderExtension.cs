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
        
        services.AddSingleton<IRenderingFactory, RenderingFactory>();
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