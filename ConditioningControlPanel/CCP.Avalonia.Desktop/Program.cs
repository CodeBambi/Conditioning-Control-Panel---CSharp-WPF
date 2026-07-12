using System;
using Avalonia;
using Avalonia.Logging;
#if DEBUG
using AvaloniaUI.DiagnosticsSupport;
#endif
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop;

public static class ProgramShared
{
    [STAThread]
    public static void Main(string[] args)
    {
        Run(args);
    }

    /// <summary>
    /// Shared entry point for Linux and macOS desktop heads. Handles single-instance
    /// enforcement and wires up the common desktop platform services, then lets the
    /// head inject any additional services via <paramref name="configurePlatformServices"/>.
    /// <paramref name="configureBuilder"/> runs after <see cref="BuildAvaloniaApp"/> and
    /// before the lifetime starts, so heads can attach Debug-only harnesses (e.g. the
    /// smoke-test runner) exactly the way the Windows head does with its own builder.
    /// </summary>
    public static void Run(
        string[] args,
        Action<IServiceCollection>? configurePlatformServices = null,
        Action<AppBuilder>? configureBuilder = null)
    {
        using var singleInstance = new DesktopSingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.SignalFirstInstance(args);
            return;
        }

        App.ConfigurePlatformServices = services =>
        {
            services.AddDesktopLibVLC();
            services.AddDesktopSecretStore();
            services.AddSingleton<IWallpaperProvider, DesktopWallpaperProvider>();
            configurePlatformServices?.Invoke(services);
            services.AddSingleton<ISingleInstanceService>(singleInstance);
        };

        var builder = BuildAvaloniaApp();
        configureBuilder?.Invoke(builder);
        builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Debug)
#if DEBUG
            .WithDeveloperTools()
#endif
            ;
    }
}
