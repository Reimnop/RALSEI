using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using Ralsei.Platform.Vulkan;

namespace Swag.Desktop;

[CliCommand(
    Name = "run",
    Description = "Run the beatmap in a window with real-time rendering")]
public class RunCliCommand : RootCliCommand
{
    [CliOption(Name = "vsync", Description = "Enable VSync")]
    public bool VSync { get; set; } = true;

    [CliOption(Name = "lock-aspect", Description = "Lock the aspect ratio to 16:9")]
    public bool LockAspectRatio { get; set; } = true;

    public void Run()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton(new DesktopSurfaceSettings
        {
            Size = new Vector2i(Width, Height),
            VSync = VSync,
            UseEgl = UseEgl,
            LockAspectRatio = LockAspectRatio
        });

        services.AddPlatform<GlfwService>(Backend);

        // The window is shared between the app (events) and the renderer (drawing)
        if (Backend == RenderingBackend.Vulkan)
        {
            services
                .AddScoped<VulkanDesktopSurface>()
                .AddScoped<IVulkanSurface>(sp => sp.GetRequiredService<VulkanDesktopSurface>())
                .AddScoped<DesktopWindow>(sp => sp.GetRequiredService<VulkanDesktopSurface>());
        }
        else
        {
            services
                .AddScoped<DesktopSurface>()
                .AddScoped<IOpenGLSurface>(sp => sp.GetRequiredService<DesktopSurface>())
                .AddScoped<DesktopWindow>(sp => sp.GetRequiredService<DesktopSurface>());
        }

        services.AddTransient<DesktopApp>();
        
        using var serviceProvider = services.BuildServiceProvider();
        
        var app = serviceProvider.GetRequiredService<DesktopApp>();
        app.StartApp(BeatmapPath, AudioPath, Seed, EnablePostProcessing, EnableTextRendering);
    }
}