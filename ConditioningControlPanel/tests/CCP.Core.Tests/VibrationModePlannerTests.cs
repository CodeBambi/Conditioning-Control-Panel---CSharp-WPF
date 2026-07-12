using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Verifies the vibration-mode pattern math in <see cref="VibrationModePlanner"/>: each
/// <c>VibrationMode</c>'s step list must reproduce the WPF
/// <c>HapticService.ApplyVibrationModeAsync</c> switch (Services/Haptics/HapticService.cs:230-320)
/// byte-for-byte — counts, per-step intensity, per-step duration, and gap placement.
/// These are the same patterns the AI haptic command (WPF HapticCommand.cs:24) and every
/// discrete-event feature (BubblePop, BouncingText, Blink, Subliminal, TargetHit, LevelUp,
/// Achievement) play on the device, so locking them here locks parity for all of them.
/// </summary>
public class VibrationModePlannerTests
{
    [Fact]
    public void MinPerceptibleIntensity_MatchesWpf()
    {
        // WPF HapticService.cs:274 — MinPerceptibleIntensity = 0.06.
        Assert.Equal(0.06, VibrationModePlanner.MinPerceptibleIntensity, precision: 12);
    }

    [Fact]
    public void Constant_SingleStepAtRequestedDuration()
    {
        // WPF HapticService.cs:243-246 — one VibrateAsync(intensity, durationMs).
        var steps = VibrationModePlanner.Plan(0.5, 1000, VibrationMode.Constant);

        var step = Assert.Single(steps);
        Assert.Equal(0.5, step.Intensity, precision: 12);
        Assert.Equal(1000, step.DurationMs);
    }

    [Fact]
    public void Pulse_50On_30Off_NoTrailingOff()
    {
        // WPF HapticService.cs:248-258 — pulseCount = max(1, durationMs/80); 50ms on,
        // 30ms off between pulses, NO trailing off-delay after the last pulse.
        // durationMs=800 -> pulseCount=10 -> 10 on-steps and 9 gap-steps = 19 total.
        var steps = VibrationModePlanner.Plan(0.5, 800, VibrationMode.Pulse);

        Assert.Equal(19, steps.Count);
        // On-steps at even indices (0,2,4,...), gaps at odd indices.
        for (int i = 0; i < steps.Count; i++)
        {
            if (i % 2 == 0)
            {
                Assert.Equal(0.5, steps[i].Intensity, precision: 12);
                Assert.Equal(50, steps[i].DurationMs);
            }
            else
            {
                Assert.Equal(0.0, steps[i].Intensity, precision: 12);
                Assert.Equal(30, steps[i].DurationMs);
            }
        }
        // Last step is an on-step (no trailing gap).
        Assert.Equal(0.5, steps[^1].Intensity, precision: 12);
    }

    [Fact]
    public void Pulse_ZeroDuration_StillOnePulse()
    {
        // WPF HapticService.cs:249 — pulseCount = max(1, 0/80) = 1; a 0-duration request
        // still emits a single 50ms pulse (WPF has no duration>0 guard before the call).
        var steps = VibrationModePlanner.Plan(0.9, 0, VibrationMode.Pulse);

        var step = Assert.Single(steps);
        Assert.Equal(0.9, step.Intensity, precision: 12);
        Assert.Equal(50, step.DurationMs);
    }

    [Fact]
    public void Wave_TwelveSteps_RampUpDown_FlooredAtMinPerceptible()
    {
        // WPF HapticService.cs:260-277 — 6 ramp-up + 6 ramp-down = 12 steps;
        // waveStepDuration = durationMs/(6*2); step intensity = intensity*(i/6),
        // floored at MinPerceptibleIntensity (0.06). durationMs=1200 -> 100ms/step.
        var steps = VibrationModePlanner.Plan(1.0, 1200, VibrationMode.Wave);

        Assert.Equal(12, steps.Count);
        Assert.All(steps, s => Assert.Equal(100, s.DurationMs));

        // Ramp up i=1..6: intensity = i/6.
        Assert.Equal(1.0 / 6.0, steps[0].Intensity, precision: 12);   // i=1
        Assert.Equal(1.0, steps[5].Intensity, precision: 12);         // i=6 (peak)
        // Ramp down i=5..0: intensity = i/6.
        Assert.Equal(5.0 / 6.0, steps[6].Intensity, precision: 12);   // i=5
        // Last ramp-down step i=0 -> intensity 0 floored to MinPerceptibleIntensity.
        Assert.Equal(VibrationModePlanner.MinPerceptibleIntensity, steps[11].Intensity, precision: 12);
    }

