using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.AvatarTube;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Tests;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-015 headless interaction tests for the AvatarTube DEMONSTRATOR surface. Draw-level
/// ONLY (verification-harness.md evidence-class rule): real in-memory layout, real routed
/// input, real engine state — never compositor/window-manager/presentation claims. The
/// rendered-frame evidence matrix is the Windows-headed Step 4's job.
/// </summary>
public class AvatarTubeHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Dashboard, AvatarTubeParticipant Participant, AvatarTubeDemonstratorWindow Tube)>
        BootAsync(ManualAvatarClock? clock = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp015-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        var dashboard = new MainWindow(host);
        dashboard.Show();

        var participant = host.Participants.OfType<AvatarTubeParticipant>().Single();
        if (clock is not null)
        {
            // Pre-start with the manual clock; the window's phase-4 StartTube is idempotent.
            participant.StartTube(clock);
        }

        var tube = new AvatarTubeDemonstratorWindow(host, dashboard, participant);
        tube.Show(dashboard);
        return (host, dashboard, participant, tube);
    }

    private static T Control<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static Point CenterIn(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>Real routed Click event (deterministic — probe-text layout shifts make
    /// coordinate hit-testing flaky; the event path is what the handlers are).</summary>
    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, button));

    private static async Task AdvanceAsync(ManualAvatarClock clock, long ms)
    {
        var deadline = clock.NowMs + ms;
        while (clock.NowMs < deadline)
        {
            // Class 2 (SP-059): the engine's async loop must observe the advance — the
            // tolerant window with the loud classifier via the single approved helper.
            await TestWait.Until(() => clock.DelayPending, $"engine loop parked (clock at {clock.NowMs}ms)");
            clock.Advance(Math.Min(16, deadline - clock.NowMs));
            await Task.Delay(10); // let posted UI projections land // wallclock-allow: negative-observation settle for the dispatcher — losing it can only false-GREEN, never flake red
        }
    }

    [AvaloniaFact]
    public async Task Tube_Opens_LayersPresent_FirstFrameAndCapabilityRendered()
    {
        var (host, _, participant, tube) = await BootAsync();
        var layerA = Control<Image>(tube, "LayerAImage");
        // Class 2 (SP-059): the first frame renders through the REAL headless dispatcher —
        // the tolerant window with the loud classifier via the single approved helper.
        await TestWait.Until(() => layerA.Source is not null, "the first frame rendering through the real dispatcher");

        Assert.IsType<WriteableBitmap>(layerA.Source);
        Assert.Equal(1.0, layerA.Opacity);
        Assert.Equal(0, Control<Image>(tube, "LayerBImage").Opacity);

        var capability = Control<TextBlock>(tube, "CapabilityText").Text;
        Assert.Contains("Available", capability);
        Assert.Contains("circuit", capability);
        Assert.Contains("avatar-probe: pack=0", Control<TextBlock>(tube, "ProbeText").Text);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task FrameAdvance_UpdatesLayerSource_FloatTransform_AndProbe()
    {
        var clock = new ManualAvatarClock();
        var (host, _, participant, tube) = await BootAsync(clock);
        var probe = Control<TextBlock>(tube, "ProbeText");
        var floatCanvas = Control<Canvas>(tube, "FloatCanvas");

        await AdvanceAsync(clock, 100);
        var probeAt100 = probe.Text;
        var floatAt100 = ((TranslateTransform)floatCanvas.RenderTransform!).Y;

        await AdvanceAsync(clock, 1400); // pose 0 hold is 1250ms — frame must have advanced
        Assert.NotEqual(probeAt100, probe.Text);
        Assert.Contains("frame=1", probe.Text);
        var floatAt1500 = ((TranslateTransform)floatCanvas.RenderTransform!).Y;
        Assert.NotEqual(floatAt100, floatAt1500); // the float transform moves (content only)
        Assert.Equal(0, (int)tube.Position.X + 0); // window position untouched by float (still owner-placed)

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AttachDetach_ViewOnlySwitch_PreservesPipeline()
    {
        var clock = new ManualAvatarClock();
        var (host, dashboard, participant, tube) = await BootAsync(clock);
        await AdvanceAsync(clock, 100);

        var frameBefore = participant.Engine!.CurrentFrame;
        var attach = Control<Button>(tube, "AttachButton");
        Click(attach);

        Assert.True(tube.Topmost); // detached = topmost ownerless widget
        Assert.Null(tube.Owner);
        Assert.Equal(frameBefore, participant.Engine.CurrentFrame); // pipeline untouched
        Assert.Equal("Attach", attach.Content);

        // The pipeline keeps advancing across the switch (state preservation, not a restart).
        await AdvanceAsync(clock, 1400);
        Assert.NotEqual(frameBefore, participant.Engine.CurrentFrame);

        Click(attach);
        Assert.False(tube.Topmost);
        Assert.Same(dashboard, tube.Owner);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AvatarClick_RoutesToClickEmote_DistinctFromSpeech()
    {
        var clock = new ManualAvatarClock();
        var (host, _, participant, tube) = await BootAsync(clock);
        // Enter animated mode so click emotes apply (WPF emote-mode parity).
        Click(Control<Button>(tube, "ModeButton"));
        await AdvanceAsync(clock, 1100); // entry crossfade into idle

        // REAL routed pointer press on the avatar stage (the click-reaction gesture).
        var stage = Control<Border>(tube, "AvatarStage");
        tube.UpdateLayout();
        var center = CenterIn(tube, stage);
        tube.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        tube.MouseUp(center, MouseButton.Left, RawInputModifiers.None);

        // Min-hold (2000ms) + crossfade (1000ms) → the click clip settles as layerA.
        await AdvanceAsync(clock, 3300);
        Assert.Equal(SyntheticAvatarPacks.ClipClick, participant.Engine!.CurrentFrame.ClipId);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task PauseButton_FreezesPipeline_ResumeContinuesSuccessor()
    {
        var clock = new ManualAvatarClock();
        var (host, _, participant, tube) = await BootAsync(clock);
        await AdvanceAsync(clock, 600);

        var pause = Control<Button>(tube, "PauseButton");
        Click(pause);
        Assert.True(participant.Engine!.Paused);

        var frozen = participant.Engine.CurrentFrame;
        clock.Advance(5000); // frozen: gate parks on the resume gate, not the clock
        await Task.Delay(100); // wallclock-allow: negative-observation settle ("nothing happens while paused") — losing the window can only false-GREEN, never flake red
        Assert.Equal(frozen, participant.Engine.CurrentFrame);

        Click(pause);
        Assert.False(participant.Engine.Paused);
        await AdvanceAsync(clock, 800); // 1250 deadline − 600 elapsed = 650 (+quantum)
        // SUCCESSOR of the frozen frame, unchanged cadence.
        Assert.Equal((frozen.ClipId, frozen.FrameIndex + 1), participant.Engine.CurrentFrame);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task CorruptPackSwitch_TypedDegraded_AndStaticFallbackVisible()
    {
        var clock = new ManualAvatarClock();
        var (host, _, participant, tube) = await BootAsync(clock);
        participant.CorruptPackForDemo(SyntheticAvatarPacks.Pulse.PackId);

        Click(Control<Button>(tube, "PackButton"));
        // Class 2 (SP-059): the capability text lands via the next dispatcher projection —
        // poll the POSITIVE condition via the approved helper instead of a fixed sleep.
        await TestWait.Until(
            () => Control<TextBlock>(tube, "CapabilityText").Text?.Contains("Degraded", StringComparison.Ordinal) == true,
            "the capability text projecting Degraded through the dispatcher");

        var degraded = Assert.IsType<CapabilityState.Degraded>(participant.AvatarCapability);
        Assert.Equal(CapabilityReasonCodes.AssetUndecodable, degraded.Reason.Code);
        Assert.Contains("Degraded", Control<TextBlock>(tube, "CapabilityText").Text);
        Assert.Contains("static fallback", Control<TextBlock>(tube, "CapabilityText").Text);
        // The fallback frame renders (a valid static avatar — pack 3 strip identity).
        Assert.Equal(SyntheticAvatarPacks.FallbackPackId, participant.Engine!.CurrentPackId);
        Assert.IsType<WriteableBitmap>(Control<Image>(tube, "LayerAImage").Source);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Close_DisposesEngineAndSubscriptions()
    {
        var clock = new ManualAvatarClock();
        var (host, _, participant, tube) = await BootAsync(clock);
        await AdvanceAsync(clock, 100);
        Assert.NotNull(participant.Engine);

        tube.Close();
        var outcome = await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5)); // wallclock-allow: terminal hang tripwire — close is token-driven; expiry means the participant never cancelled (product failure)
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
        Assert.Null(participant.Engine);
        Assert.Equal(0, participant.FrameSubscriberCount);
        // Back to the heartbeat-only baseline (the ticker stays off — restored flag false).
        Assert.Equal(1, host.Registry.OutstandingOperations);

        await host.ShutdownAsync();
    }
}
