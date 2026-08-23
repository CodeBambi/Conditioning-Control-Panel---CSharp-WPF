namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The rendered shape of one transient event. <b>TWO of upstream's six, and the count is a decision
/// rather than an omission.</b>
///
/// <para>Upstream's <c>VibrationMode</c> has six members (<c>Models/HapticSettings.cs:12-20</c>) and
/// each is a PER-EVENT SETTING (<c>:57-66</c>) chosen on the haptics tab. <b>This port persists one
/// haptic setting</b> (<see cref="HapticSettingsDocument.Enabled"/>), so every wired site renders at
/// upstream's own shipped default mode and nothing can select another: Flash Images is
/// <c>Constant</c> (<c>HapticSettings.cs:58</c>, <c>_flashDisplayMode</c>), Subliminals and Bouncing
/// Text are <c>Pulse</c> (<c>:62</c>, <c>:65</c>). Wave, Heartbeat, Escalate and Earthquake have
/// <b>no reachable caller here</b>, so they are ABSENT rather than present-and-inert — the same rule
/// <c>HapticSettingsDocument</c> applies to auto-connect, and the same reason: four renderers nothing
/// can reach are four things a reader would have to verify against a device that cannot be driven.
/// They arrive with the settings surface that selects them.</para>
/// </summary>
public enum HapticShape
{
    /// <summary>One shaped hold: attack, the full duration at level, then decay
    /// (<c>Services/Haptics/Core/HapticPatterns.cs:123-129</c>).</summary>
    Constant,

    /// <summary>Repeated taps at the on-time floor with a ~70 ms gap
    /// (<c>HapticPatterns.cs:53-65</c>).</summary>
    Pulse,
}

/// <summary>
/// One transient envelope: a rise, a plateau and a fall, at a priority.
///
/// <para>Ported from upstream's <c>HapticMixer.ActivePulse</c>
/// (<c>Services/Haptics/Core/HapticMixer.cs:1200-1224</c>). <b>The instantaneous level is the whole
/// point of this type</b>: C+ evaluates every live envelope at ONE shared instant and sums within a
/// priority group, which is only meaningful if each envelope can say what it is doing at an arbitrary
/// millisecond rather than only what its peak was.</para>
/// </summary>
/// <param name="Intensity">The crest, 0..1, before any master arithmetic.</param>
/// <param name="AttackMs">Rise from silence to <paramref name="Intensity"/>.</param>
/// <param name="HoldMs">Time at <paramref name="Intensity"/>.</param>
/// <param name="DecayMs">Fall back to silence.</param>
/// <param name="Priority">Upstream's arbitration group. Equal priorities SUM; different priorities
/// take the max (<c>HapticMixer.cs:476-503</c>).</param>
public readonly record struct HapticPulse(
    double Intensity, int AttackMs, int HoldMs, int DecayMs, int Priority)
{
    /// <summary>How long this envelope lasts from its own start.</summary>
    public int TotalMs => AttackMs + HoldMs + DecayMs;

    /// <summary>
    /// The level at <paramref name="elapsedMs"/> after this envelope started, 0 outside it.
    ///
    /// <para><b>The trailing clamp is load-bearing and is upstream's own fix.</b> A shaped pulse
    /// falls out of its decay ramp on its own, but a hard edge (<c>DecayMs == 0</c>) has no ramp to
    /// fall out of, and upstream's extra tick of expiry grace used to render it at FULL level one
    /// tick past its end (<c>HapticMixer.cs:1209-1213</c>). The same clamp is what makes a
    /// zero-length step silent.</para>
    /// </summary>
    public double LevelAt(long elapsedMs)
    {
        var t = elapsedMs;
        if (t < 0 || t >= TotalMs)
        {
            return 0;
        }

        if (t < AttackMs)
        {
            return AttackMs <= 0 ? Intensity : Intensity * (t / (double)AttackMs);
        }

        t -= AttackMs;
        if (t < HoldMs)
        {
            return Intensity;
        }

        t -= HoldMs;
        return t < DecayMs
            ? DecayMs <= 0 ? 0 : Intensity * (1.0 - (t / (double)DecayMs))
            : 0;
    }
}

/// <summary>One step of a rendered pattern: fire <paramref name="Pulse"/> <paramref name="DelayMs"/>
/// after the sequence starts. Upstream's <c>HapticPulseStep</c>
/// (<c>Services/Haptics/Core/HapticMixer.cs:8-16</c>).</summary>
/// <param name="DelayMs">Offset from the sequence's start.</param>
/// <param name="Pulse">The envelope to fire then.</param>
public readonly record struct HapticStep(int DelayMs, HapticPulse Pulse);

