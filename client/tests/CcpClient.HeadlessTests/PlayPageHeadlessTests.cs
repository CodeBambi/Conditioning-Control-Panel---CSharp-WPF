using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Mantra;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The port's flagship route, driven by REAL headless input on the REAL controls from a
/// cold composition-root boot with NO command-line arguments — rail door <c>Play</c> -> the
/// DTRH hero card -> <c>FALL IN</c> / <c>Quick Drop</c> -> the Tier-2 gate.
///
/// <para>The gate under test is the REAL <see cref="HostLoginEntitlement"/> composed by the
/// REAL <see cref="CompositionRoot"/>. What the tests substitute is the capability's own two
/// seams — the platform read and the entitlement authority — which is how the entitlement work designed it to
/// be exercised. Nothing stubs the OUTCOME, nothing stubs the gate, and no test here touches
/// the developer's real <c>%LOCALAPPDATA%/ConditioningControlPanel</c> store: the "unknown"
/// case does not even need a double, because this build's shipped authority
/// (<see cref="UnconfiguredTierSource"/>) produces it on its own.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, arranged
/// bounds, real input routing, hit-test flags. Nothing here claims composited pixels, window
/// activation, z-order, that the DTRH host window presents, or that the shell's minimize really
/// leaves and returns on a desktop — those are presentation-verified and belong to a headed
/// capture.</para>
/// </summary>
public class PlayPageHeadlessTests : HeadlessTest
{
    /// <summary>The only "credential" this file handles. It says what it is.</summary>
    private const string FixtureToken = "SP094-FIXTURE-NOT-A-REAL-TOKEN-8b3d7a";

    /// <summary>A readable shipping-app login, over an authority with a scripted answer. Null
    /// authority = this build's real default (<see cref="UnconfiguredTierSource"/>), which is
    /// what every user actually runs against today.</summary>
    private static HostLoginEntitlement Capability(TierLookup? authorityAnswer) =>
        new(new FixtureReader(), authorityAnswer is null ? null : new FixtureAuthority(authorityAnswer));

    private async Task<(ApplicationHost Host, MainWindow Window, DtrhLaunch Dtrh)> BootAsync(
        TierLookup? authorityAnswer, List<string>? diagnostics = null, IHostAuthTokenReader? reader = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp094-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => reader is null
                ? Capability(authorityAnswer)
                : new HostLoginEntitlement(reader),
            LogSinkFactory = diagnostics is null
                ? () => new DebugLogSink()
                : () => new CapturingLogSink(diagnostics),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        // No demo flags, no drive strings: the product path a user gets from a cold start.
        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return (host!, window, window.Dtrh);
    }

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>Real input on the rail door, then real input on the named card button.</summary>
    private static void PressCardButton(MainWindow window, string buttonName)
    {
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        Click(window, Descendant<Button>(window, buttonName));
    }

    private static async Task WaitForFaultAsync(DtrhLaunch dtrh)
    {
        await TestWait.Until(
            () => dtrh.LastFault is not null,
            "the DTRH launcher to surface the fault a thrown descent produced",
            () => $"gate arrivals={dtrh.GateArrivals}, decision={dtrh.LastDecision}, fault={dtrh.LastFault}");
    }

    private static async Task<DtrhGateDecision> WaitForDecisionAsync(DtrhLaunch dtrh)
    {
        await TestWait.Until(
            () => dtrh.LastDecision is not null,
            "the DTRH gate to reach a decision after a real button press",
            () => $"gate arrivals={dtrh.GateArrivals}, decision={dtrh.LastDecision}");
        return dtrh.LastDecision!;
    }

