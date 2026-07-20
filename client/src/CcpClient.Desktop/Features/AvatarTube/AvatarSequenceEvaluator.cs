namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// Pure cadence/schedule math and the evidence sequence evaluator (pre-approach consult
/// verdict #5-#7): the temporal assertions live here as machine-checked, unit-tested logic
/// — never hand-held PowerShell. Samples come from rendered captures (strip decodes);
/// trace events come from the engine's structured diagnostic; the pack definition supplies
/// the declared non-uniform delays the rendered cadence is falsified against.
/// </summary>
public static class AvatarSchedule
{
    /// <summary>Cumulative display deadlines: frame k shows during [Cum(k), Cum(k+1)); Cum(0)=0.</summary>
    public static long[] Cumulative(IReadOnlyList<int> delaysMs)
    {
        var cum = new long[delaysMs.Count + 1];
        for (var i = 0; i < delaysMs.Count; i++)
        {
            cum[i + 1] = cum[i] + delaysMs[i];
        }

        return cum;
    }

    /// <summary>Cumulative time of a looped ordinal (ordinal may exceed one pass).</summary>
    public static long CumulativeAt(IReadOnlyList<int> delaysMs, long[] cum, int ordinal)
    {
        var n = delaysMs.Count;
        var pass = cum[n];
        var cycles = Math.DivRem(ordinal, n, out var within);
        return cycles * pass + cum[within];
    }

    /// <summary>
    /// Phase fit: the maximal absolute residual of samples against
    /// <c>t = phase + scale · cum(ordinal)</c> with the best median phase. A 1x fit within
    /// tolerance PROVES declared cadence; a failed 2x/0.5x fit FALSIFIES multiplied/halved
    /// speed — non-uniform delays make the schedules distinguishable (packet rule).
    /// </summary>
    public static long MaxResidual(
        IReadOnlyList<(long TimestampMs, int Ordinal)> samples, IReadOnlyList<int> delaysMs, double scale)
    {
        var cum = Cumulative(delaysMs);
        var offsets = samples
            .Select(s => s.TimestampMs - (long)Math.Round(scale * CumulativeAt(delaysMs, cum, s.Ordinal)))
            .OrderBy(v => v)
            .ToArray();
        var phase = offsets[offsets.Length / 2];
        return samples
            .Select(s => Math.Abs(s.TimestampMs - (phase + (long)Math.Round(scale * CumulativeAt(delaysMs, cum, s.Ordinal)))))
            .Max();
    }
}

/// <summary>One capture sample: strip decode + content measurement at a wall-clock time.</summary>
public sealed record AvatarSample(
    long TimestampMs,
    bool Decoded,
    int PackId,
    int ClipId,
    int FrameIndex,
    double ContentFraction,
    string? Failure);

/// <summary>One engine trace event the evaluator correlates captures against.</summary>
public sealed record AvatarTraceEvent(long TimestampMs, string Kind, int PackId)
{
    public const string PauseBegin = "pause-begin";
    public const string PauseEnd = "pause-end";
    public const string PackSwitch = "pack-switch";
    public const string CrossfadeStart = "crossfade-start";
    public const string CrossfadeEnd = "crossfade-end";
}

/// <summary>A named evidence verdict (the CcpVerify named-check shape, applied to sequences).</summary>
public sealed record AvatarVerdict(string Name, bool Passed, string Detail)
{
    public override string ToString() => $"{(Passed ? "PASS" : "FAIL")} {Name} — {Detail}";
}

