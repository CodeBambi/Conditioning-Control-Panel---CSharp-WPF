using System;
using System.Linq;
using Avalonia;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform;
using ConditioningControlPanel.Avalonia.Infrastructure;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CCP Linux] Process started.");

        var benchmark = args.Contains("--benchmark");
        var maxBenchmark = args.Contains("--max-benchmark");
        BenchmarkContext.IsEnabled = benchmark || maxBenchmark;
        BenchmarkContext.IsMaxBenchmark = maxBenchmark;
        BenchmarkContext.EntryTimeUtc = DateTime.UtcNow;

        var assetsPathIndex = Array.IndexOf(args, "--assets-path");
        if (assetsPathIndex >= 0 && assetsPathIndex + 1 < args.Length)
        {
            App.OverrideAssetsPath = args[assetsPathIndex + 1];
        }

        try
        {
            ProgramShared.Run(
                args,
                services =>
                {
                    services.AddSingleton<IBrowserHost, WebKitGtkBrowserHost>();
                    services.AddSingleton<LinuxOverlayBackendSelector>();
                    services.AddSingleton<ILinuxOverlayBackend>(sp =>
                    {
                        var selector = sp.GetRequiredService<LinuxOverlayBackendSelector>();
                        return selector.SelectBackend();
                    });
                    services.AddSingleton<IOverlaySurface>(sp =>
                    {
                        var backend = sp.GetRequiredService<ILinuxOverlayBackend>();
                        var logger = sp.GetService<ILogger<LinuxOverlaySurface>>();
                        return new LinuxOverlaySurface(backend, logger);
                    });
                    // AI-1 awareness engine seam (linux-foreground-title-contract.md slices A+B):
                    // foreground window TITLE only (no process name/PID). Registered
                    // unconditionally so the engine starts and degrades to Unknown activity when
                    // no backend is available, rather than silently refusing to start (§1.4).
                    // The X11 backend is selected only on a native X11 session; Wayland sessions
                    // resolve to Fallback (honest Unknown) until the wave-3 Wayland backends land.
                    services.AddSingleton<IForegroundWindowTitleProvider>(sp =>
                    {
                        var loggerFactory = sp.GetService<ILoggerFactory>();
                        return new LinuxForegroundWindowTitleProvider(loggerFactory);
                    });
                });

            Console.WriteLine("[CCP Linux] StartWithClassicDesktopLifetime returned cleanly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CCP Linux] FATAL: {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => ProgramShared.BuildAvaloniaApp();
}
