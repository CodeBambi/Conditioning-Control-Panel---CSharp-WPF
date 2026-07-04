using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-layers harness (UCE plan Phase C). Exercises every migrated compositor layer
/// end-to-end THROUGH ITS OWNING SERVICE (services own state; layers are only probed
/// read-only) and asserts, per layer:
///   (a) registered with the engine at the exact <see cref="CompositorLayers"/> z-constant,
///   (b) the layer activates after the service call (bounded poll),
///   (c) the layer RENDERS: a GDI screen capture (Graphics.CopyFromScreen, the same path
///       AvaloniaScreenOcrService uses) hashed before vs during the effect must DIFFER for
///       capture-VISIBLE layers,
///   (d) the layer deactivates after the service stops it (or the effect expires).
///
/// P0 dual-surface guardrails (capture affinity, see the UCE skill):
///   - SubliminalLayer MUST produce a screenshot delta while showing. WPF deliberately sets
///     WDA_NONE on subliminal windows; a no-delta here means the MAIN compositor surface got
///     capture exclusion applied = P0 regression.
///   - BrainDrainLayer lives on the capture-EXCLUDED surface: while it is active (alone),
///     the engine must have created the excluded-surface window (ExcludedWindowCount >= 1)
///     and the screen capture must NOT change because of it — the probe seeing nothing IS
///     the pass (WDA_EXCLUDEFROMCAPTURE working). Gated on a baseline-stability pre-check so
///     ambient desktop animation cannot fake a failure; if the baseline is unstable the
///     pixel probe is reported SKIP (honestly), the surface assertions still gate.
///
/// LockCard (Z=20) is recorded truthfully as NOT-A-LAYER: no LockCardLayer type exists
/// (verified by grep); the lock card is still a separate window per the UCE skill.
/// Exit code 0 when no layer FAILs (SKIP rows allowed), 2 otherwise.
/// Mirrors <see cref="SpiralVerification"/> / <see cref="VideoVerification"/> structure.
/// </summary>
internal static class LayerVerification
{
    private sealed class Row
    {
        public string Layer = "";
        public string Z = "";
        public string Registered = "";
        public string Activated = "";
        public string Delta = "";
        public string Teardown = "";
        public string Verdict = "";
        public string Note = "";
    }

