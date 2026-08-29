using System;
using System.Collections.Generic;
using System.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE WATCHING HALF (ALIVE wave A, <c>docs/emi-desk/ALIVE-PLAN.md</c>).
///
/// <para>Wave A is what turns the stare into eye contact: the face leans toward the cursor, she
/// perks when you come at her, she looks expectant when you hover and away when the pat never
/// comes, she fidgets on her own, and she loses her temper if you poke her three times. Almost
/// none of that can be play-tested honestly - a fidget is 25 to 50 seconds away, a stretch is
/// twenty minutes away, and the poke ladder ends in a sixty second truce - so the decisions all
/// live in <see cref="EmiAlive"/> as pure functions and two tiny state machines, and this file
/// walks them in a millisecond.</para>
///
/// <para>The two properties that matter most are the ones a play-test would never catch: the
/// ladder's truce really does hold for a minute, and every one of these beats yields to anything
/// else that owns her face. Wave A is the LOWEST priority thing she owns.</para>
/// </summary>
public class EmiAliveTests
{
    private static Rect Body(double w = 220.0)
        => new(1000, 600, w, w * 869.0 / 859.0);

    // ---------------------------------------------------------------- gaze

    [Fact]
    public void GazeIsCentredWhenTheCursorIsOnHerCentre()
    {
        var body = Body();
        var centre = new Point(body.X + body.Width / 2, body.Y + body.Height / 2);

        var (x, y) = EmiAlive.GazeTarget(centre, body, body.Width);

        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void GazeLeansTowardTheCursorOnBothAxes()
    {
        var body = Body();
        var centre = new Point(body.X + body.Width / 2, body.Y + body.Height / 2);

        var (rx, ry) = EmiAlive.GazeTarget(new Point(centre.X + 40, centre.Y + 40), body, body.Width);
        var (lx, ly) = EmiAlive.GazeTarget(new Point(centre.X - 40, centre.Y - 40), body, body.Width);

        Assert.True(rx > 0 && ry > 0, "she should lean toward a cursor below and right of her");
        Assert.True(lx < 0 && ly < 0, "she should lean toward a cursor above and left of her");
        Assert.Equal(rx, -lx, 6);
    }

    [Fact]
    public void GazeIsCappedAtThreeDipsScaledByHerSize()
    {
        // A cursor on the other side of the desktop: the lean must still be tiny.
        var far = new Point(4000, 3000);

        foreach (double w in new[] { EmiAliveWidths.Min, 220.0, EmiAliveWidths.Max })
        {
            var body = Body(w);
            var (x, y) = EmiAlive.GazeTarget(far, body, w);
            double cap = EmiAlive.GazeMaxDip * EmiAlive.GazeScale(w);

            Assert.True(Math.Abs(x) <= cap + 1e-9, $"x lean {x} passed the {cap} cap at width {w}");
            Assert.True(Math.Abs(y) <= cap + 1e-9, $"y lean {y} passed the {cap} cap at width {w}");
            Assert.Equal(cap, Math.Abs(x), 6);
        }
    }

    [Fact]
    public void GazeSaturatesAtTheSameRELATIVEDistanceAtEverySize()
    {
        // The campus number is 3 px of lean per 60 px of offset on a 150 px EMI, so the lean tops
        // out 180 px away - 1.2 body widths. Scaling BOTH halves is what keeps that true when she
        // is dragged out to 420 DIPs; scaling only the cap would make a big EMI a twitchy one.
        foreach (double w in new[] { 152.0, 220.0, 420.0 })
        {
            var body = Body(w);
            var centre = new Point(body.X + body.Width / 2, body.Y + body.Height / 2);
            double cap = EmiAlive.GazeMaxDip * EmiAlive.GazeScale(w);

            var (justUnder, _) = EmiAlive.GazeTarget(new Point(centre.X + 1.19 * w, centre.Y), body, w);
            var (justOver, _) = EmiAlive.GazeTarget(new Point(centre.X + 1.30 * w, centre.Y), body, w);

            Assert.True(justUnder < cap - 1e-9, $"width {w} was already capped at 1.19 body widths");
            Assert.Equal(cap, justOver, 6);
        }
    }

    [Fact]
    public void GazeEaseKeepsTheCampusTimeConstantOnATenHertzPoll()
    {
        double k = EmiAlive.GazeEasePerPoll;
        Assert.InRange(k, 0.5, 0.75);

        // Six 100 ms polls of easing must land on the target as surely as the campus's ~36 render
        // frames do: within a hundredth of a DIP, which is invisible.
        double v = 0;
        for (int i = 0; i < 6; i++) v = EmiAlive.Ease(v, 3.0, k);
        Assert.True(Math.Abs(3.0 - v) < 0.01, $"the lean was still {3.0 - v} DIPs out after 600 ms");
    }

    [Fact]
    public void GazeSurvivesJunkInput()
    {
        var body = Body();
        var (x, y) = EmiAlive.GazeTarget(new Point(double.NaN, double.NaN), body, double.NaN);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
        Assert.Equal(1.0, EmiAlive.GazeScale(0), 6);
        Assert.Equal(1.0, EmiAlive.GazeScale(double.PositiveInfinity), 6);
    }

    [Fact]
    public void GazeNudgeIsOneCappedLeanInTheAskedDirection()
    {
        double cap = EmiAlive.GazeMaxDip * EmiAlive.GazeScale(220);
        Assert.Equal(cap, EmiAlive.GazeNudge(1, 220), 6);
        Assert.Equal(-cap, EmiAlive.GazeNudge(-1, 220), 6);
        Assert.Equal(cap, EmiAlive.GazeNudge(9, 220), 6);      // clamped to the direction, not scaled by it
        Assert.Equal(0, EmiAlive.GazeNudge(0, 220), 6);
    }

    // ---------------------------------------------------------------- approach

    [Fact]
    public void ApproachIsMeasuredFromHerEdgeSoASizeChangeIsNotADoorbellChange()
    {
        foreach (double w in new[] { 152.0, 220.0, 420.0 })
        {
            var body = Body(w);
            var centre = new Point(body.X + body.Width / 2, body.Y + body.Height / 2);

            var justInside = new Point(centre.X + w / 2 + EmiAlive.ApproachDip - 2, centre.Y);
            var justOutside = new Point(centre.X + w / 2 + EmiAlive.ApproachDip + 2, centre.Y);

            Assert.True(EmiAlive.WithinApproach(justInside, body));
            Assert.False(EmiAlive.WithinApproach(justOutside, body));
        }
    }

    // ---------------------------------------------------------------- blink

    [Fact]
    public void BlinkJitterStaysInsideTheOneSecondItIsAllowed()
    {
        var rng = new Random(1234);
        int lo = int.MaxValue, hi = int.MinValue;

        for (int i = 0; i < 20_000; i++)
        {
            int ms = EmiAlive.BlinkDelayMs(rng);
            Assert.InRange(ms,
                EmiAlive.BlinkEveryMs - EmiAlive.BlinkJitterMs,
                EmiAlive.BlinkEveryMs + EmiAlive.BlinkJitterMs);
            lo = Math.Min(lo, ms);
            hi = Math.Max(hi, ms);
        }

        // ...and it really does wander: a jitter that never moves is the metronome again.
        Assert.True(hi - lo > EmiAlive.BlinkJitterMs, $"the blink clock only spanned {hi - lo} ms");
        Assert.Equal(EmiAlive.BlinkEveryMs, EmiAlive.BlinkDelayMs(null!));
    }

    // ---------------------------------------------------------------- fidgets

    [Fact]
    public void FidgetDelaysAreAlwaysTwentyFiveToFiftySeconds()
    {
        var s = new EmiAlive.FidgetScheduler(new Random(7));
        bool sawUnder30 = false, sawOver45 = false;

        for (int i = 0; i < 20_000; i++)
        {
            int ms = s.NextDelayMs();
            Assert.InRange(ms, EmiAlive.FidgetMinMs, EmiAlive.FidgetMaxMs);
            if (ms < 30_000) sawUnder30 = true;
            if (ms > 45_000) sawOver45 = true;
        }

        Assert.True(sawUnder30 && sawOver45, "the fidget clock never used its range");
    }

    [Fact]
    public void StretchDelaysAreAlwaysTwentyToFortyMinutes()
    {
        var s = new EmiAlive.FidgetScheduler(new Random(11));
        for (int i = 0; i < 5_000; i++)
        {
            Assert.InRange(s.NextStretchDelayMs(), EmiAlive.StretchMinMs, EmiAlive.StretchMaxMs);
        }
    }

    [Fact]
    public void NoFidgetEverRepeatsItselfAndAllThreeGetUsed()
    {
        var s = new EmiAlive.FidgetScheduler(new Random(99));
        var seen = new HashSet<EmiFidget>();
        var last = EmiFidget.None;

        for (int i = 0; i < 10_000; i++)
        {
            var next = s.Next();
            Assert.NotEqual(EmiFidget.None, next);
            Assert.NotEqual(last, next);
            Assert.Equal(next, s.Last);
            seen.Add(next);
            last = next;
        }

        Assert.Equal(3, seen.Count);
    }

    // ---------------------------------------------------------------- the poke ladder

    [Fact]
    public void ThreePokesInsideTheWindowClimbToTheRage()
    {
        var t = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var ladder = new EmiAlive.PokeLadder();

        Assert.Equal(EmiPokeStep.Pat, ladder.Note(t));
        Assert.Equal(EmiPokeStep.Annoyed, ladder.Note(t.AddMilliseconds(500)));
        Assert.Equal(EmiPokeStep.Rage, ladder.Note(t.AddMilliseconds(1000)));
    }

    [Fact]
    public void APauseLongerThanTheWindowIsNotARunOfPokes()
    {
        var t = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var ladder = new EmiAlive.PokeLadder();

        Assert.Equal(EmiPokeStep.Pat, ladder.Note(t));
        Assert.Equal(EmiPokeStep.Annoyed, ladder.Note(t.AddMilliseconds(3_900)));
        // 4.1 s after the second: the run is over and this is an ordinary pat again.
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(t.AddMilliseconds(8_000)));
        Assert.Equal(EmiPokeStep.Annoyed, ladder.Note(t.AddMilliseconds(8_500)));
    }

