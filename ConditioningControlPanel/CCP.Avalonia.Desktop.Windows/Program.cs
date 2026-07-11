using System;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Desktop;
using ConditioningControlPanel.Avalonia.Desktop.Platform;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;
using ConditioningControlPanel.Avalonia.Infrastructure;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Services;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Ocr;
using ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Webcam;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Core.Services.Webcam;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new DesktopSingleInstanceService();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.SignalFirstInstance(args);
            return;
        }

        var smokeTest = args.Contains("--smoke-test");
        var smokeScreenshots = args.Contains("--smoke-screenshots");
        var benchmark = args.Contains("--benchmark");
        var maxBenchmark = args.Contains("--max-benchmark");
        var verifySpiral = args.Contains("--verify-spiral");
        var verifyLayers = args.Contains("--verify-layers");
        var verifyVideoIndex = Array.IndexOf(args, "--verify-video");
        var verifyVideo = verifyVideoIndex >= 0;
        string? verifyVideoPath = null;
        if (verifyVideo)
        {
            if (verifyVideoIndex + 1 < args.Length && !args[verifyVideoIndex + 1].StartsWith("--", StringComparison.Ordinal))
                verifyVideoPath = args[verifyVideoIndex + 1];
            // Compositor video is the default path now (UCE plan Phase E); force-clear the
            // legacy opt-out so the harness verifies the compositor path regardless of the
            // ambient environment.
            Environment.SetEnvironmentVariable("CCP_LEGACY_VIDEO", null);
        }
        var verifyVisibleIndex = Array.IndexOf(args, "--verify-visible");
        var verifyVisible = verifyVisibleIndex >= 0;
        string? verifyVisiblePath = null;
        if (verifyVisible)
        {
            if (verifyVisibleIndex + 1 < args.Length && !args[verifyVisibleIndex + 1].StartsWith("--", StringComparison.Ordinal))
                verifyVisiblePath = args[verifyVisibleIndex + 1];
            // Compositor video is the default path; clear any ambient legacy opt-out.
            Environment.SetEnvironmentVariable("CCP_LEGACY_VIDEO", null);
        }
        BenchmarkContext.IsEnabled = benchmark || maxBenchmark;
        BenchmarkContext.IsMaxBenchmark = maxBenchmark;
        BenchmarkContext.EntryTimeUtc = DateTime.UtcNow;

        var assetsPathIndex = Array.IndexOf(args, "--assets-path");
        if (assetsPathIndex >= 0 && assetsPathIndex + 1 < args.Length)
        {
            App.OverrideAssetsPath = args[assetsPathIndex + 1];
        }
#if DEBUG
        SmokeTestLogSink? smokeSink = null;
        SmokeTestRunner? smokeRunner = null;
        if (smokeTest)
        {
            smokeSink = new SmokeTestLogSink(LogEventLevel.Warning);
            Logger.Sink = smokeSink;
            smokeRunner = new SmokeTestRunner(smokeSink, captureScreenshots: smokeScreenshots);
            smokeRunner.Attach();
        }
#endif

        App.ConfigurePlatformServices = services =>
        {
            services.AddSingleton<IWallpaperProvider, WpfWallpaperProvider>();
            services.AddTransient<IOverlaySurface, WindowsOverlaySurface>();
            services.AddSingleton<IFrameSource, WindowsFrameSource>();
            services.AddSingleton<ISystemAudioDucker, WindowsSystemAudioDucker>();
            services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>();
            services.AddSingleton<IWindowChrome, WindowsWindowChrome>();
            services.AddSingleton<IAudioDeviceService, WindowsAudioDeviceService>();
            services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
            services.AddSingleton<IBrowserHost, WebView2BrowserHost>();
            services.AddSingleton<IAudioWaveformProvider, NAudioWaveformProvider>();
            services.AddSingleton<AvaloniaWebcamTrackingService>();
            services.AddSingleton<IWebcamService>(sp => sp.GetRequiredService<AvaloniaWebcamTrackingService>());
            services.AddSingleton<ConditioningControlPanel.Core.Services.Webcam.IGazeDriftCorrectionService,
                                  GazeDriftCorrectionService>();
            // Opt-in 3D Chaos tunnel background (WebView2). Resolved as null on heads without a browser host.
            services.AddSingleton<ConditioningControlPanel.Core.Services.Chaos.IChaosTunnelService,
                                  ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Chaos.ChaosTunnelService>();
            // DTRH ("Down the Rabbit Hole") web roguelite game window (WebView2). Null on heads without a browser host.
            services.AddSingleton<ConditioningControlPanel.Core.Services.Chaos.IChaosWebGameService,
                                  ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Chaos.DtrhGameHostService>();
            // Offline keyword wake (sherpa-onnx KWS). Preferred over grammar wake when the model drop-in is present; null on heads without sherpa.
            services.AddSingleton<ConditioningControlPanel.Core.Services.Speech.ISpeechWakeService,
                                  ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Speech.SherpaWakeService>();
            services.AddSingleton<IScreenOcrService, AvaloniaScreenOcrService>();
            // Real offline speech engine (Vosk + NAudio); overrides the shared NullSpeechService.
            services.AddSingleton<ConditioningControlPanel.Core.Services.Speech.ISpeechRecognitionService, WindowsSpeechService>();
            // NAudio clip-duration reader (overrides the no-op) so the mantra/voice layer waits for speech to finish.
            services.AddSingleton<ConditioningControlPanel.Core.Platform.IAudioDurationProvider, NAudioDurationProvider>();
            services.AddDesktopSecretStore();
            services.AddSingleton<ISingleInstanceService>(singleInstance);

            // Use desktop LibVLC registration; LibVLCSharp handles runtime discovery.
            services.AddDesktopLibVLC();
        };

        var builder = BuildAvaloniaApp();
#if DEBUG
        if (smokeRunner != null)
        {
            // ProgramShared.BuildAvaloniaApp replaces the log sink; restore our capturing sink.
            Logger.Sink = smokeSink;
            builder.AfterSetup(_ => smokeRunner.ScheduleRun());
        }
        else if (verifySpiral)
        {
            SpiralVerification.Attach(builder);
        }
        else if (verifyVideo)
        {
            VideoVerification.Attach(builder, verifyVideoPath);
        }
        else if (verifyLayers)
        {
            LayerVerification.Attach(builder);
        }
        else if (verifyVisible)
        {
            VisibleOverlayVerification.Attach(builder, verifyVisiblePath);
        }
#endif

        builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => ProgramShared.BuildAvaloniaApp();
}