/// <summary>One live envelope's level and the priority group it arbitrates in, at one instant.</summary>
/// <param name="Level">The envelope's value at the shared instant.</param>
/// <param name="Priority">Its arbitration group.</param>
public readonly record struct PrioritisedLevel(double Level, int Priority);

/// <summary>
/// Upstream's mixer arithmetic, as pure functions. <b>This is the C+ evaluator</b>: peak-of-sum
/// within a priority group, MAX across groups, then the master multiplier, the perceptible floor and
/// the safety cap — every constant carrying the shipped default it is.
///
/// <para><b>Why the sum matters, in numbers rather than adjectives.</b> The flash decay ladder posts
/// at priority 1 (<c>Services/Haptics/HapticService.cs:786</c>) and the subliminal pulse posts at
/// priority 1 (<c>:880</c>), and both are wired here. Two overlapping 0.5 transients SUM to 1.0,
/// scale by 0.7 and hit the 0.70 cap; taking the MAX instead gives 0.5, scales to 0.35 and does not
/// — <b>a factor of two on the level a person feels</b>. Bounces post at priority 0
/// (<c>:821</c>) and sum with each other the same way, which is a second reachable instance of the
/// same rule inside a single module.</para>
///
/// <para><b>There is no 10 Hz loop here and that is deliberate.</b> Upstream's loop exists because it
/// is the SENDER: it rate-limits the Lovense LAN API, re-asserts unchanged levels and heartbeats
/// zeros (<c>HapticMixer.cs:68-97</c>). Those are DELIVERY properties, and the sink's seam already puts
/// delivery on the provider (<c>Services/Haptics/Core/HapticContracts.cs:70-73</c>). What is left is
/// arithmetic, and arithmetic needs an instant, not a poll.</para>
/// </summary>
public static class HapticMix
{
    /// <summary>Lowest intensity that still maps to a real vibration level
    /// (<c>HapticMixer.cs:83</c>). 0.06 rather than 0.05 because <c>LovenseProvider</c> treats
    /// <c>&lt;= 0.05</c> as "off".</summary>
    public const double MinPerceptibleIntensity = 0.06;

    /// <summary>The master multiplier. Upstream's <c>GlobalIntensity</c>, shipped at 0.7
    /// (<c>Models/HapticSettings.cs:29</c>), read by <c>HapticMixer.MasterIntensity</c>
    /// (<c>:215</c>).</summary>
    public const double MasterIntensity = 0.7;

    /// <summary>The hard safety ceiling on any single actuator, applied AFTER the master multiplier
    /// (<c>HapticMixer.cs:77</c>, <c>:516</c>). <i>"full power is opt-in, not the accident you
    /// discover mid-session"</i> (<c>Models/HapticSettings.cs:784-786</c>).</summary>
    public const double MasterCap = 0.70;

    /// <summary>How many transient envelopes may overlap before the weakest is evicted
    /// (<c>HapticMixer.cs:79</c>).</summary>
    public const int MaxConcurrentPulses = 4;

    /// <summary>Time for the continuous floor to climb from silence to full
    /// (<c>HapticMixer.cs:75</c>). <i>"Safety, not taste: a session that resumes at 100% with no ramp
    /// is how people get hurt."</i></summary>
    public const int SoftRampMs = 800;

    /// <summary>Upstream's output cadence (<c>HapticMixer.cs:70</c>). <b>Not a loop here</b>: it is
    /// the resolution at which a ramp segment is sampled, and the minimum slew grant one evaluation
    /// makes, so the shape a person feels is the shape upstream produces.</summary>
    public const int TickMs = 100;

    /// <summary>
    /// The transient contribution at ONE instant: sum within each priority group, clamp the group to
    /// 1, then take the MAX across groups. Upstream's <c>BuildOutputs</c>
    /// (<c>HapticMixer.cs:476-503</c>), with the peak-over-the-tick-window collapsed to the instant
    /// because this evaluator is invoked AT the instants that matter rather than once per tick.
    /// </summary>
    public static double Transient(IReadOnlyList<PrioritisedLevel> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        double transient = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            // Upstream's own allocation-free distinct scan: the list is capped at four, so an
            // O(n^2) pass beats a HashSet (HapticMixer.cs:643-652).
            var priority = samples[i].Priority;
            var seen = false;
            for (var j = 0; j < i; j++)
            {
                if (samples[j].Priority == priority)
                {
                    seen = true;
                    break;
                }
            }

            if (seen)
            {
                continue;
            }

            double groupSum = 0;
            for (var k = 0; k < samples.Count; k++)
            {
                if (samples[k].Priority == priority)
                {
                    groupSum += samples[k].Level;
                }
            }

            if (groupSum > 1)
            {
                groupSum = 1;
            }

            if (groupSum > transient)
            {
                transient = groupSum;
            }
        }