    [Fact]
    public void TheTruceHoldsForAFullMinuteAndThenSheForgivesYou()
    {
        var t = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var ladder = new EmiAlive.PokeLadder();

        ladder.Note(t);
        ladder.Note(t.AddMilliseconds(300));
        Assert.Equal(EmiPokeStep.Rage, ladder.Note(t.AddMilliseconds(600)));

        // Mash her for the whole minute: not one rung is climbed.
        for (int ms = 700; ms < EmiAlive.PokeTruceMs + 600; ms += 300)
        {
            Assert.Equal(EmiPokeStep.Pat, ladder.Note(t.AddMilliseconds(ms)));
            Assert.True(ladder.InTruce(t.AddMilliseconds(ms)));
        }

        var after = t.AddMilliseconds(EmiAlive.PokeTruceMs + 1_500);
        Assert.False(ladder.InTruce(after));
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(after));
        Assert.Equal(EmiPokeStep.Annoyed, ladder.Note(after.AddMilliseconds(400)));
        Assert.Equal(EmiPokeStep.Rage, ladder.Note(after.AddMilliseconds(800)));
    }

    [Fact]
    public void ResetForgetsTheRunButNeverTheTruce()
    {
        var t = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var ladder = new EmiAlive.PokeLadder();

        ladder.Note(t);
        ladder.Note(t.AddMilliseconds(200));
        Assert.Equal(EmiPokeStep.Rage, ladder.Note(t.AddMilliseconds(400)));

        ladder.Reset();      // she was dismissed and summoned again
        Assert.True(ladder.InTruce(t.AddMilliseconds(1_000)));
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(t.AddMilliseconds(1_000)));
        Assert.Equal(0, ladder.Count);
    }

    // ---------------------------------------------------------------- the yield rule

    [Fact]
    public void WaveAOnlyEverStartsWhenNothingElseOwnsHerFace()
    {
        Assert.True(EmiAlive.CanPerk(false, false, false, false, false, false));

        // Each owner in turn, alone, is enough to refuse a wave-A beat: a chain, a question on
        // screen, an engine hold (which is what panic and every safety moment raise), her being
        // carried, and her being resized.
        Assert.False(EmiAlive.CanPerk(busy: true, false, false, false, false, false));
        Assert.False(EmiAlive.CanPerk(false, chainLive: true, false, false, false, false));
        Assert.False(EmiAlive.CanPerk(false, false, askLive: true, false, false, false));
        Assert.False(EmiAlive.CanPerk(false, false, false, holdActive: true, false, false));
        Assert.False(EmiAlive.CanPerk(false, false, false, false, dragging: true, false));
        Assert.False(EmiAlive.CanPerk(false, false, false, false, false, resizing: true));
    }

    /// <summary>Her clamp, restated here so the gaze tests cannot silently drift off her real range.</summary>
    private static class EmiAliveWidths
    {
        public const double Min = 152.0;
        public const double Max = 420.0;
    }
}
