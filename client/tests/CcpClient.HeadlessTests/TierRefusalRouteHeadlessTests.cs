using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>The Play card's tier refusal, said out loud — and the press it must never take away</b>
/// (census #41).
///
/// <para>Upstream's refusal is an 8-second Warning toast (<c>Services/TierGate.cs:126-134</c>) and
/// it exists because of a defect upstream recorded in its own words: a bare refusal left "no
/// dialog, no toast and nothing tying the jump to the card they clicked"
/// (<c>MainWindow/MainWindow.Lab.cs:282-288</c>). The port raises the same toast at the same
/// severity for the same duration. Upstream's "See tiers" ACTION is still absent: it opens an App
/// Info &amp; Data page this port does not have, so the route is named in words inside the message
/// (<see cref="DtrhGate.UpgradeRoute"/>) instead of behind a dead button.</para>
///
/// <para><b>Announcing it cost a relayout, and that is the fact this file mostly is.</b> Docked
/// top-right — where upstream's host lives — the refusal toast CONTAINED <c>FALL IN</c> whole at
/// every window size, because both are right-aligned inside the same page area and track each
/// other as the window resizes. A toast body carries a background and captures its own clicks
/// (upstream's do too, <c>MainWindow/MainWindow.xaml:3212-3216</c>), so the refusal silently
/// disabled the button that raised it for eight seconds and took away the one thing this card
/// guarantees in every branch: "a gated press must ARRIVE"
/// (<c>Views/Tabs/PlayTabView.xaml:503-506</c>). The surface is now docked bottom-right
/// (<c>Views/MainWindow.axaml</c>, <c>ToastLayer</c>).</para>
///
/// <para><b>Every occlusion claim here is made by PRESSING, never by asking.</b> Avalonia's
/// <c>InputHitTest</c> answered "FallInButton" for a point the toast owned, between the toast's
/// layout pass and the first pointer event, and only agreed with the delivered press afterwards —
/// so a fact built on the query would have called a covered button reachable and been wrong. The
/// arrival counter is the launcher's own <see cref="DtrhLaunch.GateArrivals"/>: it counts presses
/// that reached the gate, so a press swallowed by a toast cannot increment it.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): arranged bounds and real
/// headless pointer input. Nothing here claims a composited pixel, and nothing here runs on
/// Linux.</para>
/// </summary>
public class TierRefusalRouteHeadlessTests : HeadlessTest
{
    /// <summary>The only "credential" this file handles. It says what it is.</summary>
    private const string FixtureToken = "SP094-TIER-ROUTE-NOT-A-REAL-TOKEN-4c1f92";

    /// <summary>The shell's own declared size (<c>Views/MainWindow.axaml</c>), and four more the
    /// user can drag to. The refusal toast is a fixed 360x153 for this sentence, so a placement
    /// proved at one size proves nothing about another — the top-right one looked survivable until
    /// it was measured at all five.</summary>
    private static readonly (double Width, double Height)[] WindowSizes =
    [
        (1100, 760), (900, 640), (800, 560), (1400, 900), (1920, 1080),
    ];

