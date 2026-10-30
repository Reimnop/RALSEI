using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.Vulkan;

public static class RalseiOptionsBuilderExtension
{
    public static RalseiBuilder UseVulkanRenderer(this RalseiBuilder builder)
    {
        var services = builder.Services;

        // One factory, found by class (renderers) or interface (apps). It holds no graphics resources, so a host can share it between windows; the frame queue is per window.
        services.AddSingleton<RenderingFactory>();
        services.AddSingleton<IRenderingFactory>(x => x.GetRequiredService<RenderingFactory>());
        services.AddSingleton<IFrameQueue, FrameQueue>();
        services.AddSingleton<BuiltinRenderingPrimitive>();
        services.AddScoped<IRenderer, Renderer>();

        builder.UseResourceSourceFactory(() => new EmbeddedResourceSource(typeof(RalseiOptionsBuilderExtension).Assembly));

        return builder;
    }
}
