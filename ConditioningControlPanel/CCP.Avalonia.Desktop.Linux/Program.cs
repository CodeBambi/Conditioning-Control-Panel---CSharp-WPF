using System;
using System.Linq;
using Avalonia;
#if DEBUG
using Avalonia.Logging;
// The shared smoke harness (SmokeTestRunner/SmokeTestLogSink) is source-linked from
// tests/CCP.Avalonia.Desktop.Windows.Smoke into Debug builds only and keeps its
// original namespace; see CCP.Avalonia.Desktop.Linux.csproj.
using ConditioningControlPanel.Avalonia.Desktop.Windows;
#endif
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

        // Debug-only smoke harness (mirrors CCP.Avalonia.Desktop.Windows/Program.cs):
        // --smoke-test walks every tab/dialog headlessly and writes
        // smoke-test-report.json next to the binary. Inert in Release builds.
        Action<AppBuilder>? configureBuilder = null;
#if DEBUG
        if (args.Contains("--smoke-test"))
        {
            var smokeSink = new SmokeTestLogSink(LogEventLevel.Warning);
            Logger.Sink = smokeSink;
            var smokeRunner = new SmokeTestRunner(smokeSink, captureScreenshots: args.Contains("--smoke-screenshots"));
            smokeRunner.Attach();
            configureBuilder = builder =>
            {
                // ProgramShared.BuildAvaloniaApp replaces the log sink (LogToTrace);
                // restore our capturing sink before the lifetime starts.
                Logger.Sink = smokeSink;
                builder.AfterSetup(_ => smokeRunner.ScheduleRun());
            };
        }
#endif

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
                    // Screen-capture seam (linux-framesource-contract.md slices A+B):
                    // X11 XGetImage on native X11 sessions; black-frame fallback on
                    // Wayland/XWayland/unknown until the portal backends (slices D-F)
                    // land. Strictly pull-based: the backend is selected lazily on first
                    // resolution and no capture happens until a consuming feature calls
                    // CaptureAsync. Frames are memory-only, never persisted (§1.4).
                    services.AddSingleton<LinuxFrameSourceBackendSelector>(sp =>
                        new LinuxFrameSourceBackendSelector(sp.GetService<ILoggerFactory>()));
                    services.AddSingleton<IFrameSource>(sp =>
                    {
                        var selector = sp.GetRequiredService<LinuxFrameSourceBackendSelector>();
                        var backend = selector.SelectBackend();
                        return new LinuxFrameSource(backend, sp.GetService<ILogger<LinuxFrameSource>>());
                    });
                },
                configureBuilder);

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
