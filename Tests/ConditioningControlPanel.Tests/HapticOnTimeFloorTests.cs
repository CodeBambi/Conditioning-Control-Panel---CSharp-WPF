using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #955 (re-report of #896): "Haptics still pretty much not working except for constant during
/// video play and some test buttons." Everything LONG worked and everything SHORT did not, which
/// is not a routing bug - it is physics. An ERM motor needs ~100-150 ms to spin up to a speed
/// anyone can feel, and the app was asking for 50-100 ms taps. The commands were correct, they
/// reached the toy, and the toy never moved.
///
/// These tests pin the on-time floor: every rendered pulse asks the hardware for at least
/// <see cref="HapticPatterns.MinFeltOnMs"/> of hold, in every mode, at every requested duration.
/// The systemic note from the triage still stands - haptics has no output logging - so this is the
/// first thing in the feature with a test at all, and it guards the one property that decides
/// whether a user feels anything.
/// </summary>
public class HapticOnTimeFloorTests
{
    private static readonly VibrationMode[] AllModes =
    {
        VibrationMode.Constant, VibrationMode.Pulse, VibrationMode.Wave,
        VibrationMode.Heartbeat, VibrationMode.Escalate, VibrationMode.Earthquake,
    };

    [Theory]
    // The three durations the event table actually used, all of them below the floor.
    [InlineData(60)]    // BouncingTextBounce
    [InlineData(100)]   // BubblePop, VideoTargetHit
    [InlineData(150)]   // BlinkPulse, SubliminalTrigger, KeywordTrigger, GazeReward
    public void EveryModeClearsTheFloorEvenWhenAskedForLess(int requestedMs)
    {
        foreach (var mode in AllModes)
        {
            var steps = HapticPatterns.Render(mode, 0.35, requestedMs);

            Assert.NotEmpty(steps);
            foreach (var step in steps)
            {
                // Attack + hold + decay, because that is how long the motor is actually driven.
                // Wave is a pure triangle with no hold at all, and that is correct for a swell -
                // what matters is that the ramp is long enough to reach a felt speed.
                var onTime = step.Pulse.AttackMs + step.Pulse.HoldMs + step.Pulse.DecayMs;
                Assert.True(onTime >= HapticPatterns.MinFeltOnMs,
                    $"{mode} at {requestedMs}ms drives the motor for {onTime}ms - below its spin-up.");
            }
        }
    }

    [Fact]
    public void ALongRequestIsStillHonoured_TheFloorIsAFloorNotACap()
    {
        // The regression this floor could plausibly cause: flattening everything to 200 ms.
        var steps = HapticPatterns.Render(VibrationMode.Constant, 0.5, 4000);

        var single = Assert.Single(steps);
        Assert.Equal(4000, single.Pulse.HoldMs);
    }

    [Fact]
    public void PulseKeepsItsRhythm()
    {
        // Widening the tap must not turn taps into one continuous buzz: a full second still
        // renders as several discrete, separated pulses.
        var steps = HapticPatterns.Render(VibrationMode.Pulse, 0.5, 1000);

        Assert.True(steps.Count >= 4, $"a 1s Pulse rendered only {steps.Count} tap(s)");
        var offsets = steps.Select(s => s.DelayMs).ToList();
        for (int i = 1; i < offsets.Count; i++)
        {
            var gap = offsets[i] - offsets[i - 1] - steps[i - 1].Pulse.HoldMs;
            Assert.True(gap > 0, "consecutive taps must have silence between them");
        }
    }

    [Fact]
    public void EscalateTradesRungsForFeelWhenTheEventIsShort()
    {
        // Eight rungs over 300 ms was eight steps of nothing. Fewer, felt rungs instead - and the
        // full climb is still there when there is time for it.
        var shortRun = HapticPatterns.Render(VibrationMode.Escalate, 0.6, 300);
        var longRun = HapticPatterns.Render(VibrationMode.Escalate, 0.6, 4000);

        Assert.True(shortRun.Count < longRun.Count);
        Assert.Equal(8, longRun.Count);
        Assert.All(shortRun, s => Assert.True(s.Pulse.HoldMs >= HapticPatterns.MinFeltOnMs));

        // Still an escalation: each rung is stronger than the one before it.
        for (int i = 1; i < longRun.Count; i++)
            Assert.True(longRun[i].Pulse.Intensity > longRun[i - 1].Pulse.Intensity);
    }

    [Fact]
    public void ASilentRequestStaysSilent()
    {
        // The floor is about DURATION. A slider at zero still means nothing at all (#516).
        Assert.Empty(HapticPatterns.Render(VibrationMode.Constant, 0.0, 1000));
    }
}