        return transient;
    }

    /// <summary>
    /// The master multiplier, the perceptible floor and the safety cap, in upstream's order
    /// (<c>HapticMixer.cs:509-518</c>).
    ///
    /// <para><b>The floor is applied BEFORE the cap and that order is upstream's own bug fix</b>: the
    /// cap is a SAFETY ceiling and must be inviolable, and a 0.05 cap used to emit 0.06 when the
    /// floor ran last. The per-device trim upstream applies afterwards has no analogue here — this
    /// port has no per-device configuration, and inventing one would be a setting nothing reads.</para>
    /// </summary>
    public static double Finish(double raw)
    {
        var v = raw * MasterIntensity;
        if (raw > 0)
        {
            v = Math.Max(v, MinPerceptibleIntensity);
        }

        v = Math.Min(v, MasterCap);
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>The continuous floor and the transients combined, then finished. Upstream's
    /// <c>raw = Math.Max(state.Floor, transient)</c> (<c>HapticMixer.cs:506</c>) — transients ride
    /// OVER the floor rather than replacing it.</summary>
    public static double Combine(double floor, double transient) =>
        Finish(Math.Max(floor, transient));
}

/// <summary>
/// Upstream's six user-facing modes, rendered. Two of them (see <see cref="HapticShape"/>).
///
/// <para>Ported from <c>Services/Haptics/Core/HapticPatterns.cs:42-134</c>, including the on-time
/// floor that is the reason the whole file exists: <i>"an ERM takes roughly 100-150 ms to spin up
/// from rest to a speed anyone can feel … The commands were correct, they reached the toy, and the
/// toy never actually moved"</i> (<c>:18-34</c>).</para>
/// </summary>
public static class HapticShapes
{
    /// <summary>No rendered pulse asks the hardware for less on-time than this
    /// (<c>HapticPatterns.cs:36</c>). A floor, never a cap.</summary>
    public const int MinFeltOnMs = 130;

    /// <summary>Shortest whole pattern worth rendering — the on-time floor plus room for the attack
    /// and decay around it. Anything below is rounded UP (<c>HapticPatterns.cs:40</c>, <c>:47</c>).</summary>
    public const int MinFeltDurationMs = 200;

    /// <summary>Upstream's upper clamp on a rendered duration (<c>HapticPatterns.cs:47</c>).</summary>
    public const int MaxDurationMs = 60_000;

    /// <summary>The silent gap between <see cref="HapticShape.Pulse"/> taps
    /// (<c>HapticPatterns.cs:59</c>).</summary>
    public const int PulseGapMs = 70;

    /// <summary>Render one event into the steps a mixer plays. Upstream's
    /// <c>HapticPatterns.Render</c> (<c>:42-134</c>); the <c>ToyRole</c> target it also carries has
    /// no analogue here, because the port's seam addresses devices and not roles.</summary>
    public static IReadOnlyList<HapticStep> Render(
        HapticShape shape, double intensity, int durationMs, int priority)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        // Floor, not clamp-to-20: "a 60 ms request is not a short buzz, it is silence"
        // (HapticPatterns.cs:46-47).
        durationMs = Math.Clamp(durationMs, MinFeltDurationMs, MaxDurationMs);
        var steps = new List<HapticStep>();
        if (intensity <= 0)
        {
            return steps;
        }

        switch (shape)
        {
            case HapticShape.Pulse:
            {
                var period = MinFeltOnMs + PulseGapMs;
                var count = Math.Clamp(durationMs / period, 1, 40);
                for (var i = 0; i < count; i++)
                {
                    steps.Add(new HapticStep(
                        i * period, new HapticPulse(intensity, 8, MinFeltOnMs, 20, priority)));
                }

                break;
            }

            case HapticShape.Constant:
            default:
            {
                var attack = Math.Clamp(durationMs / 6, 10, 60);
                var decay = Math.Clamp(durationMs / 4, 30, 150);
                steps.Add(new HapticStep(
                    0, new HapticPulse(intensity, attack, durationMs, decay, priority)));
                break;
            }
        }

