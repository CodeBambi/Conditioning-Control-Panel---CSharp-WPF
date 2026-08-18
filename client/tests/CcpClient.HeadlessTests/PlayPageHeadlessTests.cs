using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-094: the port's flagship route, driven by REAL headless input on the REAL controls from a
/// cold composition-root boot with NO command-line arguments — rail door <c>Play</c> -> the
/// DTRH hero card -> <c>FALL IN</c> / <c>Quick Drop</c> -> the Tier-2 gate.
///
/// <para>The gate under test is the REAL <see cref="HostLoginEntitlement"/> composed by the
/// REAL <see cref="CompositionRoot"/>. What the tests substitute is the capability's own two
/// seams — the platform read and the entitlement authority — which is how SP-092 designed it to
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
public class PlayPageHeadlessTests
{
    /// <summary>The only "credential" this file handles. It says what it is.</summary>
    private const string FixtureToken = "SP094-FIXTURE-NOT-A-REAL-TOKEN-8b3d7a";

    /// <summary>A readable shipping-app login, over an authority with a scripted answer. Null
    /// authority = this build's real default (<see cref="UnconfiguredTierSource"/>), which is
    /// what every user actually runs against today.</summary>
    private static HostLoginEntitlement Capability(TierLookup? authorityAnswer) =>
        new(new FixtureReader(), authorityAnswer is null ? null : new FixtureAuthority(authorityAnswer));

    private static async Task<(ApplicationHost Host, MainWindow Window, DtrhLaunch Dtrh)> BootAsync(
        TierLookup? authorityAnswer)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp094-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            EntitlementFactory = _ => Capability(authorityAnswer),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

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
        // An authority explicitly answered "no pledge" — the ONLY input SP-092 allows to
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

    /// <summary>A readable shipping-app login. Fresh per call, because the capability disposes
    /// the read on every path — a shared instance would be a double-disposed token.</summary>
    private sealed class FixtureReader : IHostAuthTokenReader
    {
        public HostTokenRead Read() => HostTokenRead.Found(new HostAuthToken(FixtureToken));
    }

    /// <summary>An authority with a scripted answer. It never sees a real credential.</summary>
    private sealed class FixtureAuthority(TierLookup answer) : IEntitlementTierSource
    {
        public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }
}