/// <summary>
/// Evaluates a capture sequence + engine trace against the declared pack schedule and emits
/// named verdicts per acceptance property: frames advance, no blanks (union rule — consult
/// verdict #8), monotonic modular advance + elapsed-bounded jumps (duplicate-pipeline
/// falsification), no duplicate-run beyond hold, schedule-fit 1x / not-2x / not-0.5x
/// (multiplied-speed falsification), pause freeze, resume successor + unchanged cadence,
/// pack-switch cleanliness. Pure: no I/O, no Avalonia.
/// </summary>
public static class AvatarSequenceEvaluator
{
    /// <summary>
    /// Schedule-fit tolerance. Discrimination math (why 500ms keeps falsification robust):
    /// capture sampling at ~100-200ms spacing over NON-UNIFORM delays quantizes true-run
    /// residuals to ~420ms worst-case, and a pause/resume window adds a systematic
    /// mid-hold offset (~300ms) — 350ms was empirically too tight (evaluator tests).
    /// Multiplied-speed misfit grows ~cum(k)/2: ~1000-1900ms over one idle pass for 2x,
    /// ~2000-3800ms for 0.5x — both far beyond 500ms, so 2x/0.5x rejection stays decisive.
    /// </summary>
    public const double DefaultScheduleToleranceMs = 500;
    public const double DefaultBlankFractionThreshold = 0.05;
    public const long DefaultHoldSlackMs = 300;

