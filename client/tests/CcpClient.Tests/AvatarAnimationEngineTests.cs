using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.AvatarTube;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Engine + participant tests (SP-015 Step 2): cadence math at exact declared deadlines
/// (non-uniform delays honored), pause/resume successor-frame + unchanged cadence,
/// pack-switch cleanliness, REAL-registry timer/subscription stability across N cycles,
/// and the typed undecodable-asset capability state. Deterministic via ManualAvatarClock.
/// </summary>
public sealed class AvatarAnimationEngineTests
{
    private static readonly int[] PoseDelays = SyntheticAvatarPacks.Circuit.Clip(SyntheticAvatarPacks.ClipPoses).DelaysMs;

    [Fact]
    public async Task Cadence_StaticPosesAdvanceAtExactDeclaredDeadlines_WithDip()
    {
        var (engine, clock, _, _) = CreateEngine();
        var emitted = 0;
        engine.FrameApplied += (_, _) => Interlocked.Increment(ref emitted);
        engine.Start();
        await SettleInitialAsync(clock);

        // Pose delays [1250,1000,1400,1100], dip 150ms (swap at deadline+75). Tick
        // granularity is one 16ms quantum: identity swaps land in [deadline+75, +75+2q].
        await AdvanceToAsync(clock, 1249);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 0), engine.CurrentFrame);
        await AdvanceToAsync(clock, 1324);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 0), engine.CurrentFrame); // swap not before deadline+75
        await AdvanceToAsync(clock, 1250 + 75 + 32);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 1), engine.CurrentFrame); // swapped within quantum slack

        // Non-uniform honored: pose 2's deadline is 1250+1000=2250 (not a uniform 2500).
        await AdvanceToAsync(clock, 2324);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 1), engine.CurrentFrame);
        await AdvanceToAsync(clock, 2250 + 75 + 32);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 2), engine.CurrentFrame);

        // Pose 3's deadline: 2250+1400=3650.
        await AdvanceToAsync(clock, 3724);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 2), engine.CurrentFrame);
        await AdvanceToAsync(clock, 3650 + 75 + 32);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 3), engine.CurrentFrame);

        await StopAndAssertCancelledAsync(engine);
    }

    [Fact]
    public async Task AnimatedMode_IdleAdvancesPerDelays_ThenRotatesToIdle2ViaCrossfade()
    {
        var (engine, clock, _, _) = CreateEngine();
        engine.Start();
        await SettleInitialAsync(clock);
        engine.SetMode(AvatarMode.Animated); // queued crossfade to idle (no min-hold)

        // The incoming idle becomes layerA when the entry crossfade completes (~1016ms).
        await AdvanceUntilAsync(clock, () => engine.CurrentFrame.ClipId == SyntheticAvatarPacks.ClipIdle, 3000);
        // The full idle pass (3790ms declared) then rotates to idle2 through a crossfade.
        await AdvanceUntilAsync(clock, () => engine.CurrentFrame.ClipId == SyntheticAvatarPacks.ClipIdle2, 7000);
        Assert.Equal(SyntheticAvatarPacks.ClipIdle2, engine.CurrentFrame.ClipId);

        await StopAndAssertCancelledAsync(engine);
    }

    [Fact]
    public async Task Crossfade_OpacitySumInvariant_AndNoBlankStep()
    {
        var (engine, clock, _, _) = CreateEngine();
        var violations = new List<string>();
        var emitted = 0;
        engine.FrameApplied += (_, _) => Interlocked.Increment(ref emitted);
        engine.FrameApplied += (_, args) =>
        {
            if (args.LayerB is not null)
            {
                var sum = args.LayerA.Opacity + args.LayerB.Opacity;
                if (Math.Abs(sum - 1.0) > 1e-9)
                {
                    violations.Add($"opacity sum {sum} at fade step (clips {args.LayerA.ClipId}->{args.LayerB.ClipId})");
                }
            }
            else if (args.LayerA.Opacity < 0.3 - 1e-9)
            {
                violations.Add($"single layer opacity {args.LayerA.Opacity} below the dip floor — blank interval");
            }
        };
        engine.Start();
        await SettleInitialAsync(clock);

        engine.SetMode(AvatarMode.Animated);
        // Walk through the whole 1000ms fade in quantum steps.
        await AdvanceToAsync(clock, 1088);

        Assert.Empty(violations);
        await StopAndAssertCancelledAsync(engine);
    }

    [Fact]
    public async Task PauseResume_SuccessorFrame_AndUnchangedCadence()
    {
        var (engine, clock, _, _) = CreateEngine();
        var emitted = 0;
        engine.FrameApplied += (_, _) => Interlocked.Increment(ref emitted);
        engine.Start();
        await SettleInitialAsync(clock);

        await AdvanceToAsync(clock, 600);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 0), engine.CurrentFrame);

        engine.Pause();
        var emittedAtPause = emitted;
        // Gate entry parks on the resume gate (not the clock): raw advances produce NO
        // emits and NO frame change for the whole frozen window.
        clock.Advance(4400);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        clock.Advance(4000);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 0), engine.CurrentFrame);
        Assert.Equal(emittedAtPause, emitted);

        engine.Resume();
        await WaitForAsync(() => emitted > emittedAtPause, "post-resume emit");
        // Effective time resumes at 600ms: the 1250ms deadline is 650 engine-ms away.
        await AdvanceToAsync(clock, 9000 + 649);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 0), engine.CurrentFrame);
        // SUCCESSOR of the paused frame at the declared boundary (+quantum slack) — not a
        // skip, not a replay, cadence unchanged.
        await AdvanceToAsync(clock, 9000 + 650 + 75 + 32);
        Assert.Equal((SyntheticAvatarPacks.ClipPoses, 1), engine.CurrentFrame);

        await StopAndAssertCancelledAsync(engine);
    }

    [Fact]
    public async Task PauseGate_ObservesTeardownCancellation_NoWedge()
    {
        var registry = new OperationRegistry();
        var engine = new AvatarAnimationEngine(
            registry.OwnerFor("t"), new ManualAvatarClock(), new ListLogSink(),
            AvatarPackTests.LoadFromMemory(SyntheticAvatarPacks.Circuit,
                SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var sw, out _), sw));
        engine.Start();
        engine.Pause();
        engine.Stop(); // teardown while paused
        var outcome = await engine.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
        var log = new ListLogSink();
        await registry.CancelAndDrainAsync(log, TimeSpan.FromSeconds(1));
        Assert.Equal(0, registry.UnobservedOperations);
        Assert.Equal(0, registry.OutstandingOperations);
    }

    [Fact]
    public async Task PackSwitch_Clean_AndRegistriesStableAcrossCycles()
    {
        var registry = new OperationRegistry();
        var participant = new AvatarTubeParticipant(
            registry.OwnerFor("AvatarTubeDemo-test"), new UiDispatchBoundary(), new ListLogSink(),
            AvatarPackTests.InMemoryAssetOpener());

        participant.StartTube(new ManualAvatarClock());
        Assert.Equal(1, registry.OutstandingOperations);
        Assert.Equal(0, participant.ActivePackId);
        Assert.IsType<CapabilityState.Available>(participant.AvatarCapability);
        Assert.Equal(1, participant.FrameSubscriberCount); // the participant's one engine subscription

        // Pack switch: clean re-base to idle[0] of the new pack, zero operation churn.
        participant.SwitchPack(1);
        Assert.Equal(1, participant.ActivePackId);
        Assert.Equal(1, registry.OutstandingOperations);
        Assert.Equal(1, participant.Engine!.CurrentPackId);
        Assert.Equal((SyntheticAvatarPacks.ClipIdle, 0), participant.Engine.CurrentFrame);

        // N start/stop + pack-switch cycles: real-registry counts stay flat.
        for (var cycle = 0; cycle < 10; cycle++)
        {
            participant.StopTube();
            var outcome = await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.IsType<OperationOutcome.Cancelled>(outcome);
            Assert.Equal(0, registry.OutstandingOperations);

            participant.StartTube(new ManualAvatarClock());
            Assert.Equal(1, registry.OutstandingOperations);
            Assert.Equal(1, participant.FrameSubscriberCount);
            participant.SwitchPack(cycle % 2);
        }

        var drainLog = new ListLogSink();
        participant.StopTube();
        await participant.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await registry.CancelAndDrainAsync(drainLog, TimeSpan.FromSeconds(1));
        Assert.Equal(0, registry.UnobservedOperations);
        Assert.Equal(0, registry.OutstandingOperations);
        // Stale-completion discards are the DESIGNED path (bounded by switch/stop count, never leaked-applied).
        Assert.True(registry.DiscardedStaleCompletions <= 12,
            $"discarded {registry.DiscardedStaleCompletions} beyond cycle bound");
    }

    [Fact]
    public void UndecodablePack_TypedDegraded_StaticFallback_BoundedDiagnostics()
    {
        var good = AvatarPackTests.InMemoryAssetOpener();
        var openCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Func<string, Stream> corruptPulse = path =>
        {
            openCounts[path] = openCounts.GetValueOrDefault(path) + 1;
            return path == SyntheticAvatarPacks.Pulse.SheetPath
                ? new MemoryStream([0xDE, 0xAD, 0xBE, 0xEF])
                : good(path);
        };
        var registry = new OperationRegistry();
        var log = new ListLogSink();
        var participant = new AvatarTubeParticipant(
            registry.OwnerFor("AvatarTubeDemo-test"), new UiDispatchBoundary(), log, corruptPulse);

        participant.StartTube(new ManualAvatarClock());
        Assert.IsType<CapabilityState.Available>(participant.AvatarCapability);

        participant.SwitchPack(1);
        var degraded = Assert.IsType<CapabilityState.Degraded>(participant.AvatarCapability);
        Assert.Equal(CapabilityReasonCodes.AssetUndecodable, degraded.Reason.Code);
        Assert.Contains("static fallback", degraded.SurvivingSemantics);
        // The fallback renders as a valid static avatar (pack 3 strip identity).
        Assert.Equal(SyntheticAvatarPacks.FallbackPackId, participant.Engine!.CurrentPackId);
        Assert.Equal(1, registry.OutstandingOperations); // engine alive on the fallback

        // Bounded: exactly ONE decode attempt of the corrupt sheet per switch — no retry loop.
        Assert.Equal(1, openCounts[SyntheticAvatarPacks.Pulse.SheetPath]);
        participant.SwitchPack(0);
        participant.SwitchPack(1);
        Assert.Equal(2, openCounts[SyntheticAvatarPacks.Pulse.SheetPath]);
        Assert.Equal(2, log.Lines.Count(l => l.Contains("avatar-capability: Degraded", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ClickReaction_Cooldown_TalkScheduleInterruption()
    {
        var (engine, clock, _, log) = CreateEngine();
        var emitted = 0;
        engine.FrameApplied += (_, _) => Interlocked.Increment(ref emitted);
        engine.Start();
        await SettleInitialAsync(clock);
        engine.SetMode(AvatarMode.Animated);
        await AdvanceUntilAsync(clock, () => engine.CurrentFrame.ClipId == SyntheticAvatarPacks.ClipIdle, 3000);

        // Accepted click: min-hold applies, then the click clip settles as layerA.
        engine.TriggerClick();
        // Duplicate click immediately after: inside the 3000ms cooldown — ignored + traced.
        engine.TriggerClick();
        Assert.Contains(log.Lines, l => l.Contains("click-cooldown-ignored", StringComparison.Ordinal));
        await AdvanceUntilAsync(clock, () => engine.CurrentFrame.ClipId == SyntheticAvatarPacks.ClipClick, 6000,
            () => $"current={engine.CurrentFrame}", () => string.Join(" | ", log.Lines.TakeLast(8)));
        // One-shot: after its single pass the pipeline returns to an idle.
        await AdvanceUntilAsync(clock,
            () => engine.CurrentFrame.ClipId is SyntheticAvatarPacks.ClipIdle or SyntheticAvatarPacks.ClipIdle2,
            8000);

        // Talk with a declared duration, then a click interrupt BEFORE the reaction:
        // the reaction schedule dies (WPF StopTalkSequence parity) and the pipeline
        // settles in idle without the cancelled reaction ever becoming layerA.
        var settledClips = new List<int>();
        engine.FrameApplied += (_, args) =>
        {
            if (args.LayerB is null && args.LayerA.Opacity > 0.999)
            {
                lock (settledClips) { settledClips.Add(args.LayerA.ClipId); }
            }
        };
        engine.TriggerTalk(3000);
        await AdvanceToAsync(clock, clock.NowMs + 200);
        engine.TriggerClick();
        await AdvanceUntilAsync(clock, () => engine.CurrentFrame.ClipId == SyntheticAvatarPacks.ClipClick, 9000);
        await AdvanceUntilAsync(clock,
            () => engine.CurrentFrame.ClipId is SyntheticAvatarPacks.ClipIdle or SyntheticAvatarPacks.ClipIdle2,
            9000);
        lock (settledClips)
        {
            Assert.DoesNotContain(SyntheticAvatarPacks.ClipReaction, settledClips);
        }

        await StopAndAssertCancelledAsync(engine);
    }

    // ---- helpers ----

    private static (AvatarAnimationEngine Engine, ManualAvatarClock Clock, OperationRegistry Registry, ListLogSink Log) CreateEngine()
    {
        var registry = new OperationRegistry();
        var clock = new ManualAvatarClock();
        var log = new ListLogSink();
        var engine = new AvatarAnimationEngine(
            registry.OwnerFor("t"), clock, log,
            AvatarPackTests.LoadFromMemory(SyntheticAvatarPacks.Circuit,
                SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var sw, out _), sw));
        return (engine, clock, registry, log);
    }

    /// <summary>Waits for the loop's first park in Delay (its initial iteration completed).</summary>
    private static async Task SettleInitialAsync(ManualAvatarClock clock)
    {
        await WaitForAsync(() => clock.DelayPending, "initial loop park");
    }

    /// <summary>Advances the manual clock to an absolute time in <=16ms steps, one race-free
    /// loop iteration per step (park -> advance -> wake -> re-park).</summary>
    private static async Task AdvanceToAsync(ManualAvatarClock clock, long targetMs)
    {
        while (clock.NowMs < targetMs)
        {
            await WaitForAsync(() => clock.DelayPending, $"loop parked before {clock.NowMs}");
            clock.Advance(Math.Min(16, targetMs - clock.NowMs));
            // Let the released iteration run and re-park.
            await WaitForAsync(() => clock.DelayPending, $"loop re-parked after {targetMs}");
        }
    }

    /// <summary>Advances the manual clock in quantum steps until the condition holds (bounded, in engine-time).</summary>
    private static Task AdvanceUntilAsync(ManualAvatarClock clock, Func<bool> condition, long maxAdvanceMs) =>
        AdvanceUntilAsync(clock, condition, maxAdvanceMs, () => "", () => "");

    private static async Task AdvanceUntilAsync(
        ManualAvatarClock clock, Func<bool> condition, long maxAdvanceMs,
        Func<string> state, Func<string> logTail)
    {
        var deadline = clock.NowMs + maxAdvanceMs;
        while (clock.NowMs < deadline)
        {
            if (condition())
            {
                return;
            }

            await AdvanceToAsync(clock, Math.Min(clock.NowMs + 16, deadline));
        }

        if (!condition())
        {
            throw new Xunit.Sdk.XunitException(
                $"condition not reached within {maxAdvanceMs}ms engine-time (t={clock.NowMs}); {state()}; {logTail()}");
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 600; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5);
        }

        throw new Xunit.Sdk.XunitException($"timeout waiting for {what}");
    }

    private static async Task StopAndAssertCancelledAsync(AvatarAnimationEngine engine)
    {
        engine.Stop();
        var outcome = await engine.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
    }

    private sealed class ListLogSink : ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines { get { lock (_gate) { return _lines.ToArray(); } } }

        public void Log(string message) { lock (_gate) { _lines.Add(message); } }
    }
}