    [AvaloniaFact]
    public async Task ColdStart_NoArguments_PlayDoorThenFallIn_OpensTheRealSlotPicker_WhenEntitled()
    {
        // THE ROUTE. Two hops, as WPF has it (§3): the rail door navigates
        // (MainWindow.TabNavigation.cs:941), and the hero card's FALL IN is the launcher
        // (PlayTabView.xaml:455 -> MainWindow.Lab.cs:219). The picker is WPF's setup step
        // (MainWindow.Lab.cs:246 -> ChaosSlotPickerWindow.Pick), and this drives the REAL
        // coordinator through the REAL default descent seam — no substitution anywhere.
        var (host, window, dtrh) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));

        PressCardButton(window, "FallInButton");

        var decision = await WaitForDecisionAsync(dtrh);
        var proceed = Assert.IsType<DtrhGateDecision.Proceed>(decision);
        Assert.Equal(EntitlementTier.Lab, proceed.Tier);

        await TestWait.Until(
            () => dtrh.Coordinator.Picker is not null,
            "the real DtrhSlotPickerWindow to open after an entitled FALL IN",
            () => $"picker={dtrh.Coordinator.Picker}, host window={dtrh.Coordinator.HostWindow}");

        var picker = Assert.IsType<DtrhSlotPickerWindow>(dtrh.Coordinator.Picker);
        Assert.Equal(3, picker.CardCount);        // the real three-slot picker, not a stand-in
        Assert.Null(dtrh.Coordinator.HostWindow); // the descent waits on the pick, as WPF does

        // Cancel backs out (WPF: slot == null -> no launch), so no WebView2 host is built.
        picker.Close();
        await TestWait.Until(
            () => dtrh.Coordinator.Picker is null,
            "the picker to release after cancelling",
            () => $"picker={dtrh.Coordinator.Picker}");
        Assert.Null(dtrh.Coordinator.HostWindow);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task NotEntitled_FallIn_RefusesWithWpfsTierMessage_AndOpensNothing()
    {
        // An authority explicitly answered "no pledge" — the ONLY input the entitlement design allows to
        // produce NotEntitled, and the only refusal WPF has (Services/TierGate.cs:128,133).
        var (host, window, dtrh) = await BootAsync(TierLookup.NoEntitlement());

        PressCardButton(window, "FallInButton");

        var decision = await WaitForDecisionAsync(dtrh);
        Assert.IsType<DtrhGateDecision.RefusedNotEntitled>(decision);

        var band = Descendant<Border>(window, "GateBand");
        Assert.True(band.IsVisible);
        Assert.Equal(PlayPage.NotEntitledBandTitle, Descendant<TextBlock>(window, "GateBandTitle").Text);
        Assert.Contains(
            "Down the Rabbit Hole is a Tier 2 perk - upgrade your pledge to unlock it.",
            Descendant<TextBlock>(window, "GateBandText").Text ?? "",
            StringComparison.Ordinal);

        // Nothing opened. Not the picker, not the host window.
        Assert.Null(dtrh.Coordinator.Picker);
        Assert.Null(dtrh.Coordinator.HostWindow);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Unavailable_TierAuthorityAbsent_FallIn_RefusesWithADifferentHonestMessage_AndOpensNothing()
    {
        // THE BRANCH A REAL USER HITS TODAY, and the reason this packet exists. No double is
        // injected for the authority at all: the boot uses this build's shipped
        // UnconfiguredTierSource, so the login is read for real and the tier is unknowable.
        var (host, window, dtrh) = await BootAsync(authorityAnswer: null);

        PressCardButton(window, "FallInButton");

        var decision = await WaitForDecisionAsync(dtrh);
        var unverified = Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityAbsent, unverified.ReasonCode);

        // It refuses (an unknown entitlement may not open paid content) …
        Assert.Null(dtrh.Coordinator.Picker);
        Assert.Null(dtrh.Coordinator.HostWindow);

        // … but everything the user can see says a DIFFERENT thing from "you are not a patron".
        var title = Descendant<TextBlock>(window, "GateBandTitle").Text;
        var text = Descendant<TextBlock>(window, "GateBandText").Text ?? "";
        Assert.True(Descendant<Border>(window, "GateBand").IsVisible);
        Assert.Equal(PlayPage.UnverifiedBandTitle, title);
        Assert.NotEqual(PlayPage.NotEntitledBandTitle, title);
        Assert.DoesNotContain("Tier 2 perk", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upgrade your pledge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not a patron", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not verify your entitlement", text, StringComparison.Ordinal);
        Assert.Contains("Nothing was decided about your account", text, StringComparison.Ordinal);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheGatedCard_TakesTheClick_NothingIsDisabled_AndTheBandNeverSwallowsTheNextPress()
    {
        // WPF's gated card is present, fully readable and STILL CLICKABLE: the lock band is a
        // scrim with IsHitTestVisible="False" so the press passes through and the handler
        // refuses out loud (PlayTabView.xaml:503-512, style :251-258). Greying the button out
        // is the shape that swallows the gesture and tells the user nothing.
        var (host, window, dtrh) = await BootAsync(TierLookup.NoEntitlement());
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        var fallIn = Descendant<Button>(window, "FallInButton");
        var quickDrop = Descendant<Button>(window, "QuickDropButton");
        var band = Descendant<Border>(window, "GateBand");

        // Before any press: both live, no band, and the card is fully readable.
        Assert.True(fallIn.IsEnabled);
        Assert.True(quickDrop.IsEnabled);
        Assert.False(band.IsVisible);
        Assert.Equal("PRIME SUBJECT", Descendant<TextBlock>(window, "TierBadgeText").Text);

        Click(window, fallIn);
        await WaitForDecisionAsync(dtrh);
        Assert.Equal(1, dtrh.GateArrivals);
        Assert.True(band.IsVisible);

        // The refused card is still LIVE — nothing was disabled by the refusal …
        Assert.True(fallIn.IsEnabled);
        Assert.True(quickDrop.IsEnabled);
        // … and the band is the WPF scrim: it cannot be an input target.
        Assert.False(band.IsHitTestVisible);

        // So the next press ARRIVES, through the band, and is refused again.
        Click(window, fallIn);
        await TestWait.Until(
            () => dtrh.GateArrivals == 2,
            "the second FALL IN press to reach the gate through the refusal band",
            () => $"gate arrivals={dtrh.GateArrivals}");
        Assert.True(band.IsVisible);
        Assert.Null(dtrh.Coordinator.Picker);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task QuickDrop_IsGatedToo_AndRefusesWithoutOpeningAnything()
    {
        // WPF gates BOTH entries, with the same call: MainWindow.Lab.cs:228 (FALL IN) and
        // :313 (Quick Start). A second entry point that skipped the gate would hand out the
        // same paid content by a different button.
        var (host, window, dtrh) = await BootAsync(authorityAnswer: null);

        PressCardButton(window, "QuickDropButton");

        var decision = await WaitForDecisionAsync(dtrh);
        Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
        Assert.Equal(1, dtrh.GateArrivals);
        Assert.Null(dtrh.Coordinator.Picker);
        Assert.Null(dtrh.Coordinator.HostWindow);
        Assert.True(Descendant<Border>(window, "GateBand").IsVisible);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task QuickDrop_WhenEntitled_DescendsWithoutThePicker()
    {
        // WPF's Quick Drop skips the picker BY DESIGN and reuses the last-chosen slot
        // (MainWindow.Lab.cs:308,313-323 — "that's the 'quick' part"). The descent seam is
        // substituted HERE and only here, because QuickStartAsync goes straight to a real
        // WebView2 DtrhHostWindow that a headless frame cannot present; the entitled FALL IN
        // test above runs the unsubstituted default and reaches the real picker.
        var (host, window, dtrh) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));
        var descents = new List<DtrhEntry>();
        dtrh.Descend = (entry, coordinator) =>
        {
            Assert.Same(dtrh.Coordinator, coordinator); // the ONE coordinator, not a new one
            descents.Add(entry);
            return Task.CompletedTask;
        };

        PressCardButton(window, "QuickDropButton");

        var decision = await WaitForDecisionAsync(dtrh);
        Assert.IsType<DtrhGateDecision.Proceed>(decision);
        await TestWait.Until(
            () => descents.Count == 1,
            "the entitled Quick Drop to reach the descent",
            () => $"descents=[{string.Join(",", descents)}]");
        Assert.Equal(DtrhEntry.QuickDrop, descents[0]);
        Assert.Null(dtrh.Coordinator.Picker);                    // no picker on this entry
        Assert.False(Descendant<Border>(window, "GateBand").IsVisible); // and no refusal band

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ThePlayDoor_Navigates_AndLaunchesNothingByItself()
    {
        // WPF's load-bearing rule, stated verbatim in its own source: "Navigation tiles still
        // navigate to the ONE existing entry, never launch" (MainWindow.Presets.cs:1036). The
        // door reaching the gate would make the rail a launcher.
        var (host, window, dtrh) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));

        Assert.Equal(ShellRoutes.Studio, window.Router.Current.Id); // the shell opens elsewhere
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        Assert.Equal(ShellRoutes.Play, window.Router.Current.Id);
        Assert.IsType<PlayPage>(window.PageFor(ShellRoutes.Play));
        Assert.Equal(0, dtrh.GateArrivals);   // navigating asked the gate nothing
        Assert.Null(dtrh.LastDecision);
        Assert.Null(dtrh.Coordinator.Picker); // and opened nothing

        // The hero card is really arranged on the mounted page, not a hidden panel.
        Assert.True(Descendant<Button>(window, "FallInButton").Bounds.Width > 0);
        Assert.True(Descendant<TextBlock>(window, "HeroTitle").Bounds.Height > 0);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheHeroCard_WearsTheLiveryWording_AndTheSourceTitle_NeverTheLiteralTier2()
    {
        // "PRIME SUBJECT" is the tier livery's user-facing wording, baked into the badge art
        // (Controls/TierBadge.cs:21); the literal string "TIER 2" appears nowhere in the
        // running app. The title is PlayTabView.xaml:426 emoji-stripped (§9 D8) — §8.5 of the
        // evidence doc read it as "THE RABBIT HOLE" from an occluded capture, corrected by
        // this packet.
        var (host, window, _) = await BootAsync(authorityAnswer: null);
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        var page = window.PageFor(ShellRoutes.Play);
        Assert.Equal("PRIME SUBJECT", Descendant<TextBlock>(page, "TierBadgeText").Text);
        Assert.Equal("DOWN THE RABBIT HOLE", Descendant<TextBlock>(page, "HeroTitle").Text);

        var everyString = page.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.NotEmpty(everyString);
        Assert.DoesNotContain(everyString, s => s.Contains("TIER 2", StringComparison.Ordinal));
        // The blurb is carried verbatim from PlayTabView.xaml:435.
        Assert.Contains(everyString, s => s.StartsWith("it's right there.", StringComparison.Ordinal));

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRefusalMessage_SitsOnItsOwnOpaquePlate_AndTheScrimKeepsWpfsAlpha()
    {
        // REGRESSION GUARD for a defect a headed capture found and no headless frame could
        // (§10 D23): the message used to sit directly on the 66%-alpha scrim, so the card's own
        // title and blurb composited THROUGH it and the refusal was unreadable. The words were
        // right; the layering was not.
        //
        // WPF never has this problem because its scrim carries a glyph plus ONE short no-wrap
        // line and sends the prose to a toast with its own surface (PlayTabView.xaml:270-273).
        // The port has no toast, so the prose gets a plate. Both halves are pinned here,
        // because either one alone can be "fixed" in a way that loses the other:
        // an opaque scrim would be legible and would destroy the "seeing what you are missing"
        // quality that WPF's alpha exists for (:247-248).
        var (host, window, dtrh) = await BootAsync(authorityAnswer: null);

        PressCardButton(window, "FallInButton");
        await WaitForDecisionAsync(dtrh);

        var band = Descendant<Border>(window, "GateBand");
        var plate = Descendant<Border>(window, "GateBandPlate");
        var text = Descendant<TextBlock>(window, "GateBandText");

        // The scrim is still WPF's scrim, alpha and all — the fix did not go through it.
        Assert.Equal(Color.Parse("#A8120A1E"), ((ISolidColorBrush)band.Background!).Color);
        Assert.Equal(0xA8, ((ISolidColorBrush)band.Background!).Color.A);

        // The message's own ground is OPAQUE, which is what makes the words readable.
        Assert.Equal(0xFF, ((ISolidColorBrush)plate.Background!).Color.A);

        // …and the message really is ON it, not merely near it in the tree.
        Assert.Contains(text, plate.GetVisualDescendants());
        Assert.Contains(plate, band.GetVisualDescendants());
        Assert.True(plate.Bounds.Width > 0 && plate.Bounds.Height > 0); // arranged for real

        // The plate is narrower than the band, so the card still reads around it — the whole
        // point of keeping the scrim translucent.
        Assert.True(plate.Bounds.Width < band.Bounds.Width,
            $"plate {plate.Bounds.Width} must be narrower than band {band.Bounds.Width}");

        // And none of this made the band take input: the press must still arrive (trap 2).
        Assert.False(band.IsHitTestVisible);
        Assert.True(Descendant<Button>(window, "FallInButton").IsEnabled);

        // The wording is unchanged — the fix was layering, not shortening.
        Assert.Contains("That is a gap in the port, not a finding about your account",
            text.Text ?? "", StringComparison.Ordinal);

        await host.ShutdownAsync();
    }

    // ---------- The failure a user can see, and the fallback nothing had run ----------

    [AvaloniaFact]
    public async Task WhenTheDescentThrows_TheUserIsTOLD_InWpfsOwnWords_AndTheDetailIsNotDropped()
    {
        // THE DEFECT THIS CLOSES. WPF wraps its whole handler and shows a warning MessageBox
        // reading "Couldn't start Down the Rabbit Hole:" plus ex.Message
        // (MainWindow/MainWindow.Lab.cs:266-271). The port caught only around ResolveAsync, and
        // PlayPage fires `_ = dtrh.FallInAsync()` — so a throw from the descent became an
        // UNOBSERVED task exception: raked up by the panic hook at some later GC (CcpClient.Desktop/Program.cs:313)
        // and never shown to anyone. Entitled, so the gate really opens and the throw is
        // unambiguously PAST it.
        var diagnostics = new List<string>();
        var (host, window, dtrh) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab), diagnostics);
        dtrh.Descend = async (_, _) =>
        {
            // Thrown from a CONTINUATION, not synchronously: that is the exact shape that used to
            // become an unobserved task exception, and a catch that only covered the synchronous
            // call would still be green.
            await Task.Yield();
            throw new InvalidOperationException("the WebView2 host refused to construct");
        };

        PressCardButton(window, "FallInButton");

        // The gate still said yes — a fault must not be back-written into the verdict.
        var decision = await WaitForDecisionAsync(dtrh);
        Assert.IsType<DtrhGateDecision.Proceed>(decision);
        await WaitForFaultAsync(dtrh);

        // The user sees it, on the page, in WPF's words.
        var band = Descendant<Border>(window, "FaultBand");
        var text = Descendant<TextBlock>(window, "FaultBandText").Text ?? "";
        Assert.True(band.IsVisible);
        Assert.Equal(PlayPage.FaultBandTitleText, Descendant<TextBlock>(window, "FaultBandTitle").Text);
        Assert.Equal("Couldn't start Down the Rabbit Hole", PlayPage.FaultBandTitleText);
        Assert.StartsWith("Couldn't start Down the Rabbit Hole:", text, StringComparison.Ordinal);

        // THE TRAP: not swallowed. The type AND the message reach the user, the exception object
        // is still on the launcher, and the diagnostic carries both as well.
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("the WebView2 host refused to construct", text, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(dtrh.LastFault);
        Assert.Contains(
            diagnostics.ToArray(),
            line => line.Contains("FAULTED", StringComparison.Ordinal)
                && line.Contains("InvalidOperationException", StringComparison.Ordinal)
                && line.Contains("the WebView2 host refused to construct", StringComparison.Ordinal));

        // Nothing opened, and the card is still live so the user can try again — WPF's dialog is
        // dismissed and leaves a working card, which is the outcome being ported.
        Assert.Null(dtrh.Coordinator.Picker);
        Assert.Null(dtrh.Coordinator.HostWindow);
        Assert.True(Descendant<Button>(window, "FallInButton").IsEnabled);
        Assert.False(band.IsHitTestVisible);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AFailureLooksNothingLikeARefusal_AndTheTwoAreNeverUpTogether()
    {
        // THE SECOND TRAP, named at authoring. The band on this card already means "we could not
        // determine your entitlement". If a failure rendered the same way, the user would learn
        // that a broken app and an unknown subscription are the same event. Five independent
        // differences are pinned here — element, livery, headline, wording, and mutual exclusion —
        // because any one of them alone can be "tidied" away by a later edit.
        var authority = new MutableAuthority(TierLookup.Entitled(EntitlementTier.Lab));
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp097-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => new HostLoginEntitlement(new FixtureReader(), authority),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        Assert.IsType<StartupOutcome.Success>(await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None));
        Track(host!);
        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        var dtrh = window.Dtrh;
        dtrh.Descend = (_, _) => throw new TimeoutException("the descent never answered");

        // Press 1: entitled, and the descent throws.
        PressCardButton(window, "FallInButton");
        await WaitForFaultAsync(dtrh);
        window.UpdateLayout();

        var gateBand = Descendant<Border>(window, "GateBand");
        var faultBand = Descendant<Border>(window, "FaultBand");
        var faultPlate = Descendant<Border>(window, "FaultBandPlate");
        var faultText = Descendant<TextBlock>(window, "FaultBandText");

        // (1) A different ELEMENT — not the refusal band recoloured.
        Assert.NotSame(gateBand, faultBand);
        Assert.True(faultBand.IsVisible);
        Assert.False(gateBand.IsVisible);

        // (2) A different LIVERY. Amber, carrying MessageBoxImage.Warning's severity across the
        // idiom change; never the tier-2 violet the refusal wears.
        var refusalRim = Color.Parse("#FFB47BFF");
        Assert.Equal(Color.Parse("#FFF0A02E"), ((ISolidColorBrush)faultBand.BorderBrush!).Color);
        Assert.NotEqual(refusalRim, ((ISolidColorBrush)faultBand.BorderBrush!).Color);
        Assert.Equal(refusalRim, ((ISolidColorBrush)gateBand.BorderBrush!).Color);
        Assert.NotEqual(
            ((ISolidColorBrush)gateBand.Background!).Color, ((ISolidColorBrush)faultBand.Background!).Color);
        Assert.NotEqual(
            ((ISolidColorBrush)Descendant<Border>(window, "GateBandPlate").Background!).Color,
            ((ISolidColorBrush)faultPlate.Background!).Color);
        // …and the §10 D23 lesson still holds on the new plate: prose needs an OPAQUE ground,
        // while the scrim stays translucent so the card underneath still reads.
        Assert.Equal(0xFF, ((ISolidColorBrush)faultPlate.Background!).Color.A);
        Assert.True(((ISolidColorBrush)faultBand.Background!).Color.A < 0xFF);
        Assert.Contains(faultText, faultPlate.GetVisualDescendants());
        Assert.True(faultPlate.Bounds.Width > 0 && faultPlate.Bounds.Height > 0);
        Assert.True(faultPlate.Bounds.Width < faultBand.Bounds.Width);

        // (3) A different HEADLINE, and (4) different WORDS: no refusal vocabulary anywhere.
        var text = faultText.Text ?? "";
        Assert.NotEqual(PlayPage.NotEntitledBandTitle, PlayPage.FaultBandTitleText);
        Assert.NotEqual(PlayPage.UnverifiedBandTitle, PlayPage.FaultBandTitleText);
        Assert.DoesNotContain("Tier 2 perk", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upgrade your pledge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("could not verify your entitlement", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a decision about your account", text, StringComparison.Ordinal);

        // (5) MUTUAL EXCLUSION, driven both ways. A later refused press replaces the fault plate
        // rather than stacking beside it…
        authority.Answer = TierLookup.NoEntitlement();
        PressCardButton(window, "FallInButton");
        await TestWait.Until(
            () => dtrh.LastDecision is DtrhGateDecision.RefusedNotEntitled,
            "the second press to be refused now the authority answers no pledge",
            () => $"decision={dtrh.LastDecision}");
        Assert.True(gateBand.IsVisible);
        Assert.False(faultBand.IsVisible);
        Assert.Equal(PlayPage.NotEntitledBandTitle, Descendant<TextBlock>(window, "GateBandTitle").Text);

        // …and a later fault replaces the refusal.
        authority.Answer = TierLookup.Entitled(EntitlementTier.Lab);
        PressCardButton(window, "FallInButton");
        await TestWait.Until(
            () => dtrh.GateArrivals == 3 && Descendant<Border>(window, "FaultBand").IsVisible,
            "the third press to fault and replace the refusal band",
            () => $"arrivals={dtrh.GateArrivals}, fault={dtrh.LastFault}, decision={dtrh.LastDecision}");
        Assert.False(gateBand.IsVisible);

        window.Close();
        await host!.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task WhenResolvingTheEntitlementTHROWS_ItStaysARefusal_AndLandsTheTierAuthorityFaultFallback()
    {
        // THE PATH NOTHING HAD EVER EXECUTED: DtrhLaunch's catch around ResolveAsync, which
        // synthesises Unavailable(tier-authority-fault). HostLoginEntitlement is sealed and turns
        // every DETERMINABLE failure into an outcome rather than a throw, so the only way in is a
        // read seam that throws — which is a real shape (a DPAPI or filesystem call can throw
        // outside the reader's own handling).
        var diagnostics = new List<string>();
        var (host, window, dtrh) = await BootAsync(
            authorityAnswer: null, diagnostics, reader: new ThrowingReader());

        PressCardButton(window, "FallInButton");

        var decision = await WaitForDecisionAsync(dtrh);
        var unverified = Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, unverified.ReasonCode);

        // It stays a REFUSAL, not the fault plate, and that is the point: the question asked was
        // "what is this account entitled to" and the honest answer is that it could not be
        // determined. Rendering it as "the app broke" would be as wrong as rendering it as
        // "you are not a patron" (the §10 D21 rule, third direction).
        Assert.True(Descendant<Border>(window, "GateBand").IsVisible);
        Assert.False(Descendant<Border>(window, "FaultBand").IsVisible);
        Assert.Null(dtrh.LastFault);
        Assert.Equal(PlayPage.UnverifiedBandTitle, Descendant<TextBlock>(window, "GateBandTitle").Text);
        Assert.Contains(
            "The entitlement lookup itself failed.",
            Descendant<TextBlock>(window, "GateBandText").Text ?? "",
            StringComparison.Ordinal);

        // And the log names the reason code and the exception TYPE — but never the message, which
        // on this one path can carry a path or a bearer (DtrhLaunch's own comment).
        Assert.Contains(
            diagnostics.ToArray(),
            line => line.Contains("tier-authority-fault", StringComparison.Ordinal)
                && line.Contains("refused(unverified:tier-authority-fault)", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics.ToArray(),
            line => line.Contains(ThrowingReader.SecretShapedMessage, StringComparison.Ordinal));

        Assert.Null(dtrh.Coordinator.Picker);
        Assert.Null(dtrh.Coordinator.HostWindow);

        await host.ShutdownAsync();
    }

    // ================================ THE MANTRA DOOR ================================
    //
    // Upstream's Mantras card was the ONLY caller of its typed mantra window, and the 2026-08-12
    // relayout took the card off this page: MainWindow/MainWindow.PlayTab.cs:262 says in capitals
    // that MantraWindow has "NO CALLER", and the relayout's own commit message (a9859e7b6) records
    // the consequence as "MantraWindow entry point orphaned - re-home pending owner call". The
    // removal was de-duplication - "only the duplicate Play-page card is gone"
    // (Views/Tabs/PlayTabView.xaml:20-24) - and that premise is false for this one card. These
    // facts are the restored door, driven the way a user reaches it.

    /// <summary>The launch's ledger, pointed at a throwaway directory. NEVER the developer's own
    /// store: <see cref="MantraLaunch.DataDirectory"/>'s product default is the install's data root
    /// through the data-root choke point, and a test that let it stand would bank XP into the
    /// machine's real progression.json.</summary>
    private static string NewLedgerDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-mantra-door-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The Play page has no viewport of its own, so a card below the fold is reached the
    /// way a user reaches it — by scrolling. Deterministic and synchronous: no wait, no wheel
    /// count, no timing.</summary>
    private static void ScrollTo(MainWindow window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task ColdStart_PlayDoorThenBegin_OpensTheRealMantraWindow_AtThePickersOwnCount()
    {
        // THE DOOR, end to end, from a cold composition-root boot with no arguments: rail door
        // Play -> the Mantras card -> Begin -> the real MantraWindow, playing a real started
        // session. Upstream's two-step (StartSession(n) then the window,
        // MainWindow/MainWindow.PlayTab.cs:305-306) is the launcher's here, and the count comes
        // from the card's picker exactly as upstream's did (PlayTabView.Cards.cs:107-117 at
        // a9859e7b6^).
        var (host, window, _) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));
        window.Mantra.DataDirectory = NewLedgerDir();

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        // Upstream's picker opens on SelectedIndex="1" — the second of 10/25/50/100 — and this
        // card carries the same four values in the same order.
        var picker = Descendant<ComboBox>(window, "MantraRepsPicker");
        Assert.Equal(1, picker.SelectedIndex);
        Assert.Equal(
            new[] { "10", "25", "50", "100" },
            picker.Items.OfType<ComboBoxItem>().Select(i => i.Content as string).ToArray());

        var begin = Descendant<Button>(window, "MantraBeginButton");
        ScrollTo(window, begin);
        Click(window, begin);

        var game = window.Mantra.Window;
        Assert.NotNull(game);
        Assert.Equal(1, window.Mantra.LaunchCount);
        Assert.Null(window.Mantra.LastFault);

        // The run the window is playing is REAL and already started — the footgun upstream's
        // rescue note exists to describe (:272-275, "it has always assumed a session was already
        // running").
        Assert.Equal(PlayPage.DefaultCardReps, game.Session.TargetCount);
        Assert.Equal(25, game.Session.TargetCount);
        Assert.True(game.Session.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(game.Session.CurrentMantra));

        // Nothing broke, so the card says nothing.
        Assert.False(Descendant<TextBlock>(window, "MantraFaultText").IsVisible);

        game.Close();
        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ThePickedCount_IS_THE_RUN_NotAFixedDefault()
    {
        // The picker is a dial the user turns, not decoration: 100 picked is 100 asked for.
        // Upstream's four values are inside MantraService's own 1..100 clamp by construction
        // (Services/MantraService.cs:28), so the count reaches the run unmodified.
        var (host, window, _) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));
        window.Mantra.DataDirectory = NewLedgerDir();

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        var picker = Descendant<ComboBox>(window, "MantraRepsPicker");
        picker.SelectedIndex = 3;                       // the fourth value: 100
        window.UpdateLayout();

        var begin = Descendant<Button>(window, "MantraBeginButton");
        ScrollTo(window, begin);
        Click(window, begin);

        var game = window.Mantra.Window;
        Assert.NotNull(game);
        Assert.Equal(100, game.Session.TargetCount);
        Assert.NotEqual(PlayPage.DefaultCardReps, game.Session.TargetCount);

        game.Close();
        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task WhenTheMantraLaunchTHROWS_TheCardSaysSo_InWpfsOwnWords_AndALaterSuccessTakesItBack()
    {
        // Upstream answers a thrown StartMantraSession with a MessageBox carrying its own
        // headline and the exception's message (MainWindow/MainWindow.PlayTab.cs:308-313). The
        // port has no dialog service, so the card carries the same disclosure in its own line —
        // and the SHOW seam is what is made to throw, because that is exactly the half of
        // upstream's try block that opens a window.
        var (host, window, _) = await BootAsync(TierLookup.Entitled(EntitlementTier.Lab));
        window.Mantra.DataDirectory = NewLedgerDir();
        window.Mantra.Show = (_, _) => throw new InvalidOperationException(ShowFailureMessage);

        Click(window, window.FindControl<RadioButton>("DoorPlay")!);
        var begin = Descendant<Button>(window, "MantraBeginButton");
        ScrollTo(window, begin);
        Click(window, begin);

        // Nothing opened, and the user is TOLD rather than left with a button that did nothing.
        Assert.Null(window.Mantra.Window);
        var line = Descendant<TextBlock>(window, "MantraFaultText");
        Assert.True(line.IsVisible);
        var text = line.Text ?? "";
        Assert.StartsWith(PlayPage.MantraFaultHeadline + ":", text, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.Contains(ShowFailureMessage, text, StringComparison.Ordinal);   // the detail is not dropped
        Assert.Contains(LaunchFaultText.NotADecisionLine, text, StringComparison.Ordinal);

        // A LATER success takes the plate back down: leaving it up would tell the user the app is
        // broken while she is typing into the window it just opened.
        window.Mantra.Show = static (game, owner) => game.Show(owner);
        Click(window, begin);

        var opened = window.Mantra.Window;
        Assert.NotNull(opened);
        Assert.False(line.IsVisible);
        Assert.Equal(string.Empty, line.Text);

        opened.Close();
        await host.ShutdownAsync();
    }

    /// <summary>Shaped like an exception message a real launch failure would carry, and unlike
    /// anything else on this page.</summary>
    private const string ShowFailureMessage = "the mantra window refused to present (fixture)";

    /// <summary>A readable shipping-app login. Fresh per call, because the capability disposes
    /// the read on every path — a shared instance would be a double-disposed token.</summary>
    private sealed class FixtureReader : IHostAuthTokenReader
    {
        public HostTokenRead Read() => HostTokenRead.Found(new HostAuthToken(FixtureToken));
    }

    /// <summary>A read seam that THROWS rather than reporting a typed failure — the only input
    /// that makes the sealed capability's ResolveAsync throw, and therefore the only way to reach
    /// DtrhLaunch's tier-authority-fault fallback.</summary>
    private sealed class ThrowingReader : IHostAuthTokenReader
    {
        /// <summary>Shaped like the thing the type-only logging rule exists to keep out of a log.</summary>
        public const string SecretShapedMessage = @"C:\Users\someone\AppData\Local\ConditioningControlPanel\auth.bin";

        public HostTokenRead Read() => throw new UnauthorizedAccessException(SecretShapedMessage);
    }

    /// <summary>An authority with a scripted answer. It never sees a real credential.</summary>
    private sealed class FixtureAuthority(TierLookup answer) : IEntitlementTierSource
    {
        public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    /// <summary>An authority whose answer can change between presses — the gate resolves per press
    /// precisely so it can (DtrhLaunch's class remarks), and mutual exclusion of the two bands is
    /// only checkable across presses that decide differently.</summary>
    private sealed class MutableAuthority(TierLookup answer) : IEntitlementTierSource
    {
        public TierLookup Answer { get; set; } = answer;

        public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
            Task.FromResult(Answer);
    }

    /// <summary>The host's diagnostic sink, captured. The port's log is half of "not swallowed".</summary>
    private sealed class CapturingLogSink(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
