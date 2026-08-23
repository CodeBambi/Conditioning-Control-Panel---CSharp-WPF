using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The Haptics rack row, driven by REAL headless input on the REAL controls, from a cold
/// composition-root boot with no command-line arguments and NO substituted seams at all.
///
/// <para><b>Nothing is injected here, and that is deliberate.</b> The other headless suites replace
/// a clock or a pool so nothing waits; this row has neither. What it has instead is the product's
/// own refusing sink and the product's own unconfigured entitlement authority, which is exactly the
/// combination every real user gets — so these facts are about the build that ships rather than
/// about a build assembled to make them pass.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, style-resolved
/// classes, real input routing. Nothing here claims composited pixels, and — said plainly because it
/// is the whole shape of this packet — <b>nothing here claims anything ever moved</b>.</para>
/// </summary>
public class HapticsRowHeadlessTests
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window)
    {
        public HapticParticipant Haptics => Window.Haptics;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private static async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp119-shell-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window);
    }

    private static T Descendant<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control, MouseButton button = MouseButton.Left)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static void OpenStudioAndHapticsRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowHaptics"));
    }

    [AvaloniaFact]
    public async Task TheHapticsRowOpensAPanelWithTHREEControls_AndTheCountIsTheFinding()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var panel = Descendant<StackPanel>(window, "HapticsModulePanel");
        Assert.True(panel.IsVisible);

        // Upstream's haptics page is 1640 lines over a 9193-line service: two provider boxes with
        // their own URLs, an auto-connect box, a per-event routing table, a master cap and a DSP
        // block (Views/Tabs/HapticsTabView.xaml, Services/Haptics/**). The ones NOT here configure a
        // mixer or an address this build has no implementation for, so each would be a control that
        // decides nothing.
        //
        // THE TWO PROVIDER BOXES ARE HERE because they decide something: both routes have a client
        // (HapticSinkFactory.AdmittedRoutes) and both flags default FALSE, which is upstream's own
        // stored default (Models/HapticSettings.cs:769). Without them that default would be
        // unreachable by design and the nothing-ticked refusal would name a checkbox nobody has.
        // Still a SET, so a fourth control cannot arrive unnoticed - and the URL boxes beside
        // upstream's are still absent, because no setting here honours a custom address.
        var interactive = panel.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CheckBox or TextBox or Slider or ComboBox)
            .Select(c => c.Name)
            .ToList();
        Assert.Equal(
            ["HapticsEnableToggle", "HapticsLovenseToggle", "HapticsButtplugToggle"],
            interactive);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>A provider box is REAL: it writes, it is not gated, and on its own it contacts nothing.</b>
    ///
    /// <para>This is the fact the two boxes were added for. The refusal a user reads when they have
    /// ticked nothing names a checkbox, and until this packet that checkbox did not exist — so the
    /// sentence sent people to look for a control the build did not have.</para>
    ///
    /// <para><b>Not gated</b> is upstream's shape rather than an oversight: its per-provider handler
    /// writes and saves with no premium check (<c>MainWindow/MainWindow.Haptics.cs:580-595</c>) while
    /// the gate sits on the master toggle (<c>:552-564</c>) — which is why the box below stays ticked
    /// on a build whose entitlement authority is unconfigured, and the master box beside it does
    /// not.</para>
    ///
    /// <para><b>And it opens nothing.</b> Ticking a route while the master toggle is off changes the
    /// sink's route and asks no server anything: the participant's connect needs BOTH conjuncts,
    /// which is upstream's own auto-connect guard (<c>App.xaml.cs:2176</c>, predicate
    /// <c>:3580-3589</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public async Task APROVIDERBoxWritesAndIsNOTGated_AndTickingItAloneContactsNOTHING()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var lovense = Descendant<CheckBox>(window, "HapticsLovenseToggle");
        var buttplug = Descendant<CheckBox>(window, "HapticsButtplugToggle");
        Assert.False(lovense.IsChecked);
        Assert.False(buttplug.IsChecked);
        Assert.Equal(HapticProviderRoute.None, boot.Haptics.Sink.Route);

        Click(window, lovense);

        // It STUCK - unlike the master box beside it, which the gate reverts.
        Assert.True(lovense.IsChecked);
        Assert.True(boot.Haptics.Preset.Current.LovenseEnabled);
        Assert.Equal(HapticProviderRoute.Lovense, boot.Haptics.Sink.Route);

        // The two are a SET, not a choice: ticking one never un-ticks the other, which is the exact
        // defect upstream records at MainWindow.Haptics.cs:576-579.
        Click(window, buttplug);
        Assert.True(lovense.IsChecked);
        Assert.True(buttplug.IsChecked);
        Assert.True(boot.Haptics.Preset.Current.ButtplugEnabled);

        // AND NOTHING WAS CONTACTED. The master toggle is still off - the gate refused it - so no
        // server was asked anything, and the capability line still says so.
        Assert.False(boot.Haptics.Enabled);
        Assert.Equal(0, boot.Haptics.ConnectAttempts);
        Assert.Null(boot.Haptics.LastObservation);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEREALCheckboxIsREFUSED_TheBoxSnapsBackAndTheSettingIsNeverWritten()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);
        var box = Descendant<CheckBox>(window, "HapticsEnableToggle");
        Assert.False(box.IsChecked);
        Assert.False(boot.Haptics.Enabled);

        Click(window, box);

        // Upstream reverts the box (MainWindow/MainWindow.Haptics.cs:491) and RETURNS at :497,
        // before the write at :500. Both halves are asserted, and the second is the one that matters: a box
        // that snapped back over a setting that had already been written would disagree with itself
        // at the next launch.
        Assert.False(box.IsChecked);
        Assert.False(boot.Haptics.Enabled);
        Assert.False(boot.Haptics.Preset.Current.Enabled);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEROWSRightClickIsREFUSEDToo_AndItIsTheOneGestureOnThisPageThatCanBe()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowHaptics");

        // Upstream's rack reaches the same refusal by flipping the panel's own box so the real
        // handler runs "including the premium gate that reverts the box for a free account"
        // (StudioTabView.xaml.cs:521-525).
        Click(window, row, MouseButton.Right);

        Assert.False(boot.Haptics.Enabled);
        // And the gesture did NOT also select the row: Handled stops it here, exactly as it does for
        // every other row (StudioTabView.xaml.cs:1115).
        Assert.False(row.IsChecked);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEDOTIsDARKAndCarriesNEITHERClass_OnEveryRunOfThisBuild()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var dot = Descendant<Avalonia.Controls.Shapes.Ellipse>(window, "HapticsRowDot");
        Assert.Equal(EffectDotState.Off, boot.Studio.RenderedHapticsDot);
        Assert.DoesNotContain("armed", dot.Classes);
        Assert.DoesNotContain("live", dot.Classes);

        // And it stays dark after the refused gesture, because the refusal wrote nothing for it to
        // report.
        Click(window, Descendant<CheckBox>(window, "HapticsEnableToggle"));
        Assert.Equal(EffectDotState.Off, boot.Studio.RenderedHapticsDot);
        Assert.DoesNotContain("armed", dot.Classes);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>The capability line says NOTHING WAS ASKED, and never a missing device.</b>
    ///
    /// <para>It used to say the admitted-provider gap, because no route had a client. Both do now, so
    /// on a fresh boot with the feature switched off the honest line is that a client is admitted and
    /// nobody has asked it anything — which is also the evidence that the launch probe did not open a
    /// socket. A server-unreachable line here would be a claim about a socket the build deliberately
    /// did not open, and the admission gap would tell a user this build has no client when it has
    /// two.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task THEPANELSaysNOTHINGWasASKED_AndNEVERNoDeviceFoundOrTheAdmissionGap()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var sink = Descendant<TextBlock>(window, "HapticsSinkState").Text!;

        Assert.Contains("a haptic client is admitted in this build", sink, StringComparison.Ordinal);
        Assert.Contains("nothing has been asked of its server yet", sink, StringComparison.Ordinal);
        // NOT the admission gap: this build has both clients.
        Assert.DoesNotContain("this build admits no haptic provider client", sink, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no haptic provider client stands behind", sink, StringComparison.OrdinalIgnoreCase);
        // NOT a claim about a server nobody contacted.
        Assert.DoesNotContain("the haptic server did not answer", sink, StringComparison.OrdinalIgnoreCase);
        // And NOT upstream's device-refusal wording, which would send a user to plug in a toy that
        // was never the problem (ButtplugProvider.cs:135, LovenseProvider.cs:116).
        Assert.DoesNotContain("Connect your device in Intiface first", sink, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connect toy in Lovense app first", sink, StringComparison.OrdinalIgnoreCase);

        // The lead line tells the user what is on the other end before any of that.
        var lead = Descendant<TextBlock>(window, "HapticsWhatItIs").Text!;
        Assert.Contains("separate program you install", lead, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEGATELineSaysCOULDNOTVERIFY_AndNEVERYouAreNotAPatron()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var gate = Descendant<TextBlock>(window, "HapticsGateState").Text!;

        // THE RULE, on the surface that renders it. This build's authority is unconfigured, so every
        // real user reaches the unknown branch — and the Windows app shows the refusal wording here
        // instead (MainWindow/MainWindow.Haptics.cs:492-496, en.json:3394).
        Assert.Contains(HapticGate.CouldNotVerifyHeader, gate, StringComparison.Ordinal);
        Assert.Contains(HapticGate.CouldNotVerifyFooter, gate, StringComparison.Ordinal);
        Assert.DoesNotContain(HapticGate.DeniedMessage, gate, StringComparison.Ordinal);
        Assert.DoesNotContain("supporters", gate, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<HapticGateDecision.RefusedUnverified>(boot.Haptics.Gate);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEABSENCELineSaysWhereTheSendReallyStops_WhichIsTheSinkNotTheModules()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var absence = Descendant<TextBlock>(window, "HapticsAbsenceState").Text!;

        // The sentence that stops a landed capability being read as a working feature.
        //
        // THIS NEEDLE HAS BEEN RE-POINTED TWICE, and neither time was it weakened. D210 wired the
        // limb, so "no effect in this build sends" went false on the page while the XML docs beside
        // the code were kept current; the replacement blamed the BUILD instead — "no provider route
        // is admitted at all" — and admitting both routes made THAT false in turn. Each time a user
        // was sent to wait for a release when the repair was on the screen in front of them. Both
        // dead clauses are banned by name below, because the pattern is the defect.
        //
        // What the page must say now is where the send really stops, which is outside this program:
        // a ticked provider box and a running server.
        //
        // The METHOD NAME moved with the assertion the first time, and that is deliberate. That wave
        // first kept the old name and justified it with "the floor pin is name-anchored", which is
        // FALSE: check-floor.mjs:222 compares a COUNT, and matches names only for NotExecuted
        // results (:231). This fact is not skipped, so renaming changes no pinned name and no total.
        Assert.Contains("tick a provider above", absence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command the haptic limb now", absence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no effect in this build sends", absence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no provider route is admitted", absence, StringComparison.OrdinalIgnoreCase);
        // And it still refuses to claim anything moved.
        Assert.Contains("no run of this app has yet driven a real toy", absence, StringComparison.OrdinalIgnoreCase);
        // And the structural half of the same claim: this row is not an effect at all.
        Assert.DoesNotContain(window.Session.Engine.Effects,
            e => string.Equals(e.Id, "haptics", StringComparison.Ordinal));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THESHELLTakesTheSAMEHapticOwnerTheCompositionRootBuilt()
    {
        var boot = await BootAsync();

        // A shell-local second owner would hold a second entitlement decision and a second sink, so
        // the switch on the page and the all-stop at teardown would be about different objects — and
        // the one that outlives the process is the one still holding a level.
        var fromHost = Assert.IsType<HapticParticipant>(
            boot.Host.Participants.OfType<HapticParticipant>().Single());
        Assert.Same(fromHost, boot.Window.Haptics);

        await boot.Host.ShutdownAsync();

        // Teardown really reached it, through the composition root's own pre-drain head slot.
        Assert.Equal(1, fromHost.AllStops);
    }
}