        return steps;
    }

    /// <summary>Total wall time of a rendered sequence, including the last envelope's tail.
    /// Upstream's <c>TotalMs</c> (<c>HapticPatterns.cs:136-146</c>).</summary>
    public static int SpanMs(IReadOnlyList<HapticStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var end = 0;
        foreach (var step in steps)
        {
            var e = step.DelayMs + step.Pulse.TotalMs;
            if (e > end)
            {
                end = e;
            }
        }

        return end;
    }

    /// <summary>Offset a rendered sequence so several can be chained into one — upstream's
    /// <c>Append</c> (<c>HapticPatterns.cs:148-153</c>), and the thing that builds a decay
    /// ladder.</summary>
    public static void Append(List<HapticStep> into, IReadOnlyList<HapticStep> steps, int offsetMs)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(steps);
        foreach (var step in steps)
        {
            into.Add(new HapticStep(step.DelayMs + offsetMs, step.Pulse));
        }
    }
}

/// <summary>
/// One envelope per WIRED site, each carrying upstream's own arithmetic at upstream's own shipped
/// defaults. <b>Every number here is a citation, not a taste.</b>
///
/// <para><b>Where the intensities come from, given that this port has no dials.</b> Upstream's
/// per-event rows default to <c>Intensity = 0.5</c> (<c>Models/HapticSettings.cs:742</c>) and are
/// seeded from the legacy fields, which are also 0.5 for all three wired events
/// (<c>:46</c> flash, <c>:50</c> subliminal, <c>:53</c> bouncing text). The video slider is 0.5
/// (<c>:48</c>). So the shipped-default user feels exactly what this class produces, and the day this
/// port grows the routing surface these become its defaults rather than its constants.</para>
/// </summary>
public static class HapticEnvelopes
{
    /// <summary>Upstream's shipped flash-display intensity (<c>Models/HapticSettings.cs:46</c>),
    /// through <c>GetSliderIntensity</c> (<c>HapticService.cs:439-440</c>), which leaves 0.5
    /// alone.</summary>
    public const double FlashIntensity = 0.5;

    /// <summary>The ladder's per-rung decay factor (<c>HapticService.cs:784</c>).</summary>
    public const double LadderDecay = 0.7;

    /// <summary>Rungs in the ladder (<c>HapticService.cs:782</c>).</summary>
    public const int LadderRungs = 8;

    /// <summary>Milliseconds between rung starts (<c>HapticService.cs:787</c>).</summary>
    public const int LadderSpacingMs = 450;

    /// <summary>Each rung's requested duration (<c>HapticService.cs:786</c>).</summary>
    public const int LadderRungDurationMs = 250;

    /// <summary>The ladder's arbitration group (<c>HapticService.cs:786</c>) — the same one the
    /// subliminal pulse posts at, which is why peak-of-sum is reachable in this build.</summary>
    public const int LadderPriority = 1;

    /// <summary>Upstream's shipped subliminal intensity (<c>Models/HapticSettings.cs:50</c>).</summary>
    public const double SubliminalIntensity = 0.5;

    /// <summary>The subliminal pulse's arbitration group (<c>HapticService.cs:880</c>).</summary>
    public const int SubliminalPriority = 1;

    /// <summary>Upstream's shipped bouncing-text intensity (<c>Models/HapticSettings.cs:53</c>).</summary>
    public const double BounceIntensity = 0.5;

    /// <summary>The bounce's requested duration (<c>HapticService.cs:821</c>). Below the on-time
    /// floor, so <see cref="HapticShapes.Render"/> raises it — which is the defect
    /// <c>HapticPatterns.cs:18-34</c> was written to fix.</summary>
    public const int BounceDurationMs = 60;

    /// <summary>The bounce's arbitration group (<c>HapticService.cs:821</c>): 0, so it never sums
    /// with a flash or a subliminal — but it does sum with ANOTHER bounce.</summary>
    public const int BouncePriority = 0;

    /// <summary>Upstream's shipped video slider (<c>Models/HapticSettings.cs:48</c>).</summary>
    public const double VideoSlider = 0.5;

    /// <summary>The background vibe is a tenth of the slider <i>"so target hits feel impactful"</i>
    /// (<c>HapticService.cs:846-847</c>). The port has no target hits (census sites 10 and 11 are
    /// absent-by-decision), and the fraction is kept anyway: it is what the shipped-default user
    /// feels during a clip, and changing it because half its rationale is unported would be
    /// re-tuning rather than porting.</summary>
    public const double VideoBackgroundFraction = 0.1;