    public static void Attach(AppBuilder builder)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background));
    }

    private static async Task RunAsync()
    {
        var rows = new List<Row>();
        var pass = false;
        string? tempFlashImage = null;
        IFlashService? flash = null;
        IBubbleService? bubbles = null;
        IBouncingTextService? bouncing = null;
        IOverlayService? overlay = null;
        try
        {
            await Task.Delay(2000); // let splash/init settle

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                Console.WriteLine("[LAYERS] Main window not available.");
                return;
            }

            var services = App.Services;
            if (services == null)
            {
                Console.WriteLine("[LAYERS] App.Services not available.");
                return;
            }

            var engine = services.GetService<CompositorEngine>();
            if (engine == null)
            {
                Console.WriteLine("[LAYERS] CompositorEngine is not registered in DI.");
                return;
            }

            // Resolving each owning service forces its ctor to register its layer (DI is lazy).
            flash = services.GetService<IFlashService>();
            var subliminal = services.GetService<ISubliminalService>();
            bouncing = services.GetService<IBouncingTextService>();
            bubbles = services.GetService<IBubbleService>();
            overlay = services.GetService<IOverlayService>();
            var settings = services.GetService<ISettingsService>()?.Current;
            var screenProvider = services.GetService<IScreenProvider>();
            if (flash == null || subliminal == null || bouncing == null || bubbles == null || overlay == null || screenProvider == null)
            {
                Console.WriteLine("[LAYERS] A required service is missing from DI " +
                    $"(flash={flash != null}, subliminal={subliminal != null}, bouncing={bouncing != null}, " +
                    $"bubbles={bubbles != null}, overlay={overlay != null}, screens={screenProvider != null}).");
                return;
            }

            // Guard: harness runs must own the whole effect lifecycle. App.axaml.cs skips
            // the launch behaviors (AutoStartEngine/ForceVideoOnLaunch) and scheduler arming
            // for --verify-* runs; if a session is somehow live anyway, the stages below
            // would race it and every delta/teardown assertion would be meaningless.
            var sessionService = services.GetService<ConditioningControlPanel.Core.Services.Sessions.ISessionService>();
            if (sessionService != null && sessionService.State != ConditioningControlPanel.Core.Services.Sessions.SessionState.Idle)
            {
                Console.WriteLine($"[LAYERS] A session is active (state={sessionService.State}) — harness isolation broken; aborting.");
                return;
            }

            // Hide the dashboard for the probe run: the main window animates (marquee,
            // avatar, cards) and would inject ambient deltas into every hash. Hiding is a
            // supported state (StartMinimized uses the same path); restored in finally.
            lifetime.MainWindow.Hide();
            await Task.Delay(400);

            // Wake the engine once (documented recommended wake): with the engine never
            // started there is no watchdog tick, and services like the subliminal engine
            // do not call compositor.Start() themselves.
            engine.Start();
            await Task.Delay(400); // first window is synchronous; give Opened/ex-styles a beat

            var screens = screenProvider.GetAllScreens();
            var primary = screenProvider.GetPrimaryScreen() ?? screens.FirstOrDefault();
            if (screens.Count == 0 || primary == null)
            {
                Console.WriteLine("[LAYERS] No screens reported by IScreenProvider.");
                return;
            }

            // Environment probe (diagnostic): two captures 700ms apart. If they already
            // differ, DIFFER assertions are still gated (expected direction) but reported
            // as inconclusive-by-noise; the brain-drain NO-delta probe re-checks freshly.
            var env1 = Capture(screens, primary);
            await Task.Delay(700);
            var env2 = Capture(screens, primary);
            var envStable = env1.Full == env2.Full;
            Console.WriteLine($"[LAYERS] Baseline stability: {(envStable ? "STABLE" : "NOISY (ambient screen content changes between captures)")}.");

            // ---------------- Registration sweep (exact z-constants) ----------------
            var flashLayer = ExpectLayer<FlashLayer>(engine, CompositorLayers.Flash, "FlashLayer", rows);
            var subliminalLayer = ExpectLayer<SubliminalLayer>(engine, CompositorLayers.Subliminal, "SubliminalLayer", rows);
            var bubbleLayer = ExpectLayer<BubbleLayer>(engine, CompositorLayers.Bubbles, "BubbleLayer", rows);
            var bouncingLayer = ExpectLayer<BouncingTextLayer>(engine, CompositorLayers.BouncingText, "BouncingTextLayer", rows);
            var brainDrainLayer = ExpectLayer<BrainDrainLayer>(engine, CompositorLayers.BrainDrain, "BrainDrainLayer", rows);
            var spiralLayer = ExpectLayer<SpiralLayer>(engine, CompositorLayers.Spiral, "SpiralLayer", rows);
            var pinkTintLayer = ExpectLayer<PinkTintLayer>(engine, CompositorLayers.PinkTint, "PinkTintLayer", rows);

            // LockCard (Z=20): the skill says no LockCardLayer exists (lock card is still a
            // window). Verified: no LockCardLayer type in the codebase, and nothing should be
            // registered at Z=20. Recorded truthfully as SKIP, not built here.
            var lockCardRow = new Row { Layer = "LockCard", Z = $"{CompositorLayers.LockCard} (reserved)" };
            var lockCardOccupant = engine.GetLayer(CompositorLayers.LockCard);
            lockCardRow.Registered = lockCardOccupant == null ? "none (expected)" : $"UNEXPECTED: {lockCardOccupant.GetType().Name}";
            lockCardRow.Activated = "-";
            lockCardRow.Delta = "-";
            lockCardRow.Teardown = "-";
            lockCardRow.Verdict = lockCardOccupant == null ? "SKIP" : "FAIL";
            lockCardRow.Note = lockCardOccupant == null
                ? "No LockCardLayer exists (skill claim verified by grep); lock card is still a window. Not migrated - nothing to verify."
                : "Something is registered at the reserved LockCard z-index.";
            rows.Add(lockCardRow);

            if (flashLayer == null || subliminalLayer == null || bubbleLayer == null || bouncingLayer == null
                || brainDrainLayer == null || spiralLayer == null || pinkTintLayer == null)
            {
                Console.WriteLine("[LAYERS] Registration sweep failed; skipping activation stages.");
                return;
            }
            Console.WriteLine("[LAYERS] All 7 migrated layers registered at their exact z-constants.");

            // ---------------- Stage 1: FlashLayer (Z=30) via IFlashService ----------------
            {
                Console.WriteLine("[LAYERS] Stage 1: FlashLayer via IFlashService.TriggerFlashOnce...");
                var row = rows.First(r => r.Layer == "FlashLayer");
                tempFlashImage = CreateTempFlashImage();
                flash.Start(); // TriggerFlashOnce is gated on IsRunning
                var before = Capture(screens, primary);
                flash.TriggerFlashOnce(tempFlashImage, durationMs: 4000, playSound: false, suppressHaptic: true);
                var activated = await PollAsync(() => flashLayer.IsActive, 6000);
                row.Activated = activated ? "yes" : "TIMEOUT";
                if (activated)
                {
                    await Task.Delay(700); // fade-in (~0.33s to full opacity) + present
                    var during = Capture(screens, primary);
                    var flashOpacity = settings?.FlashOpacity ?? 100;
                    if (flashOpacity <= 0)
                        row.Delta = $"SKIP (FlashOpacity={flashOpacity} in user settings; layer invisible by config)";
                    else
                        row.Delta = during.Full != before.Full ? "DIFFER (full-screen)" : "SAME (FAIL)";
                }
                flash.Stop(); // clears the layer's items
                var deactivated = await PollAsync(() => !flashLayer.IsActive, 3000);
                row.Teardown = deactivated ? "clean" : "TIMEOUT";
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Stage 2: SubliminalLayer (Z=40) — P0 capture-VISIBLE ----------------
            {
                Console.WriteLine("[LAYERS] Stage 2: SubliminalLayer via ISubliminalService.FlashSubliminalCustom...");
                var row = rows.First(r => r.Layer == "SubliminalLayer");
                var before = Capture(screens, primary);
                // Concrete overload forces target opacity to 100% so a user's SubliminalOpacity=0
                // cannot mask the P0 probe; interface fallback uses settings opacity.
                if (subliminal is ConditioningControlPanel.Avalonia.Services.Subliminal.AvaloniaSubliminalService avaloniaSub)
                    avaloniaSub.FlashSubliminalCustom("VERIFY LAYERS", opacity: 100, overrideDurationMs: 4000);
                else
                    subliminal.FlashSubliminalCustom("VERIFY LAYERS", overrideDurationMs: 4000);
                var activated = await PollAsync(() => subliminalLayer.IsActive, 3000);
                row.Activated = activated ? "yes" : "TIMEOUT";
                if (activated)
                {
                    await Task.Delay(500); // 50ms fade-in + present headroom
                    var during = Capture(screens, primary);
                    // P0: subliminals are capture-visible BY DESIGN (WPF sets WDA_NONE). A
                    // no-delta here means the main surface got capture exclusion = P0 regression.
                    row.Delta = during.Center != before.Center
                        ? "DIFFER (center-crop) - P0 capture-visible OK"
                        : "SAME - P0 REGRESSION: main surface excluded from capture (FAIL)";
                }
                // No sustained stop API for a one-shot subliminal; the service-set duration
                // expiring IS the teardown path (Update removes the item).
                var deactivated = await PollAsync(() => !subliminalLayer.IsActive, 7000);
                row.Teardown = deactivated ? "clean (duration expiry)" : "TIMEOUT";
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Stage 3: BouncingTextLayer (Z=50) via IBouncingTextService ----------------
            {
                Console.WriteLine("[LAYERS] Stage 3: BouncingTextLayer via IBouncingTextService.Start...");
                var row = rows.First(r => r.Layer == "BouncingTextLayer");
                var before = Capture(screens, primary);
                bouncing.Start(new[] { "VERIFY LAYERS BOUNCE" });
                var activated = await PollAsync(() => bouncingLayer.IsActive, 3000);
                row.Activated = activated ? "yes" : "TIMEOUT";
                if (activated)
                {
                    await Task.Delay(700);
                    var during = Capture(screens, primary);
                    var btOpacity = settings?.BouncingTextOpacity ?? 100;
                    if (btOpacity <= 0)
                        row.Delta = $"SKIP (BouncingTextOpacity={btOpacity} in user settings; layer invisible by config)";
                    else
                        row.Delta = during.Full != before.Full ? "DIFFER (full-screen)" : "SAME (FAIL)";
                }
                bouncing.Stop();
                var deactivated = await PollAsync(() => !bouncingLayer.IsActive, 3000);
                row.Teardown = deactivated ? "clean" : "TIMEOUT";
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Stage 4: BubbleLayer (Z=45) via IBubbleService ----------------
            // Ambient bubbles have a direct API: Start() spawns the first bubble immediately
            // (BubbleEngine.Start -> SpawnBubble), no session required.
            {
                Console.WriteLine("[LAYERS] Stage 4: BubbleLayer via IBubbleService.Start/SpawnOnce...");
                var row = rows.First(r => r.Layer == "BubbleLayer");
                var before = Capture(screens, primary);
                bubbles.Start();
                bubbles.SpawnOnce();
                var activated = await PollAsync(() => bubbleLayer.IsActive, 5000);
                row.Activated = activated ? "yes" : "TIMEOUT";
                if (activated)
                {
                    await Task.Delay(800); // bubble fade/scale-in
                    var during = Capture(screens, primary);
                    row.Delta = during.Full != before.Full ? "DIFFER (full-screen)" : "SAME (FAIL)";
                }
                bubbles.Stop(); // destroys all bubbles and clears the layer
                var deactivated = await PollAsync(() => !bubbleLayer.IsActive, 3000);
                row.Teardown = deactivated ? "clean" : "TIMEOUT";
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Stage 5: BrainDrainLayer (Z=55) — P0 capture-EXCLUDED, ALONE ----------------
            // Runs BEFORE the pink/spiral stages: releasing those re-applies the user's
            // persistent PinkFilterEnabled/SpiralEnabled overlays (WPF-parity settings-held
            // release), which would leave animating content on screen and poison the
            // brain-drain NO-delta probe. NOTE: overlay.Start() is deliberately never called
            // — its 500ms settings sync would apply the user's persistent overlays mid-run.
            // Sustained ad-hoc overlays work while the service is stopped (WPF parity).
            {
                Console.WriteLine("[LAYERS] Stage 5: BrainDrainLayer via IOverlayService.ShowOverlaySustained(braindrain), ALONE...");
                var row = rows.First(r => r.Layer == "BrainDrainLayer");

                // Precondition: brain drain must run alone so the no-delta assertion is clean.
                var stillActive = engine.Layers.Where(l => l.IsActive).Select(l => l.GetType().Name).ToList();
                if (stillActive.Count > 0)
                    Console.WriteLine($"[LAYERS] WARNING: layers still active before brain-drain stage: {string.Join(", ", stillActive)}.");

                // Fresh PER-SCREEN stability pair right before the stage. The no-delta probe
                // gates on the screens whose ambient content is static across 700ms: brain
                // drain covers EVERY monitor, so any stable screen is a valid exclusion probe
                // (a live terminal/browser on one monitor no longer voids the assertion).
                var stab1 = Capture(screens, primary);
                await Task.Delay(700);
                var stab2 = Capture(screens, primary);
                var stableScreens = new List<int>();
                for (var i = 0; i < Math.Min(stab1.PerScreen.Length, stab2.PerScreen.Length); i++)
                {
                    if (stab1.PerScreen[i] == stab2.PerScreen[i]) stableScreens.Add(i);
                }
                var stableNow = stableScreens.Count > 0 && stillActive.Count == 0;
                Console.WriteLine($"[LAYERS] Brain-drain probe: {stableScreens.Count}/{stab2.PerScreen.Length} screen(s) ambient-stable.");

                overlay.ShowOverlaySustained("braindrain", 0.8);
                var activated = await PollAsync(() => brainDrainLayer.IsActive, 3000);
                row.Activated = activated ? "yes" : "TIMEOUT";

                // (i) The engine must create the excluded-surface window for the active
                // excluded layer (lazy, staggered creation on the next active tick).
                var excludedUp = activated && await PollAsync(() => engine.ExcludedWindowCount >= 1, 5000);
                var surfaceNote = excludedUp
                    ? $"excluded surface up (ExcludedWindowCount={engine.ExcludedWindowCount})"
                    : "excluded surface NOT created (FAIL)";

                // (ii) The screen capture must NOT change because of brain drain: capture
                // exclusion working = the probe seeing nothing IS the pass.
                if (activated && excludedUp)
                {
                    await Task.Delay(900); // let the blur render + settle on the physical screen
                    var during = Capture(screens, primary);
                    if (!stableNow)
                    {
                        row.Delta = "SKIP (no ambient-stable screen; no-delta probe inconclusive) - surface assertions gated instead";
                    }
                    else
                    {
                        var leaked = stableScreens.Any(i =>
                            i < during.PerScreen.Length && during.PerScreen[i] != stab2.PerScreen[i]);
                        row.Delta = leaked
                            ? "DELTA - P0 REGRESSION: excluded surface visible in capture (FAIL)"
                            : $"NO-DELTA on {stableScreens.Count} stable screen(s) - P0 capture-exclusion OK (blur invisible to capture)";
                    }
                }
                else if (activated)
                {
                    row.Delta = "-";
                }

                overlay.HideOverlaySustained("braindrain"); // unconditional StopBrainDrain
                var deactivated = await PollAsync(() => !brainDrainLayer.IsActive, 3000);
                // Excluded-surface idle teardown is ~500ms after the last excluded layer goes idle.
                var surfaceDown = await PollAsync(() => engine.ExcludedWindowCount == 0, 4000);
                row.Teardown = (deactivated ? "clean" : "TIMEOUT") + (surfaceDown ? ", excluded surface closed (~500ms idle)" : ", excluded surface STILL OPEN (FAIL)");
                row.Note = surfaceNote;

                var failed = !activated || !excludedUp || !deactivated || !surfaceDown || row.Delta.Contains("FAIL");
                row.Verdict = failed ? "FAIL" : (row.Delta.StartsWith("SKIP") ? "PASS (pixel probe SKIP)" : "PASS");
                await SettleIdle(engine);
            }

            // ---------------- Stage 6: PinkTintLayer (Z=70) via IOverlayService ----------------
            // Teardown caveat: HideOverlaySustained releases the ad-hoc hold and then
            // re-applies the user's persistent setting (ReleaseOverlayIfUnheld — WPF-parity
            // "a timed expiry never blinks a user-enabled overlay off"). With
            // PinkFilterEnabled=true in the user's profile the layer legitimately STAYS
            // active at the setting opacity — that IS the correct release behavior.
            {
                Console.WriteLine("[LAYERS] Stage 6: PinkTintLayer via IOverlayService.ShowOverlaySustained(pink)...");
                var row = rows.First(r => r.Layer == "PinkTintLayer");
                var before = Capture(screens, primary);
                overlay.ShowOverlaySustained("pink", 0.6);
                var activated = await PollAsync(() => pinkTintLayer.IsActive, 3000);
                row.Activated = activated ? "yes" : "TIMEOUT";
                if (activated)
                {
                    await Task.Delay(600);
                    var during = Capture(screens, primary);
                    row.Delta = during.Full != before.Full ? "DIFFER (full-screen)" : "SAME (FAIL)";
                }
                overlay.HideOverlaySustained("pink");
                var settingsHeld = settings?.PinkFilterEnabled == true;
                bool deactivated;
                if (settingsHeld)
                {
                    // Release re-applies the setting-driven overlay; the layer staying active
                    // is the WPF-parity pass condition, not a leak.
                    await Task.Delay(500);
                    deactivated = pinkTintLayer.IsActive;
                    row.Teardown = deactivated
                        ? "released to settings-held (PinkFilterEnabled=true re-applied; WPF parity)"
                        : "released but settings-held overlay did NOT re-apply (FAIL)";
                }
                else
                {
                    deactivated = await PollAsync(() => !pinkTintLayer.IsActive, 3000);
                    row.Teardown = deactivated ? "clean" : "TIMEOUT";
                }
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Stage 7: SpiralLayer (Z=60) via IOverlayService ----------------
            // Same settings-held release caveat as pink (SpiralEnabled).
            {
                Console.WriteLine("[LAYERS] Stage 7: SpiralLayer via IOverlayService.ShowOverlaySustained(spiral)...");
                var row = rows.First(r => r.Layer == "SpiralLayer");
                var before = Capture(screens, primary);
                overlay.ShowOverlaySustained("spiral", 0.5);
                // Background GIF decode (bundled spiral.gif fallback) can take a while.
                var decoded = await PollAsync(() => spiralLayer.HasDecodedSource, 10000);
                var activated = decoded && await PollAsync(() => spiralLayer.IsActive, 2000);
                row.Activated = activated ? "yes" : (decoded ? "decoded but not active (TIMEOUT)" : "decode TIMEOUT");
                if (activated)
                {
                    await Task.Delay(800);
                    var during = Capture(screens, primary);
                    row.Delta = during.Full != before.Full ? "DIFFER (full-screen)" : "SAME (FAIL)";
                }
                overlay.HideOverlaySustained("spiral");
                var settingsHeld = settings?.SpiralEnabled == true;
                bool deactivated;
                if (settingsHeld)
                {
                    await Task.Delay(500);
                    deactivated = spiralLayer.IsActive;
                    row.Teardown = deactivated
                        ? "released to settings-held (SpiralEnabled=true re-applied; WPF parity)"
                        : "released but settings-held overlay did NOT re-apply (FAIL)";
                }
                else
                {
                    deactivated = await PollAsync(() => !spiralLayer.IsActive, 3000);
                    row.Teardown = deactivated ? "clean" : "TIMEOUT";
                }
                FinishRow(row, activated, deactivated, envStable);
                await SettleIdle(engine);
            }

            // ---------------- Report ----------------
            Console.WriteLine();
            Console.WriteLine("[LAYERS] Per-layer results:");
            Console.WriteLine("[LAYERS] {0,-18} {1,-14} {2,-28} {3,-10} {4}", "Layer", "Z", "Registered", "Verdict", "Activated / Delta / Teardown");
            foreach (var r in rows)
            {
                Console.WriteLine("[LAYERS] {0,-18} {1,-14} {2,-28} {3,-10} {4} | {5} | {6}",
                    r.Layer, r.Z, r.Registered, r.Verdict, r.Activated, r.Delta, r.Teardown);
                if (!string.IsNullOrEmpty(r.Note))
                    Console.WriteLine($"[LAYERS]     note: {r.Note}");
            }

            var anyFail = rows.Any(r => r.Verdict.StartsWith("FAIL"));
            if (anyFail)
            {
                Console.WriteLine("[LAYERS] FAIL: one or more layers failed verification (see table).");
            }
            else
            {
                Console.WriteLine("[LAYERS] PASS: all migrated layers verified (LockCard honestly SKIP: not a layer yet). " +
                    "Side-by-side WPF timing/opacity parity still needs human eyes.");
                pass = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LAYERS] ERROR: {ex}");
        }
        finally
        {
            Environment.ExitCode = pass ? 0 : 2;
            try { flash?.Stop(); } catch { }
            try { bouncing?.Stop(); } catch { }
            try { bubbles?.Stop(); } catch { }
            try
            {
                overlay?.HideOverlaySustained("pink");
                overlay?.HideOverlaySustained("spiral");
                overlay?.HideOverlaySustained("braindrain");
            }
            catch { }
            if (tempFlashImage != null)
            {
                try { File.Delete(tempFlashImage); } catch { }
            }
            await Task.Delay(500); // let teardown settle before shutdown
            Shutdown(pass ? 0 : 2);
        }
    }

    // ---------------- helpers ----------------

    private static T? ExpectLayer<T>(CompositorEngine engine, int zIndex, string name, List<Row> rows)
        where T : class
    {
        var row = new Row { Layer = name, Z = zIndex.ToString() };
        var layer = engine.GetLayer(zIndex);
        if (layer is T typed)
        {
            row.Registered = $"yes ({name} @ Z={zIndex})";
            rows.Add(row);
            return typed;
        }

        row.Registered = layer == null ? "MISSING" : $"WRONG TYPE: {layer.GetType().Name}";
        row.Activated = "-";
        row.Delta = "-";
        row.Teardown = "-";
        row.Verdict = "FAIL";
        rows.Add(row);
        return null;
    }

    private static void FinishRow(Row row, bool activated, bool deactivated, bool envStable)
    {
        var deltaFail = row.Delta.Contains("FAIL");
        var deltaSkip = row.Delta.StartsWith("SKIP");
        if (!activated || !deactivated || deltaFail)
        {
            row.Verdict = "FAIL";
            return;
        }
        row.Verdict = deltaSkip ? "PASS (render probe SKIP)" : "PASS";
        if (!envStable && row.Delta.StartsWith("DIFFER"))
            row.Note = AppendNote(row.Note, "ambient screen noisy; DIFFER is the expected direction but not exclusively attributable to the layer");
    }

    private static string AppendNote(string existing, string extra) =>
        string.IsNullOrEmpty(existing) ? extra : existing + "; " + extra;

    private static async Task<bool> PollAsync(Func<bool> condition, int timeoutMs, int stepMs = 250)
    {
        var waited = 0;
        while (waited < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(stepMs);
            waited += stepMs;
        }
        return condition();
    }

    /// <summary>
    /// Wait for the engine to notice all-idle and present one cleared transparent frame
    /// (the main-surface windows stay alive by design — WS0 lot 3 no-churn contract — so
    /// "teardown" for main-surface layers is IsActive=false plus this settle).
    /// </summary>
    private static Task SettleIdle(CompositorEngine engine) => Task.Delay(800);

    /// <summary>
    /// GDI capture of the whole virtual desktop (physical px — the same coordinate space
    /// the layers render in). The "Full" hash is the concatenated per-screen hash of each
    /// monitor's WORKING AREA (taskbar excluded — the tray clock/icons repaint constantly
    /// and would make every no-delta probe report a false change); "Center" is the central
    /// 30% of the primary screen (subliminals draw centered text). Excluded-surface windows
    /// carry WDA_EXCLUDEFROMCAPTURE and are invisible to this capture BY DESIGN.
    /// </summary>
    private static (string Full, string Center, string[] PerScreen) Capture(IReadOnlyList<ScreenInfo> screens, ScreenInfo primary)
    {
        var minX = (int)Math.Floor(screens.Min(s => s.Bounds.X));
        var minY = (int)Math.Floor(screens.Min(s => s.Bounds.Y));
        var maxX = (int)Math.Ceiling(screens.Max(s => s.Bounds.Right));
        var maxY = (int)Math.Ceiling(screens.Max(s => s.Bounds.Bottom));
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);

        using var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(minX, minY, 0, 0, new System.Drawing.Size(width, height),
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        // Per-screen working-area hashes (taskbar excluded), in stable screen order.
        var perScreen = new List<string>();
        foreach (var s in screens.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var wa = s.WorkingArea.IsEmpty ? s.Bounds : s.WorkingArea;
            var rx = Math.Clamp((int)(wa.X - minX), 0, Math.Max(0, width - 1));
            var ry = Math.Clamp((int)(wa.Y - minY), 0, Math.Max(0, height - 1));
            var rw = Math.Clamp((int)wa.Width, 1, width - rx);
            var rh = Math.Clamp((int)wa.Height, 1, height - ry);
            perScreen.Add(HashRegion(bmp, new System.Drawing.Rectangle(rx, ry, rw, rh)));
        }
        var full = string.Concat(perScreen);

        // Center crop: central 30% of the primary screen (subliminals draw centered text).
        var cw = Math.Max(1, (int)(primary.Bounds.Width * 0.30));
        var ch = Math.Max(1, (int)(primary.Bounds.Height * 0.30));
        var cx = (int)(primary.Bounds.X - minX + (primary.Bounds.Width - cw) / 2.0);
        var cy = (int)(primary.Bounds.Y - minY + (primary.Bounds.Height - ch) / 2.0);
        cx = Math.Clamp(cx, 0, Math.Max(0, width - cw));
        cy = Math.Clamp(cy, 0, Math.Max(0, height - ch));
        var center = HashRegion(bmp, new System.Drawing.Rectangle(cx, cy, cw, ch));

        return (full, center, perScreen.ToArray());
    }

    private static string HashRegion(System.Drawing.Bitmap bmp, System.Drawing.Rectangle rect)
    {
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = rect.Width * 4;
            var buffer = new byte[(long)rowBytes * rect.Height];
            for (var y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            }
            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(buffer));
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>Solid high-contrast temp PNG so the flash stage never depends on the user's assets folder.</summary>
    private static string CreateTempFlashImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-verify-layers-{Guid.NewGuid():N}.png");
        using var bmp = new System.Drawing.Bitmap(600, 400, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Magenta);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Lime, 20);
            g.DrawRectangle(pen, 30, 30, 540, 340);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private static void Shutdown(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                // Pass the code through the lifetime: Avalonia's shutdown otherwise
                // overwrites Environment.ExitCode with its own default (0).
                lifetime?.Shutdown(exitCode);
            }
            catch { }
        });
    }
}
