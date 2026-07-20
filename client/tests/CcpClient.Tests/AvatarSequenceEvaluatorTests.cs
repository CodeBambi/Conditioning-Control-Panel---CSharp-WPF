using CcpClient.Desktop.Features.AvatarTube;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Sequence-evaluator tests (SP-015 Step 2): every named verdict exercised on synthetic
/// capture series — pass AND fail shapes — incl. multiplied-speed falsification via
/// non-uniform-delay schedule discrimination and the resume successor/cadence assertions.
/// </summary>
public sealed class AvatarSequenceEvaluatorTests
{
    private static readonly AvatarPackDef Pack = SyntheticAvatarPacks.Circuit;
    private static readonly AvatarClipDef Idle = Pack.Clip(SyntheticAvatarPacks.ClipIdle);

    [Fact]
    public void NoBlank_UnionRule_DecodedOrVisibleContentPasses_TrueBlankFails()
    {
        var samples = new[]
        {
            new AvatarSample(1000, true, 0, 1, 2, 0.9, null),
            new AvatarSample(1200, false, -1, -1, -1, 0.5, "ambiguous-bit"), // crossfade/dip blend: visible, not blank
            new AvatarSample(1400, true, 0, 1, 3, 0.9, null),
        };
        var verdicts = AvatarSequenceEvaluator.Evaluate(samples, [], Pack);
        Assert.True(ByName(verdicts, "no-blank").Passed, ByName(verdicts, "no-blank").Detail);

        var withBlank = samples.Append(new AvatarSample(1600, false, -1, -1, -1, 0.01, "no-marker")).ToArray();
        var failed = AvatarSequenceEvaluator.Evaluate(withBlank, [], Pack);
        Assert.False(ByName(failed, "no-blank").Passed);
    }