    private async Task<(ApplicationHost Host, MainWindow Window, DtrhLaunch Dtrh)> BootRefusingAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-tier-route-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            // An authority that ANSWERED "no pledge" about this account — the only input that
            // produces RefusedNotEntitled, which is the only branch upstream toasts.
            EntitlementFactory = _ => new HostLoginEntitlement(new FixtureReader(), new RefusingAuthority()),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return (host, window, window.Dtrh);
    }

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static Rect BoundsIn(MainWindow window, Visual control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        return new Rect(origin, control.Bounds.Size);
    }

    /// <summary>A real MouseDown/MouseUp at the control's own centre, which is the only kind of
    /// occlusion evidence this file accepts.</summary>
    private static void Press(MainWindow window, Control control)
    {
        var centre = control.TranslatePoint(
                         new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static IReadOnlyList<Rect> ToastRects(MainWindow window) =>
        [.. window.Toasts.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("toast"))
            .Select(b => BoundsIn(window, b))];

    private static async Task PressAndAwaitRefusalAsync(MainWindow window, DtrhLaunch dtrh, string button)
    {
        var before = dtrh.GateArrivals;
        Press(window, Descendant<Button>(window, button));
        await TestWait.Until(
            () => dtrh.GateArrivals > before && dtrh.LastDecision is DtrhGateDecision.RefusedNotEntitled,
            $"the press on {button} to reach the gate and be refused",
            () => $"arrivals={dtrh.GateArrivals} (was {before}), decision={dtrh.LastDecision}");
        window.UpdateLayout();
    }

    /// <summary>
    /// <b>The announcement half of census #41, end to end.</b> A real press on a real refusal
    /// raises upstream's toast — same sentence as the band, upstream's Warning severity, upstream's
    /// eight seconds (<c>Services/TierGate.cs:133</c>) — and both gated buttons are still pressable
    /// while it is up, for two further refusals that do not stack a second copy on top of it.
    ///
    /// <para>The duration is read off the host's injected timer seam rather than waited for, so
    /// nothing here touches a clock.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheRefusalIsAnnouncedAtUpstreamsSeverityAndDuration_AndTheGatedPressStillArrivesUnderIt()
    {
        var (host, window, dtrh) = await BootRefusingAsync();

        // The one-shot timer seam, replaced so the toast cannot expire mid-fact and its requested
        // duration is readable. Nothing is scheduled on a real clock from here on.
        var requested = new List<TimeSpan>();
        window.Toasts.Schedule = (due, _) =>
        {
            requested.Add(due);
            return new NoDismiss();
        };

        Press(window, window.FindControl<RadioButton>("DoorPlay")!);
        await PressAndAwaitRefusalAsync(window, dtrh, "FallInButton");

        // (1) IT WAS SAID. The toast carries the gate's own sentence, not a paraphrase.
        Assert.Equal([DtrhGate.TierRefusalMessage], window.Toasts.Messages);
        Assert.Equal([PlayPage.TierRefusalToastDuration], requested);
        Assert.Equal(TimeSpan.FromSeconds(8), PlayPage.TierRefusalToastDuration);

        var toast = window.Toasts.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("toast"));
        Assert.Contains("warning", toast.Classes);   // upstream's NotificationType.Warning

        // The plate says it too, durably: the toast expires and the band does not.
        Assert.True(Descendant<Border>(window, "GateBand").IsVisible);

        // (2) AND THE PRESS STILL ARRIVES. Twice more, both buttons, with the refusal on screen —
        // pressed for real at each button's own centre, never hit-tested.
        var fallIn = BoundsIn(window, Descendant<Button>(window, "FallInButton"));
        var quickDrop = BoundsIn(window, Descendant<Button>(window, "QuickDropButton"));
        foreach (var rect in ToastRects(window))
        {
            Assert.False(rect.Intersects(fallIn), $"the toast at {rect} overlaps FALL IN at {fallIn}");
            Assert.False(rect.Intersects(quickDrop), $"the toast at {rect} overlaps Quick Drop at {quickDrop}");
        }

        await PressAndAwaitRefusalAsync(window, dtrh, "FallInButton");
        await PressAndAwaitRefusalAsync(window, dtrh, "QuickDropButton");
        Assert.Equal(3, dtrh.GateArrivals);

        // (3) AND THREE REFUSALS ARE STILL ONE TOAST, WHICH IS LOAD-BEARING RATHER THAN TIDY. This
        // fact reported the defect itself the first time it ran: three stacked copies grew the
        // surface to 489 DIP, from y=121 to the foot of a 610-DIP page area, and the third one
        // covered FALL IN at y=157 again — the very failure the dock moved to prevent. Each press
        // asks for its own full eight seconds; only the copies are refused (ToastHost.Show).
        Assert.Equal([DtrhGate.TierRefusalMessage], window.Toasts.Messages);
        Assert.Equal(
            [PlayPage.TierRefusalToastDuration, PlayPage.TierRefusalToastDuration, PlayPage.TierRefusalToastDuration],
            requested);

        var stacked = BoundsIn(window, Descendant<Button>(window, "FallInButton"));
        foreach (var rect in ToastRects(window))
        {
            Assert.False(rect.Intersects(stacked),
                $"after three refusals the toast at {rect} overlaps FALL IN at {stacked}");
        }

        await host.ShutdownAsync();
    }

    /// <summary>
    /// <b>The placement, at five window sizes, proved by pressing.</b> The old top-right dock
    /// failed this at all five — the toast contained <c>FALL IN</c> whole from 1183,131 at 800x560
    /// to 1703,131 at 1920x1080 — because the toast and the card's buttons are both right-aligned
    /// inside the same page area. One window size would not have caught that, and would not catch
    /// the next placement that only works at one.
    ///
    /// <para><b>The claim is exactly three things, and the third is the only one that matters to a
    /// user:</b> the toast never contains a launch button, never covers the point a press lands on,
    /// and the press arrives. Total clearance is NOT claimed at every size and the measurement says
    /// why: the shell declares no minimum, and at 800x560 the page area is 600x410, where the
    /// refusal band pushes Quick Drop down to y 229-257 and the toast's top edge is at 247 — the
    /// button's bottom 10 DIP are under it, its centre is not, and the press still arrives. A
    /// 360x153 overlay cannot be wholly clear of a 600x410 page; the top-right dock was not clear
    /// there either, and covered FALL IN's centre instead.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheRefusalToastStaysOffThisCardsButtons_AtEveryWindowSizeMeasured()
    {
        var (host, window, dtrh) = await BootRefusingAsync();
        window.Toasts.Schedule = (_, _) => new NoDismiss();
        Press(window, window.FindControl<RadioButton>("DoorPlay")!);

        foreach (var (width, height) in WindowSizes)
        {
            window.Width = width;
            window.Height = height;

            // A HEADLESS RESIZE LANDS ON THE DISPATCHER, NOT ON THE NEXT UpdateLayout. Without this
            // the tree keeps the PREVIOUS size's arrangement, every rect below is a rect from the
            // last iteration, and the press goes to where the button used to be — which is a test
            // that measures nothing while looking like it measures five sizes.
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.Toasts.DismissAll();

            // One refusal at this size, then the two presses that must still arrive under it.
            await PressAndAwaitRefusalAsync(window, dtrh, "FallInButton");
            Assert.Single(window.Toasts.Messages);

            var toastRect = ToastRects(window).Single();
            foreach (var name in new[] { "FallInButton", "QuickDropButton" })
            {
                var button = Descendant<Button>(window, name);
                var rect = BoundsIn(window, button);

                // The original defect, in its own words: the toast CONTAINED the button.
                Assert.False(
                    toastRect.Contains(rect),
                    $"at {width}x{height} the refusal toast at {toastRect} contains {name} at {rect}");

                // And it does not own the point a press lands on. This is the universal half of the
                // clearance; the total-clearance half is only claimed at the shell's own size, by
                // AtTheShellsOwnSize_ALiveToastOverlapsNoInteractiveControlOnAnyPage.
                Assert.False(
                    toastRect.Contains(rect.Center),
                    $"at {width}x{height} the refusal toast at {toastRect} covers the centre of {name} at {rect}");

                var before = dtrh.GateArrivals;
                Press(window, button);
                await TestWait.Until(
                    () => dtrh.GateArrivals > before,
                    $"a press on {name} at {width}x{height} to reach the gate through the refusal toast",
                    () => $"arrivals={dtrh.GateArrivals} (was {before}), toast={toastRect}, button={rect}");
            }
        }

        await host.ShutdownAsync();
    }

    /// <summary>
    /// <b>The same bug with a different name, guarded.</b> A placement change is global: this host
    /// is also the System page's phrase-backup surface (<c>Views/Pages/SystemPage.axaml.cs</c>),
    /// and a refusal that cleared the Play card by landing on somebody else's button would have
    /// moved the defect rather than fixed it. At the shell's own declared size, with the longest
    /// message this app sends, a live toast overlaps NO interactive control on any of the five
    /// pages — scrolled to the top and scrolled to the end, because a page's lower content is
    /// exactly what a bottom dock could collide with.
    ///
    /// <para><b>What this does NOT claim.</b> The shell declares no minimum size, and at 800x560
    /// the page area is 600x410 — smaller than four toasts — so a 360x153 overlay cannot miss
    /// everything there: the Studio rack's right edge and the Mantra reps picker fall under it
    /// (the top-right dock missed those and covered <c>FALL IN</c> and the Companion page's only
    /// button instead). This fact is at the size the shell actually opens at.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task AtTheShellsOwnSize_ALiveToastOverlapsNoInteractiveControlOnAnyPage()
    {
        var (host, window, _) = await BootRefusingAsync();
        window.Toasts.Schedule = (_, _) => new NoDismiss();

        foreach (var door in new[] { "DoorStudio", "DoorCompanion", "DoorPlay", "DoorIntake", "DoorSystem" })
        {
            foreach (var scrolled in new[] { false, true })
            {
                Press(window, window.FindControl<RadioButton>(door)!);
                window.Toasts.DismissAll();
                window.Toasts.Show(DtrhGate.TierRefusalMessage, ToastKind.Warning, PlayPage.TierRefusalToastDuration);
                window.UpdateLayout();

                if (scrolled)
                {
                    foreach (var viewer in window.GetVisualDescendants().OfType<ScrollViewer>())
                    {
                        viewer.ScrollToEnd();
                    }

                    window.UpdateLayout();
                }

                var toastRect = ToastRects(window).Single();
                var pageHost = Descendant<ContentControl>(window, "PageHost");
                foreach (var control in pageHost.GetVisualDescendants().OfType<Control>())
                {
                    if (control is not (Button or ToggleButton or Slider or ComboBox or TextBox)) continue;
                    if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0) continue;

                    var rect = BoundsIn(window, control);
                    Assert.False(
                        toastRect.Intersects(rect),
                        $"on {door} (scrolled={scrolled}) the toast at {toastRect} overlaps "
                        + $"{control.Name ?? control.GetType().Name} at {rect}");
                }
            }
        }

        await host.ShutdownAsync();
    }

    /// <summary>
    /// <b>Three DIFFERENT notices at once, which is the case coalescing never covered.</b> The path
    /// is the one the user really has and it needs no flag: export phrases, import phrases — both
    /// dismiss-only, neither expires (<c>Views/Pages/SystemPage.axaml.cs</c>, upstream's two modal
    /// results, <c>MainWindow/MainWindow.PresetIO.cs:81-83</c>, <c>:125-127</c>) — then walk to Play
    /// and be refused.
    ///
    /// <para><b>The defect this fact reported before the fix, in its own numbers.</b> Stacked
    /// bottom-up the three occupied y 188..600 of a 610-DIP page area and covered BOTH launch
    /// buttons: the export result's plate at 819,188 265x46 over <c>FALL IN</c> at 883,157 172x46,
    /// and over <c>Quick Drop</c> at 905,211 150x28. That is the same defect the top-right dock was
    /// moved to escape, reached by a different road.</para>
    ///
    /// <para><b>A CAP WAS NOT THE ANSWER AND IS NOT WHAT THIS PINS.</b> Dropping an unacknowledged
    /// notice to make room is itself a defect — the user who never saw the import result is worse
    /// off than the one whose toast overlapped a button — so all three are still OWED here, and the
    /// last third of this fact spends real dismiss presses proving each one still arrives. What is
    /// bounded is the footprint: one toast on screen, the newest, because a refusal queued behind an
    /// unread export result would arrive attached to nothing.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ThreeDifferentNoticesAtOnce_AreAllStillOwed_AndCoverNoControlOnAnyPage()
    {
        var (host, window, dtrh) = await BootRefusingAsync();
        window.Toasts.Schedule = (_, _) => new NoDismiss();

        // The real sentences, from the real notice source — a phrase export and the longest import
        // result this build can produce (not persisted, with skipped pools), which is the tallest
        // toast in the app at 360x193.
        var exported = PhraseBackupNotices.Exported(36);
        var imported = PhraseBackupNotices.Imported(3, 36, ["Affirmations", "Denials"], persisted: false);
        window.Toasts.ShowUntilDismissed(exported.Message, exported.Kind);
        window.Toasts.ShowUntilDismissed(imported.Message, imported.Kind);

        Press(window, window.FindControl<RadioButton>("DoorPlay")!);
        await PressAndAwaitRefusalAsync(window, dtrh, "FallInButton");

        // (1) NOTHING WAS DROPPED, and (2) one of them is on screen — the newest, so the refusal is
        // attached to the press that raised it.
        Assert.Equal(
            [exported.Message, imported.Message, DtrhGate.TierRefusalMessage],
            window.Toasts.Messages);
        Assert.Single(ToastRects(window));

        // (3) AND IT COVERS NOTHING, ON ANY PAGE, WITH ALL THREE STILL OWED. Same sweep as
        // AtTheShellsOwnSize_ALiveToastOverlapsNoInteractiveControlOnAnyPage — that one holds the
        // placement for a single notice, this one holds it for a surface with a queue behind it.
        foreach (var door in new[] { "DoorPlay", "DoorStudio", "DoorCompanion", "DoorIntake", "DoorSystem" })
        {
            Press(window, window.FindControl<RadioButton>(door)!);
            window.UpdateLayout();

            var toastRect = Assert.Single(ToastRects(window));
            var pageHost = Descendant<ContentControl>(window, "PageHost");
            foreach (var control in pageHost.GetVisualDescendants().OfType<Control>())
            {
                if (control is not (Button or ToggleButton or Slider or ComboBox or TextBox)) continue;
                if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0) continue;

                var rect = BoundsIn(window, control);
                Assert.False(
                    toastRect.Intersects(rect),
                    $"on {door}, with three notices owed, the toast at {toastRect} overlaps "
                    + $"{control.Name ?? control.GetType().Name} at {rect}");
            }
        }

        // (4) AND EACH ONE STILL ARRIVES. Newest first, taken away by a real press on the dismiss
        // button the toast itself carries — not by DismissAll, because "the queue drains" is the
        // whole reason a cap was refused and it has to be driven the way a user drains it.
        foreach (var expected in new[] { DtrhGate.TierRefusalMessage, imported.Message, exported.Message })
        {
            var showing = window.Toasts.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("toast"));
            Assert.Equal(expected, showing.GetVisualDescendants().OfType<TextBlock>().First().Text);
            Press(window, showing.GetVisualDescendants().OfType<Button>().Single());
        }

        Assert.Empty(window.Toasts.Messages);
        Assert.Empty(ToastRects(window));

        await host.ShutdownAsync();
    }

    /// <summary>A toast that never expires on its own, so a fact can hold it up while it presses
    /// underneath it. Disposal is the production seam's cancel and is a no-op here.</summary>
    private sealed class NoDismiss : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>A readable shipping-app login. The token says what it is.</summary>
    private sealed class FixtureReader : IHostAuthTokenReader
    {
        public HostTokenRead Read() => HostTokenRead.Found(new HostAuthToken(FixtureToken));
    }

    /// <summary>An authority that answered about this account: no pledge. The only input that
    /// produces <see cref="DtrhGateDecision.RefusedNotEntitled"/>, which is the only branch
    /// upstream's <c>ShowDenied</c> is reached from (<c>Services/TierGate.cs:94,126</c>).</summary>
    private sealed class RefusingAuthority : IEntitlementTierSource
    {
        public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
            Task.FromResult(TierLookup.NoEntitlement());
    }
}
