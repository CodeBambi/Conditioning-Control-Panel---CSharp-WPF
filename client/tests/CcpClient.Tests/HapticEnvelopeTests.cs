using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The ARITHMETIC of the haptic limb, with no clock, no sink and no device anywhere near
/// it. Every number asserted here is upstream's, cited at the line it was read from, and the point
/// of the file is that a port which drifted from those numbers would fail HERE rather than on
/// somebody's toy.
/// </summary>
public class HapticEnvelopeTests
{
    // =====================================================================================
    //  The flash decay ladder (census sites 1-3)
    // =====================================================================================

    [Fact]
    public void TheFlashLadderIsEightRungsOfSevenTenths_FlooredAtThePerceptibleSixHundredths()
    {
        var ladder = HapticEnvelopes.FlashDecayLadder();

        // Services/Haptics/HapticService.cs:782-788. Eight rungs, each
        // max(start * 0.7^i, MinPerceptibleIntensity), 450 ms apart. The shipped mode is Constant,
        // which renders one step per rung.
        Assert.Equal(8, ladder.Count);
        for (var i = 0; i < ladder.Count; i++)
        {
            var expected = Math.Max(0.5 * Math.Pow(0.7, i), HapticMix.MinPerceptibleIntensity);
            Assert.Equal(expected, ladder[i].Pulse.Intensity, 10);
            Assert.Equal(i * 450, ladder[i].DelayMs);
            // Priority 1 — the SAME group the subliminal pulse posts at (HapticService.cs:880),
            // which is the whole reason peak-of-sum is reachable in this build.
            Assert.Equal(1, ladder[i].Pulse.Priority);
        }

        // The floor really bites, and from rung 5 on: 0.5 * 0.7^5 = 0.084, 0.7^6 = 0.059 -> 0.06.
        Assert.Equal(HapticMix.MinPerceptibleIntensity, ladder[6].Pulse.Intensity, 10);
        Assert.Equal(HapticMix.MinPerceptibleIntensity, ladder[7].Pulse.Intensity, 10);
        Assert.True(ladder[5].Pulse.Intensity > HapticMix.MinPerceptibleIntensity);
    }

    [Fact]
    public void TheLadderSpansThirtyFiveHundredAndThreeMilliseconds_NotTheTwoSecondsItsOwnCommentClaims()
    {
        // D205, executed rather than quoted. HapticService.cs:761 documents a vibe that "decays over
        // ~2s"; the arithmetic at :782-787 is eight rungs at i*450 ms, each rendering a 250 ms
        // Constant pattern, and Constant 250 renders as 41 + 250 + 62 = 353 ms
        // (HapticPatterns.cs:123-128). 7*450 + 353 = 3503. CODE WINS.
        Assert.Equal(3503, HapticShapes.SpanMs(HapticEnvelopes.FlashDecayLadder()));

        // And the gap is 75 %, not a rounding difference: a limb that trusted the sentence would end
        // its envelope a second and a half before upstream's does.
        Assert.True(3503 > 2000 * 1.7);
    }

    [Fact]
    public void ASliderAtZeroIsSILENCE_AndNotAFloorBuzz()
    {
        // "slider at 0 = silence, not a floor buzz" (HapticService.cs:779). The perceptible floor
        // inside the rung arithmetic must never turn a deliberate zero into a 6 % hum.
        Assert.Empty(HapticEnvelopes.FlashDecayLadder(0));
        Assert.Empty(HapticEnvelopes.FlashDecayLadder(-1));

        // The same rule on the continuous side (HapticService.cs:836-843).
        Assert.Equal(0, HapticEnvelopes.VideoBackgroundLevel(0));
    }

    // =====================================================================================
    //  The two rendered shapes
    // =====================================================================================

    [Fact]
    public void AConstantTwoFiftyRendersAsFortyOnePlusTwoFiftyPlusSixtyTwo()
    {
        // HapticPatterns.cs:126-128 — attack = clamp(d/6, 10, 60), decay = clamp(d/4, 30, 150),
        // hold = the whole duration.
        var steps = HapticShapes.Render(HapticShape.Constant, 0.5, 250, priority: 1);

        var pulse = Assert.Single(steps).Pulse;
        Assert.Equal(41, pulse.AttackMs);
        Assert.Equal(250, pulse.HoldMs);
        Assert.Equal(62, pulse.DecayMs);
        Assert.Equal(353, pulse.TotalMs);
    }