    [Fact]
    public void Heartbeat_DoublePulse_WithBetweenBeatGap()
    {
        // WPF HapticService.cs:279-293 — heartbeatCount = max(1, durationMs/400);
        // each beat = 80ms on, 60ms off, 60ms lighter (0.7x); 200ms gap between beats.
        // durationMs=800 -> heartbeatCount=2 -> beat + gap + beat = 3 + 1 + 3 = 7 steps.
        var steps = VibrationModePlanner.Plan(1.0, 800, VibrationMode.Heartbeat);

        Assert.Equal(7, steps.Count);
        // Beat 1.
        Assert.Equal((1.0, 80), (steps[0].Intensity, steps[0].DurationMs));
        Assert.Equal((0.0, 60), (steps[1].Intensity, steps[1].DurationMs));
        Assert.Equal((0.7, 60), (steps[2].Intensity, steps[2].DurationMs));
        // Inter-beat gap (only between beats, not after the last).
        Assert.Equal((0.0, 200), (steps[3].Intensity, steps[3].DurationMs));
        // Beat 2 (no trailing gap).
        Assert.Equal((1.0, 80), (steps[4].Intensity, steps[4].DurationMs));
        Assert.Equal((0.0, 60), (steps[5].Intensity, steps[5].DurationMs));
        Assert.Equal((0.7, 60), (steps[6].Intensity, steps[6].DurationMs));
    }

    [Fact]
    public void Escalate_EightSteps_LowToFull()
    {
        // WPF HapticService.cs:295-305 — 8 steps; stepDuration = durationMs/8;
        // step intensity = intensity*(0.2 + 0.8*(i/8)). durationMs=800 -> 100ms/step.
        var steps = VibrationModePlanner.Plan(1.0, 800, VibrationMode.Escalate);

        Assert.Equal(8, steps.Count);
        Assert.All(steps, s => Assert.Equal(100, s.DurationMs));
        // First step i=1 -> 0.2 + 0.8*(1/8) = 0.3.
        Assert.Equal(0.3, steps[0].Intensity, precision: 12);
        // Last step i=8 -> 0.2 + 0.8*(8/8) = 1.0.
        Assert.Equal(1.0, steps[7].Intensity, precision: 12);
    }

    [Fact]
    public void Earthquake_RandomInRange_80On_20Off_AlwaysTrailingOff()
    {
        // WPF HapticService.cs:307-318 — quakeSteps = max(2, durationMs/100);
        // random intensity = intensity*(0.3 + rand*0.7); 80ms on then 20ms off,
        // and the 20ms off-delay is UNCONDITIONAL (including after the last step).
        // durationMs=200 -> quakeSteps = max(2, 2) = 2 -> 2*(on+off) = 4 steps.
        // Fixed randomSource=0.5 -> randomIntensity = 0.3 + 0.5*0.7 = 0.65.
        var steps = VibrationModePlanner.Plan(1.0, 200, VibrationMode.Earthquake, randomSource: () => 0.5);

        Assert.Equal(4, steps.Count);
        Assert.All(steps, s => Assert.True(s.DurationMs == 80 || s.DurationMs == 20));
        Assert.Equal(0.65, steps[0].Intensity, precision: 12);  // on
        Assert.Equal(0.0, steps[1].Intensity, precision: 12);   // off
        Assert.Equal(0.65, steps[2].Intensity, precision: 12);  // on
        Assert.Equal(0.0, steps[3].Intensity, precision: 12);   // trailing off (unconditional)
    }

    [Fact]
    public void Earthquake_RandomBounds_MapTo30And100Percent()
    {
        // WPF HapticService.cs:312 — randomIntensity = intensity*(0.3 + rand*0.7).
        // rand=0 -> 30%; rand=1 -> 100%.
        var low = VibrationModePlanner.Plan(1.0, 100, VibrationMode.Earthquake, randomSource: () => 0.0);
        Assert.Equal(0.3, low[0].Intensity, precision: 12);

        var high = VibrationModePlanner.Plan(1.0, 100, VibrationMode.Earthquake, randomSource: () => 1.0);
        Assert.Equal(1.0, high[0].Intensity, precision: 12);
    }

    [Fact]
    public async Task IHapticsService_DefaultApplyVibrationModeAsync_IsSafeNoOp()
    {
        // Iron rule 4: new interface members get a default no-op so fakes/stubs compile
        // unchanged. A fake that does NOT override ApplyVibrationModeAsync must still
        // satisfy the seam without throwing (every mode, with and without a token).
        IHapticsService fake = new NoOpHapticsFake();

        foreach (var mode in new[] { VibrationMode.Constant, VibrationMode.Pulse, VibrationMode.Wave,
                                     VibrationMode.Heartbeat, VibrationMode.Escalate, VibrationMode.Earthquake })
        {
            await fake.ApplyVibrationModeAsync(0.5, 1000, mode);
            await fake.ApplyVibrationModeAsync(0.5, 1000, mode, CancellationToken.None);
        }
    }

    /// <summary>Minimal <see cref="IHapticsService"/> fake that relies on the default
    /// <c>ApplyVibrationModeAsync</c> implementation. Used to prove the seam's default
    /// body is a safe no-op for any head/test that does not override it.</summary>
    private sealed class NoOpHapticsFake : IHapticsService
    {
        public bool IsConnected => false;
        public bool IsConnecting => false;
        public IReadOnlyList<string> ConnectedDevices => Array.Empty<string>();
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? DeviceAdded;
        public event EventHandler<string>? DeviceRemoved;
        public Task<bool> ConnectAsync(string providerUrl) => Task.FromResult(false);
        public void Disconnect() { }
        public Task<bool> TestAsync(int intensityPercent, int durationMs) => Task.FromResult(false);
        public Task SetSyncPatternAsync(float[] samples, int durationMs) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }
}
