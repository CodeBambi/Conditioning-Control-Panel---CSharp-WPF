using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Offscreen verification rig for POSSESSION (launched via <c>--possession-preview [outDir]</c>).
///
/// <para><b>What it does.</b> Navigates the real main window to the Lockdown card, then applies every
/// effect in <see cref="PossessionEffectCatalog"/> one at a time against a real victim from the real
/// target registry, photographing four moments per effect: before, mid-charge, live, undone. It writes
/// a <c>_report.txt</c> naming the victim, whether the effect went live, and whether Undo put the
/// control back on the exact pixel it started from.</para>
///
/// <para><b>What it deliberately does NOT do:</b> it never touches <c>LockdownService</c>. A real
/// lockdown installs a low-level keyboard hook, forces Strict Lock and turns the panic key off - it is
/// designed to be hard to escape, which is the last thing an unattended rig should arm. Everything here
/// is the haunt plumbing built by hand (host + <see cref="EmberAttribution"/> + a hand-rolled
/// <see cref="PossessionContext"/>) and driven straight at the effects, exactly as the director would.</para>
///
/// <para>Capture is <see cref="RenderTargetBitmap"/> at 96 DPI over the window's Content, which IS
/// RootGrid, so the GhostLayer and RubbleFloor (children of RootGrid) are in every shot. See
/// <see cref="DoorShooter"/> for why in-process rasterization rather than a screen grab.</para>
/// </summary>
internal static class PossessionPreview
{
    /// <summary>The ladder, bottom to top. Names must match IPossessionEffect.Id.</summary>
    private static readonly string[] Order =
    {
        "nudge", "typo", "breathe",              // R0 Settle
        "drift", "swap", "dodge",                // R1 Drift
        "melt", "wobble", "dissolve",            // R2 Melt
        "drop", "fall", "crack",                 // R3 Collapse
        "retitle", "dokidialog",                 // R4 It knows
    };

    /// <summary>Effects that arm a hover handler on Apply and only bite when the pointer arrives.
    /// Both hang off the victim's own MouseEnter, which is a Direct routed event we can raise on the
    /// element - so they DO get exercised here. Dodge is the exception: it reads the live cursor
    /// position off the mouse device inside a window-level PreviewMouseMove, which no synthetic event
    /// can fake, so dodge is only photographed armed.</summary>
    private static readonly HashSet<string> HoverDriven =
        new(StringComparer.OrdinalIgnoreCase) { "melt", "dissolve" };

    /// <summary>Hard ceiling for the whole run; the app shuts down whatever is left.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(4);

    /// <summary>Startup cards that would otherwise open a modal over the shots. The rig also sets
    /// MainWindow.IsStartupDialogShowing, which is the shipped suppression gate for most of them;
    /// this sweeper is the belt to that braces.</summary>
    private static readonly string[] StrayDialogMarkers =
    {
        "IntroPopup", "WhatsNew", "Recap", "Tour", "Wizard", "Celebration",
        "UpdateNotification", "TierChanged", "Announcement",
    };

    public static void Run(Window window, string outDir)
    {
        // Deferred to Loaded for the same reason DoorShooter defers: rendering an unarranged visual
        // yields an empty bitmap and reads exactly like a broken effect.
        if (window.IsLoaded) _ = RunCore(window, outDir);
        else window.Loaded += (_, _) => _ = RunCore(window, outDir);
    }

