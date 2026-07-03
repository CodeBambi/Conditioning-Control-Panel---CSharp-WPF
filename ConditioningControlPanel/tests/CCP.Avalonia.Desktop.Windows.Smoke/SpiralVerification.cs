using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-spiral harness. The spiral no longer lives in a dedicated overlay window: it is
/// <see cref="SpiralLayer"/> (Z=<see cref="CompositorLayers.Spiral"/>) rendered inside the
/// per-monitor <see cref="CompositorWindow"/> surfaces of the unified compositor engine.
/// This harness asserts that compositor reality:
///   1. the spiral layer is registered with the engine,
///   2. showing the spiral decodes a real frame set (bundled spiral.gif fallback included)
///      and activates the layer,
///   3. the engine is running with at least one CompositorWindow per expected monitor
///      (expected = all screens when DualMonitorEnabled, else 1).
/// Exit code 0 on success, 2 on any failure. An animation-progress sample (GIF frame index
/// advancing across ~700ms) is printed as a non-gating diagnostic.
/// </summary>
internal static class SpiralVerification
{
    public static void Attach(AppBuilder builder)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background));
    }

    private static async Task RunAsync()
    {
        var pass = false;
        try
        {
            await Task.Delay(2000); // let splash/init settle

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                Console.WriteLine("[SPIRAL] Main window not available.");
                return;
            }

            var services = App.Services;
            if (services == null)
            {
                Console.WriteLine("[SPIRAL] App.Services not available.");
                return;
            }

            // Stage 1: the spiral layer must be registered with the compositor engine
            // (AvaloniaOverlayService registers it once in its ctor).
            var engine = services.GetService<CompositorEngine>();
            if (engine == null)
            {
                Console.WriteLine("[SPIRAL] CompositorEngine is not registered in DI.");
                return;
            }

            var overlayService = services.GetRequiredService<IOverlayService>();
            if (engine.GetLayer(CompositorLayers.Spiral) is not SpiralLayer spiralLayer)
            {
                Console.WriteLine("[SPIRAL] SpiralLayer is not registered with the compositor engine.");
                return;
            }
            Console.WriteLine("[SPIRAL] SpiralLayer registered at Z=" + CompositorLayers.Spiral + ".");

            overlayService.Start();
            await Task.Delay(200);

            overlayService.ShowOverlaySustained("spiral", 0.5);

            // Stage 2: wait for the background GIF decode to land (stock spiral.gif is ~8.6MB;
            // decode runs off the UI thread). If the spiral path chain resolved to nothing the
            // layer never activates and this times out with a distinct message.
            var decoded = false;
            for (var i = 0; i < 40; i++) // up to 10s
            {
                await Task.Delay(250);
                if (spiralLayer.HasDecodedSource) { decoded = true; break; }
            }

            if (!decoded)
            {
                Console.WriteLine(
                    "[SPIRAL] Spiral source failed to resolve/decode within 10s. " +
                    "Check the spiral.gif resolution chain (settings.SpiralPath -> mod override -> " +
                    "Spirals folder -> assets -> bundled avares://CCP.Avalonia/Assets/spiral.gif).");
                return;
            }
            Console.WriteLine("[SPIRAL] Spiral source decoded.");

            if (!spiralLayer.IsActive)
            {
                Console.WriteLine("[SPIRAL] SpiralLayer decoded a source but is not active (visible/opacity gate failed).");
                return;
            }

            // Stage 3: the engine must be running with one main-surface CompositorWindow per
            // expected monitor. WindowCount > 0 also proves the auto-stop watchdog considers
            // the spiral layer active (engine closes all windows after 500ms of idle).
            await Task.Delay(800); // staggered window creation is ~250ms per extra monitor

            var settings = services.GetService<ISettingsService>()?.Current;
            var screenProvider = services.GetService<IScreenProvider>();
            var screenCount = 0;
            try { screenCount = screenProvider?.GetAllScreens().Count ?? 0; } catch { }
            var expectedMonitors = settings?.DualMonitorEnabled == true ? Math.Max(1, screenCount) : 1;

            if (!engine.IsRunning)
            {
                Console.WriteLine("[SPIRAL] CompositorEngine is not running while the spiral layer is active.");
                return;
            }

            if (engine.WindowCount < expectedMonitors)
            {
                Console.WriteLine($"[SPIRAL] Expected >= {expectedMonitors} CompositorWindow(s) (screens={screenCount}), found {engine.WindowCount}.");
                return;
            }
            Console.WriteLine($"[SPIRAL] CompositorEngine running with {engine.WindowCount} window(s) (expected >= {expectedMonitors}).");

            // Non-gating diagnostic: prove the 60Hz tick is advancing the animation
            // (GIF frame index, or rotation angle for a static image).
            var sample1 = spiralLayer.AnimationProgress;
            await Task.Delay(700);
            var sample2 = spiralLayer.AnimationProgress;
            Console.WriteLine(sample2 != sample1
                ? $"[SPIRAL] Animation advancing (progress {sample1} -> {sample2})."
                : $"[SPIRAL] WARNING: animation progress did not advance across 700ms (progress={sample1}); single-frame source or frozen tick.");

            Console.WriteLine("[SPIRAL] PASS: spiral layer registered, decoded, and composited on every expected monitor.");
            pass = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SPIRAL] ERROR: {ex}");
        }
        finally
        {
            Environment.ExitCode = pass ? 0 : 2;
            try
            {
                var overlayService = App.Services?.GetService<IOverlayService>();
                overlayService?.Stop();
            }
            catch { }
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
