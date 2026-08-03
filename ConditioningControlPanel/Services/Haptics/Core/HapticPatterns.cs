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
        public static IReadOnlyList<HapticPulseStep> Render(
            VibrationMode mode, double intensity, int durationMs, int priority = 0, ToyRole target = ToyRole.All)
        {
            intensity = Math.Clamp(intensity, 0, 1);
            durationMs = Math.Clamp(durationMs, 20, 60_000);
            var steps = new List<HapticPulseStep>();
            if (intensity <= 0) return steps;

            switch (mode)
            {
                case VibrationMode.Pulse:
                {
                    // Quick on/off taps — 50 ms on, 30 ms off, exactly the old intent.
                    var count = Math.Clamp(durationMs / 80, 1, 40);
                    for (int i = 0; i < count; i++)
                        steps.Add(new HapticPulseStep(i * 80, new HapticPulse(intensity, 8, 42, 20, priority, target)));
                    break;
                }

                case VibrationMode.Wave:
                {
                    // One smooth swell: half the duration up, half back down.
                    var half = Math.Max(20, durationMs / 2);
                    steps.Add(new HapticPulseStep(0, new HapticPulse(intensity, half, 0, half, priority, target)));
                    break;
                }

                case VibrationMode.Heartbeat:
                {
                    // ba-BUMP ... ba-BUMP: a strong beat then a lighter one, 400 ms apart.
                    var beats = Math.Clamp(durationMs / 400, 1, 20);
                    for (int i = 0; i < beats; i++)
                    {
                        var t = i * 400;
                        steps.Add(new HapticPulseStep(t, new HapticPulse(intensity, 10, 70, 30, priority, target)));
                        steps.Add(new HapticPulseStep(t + 140, new HapticPulse(intensity * 0.7, 10, 50, 30, priority, target)));
                    }
                    break;
                }

                case VibrationMode.Escalate:
                {
                    // 20% -> 100% of the set intensity across the duration.
                    const int n = 8;
                    var slice = Math.Max(20, durationMs / n);
                    for (int i = 1; i <= n; i++)
                        steps.Add(new HapticPulseStep((i - 1) * slice,
                            new HapticPulse(intensity * (0.2 + 0.8 * (i / (double)n)), 10, slice, 20, priority, target)));
                    break;
                }

                case VibrationMode.Earthquake:
                {
                    // Random 30-100% jolts. Random.Shared is thread-safe (net6+).
                    var count = Math.Clamp(durationMs / 100, 2, 60);
                    for (int i = 0; i < count; i++)
                        steps.Add(new HapticPulseStep(i * 100,
                            new HapticPulse(intensity * (0.3 + Random.Shared.NextDouble() * 0.7), 5, 60, 25, priority, target)));
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