    [Fact]
    public void APulseBelowTheOnTimeFloorIsWidenedToIt_BecauseTheMotorNeverLeftStandstill()
    {
        // HapticPatterns.cs:18-34 is the whole reason this floor exists: "The commands were correct,
        // they reached the toy, and the toy never actually moved." A 60 ms bounce request
        // (HapticService.cs:821) is floored to 200 ms and renders as ONE tap of MinFeltOnMs on-time.
        var bounce = HapticEnvelopes.BouncePulse();

        var pulse = Assert.Single(bounce).Pulse;
        Assert.Equal(HapticShapes.MinFeltOnMs, pulse.HoldMs);
        Assert.Equal(8, pulse.AttackMs);
        Assert.Equal(20, pulse.DecayMs);
        Assert.Equal(158, pulse.TotalMs);
        // Priority 0 (HapticService.cs:821) — a different group from the flash and the subliminal,
        // so it MAXes against them and sums only with another bounce.
        Assert.Equal(0, pulse.Priority);
    }

    [Fact]
    public void ARenderedDurationIsClampedIntoUpstreamsOwnWindow()
    {
        // HapticPatterns.cs:47 — floor at MinFeltDurationMs, ceiling at 60 s.
        var tiny = Assert.Single(HapticShapes.Render(HapticShape.Constant, 1.0, 1, priority: 0)).Pulse;
        Assert.Equal(HapticShapes.MinFeltDurationMs, tiny.HoldMs);

        var huge = Assert.Single(HapticShapes.Render(HapticShape.Constant, 1.0, 999_999, priority: 0)).Pulse;
        Assert.Equal(HapticShapes.MaxDurationMs, huge.HoldMs);

        // Zero intensity renders NOTHING rather than a quiet something (HapticPatterns.cs:49).
        Assert.Empty(HapticShapes.Render(HapticShape.Constant, 0, 500, priority: 0));
    }

    // =====================================================================================
    //  The subliminal pulse (census sites 14-15)
    // =====================================================================================

    [Fact]
    public void TheSubliminalDurationIsKeyedOffTheTriggersOwnWording()
    {
        // HapticService.cs:899-909, with the Buttplug multiplier at 1.0 because this port does not
        // port it: upstream keys that doubling off the ACTIVE provider (:903) and these envelopes
        // are built before any route is chosen. Named as unported rather than as unreachable - the
        // Buttplug route has a client now (HapticEnvelope.cs, SubliminalDurationMs).
        Assert.Equal(250, HapticEnvelopes.SubliminalDurationMs("you will CUM on command"));
        Assert.Equal(250, HapticEnvelopes.SubliminalDurationMs("collapse"));
        Assert.Equal(250, HapticEnvelopes.SubliminalDurationMs("DROP"));
        Assert.Equal(120, HapticEnvelopes.SubliminalDurationMs("Bambi freeze"));
        Assert.Equal(120, HapticEnvelopes.SubliminalDurationMs("zap"));
        Assert.Equal(150, HapticEnvelopes.SubliminalDurationMs("good girl"));
        Assert.Equal(150, HapticEnvelopes.SubliminalDurationMs(""));
        Assert.Equal(150, HapticEnvelopes.SubliminalDurationMs(null));

        // Order matters: "drop" wins over "freeze" when a phrase carries both, because upstream tests
        // the intense set first.
        Assert.Equal(250, HapticEnvelopes.SubliminalDurationMs("freeze and drop"));
    }

    [Fact]
    public void AllThreeSubliminalDurationsRenderIDENTICALLYInTheShippedMode_WhichIsAFindingNotADefect()
    {
        // The shipped subliminal mode is Pulse (Models/HapticSettings.cs:62). Pulse's tap count is
        // clamp(duration / 200, 1, 40) (HapticPatterns.cs:59-64), and 250, 150 and 120 all yield ONE
        // tap of the same shape. So the wording changes nothing a person feels until the duration
        // reaches 400 ms, which no phrase produces. The arithmetic above is still ported exactly,
        // because it IS the arithmetic and because a mode change makes it bite.
        var intense = HapticEnvelopes.SubliminalPulse("drop");
        var sharp = HapticEnvelopes.SubliminalPulse("freeze");
        var ordinary = HapticEnvelopes.SubliminalPulse("good girl");

        Assert.Equal(intense, sharp);
        Assert.Equal(intense, ordinary);
        Assert.Equal(1, Assert.Single(ordinary).Pulse.Priority);

        // And the moment a longer duration is asked for, the count moves — so this is a property of
        // the shipped numbers rather than of a renderer that ignores its input.
        Assert.Equal(2, HapticShapes.Render(HapticShape.Pulse, 0.5, 400, priority: 1).Count);
    }

    // =====================================================================================
    //  The continuous video layer (census sites 6, 7, 12)
    // =====================================================================================

    [Fact]
    public void TheVideoBackgroundIsATenthOfTheSlider_LiftedToThePerceptibleFloor()
    {
        // HapticService.cs:846-847 — "Background vibe is 10% of the slider so target hits feel
        // impactful". At the shipped 0.5 slider (Models/HapticSettings.cs:48) a tenth is 0.05, which
        // is BELOW the perceptible floor, so the floor wins and the layer sits at exactly 0.06.
        Assert.Equal(0.06, HapticEnvelopes.VideoBackgroundLevel(), 10);
        Assert.Equal(HapticMix.MinPerceptibleIntensity, HapticEnvelopes.VideoBackgroundLevel(), 10);

        // A slider high enough for the tenth to clear the floor uses the tenth.
        Assert.Equal(0.1, HapticEnvelopes.VideoBackgroundLevel(1.0), 10);
    }