    /// <summary>
    /// Sites 1-3 — the flash decay ladder (<c>Services/Haptics/HapticService.cs:781-788</c>).
    ///
    /// <para>Eight rungs of <c>max(start · 0.7^i, 0.06)</c> at 450 ms spacing, each a 250 ms
    /// <see cref="HapticShape.Constant"/> render. In the shipped mode that renders as
    /// <c>41 + 250 + 62 = 353</c> ms, so the ladder spans <b>3503 ms</b> — <b>not</b> the <i>"decays
    /// over ~2s"</i> its own doc comment claims (<c>:761</c>). Code wins (D205).</para>
    /// </summary>
    public static IReadOnlyList<HapticStep> FlashDecayLadder(double start = FlashIntensity)
    {
        var steps = new List<HapticStep>();
        if (start <= 0)
        {
            // "slider at 0 = silence, not a floor buzz" (HapticService.cs:779). The perceptible
            // floor below must never turn a deliberate zero into a 6 % hum.
            return steps;
        }

        for (var i = 0; i < LadderRungs; i++)
        {
            var intensity = Math.Max(start * Math.Pow(LadderDecay, i), HapticMix.MinPerceptibleIntensity);
            HapticShapes.Append(
                steps,
                HapticShapes.Render(HapticShape.Constant, intensity, LadderRungDurationMs, LadderPriority),
                i * LadderSpacingMs);
        }

        return steps;
    }

    /// <summary>
    /// Sites 14-15 — the duration a subliminal pulse asks for, keyed off the phrase's own wording
    /// (<c>HapticService.cs:899-909</c>).
    ///
    /// <para><b>The Buttplug multiplier is 1.0 and that is a fact about this build, not a
    /// simplification.</b> Upstream doubles every one of these <i>"due to protocol overhead"</i>
    /// when the active provider is Buttplug (<c>:903</c>); <see cref="HapticSinkFactory.AdmittedRoutes"/>
    /// carries Lovense and NOT Buttplug, so the Buttplug provider is never the active one here and the
    /// multiplier cannot be anything but one. It returns the day the Buttplug route is admitted.</para>
    /// </summary>
    public static int SubliminalDurationMs(string? phrase)
    {
        var lower = (phrase ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("cum", StringComparison.Ordinal)
            || lower.Contains("collapse", StringComparison.Ordinal)
            || lower.Contains("drop", StringComparison.Ordinal))
        {
            return 250;
        }

        return lower.Contains("freeze", StringComparison.Ordinal)
            || lower.Contains("zap", StringComparison.Ordinal)
            ? 120
            : 150;
    }

    /// <summary>
    /// Sites 14-15 — one short pulse per card (<c>HapticService.cs:877-881</c> through
    /// <c>PostEvent</c> at <c>:372-392</c>).
    ///
    /// <para><b>A finding worth stating where it is true rather than only in a record:</b> all three
    /// durations <see cref="SubliminalDurationMs"/> can return (250, 150, 120) render IDENTICALLY in
    /// the shipped <see cref="HapticShape.Pulse"/> mode, because every one of them yields
    /// <c>clamp(duration/200, 1, 40) == 1</c> tap. The wording only changes what a person feels once
    /// the duration reaches 400 ms, which no phrase produces. The arithmetic is ported exactly
    /// anyway, because it is the arithmetic, and because a mode change makes it bite.</para>
    /// </summary>
    public static IReadOnlyList<HapticStep> SubliminalPulse(string? phrase) =>
        HapticShapes.Render(
            HapticShape.Pulse, SubliminalIntensity, SubliminalDurationMs(phrase), SubliminalPriority);

    /// <summary>Site 18 — one brief sharp pulse when bouncing text hits a screen edge
    /// (<c>HapticService.cs:819-821</c>).</summary>
    public static IReadOnlyList<HapticStep> BouncePulse() =>
        HapticShapes.Render(HapticShape.Pulse, BounceIntensity, BounceDurationMs, BouncePriority);

    /// <summary>
    /// Sites 6-7 — the continuous level a playing clip holds
    /// (<c>HapticService.cs:832-851</c>).
    ///
    /// <para>At the shipped slider this is <c>max(0.5 · 0.1, 0.06) = 0.06</c>: the perceptible floor
    /// wins over a tenth of the slider. A slider at zero is SILENCE and not the floor
    /// (<c>:836-843</c>), which is why the zero arm is explicit.</para>
    /// </summary>
    public static double VideoBackgroundLevel(double slider = VideoSlider) =>
        slider <= 0
            ? 0
            : Math.Max(slider * VideoBackgroundFraction, HapticMix.MinPerceptibleIntensity);
}
