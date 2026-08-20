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
/// SP-119 — the Haptics rack row, driven by REAL headless input on the REAL controls, from a cold
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
    public async Task TheHapticsRowOpensAPanelWithONEControl_AndTheCountIsTheFinding()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var panel = Descendant<StackPanel>(window, "HapticsModulePanel");
        Assert.True(panel.IsVisible);

        // Upstream's haptics page is 1640 lines over a 9193-line service: two provider boxes with
        // their own URLs, an auto-connect box, a per-event routing table, a master cap and a DSP
        // block (Views/Tabs/HapticsTabView.xaml, Services/Haptics/**). EVERY one of them configures
        // a connection this build cannot make, so the panel carries the ONE control the premium gate
        // guards and no other. Asserted as a SET so a second one cannot arrive unnoticed.
        var interactive = panel.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CheckBox or TextBox or Slider or ComboBox)
            .Select(c => c.Name)
            .ToList();
        Assert.Equal(["HapticsEnableToggle"], interactive);

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

        // Upstream reverts the box (MainWindow/MainWindow.Haptics.cs:489) and RETURNS before the
        // write at :499. Both halves are asserted, and the second is the one that matters: a box
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

    [AvaloniaFact]
    public async Task THEPANELSaysTheADMITTEDPROVIDERGap_AndNEVERNoDeviceFound()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var sink = Descendant<TextBlock>(window, "HapticsSinkState").Text!;

        // Both routes, both transports, and the fact that both are clients of another program.
        Assert.Contains("ws://127.0.0.1:12345", sink, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:20010", sink, StringComparison.Ordinal);
        Assert.Contains("SEPARATE SERVER PROCESS", sink, StringComparison.Ordinal);
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
        // instead (MainWindow/MainWindow.Haptics.cs:490-495, en.json:3394).
        Assert.Contains(HapticGate.CouldNotVerifyHeader, gate, StringComparison.Ordinal);
        Assert.Contains(HapticGate.CouldNotVerifyFooter, gate, StringComparison.Ordinal);
        Assert.DoesNotContain(HapticGate.DeniedMessage, gate, StringComparison.Ordinal);
        Assert.DoesNotContain("supporters", gate, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<HapticGateDecision.RefusedUnverified>(boot.Haptics.Gate);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task THEABSENCELineSaysNOEFFECTSendsAnythingHereYet_WhichIsD179OnThePage()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndHapticsRow(window);

        var absence = Descendant<TextBlock>(window, "HapticsAbsenceState").Text!;

        // The sentence that stops a landed capability being read as a working feature.
        Assert.Contains("no effect in this build sends anything to haptics yet",
            absence, StringComparison.OrdinalIgnoreCase);
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
