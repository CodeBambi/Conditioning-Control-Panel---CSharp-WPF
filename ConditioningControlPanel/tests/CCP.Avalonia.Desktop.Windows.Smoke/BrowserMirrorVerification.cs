using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;
// Force the Core PixelRect to win over Avalonia.PixelRect (both are in scope via the usings above).
using PixelRect = ConditioningControlPanel.Core.Platform.PixelRect;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-browser-mirror harness (multimon mirror verification, eyes+logs). Proves the
/// <see cref="BrowserMirrorVideoLayer"/> activates, its GDI capture loop produces frames, and
/// the compositor renders it on every per-monitor window without crashing. Single-monitor boxes
/// cannot show the mirror paint (the source monitor is skipped to avoid self-capture feedback),
/// so the screenshot captures the SOURCE content (a distinctive test card) plus — on a
/// multi-monitor box — the mirrored copies on every OTHER monitor in one combined virtual-desktop
/// shot. Frame counts are sampled twice to prove the capture loop is live (monotonic increase).
/// Exit code 0 on success (mirror registered + active + frames captured &gt; 0), 2 otherwise.
/// </summary>
internal static class BrowserMirrorVerification
{
    private const string LogPrefix = "[BROWSERMIRROR]";

    public static void Attach(AppBuilder builder)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background));
    }

    private static async Task RunAsync()
    {
        Window? testCardWindow = null;
        string? testCardPath = null;
        BrowserMirrorVideoService? mirror = null;
        CompositorEngine? engine = null;
        try
        {
            await Task.Delay(2500); // let splash/init settle

            var services = App.Services;
            if (services == null) { Console.WriteLine($"{LogPrefix} App.Services not available."); Fail(); return; }

            engine = services.GetService<CompositorEngine>();
            mirror = services.GetService<BrowserMirrorVideoService>();
            var screens = services.GetService<IScreenProvider>();
            if (engine == null || mirror == null || screens == null)
            {
                Console.WriteLine($"{LogPrefix} Missing DI services (engine={engine != null}, mirror={mirror != null}, screens={screens != null}).");
                Fail();
                return;
            }

            var allScreens = screens.GetAllScreens();
            var primary = screens.GetPrimaryScreen() ?? allScreens.FirstOrDefault();
            Console.WriteLine($"{LogPrefix} screen count={allScreens.Count}, primary={primary?.Name} {primary?.Bounds}");
            foreach (var s in allScreens)
                Console.WriteLine($"{LogPrefix}   screen {s.Name} bounds={s.Bounds} scaling={s.Scaling}");

            // Quietchase any auto-started session/effects so the screenshot isolates the mirror
            // (otherwise a resumed session paints pink tint / subliminals / video over the test card
            // and the proof shot is unreadable). Best-effort — a missing seam just leaves that effect.
            try { services.GetService<ISessionService>()?.StopSession(); } catch { }
            try { services.GetService<IVideoService>()?.Stop(); } catch { }
            try { services.GetService<IFlashService>()?.Stop(); } catch { }
            try { services.GetService<IOverlayService>()?.HideOverlaySustained("pink"); } catch { }
            try { services.GetService<IOverlayService>()?.HideOverlaySustained("spiral"); } catch { }
            await Task.Delay(600);

            // Stage a distinctive test card fullscreen on the primary so the capture has unmistakable
            // content and the screenshot is recognizable (the source the mirror copies FROM).
            testCardPath = CreateTestCard();
            testCardWindow = ShowTestCard(testCardPath, primary);
            await Task.Delay(800);

            // Activate the mirror (same call the browser-fullscreen path makes). Captures the primary
            // and paints a stretched copy on every OTHER compositor window.
            mirror.Start(primary);
            await Task.Delay(2000);

            var layer = engine.GetLayer(CompositorLayers.BrowserMirrorVideo) as BrowserMirrorVideoLayer;
            var framesA = layer?.FramesCaptured ?? -1;
            await Task.Delay(800);
            var framesB = layer?.FramesCaptured ?? -1;

            Console.WriteLine($"{LogPrefix} mirror.IsMirroring={mirror.IsMirroring} layerRegistered={layer != null} layerActive={layer?.IsActive}");
            Console.WriteLine($"{LogPrefix} engine.IsRunning={engine.IsRunning} engine.WindowCount={engine.WindowCount} (one per monitor)");
            Console.WriteLine($"{LogPrefix} layer.Source={layer?.Source?.Name} (skipped on its own compositor window; painted on every other)");
            Console.WriteLine($"{LogPrefix} capture frames: t0={framesA}, t1={framesB} (increase => live capture loop)");

            // Combined virtual-desktop screenshot: source (test card) +, on multi-monitor, the mirrored
            // copies on every other monitor. Saved under the head bin logs folder.
            var shotPath = SaveCombinedScreenshot(allScreens);
            Console.WriteLine($"{LogPrefix} SCREENSHOT: {shotPath}");

            var ok = mirror.IsMirroring && layer != null && layer.IsActive && framesB > 0;
            Console.WriteLine($"{LogPrefix} RESULT: {(ok ? "PASS" : "FAIL")} (mirror active + frames captured)");
            Environment.ExitCode = ok ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{LogPrefix} ERROR: {ex}");
            Fail();
        }
        finally
        {
            try { mirror?.Stop(); } catch { }
            try { testCardWindow?.Close(); } catch { }
            if (testCardPath != null) { try { File.Delete(testCardPath); } catch { } }
            await Task.Delay(500);
            Shutdown();
        }
    }

    private static string CreateTestCard()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-verify-browser-mirror-{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(960, 540);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, 20, 20, 80));
            using var pen = new Pen(Color.White, 10);
            for (int i = -540; i < 960; i += 60) g.DrawLine(pen, i, 0, i + 540, 540);
            using var font = new Font("Segoe UI", 40, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString("BROWSER MIRROR TEST CARD", font, brush, 70, 220);
            using var font2 = new Font("Segoe UI", 20, FontStyle.Bold);
            g.DrawString("source content copied to every other monitor", font2, brush, 70, 300);
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static Window ShowTestCard(string imagePath, ScreenInfo? primary)
    {
        var bounds = primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowState = WindowState.FullScreen,
            Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Black),
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new global::Avalonia.Controls.Image
            {
                Source = new global::Avalonia.Media.Imaging.Bitmap(imagePath),
                Stretch = global::Avalonia.Media.Stretch.Fill,
            },
        };
        window.Show();
        return window;
    }

    /// <summary>One screenshot spanning the union of every screen's bounds: the source monitor shows
    /// the test card, every OTHER monitor shows the mirrored (stretched) copy of it.</summary>
    private static string SaveCombinedScreenshot(System.Collections.Generic.IReadOnlyList<ScreenInfo> screens)
    {
        if (screens.Count == 0) screens = new[] { new ScreenInfo("fallback", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1.0) };
        var minX = (int)Math.Floor(screens.Min(s => s.Bounds.X));
        var minY = (int)Math.Floor(screens.Min(s => s.Bounds.Y));
        var maxX = (int)Math.Ceiling(screens.Max(s => s.Bounds.Right));
        var maxY = (int)Math.Ceiling(screens.Max(s => s.Bounds.Bottom));
        var w = Math.Max(1, maxX - minX);
        var h = Math.Max(1, maxY - minY);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // CopyFromScreen coordinates are virtual-desktop absolute; translate by (minX,minY) so the
            // union fits into the bitmap origin.
            g.CopyFromScreen(minX, minY, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        var path = Path.Combine(dir, $"browser-mirror-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static void Fail() => Environment.ExitCode = 2;

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
