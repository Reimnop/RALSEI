using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.Vulkan;

public static class RalseiOptionsBuilderExtension
{
    public static RalseiBuilder UseVulkanRenderer(this RalseiBuilder builder)
    {
        var services = builder.Services;

        services.AddSingleton<IRenderingFactory, RenderingFactory>();
        services.AddSingleton<BuiltinRenderingPrimitive>();
        services.AddScoped<IRenderer, Renderer>();

        builder.UseResourceSourceFactory(() => new EmbeddedResourceSource(typeof(RalseiOptionsBuilderExtension).Assembly));

        return builder;
    }
}
