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
        
        services.AddSingleton<IRenderingFactory, RenderingFactory>();
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