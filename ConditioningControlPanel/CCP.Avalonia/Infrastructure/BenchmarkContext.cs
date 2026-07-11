using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Infrastructure;

/// <summary>
/// Lightweight benchmarking hook for the Avalonia head. Activated with the
/// <c>--benchmark</c> or <c>--max-benchmark</c> command-line flags. Records startup time,
/// memory, frame rate, and CPU usage and writes a JSON report next to the executable.
/// </summary>
public static class BenchmarkContext
{
    public static bool IsEnabled { get; set; }
    public static bool IsMaxBenchmark { get; set; }

    public static DateTime EntryTimeUtc { get; set; } = DateTime.UtcNow;

    public static BenchmarkReport Report { get; } = new();

    /// <summary>
    /// Attaches to the main window lifecycle and records the benchmark sequence.
    /// Call from App.OnFrameworkInitializationCompleted when a desktop lifetime
    /// main window has been created.
    /// </summary>
    public static void Attach(Window mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!IsEnabled) return;

        mainWindow.Opened += async (_, _) =>
        {
            Report.MainWindowShownMs = (DateTime.UtcNow - EntryTimeUtc).TotalMilliseconds;
            Log.Information("[BENCH] MainWindow shown at {MainWindowShownMs:F1} ms", Report.MainWindowShownMs);

            if (IsMaxBenchmark)
            {
                await RunMaxIntensityBenchmarkAsync(mainWindow, desktop, CancellationToken.None);
                return;
            }

            // Sample memory after the app has settled for 10 s. Stay on the UI thread:
            // the session measured below creates a DispatcherTimer that never ticks off it.
            await Task.Delay(TimeSpan.FromSeconds(10));
            SampleMemory();

            // Measure idle frame rate + CPU on the main window.
            await MeasureIdleAsync(mainWindow, CancellationToken.None);

            // Start a quick session and measure frame rate + CPU under load.
            await MeasureActiveSessionAsync(mainWindow, CancellationToken.None);

            WriteReport();

            // Shut down cleanly on the UI thread.
            Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
        };
    }

    private static async Task RunMaxIntensityBenchmarkAsync(Window mainWindow, IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken)
    {
        // 4-minute max benchmark split into 3 phases:
        // Phase 1 (0-60s): All effects at max intensity, local video
        // Phase 2 (60-180s): All effects + web browser video + all layers at full
        // Phase 3 (180-240s): continuing effects + video stress at max intensity
        const int TotalMinutes = 4;
        var totalDuration = TimeSpan.FromMinutes(TotalMinutes);
        Log.Information("[BENCH] Starting {TotalMinutes}-minute MAX-INTENSITY benchmark (all modules, all layers, all phases)", TotalMinutes);

        var services = App.Services;
        if (services == null)
        {
            Log.Error("[BENCH] Service provider not available; aborting max-intensity benchmark");
            Dispatcher.UIThread.Post(() => desktop.Shutdown(1));
            return;
        }

        var settingsService = services.GetRequiredService<ISettingsService>();
        var sessionService = services.GetRequiredService<ISessionService>();
        var orchestrator = services.GetService<ISessionEffectOrchestrator>();

        var flash = services.GetService<IFlashService>();
        var video = services.GetService<IVideoService>();
        var overlay = services.GetService<IOverlayService>();
        var bubbles = services.GetService<IBubbleService>();
        var subliminal = services.GetService<ISubliminalService>();
        var bouncingText = services.GetService<IBouncingTextService>();
        var mindWipe = services.GetService<IMindWipeService>();
        var lockCard = services.GetService<ILockCardService>();
        var popQuiz = services.GetService<IPopQuizService>();
        var bubbleCount = services.GetService<IBubbleCountService>();

        // Build a session that has EVERY effect flag flipped on at MAX settings.
        var session = new Session
        {
            Id = "max-intensity",
            Name = "Max Intensity Benchmark",
            Icon = "🔥",
            DurationMinutes = TotalMinutes,
            IsAvailable = true,
            Source = SessionSource.BuiltIn,
            Settings = new SessionSettings
            {
                FlashEnabled = true,
                FlashPerHour = 300,
                FlashImages = 9999,
                FlashOpacity = 80,
                FlashClickable = true,
                FlashHydra = true,
                SubliminalEnabled = true,
                SubliminalPerMin = 999,
                SubliminalFrames = 10,
                SubliminalOpacity = 80,
                BouncingTextEnabled = true,
                BouncingTextSpeed = 10,
                BouncingTextSize = 200,
                BouncingTextOpacity = 100,
                BouncingTextPhrases = new List<string> { "MAX", "LOAD", "BENCHMARK", "OVERLAY", "VIDEO", "CHAOS", "SPIRAL", "PINK", "TINT", "BUBBLE" },
                PinkFilterEnabled = true,
                // Overlays held at 10% for the benchmark: the point is to measure render load, not
                // to obscure the screen (user request). The <=50% apply-cap also bounds these.
                PinkFilterStartOpacity = 10,
                PinkFilterEndOpacity = 10,
                SpiralEnabled = true,
                SpiralOpacity = 10,
                BrainDrainEnabled = true,
                BrainDrainStartIntensity = 25,
                BrainDrainEndIntensity = 25,
                BubblesEnabled = true,
                BubblesIntermittent = false,
                BubblesFrequency = 99,
                BubblesClickable = true,
                MandatoryVideosEnabled = true,
                VideosPerHour = 9999,
                LockCardEnabled = true,
                LockCardFrequency = 99,
                BubbleCountEnabled = true,
                BubbleCountFrequency = 99,
                PopQuizEnabled = true,
                PopQuizFrequency = 99,
                MindWipeEnabled = true,
                MindWipeBaseMultiplier = 5,
                MindWipeVolume = 25,
            },
            Phases = new List<SessionPhase>()
        };

        // Save original settings to restore later
        var originalSettings = settingsService.Current?.Clone();
        bool originalStrictLock = settingsService.Current?.StrictLockEnabled ?? false;
        // Attention checks (the click-the-words video game) require a human; in a headless
        // benchmark the targets go un-clicked and the mandatory video cuts out early on the
        // attention-fail path, which defeats the point - the FULL video duration is needed to
        // measure render performance. Disable them for the run and restore after (mirrors the
        // StrictLock handling above/below).
        bool originalAttentionChecks = settingsService.Current?.AttentionChecksEnabled ?? false;
        if (settingsService.Current != null)
        {
            settingsService.Current.StrictLockEnabled = false;
            settingsService.Current.AttentionChecksEnabled = false;
        }

        try
        {
            // Session log begin/end is owned by Core SessionService (StartSessionAsync below).
            orchestrator?.StartEffects(session);
            await sessionService.StartSessionAsync(session, cancellationToken);

            // Overlays held at 10% for the benchmark: the point is to measure render load while
            // keeping the screen WATCHABLE, not to obscure it (user request). braindrain was
            // previously forced to 1.0 (100%) which blacked out the screen. The <=50% apply-cap
            // also bounds pink. (MindWipe - a separate full-screen wipe, multiplier 5 in this
            // session - still pulses independently; tone it down separately if it obscures.)
            overlay?.ShowOverlaySustained("braindrain", 0.10);
            overlay?.ShowOverlaySustained("spiral", 0.10);
            overlay?.ShowOverlaySustained("pink", 0.10);

            // Start every individual service at max.
            TryRun("flash", () => flash?.Start());
            TryRun("video", () => video?.Start());
            TryRun("subliminal", () => subliminal?.Start());
            TryRun("bouncing text", () => bouncingText?.Start(session.Settings.BouncingTextPhrases));
            TryRun("bubbles", () => bubbles?.Start());
            TryRun("mind wipe", () => mindWipe?.Start(session.Settings.MindWipeBaseMultiplier, session.Settings.MindWipeVolume / 100.0));
            TryRun("lock card", () => lockCard?.Start());
            TryRun("pop quiz", () => popQuiz?.Start());
            TryRun("bubble count", () => bubbleCount?.Start());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BENCH] Failed to start max-intensity effects");
        }

        // Let effects ramp up for 2 s before phase 1 measurement.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        // ── Phase 1: 0-60s — Everything at max, local video ──
        Log.Information("[BENCH] === PHASE 1 (0-60s): All effects + local video at MAX ===");
        var phase1Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var phase1Task = DriveEffectsAsync(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(200),
            flash, overlay, bubbles, subliminal, video, false, phase1Cts.Token);
        var phase1Video = RunVideoStressSegmentAsync(TimeSpan.FromMinutes(1), video, false, phase1Cts.Token);

        // Frame + CPU measurement for full 4 min
        var frameTask = BenchmarkFrameCounter.MeasureAsync(mainWindow, totalDuration);
        var cpuTask = BenchmarkCpuSampler.SampleAsync(totalDuration, TimeSpan.FromMilliseconds(250), cancellationToken);
        var memoryCts = new CancellationTokenSource();
        var memoryTask = SamplePeakMemoryAsync(totalDuration, TimeSpan.FromSeconds(1), memoryCts.Token);

        await Task.WhenAll(phase1Task, phase1Video);
        phase1Cts.Dispose();
        Log.Information("[BENCH] === PHASE 1 COMPLETE ===");

        // ── Phase 2 (60-180s): Continue all effects + web browser video ──
        Log.Information("[BENCH] === PHASE 2 (60-180s): All effects continue + WEB VIDEO ===");
        var phase2Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var phase2Task = DriveEffectsAsync(TimeSpan.FromMinutes(2), TimeSpan.FromMilliseconds(200),
            flash, overlay, bubbles, subliminal, video, true, phase2Cts.Token);
        var phase2Video = RunWebVideoStressSegmentAsync(TimeSpan.FromMinutes(2), video, phase2Cts.Token);

        await Task.WhenAll(phase2Task, phase2Video);
        phase2Cts.Dispose();
        Log.Information("[BENCH] === PHASE 2 COMPLETE ===");

        // ── Phase 3: 180-240s — continuing effects + video stress ──
        // (Native chaos-run phase dropped with the DTRH strip — web is the sole DTRH path,
        // owner ruling 2026-07-11; the native chaos.StartRun surface no longer exists.)
        Log.Information("[BENCH] === PHASE 3 (180-240s): continuing effects + video stress ===");
        var phase3Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var phase3Task = DriveEffectsAsync(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(200),
            flash, overlay, bubbles, subliminal, video, false, phase3Cts.Token);
        var phase3Video = RunVideoStressSegmentAsync(TimeSpan.FromMinutes(1), video, false, phase3Cts.Token);

        await Task.WhenAll(phase3Task, phase3Video);
        phase3Cts.Dispose();
        Log.Information("[BENCH] === PHASE 3 COMPLETE ===");

        // Wait for remaining measurement tasks
        try { await Task.WhenAll(frameTask, cpuTask, memoryTask); }
        catch (OperationCanceledException) { Log.Information("[BENCH] Benchmark cancelled"); }
        catch (Exception ex) { Log.Error(ex, "[BENCH] Measurement task failed"); }
        finally { memoryCts.Cancel(); }

        var frameResult = frameTask.IsCompletedSuccessfully ? frameTask.Result : null;
        var cpuResult = cpuTask.IsCompletedSuccessfully ? cpuTask.Result : null;
        var memoryResult = memoryTask.IsCompletedSuccessfully ? memoryTask.Result : (WorkingSet: 0L, Peak: 0L, Managed: 0L);

        Report.MaxIntensityDurationSeconds = totalDuration.TotalSeconds;
        Report.MaxIntensityFps = frameResult?.AverageFps ?? 0;
        Report.MaxIntensityMinFps = frameResult?.MinimumFps ?? 0;
        Report.MaxIntensityMaxFps = frameResult?.MaximumFps ?? 0;
        Report.MaxIntensityCpuPercent = cpuResult?.AverageCpuPercent ?? 0;
        Report.MaxIntensityPeakCpuPercent = cpuResult?.PeakCpuPercent ?? 0;
        Report.MaxIntensityWorkingSetBytes = memoryResult.WorkingSet;
        Report.MaxIntensityPeakWorkingSetBytes = memoryResult.Peak;
        Report.MaxIntensityManagedBytes = memoryResult.Managed;

        Log.Information(
            "[BENCH] Max-intensity ({TotalMinutes} min) — AvgFPS={AvgFps:F1}, MinFPS={MinFps:F1}, MaxFPS={MaxFps:F1}, AvgCPU={AvgCpu:F1}%, PeakCPU={PeakCpu:F1}%, WorkingSet={WsMB:F1} MB, Peak={PeakMB:F1} MB, Managed={ManagedMB:F1} MB",
            TotalMinutes,
            Report.MaxIntensityFps, Report.MaxIntensityMinFps, Report.MaxIntensityMaxFps,
            Report.MaxIntensityCpuPercent, Report.MaxIntensityPeakCpuPercent,
            Report.MaxIntensityWorkingSetBytes / 1024.0 / 1024.0,
            Report.MaxIntensityPeakWorkingSetBytes / 1024.0 / 1024.0,
            Report.MaxIntensityManagedBytes / 1024.0 / 1024.0);

        // Cleanup
        try
        {
            sessionService.StopSession(completed: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BENCH] Failed to stop session after max-intensity benchmark");
        }
        finally
        {
            if (settingsService.Current != null && originalSettings != null)
            {
                settingsService.Current.StrictLockEnabled = originalStrictLock;
                settingsService.Current.AttentionChecksEnabled = originalAttentionChecks;
            }
        }

        WriteReport();
        Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
    }

    private static async Task DriveEffectsAsync(
        TimeSpan duration,
        TimeSpan tick,
        IFlashService? flash,
        IOverlayService? overlay,
        IBubbleService? bubbles,
        ISubliminalService? subliminal,
        IVideoService? video,
        bool useWebVideo,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var rng = new Random();
        var variants = ChaosBubbleVariants.All;
        var pending = 0;

        while (sw.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            var remaining = duration - sw.Elapsed;
            var delay = TimeSpan.FromMilliseconds(Math.Min(tick.TotalMilliseconds, remaining.TotalMilliseconds));
            await Task.Delay(delay, cancellationToken);

            // Skip this tick if the UI thread is still processing the previous one.
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0)
                continue;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // TriggerFlash respects the service's own busy guard and shows
                    // FlashImages simultaneous images in one batch, so it is a better
                    // stress load than repeated one-shot calls that get skipped.
                    flash?.TriggerFlash();

                    subliminal?.FlashSubliminal();
                    bubbles?.SpawnOnce();

                    if (variants.Count > 0)
                    {
                        var v = variants[rng.Next(variants.Count)];
                        var spec = ChaosBubbleVariants.Build(v, intensity: 1.0, sizeScale: 1.2);
                        bubbles?.SpawnChaosBubble(spec);
                    }
                    else
                    {
                        // Fallback if the catalog has not been seeded yet.
                        bubbles?.SpawnChaosBubble(new ChaosBubbleSpec
                        {
                            VariantId = "generic",
                            PayloadKind = "generic",
                            SizePx = 80 + rng.NextDouble() * 80,
                            IsLive = rng.NextDouble() < 0.5,
                            FuseMs = 3000,
                            Motion = ChaosMotion.RoamBounce,
                            TintR = 0xFF, TintG = 0x20, TintB = 0x80,
                            EffectIntensity = 1.0,
                        });
                    }

                    // Keep sustained overlays alive in case something clears them (idempotent).
                    // Held at 10% to match the initial force block: braindrain at 1.0 rendered a
                    // full-screen DARK-VIOLET tint (its capture-fallback) on the capture-excluded
                    // surface, which sits ABOVE every other layer and the desktop - that was the
                    // "purple-blue 100%" that blacked out the screen. This tick re-forced it every
                    // 200ms, overriding the initial 0.10, so it had to be lowered here too.
                    overlay?.ShowOverlaySustained("braindrain", 0.10);
                    overlay?.ShowOverlaySustained("spiral", 0.10);
                    overlay?.ShowOverlaySustained("pink", 0.10);

                    // In web video phase, periodically trigger web video
                    if (useWebVideo && rng.NextDouble() < 0.3)
                    {
                        try { video?.PlayUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ"); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[BENCH] Effect drive tick failed");
                }
                finally
                {
                    Interlocked.Exchange(ref pending, 0);
                }
            }, DispatcherPriority.Background);
        }
    }

    private static async Task RunVideoStressSegmentAsync(TimeSpan videoDuration, IVideoService? video, bool useWebVideo, CancellationToken cancellationToken)
    {
        if (video == null) return;

        try
        {
            Log.Information("[BENCH] Starting {Duration}s video stress segment (web={Web})", videoDuration.TotalSeconds, useWebVideo);
            if (useWebVideo)
            {
                await Task.Run(() => video.PlayUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), cancellationToken);
            }
            else
            {
                await Task.Run(() => video.TriggerVideo(), cancellationToken);
            }

            await Task.Delay(videoDuration, cancellationToken);

            await Task.Run(() =>
            {
                video.ForceCleanup();
                video.Stop();
            }, cancellationToken);

            Log.Information("[BENCH] Video stress segment ended");
        }
        catch (OperationCanceledException)
        {
            // Benchmark cancelled — clean up the video window so the app can shut down.
            try { video.ForceCleanup(); video.Stop(); } catch { }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[BENCH] Video stress segment failed");
        }
    }

    private static async Task RunWebVideoStressSegmentAsync(TimeSpan videoDuration, IVideoService? video, CancellationToken cancellationToken)
    {
        if (video == null) return;
        try
        {
            Log.Information("[BENCH] Starting {Duration}s WEB video stress segment", videoDuration.TotalSeconds);
            await Task.Run(() => video.PlayUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), cancellationToken);
            await Task.Delay(videoDuration, cancellationToken);
            await Task.Run(() => { video.ForceCleanup(); video.Stop(); }, cancellationToken);
            Log.Information("[BENCH] Web video stress segment ended");
        }
        catch (OperationCanceledException) { try { video.ForceCleanup(); video.Stop(); } catch { } }
        catch (Exception ex) { Log.Debug(ex, "[BENCH] Web video stress segment failed"); }
    }

    private static async Task<(long WorkingSet, long Peak, long Managed)> SamplePeakMemoryAsync(TimeSpan duration, TimeSpan interval, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        long peak = 0;
        long lastWorkingSet = 0;
        long lastManaged = 0;

        while (sw.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            using var proc = Process.GetCurrentProcess();
            proc.Refresh();
            lastWorkingSet = proc.WorkingSet64;
            lastManaged = GC.GetTotalMemory(forceFullCollection: false);
            if (lastWorkingSet > peak) peak = lastWorkingSet;
        }

        return (WorkingSet: lastWorkingSet, Peak: peak, Managed: lastManaged);
    }

    private static async Task MeasureIdleAsync(Window mainWindow, CancellationToken cancellationToken)
    {
        Log.Information("[BENCH] Measuring idle frame rate + CPU for 5 s...");

        var frameTask = BenchmarkFrameCounter.MeasureAsync(mainWindow, TimeSpan.FromSeconds(5));
        var cpuTask = BenchmarkCpuSampler.SampleAsync(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250), cancellationToken);

        await Task.WhenAll(frameTask, cpuTask);

        Report.IdleFps = frameTask.Result.AverageFps;
        Report.IdleMinFps = frameTask.Result.MinimumFps;
        Report.IdleMaxFps = frameTask.Result.MaximumFps;
        Report.IdleCpuPercent = cpuTask.Result.AverageCpuPercent;
        Report.IdlePeakCpuPercent = cpuTask.Result.PeakCpuPercent;

        Log.Information(
            "[BENCH] Idle — AvgFPS={AvgFps:F1}, MinFPS={MinFps:F1}, MaxFPS={MaxFps:F1}, AvgCPU={AvgCpu:F1}%, PeakCPU={PeakCpu:F1}%",
            Report.IdleFps, Report.IdleMinFps, Report.IdleMaxFps, Report.IdleCpuPercent, Report.IdlePeakCpuPercent);
    }

    private static async Task MeasureActiveSessionAsync(Window mainWindow, CancellationToken cancellationToken)
    {
        try
        {
            var settingsService = App.Services.GetRequiredService<ISettingsService>();
            var sessionService = App.Services.GetRequiredService<ISessionService>();

            var session = Session.QuickStartFromSettings(settingsService.Current);
            Log.Information("[BENCH] Starting quick-start session for active-load measurement...");
            await sessionService.StartSessionAsync(session, cancellationToken);

            // Let effects ramp up for 2 s before measuring.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var frameTask = BenchmarkFrameCounter.MeasureAsync(mainWindow, TimeSpan.FromSeconds(10));
            var cpuTask = BenchmarkCpuSampler.SampleAsync(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), cancellationToken);

            await Task.WhenAll(frameTask, cpuTask);

            Report.ActiveSessionFps = frameTask.Result.AverageFps;
            Report.ActiveSessionMinFps = frameTask.Result.MinimumFps;
            Report.ActiveSessionMaxFps = frameTask.Result.MaximumFps;
            Report.ActiveSessionCpuPercent = cpuTask.Result.AverageCpuPercent;
            Report.ActiveSessionPeakCpuPercent = cpuTask.Result.PeakCpuPercent;

            Log.Information(
                "[BENCH] Active session — AvgFPS={AvgFps:F1}, MinFPS={MinFps:F1}, MaxFPS={MaxFps:F1}, AvgCPU={AvgCpu:F1}%, PeakCPU={PeakCpu:F1}%",
                Report.ActiveSessionFps, Report.ActiveSessionMinFps, Report.ActiveSessionMaxFps,
                Report.ActiveSessionCpuPercent, Report.ActiveSessionPeakCpuPercent);

            sessionService.StopSession(completed: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BENCH] Active session measurement failed");
        }
    }

    private static void SampleMemory()
    {
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        Report.WorkingSetBytes10s = proc.WorkingSet64;
        Report.PrivateMemoryBytes10s = proc.PrivateMemorySize64;
        Report.PeakWorkingSetBytes10s = proc.PeakWorkingSet64;
        Report.ManagedMemoryBytes10s = GC.GetTotalMemory(forceFullCollection: false);
        Log.Information(
            "[BENCH] 10 s memory — WorkingSet={WorkingSetMB:F1} MB, Private={PrivateMB:F1} MB, Peak={PeakMB:F1} MB, Managed={ManagedMB:F1} MB",
            Report.WorkingSetBytes10s / 1024.0 / 1024.0,
            Report.PrivateMemoryBytes10s / 1024.0 / 1024.0,
            Report.PeakWorkingSetBytes10s / 1024.0 / 1024.0,
            Report.ManagedMemoryBytes10s / 1024.0 / 1024.0);
    }

    private static void WriteReport()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "benchmark-report.json");
            var json = JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Log.Information("[BENCH] Report written to {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BENCH] Failed to write report");
        }
    }

    private static void TryRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[BENCH] Effect '{Name}' failed", name);
        }
    }
}