    // =====================================================================================
    //  The envelope itself
    // =====================================================================================

    [Fact]
    public void AnEnvelopeRisesThenHoldsThenFalls_AndIsSilentAtItsOwnEndInstant()
    {
        var pulse = new HapticPulse(0.8, AttackMs: 100, HoldMs: 200, DecayMs: 100, Priority: 0);

        Assert.Equal(0, pulse.LevelAt(-1));
        Assert.Equal(0, pulse.LevelAt(0));               // an envelope is 0 at its own t=0
        Assert.Equal(0.4, pulse.LevelAt(50), 10);        // half way up the attack
        Assert.Equal(0.8, pulse.LevelAt(100), 10);       // the crest
        Assert.Equal(0.8, pulse.LevelAt(299), 10);       // still on the plateau
        Assert.Equal(0.4, pulse.LevelAt(350), 10);       // half way down the decay
        // THE TRAILING CLAMP (HapticMixer.cs:1209-1217): silent from its own end onwards. Without it
        // a hard edge renders at FULL level one tick past its end, which is the defect upstream's
        // expiry grace introduced and this clause fixed.
        Assert.Equal(0, pulse.LevelAt(400));
        Assert.Equal(0, pulse.LevelAt(10_000));
    }

    [Fact]
    public void AHardEdgedPulseIsFullFromItsFirstInstantAndSilentFromItsLast()
    {
        // Attack 0 means an instant edge, which is a deliberate shape (HapticMixer.cs:1211-1213).
        var edge = new HapticPulse(0.5, AttackMs: 0, HoldMs: 100, DecayMs: 0, Priority: 0);

        Assert.Equal(0.5, edge.LevelAt(0), 10);
        Assert.Equal(0.5, edge.LevelAt(99), 10);
        Assert.Equal(0, edge.LevelAt(100));

        // A zero-length step is silent, not instantaneous-and-loud.
        var nothing = new HapticPulse(1.0, 0, 0, 0, 0);
        Assert.Equal(0, nothing.LevelAt(0));
    }

    // =====================================================================================
    //  The combination rule itself — C+'s reason for existing
    // =====================================================================================

    [Fact]
    public void TransientsSUMWithinAPriorityGroup_AndTakeTheMAXAcrossGroups()
    {
        // HapticMixer.cs:476-503, the rule option C would have dropped.
        Assert.Equal(1.0, HapticMix.Transient([new(0.5, 1), new(0.5, 1)]), 10);
        Assert.Equal(0.5, HapticMix.Transient([new(0.5, 1), new(0.5, 0)]), 10);
        Assert.Equal(0.9, HapticMix.Transient([new(0.4, 1), new(0.5, 1)]), 10);

        // The group's own clamp at 1 (:501) — three halves do not become 1.5.
        Assert.Equal(1.0, HapticMix.Transient([new(0.5, 1), new(0.5, 1), new(0.5, 1)]), 10);

        // Nothing at all is nothing at all.
        Assert.Equal(0, HapticMix.Transient([]));
    }

    [Fact]
    public void TheMasterArithmeticAppliesTheFloorBEFORETheCap_BecauseTheCapIsSafety()
    {
        // HapticMixer.cs:509-518. Master multiplier 0.7 (Models/HapticSettings.cs:29), perceptible
        // floor 0.06, hard cap 0.70 (HapticMixer.cs:77).
        Assert.Equal(0, HapticMix.Finish(0));
        Assert.Equal(0.35, HapticMix.Finish(0.5), 10);
        Assert.Equal(0.70, HapticMix.Finish(1.0), 10);
        // Anything above the cap is the cap: "full power is opt-in, not the accident you discover
        // mid-session".
        Assert.Equal(HapticMix.MasterCap, HapticMix.Finish(5.0), 10);
        // A tiny non-zero request is lifted to the perceptible floor rather than vanishing.
        Assert.Equal(HapticMix.MinPerceptibleIntensity, HapticMix.Finish(0.01), 10);
    }

    [Fact]
    public void TransientsRideOVERTheContinuousFloorRatherThanReplacingIt()
    {
        // HapticMixer.cs:506 — raw = max(floor, transient).
        Assert.Equal(HapticMix.Finish(0.5), HapticMix.Combine(0.06, 0.5), 10);
        Assert.Equal(HapticMix.Finish(0.06), HapticMix.Combine(0.06, 0.0), 10);
        Assert.Equal(HapticMix.Finish(0.06), HapticMix.Combine(0.06, 0.02), 10);
    }
}
