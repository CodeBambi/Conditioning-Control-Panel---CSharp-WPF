using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-095: the Graded Intake route, driven by REAL headless input on the REAL controls from a
/// cold composition-root boot with NO command-line arguments — rail door <c>Graded Intake</c> ->
/// the page's <c>Begin Intake</c> button -> the weekly-pass gate -> the ONE
/// <see cref="IntakeLaunchCoordinator"/>.
///
/// <para>WPF's route is the rail sub-entry <c>BtnNavGradedIntake</c>
/// (<c>MainWindow/MainWindow.xaml:811-812</c>) -> <c>ShowTab("gradedintake")</c>
/// (<c>MainWindow.TabNavigation.cs:947</c>) -> the page's one launcher <c>BtnStartIntake</c>
/// (<c>Views/Tabs/GradedIntakeTabView.xaml:152</c>) -> the pass check
/// (<c>MainWindow.Lab.cs:119-146</c>) -> <c>IntakeHostService.Launch</c> (<c>:159</c>).</para>
///
/// <para><b>What is substituted, and what is not.</b> Two seams, both the product's own and both
/// the shape SP-054/SP-092 built for exactly this: the entitlement source
/// (<see cref="IntakePassService.IIntakeEntitlementSource"/>) and the data root. Nothing stubs
/// the DECISION — <see cref="IntakePassGate"/> is a pure function over whatever the seam reports,
/// and the real <see cref="IntakePassService"/> evaluates it against real stores. The
/// undeterminable case needs no double at all: it is what this build's shipped default produces
/// on its own, exactly as <c>UnconfiguredTierSource</c> is for DTRH. The one presentation seam is
/// <see cref="IntakeLaunch.Open"/>, because the real host window binds a loopback origin and an
/// embedded browser surface no headless frame can present.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, arranged bounds,
/// real input routing, hit-test flags, style-resolved brushes. Nothing here claims composited
/// pixels, window activation, that the intake host window presents, or that a run boots.</para>
/// </summary>
public class IntakePageHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window, List<IntakeLaunchCoordinator> Opened)>
        BootAsync(IntakePassService.IIntakeEntitlementSource? entitlement = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp095-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        // No demo flags, no drive strings: the product path a user gets from a cold start.
        var window = new MainWindow(host!);
        var opened = new List<IntakeLaunchCoordinator>();
        window.Intake.Open = opened.Add;
        window.Intake.EntitlementSource = entitlement;   // null = this build's real default
        window.Intake.DataDirectory = Path.Combine(dir, "intake");
        window.Show();
        window.UpdateLayout();
        return (host!, window, opened);
    }

    private static RadioButton Door(MainWindow window, string name) =>
        window.FindControl<RadioButton>(name) ?? throw new InvalidOperationException($"no rail door '{name}'");

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static bool HasDescendant(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task ColdStart_NoArguments_IntakeDoorThenBeginIntake_RefusesUndeterminable_AndOpensNothing()
    {
        // THE ROUTE, and the branch every real user of this build reaches. Two clicks from a cold
        // start replace what used to need --intake-demo — and the second click is REFUSED, because
        // unlimited retakes are the patron privilege upstream
        // (Services/Progression/IntakePassService.cs:13,26-29) and this build has no account to
        // read. No double is injected: the shipped NoEntitlementSource produces this on its own.
        var (host, window, opened) = await BootAsync();

        // Leave the default page first, so the door click below is a real navigation.
        Assert.Equal(ShellRoutes.Studio, window.Router.Current.Id);
        Assert.False(HasDescendant(window, "BeginIntakeButton"));

        Click(window, Door(window, "DoorIntake"));
        Assert.Equal(ShellRoutes.Intake, window.Router.Current.Id);
        Assert.IsType<IntakePage>(window.PageFor(ShellRoutes.Intake));
        Assert.False(Descendant<Border>(window, "PassGate").IsVisible);  // no band before a press

        Click(window, Descendant<Button>(window, "BeginIntakeButton"));

        Assert.Equal(1, window.Intake.LaunchCount);                       // the press arrived
        var decision = Assert.IsType<IntakePassDecision.RefusedUndeterminable>(window.Intake.LastDecision);
        Assert.Equal(IntakePassService.IntakePassReason.AvailableNoEntitlementProvider, decision.Reason);

        // Nothing opened. Not a coordinator launch, not a host window.
        Assert.Empty(opened);
        Assert.Null(window.Intake.Coordinator.HostWindow);

        // And what the user reads says the port could not tell — never that they used their run.
        var text = Descendant<TextBlock>(window, "PassGateText").Text ?? "";
        Assert.True(Descendant<Border>(window, "PassGate").IsVisible);
        Assert.Equal(IntakePage.UndeterminableBandTitle, Descendant<TextBlock>(window, "PassGateTitle").Text);
        Assert.NotEqual(IntakePage.SpentBandTitle, Descendant<TextBlock>(window, "PassGateTitle").Text);
        Assert.Contains("could not determine your Graded Intake pass", text, StringComparison.Ordinal);
        Assert.Contains("nothing was decided about you", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("You've taken your free Graded Intake", text, StringComparison.Ordinal);
        Assert.DoesNotContain("next pass unlocks", text, StringComparison.OrdinalIgnoreCase);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task WhenAnAuthoritySaysPatron_TheSameTwoClicksReachTheOneCoordinator()
    {
        // The grant branch, through the SP-054 entitlement seam rather than a stubbed decision.
        // WPF: Premium is "unlimited runs, no week, no door" (:13), and CanStartIntake is
        // Premium || Available (:139-146). Without this fact the door could be permanently dead
        // and every other test here would still be green.
        var (host, window, opened) = await BootAsync(new PatronSource());

        Click(window, Door(window, "DoorIntake"));
        var button = Descendant<Button>(window, "BeginIntakeButton");
        Click(window, button);

        var proceed = Assert.IsType<IntakePassDecision.Proceed>(window.Intake.LastDecision);
        Assert.Equal(IntakePassService.IntakePassState.Premium, proceed.State);
        Assert.Same(window.Intake.Coordinator, Assert.Single(opened));
        Assert.False(Descendant<Border>(window, "PassGate").IsVisible);   // and no refusal band

        // A second press reaches the SAME coordinator — never a second construction site, which
        // is exactly what --intake-demo used to be (WPF's own rule: "never re-launch a live run",
        // MainWindow.Lab.cs:112-117, and the coordinator owns the refocus).
        Click(window, button);
        Assert.Equal(2, window.Intake.LaunchCount);
        Assert.Equal(2, opened.Count);
        Assert.Same(opened[0], opened[1]);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRefusedPage_TakesTheClick_NothingIsDisabled_AndTheBandNeverSwallowsTheNextPress()
    {
        // WPF's launch button is COVERED by the gate overlay, never greyed, so a refused press
        // still arrives and is answered out loud. Two port-specific facts ride here:
        //   * DIVERGENCE §11 D34 — WPF's intake scrim is IsHitTestVisible="True" and disables the
        //     content behind it (GradedIntakeTabView.xaml:191-192). The port's band appears only
        //     AFTER a press, so a curtain would trap the user on a refusal that expires at Monday
        //     00:00. Hit-test transparent, so the next press re-asks the gate.
        //   * The §10 D23 layering lesson, found by a headed capture on the DTRH band: prose on a
        //     translucent scrim composites through the card and cannot be read, so it gets an
        //     opaque plate. Both alphas are pinned, because either can be "fixed" into the other.
        var (host, window, opened) = await BootAsync();
        Click(window, Door(window, "DoorIntake"));

        var button = Descendant<Button>(window, "BeginIntakeButton");
        Assert.True(button.IsEnabled);

        Click(window, button);
        var band = Descendant<Border>(window, "PassGate");
        var plate = Descendant<Border>(window, "PassGatePlate");
        var text = Descendant<TextBlock>(window, "PassGateText");

        Assert.True(band.IsVisible);
        Assert.True(button.IsEnabled);          // the refusal disabled nothing
        Assert.False(band.IsHitTestVisible);    // and cannot be an input target

        // WPF's own scrim colour and radius (GradedIntakeTabView.xaml:192), kept rather than
        // unified with the DTRH band's #A8120A1E.
        Assert.Equal(Color.Parse("#CC0A0A14"), ((ISolidColorBrush)band.Background!).Color);
        Assert.Equal(0xCC, ((ISolidColorBrush)band.Background!).Color.A);
        // The message's own ground is OPAQUE — what makes the words readable.
        Assert.Equal(0xFF, ((ISolidColorBrush)plate.Background!).Color.A);
        Assert.Contains(text, plate.GetVisualDescendants());
        Assert.Contains(plate, band.GetVisualDescendants());
        Assert.True(plate.Bounds.Width > 0 && plate.Bounds.Height > 0);
        Assert.True(plate.Bounds.Width < band.Bounds.Width,
            $"plate {plate.Bounds.Width} must be narrower than band {band.Bounds.Width}");

        // So the next press ARRIVES, through the band, and is refused again.
        Click(window, button);
        Assert.Equal(2, window.Intake.LaunchCount);
        Assert.IsType<IntakePassDecision.RefusedUndeterminable>(window.Intake.LastDecision);
        Assert.Empty(opened);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheIntakeDoor_Navigates_AndAsksTheGateNothing()
    {
        // The rail must never be a launcher, and it must never adjudicate either. WPF's rail entry
        // opens a TAB; the pass is asked about by the button on that tab and nowhere else
        // (the one-entry rule, MainWindow.Presets.cs:1007; WPF's page comment calls BtnStartIntake
        // "the page's primary (and only visible) action", GradedIntakeTabView.xaml:151).
        var (host, window, opened) = await BootAsync();

        Click(window, Door(window, "DoorIntake"));
        Click(window, Door(window, "DoorPlay"));
        Click(window, Door(window, "DoorIntake"));

        Assert.Empty(opened);
        Assert.Equal(0, window.Intake.LaunchCount);
        Assert.Null(window.Intake.LastDecision);                          // the gate was never asked
        Assert.False(Descendant<Border>(window, "PassGate").IsVisible);

        // …and the page is really arranged, not a hidden panel behind a door that goes nowhere.
        var button = Descendant<Button>(window, "BeginIntakeButton");
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ThePage_CarriesWpfsOwnWords_AndNoneOfTheControlsItDoesNotHave()
    {
        // The page's strings are WPF's, not invented copy: title GradedIntakeTabView.xaml:60,
        // sub-line :67-68, section head :125, blurb :127, button :152 emoji-stripped (§9 D8).
        var (host, window, _) = await BootAsync();
        Click(window, Door(window, "DoorIntake"));

        var page = window.PageFor(ShellRoutes.Intake);
        var strings = page.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.NotEmpty(strings);

        Assert.Contains("Graded Intake", strings);
        Assert.Contains("Begin a run", strings);
        Assert.Contains(strings, s => s.StartsWith("A banded descent that reads how you answer", StringComparison.Ordinal));
        Assert.Contains(strings, s => s.StartsWith("Each intake runs in its own window.", StringComparison.Ordinal));
        Assert.Equal("Begin Intake", Descendant<Button>(page, "BeginIntakeButton").Content as string);

        // The emoji really is stripped — WPF's XAML content is "✨ Begin Intake" (:152).
        Assert.DoesNotContain(strings, s => s.Contains('✨', StringComparison.Ordinal));

        // None of WPF's absent controls was rendered dead (the §9 D7 rule): no pass banner, no
        // past-runs list, and above all no unlock CTA with nowhere to go (the §10 D17 rule).
        Assert.DoesNotContain(strings, s => s.Contains("Your weekly pass is ready", StringComparison.Ordinal));
        Assert.DoesNotContain(strings, s => s.Contains("Unlock unlimited retakes", StringComparison.Ordinal));
        // Exactly one button on the page: WPF's gate CTA and its hidden classic-quiz launcher
        // have no port analogue, and a second button here would be the dead control §10 D17 bans.
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
            b => b.Name != "BeginIntakeButton");
        Assert.Single(page.GetVisualDescendants().OfType<Button>());

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheIntakeDoorSitsAfterPlay_AndTheRailStillPublishesAProbePerDoor()
    {
        // Rail ORDER is WPF's: "gradedintake" is an entry inside the PLAY door's list
        // (MainWindow.TabNavigation.cs:600-601), so the port's door sits after Play; System is the
        // port's own door (§9 D2) and stays last.
        //
        // The second half is what keeps the HEADED harness honest. capture.ps1:136-151 derives the
        // rail's door set from UIA names of the form "<id> door" and demands a layout-probe line
        // per derived door; a new door whose AutomationProperties.Name did not follow that shape
        // would be invisible to it, exactly the blindness SP-094 hit with a hard-coded list. This
        // asserts the shape the harness reads, headlessly, so the harness cannot silently stop
        // checking this door.
        var (host, window, _) = await BootAsync();

        var ids = ShellRoutes.Declared.Select(r => r.Id).ToArray();
        Assert.Equal(
            new[] { ShellRoutes.Studio, ShellRoutes.Companion, ShellRoutes.Play, ShellRoutes.Intake, ShellRoutes.System },
            ids);

        var door = Door(window, "DoorIntake");
        Assert.Equal("Intake door", Avalonia.Automation.AutomationProperties.GetName(door));

        // The probe text the harness parses, published for every declared door including this one.
        var probe = Descendant<TextBlock>(window, "LayoutProbeText").Text ?? "";
        foreach (var id in ids)
        {
            Assert.Contains($"layout-probe: door {id} ", probe, StringComparison.Ordinal);
        }

        await host.ShutdownAsync();
    }

    /// <summary>An authority that reports a patron. It is the SP-054 seam, not a stubbed
    /// decision: the real pass service still evaluates, and the real gate still decides.</summary>
    private sealed class PatronSource : IntakePassService.IIntakeEntitlementSource
    {
        public bool IsPremium => true;

        public bool? IsLoggedIn => true;

#pragma warning disable CS0067 // never raised — this fixture's tier does not change mid-test
        public event Action? TierChanged;
#pragma warning restore CS0067
    }
}