    private static async Task RunCore(Window window, string outDir)
    {
        var report = new List<string>();
        var names = new List<string>();
        var sw = Stopwatch.StartNew();
        DispatcherTimer? sweeper = null;
        var shots = 0;

        try
        {
            Directory.CreateDirectory(outDir);
            App.Logger?.Information("PossessionPreview: writing to {Dir}", outDir);

            InstallDiagnostics(window);

            MainWindow.IsStartupDialogShowing = true;
            sweeper = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            sweeper.Tick += (_, __) => CloseStrayDialogs();
            sweeper.Start();

            await Task.Delay(2500).ConfigureAwait(true);
            App.Logger?.Information("PossessionPreview: settle done, navigating");

            // ---- 1. the Lockdown card ------------------------------------------------------
            var doorOk = Click(window, "DoorPlay");
            App.Logger?.Information("PossessionPreview: DoorPlay clicked = {Ok}", doorOk);
            await Task.Delay(800).ConfigureAwait(true);
            var railOk = Click(window, "BtnNavLockdown");
            App.Logger?.Information("PossessionPreview: BtnNavLockdown clicked = {Ok}", railOk);
            await Task.Delay(1600).ConfigureAwait(true);
            App.Logger?.Information("PossessionPreview: navigation settled");

            var tabState = "unknown";
            if (window is MainWindow mw0)
            {
                try { tabState = mw0.LockdownTab?.Visibility.ToString() ?? "null"; } catch { }
            }
            report.Add($"nav: DoorPlay={(doorOk ? "ok" : "FAILED")} BtnNavLockdown={(railOk ? "ok" : "FAILED")} LockdownTab.Visibility={tabState}");

            if (Capture(window, Path.Combine(outDir, "00-lockdown-card.png"))) shots++;

            if (window is not IPossessionHost host)
            {
                report.Add("FATAL: the main window does not implement IPossessionHost - nothing to drive");
                return;
            }

            // ---- 2. the haunt plumbing, by hand -------------------------------------------
            var attribution = new EmberAttribution(host, () => false);
            var effects = PossessionEffectCatalog.CreateAll();

            PossessionContext MakeCtx(PossessionRung rung) => new()
            {
                Host = host,
                Attribution = attribution,
                Rung = rung,
                Intensity = PossessionIntensity.FullDoki,
                Photosafe = false,
                Rng = new Random(7),
                ElapsedFraction = FractionFor(rung),
                Remaining = TimeSpan.FromMinutes(3),
                Name = (id, t) =>
                {
                    names.Add(id + " -> " + (t ?? "(no name)"));
                    App.Logger?.Information("PossessionPreview: warden names {Id} / {Target}", id, t);
                },
            };

            // Inventory first: an effect with no victim is a target-registry problem, not an effect bug,
            // and the report has to be able to tell those apart.
            try
            {
                var inv = host.Targets
                    .GroupBy(t => t.Role)
                    .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
                    .Select(g => g.Key + "x" + g.Count());
                report.Add("targets on the Lockdown card: " + string.Join(", ", inv));
            }
            catch (Exception ex) { report.Add("targets: walk failed - " + ex.Message); }

            // The coordinate probe. Every ember visual and every ghost positions itself in GhostLayer
            // space, so if that math is wrong the whole attribution grammar is invisible while every
            // effect still reports success. Probe it explicitly rather than inferring it from pixels.
            try
            {
                var layer = host.GhostLayer;
                report.Add($"ghost layer: {layer?.ActualWidth:F1}x{layer?.ActualHeight:F1} children={layer?.Children.Count}");
                foreach (var probe in host.Targets.GroupBy(x => x.Role).Select(g => g.First()))
                {
                    var el = probe.Element;
                    string tta;
                    try
                    {
                        var m = el.TransformToVisual(layer);
                        tta = "ok " + m.TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                    }
                    catch (Exception ex) { tta = "THROWS " + ex.GetType().Name; }

                    report.Add($"probe {probe.Key} [{probe.Role}]: PointOf={Fmt(host.PointOf(el))} Actual={el.ActualWidth:F1}x{el.ActualHeight:F1}"
                               + $" | TransformToVisual(layer)={tta}"
                               + $" | PossessionVisual.BoundsOf={PossessionVisual.BoundsOf(host, el)}"
                               + $" | ScaleOf={PossessionVisual.ScaleOf(host, el)}");
                }
            }
            catch (Exception ex) { report.Add("probe failed: " + ex.Message); }

            report.Add("");

            // ---- 3. every effect, one at a time --------------------------------------------
            var index = 0;
            foreach (var id in Order)
            {
                index++;
                var prefix = index.ToString("00", CultureInfo.InvariantCulture) + "-" + id;

                if (sw.Elapsed > Ceiling)
                {
                    report.Add(prefix + ": SKIPPED - 4 minute ceiling reached");
                    continue;
                }

                var effect = effects.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                if (effect == null)
                {
                    report.Add(prefix + ": MISSING from PossessionEffectCatalog.CreateAll()");
                    continue;
                }

                try
                {
                    shots += await RunOne(window, host, outDir, prefix, effect, MakeCtx(effect.MinRung), report)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    report.Add(prefix + ": EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
                    App.Logger?.Error(ex, "PossessionPreview: {Id} blew up", id);
                    // A failing effect must never take the run with it - make sure it is not left live.
                    try { await effect.UndoAsync(TimeSpan.Zero).ConfigureAwait(true); } catch { }
                }
            }

            report.Add("");
            report.Add("warden named: " + (names.Count == 0 ? "(nothing)" : string.Join(" | ", names)));
        }
        catch (Exception ex)
        {
            report.Add("RIG FAILED: " + ex.GetType().Name + ": " + ex.Message);
            App.Logger?.Error(ex, "PossessionPreview failed");
        }
        finally
        {
            try { sweeper?.Stop(); } catch { }
            try { MainWindow.IsStartupDialogShowing = false; } catch { }

            try
            {
                var head = new StringBuilder();
                head.AppendLine("PossessionPreview - " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                head.AppendLine("usage: ConditioningControlPanel.exe --possession-preview <outDir>");
                head.AppendLine("shots: " + shots + "   elapsed: " + sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s");
                head.AppendLine("LockdownService was NOT activated (no keyboard hook, no safeties).");
                head.AppendLine();
                File.WriteAllText(Path.Combine(outDir, "_report.txt"),
                                  head + string.Join(Environment.NewLine, report) + Environment.NewLine);
            }
            catch (Exception ex) { App.Logger?.Error(ex, "PossessionPreview: report write failed"); }

            try
            {
                File.WriteAllText(Path.Combine(outDir, "_done.txt"),
                    string.Format(CultureInfo.InvariantCulture, "{0} shots at {1:o}", shots, DateTime.Now));
            }
            catch { }

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => Application.Current.Shutdown()));
        }
    }

    /// <summary>One effect, four shots. Returns the number of shots written.</summary>
    private static async Task<int> RunOne(Window window, IPossessionHost host, string outDir, string prefix,
                                          IPossessionEffect effect, PossessionContext ctx, List<string> report)
    {
        var shots = 0;
        var id = effect.Id;

        // ---- pick a victim -------------------------------------------------------------
        PossessionTarget? target = null;
        var roleMatches = 0;
        if (effect.Roles.Count > 0)
        {
            var targets = host.Targets;   // refreshed on every read by design
            foreach (var t in targets)
            {
                if (!effect.Roles.Contains(t.Role)) continue;
                roleMatches++;
                if (target == null && effect.CanApply(ctx, t)) target = t;
            }
            if (target == null)
            {
                report.Add($"{prefix}: NO TARGET - roles [{string.Join("/", effect.Roles)}], {roleMatches} visible role match(es), CanApply false on all");
                return shots;
            }
        }
        else if (!effect.CanApply(ctx, null))
        {
            report.Add(prefix + ": CanApply false (targetless effect)");
            return shots;
        }

        var el = target?.Element;
        var beforePt = el != null ? host.PointOf(el) : default;
        var beforeW = el?.ActualWidth ?? 0;
        var beforeH = el?.ActualHeight ?? 0;
        var victim = target == null
            ? "(targetless)"
            : $"{target.Key} \"{target.DisplayName}\" [{target.Role}] at {Fmt(beforePt)} {beforeW:F1}x{beforeH:F1}";

        if (Capture(window, Path.Combine(outDir, prefix + "-a-before.png"))) shots++;

        // ---- apply ---------------------------------------------------------------------
        var known = Application.Current.Windows.OfType<Window>().ToList();
        using var cts = new CancellationTokenSource();
        var note = "applied";
        var apply = effect.ApplyAsync(ctx, target, cts.Token);

        if (HoverDriven.Contains(id))
        {
            // Apply only arms the hover handler and returns; the bite comes from MouseEnter.
            if (!await WithTimeout(apply, 10000).ConfigureAwait(true)) note = "arm did not complete within 10s";
            RaiseMouseEnter(el);
            note += " (hover simulated: MouseEnter raised on the victim)";
            await Task.Delay(250).ConfigureAwait(true);
            if (Capture(window, Path.Combine(outDir, prefix + "-b-charge.png"))) shots++;
            await Task.Delay(1500).ConfigureAwait(true);
        }
        else
        {
            await Task.Delay(250).ConfigureAwait(true);
            if (Capture(window, Path.Combine(outDir, prefix + "-b-charge.png"))) shots++;
            if (!await WithTimeout(apply, 10000).ConfigureAwait(true)) note = "apply did not complete within 10s";
            if (string.Equals(id, "dodge", StringComparison.OrdinalIgnoreCase))
                note += " (hover-triggered off the live cursor; ARMED ONLY, not exercised)";
            await Task.Delay(400).ConfigureAwait(true);
        }

        if (Capture(window, Path.Combine(outDir, prefix + "-c-live.png"))) shots++;

        // A themed Doki dialog is its own Window - render it too, or the shot shows nothing at all.
        foreach (var extra in Application.Current.Windows.OfType<Window>().Where(w => !known.Contains(w)).ToList())
        {
            if (Capture(extra, Path.Combine(outDir, prefix + "-c-live-dialog.png"))) shots++;
        }

        var wasLive = effect.IsLive;

        // ---- undo ----------------------------------------------------------------------
        if (!await WithTimeout(effect.UndoAsync(TimeSpan.FromMilliseconds(600)), 10000).ConfigureAwait(true))
            note += "; undo did not complete within 10s";
        await Task.Delay(800).ConfigureAwait(true);
        if (Capture(window, Path.Combine(outDir, prefix + "-d-undone.png"))) shots++;

        // ---- did the control land back on its own pixel? --------------------------------
        var undo = "undo exact n/a (targetless)";
        if (el != null)
        {
            var afterPt = host.PointOf(el);
            var dx = afterPt.X - beforePt.X;
            var dy = afterPt.Y - beforePt.Y;
            var dw = el.ActualWidth - beforeW;
            var dh = el.ActualHeight - beforeH;
            var exact = Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5 && Math.Abs(dw) < 0.5 && Math.Abs(dh) < 0.5;
            undo = exact
                ? "undo exact yes"
                : $"UNDO NOT EXACT dx={dx:F2} dy={dy:F2} dw={dw:F2} dh={dh:F2} (before {Fmt(beforePt)} {beforeW:F1}x{beforeH:F1} -> after {Fmt(afterPt)} {el.ActualWidth:F1}x{el.ActualHeight:F1})";
        }

        report.Add($"{prefix}: rung={effect.MinRung} big={effect.IsBig} target={victim}; {note}; IsLive after c-shot={wasLive}; IsLive after undo={effect.IsLive}; {undo}");
        return shots;
    }

    private static double FractionFor(PossessionRung rung) => rung switch
    {
        PossessionRung.Settle => 0.05,
        PossessionRung.Drift => 0.20,
        PossessionRung.Melt => 0.45,
        PossessionRung.Collapse => 0.70,
        _ => 0.92,
    };

    private static string Fmt(Point p) =>
        "(" + p.X.ToString("F1", CultureInfo.InvariantCulture) + "," + p.Y.ToString("F1", CultureInfo.InvariantCulture) + ")";

    /// <summary>Awaits the task but never lets one wedged effect eat the run. Returns false on
    /// timeout; a faulted task is logged and swallowed.</summary>
    private static async Task<bool> WithTimeout(Task task, int ms)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms)).ConfigureAwait(true);
        if (!ReferenceEquals(done, task)) return false;
        try { await task.ConfigureAwait(true); }
        catch (Exception ex) { App.Logger?.Warning("PossessionPreview: awaited task faulted: {Error}", ex.Message); }
        return true;
    }

    /// <summary>MouseEnter is a Direct routed event, so raising it on the element runs exactly the
    /// handlers melt and dissolve attached in ApplyCore. Nothing in the effects is modified or
    /// reflected into.</summary>
    private static void RaiseMouseEnter(FrameworkElement? el)
    {
        try
        {
            if (el == null) return;
            el.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = Mouse.MouseEnterEvent,
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("PossessionPreview: MouseEnter simulation failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// The first run of this rig died 600ms into navigation with no report and no crash log: something
    /// closed the app while the rig was mid-click. These hooks name the culprit - every button click in
    /// the process, and a stack at Closing / Exit / SessionEnding. Cheap, and this rig only ever runs
    /// under its own command-line flag.
    /// </summary>
    private static void InstallDiagnostics(Window window)
    {
        try
        {
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
                new RoutedEventHandler((s, _) =>
                    App.Logger?.Information("PossessionPreview[diag]: Click on '{Name}' ({Type})",
                                            (s as FrameworkElement)?.Name, s?.GetType().Name)), true);

            window.Closing += (_, __) =>
                App.Logger?.Information("PossessionPreview[diag]: main window Closing\n{Stack}", Environment.StackTrace);

            if (Application.Current != null)
            {
                Application.Current.Exit += (_, __) =>
                    App.Logger?.Information("PossessionPreview[diag]: Application.Exit\n{Stack}", Environment.StackTrace);
                Application.Current.SessionEnding += (_, __) =>
                    App.Logger?.Information("PossessionPreview[diag]: SessionEnding");
            }
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionPreview: diagnostics hook failed: {Error}", ex.Message); }
    }

    private static void CloseStrayDialogs()
    {
        try
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (w is MainWindow) continue;
                var n = w.GetType().Name;
                if (!StrayDialogMarkers.Any(m => n.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                App.Logger?.Information("PossessionPreview: closing stray startup dialog {Name}", n);
                try { w.Close(); } catch { }
            }
        }
        catch { }
    }

    /// <summary>Navigates by raising the real Click on the named button, so the shipped handler runs
    /// (same reason as DoorShooter). False when the button is missing or disabled.</summary>
    private static bool Click(Window window, string name)
    {
        if (window.FindName(name) is not ButtonBase b)
        {
            App.Logger?.Warning("PossessionPreview: '{Name}' not found", name);
            return false;
        }
        if (!b.IsEnabled)
        {
            App.Logger?.Information("PossessionPreview: '{Name}' is disabled - skipped", name);
            return false;
        }
        b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    /// <summary>Window background first, then Content, both into one 96-DPI bitmap. See the long note
    /// in DoorShooter.Capture: rendering Content alone leaves alpha-0 where the window's own brush
    /// should be, and a VisualBrush would re-origin the content on its descendant bounds.</summary>
    private static bool Capture(Window window, string path)
    {
        try
        {
            var source = (Visual?)window.Content ?? window;
            var size = window.Content is FrameworkElement fe && fe.ActualWidth > 0
                ? new Size(fe.ActualWidth, fe.ActualHeight)
                : new Size(window.ActualWidth, window.ActualHeight);

            if (size.Width < 4 || size.Height < 4)
            {
                App.Logger?.Warning("PossessionPreview: {Path} skipped - degenerate size {W}x{H}",
                                    path, size.Width, size.Height);
                return false;
            }

            var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height),
                                             96, 96, PixelFormats.Pbgra32);

            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, size.Width, size.Height));
            rtb.Render(backdrop);
            rtb.Render(source);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "PossessionPreview: capture failed for {Path}", path);
            return false;
        }
    }
}
