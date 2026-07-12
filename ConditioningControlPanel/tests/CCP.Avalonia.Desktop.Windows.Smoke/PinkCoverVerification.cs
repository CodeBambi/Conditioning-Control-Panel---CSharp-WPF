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
/// --verify-pink-cover harness (the pink-cover regression test). Reproduces the exact failure
/// scenario from the bug report: a 50% PinkTint (the UCE cap) active over a fullscreen browser
/// video that is being multi-monitor-mirrored by <see cref="BrowserMirrorVideoLayer"/>. Before
/// the fix, after ~1s the screen went SOLID pink because the mirror painted an opaque black
/// background that the 50% tint then composited into a solid dark-pink cover. After the fix the
/// mirror never paints the opaque fill, so the captured video frame stays visible through the
/// capped 50% tint and the screen is a tinted video, NOT a solid color.
///
/// The harness stages a distinctive LIME test card fullscreen on the primary monitor (the
/// browser video stand-in), enables the pink filter at the capped 50%, starts the mirror,
/// waits well past the 1s mark, then captures a combined virtual-desktop screenshot via the
/// same GDI CopyFromScreen path the mirror itself uses. It saves the screenshot under the
/// head bin logs folder and reports whether the test card's signature color survived
/// (video visible) or was overwhelmed by a solid pink cover. Exit 0 on PASS, 2 on FAIL.
/// </summary>
internal static class PinkCoverVerification
{
    private const string LogPrefix = "[PINKCOVER]";

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
        IOverlayService? overlays = null;
        try
        {
            await Task.Delay(2500); // let splash/init settle

            var services = App.Services;
            if (services == null) { Console.WriteLine($"{LogPrefix} App.Services not available."); Fail(); return; }

            engine = services.GetService<CompositorEngine>();
            mirror = services.GetService<BrowserMirrorVideoService>();
            var screens = services.GetService<IScreenProvider>();
            overlays = services.GetService<IOverlayService>();
            if (engine == null || mirror == null || screens == null || overlays == null)
            {
                Console.WriteLine($"{LogPrefix} Missing DI services (engine={engine != null}, mirror={mirror != null}, screens={screens != null}, overlays={overlays != null}).");
                Fail();
                return;
            }

            var allScreens = screens.GetAllScreens();
            var primary = screens.GetPrimaryScreen() ?? allScreens.FirstOrDefault();
            Console.WriteLine($"{LogPrefix} screen count={allScreens.Count}, primary={primary?.Name} {primary?.Bounds}");

            // Quiet everything that could paint over the test card EXCEPT the pink filter.
            try { services.GetService<ISessionService>()?.StopSession(); } catch { }
            try { services.GetService<IVideoService>()?.Stop(); } catch { }
            try { services.GetService<IFlashService>()?.Stop(); } catch { }
            try { overlays.HideOverlaySustained("spiral"); } catch { }
            try { overlays.HideOverlaySustained("braindrain"); } catch { }
            await Task.Delay(500);

            // Stage a distinctive LIME test card fullscreen on the primary monitor — the browser
            // video stand-in. Lime was chosen so a solid-pink cover (the regression signature) is
            // unmistakable: the screenshot's center would have ~0 green channel. The card also has
            // diagonal white lines and big text so the human reviewer (owner on the rig) can see at
            // a glance whether the video is visible through the tint.
            testCardPath = CreateTestCard();
            testCardWindow = ShowTestCard(testCardPath, primary);
            await Task.Delay(800);

            // Start the mirror first (so it begins capturing the test card), THEN enable the pink
            // filter at the capped 50%. This is the exact order of the bug scenario: fullscreen
            // video mirrored, then the session's pink filter ramps to its cap.
            mirror.Start(primary);
            await Task.Delay(400);

            // Enable the pink filter at the capped max (the UCE PinkTintLayer clamps to 0.5).
            // This is the trigger for the regression: the mirror captures the test card + 50% pink
            // tint and (pre-fix) painted an opaque black bg that composited with the tint into a
            // solid cover.
            try { overlays.ShowOverlaySustained("pink", 0.5); } catch { }
            // Give the session-style ramp a moment to settle at the cap, then wait WELL PAST the
            // ~1s mark where the regression turned the screen solid pink.
            await Task.Delay(2500);

            var layer = engine.GetLayer(CompositorLayers.BrowserMirrorVideo) as BrowserMirrorVideoLayer;
            var framesA = layer?.FramesCaptured ?? -1;
            await Task.Delay(600);
            var framesB = layer?.FramesCaptured ?? -1;
            var pinkLayer = engine.GetLayer(CompositorLayers.PinkTint) as PinkTintLayer;

            Console.WriteLine($"{LogPrefix} mirror.IsMirroring={mirror.IsMirroring} layerActive={layer?.IsActive} capture frames t0={framesA} t1={framesB}");
            Console.WriteLine($"{LogPrefix} pinkTint active={pinkLayer?.IsActive} color={pinkLayer?.CurrentColor}");

            // Combined virtual-desktop screenshot via the same GDI CopyFromScreen path the mirror
            // uses to capture frames. Saved under the head bin logs folder.
            var shotPath = SaveCombinedScreenshot(allScreens);
            Console.WriteLine($"{LogPrefix} SCREENSHOT: {shotPath}");

            // Analyze the screenshot: is the test card's lime signature still visible in the primary
            // monitor's region, or was it overwhelmed by a solid pink cover?
            var verdict = AnalyzePrimaryCenter(shotPath, primary);
            Console.WriteLine($"{LogPrefix} ANALYSIS: {verdict.Summary}");
            Console.WriteLine($"{LogPrefix} RESULT: {(verdict.Pass ? "PASS" : "FAIL")} ({(verdict.Pass ? "test card visible through the capped 50% tint" : "screen overwhelmed by a solid pink cover")})");
            Environment.ExitCode = verdict.Pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{LogPrefix} ERROR: {ex}");
            Fail();
        }
        finally
        {
            try { overlays?.HideOverlaySustained("pink"); } catch { }
            try { mirror?.Stop(); } catch { }
            try { testCardWindow?.Close(); } catch { }
            if (testCardPath != null) { try { File.Delete(testCardPath); } catch { } }
            await Task.Delay(500);
            Shutdown();
        }
    }

    private static string CreateTestCard()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-verify-pink-cover-{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(960, 540);
        using (var g = Graphics.FromImage(bmp))
        {
            // LIME background — the signature color the analyzer looks for at the screenshot center.
            g.Clear(Color.FromArgb(255, 20, 200, 40));
            using var pen = new Pen(Color.White, 10);
            for (int i = -540; i < 960; i += 60) g.DrawLine(pen, i, 0, i + 540, 540);
            using var font = new Font("Segoe UI", 40, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString("PINK-COVER TEST CARD", font, brush, 70, 200);
            using var font2 = new Font("Segoe UI", 20, FontStyle.Bold);
            g.DrawString("must stay visible through the 50% tint", font2, brush, 70, 280);
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static Window ShowTestCard(string imagePath, ScreenInfo? primary)
    {
        // Position explicitly on the primary monitor's physical bounds (don't rely on
        // WindowState.FullScreen, which Avalonia places on whatever monitor the cursor is on —
        // that left the test card on a non-primary screen in earlier runs and the screenshot
        // then showed the desktop instead of the card). Avalonia window Position/Width/Height
        // are in DIPs, so convert the primary's physical px bounds by its scaling factor.
        var bounds = primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        var scaling = primary != null && primary.Scaling > 0 ? primary.Scaling : 1.0;
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            WindowState = WindowState.Normal,
            Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Black),
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            Position = new global::Avalonia.PixelPoint((int)bounds.X, (int)bounds.Y),
            Width = bounds.Width / scaling,
            Height = bounds.Height / scaling,
            Content = new global::Avalonia.Controls.Image
            {
                Source = new global::Avalonia.Media.Imaging.Bitmap(imagePath),
                Stretch = global::Avalonia.Media.Stretch.Fill,
            },
        };
        window.Show();
        return window;
    }

    /// <summary>One screenshot spanning the union of every screen's bounds, via GDI CopyFromScreen
    /// (the same path <see cref="WindowsFrameSource"/> uses to capture mirror frames).</summary>
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
            g.CopyFromScreen(minX, minY, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        var path = Path.Combine(dir, $"pink-cover-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// Samples the center of the primary monitor in the saved screenshot and decides whether the
    /// test card's lime signature survived the 50% pink tint (video visible) or was overwhelmed by
    /// a solid pink cover (the regression). Lime tinted with 50% pink still has green as a
    /// prominent channel; a solid pink cover has red dominant with green crushed low.
    /// </summary>
    private static (bool Pass, string Summary) AnalyzePrimaryCenter(string shotPath, ScreenInfo? primary)
    {
        try
        {
            using var shot = new Bitmap(shotPath);
            var b = primary?.Bounds ?? new PixelRect(0, 0, shot.Width, shot.Height);
            var cx = (int)(b.X + b.Width / 2);
            var cy = (int)(b.Y + b.Height / 2);
            // Sample a 40x40 region around the center and average it (smooths out the white lines).
            long r = 0, g = 0, bl = 0; int n = 0;
            for (int y = cy - 20; y < cy + 20; y++)
                for (int x = cx - 20; x < cx + 20; x++)
                {
                    if (x < 0 || y < 0 || x >= shot.Width || y >= shot.Height) continue;
                    var px = shot.GetPixel(x, y);
                    r += px.R; g += px.G; bl += px.B; n++;
                }
            if (n == 0) return (false, "center sample was empty");
            r /= n; g /= n; bl /= n;
            var greenIsMax = g >= r && g >= bl;
            // Lime tinted with 50% pink: green stays prominent. Solid pink cover: red dominant,
            // green crushed. greenIsMax == video visible through the tint.
            return (greenIsMax, $"center avg RGB=({r},{g},{bl}) greenIsMax={greenIsMax} => {(greenIsMax ? "VIDEO VISIBLE through tint" : "SOLID PINK COVER")}");
        }
        catch (Exception ex)
        {
            return (false, $"analysis failed: {ex.Message}");
        }
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