    public static IReadOnlyList<AvatarVerdict> Evaluate(
        IReadOnlyList<AvatarSample> samples,
        IReadOnlyList<AvatarTraceEvent> trace,
        AvatarPackDef pack)
    {
        var verdicts = new List<AvatarVerdict>();
        if (samples.Count == 0)
        {
            verdicts.Add(new AvatarVerdict("samples-present", false, "no capture samples supplied"));
            return verdicts;
        }

        verdicts.Add(EvaluateNoBlank(samples));
        verdicts.Add(EvaluateFramesAdvance(samples));
        verdicts.AddRange(EvaluateRuns(samples, pack, trace));

        var pauseBegin = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PauseBegin);
        var pauseEnd = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PauseEnd);
        if (pauseBegin is not null && pauseEnd is not null)
        {
            verdicts.AddRange(EvaluatePauseResume(samples, pack, pauseBegin, pauseEnd));
        }

        var packSwitch = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PackSwitch);
        if (packSwitch is not null)
        {
            verdicts.Add(EvaluatePackSwitch(samples, packSwitch));
        }

        return verdicts;
    }

    /// <summary>Union no-blank (consult verdict #8): every capture either strip-decodes OR shows visible content.</summary>
    private static AvatarVerdict EvaluateNoBlank(IReadOnlyList<AvatarSample> samples)
    {
        var blanks = samples
            .Where(s => !s.Decoded && s.ContentFraction < DefaultBlankFractionThreshold)
            .Select(s => $"t={s.TimestampMs} fraction={s.ContentFraction:F3} ({s.Failure})")
            .ToArray();
        return new AvatarVerdict(
            "no-blank",
            blanks.Length == 0,
            blanks.Length == 0
                ? $"{samples.Count} captures: every one strip-decodes or shows visible content (crossfade/dip union)"
                : $"{blanks.Length} blank capture(s): {string.Join("; ", blanks.Take(3))}");
    }

    private static AvatarVerdict EvaluateFramesAdvance(IReadOnlyList<AvatarSample> samples)
    {
        var distinct = samples.Where(s => s.Decoded).Select(s => (s.ClipId, s.FrameIndex)).Distinct().Count();
        return new AvatarVerdict(
            "frames-advance",
            distinct >= 2,
            distinct >= 2
                ? $"{distinct} distinct decoded frames across the sequence"
                : $"only {distinct} distinct decoded frame(s) — rendered frames did not change");
    }

    /// <summary>Per-run checks on maximal same-(pack,clip) segments.</summary>
    private static IEnumerable<AvatarVerdict> EvaluateRuns(
        IReadOnlyList<AvatarSample> samples, AvatarPackDef pack, IReadOnlyList<AvatarTraceEvent> trace)
    {
        // Pauses legitimately freeze a frame: pause windows are subtracted from run spans
        // (a frozen frame is not a duplicate-run defect).
        var pauseWindows = new List<(long Begin, long End)>();
        AvatarTraceEvent? pending = null;
        foreach (var e in trace)
        {
            if (e.Kind == AvatarTraceEvent.PauseBegin)
            {
                pending = e;
            }
            else if (e.Kind == AvatarTraceEvent.PauseEnd && pending is not null)
            {
                pauseWindows.Add((pending.TimestampMs, e.TimestampMs));
                pending = null;
            }
        }

        var runs = new List<List<AvatarSample>>();
        foreach (var sample in samples.Where(s => s.Decoded))
        {
            if (runs.Count == 0 || runs[^1][0].PackId != sample.PackId || runs[^1][0].ClipId != sample.ClipId)
            {
                runs.Add([sample]);
            }
            else
            {
                runs[^1].Add(sample);
            }
        }

        var monotonicViolations = new List<string>();
        var dupRunViolations = new List<string>();
        var fitDetails = new List<string>();
        var fit1xPassed = 0;
        var fit2xRejected = 0;
        var fitHalfRejected = 0;
        var fitRuns = 0;

        foreach (var run in runs)
        {
            var clip = pack.Clip(run[0].ClipId);
            var frames = clip.Frames;

            // Monotonic modular advance: no decrease except a wrap from the last frame;
            // forward jump bounded by elapsed time (an interleaved second pipeline violates
            // one of these — duplicate-pipeline falsification, consult verdict #6).
            for (var i = 1; i < run.Count; i++)
            {
                var prev = run[i - 1];
                var next = run[i];
                var rawJump = next.FrameIndex - prev.FrameIndex;
                if (rawJump < 0 && prev.FrameIndex != frames - 1)
                {
                    monotonicViolations.Add($"t={next.TimestampMs}: {prev.FrameIndex}->{next.FrameIndex} backward (clip {next.ClipId})");
                    continue;
                }

                var jump = (next.FrameIndex - prev.FrameIndex + frames) % frames;
                var elapsed = next.TimestampMs - prev.TimestampMs;
                var maxJump = elapsed <= 0 ? 0 : (int)(elapsed / clip.DelaysMs.Min()) + 1;
                if (jump > maxJump)
                {
                    monotonicViolations.Add(
                        $"t={next.TimestampMs}: jump {jump} frames in {elapsed}ms exceeds schedule bound {maxJump} (clip {next.ClipId})");
                }
            }

            // No duplicate-run beyond hold: identical-frame runs span at most that frame's
            // declared delay + slack (sampling duplicates inside one hold are expected).
            var runStart = 0;
            for (var i = 1; i <= run.Count; i++)
            {
                if (i < run.Count && run[i].FrameIndex == run[runStart].FrameIndex)
                {
                    continue;
                }

                var rawSpan = run[i - 1].TimestampMs - run[runStart].TimestampMs;
                var pausedOverlap = pauseWindows.Sum(w =>
                    Math.Max(0, Math.Min(run[i - 1].TimestampMs, w.End) - Math.Max(run[runStart].TimestampMs, w.Begin)));
                var span = rawSpan - pausedOverlap;
                var allowed = clip.DelaysMs[run[runStart].FrameIndex] + DefaultHoldSlackMs;
                if (span > allowed)
                {
                    dupRunViolations.Add(
                        $"clip {run[runStart].ClipId} frame {run[runStart].FrameIndex}: identical run spans {span}ms (raw {rawSpan}ms - paused {pausedOverlap}ms) > hold {allowed}ms");
                }

                runStart = i;
            }

            // Schedule fit on runs long enough to discriminate (ordinal-unwrapped).
            var distinct = run.Select(s => s.FrameIndex).Distinct().Count();
            if (run.Count >= 4 && distinct >= 3)
            {
                fitRuns++;
                var ordinals = new List<(long, int)> { (run[0].TimestampMs, run[0].FrameIndex) };
                for (var i = 1; i < run.Count; i++)
                {
                    var jump = (run[i].FrameIndex - run[i - 1].FrameIndex + frames) % frames;
                    ordinals.Add((run[i].TimestampMs, ordinals[^1].Item2 + jump));
                }

                var residual1x = AvatarSchedule.MaxResidual(ordinals, clip.DelaysMs, 1.0);
                var residual2x = AvatarSchedule.MaxResidual(ordinals, clip.DelaysMs, 0.5);
                var residualHalf = AvatarSchedule.MaxResidual(ordinals, clip.DelaysMs, 2.0);
                if (residual1x <= DefaultScheduleToleranceMs) fit1xPassed++;
                if (residual2x > DefaultScheduleToleranceMs) fit2xRejected++;
                if (residualHalf > DefaultScheduleToleranceMs) fitHalfRejected++;
                fitDetails.Add($"clip {run[0].ClipId} run ({run.Count} samples): 1x={residual1x}ms 2x={residual2x}ms 0.5x={residualHalf}ms");
            }
        }

        yield return new AvatarVerdict(
            "monotonic-modular-advance",
            monotonicViolations.Count == 0,
            monotonicViolations.Count == 0
                ? "every decoded frame is a schedule-bounded forward step (no second-pipeline interleave)"
                : string.Join("; ", monotonicViolations.Take(3)));

        yield return new AvatarVerdict(
            "no-duplicate-run-beyond-hold",
            dupRunViolations.Count == 0,
            dupRunViolations.Count == 0
                ? "no identical-frame run outlives its declared hold"
                : string.Join("; ", dupRunViolations.Take(3)));

        if (fitRuns > 0)
        {
            yield return new AvatarVerdict(
                "schedule-fit-1x",
                fit1xPassed == fitRuns,
                $"{fit1xPassed}/{fitRuns} discriminating runs fit the declared 1x cadence within {DefaultScheduleToleranceMs}ms; {string.Join(" | ", fitDetails)}");
            yield return new AvatarVerdict(
                "schedule-not-2x-speed",
                fit2xRejected == fitRuns,
                $"{fit2xRejected}/{fitRuns} runs REJECT a 2x-speed schedule (multiplied-speed falsified); {string.Join(" | ", fitDetails)}");
            yield return new AvatarVerdict(
                "schedule-not-half-speed",
                fitHalfRejected == fitRuns,
                $"{fitHalfRejected}/{fitRuns} runs REJECT a 0.5x-speed schedule; {string.Join(" | ", fitDetails)}");
        }
    }

    /// <summary>
    /// Resume fast-forward check (packet acceptance): the frozen segment decodes ONE frame
    /// throughout; the first post-resume frame is that frame or its SUCCESSOR (never a
    /// skip/replay); post-pause cadence is the pre-pause cadence shifted by exactly the
    /// pause duration (deadline re-base) — not merely "deltas resume".
    /// </summary>
    private static IEnumerable<AvatarVerdict> EvaluatePauseResume(
        IReadOnlyList<AvatarSample> samples, AvatarPackDef pack,
        AvatarTraceEvent pauseBegin, AvatarTraceEvent pauseEnd)
    {
        var frozen = samples.Where(s => s.TimestampMs >= pauseBegin.TimestampMs && s.TimestampMs <= pauseEnd.TimestampMs && s.Decoded).ToArray();
        var frozenDistinct = frozen.Select(s => (s.ClipId, s.FrameIndex)).Distinct().ToArray();
        yield return new AvatarVerdict(
            "pause-freeze",
            frozen.Length > 0 && frozenDistinct.Length == 1,
            frozen.Length == 0
                ? "no decoded captures inside the pause window"
                : frozenDistinct.Length == 1
                    ? $"paused frame held across {frozen.Length} captures ({pauseEnd.TimestampMs - pauseBegin.TimestampMs}ms)"
                    : $"frame changed DURING pause: {frozenDistinct.Length} distinct frames");

        if (frozenDistinct.Length != 1)
        {
            yield break;
        }

        var pausedFrame = frozenDistinct[0];
        var clip = pack.Clip(pausedFrame.ClipId);
        var successor = (pausedFrame.FrameIndex + 1) % clip.Frames;
        var after = samples.Where(s => s.TimestampMs > pauseEnd.TimestampMs && s.Decoded && s.ClipId == pausedFrame.ClipId).ToArray();
        var firstDistinct = after.Select(s => s.FrameIndex).Distinct().Take(2).ToArray();
        var successorOk = firstDistinct.Length > 0 && firstDistinct[0] is var first && (first == pausedFrame.FrameIndex || first == successor)
            && (firstDistinct.Length < 2 || firstDistinct[1] == (first == pausedFrame.FrameIndex ? successor : (successor + 1) % clip.Frames));
        yield return new AvatarVerdict(
            "resume-successor",
            successorOk,
            successorOk
                ? $"post-resume frames continue the paused frame's successor chain ({string.Join("->", firstDistinct)})"
                : $"post-resume frames {string.Join(",", firstDistinct)} are not the paused frame {pausedFrame.FrameIndex}/successor {successor} chain");

        // Cadence unchanged: pre- and post-pause segments share one schedule shifted by the
        // pause duration (the engine's deadline re-base).
        var before = samples.Where(s => s.TimestampMs < pauseBegin.TimestampMs && s.Decoded && s.ClipId == pausedFrame.ClipId).ToArray();
        if (before.Length >= 3 && after.Length >= 3)
        {
            var ordinals = new List<(long, int)>();
            var ordinal = before[0].FrameIndex;
            ordinals.Add((before[0].TimestampMs, ordinal));
            foreach (var sample in before.Skip(1))
            {
                ordinal += (sample.FrameIndex - ordinals[^1].Item2 % clip.Frames + clip.Frames) % clip.Frames;
                ordinals.Add((sample.TimestampMs, ordinal));
            }

            var pauseDuration = pauseEnd.TimestampMs - pauseBegin.TimestampMs;
            ordinal += (after[0].FrameIndex - ordinals[^1].Item2 % clip.Frames + clip.Frames) % clip.Frames;
            ordinals.Add((after[0].TimestampMs - pauseDuration, ordinal));
            foreach (var sample in after.Skip(1))
            {
                ordinal += (sample.FrameIndex - ordinals[^1].Item2 % clip.Frames + clip.Frames) % clip.Frames;
                ordinals.Add((sample.TimestampMs - pauseDuration, ordinal));
            }

            var residual = AvatarSchedule.MaxResidual(ordinals, clip.DelaysMs, 1.0);
            yield return new AvatarVerdict(
                "cadence-unchanged-after-resume",
                residual <= DefaultScheduleToleranceMs,
                $"pre+post segments fit ONE schedule shifted by the {pauseDuration}ms pause: max residual {residual}ms (tolerance {DefaultScheduleToleranceMs}ms)");
        }
    }

    private static AvatarVerdict EvaluatePackSwitch(IReadOnlyList<AvatarSample> samples, AvatarTraceEvent packSwitch)
    {
        var after = samples.Where(s => s.TimestampMs > packSwitch.TimestampMs && s.Decoded).ToArray();
        var wrongPack = after.Where(s => s.PackId != packSwitch.PackId).ToArray();
        var first = after.FirstOrDefault();
        var startsClean = first is not null && first.FrameIndex == 0;
        var passed = after.Length > 0 && wrongPack.Length == 0 && startsClean;
        return new AvatarVerdict(
            "pack-switch-clean",
            passed,
            passed
                ? $"{after.Length} post-switch captures all decode pack {packSwitch.PackId}, starting at frame 0 — old pack fully disposed (never two avatars)"
                : $"post-switch: {wrongPack.Length} old-pack captures, first frame index {(first is null ? "none" : first.FrameIndex)}");
    }
}
