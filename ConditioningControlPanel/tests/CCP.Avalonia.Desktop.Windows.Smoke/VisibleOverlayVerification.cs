using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-visible [path] harness (UCE plan Phase E eyes-verification). Meant for a HUMAN to
/// watch. Two stages:
///   STAGE 1 (no video): shows pink tint + spiral + big flash popups on the desktop so the
///     overlays are unmistakable on a static background.
///   STAGE 2 (video underneath): starts a mandatory video through the compositor and keeps the
///     overlays + flash popups running, so you can confirm the video sits UNDER the overlays
///     (never covers them — the reported z-order bug).
/// Each stage prints a [DUMP ...] line with the engine window count and each layer's
/// registered/active state, so even a missed effect is diagnosable from the console.
/// Compositor video is the default path now (no CCP_UCE_VIDEO opt-in). Exit code 0.
/// </summary>
internal static class VisibleOverlayVerification
{
    public static void Attach(AppBuilder builder, string? videoPath)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(videoPath), DispatcherPriority.Background));
    }

    private static async Task RunAsync(string? videoPathArg)
    {
        string? tempA = null;
        string? tempB = null;
        IVideoService? video = null;
        IFlashService? flash = null;
        IOverlayService? overlay = null;
        try
        {
            await Task.Delay(2500); // let splash/init settle

            var services = App.Services;
            if (services == null) { Console.WriteLine("[VISIBLE] App.Services not available."); return; }

            var engine = services.GetService<CompositorEngine>();
            video = services.GetService<IVideoService>();
            flash = services.GetService<IFlashService>();
            overlay = services.GetService<IOverlayService>();
            var settings = services.GetService<ISettingsService>()?.Current;
            if (engine == null || video == null || flash == null || overlay == null)
            {
                Console.WriteLine($"[VISIBLE] Missing DI services (engine={engine != null}, video={video != null}, flash={flash != null}, overlay={overlay != null}).");
                return;
            }

            Console.WriteLine($"[VISIBLE] User settings: FlashOpacity={settings?.FlashOpacity}, SpiralEnabled={settings?.SpiralEnabled}, SpiralOpacity={settings?.SpiralOpacity}, PinkFilterEnabled={settings?.PinkFilterEnabled}, PinkFilterOpacity={settings?.PinkFilterOpacity}");

            flash.Start(); // TriggerFlashOnce is gated on IsRunning
            tempA = CreateTempImage(Color.FromArgb(255, 255, 60, 200), "IMG POPUP A");
            tempB = CreateTempImage(Color.FromArgb(255, 60, 200, 255), "IMG POPUP B");

            // ---------- STAGE 1: overlays + flash, NO video ----------
            Console.WriteLine();
            Console.WriteLine("======== STAGE 1 (~6s): NO VIDEO ========");
            Console.WriteLine(" On your DESKTOP you should now see: a PINK tint, a faint SPIRAL, and BIG");
            Console.WriteLine(" pink/blue 'IMG POPUP' boxes appearing. (Spiral maxes at ~10% opacity by design.)");
            overlay.ShowOverlaySustained("pink", 0.2);
            overlay.ShowOverlaySustained("spiral", 0.2);
            flash.TriggerFlashOnce(tempA, durationMs: 5000, playSound: false, suppressHaptic: true);
            await Task.Delay(1200);
            Dump(engine, "STAGE 1 (overlays only, no video)");
            flash.TriggerFlashOnce(tempB, durationMs: 5000, playSound: false, suppressHaptic: true);
            await Task.Delay(4500);

            // ---------- STAGE 2: mandatory video UNDER the overlays ----------
            var videoPath = ResolveVideoPath(videoPathArg, services);
            Console.WriteLine();
            Console.WriteLine("======== STAGE 2 (~14s): MANDATORY VIDEO underneath ========");
            Console.WriteLine($" Video: {videoPath ?? "(none found)"}");
            Console.WriteLine(" The video should appear UNDER the pink tint / spiral, and the flash popups");
            Console.WriteLine(" should keep appearing OVER the video. The video must NOT cover the overlays.");
            if (videoPath != null)
                video.PlaySpecificVideo(videoPath, strictMode: false);

            var videoLayer = engine.GetLayer(CompositorLayers.MandatoryVideo) as MandatoryVideoLayer;
            for (var i = 0; i < 24 && videoLayer != null; i++)
            {
                await Task.Delay(250);
                if (videoLayer.HasRenderedFrame) break;
            }
            Dump(engine, "STAGE 2 (video started + overlays)");

            for (var round = 1; round <= 6; round++)
            {
                var img = (round % 2 == 0) ? tempB : tempA;
                try { flash.TriggerFlashOnce(img, durationMs: 3500, playSound: false, suppressHaptic: true); } catch { }
                Console.WriteLine($"[VISIBLE] Stage 2 flash popup {round}/6...");
                await Task.Delay(2000);
                if (round == 3) Dump(engine, "STAGE 2 mid-run");
            }

            Console.WriteLine("[VISIBLE] Done. Tell me what you saw in STAGE 1 vs STAGE 2 (video / pink / spiral / flash popups).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VISIBLE] ERROR: {ex}");
        }
        finally
        {
            Environment.ExitCode = 0; // human-judged; never fail the process
            try { overlay?.HideOverlaySustained("spiral"); } catch { }
            try { overlay?.HideOverlaySustained("pink"); } catch { }
            try { flash?.Stop(); } catch { }
            try { video?.Stop(); } catch { }
            if (tempA != null) { try { File.Delete(tempA); } catch { } }
            if (tempB != null) { try { File.Delete(tempB); } catch { } }
            await Task.Delay(600);
            Shutdown();
        }
    }

    private static void Dump(CompositorEngine engine, string label)
    {
        string State(int z)
        {
            var layer = engine.GetLayer(z);
            return layer == null ? "MISSING" : (layer.IsActive ? "ACTIVE" : "idle");
        }
        var flashLayer = engine.GetLayer(CompositorLayers.Flash) as FlashLayer;
        Console.WriteLine(
            $"[VISIBLE][DUMP {label}] engine.IsRunning={engine.IsRunning} windows={engine.WindowCount} excluded={engine.ExcludedWindowCount} | " +
            $"Video(Z{CompositorLayers.MandatoryVideo})={State(CompositorLayers.MandatoryVideo)} " +
            $"Flash(Z{CompositorLayers.Flash})={State(CompositorLayers.Flash)}(count={flashLayer?.ActiveCount.ToString() ?? "n/a"}) " +
            $"Spiral(Z{CompositorLayers.Spiral})={State(CompositorLayers.Spiral)} " +
            $"PinkTint(Z{CompositorLayers.PinkTint})={State(CompositorLayers.PinkTint)}");
    }

    private static string? ResolveVideoPath(string? arg, IServiceProvider services)
    {
        if (!string.IsNullOrWhiteSpace(arg) && File.Exists(arg))
            return arg;

        try
        {
            var assets = services.GetService<IAppEnvironment>()?.EffectiveAssetsPath;
            if (!string.IsNullOrWhiteSpace(assets))
            {
                var videosDir = Path.Combine(assets, "videos");
                if (Directory.Exists(videosDir))
                {
                    foreach (var f in Directory.EnumerateFiles(videosDir))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is ".mp4" or ".webm" or ".avi" or ".mov")
                            return f;
                    }
                }
            }
        }
        catch { }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos", "_test_loop.mp4");
        return File.Exists(bundled) ? bundled : null;
    }

    private static string CreateTempImage(Color color, string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-verify-visible-{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(760, 520);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
            using var pen = new Pen(Color.White, 12);
            g.DrawRectangle(pen, 16, 16, 760 - 32, 520 - 32);
            using var font = new Font("Segoe UI", 44, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString(label, font, brush, 60, 220);
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
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