public sealed class BenchmarkReport
{
    public double MainWindowShownMs { get; set; }
    public long WorkingSetBytes10s { get; set; }
    public long PrivateMemoryBytes10s { get; set; }
    public long PeakWorkingSetBytes10s { get; set; }
    public long ManagedMemoryBytes10s { get; set; }

    public double IdleFps { get; set; }
    public double IdleMinFps { get; set; }
    public double IdleMaxFps { get; set; }
    public double IdleCpuPercent { get; set; }
    public double IdlePeakCpuPercent { get; set; }

    public double ActiveSessionFps { get; set; }
    public double ActiveSessionMinFps { get; set; }
    public double ActiveSessionMaxFps { get; set; }
    public double ActiveSessionCpuPercent { get; set; }
    public double ActiveSessionPeakCpuPercent { get; set; }

    public double MaxIntensityDurationSeconds { get; set; }
    public double MaxIntensityFps { get; set; }
    public double MaxIntensityMinFps { get; set; }
    public double MaxIntensityMaxFps { get; set; }
    public double MaxIntensityCpuPercent { get; set; }
    public double MaxIntensityPeakCpuPercent { get; set; }
    public long MaxIntensityWorkingSetBytes { get; set; }
    public long MaxIntensityPeakWorkingSetBytes { get; set; }
    public long MaxIntensityManagedBytes { get; set; }
}
