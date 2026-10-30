using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swag.Avalonia.Export;
using Swag.Avalonia.Generated;
using Swag.Avalonia.Integration;
using Swag.Avalonia.Services;
using Swag.Avalonia.ViewModels;
using Swag.Avalonia.Views;
using Reimnop.MvvmHelper;
using ShadUI;

namespace Swag.Avalonia;

public class App : Application, IDisposable
{
    /// <summary>What the app was started with: the level to open, if there is one.</summary>
    public static string[] StartupArgs { get; set; } = [];

    private ServiceProvider? serviceProvider;
    private MainWindowViewModel? mainWindowViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        serviceProvider = BuildServices().BuildServiceProvider();

        var viewLocator = serviceProvider.GetRequiredService<IViewLocator>();
        DataTemplates.Add(viewLocator);

        mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();

        // Whatever goes wrong in an event handler is reported, it is not a reason to end the player
        var logger = serviceProvider.GetRequiredService<ILogger<App>>();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "An error was not handled");
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        mainWindowViewModel?.Dispose();
        serviceProvider?.Dispose();
    }

    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection().AddMvvm();

        services.AddSingleton<LogStore>();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<LogStore>());
        });

        services.AddSingleton<SettingsService>();
        services.AddSingleton<RecentLevelsService>();
        services.AddSingleton<WindowServices>();
        services.AddSingleton<ToastManager>();
        services.AddSingleton<FFmpegExporter>();

        // The generated registration makes view models transient, but these are shared
        services.AddSingleton<SwagViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaybackViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
