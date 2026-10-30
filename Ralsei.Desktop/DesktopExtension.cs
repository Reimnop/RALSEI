using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ralsei.Core.Audio;
using Ralsei.Platform.OpenGL;
using Ralsei.Platform.Vulkan;
using Ralsei.Platform.Window;
using Ralsei.Rendering.OpenGL;
using Ralsei.Rendering.OpenGLES;
using Ralsei.Rendering.Vulkan;
using Ralsei.Resources.Compressed;

namespace Ralsei.Desktop;

/// <summary>What the pictures of a desktop app are drawn with.</summary>
public enum RenderingBackend
{
    OpenGL,
    OpenGLES,
    Vulkan
}

public static class DesktopExtension
{
    /// <summary>Reads what every desktop app takes on its command line: <c>--renderer gl|gles|vulkan</c> (default Vulkan) and <c>--vsync on|off|adaptive</c>. Returns false, with a message for the user, when one is not understood.</summary>
    public static bool TryRead(Arguments arguments, out RenderingBackend backend, out string? error)
    {
        backend = RenderingBackend.Vulkan;
        error = null;

        if (arguments.Value("vsync") is { } vsync)
        {
            if (DesktopSettings.Parse(vsync) is not { } interval)
            {
                error = $"Unknown vsync '{vsync}', it is on, off or adaptive";
                return false;
            }

            DesktopSettings.SwapInterval = interval;
        }

        var renderer = arguments.Value("renderer")?.ToLowerInvariant();
        switch (renderer)
        {
            case null or "vulkan":
                break;
            case "gl":
                backend = RenderingBackend.OpenGL;
                break;
            case "gles":
                backend = RenderingBackend.OpenGLES;
                break;
            default:
                error = $"Unknown renderer '{renderer}', it is gl, gles or vulkan";
                return false;
        }

        return true;
    }

    /// <summary>Adds the engine and a desktop to run it on: windows (<paramref name="host"/> runs them; scenes ask it for more), sound, clipboard and renderer. The app adds the rest, builds the services and has <paramref name="host"/> run them.</summary>
    public static RalseiBuilder AddDesktop(this IServiceCollection services, RenderingBackend backend, out DesktopHost host)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });

        host = new DesktopHost(services);

        services
            .AddSingleton(host)
            .AddSingleton<IWindowManager>(host)
            .AddSingleton<IAudioPlayer, BassAudioPlayer>()
            .AddSingleton<ITrackPlayer, BassTrackPlayer>()
            .AddSingleton<DesktopWindow>()
            .AddSingleton<IClipboard>(x => x.GetRequiredService<DesktopWindow>().Clipboard)
            .AddSingleton<IWindowControl>(x => x.GetRequiredService<DesktopWindow>().WindowControl);

        var ralsei = services.AddRalsei(x => x.AddCompressedResources());
        switch (backend)
        {
            case RenderingBackend.OpenGLES:
                ralsei.UseOpenGLESRenderer();
                services.AddSingleton<IOpenGLSurface>(x => x.GetRequiredService<DesktopWindow>().OpenGLSurface);
                break;
            case RenderingBackend.OpenGL:
                ralsei.UseOpenGLRenderer();
                services.AddSingleton<IOpenGLSurface>(x => x.GetRequiredService<DesktopWindow>().OpenGLSurface);
                break;
            default:
                // A Vulkan window has no OpenGL context; the renderer draws to what the window gives it
                ralsei.UseVulkanRenderer();
                services.AddSingleton<IVulkanSurface>(x => x.GetRequiredService<DesktopWindow>().VulkanSurface);
                break;
        }

        return ralsei;
    }
}
