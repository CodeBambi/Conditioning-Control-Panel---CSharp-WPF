using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// One time-boxed vibration command in a mode's plan: an intensity in 0..1 and a
/// duration in ms. A step whose <see cref="Intensity"/> is 0 is a silence gap (the
/// device motor is held off for <see cref="DurationMs"/>).
/// </summary>
public readonly record struct VibrationStep(double Intensity, int DurationMs);

/// <summary>
/// Pure, deterministic planner that turns an (intensity, durationMs, mode) request into
/// the ordered list of (<see cref="double"/>, int) vibration steps the device should
/// play, byte-for-byte from the WPF <c>HapticService.ApplyVibrationModeAsync</c> switch.
/// Extracted into Core so the per-mode pattern math is unit-testable without a connected
/// Buttplug device, and so every head plays the identical pattern.
///
/// WPF ground truth (Services/Haptics/HapticService.cs):
///   - <c>MinPerceptibleIntensity = 0.06</c> (HapticService.cs:274) — Wave-mode step
///     intensities are floored at this value via <c>Math.Max(stepIntensity, MinPerceptibleIntensity)</c>.
///   - Constant (HapticService.cs:243-246): one <c>VibrateAsync(intensity, durationMs)</c>.
///   - Pulse (HapticService.cs:248-258): <c>pulseCount = max(1, durationMs/80)</c>,
///     50ms on then 30ms off, no trailing off-delay after the last pulse.
///   - Wave (HapticService.cs:260-277): 6 ramp-up + 6 ramp-down steps,
///     <c>waveStepDuration = durationMs/12</c>, step intensity <c>intensity*(i/6)</c>.
///   - Heartbeat (HapticService.cs:279-293): <c>heartbeatCount = max(1, durationMs/400)</c>,
///     ba-bump = 80ms on, 60ms off, 60ms lighter (0.7x), 200ms between beats.
///   - Escalate (HapticService.cs:295-305): 8 steps, <c>stepDuration = durationMs/8</c>,
///     step intensity <c>intensity*(0.2 + 0.8*(i/8))</c>.
///   - Earthquake (HapticService.cs:307-318): <c>quakeSteps = max(2, durationMs/100)</c>,
///     random intensity <c>intensity*(0.3 + rand*0.7)</c>, 80ms on then 20ms off.
///
/// NOTE: the planner reproduces the WPF step <b>structure</b> (counts, per-step
/// intensity, per-step duration, gap placement) exactly. It does not reproduce the
/// provider call cadence; the caller is responsible for actually awaiting each step's
/// duration so adjacent pulses/gaps do not collapse into the last device command.
/// </summary>
public static class VibrationModePlanner
{
    /// <summary>Lowest intensity that still maps to a real vibration level. WPF floors
    /// Wave-mode step intensities at this value (HapticService.cs:274,
    /// <c>MinPerceptibleIntensity = 0.06</c>).</summary>
    public const double MinPerceptibleIntensity = 0.06;

    /// <summary>
    /// Build the ordered vibration step list for the given mode. Intensities are in 0..1
    /// (a 0 intensity is a silence gap); durations are in ms. Pass a <paramref name="randomSource"/>
    /// returning a value in [0,1) to make Earthquake deterministic (tests pass a fixed value;
    /// production leaves it null for <c>Random.Shared.NextDouble</c>).
    /// </summary>
    public static IReadOnlyList<VibrationStep> Plan(
        double intensity,
        int durationMs,
        VibrationMode mode,
        Func<double>? randomSource = null)
    {
        var rng = randomSource ?? Random.Shared.NextDouble;
        var steps = new List<VibrationStep>();

        switch (mode)
        {
            case VibrationMode.Constant:
                // WPF HapticService.cs:243-246 — single continuous vibration.
                steps.Add(new VibrationStep(intensity, durationMs));
                break;

            case VibrationMode.Pulse:
            {
                // WPF HapticService.cs:248-258 — pulseCount = max(1, durationMs/80);
                // 50ms on, 30ms off, no trailing off-delay after the last pulse.
                var pulseCount = Math.Max(1, durationMs / 80);
                for (int i = 0; i < pulseCount; i++)
                {
                    steps.Add(new VibrationStep(intensity, 50));
                    if (i < pulseCount - 1) steps.Add(new VibrationStep(0.0, 30));
                }
                break;
            }

            case VibrationMode.Wave:
            {
                // WPF HapticService.cs:260-277 — 6 ramp-up + 6 ramp-down steps;
                // waveStepDuration = durationMs/(6*2); step intensity floored at 0.06.
                var waveSteps = 6;
                var waveStepDuration = durationMs / (waveSteps * 2);
                for (int i = 1; i <= waveSteps; i++)
                {
                    var stepIntensity = intensity * (i / (double)waveSteps);
                    steps.Add(new VibrationStep(Math.Max(stepIntensity, MinPerceptibleIntensity), waveStepDuration));
                }
                for (int i = waveSteps - 1; i >= 0; i--)
                {
                    var stepIntensity = intensity * (i / (double)waveSteps);
                    steps.Add(new VibrationStep(Math.Max(stepIntensity, MinPerceptibleIntensity), waveStepDuration));
                }
                break;
            }

            case VibrationMode.Heartbeat:
            {
                // WPF HapticService.cs:279-293 — heartbeatCount = max(1, durationMs/400);
                // ba-bump = 80ms on, 60ms off, 60ms lighter (0.7x), 200ms between beats.
                var heartbeatCount = Math.Max(1, durationMs / 400);
                for (int i = 0; i < heartbeatCount; i++)
                {
                    steps.Add(new VibrationStep(intensity, 80));
                    steps.Add(new VibrationStep(0.0, 60));
                    steps.Add(new VibrationStep(intensity * 0.7, 60));
                    if (i < heartbeatCount - 1) steps.Add(new VibrationStep(0.0, 200));
                }
                break;
            }

            case VibrationMode.Escalate:
            {
                // WPF HapticService.cs:295-305 — 8 steps; stepDuration = durationMs/8;
                // step intensity = intensity*(0.2 + 0.8*(i/8)).
                var escalateSteps = 8;
                var escalateStepDuration = durationMs / escalateSteps;
                for (int i = 1; i <= escalateSteps; i++)
                {
                    var stepIntensity = intensity * (0.2 + 0.8 * (i / (double)escalateSteps));
                    steps.Add(new VibrationStep(stepIntensity, escalateStepDuration));
                }
                break;
            }

            case VibrationMode.Earthquake:
            {
                // WPF HapticService.cs:307-318 — quakeSteps = max(2, durationMs/100);
                // random intensity = intensity*(0.3 + rand*0.7); 80ms on then 20ms off
                // (the 20ms off-delay is unconditional, including after the last step).
                var quakeSteps = Math.Max(2, durationMs / 100);
                for (int i = 0; i < quakeSteps; i++)
                {
                    var randomIntensity = intensity * (0.3 + rng() * 0.7);
                    steps.Add(new VibrationStep(randomIntensity, 80));
                    steps.Add(new VibrationStep(0.0, 20));
                }
                break;
            }
        }

        return steps;
    }
}
