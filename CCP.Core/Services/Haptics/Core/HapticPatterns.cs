using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Haptics.Core
{
    /// <summary>
    /// Renders the six user-facing <see cref="VibrationMode"/>s into mixer envelope sequences.
    ///
    /// This is where "all six modes feel like the same mush" gets fixed. The old code sent one
    /// Lovense command per pattern step, where a 200 ms client-side throttle dropped anything
    /// faster than 5 Hz and <c>Math.Max(1, durationMs/1000)</c> rounded every duration UP to a
    /// full second — so a 50 ms Pulse tick and a 300 ms Constant both became "buzz for 1 s".
    /// Envelopes are evaluated by the mixer at 10 Hz against a level-set provider, so attack and
    /// decay actually reach the toy.
    /// </summary>
    public static class HapticPatterns
    {
        /// <summary>
        /// THE ON-TIME FLOOR (#955, re-report of #896). A vibration motor is a physical object
        /// with inertia: an ERM takes roughly 100-150 ms to spin up from rest to a speed anyone
        /// can feel, and about as long to coast back down. Every discrete event in this app was
        /// authored in software time, not motor time - <c>BouncingTextBounce</c> asked for 60 ms,
        /// <c>BubblePop</c> and <c>VideoTargetHit</c> for 100 ms, and Pulse mode chopped whatever
        /// it was given into 50 ms taps. The commands were correct, they reached the toy, and the
        /// toy never actually moved. That is precisely the shape of the report: "haptics still
        /// pretty much not working EXCEPT for constant during video play and some test buttons" -
        /// i.e. everything long works, everything short does not.
        ///
        /// <para>So no rendered pulse asks the hardware for less on-time than this. It is a floor,
        /// never a cap: a caller asking for 2 s still gets 2 s.</para>
        ///
        /// <para>It is also comfortably above the mixer's 100 ms evaluation period, which means a
        /// pulse can no longer live and die inside a single tick and reach the toy as one lonely
        /// sample of an envelope it never got to climb.</para>
        /// </summary>
        public const int MinFeltOnMs = 130;

        /// <summary>Shortest whole pattern worth rendering - the on-time floor plus room for the
        /// attack and decay that surround it. Anything below this is rounded UP to it.</summary>
        public const int MinFeltDurationMs = 200;

        public static IReadOnlyList<HapticPulseStep> Render(
            VibrationMode mode, double intensity, int durationMs, int priority = 0, ToyRole target = ToyRole.All)
        {
            intensity = Math.Clamp(intensity, 0, 1);
            // Floor, not clamp-to-20 (#955): a 60 ms request is not a short buzz, it is silence.
            durationMs = Math.Clamp(durationMs, MinFeltDurationMs, 60_000);
            var steps = new List<HapticPulseStep>();
            if (intensity <= 0) return steps;

            switch (mode)
            {
                case VibrationMode.Pulse:
                {
                    // Quick on/off taps. The tap was 50 ms on / 30 ms off, which is the right
                    // RHYTHM and an unrenderable on-time - the motor never left standstill. Same
                    // shape, widened to the floor: MinFeltOnMs on, ~70 ms off. Still reads as
                    // taps, now actually felt.
                    const int gapMs = 70;
                    var period = MinFeltOnMs + gapMs;
                    var count = Math.Clamp(durationMs / period, 1, 40);
                    for (int i = 0; i < count; i++)
                        steps.Add(new HapticPulseStep(i * period,
                            new HapticPulse(intensity, 8, MinFeltOnMs, 20, priority, target)));
                    break;
                }

                case VibrationMode.Wave:
                {
                    // One smooth swell: half the duration up, half back down. The floor applies to
                    // each half so the crest is actually reached rather than skimmed.
                    var half = Math.Max(MinFeltOnMs, durationMs / 2);
                    steps.Add(new HapticPulseStep(0, new HapticPulse(intensity, half, 0, half, priority, target)));
                    break;
                }

                case VibrationMode.Heartbeat:
                {
                    // ba-BUMP ... ba-BUMP: a strong beat then a lighter one, 400 ms apart. Both
                    // beats were under the on-time floor (70 ms and 50 ms), so the whole pattern
                    // was inaudible to the hardware; the 400 ms spacing and the 140 ms offset
                    // between the two beats are what carry the rhythm, and both are untouched.
                    var beats = Math.Clamp(durationMs / 400, 1, 20);
                    for (int i = 0; i < beats; i++)
                    {
                        var t = i * 400;
                        steps.Add(new HapticPulseStep(t,
                            new HapticPulse(intensity, 10, MinFeltOnMs, 30, priority, target)));
                        steps.Add(new HapticPulseStep(t + 140,
                            new HapticPulse(intensity * 0.7, 10, MinFeltOnMs, 30, priority, target)));
                    }
                    break;
                }

                case VibrationMode.Escalate:
                {
                    // 20% -> 100% of the set intensity across the duration. The rung COUNT now
                    // falls out of the duration instead of being fixed at 8: eight rungs over a
                    // 300 ms event gave 37 ms slices, i.e. eight steps of nothing. Short events
                    // get fewer, felt rungs; long ones still get the full eight-step climb.
                    var n = Math.Clamp(durationMs / MinFeltOnMs, 1, 8);
                    var slice = Math.Max(MinFeltOnMs, durationMs / n);
                    for (int i = 1; i <= n; i++)
                        steps.Add(new HapticPulseStep((i - 1) * slice,
                            new HapticPulse(intensity * (0.2 + 0.8 * (i / (double)n)), 10, slice, 20, priority, target)));
                    break;
                }

                case VibrationMode.Earthquake:
                {
                    // Random 30-100% jolts. Random.Shared is thread-safe (net6+). A "jolt" the
                    // motor cannot reach is not a jolt, so each one holds for the floor and the
                    // spacing widens to match.
                    const int joltPeriodMs = MinFeltOnMs + 40;
                    var count = Math.Clamp(durationMs / joltPeriodMs, 2, 60);
                    for (int i = 0; i < count; i++)
                        steps.Add(new HapticPulseStep(i * joltPeriodMs,
                            new HapticPulse(intensity * (0.3 + Random.Shared.NextDouble() * 0.7),
                                            5, MinFeltOnMs, 25, priority, target)));
                    break;
                }

                case VibrationMode.Constant:
                default:
                {
                    var attack = Math.Clamp(durationMs / 6, 10, 60);
                    var decay = Math.Clamp(durationMs / 4, 30, 150);
                    steps.Add(new HapticPulseStep(0, new HapticPulse(intensity, attack, durationMs, decay, priority, target)));
                    break;
                }
            }

            return steps;
        }

        /// <summary>Total wall time of a rendered sequence, including the last envelope's tail.</summary>
        public static int TotalMs(IReadOnlyList<HapticPulseStep> steps)
        {
            int end = 0;
            foreach (var s in steps)
            {
                var e = s.DelayMs + s.Pulse.AttackMs + s.Pulse.HoldMs + s.Pulse.DecayMs;
                if (e > end) end = e;
            }
            return end;
        }

        /// <summary>Offset a rendered sequence so several patterns can be chained into one
        /// sequence (celebration patterns, flash decay ladders).</summary>
        public static void Append(List<HapticPulseStep> into, IReadOnlyList<HapticPulseStep> steps, int offsetMs)
        {
            foreach (var s in steps) into.Add(new HapticPulseStep(s.DelayMs + offsetMs, s.Pulse));
        }

        /// <summary>
        /// Level a rendered sequence would produce at <paramref name="timeMs"/> after it starts,
        /// using the mixer's own attack/hold/decay shape and its "max across the active
        /// envelopes" rule. Added in Phase E so the pattern preview strip and the per-device
        /// test path draw/play exactly what the engine would do, instead of a look-alike curve.
        /// </summary>
        public static double SampleAt(IReadOnlyList<HapticPulseStep> steps, int timeMs)
        {
            if (steps == null || steps.Count == 0) return 0;
            double best = 0;
            foreach (var step in steps)
            {
                var t = timeMs - step.DelayMs;
                if (t < 0) continue;
                var p = step.Pulse;
                double v;
                if (t < p.AttackMs) v = p.AttackMs <= 0 ? p.Intensity : p.Intensity * (t / (double)p.AttackMs);
                else if (t - p.AttackMs < p.HoldMs) v = p.Intensity;
                else if (t - p.AttackMs - p.HoldMs < p.DecayMs)
                    v = p.DecayMs <= 0 ? 0 : p.Intensity * (1.0 - (t - p.AttackMs - p.HoldMs) / (double)p.DecayMs);
                else v = 0;
                if (v > best) best = v;
            }
            return Math.Clamp(best, 0, 1);
        }
    }
}
