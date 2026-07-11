using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.AvatarTube;
using ConditioningControlPanel.Core.Services.AvatarTube;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// --verify-avatartube harness. Closes the AvatarTube visual/interactive blind spot that
/// let two structurally-green builds fail the owner's live retest (owner repro 2026-07-11:
/// a speech/text bubble appears above the avatar -> the avatar SHIFTS UP + SHRINKS +
/// FREEZES). The fix (commit 0f338f87) rebuilt TubeGeometryController as the single writer
/// of window geometry and removed every speech-driven re-settle path. This harness PROVES
/// that fix holds repeatably.
///
/// What it does, end to end (spec: add verify-avatartube harness):
///   (a) Constructs/shows an <see cref="AvatarTubeWindow"/> attached to a stand-in parent
///       window (reusing the running app's DI, like --verify-layers).
///   (b) Records a BASELINE: finalScale, tube window Width/Height, ContentViewbox size, and
///       the avatar's rendered rect in window space.
///   (c) Fires 10 consecutive speech bubbles through the PUBLIC speech entry callers use
///       (<see cref="AvatarTubeWindow.ShowGiggle(string)"/> -> PopulateSpeechBubble), pumping
///       the dispatcher between each so layout + any coalesced geometry pass settles.
///   (d) After EACH bubble: re-samples and ASSERTS geometry equals baseline within epsilon
///       (allowing ONLY the +/-4px liveness bob on Y, Windowing.cs FloatDistance=4). Also
///       asserts the AvatarTubeWindow instance count stays at one and the AvatarRandomBubble
///       live-window count never exceeds the ceiling and returns to baseline.
///   (e) Detach(): CanResize==true, the WM_NCHITTEST art-only hook is active, and a simulated
///       MoveTo changes BOTH X and Y. Attach(): CanResize==false.
///   (f) Saves a PNG per step (baseline, after bubbles 1/5/10, detached) to
///       logs/avatartube-verify/ via a Win32 GetWindowRect + GDI CopyFromScreen capture of
///       the tube window (the same capture path AvaloniaScreenOcrService / --verify-layers use).
///   (g) Prints PASS/FAIL per assertion and exits nonzero on any violation.
///
/// Mirrors <see cref="LayerVerification"/> / <see cref="SpiralVerification"/> structure.
/// Exit 0 = all assertions PASS; 2 = one or more FAIL. Sibling of SmokeTestRunner — does
/// NOT edit it (never-edit file).
/// </summary>
internal static class AvatarTubeVerification
{
    private sealed class Check
    {
        public string Step = "";
        public string Assertion = "";
        public string Result = ""; // PASS / FAIL
        public string Detail = "";
    }

    private const int BubbleCount = 10;

    // Epsilons. The controller's dead-band (TubeGeometryMath.ShouldApplyScale) rejects
    // sub-permille scale deltas, so a stable geometry compares well within these bounds.
    private const double ScaleEpsilon = 0.001;       // finalScale
    private const double SizeEpsilon = 0.5;          // window / viewbox size (DIPs)
    private const double AvatarEpsilon = 0.5;        // avatar X + size
    // Windowing.cs FloatDistance=4: the liveness bob writes +/-4px to an INNER TranslateTransform
    // on ImgAvatar only. AvatarBorder itself does not bob, but the spec mandates tolerating the
    // +/-4px cue on Y, so the Y bound is generous (bob + slack) and never masks a real shift.
    private const double AvatarYTolerance = 4.0 + 0.5;

