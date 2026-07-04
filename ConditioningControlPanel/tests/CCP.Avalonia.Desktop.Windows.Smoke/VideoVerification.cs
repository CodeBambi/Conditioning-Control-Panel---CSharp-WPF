using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-video &lt;path&gt; harness (UCE plan Phase A acceptance probe). Program.cs sets
/// CCP_UCE_VIDEO=1 before DI construction so <see cref="AvaloniaVideoService"/> registers
/// <see cref="MandatoryVideoLayer"/> (Z=<see cref="CompositorLayers.MandatoryVideo"/>) with the
/// compositor engine. This harness then asserts the full frame path:
///   1. the mandatory-video layer is registered with the engine (opt-in wiring works),
///   2. PlaySpecificVideo produces a decoded frame in the layer (LibVLC vmem callbacks fire and
///      the copy tick publishes a bitmap) — allows for the 1.3s pre-announce delay,
///   3. the engine is running with at least one CompositorWindow per expected monitor,
///   4. frames keep advancing across ~700ms (live playback, not a single frozen frame).
/// Exit code 0 on success, 2 on any failure. Mirrors <see cref="SpiralVerification"/>.
/// </summary>
internal static class VideoVerification
{
    public static void Attach(AppBuilder builder, string? videoPath)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(videoPath), DispatcherPriority.Background));
    }

    private static async Task RunAsync(string? videoPath)
    {
        var pass = false;
        IVideoService? videoService = null;
        try
        {
            await Task.Delay(2000); // let splash/init settle

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                Console.WriteLine("[VIDEO] Main window not available.");
                return;
            }

            var services = App.Services;
            if (services == null)
            {
                Console.WriteLine("[VIDEO] App.Services not available.");
                return;
            }

            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                Console.WriteLine($"[VIDEO] Usage: --verify-video <existing local video file>. Got: '{videoPath}'.");
                return;
            }

            // Stage 1: the mandatory-video layer must be registered with the engine.
            // AvaloniaVideoService registers it in its ctor when CCP_UCE_VIDEO=1 (set by Program.cs
            // for this switch); a failure here means the opt-in wiring or DI ordering is broken.
            var engine = services.GetService<CompositorEngine>();
            if (engine == null)
            {
                Console.WriteLine("[VIDEO] CompositorEngine is not registered in DI.");
                return;
            }

            videoService = services.GetService<IVideoService>();
            if (videoService == null)
            {
                Console.WriteLine("[VIDEO] IVideoService is not registered in DI.");
                return;
            }

            if (engine.GetLayer(CompositorLayers.MandatoryVideo) is not MandatoryVideoLayer videoLayer)
            {
                Console.WriteLine(
                    "[VIDEO] MandatoryVideoLayer is not registered with the compositor engine. " +
                    "CCP_UCE_VIDEO=1 must be set before AvaloniaVideoService construction (Program.cs does " +
                    "this for --verify-video); if it was, the ctor registration path is broken.");
                return;
            }
            Console.WriteLine("[VIDEO] MandatoryVideoLayer registered at Z=" + CompositorLayers.MandatoryVideo + ".");

            var framesBefore = videoLayer.FramesCopied;
            videoService.PlaySpecificVideo(videoPath, strictMode: false);
            Console.WriteLine($"[VIDEO] PlaySpecificVideo issued for {Path.GetFileName(videoPath)}.");

            // Stage 2: wait for the first decoded frame. Budget covers the service's 1.3s
            // pre-announce timer + LibVLC open/decode; a timeout here means frame delivery is
            // broken (callbacks not firing, buffer invalid, or media failed to open — check
            // app-*.log for 'VideoLayer:' lines to bisect).
            var gotFrame = false;
            for (var i = 0; i < 60; i++) // up to 15s
            {
                await Task.Delay(250);
                if (videoLayer.HasRenderedFrame) { gotFrame = true; break; }
            }

            if (!gotFrame)
            {
                Console.WriteLine(
                    "[VIDEO] No decoded frame within 15s. Bisect via app-*.log 'VideoLayer:' lines: " +
                    "no 'started' line = play path broken; 'started' but no 'first frame copied' = " +
                    "vmem callbacks/copy tick broken; 'EncounteredError' = media failed to open/decode.");
                return;
            }
            Console.WriteLine("[VIDEO] First decoded frame published to the layer.");

            if (!videoLayer.IsActive)
            {
                Console.WriteLine("[VIDEO] Layer has a frame but IsActive is false (buffer gate broken).");
                return;
            }

            // Stage 3: engine running with one main-surface window per expected monitor.
            await Task.Delay(800); // staggered window creation is ~250ms per extra monitor

            var settings = services.GetService<ISettingsService>()?.Current;
            var screenProvider = services.GetService<IScreenProvider>();
            var screenCount = 0;
            try { screenCount = screenProvider?.GetAllScreens().Count ?? 0; } catch { }
            var expectedMonitors = settings?.DualMonitorEnabled == true ? Math.Max(1, screenCount) : 1;

            if (!engine.IsRunning)
            {
                Console.WriteLine("[VIDEO] CompositorEngine is not running while the video layer is active.");
                return;
            }

            if (engine.WindowCount < expectedMonitors)
            {
                Console.WriteLine($"[VIDEO] Expected >= {expectedMonitors} CompositorWindow(s) (screens={screenCount}), found {engine.WindowCount}.");
                return;
            }
            Console.WriteLine($"[VIDEO] CompositorEngine running with {engine.WindowCount} window(s) (expected >= {expectedMonitors}).");

            // Stage 4 (gating): frames must keep advancing — proves live playback through the
            // vmem copy tick, not a single frozen frame.
            var sample1 = videoLayer.FramesCopied;
            await Task.Delay(700);
            var sample2 = videoLayer.FramesCopied;
            if (sample2 <= sample1)
            {
                Console.WriteLine($"[VIDEO] Frame counter did not advance across 700ms ({sample1} -> {sample2}); playback is frozen.");
                return;
            }
            Console.WriteLine($"[VIDEO] Frames advancing ({sample1} -> {sample2} across ~700ms; {framesBefore} at start).");

            // Stage 5 (gating, Phase B1): the service must report IsPlaying while the UCE layer
            // plays — the legacy expression was window-based and read false on this path,
            // starving the interaction queue and the #462 HasOpenVideoWindows DIM.
            if (!videoService.IsPlaying)
            {
                Console.WriteLine("[VIDEO] IVideoService.IsPlaying is false while the UCE layer plays (Phase B1 decoupling broken).");
                return;
            }
            Console.WriteLine("[VIDEO] IVideoService.IsPlaying reflects the UCE layer.");

            // Stage 6 (Phase B1 audio): the layer's player must carry the effective volume
            // (master*video/100). Gate only when the expected volume is > 0; at 0 the helper
            // mutes without writing Volume, so the raw value is undefined.
            var master = Math.Clamp(settings?.MasterVolume ?? 50, 0, 100);
            var video = Math.Clamp(settings?.VideoVolume ?? 50, 0, 100);
            var expectedVolume = (int)(master * video / 100.0);
            var playerVolume = videoLayer.PlayerVolume;
            if (expectedVolume > 0 && playerVolume != expectedVolume)
            {
                Console.WriteLine($"[VIDEO] Layer player volume {playerVolume?.ToString() ?? "null"} != effective volume {expectedVolume} (Phase B1 audio routing broken).");
                return;
            }
            Console.WriteLine($"[VIDEO] Audio applied (player volume {playerVolume?.ToString() ?? "null"}, effective {expectedVolume}).");

            // Non-gating Phase B2 diagnostic: report whether the layer-path orchestration
            // (safety timer + attention scheduling) armed on the mandatory layer's Playing
            // event. Attention targets themselves spawn only when the user's
            // AttentionChecksEnabled setting is on; this probe reads state only — it never
            // mutates settings and never gates the verdict.
            var orchestrationArmed =
                (videoService as ConditioningControlPanel.Avalonia.Services.Video.AvaloniaVideoService)?.LayerOrchestrationArmed;
            Console.WriteLine($"[VIDEO] Diagnostic (non-gating): layer orchestration armed = {orchestrationArmed?.ToString() ?? "unknown (service type mismatch)"}.");

            Console.WriteLine("[VIDEO] PASS: mandatory video decodes, publishes frames, and composites on every expected monitor.");
            pass = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VIDEO] ERROR: {ex}");
        }
        finally
        {
            Environment.ExitCode = pass ? 0 : 2;
            try { videoService?.Stop(); } catch { }
            await Task.Delay(500); // let teardown settle before shutdown
            Shutdown();
        }
    }

    private static void Shutdown()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                lifetime?.Shutdown();
            }
            catch { }
        });
    }
}
