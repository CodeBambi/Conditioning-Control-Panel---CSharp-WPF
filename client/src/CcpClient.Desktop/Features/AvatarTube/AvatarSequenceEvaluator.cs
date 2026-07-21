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
    /// Least-squares SPEED estimate of observed samples against the declared schedule:
    /// fits <c>t = phase + speed · cum(ordinal)</c> and returns the speed (1.0 = declared
    /// cadence, 0.5 = doubled speed, 2.0 = halved). Within-hold capture quantization is
    /// ZERO-MEAN noise on the slope (unlike max-residual phase fits, whose error grows with
    /// the clip's longest hold — the 1400ms pose hold produced 704ms phantom residuals
    /// under a residual fit, SP-015 Step 4 finding). Non-uniform delays keep the slope
    /// identifiable; capture-timestamp jitter contributes ~jitter/span (≤0.07 at span ≥3s).
    /// </summary>
    public static double SpeedEstimate(
        IReadOnlyList<(long TimestampMs, int Ordinal)> samples, IReadOnlyList<int> delaysMs)
    {
        var cum = Cumulative(delaysMs);
        var n = samples.Count;
        double xMean = 0, yMean = 0;
        var xs = new double[n];
        for (var i = 0; i < n; i++)
        {
            xs[i] = CumulativeAt(delaysMs, cum, samples[i].Ordinal);
            xMean += xs[i];
            yMean += samples[i].TimestampMs;
        }

        xMean /= n;
        yMean /= n;
        double sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - xMean;
            sxx += dx * dx;
            sxy += dx * (samples[i].TimestampMs - yMean);
        }

        return sxx <= 0 ? double.NaN : sxy / sxx;
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
    double ContentCentroidY,
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
    /// Schedule-fit windows on the estimated speed (see AvatarSchedule.SpeedEstimate for why
    /// a slope, not a max-residual). 1x acceptance ±0.20 absorbs capture-period aliasing on
    /// short runs (a ~264ms capture cadence against 480-820ms holds produces within-hold
    /// placement bias up to ±0.17 on 4-ordinal runs — Step-4 finding) while staying
    /// decisively clear of the falsified speeds: the 2x window [0.35,0.65] and the 0.5x
    /// window [1.7,2.3] bracket the defect speeds with a >=0.15 margin on every side.
    /// Runs need >= 8 samples and >= 4 distinct ordinals to discriminate; shorter runs emit
    /// NO schedule verdicts (honest silence, never a phantom pass/fail).
    /// </summary>
    public const double SpeedFit1xTolerance = 0.20;
    public const int SpeedFitMinSamples = 8;
    public const int SpeedFitMinDistinctOrdinals = 4;
    public const double TwoSpeedWindowMin = 0.35;
    public const double TwoSpeedWindowMax = 0.65;
    public const double HalfSpeedWindowMin = 1.7;
    public const double HalfSpeedWindowMax = 2.3;
    /// <summary>
    /// Blank threshold on content fraction (undecoded captures only — the union rule).
    /// 0.02 sits below the SPARSEST legitimate frame (reaction art ≈ 0.033, generator's
    /// 13-block radiating pattern + scan mark) and above a true blank (uniform stage bg
    /// renders exactly 0.000 in lossless BMP captures) — both margins verified in Step-4
    /// headed evidence (G4 false positive at the old 0.05, fraction 0.031).
    /// </summary>
    public const double DefaultBlankFractionThreshold = 0.02;
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
        verdicts.Add(EvaluateFloatLiveness(samples, trace));
        verdicts.AddRange(EvaluateRuns(samples, pack, trace));

        // Pause/pack-switch verdicts bind to events INSIDE the samples' window (+3s pre-
        // margin for the event driving what follows it): the trace is session-global but
        // each evaluation is a time slice — a stale pause from an earlier gate must never
        // be judged against this slice's samples (Step-4 finding: G7's pause phantom-
        // FAILED G8's evaluation with "no decoded captures inside the pause window").
        const long preMarginMs = 3000;
        var windowBegin = samples.Min(s => s.TimestampMs) - preMarginMs;
        var windowEnd = samples.Max(s => s.TimestampMs);
        var pauseBegin = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PauseBegin
            && e.TimestampMs >= windowBegin && e.TimestampMs <= windowEnd);
        var pauseEnd = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PauseEnd
            && e.TimestampMs >= windowBegin && e.TimestampMs <= windowEnd);
        if (pauseBegin is not null && pauseEnd is not null)
        {
            verdicts.AddRange(EvaluatePauseResume(samples, pack, pauseBegin, pauseEnd));
        }

        var packSwitch = trace.LastOrDefault(e => e.Kind == AvatarTraceEvent.PackSwitch
            && e.TimestampMs >= windowBegin && e.TimestampMs <= windowEnd);
        if (packSwitch is not null)
        {
            verdicts.Add(EvaluatePackSwitch(samples, packSwitch));
        }

        return verdicts;
    }

    /// <summary>
    /// Gentle-float liveness, measured WITHIN one rendered identity: samples group by
    /// (pack, clip, frame); inside a group the strip/content is identical, so centroid
    /// motion is the float transform only — never pose art changes (Step-4 finding: raw
    /// cross-frame centroids conflate art with float, 14.3px phantom against a 12px bound).
    /// Dip/crossfade-edge captures of the same identity show LESS content and are excluded
    /// (relative-content floor). Alive = some group oscillates >= 1px; bounded = every
    /// group stays inside 2x the demonstrator amplitude + capture tolerance.
    /// </summary>
    private static AvatarVerdict EvaluateFloatLiveness(IReadOnlyList<AvatarSample> samples, IReadOnlyList<AvatarTraceEvent> trace)
    {
        const double amplitudeBound = 4.0 + 2.0;
        // Paused windows freeze the float legitimately — frozen samples are excluded.
        var pauseWindows = PauseWindows(trace);
        var groups = samples
            .Where(s => s.Decoded && s.ContentCentroidY >= 0
                && !pauseWindows.Any(w => s.TimestampMs >= w.Begin && s.TimestampMs <= w.End))
            .GroupBy(s => (s.PackId, s.ClipId, s.FrameIndex));
        var ranges = new List<double>();
        foreach (var group in groups)
        {
            if (group.Count() < 2)
            {
                continue;
            }

            var maxContent = group.Max(s => s.ContentFraction);
            var pure = group.Where(s => s.ContentFraction >= 0.8 * maxContent).Select(s => s.ContentCentroidY).ToArray();
            if (pure.Length >= 2)
            {
                ranges.Add(pure.Max() - pure.Min());
            }
        }

        if (ranges.Count == 0)
        {
            return new AvatarVerdict("float-liveness", false, "no frame identity captured twice — float motion not measurable");
        }

        var maxRange = ranges.Max();
        var bounded = ranges.All(r => r <= 2 * amplitudeBound);
        var passed = maxRange >= 1.0 && bounded;
        return new AvatarVerdict(
            "float-liveness",
            passed,
            passed
                ? $"same-frame content centroid oscillates up to {maxRange:F1}px across {ranges.Count} frame group(s), all within the ±{amplitudeBound}px float bound"
                : $"same-frame centroid ranges [{string.Join(", ", ranges.Select(r => r.ToString("F1")))}]px (need max >= 1px alive, all <= {2 * amplitudeBound}px bounded)");
    }

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

    private static List<(long Begin, long End)> PauseWindows(IReadOnlyList<AvatarTraceEvent> trace)
    {
        var windows = new List<(long Begin, long End)>();
        AvatarTraceEvent? pending = null;
        foreach (var e in trace)
        {
            if (e.Kind == AvatarTraceEvent.PauseBegin)
            {
                pending = e;
            }
            else if (e.Kind == AvatarTraceEvent.PauseEnd && pending is not null)
            {
                windows.Add((pending.TimestampMs, e.TimestampMs));
                pending = null;
            }
        }

        return windows;
    }

    /// <summary>Per-run checks on maximal same-(pack,clip) segments.</summary>
    private static IEnumerable<AvatarVerdict> EvaluateRuns(
        IReadOnlyList<AvatarSample> samples, AvatarPackDef pack, IReadOnlyList<AvatarTraceEvent> trace)
    {
        // Pauses legitimately freeze a frame: pause windows are subtracted from run spans
        // (a frozen frame is not a duplicate-run defect).
        var pauseWindows = PauseWindows(trace);
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
            // Foreign-pack runs (a pack-switch sequence evaluated against ONE def) carry a
            // different declared schedule — per-run checks against this def are meaningless;
            // the pack-switch-clean verdict judges them (Step-4 finding: pulse's run judged
            // against circuit's delays phantom-failed dup-run + schedule-fit).
            if (run[0].PackId != pack.PackId)
            {
                continue;
            }

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

            // Schedule fit on runs long enough to discriminate (ordinal-unwrapped). Runs
            // spanning a PAUSE are excluded: the pause gap inflates wall time without frame
            // progress (Step-4 finding: pause-spanning run measured speed 2.22) — pause-
            // shifted cadence is the cadence-unchanged-after-resume verdict's job.
            var distinct = run.Select(s => s.FrameIndex).Distinct().Count();
            var spansPause = pauseWindows.Any(w => run[^1].TimestampMs > w.Begin && run[0].TimestampMs < w.End);
            if (!spansPause && run.Count >= SpeedFitMinSamples && distinct >= SpeedFitMinDistinctOrdinals)
            {
                fitRuns++;
                var ordinals = new List<(long, int)> { (run[0].TimestampMs, run[0].FrameIndex) };
                for (var i = 1; i < run.Count; i++)
                {
                    var jump = (run[i].FrameIndex - run[i - 1].FrameIndex + frames) % frames;
                    ordinals.Add((run[i].TimestampMs, ordinals[^1].Item2 + jump));
                }

                var speed = AvatarSchedule.SpeedEstimate(ordinals, clip.DelaysMs);
                if (Math.Abs(speed - 1.0) <= SpeedFit1xTolerance) fit1xPassed++;
                if (double.IsNaN(speed) || speed < TwoSpeedWindowMin || speed > TwoSpeedWindowMax) fit2xRejected++;
                if (double.IsNaN(speed) || speed < HalfSpeedWindowMin || speed > HalfSpeedWindowMax) fitHalfRejected++;
                fitDetails.Add($"clip {run[0].ClipId} run ({run.Count} samples): speed={speed:F3}");
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
                $"{fit1xPassed}/{fitRuns} discriminating runs fit the declared 1x cadence (speed within ±{SpeedFit1xTolerance:F2}); {string.Join(" | ", fitDetails)}");
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
        var maxGap = 2L * clip.DelaysMs.Max() + DefaultHoldSlackMs;
        var after = PauseAdjacentRun(samples, pausedFrame.ClipId, pauseEnd.TimestampMs, forward: true, maxGap);
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
        // pause duration (the engine's deadline re-base). PAUSE-ADJACENT contiguous runs
        // only: samples captured seconds away from the pause belong to another pass/rotation
        // and make the ordinal bridge invalid (Step-4 finding: phantom speed 4.16 from a
        // 7.5s stale pre-pause segment; the capture-first script order is the harness fix,
        // this adjacency bound is the evaluator guard).
        var before = PauseAdjacentRun(samples, pausedFrame.ClipId, pauseBegin.TimestampMs, forward: false, maxGap);
        var distinctOrdinals = before.Select(s => s.FrameIndex).Concat(after.Select(s => s.FrameIndex)).Distinct().Count();
        if (before.Length >= 3 && after.Length >= 2 && before.Length + after.Length >= 6 && distinctOrdinals >= 4)
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

            var speed = AvatarSchedule.SpeedEstimate(ordinals, clip.DelaysMs);
            yield return new AvatarVerdict(
                "cadence-unchanged-after-resume",
                Math.Abs(speed - 1.0) <= SpeedFit1xTolerance,
                $"pre+post segments fit ONE schedule shifted by the {pauseDuration}ms pause: speed {speed:F3} (1x within ±{SpeedFit1xTolerance:F2})");
        }
        else
        {
            yield return new AvatarVerdict(
                "cadence-unchanged-after-resume",
                false,
                $"insufficient pause-adjacent coverage: {before.Length} before (need >=3) / {after.Length} after (need >=2) samples, {distinctOrdinals} distinct ordinals (need >=4) within {maxGap}ms of the pause");
        }
    }

    /// <summary>
    /// The maximal contiguous same-clip decoded run adjacent to a boundary: the first/last
    /// sample must lie within <paramref name="maxGap"/> of the boundary and consecutive
    /// samples within <paramref name="maxGap"/> of each other — anything beyond belongs to
    /// another pass and must never enter the cadence bridge.
    /// </summary>
    private static AvatarSample[] PauseAdjacentRun(
        IReadOnlyList<AvatarSample> samples, int clipId, long boundaryMs, bool forward, long maxGap)
    {
        var adjacent = samples
            .Where(s => s.Decoded && s.ClipId == clipId
                && (forward ? s.TimestampMs > boundaryMs : s.TimestampMs < boundaryMs))
            .OrderBy(s => s.TimestampMs)
            .ToArray();
        var run = new List<AvatarSample>();
        if (forward)
        {
            foreach (var sample in adjacent)
            {
                var anchor = run.Count == 0 ? boundaryMs : run[^1].TimestampMs;
                if (sample.TimestampMs - anchor > maxGap)
                {
                    break;
                }

                run.Add(sample);
            }
        }
        else
        {
            foreach (var sample in adjacent.Reverse())
            {
                var anchor = run.Count == 0 ? boundaryMs : run[^1].TimestampMs;
                if (anchor - sample.TimestampMs > maxGap)
                {
                    break;
                }

                run.Add(sample);
            }

            run.Reverse();
        }

        return run.ToArray();
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