    // Win32 window-rect capture (same GDI path as LayerVerification.CaptureScreens).
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static void Attach(AppBuilder builder)
    {
        builder.AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background));
    }

    private static async Task RunAsync()
    {
        var checks = new List<Check>();
        Window? standIn = null;
        AvatarTubeWindow? tube = null;
        string? shotDir = null;

        try
        {
            await Task.Delay(2500); // let splash/init settle (mirrors LayerVerification)

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime == null)
            {
                Console.WriteLine("[AVATARTUBE] No classic desktop lifetime.");
                return;
            }
            var services = ConditioningControlPanel.Avalonia.App.Services;
            if (services == null)
            {
                Console.WriteLine("[AVATARTUBE] App.Services not available.");
                return;
            }

            // Close any pre-existing tube (e.g. the app created one when AvatarEnabled) so the
            // instance + per-bubble leak assertions start from a known baseline of zero.
            foreach (var existing in lifetime.Windows.OfType<AvatarTubeWindow>().ToArray())
            {
                try { existing.Close(); } catch { }
            }
            await PumpAsync(300);

            // (a) Stand-in parent: a plain visible window the tube attaches to (replaces MainWindow).
            standIn = new Window
            {
                Title = "AvatarTube verify stand-in parent",
                Width = 1000,
                Height = 1000,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            standIn.Show();
            await PumpAsync(600); // realize handle + first layout

            tube = new AvatarTubeWindow(standIn);
            tube.Show();
            // Wait for Opened -> OnFirstContentRendered (registers the hit-test hook + first anchor).
            await PumpAsync(1000);

            shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "avatartube-verify");
            Directory.CreateDirectory(shotDir);

            // (b) BASELINE.
            var baseGeo = tube.VerifyGeometrySnapshot();
            CaptureTubePng(tube, shotDir, "step00_baseline");

            Add(checks, "baseline", "tube visible after Show",
                tube.IsVisible ? "PASS" : "FAIL",
                tube.IsVisible ? "IsVisible=true" : "IsVisible=false (ShowGiggle would early-out)");
            Add(checks, "baseline", "finalScale computed (not NaN)",
                !double.IsNaN(baseGeo.finalScale) ? "PASS" : "FAIL",
                $"finalScale={baseGeo.finalScale:F4}");
            Add(checks, "baseline", "avatar rendered rect valid",
                !double.IsNaN(baseGeo.avatarX) ? "PASS" : "FAIL",
                $"avatar=({baseGeo.avatarX:F1},{baseGeo.avatarY:F1}) {baseGeo.avatarW:F1}x{baseGeo.avatarH:F1}");
            Add(checks, "baseline", "hit-test hook registered",
                tube.VerifyHitTestHookRegistered ? "PASS" : "FAIL",
                tube.VerifyHitTestHookRegistered ? "_hitTestHook set" : "_hitTestHook null (OnFirstContentRendered did not run)");
            Add(checks, "baseline", "single AvatarTubeWindow instance",
                CountTubes(lifetime) == 1 ? "PASS" : "FAIL",
                $"count={CountTubes(lifetime)}");
            // OWNER RULING 2026-07-11: portrait mode is disabled, so in steady state AvatarBorder must
            // NOT carry the portrait translate (TranslateTransform(10, -30) - the "shifts up" bug).
            // Steady = null or a transform with no upward Y translation (legacy scale/X-offset are fine).
            Add(checks, "baseline", "AvatarBorder transform steady (portrait mode disabled)",
                tube.VerifyAvatarBorderTransformIsSteady ? "PASS" : "FAIL",
                tube.VerifyAvatarBorderTransformIsSteady
                    ? "no portrait Y-shift on AvatarBorder"
                    : "portrait Y-translate present on AvatarBorder");

            // (c)+(d) Fire 10 consecutive speech bubbles via the PUBLIC entry, asserting geometry
            // holds after each. This is the exact owner repro (a bubble appears -> avatar must NOT
            // shift/shrink/freeze). WPF path: callers use ShowGiggle (axaml.cs:1321) which routes to
            // PopulateSpeechBubble (axaml.cs:1367) + AdjustBubbleSize (axaml.cs:1373) + the show path.
            for (int i = 1; i <= BubbleCount; i++)
            {
                tube.ShowGiggle($"Verify bubble #{i}: the avatar must hold its geometry across speech.");
                await PumpAsync(250); // let layout + any coalesced geometry pass settle

                var g = tube.VerifyGeometrySnapshot();

                AssertNear(checks, $"bubble{i}", "finalScale stable",
                    g.finalScale, baseGeo.finalScale, ScaleEpsilon);
                AssertNearPair(checks, $"bubble{i}", "tube window size stable",
                    g.winW, baseGeo.winW, g.winH, baseGeo.winH, SizeEpsilon);
                AssertNearPair(checks, $"bubble{i}", "ContentViewbox size stable",
                    g.viewboxW, baseGeo.viewboxW, g.viewboxH, baseGeo.viewboxH, SizeEpsilon);
                AssertNear(checks, $"bubble{i}", "avatar X stable (no horizontal shift)",
                    g.avatarX, baseGeo.avatarX, AvatarEpsilon);
                AssertNear(checks, $"bubble{i}", "avatar Y within +/-4px liveness bob",
                    g.avatarY, baseGeo.avatarY, AvatarYTolerance);
                AssertNearPair(checks, $"bubble{i}", "avatar size stable (no shrink/freeze)",
                    g.avatarW, baseGeo.avatarW, g.avatarH, baseGeo.avatarH, AvatarEpsilon);

                int tubesNow = CountTubes(lifetime);
                int bubblesNow = AvatarTubeWindow.VerifyRandomBubbleLiveCount;
                Add(checks, $"bubble{i}", "AvatarTubeWindow count did not grow (no per-bubble leak)",
                    tubesNow == 1 ? "PASS" : "FAIL", $"count={tubesNow}");
                Add(checks, $"bubble{i}", "random-bubble live count within ceiling",
                    bubblesNow <= AvatarTubeWindow.VerifyRandomBubbleMaxLive ? "PASS" : "FAIL",
                    $"live={bubblesNow} ceiling={AvatarTubeWindow.VerifyRandomBubbleMaxLive}");

                if (i == 1 || i == 5 || i == 10)
                    CaptureTubePng(tube, shotDir, $"step{i:D2}_after_bubble{i}");
            }

            // (e) Detached assertions.
            // WPF Windowing.cs:1477-1485 (detach) sets CanResize=true + visible vessel as drag
            // handle; Avalonia parity Detach() (Windowing.cs Detach) does the same.
            tube.Detach();
            await PumpAsync(450);
            CaptureTubePng(tube, shotDir, "step11_detached");

            Add(checks, "detach", "CanResize==true after Detach",
                tube.CanResize ? "PASS" : "FAIL", $"CanResize={tube.CanResize}");
            // The art-only WM_NCHITTEST hook (Windowing.cs TubeWndProcHook) returns HTTRANSPARENT
            // for non-art points ONLY when !_isAttached; CanResize==true is the detached indicator.
            // Combined with hook registration, that is the "art-only active" state.
            Add(checks, "detach", "WM_NCHITTEST art-only hook active while detached",
                (tube.VerifyHitTestHookRegistered && tube.CanResize) ? "PASS" : "FAIL",
                $"hook={tube.VerifyHitTestHookRegistered} detached={tube.CanResize}");

            // Simulated detached-drag MoveTo must change BOTH axes (TubeGeometryController.MoveTo,
            // clamped to the work area; detached mode never re-anchors over it).
            var before = tube.Position;
            var screen = tube.Screens.ScreenFromWindow(tube) ?? tube.Screens.Primary;
            double scaling = tube.RenderScaling;
            double physW = (!double.IsNaN(tube.Width) && tube.Width > 0 ? tube.Width : tube.ClientSize.Width) * scaling;
            double physH = (!double.IsNaN(tube.Height) && tube.Height > 0 ? tube.Height : tube.ClientSize.Height) * scaling;
            var wa = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            // Pick the opposite half of the work area on each axis so the move is guaranteed to
            // differ in both X and Y (clamped to stay fully on-screen).
            int targetX = before.X < (wa.X + wa.Width / 2) ? wa.Right - (int)physW - 24 : wa.X + 24;
            int targetY = before.Y < (wa.Y + wa.Height / 2) ? wa.Bottom - (int)physH - 24 : wa.Y + 24;
            tube.VerifyMoveTo(new PixelPoint(targetX, targetY));
            await PumpAsync(350);
            var after = tube.Position;
            Add(checks, "detach", "MoveTo changed X",
                after.X != before.X ? "PASS" : "FAIL",
                $"before.X={before.X} after.X={after.X} target={targetX}");
            Add(checks, "detach", "MoveTo changed Y",
                after.Y != before.Y ? "PASS" : "FAIL",
                $"before.Y={before.Y} after.Y={after.Y} target={targetY}");

            // Re-attach: WPF Windowing.cs:1529-1531 restores NoResize; Avalonia Attach() mirrors it.
            tube.Attach();
            await PumpAsync(450);
            Add(checks, "attach", "CanResize==false after Attach",
                !tube.CanResize ? "PASS" : "FAIL", $"CanResize={tube.CanResize}");

            // Teardown: instance + per-bubble counts return to baseline.
            try { tube.Close(); } catch { }
            await PumpAsync(450);
            Add(checks, "teardown", "AvatarTubeWindow instance returns to baseline (0)",
                CountTubes(lifetime) == 0 ? "PASS" : "FAIL", $"count={CountTubes(lifetime)}");
            Add(checks, "teardown", "random-bubble live count returns to baseline (0)",
                AvatarTubeWindow.VerifyRandomBubbleLiveCount == 0 ? "PASS" : "FAIL",
                $"live={AvatarTubeWindow.VerifyRandomBubbleLiveCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVATARTUBE] ERROR: {ex}");
            checks.Add(new Check { Step = "fatal", Assertion = "harness ran to completion",
                Result = "FAIL", Detail = ex.Message });
        }
        finally
        {
            var fail = checks.Any(c => c.Result == "FAIL");
            PrintReport(checks, shotDir);
            Environment.ExitCode = fail ? 2 : 0;
            try { tube?.Close(); } catch { }
            try { standIn?.Close(); } catch { }
            await Task.Delay(400); // let teardown settle before shutdown
            Shutdown(fail ? 2 : 0);
        }
    }

    // ================= Helpers =================

    private static int CountTubes(IClassicDesktopStyleApplicationLifetime lifetime)
        => lifetime.Windows.Count(w => w is AvatarTubeWindow);

    private static async Task PumpAsync(int ms)
    {
        // Run queued dispatcher jobs (coalesced geometry passes live at Render priority), then
        // yield for the wall-clock delay so timers (speech hide, scale-settle) can fire.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        await Task.Delay(ms);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    }

    private static void Add(List<Check> checks, string step, string assertion, string result, string detail)
    {
        checks.Add(new Check { Step = step, Assertion = assertion, Result = result, Detail = detail });
        Console.WriteLine($"[AVATARTUBE] {result,-4} | {step,-9} | {assertion} | {detail}");
    }

    private static void AssertNear(List<Check> checks, string step, string label, double actual, double baseline, double eps)
    {
        bool ok = !double.IsNaN(actual) && !double.IsNaN(baseline) && Math.Abs(actual - baseline) <= eps;
        Add(checks, step, label, ok ? "PASS" : "FAIL",
            $"base={baseline:F3} now={actual:F3} |d|={Math.Abs(actual - baseline):F3} eps={eps:F2}");
    }

    private static void AssertNearPair(List<Check> checks, string step, string label,
        double actualA, double baselineA, double actualB, double baselineB, double eps)
    {
        bool ok = !double.IsNaN(actualA) && !double.IsNaN(baselineA)
                  && !double.IsNaN(actualB) && !double.IsNaN(baselineB)
                  && Math.Abs(actualA - baselineA) <= eps && Math.Abs(actualB - baselineB) <= eps;
        Add(checks, step, label, ok ? "PASS" : "FAIL",
            $"base={baselineA:F2}x{baselineB:F2} now={actualA:F2}x{actualB:F2} eps={eps:F1}");
    }

    private static void CaptureTubePng(AvatarTubeWindow tube, string dir, string label)
    {
        try
        {
            IntPtr hwnd = tube.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"[AVATARTUBE] capture SKIP {label}: no HWND yet");
                return;
            }
            if (!GetWindowRect(hwnd, out RECT r))
            {
                Console.WriteLine($"[AVATARTUBE] capture SKIP {label}: GetWindowRect failed");
                return;
            }
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0)
            {
                Console.WriteLine($"[AVATARTUBE] capture SKIP {label}: zero-size rect ({w}x{h})");
                return;
            }
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }
            var path = Path.Combine(dir, $"{label}.png");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"[AVATARTUBE] captured {label} -> {path} ({w}x{h})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVATARTUBE] capture failed {label}: {ex.Message}");
        }
    }

    private static void PrintReport(List<Check> checks, string? shotDir)
    {
        Console.WriteLine();
        Console.WriteLine("[AVATARTUBE] ===================== REPORT =====================");
        Console.WriteLine($"[AVATARTUBE] {checks.Count(c => c.Result == "PASS")}/{checks.Count} assertions PASS");
        var fails = checks.Where(c => c.Result == "FAIL").ToList();
        if (fails.Count > 0)
        {
            Console.WriteLine("[AVATARTUBE] FAILURES:");
            foreach (var f in fails)
                Console.WriteLine($"[AVATARTUBE]   - [{f.Step}] {f.Assertion}: {f.Detail}");
        }
        if (!string.IsNullOrEmpty(shotDir) && Directory.Exists(shotDir))
        {
            Console.WriteLine($"[AVATARTUBE] PNG dir: {shotDir}");
            foreach (var p in Directory.GetFiles(shotDir, "*.png").OrderBy(p => p))
                Console.WriteLine($"[AVATARTUBE]   {Path.GetFileName(p)}");
        }
        Console.WriteLine("[AVATARTUBE] ======================================================");
    }

    private static void Shutdown(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                // Pass the code through the lifetime: Avalonia's shutdown otherwise overwrites
                // Environment.ExitCode with its own default (0) — same convention as --verify-layers.
                lifetime?.Shutdown(exitCode);
            }
            catch { }
        });
    }
}