    [Fact]
    public void FramesAdvance_RequiresRenderedChange()
    {
        var stuck = Enumerable.Range(0, 6).Select(i => new AvatarSample(1000 + i * 200, true, 0, 1, 2, 0.9, null)).ToArray();
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(stuck, [], Pack), "frames-advance").Passed);

        var moving = stuck.Append(new AvatarSample(2400, true, 0, 1, 3, 0.9, null)).ToArray();
        Assert.True(ByName(AvatarSequenceEvaluator.Evaluate(moving, [], Pack), "frames-advance").Passed);
    }

    [Fact]
    public void MonotonicModular_BackwardJumpFails_WrapAllowed()
    {
        var backward = new[]
        {
            new AvatarSample(1000, true, 0, 1, 3, 0.9, null),
            new AvatarSample(1300, true, 0, 1, 1, 0.9, null), // backward without wrap: second-pipeline interleave
        };
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(backward, [], Pack), "monotonic-modular-advance").Passed);

        var wrap = new[]
        {
            new AvatarSample(1000, true, 0, 1, Idle.Frames - 1, 0.9, null),
            new AvatarSample(1600, true, 0, 1, 0, 0.9, null), // legitimate wrap
        };
        Assert.True(ByName(AvatarSequenceEvaluator.Evaluate(wrap, [], Pack), "monotonic-modular-advance").Passed);
    }

    [Fact]
    public void MonotonicModular_JumpBeyondElapsedBoundFails()
    {
        var jump = new[]
        {
            new AvatarSample(1000, true, 0, 1, 0, 0.9, null),
            new AvatarSample(1100, true, 0, 1, 3, 0.9, null), // 3 frames in 100ms, min delay 480ms
        };
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(jump, [], Pack), "monotonic-modular-advance").Passed);
    }

    [Fact]
    public void DuplicateRun_BeyondHoldFails_WithinHoldPasses()
    {
        var within = Enumerable.Range(0, 3).Select(i => new AvatarSample(1000 + i * 200, true, 0, 1, 0, 0.9, null))
            .Append(new AvatarSample(1650, true, 0, 1, 1, 0.9, null)).ToArray();
        Assert.True(ByName(AvatarSequenceEvaluator.Evaluate(within, [], Pack), "no-duplicate-run-beyond-hold").Passed);

        // Frame 0's hold is 640ms (+300 slack); a 1200ms identical run outlives it.
        var beyond = Enumerable.Range(0, 7).Select(i => new AvatarSample(1000 + i * 200, true, 0, 1, 0, 0.9, null)).ToArray();
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(beyond, [], Pack), "no-duplicate-run-beyond-hold").Passed);
    }

    [Fact]
    public void ScheduleFit_TrueCadencePasses_AndMultipliedSpeedIsFalsified()
    {
        // True 1x: samples taken every 200ms against the declared non-uniform schedule.
        var trueRun = SimulateRun(0, SyntheticAvatarPacks.ClipIdle, Idle, startT: 10_000, captureEveryMs: 200, durationMs: 4200, speed: 1.0);
        var verdicts = AvatarSequenceEvaluator.Evaluate(trueRun, [], Pack);
        Assert.True(ByName(verdicts, "schedule-fit-1x").Passed, ByName(verdicts, "schedule-fit-1x").Detail);
        Assert.True(ByName(verdicts, "schedule-not-2x-speed").Passed, ByName(verdicts, "schedule-not-2x-speed").Detail);
        Assert.True(ByName(verdicts, "schedule-not-half-speed").Passed, ByName(verdicts, "schedule-not-half-speed").Detail);

        // Doubled speed (the duplicate-pipeline/multiplied-speed defect): the 1x verdict
        // must FAIL — non-uniform delays make the schedules distinguishable.
        var doubled = SimulateRun(0, SyntheticAvatarPacks.ClipIdle, Idle, startT: 10_000, captureEveryMs: 200, durationMs: 2100, speed: 2.0);
        var doubledVerdicts = AvatarSequenceEvaluator.Evaluate(doubled, [], Pack);
        Assert.False(ByName(doubledVerdicts, "schedule-fit-1x").Passed);

        // Halved speed likewise falsified.
        var halved = SimulateRun(0, SyntheticAvatarPacks.ClipIdle, Idle, startT: 10_000, captureEveryMs: 200, durationMs: 8400, speed: 0.5);
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(halved, [], Pack), "schedule-fit-1x").Passed);
    }

    [Fact]
    public void PauseResume_Freeze_Successor_AndUnchangedCadence()
    {
        var before = SimulateRun(0, SyntheticAvatarPacks.ClipIdle, Idle, startT: 10_000, captureEveryMs: 200, durationMs: 1400, speed: 1.0);
        // Freeze: capture clock keeps running; the frame holds (pause 11.400 .. 16.400).
        var pausedFrame = before[^1];
        var frozen = new[]
        {
            new AvatarSample(12_000, true, 0, SyntheticAvatarPacks.ClipIdle, pausedFrame.FrameIndex, 0.9, null),
            new AvatarSample(14_000, true, 0, SyntheticAvatarPacks.ClipIdle, pausedFrame.FrameIndex, 0.9, null),
            new AvatarSample(16_000, true, 0, SyntheticAvatarPacks.ClipIdle, pausedFrame.FrameIndex, 0.9, null),
        };
        // Resume: the engine resumes at the freeze point (elapsed 1400ms) — the paused
        // frame's hold completes, then the successor chain at declared cadence, wall times
        // shifted by the 5000ms pause.
        var after = SimulateRun(0, SyntheticAvatarPacks.ClipIdle, Idle, startT: 16_400, captureEveryMs: 200, durationMs: 2000, speed: 1.0,
            engineElapsedOffsetMs: 1400);
        var trace = new[]
        {
            new AvatarTraceEvent(11_400, AvatarTraceEvent.PauseBegin, 0),
            new AvatarTraceEvent(16_400, AvatarTraceEvent.PauseEnd, 0),
        };
        var verdicts = AvatarSequenceEvaluator.Evaluate([.. before, .. frozen, .. after], trace, Pack);
        Assert.True(ByName(verdicts, "pause-freeze").Passed, ByName(verdicts, "pause-freeze").Detail);
        Assert.True(ByName(verdicts, "resume-successor").Passed, ByName(verdicts, "resume-successor").Detail);
        Assert.True(ByName(verdicts, "cadence-unchanged-after-resume").Passed, ByName(verdicts, "cadence-unchanged-after-resume").Detail);
    }

    [Fact]
    public void PauseResume_FrameAdvancingDuringPauseFails()
    {
        var frozen = new[]
        {
            new AvatarSample(12_000, true, 0, 1, 2, 0.9, null),
            new AvatarSample(14_000, true, 0, 1, 3, 0.9, null), // CHANGED during pause
        };
        var trace = new[]
        {
            new AvatarTraceEvent(11_400, AvatarTraceEvent.PauseBegin, 0),
            new AvatarTraceEvent(16_400, AvatarTraceEvent.PauseEnd, 0),
        };
        var verdicts = AvatarSequenceEvaluator.Evaluate(frozen, trace, Pack);
        Assert.False(ByName(verdicts, "pause-freeze").Passed);
    }

    [Fact]
    public void PackSwitch_AllNewPackFromFrameZero_OldPackAfterFails()
    {
        var samples = new[]
        {
            new AvatarSample(1000, true, 0, 1, 2, 0.9, null),
            new AvatarSample(2000, true, 1, 1, 0, 0.9, null),
            new AvatarSample(2600, true, 1, 1, 1, 0.9, null),
        };
        var trace = new[] { new AvatarTraceEvent(1500, AvatarTraceEvent.PackSwitch, 1) };
        Assert.True(ByName(AvatarSequenceEvaluator.Evaluate(samples, trace, Pack), "pack-switch-clean").Passed);

        var dirty = samples.Append(new AvatarSample(3000, true, 0, 1, 4, 0.9, null)).ToArray();
        Assert.False(ByName(AvatarSequenceEvaluator.Evaluate(dirty, trace, Pack), "pack-switch-clean").Passed);
    }

    // ---- synthesis helpers ----

    private static AvatarVerdict ByName(IReadOnlyList<AvatarVerdict> verdicts, string name) =>
        verdicts.Single(v => v.Name == name);

    /// <summary>Generates capture samples of a pipeline running the declared clip at <paramref name="speed"/>x.</summary>
    private static List<AvatarSample> SimulateRun(
        int packId, int clipId, AvatarClipDef clip, long startT, double captureEveryMs, long durationMs,
        double speed, int startOrdinal = 0, long engineElapsedOffsetMs = 0)
    {
        var cum = AvatarSchedule.Cumulative(clip.DelaysMs);
        var pass = cum[^1];
        var samples = new List<AvatarSample>();
        for (var t = 0.0; t < durationMs; t += captureEveryMs)
        {
            var engineElapsed = engineElapsedOffsetMs + (long)(t * speed);
            var cycles = (int)(engineElapsed / pass);
            var within = engineElapsed % pass;
            var index = 0;
            while (index < clip.Frames - 1 && cum[index + 1] <= within)
            {
                index++;
            }

            var ordinal = startOrdinal + cycles * clip.Frames + index;
            samples.Add(new AvatarSample(startT + (long)t, true, packId, clipId, ordinal % clip.Frames, 0.9, null));
        }

        return samples;
    }
}
