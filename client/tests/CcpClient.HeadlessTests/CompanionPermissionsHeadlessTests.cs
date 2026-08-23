using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The AI effect-permissions grid on the companion surface. Draw-level ONLY
/// (verification-harness.md evidence-class rule): real layout, real binding application, real
/// headless pointer input — no compositor, no pixel and no focus claim. The headed seat owns
/// those.
/// </summary>
public class CompanionPermissionsHeadlessTests
{
    /// <summary>
    /// The default-closed fact AT THE SURFACE: the master switch is off and the ten per-effect
    /// switches are not on screen at all, on a build that has just started with no persisted
    /// permission state anywhere (there is none to persist — the state is session-scoped).
    /// </summary>
    [AvaloniaFact]
    public async Task TheGridOpensClosed_MasterOff_AndNoSwitchOnScreen()
    {
        var (host, window, companion) = await OpenAsync();

        var master = companion.FindControl<CheckBox>("EffectsMasterToggle")!;
        Assert.False(master.IsChecked);
        Assert.False(companion.FindControl<Border>("EffectPermissionsPanel")!.IsVisible);
        Assert.Empty(Switches(companion));

        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        Assert.False(participant.Permissions.MasterEnabled);
        Assert.All(Enum.GetValues<AiCommandKind>(), kind => Assert.False(participant.Permissions.IsAllowed(kind)));

        await host.ShutdownAsync();
    }

    /// <summary>
    /// Turning the master on with a real pointer press reveals upstream's ten switches
    /// (<c>Views/Controls/Companion/AiPermissionsGrid.xaml:185-199</c>), every one of them
    /// UNTICKED — the master is a door, never a bulk admission.
    /// </summary>
    [AvaloniaFact]
    public async Task TurningTheMasterOnRevealsTenSwitches_EveryOneStillUnticked()
    {
        var (host, window, companion) = await OpenAsync();

        Click(companion, companion.FindControl<CheckBox>("EffectsMasterToggle")!);

        Assert.True(companion.FindControl<Border>("EffectPermissionsPanel")!.IsVisible);
        var switches = Switches(companion);
        Assert.Equal(10, switches.Count);
        Assert.All(switches, box => Assert.False(box.IsChecked));

        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        Assert.True(participant.Permissions.MasterEnabled);
        Assert.All(Enum.GetValues<AiCommandKind>(), kind => Assert.False(participant.Permissions.IsAllowed(kind)));

        await host.ShutdownAsync();
    }

    /// <summary>
    /// The change half of "see and change": a real press on the Overlay switch admits BOTH
    /// overlay kinds in the participant's typed state — the same state the execution gates read
    /// (<c>AiExecutionGates.IsEffectAllowed</c>) — and pressing it again withdraws them.
    /// One switch, two kinds, because upstream's <c>AllowAiOverlay</c> governs both
    /// (<c>Services/Commands/AiCommandService.cs:193-194</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task PressingASwitchMovesTheGateTheExecutorReads_AndPressingItAgainWithdrawsIt()
    {
        var (host, window, companion) = await OpenAsync();
        var participant = host.Participants.OfType<CompanionParticipant>().Single();

        Click(companion, companion.FindControl<CheckBox>("EffectsMasterToggle")!);
        var overlay = Switches(companion).Single(box => AutomationProperties.GetAutomationId(box) == "Overlay");

        Click(companion, overlay);
        Assert.True(participant.Permissions.IsAllowed(AiCommandKind.Spiral));
        Assert.True(participant.Permissions.IsAllowed(AiCommandKind.Pink));
        Assert.False(participant.Permissions.IsAllowed(AiCommandKind.Bubbles));

        Click(companion, overlay);
        Assert.False(participant.Permissions.IsAllowed(AiCommandKind.Spiral));
        Assert.False(participant.Permissions.IsAllowed(AiCommandKind.Pink));

        await host.ShutdownAsync();
    }

    /// <summary>
    /// Each row says whether this build can do it at all, and never leaves the answer implied:
    /// a row the executor cannot handle carries a visible note and the full named reason on it.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryRowTheExecutorCannotHandleSaysSoOnScreen()
    {
        var (host, window, companion) = await OpenAsync();
        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        Click(companion, companion.FindControl<CheckBox>("EffectsMasterToggle")!);

        foreach (var row in companion.ViewModel.EffectPermissions)
        {
            var backed = AiEffectPermissions.Rows.Single(r => r.Id == row.Id)
                .Kinds.All(participant.Executor.Handles);
            Assert.Equal(backed, row.Backed);
            Assert.Equal(!backed, row.BackendNoteVisible);
            Assert.Equal(backed, row.BackendNote.Length == 0);
        }

        // The notes really render: one TextBlock per unbacked row, none for a backed one.
        var notes = companion.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "· not in this build" && t.IsVisible).ToList();
        Assert.Equal(companion.ViewModel.EffectPermissions.Count(r => !r.Backed), notes.Count);
        Assert.All(notes, note => Assert.NotNull(ToolTip.GetTip(note)));

        await host.ShutdownAsync();
    }

    /// <summary>
    /// The two honesty lines that keep the panel from over-claiming: the door says where the
    /// permissions are, and the window says that nothing dispatches yet.
    /// </summary>
    [AvaloniaFact]
    public async Task TheDoorNamesThePermissions_AndTheWindowSaysNothingDispatchesYet()
    {
        var (host, window, companion) = await OpenAsync();

        var pointer = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "PermissionsPointer");
        Assert.Contains("allowed to do to your screen", pointer.Text);
        Assert.True(pointer.IsVisible);

        var notice = companion.FindControl<TextBlock>("EffectDispatchNotice")!;
        Assert.True(notice.IsVisible);
        Assert.Contains("Nothing dispatches yet", notice.Text);

        await host.ShutdownAsync();
    }

    // =====================================================================================

    private static async Task<(ApplicationHost Host, MainWindow Window, CompanionWindow Companion)> OpenAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-ai-perms-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();

        // Both hops are real pointer input, as the companion's own headless suite does it.
        ClickIn(window, window.FindControl<RadioButton>("DoorCompanion")!);
        window.UpdateLayout();
        ClickIn(window, window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CompanionButton"));

        var companion = window.Companion;
        Assert.NotNull(companion);
        companion!.UpdateLayout();
        return (host!, window, companion);
    }

    private static IReadOnlyList<CheckBox> Switches(CompanionWindow companion) =>
        [.. companion.GetVisualDescendants().OfType<CheckBox>()
            .Where(box => AiEffectPermissions.Rows.Any(row => row.Id == AutomationProperties.GetAutomationId(box)))];

    private static void Click(CompanionWindow companion, Control control)
    {
        ClickIn(companion, control);
        companion.UpdateLayout();
    }

    private static void ClickIn(Window window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
    }
}
