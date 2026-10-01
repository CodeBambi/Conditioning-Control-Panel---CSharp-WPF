using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>What a press on a Flicker Deck flash turned into.</summary>
public enum FlickerPress
{
    /// <summary>The flash is still mid lift or mid flip: the click is swallowed and changes nothing.</summary>
    Ignored,
    /// <summary>Lift, raise, flip to a new picture.</summary>
    Flip,
    /// <summary>This click reached BreakAt: the flash shatters, which is its dismissal.</summary>
    Shatter,
}

/// <summary>One frame's events out of <see cref="FlickerDeck.Step"/>.</summary>
[Flags]
public enum FlickerEvents
{
    None = 0,
    /// <summary>The card is in the air: bring it to the front now (never on the click frame).</summary>
    Raise = 1,
    /// <summary>The flip reached its edge with the new picture ready: swap the frames now.</summary>
    Swap = 2,
    /// <summary>The flip waited too long for a picture and opened on the old one: drop any late arrival.</summary>
    GaveUp = 4,
}

/// <summary>How one Flicker Deck flash is drawn this frame, relative to its resting rect.</summary>
public readonly record struct FlickerPose(
    double ScaleX,      // flip squash, 0..1 (cos of the flip, never mirrored)
    double Scale,       // lift scale about the centre, 1..1.1
    double Lift,        // 0..1, drives the shadow and the rise
    double RiseFrac,    // vertical offset as a share of the height (negative = up)
    double WobbleRad,   // rotation about the centre
    double PeekFrac,    // outward peek as a share of the width
    double Crack,       // 0 none, 0.4 faint, 1 full
    double SquashY = 1, // juice: the press squashes the card flat before it lifts
    double Sheen = 0,   // juice: 0..1 strength of the lit band sweeping the face mid-flip
    double SheenPos = 0,// juice: 0..1 across the face, left to right
    double CrackReach = 1); // juice: 0..1 how far the hairlines have run from the impact point

/// <summary>Per-flash Flicker Deck state. Lives on the compositor item; stepped by the layer tick.</summary>
public sealed class FlickerDeckState
{
    /// <summary>Phase variety for the idle wobble, so a stack of cards never sways in step.</summary>
    public int Index;
    public int Flips;
    /// <summary>3..5, rolled at spawn. The click that reaches it shatters instead of flipping.</summary>
    public int BreakAt;
    /// <summary>Idle wobble clock, seconds (runs at half speed under Reduced).</summary>
    public double Clock;
    /// <summary>Seconds since the press, or -1 while resting.</summary>
    public double LiftT = -1;
    /// <summary>Seconds into the flip, or -1 before it starts.</summary>
    public double FlipT = -1;
    /// <summary>Seconds the flip has held at its edge waiting for the next picture.</summary>
    public double HoldSec;
    public bool Raised;
    public bool Swapped;
    /// <summary>Seconds since the last flip began (drives the decaying ring).</summary>
    public double SinceFlip = 99;
    /// <summary>0..1 peek progress; target set by <see cref="Hover"/>.</summary>
    public double Peek;
    /// <summary>True while the cursor is over this flash AND it sits under another one.</summary>
    public bool Hover;
    /// <summary>Crack polylines, normalised 0..1 over the picture box, rolled at spawn.</summary>
    public double[][] Cracks = Array.Empty<double[]>();
    /// <summary>
    /// The pool had nothing new to flip to: the flip opens on the old picture as soon as it
    /// reaches its edge instead of holding edge-on for <see cref="FlickerDeck.MaxHoldSec"/>.
    /// Reset on every press.
    /// </summary>
    public bool NoPicture;
    /// <summary>Juice: seconds since the last flip settled (drives the landing wobble).</summary>
    public double SinceLand = 99;
    /// <summary>Juice: seconds since the last flip's face landed (drives the crack growth).</summary>
    public double SinceSwap = 99;

    public bool Busy => LiftT >= 0;
}

/// <summary>
/// Super: Flicker Deck. With the switch on, every compositor flash is a card: a click lifts it,
/// raises it in the air, flips it to a new picture, and wobbles it a little more each time; the
/// click that reaches its BreakAt shatters it (see <see cref="FlickerShatter"/>). WPF-free, Skia-free
/// and App-free like <see cref="FlashShatter"/>: the layer steps and samples, the tests drive the
/// same calls. Constants are the approved mockup's mkFlash.
/// </summary>
public static class FlickerDeck
{
    public const int BreakAtMin = 3, BreakAtMax = 5;

    /// <summary>The lift: a sine hump over <see cref="LiftSpanSec"/>, flip starting <see cref="LiftLeadSec"/> in.</summary>
    public const double LiftLeadSec = 0.18;
    public const double LiftSpanSec = 0.58;
    /// <summary>The z-order changes this far into the lift, while the card is already in the air.</summary>
    public const double RaiseAtSec = 0.12;
    public const double LiftScale = 0.10;
    public const double LiftRiseFrac = 0.10;

    /// <summary>The flip: scaleX = cos over this span, picture swaps at the midpoint.</summary>
    public const double FlipSec = 0.30;
    /// <summary>How long a flip waits edge-on for its next picture before opening on the old one.</summary>
    public const double MaxHoldSec = 1.5;
    /// <summary>Edge-on never quite vanishes, so a held flip still reads as a card.</summary>
    public const double MinScaleX = 0.06;

    public const double WobbleMaxRad = 0.15;
    public const double WobblePow = 1.4;
    public const double WobbleRate = 9.0, WobbleRatePerIndex = 1.3;
    public const double RingSec = 0.7, RingRadPerSec = 40.0, RingShare = 0.9;

    public const double PeekFrac = 0.07, PeekSec = 0.3;

    // Juice round (owner, 2026-10-01). Feel only: the timings above stay the card's own.
    /// <summary>Anticipation: the press squashes the card 5% flat (and 2.5% wide) over its first 80 ms.</summary>
    public const double AnticipationSec = 0.08, AnticipationSquash = 0.05;
    /// <summary>The opening half of the flip pops 5% past full width (back ease, s = 1.2) before it settles.</summary>
    public const double FlipBack = 1.2, FlipOvershoot = 0.053;
    /// <summary>The landing: one soft damped bounce on the new face once the lift is down.</summary>
    public const double LandSec = 0.5, LandAmp = 0.045, LandHz = 3.2, LandDecay = 7.0;
    /// <summary>The lit band that sweeps the face through the flip: tinted, low, one pulse per flip.</summary>
    public const double SheenMax = 0.3;
    /// <summary>New hairlines run out from the impact point over this long once the new face lands.</summary>
    public const double CrackGrowSec = 0.35;
    /// <summary>Dust motes shaken loose when the cracks first show and when they deepen.</summary>
    public const int MotesFaint = 4, MotesDeep = 7;
    /// <summary>An exiting card eases out of its pose to rest over this long instead of snapping.</summary>
    public const double ExitSettleSec = 0.18;

    /// <summary>
    /// Does a flash become a card? Super on, drawn by the compositor and clickable by the mouse.
    /// Anything owned that would also act on the press or the dismiss wins and Super stands down
    /// for that flash: a v2 preview, owned dragging, owned Shatter. A remix (one hero picture
    /// across the screens) and Natasha's red flash (a click is how you dodge it) stay plain too.
    /// </summary>
    public static bool Applies(bool superOn, bool usesLayer, bool clickable, bool previewV2, bool isRemix,
        bool isNatasha, bool dragOwnedOn, bool shatterOwnedOn)
        => superOn && usesLayer && clickable && !previewV2 && !isRemix && !isNatasha
           && !dragOwnedOn && !shatterOwnedOn;

    /// <summary>A fresh card with its BreakAt rolled and its cracks laid out.</summary>
    public static FlickerDeckState Create(int index, Random rng) => new()
    {
        Index = index,
        BreakAt = rng.Next(BreakAtMin, BreakAtMax + 1),
        Clock = rng.NextDouble() * 10.0,
        Cracks = CrackLines(rng),
    };

    /// <summary>
    /// A click on this card. Busy cards swallow it. The click that would make the flip count
    /// reach <see cref="FlickerDeckState.BreakAt"/> shatters instead. Off motion skips the lift:
    /// the card is raised at once and swaps the moment its picture lands.
    /// </summary>
    public static FlickerPress Press(FlickerDeckState s, MotionLevel level)
    {
        if (s.Busy) return FlickerPress.Ignored;
        if (s.Flips + 1 >= s.BreakAt)
        {
            s.Flips++;
            return FlickerPress.Shatter;
        }
        s.LiftT = 0;
        s.FlipT = -1;
        s.HoldSec = 0;
        s.NoPicture = false;
        s.Raised = false;
        s.Swapped = false;
        if (level == MotionLevel.Off)
        {
            // No air time: count the flip now and go straight to the edge, waiting on the picture.
            s.LiftT = LiftLeadSec;
            s.FlipT = FlipSec / 2;
            s.Flips++;
            s.SinceFlip = 0;
        }
        return FlickerPress.Flip;
    }

    /// <summary>
    /// Advance by <paramref name="dt"/>. <paramref name="pictureReady"/> says whether the next
    /// picture has landed. Returns the events the caller must act on this frame.
    /// </summary>
    public static FlickerEvents Step(FlickerDeckState s, double dt, bool pictureReady, MotionLevel level)
    {
        if (dt <= 0) return FlickerEvents.None;
        var ev = FlickerEvents.None;
        var off = level == MotionLevel.Off;
        s.Clock += dt * (level == MotionLevel.Reduced ? 0.5 : 1.0);
        s.SinceFlip += dt;
        s.SinceLand += dt;
        s.SinceSwap += dt;

        var target = s.Hover && !off ? 1.0 : 0.0;
        if (s.Peek != target)
        {
            var step = dt / PeekSec;
            s.Peek = target > s.Peek ? Math.Min(target, s.Peek + step) : Math.Max(target, s.Peek - step);
        }

        if (!s.Busy) return ev;

        if (!s.Raised && (off || s.LiftT >= RaiseAtSec))
        {
            s.Raised = true;
            ev |= FlickerEvents.Raise;
        }

        // Holding at the edge: the clock stops until the picture lands or the wait runs out.
        if (s.FlipT >= 0 && !s.Swapped && s.FlipT >= FlipSec / 2)
        {
            if (pictureReady)
            {
                s.Swapped = true;
                s.SinceSwap = 0;
                ev |= FlickerEvents.Swap;
            }
            else
            {
                s.HoldSec += dt;
                if (s.HoldSec < MaxHoldSec && !s.NoPicture) return ev;
                s.Swapped = true;
                s.SinceSwap = 0;
                ev |= FlickerEvents.GaveUp;
            }
            if (off) { s.LiftT = -1; s.FlipT = -1; return ev; }
        }

        s.LiftT += dt;
        if (s.FlipT < 0 && s.LiftT >= LiftLeadSec)
        {
            s.FlipT = s.LiftT - LiftLeadSec;
            s.Flips++;
            s.SinceFlip = s.FlipT;
        }
        else if (s.FlipT >= 0)
        {
            // Never step past the midpoint before the swap is decided.
            s.FlipT = s.Swapped ? s.FlipT + dt : Math.Min(FlipSec / 2, s.FlipT + dt);
        }

        if (s.LiftT >= LiftSpanSec && s.FlipT >= FlipSec)
        {
            s.LiftT = -1;
            s.FlipT = -1;
            s.SinceLand = 0;
        }
        return ev;
    }

    /// <summary>The pose to draw this frame. Off draws the card still, cracks included.</summary>
    public static FlickerPose Sample(FlickerDeckState s, MotionLevel level)
    {
        var crack = CrackAmount(s.Flips, s.BreakAt);
        if (level == MotionLevel.Off) return new FlickerPose(1, 1, 0, 0, 0, 0, crack);
        var amp = level == MotionLevel.Reduced ? 0.5 : 1.0;

        var lift = s.LiftT >= 0 && s.LiftT <= LiftSpanSec ? Math.Sin(Math.PI * s.LiftT / LiftSpanSec) : 0.0;
        var flipping = s.FlipT >= 0 && s.FlipT <= FlipSec;
        var scaleX = flipping ? FlipWidth(s.FlipT) : 1.0;
        var ant = Anticipation(s.LiftT) * amp;
        scaleX *= 1 + AnticipationSquash * 0.5 * ant;
        var scale = (1 + LiftScale * lift * amp) * (1 + Land(s.SinceLand) * amp);
        var (sheen, sheenPos) = flipping ? SheenAt(s.FlipT) : (0.0, 0.0);
        var (shown, reach) = CrackShown(s, level);
        var peek = Ease(s.Peek) * PeekFrac * amp;
        return new FlickerPose(scaleX, scale, lift * amp, -LiftRiseFrac * lift * amp,
            Wobble(s, amp), peek, shown, 1 - AnticipationSquash * ant, sheen * amp, sheenPos, reach);
    }

    /// <summary>
    /// The card's width through the flip: the closing half is the plain cosine, the opening half a
    /// back ease that pops about 5% wide before it settles on 1. Never mirrored, never thinner than
    /// <see cref="MinScaleX"/>.
    /// </summary>
    public static double FlipWidth(double flipT)
    {
        var half = FlipSec / 2;
        if (flipT <= half) return Math.Max(MinScaleX, Math.Cos(Math.PI * Math.Max(0, flipT) / FlipSec));
        var u = Math.Clamp((flipT - half) / half, 0, 1) - 1;
        return Math.Max(MinScaleX, 1 + (FlipBack + 1) * u * u * u + FlipBack * u * u);
    }

    /// <summary>The press: one 0..1..0 sine dip over <see cref="AnticipationSec"/> from the click.</summary>
    public static double Anticipation(double liftT)
        => liftT >= 0 && liftT < AnticipationSec ? Math.Sin(Math.PI * liftT / AnticipationSec) : 0.0;

    /// <summary>The landing bounce as a scale offset: a damped sine, zero at touchdown and gone by <see cref="LandSec"/>.</summary>
    public static double Land(double sinceLand)
    {
        if (sinceLand < 0 || sinceLand >= LandSec) return 0;
        var fade = 1 - sinceLand / LandSec;
        return LandAmp * Math.Exp(-LandDecay * sinceLand) * Math.Sin(2 * Math.PI * LandHz * sinceLand) * fade;
    }

    /// <summary>
    /// The lit band: it crosses the face left to right over the flip and is brightest edge-on, one
    /// sin-squared pulse per flip (a flip takes 0.58 s at least, so well under the 3 Hz photosafe line).
    /// </summary>
    public static (double Alpha, double Pos) SheenAt(double flipT)
    {
        var p = Math.Clamp(flipT / FlipSec, 0, 1);
        var k = Math.Sin(Math.PI * p);
        return (SheenMax * k * k, p);
    }

    /// <summary>
    /// The crack drawn this frame. Before the new face lands it keeps the old face's crack; after,
    /// it eases up to the new amount over <see cref="CrackGrowSec"/> (twice that under Reduced), and
    /// a first crack runs out from the impact point instead of appearing whole. Off shows it at once.
    /// </summary>
    public static (double Amount, double Reach) CrackShown(FlickerDeckState s, MotionLevel level)
    {
        var target = CrackAmount(s.Flips, s.BreakAt);
        if (level == MotionLevel.Off) return (target, 1);
        var prev = s.Flips > 0 ? CrackAmount(s.Flips - 1, s.BreakAt) : 0.0;
        if (target <= prev) return (target, 1);
        if (s.Busy && !s.Swapped) return (prev, 1);
        var grow = CrackGrowSec * (level == MotionLevel.Reduced ? 2 : 1);
        var g = Ease(s.SinceSwap / grow);
        return (prev + (target - prev) * g, prev > 0 ? 1 : g);
    }

    /// <summary>How many dust motes the crack sheds as this flip's face lands: only when the crack deepens.</summary>
    public static int CrackMotes(int flips, int breakAt, MotionLevel level)
    {
        if (level == MotionLevel.Off || flips <= 0) return 0;
        var now = CrackAmount(flips, breakAt);
        if (now <= CrackAmount(flips - 1, breakAt)) return 0;
        var n = now >= 0.5 ? MotesDeep : MotesFaint;
        return level == MotionLevel.Reduced ? n / 2 : n;
    }

    /// <summary>
    /// An exiting card eases out of its pose: <paramref name="exitSec"/> into the exit, every
    /// deviation from rest is scaled down by an ease-out to nothing at <see cref="ExitSettleSec"/>.
    /// The crack stays as it was; the exit's own fade takes it.
    /// </summary>
    public static FlickerPose Settle(FlickerPose p, double exitSec)
    {
        var u = Math.Clamp(exitSec / ExitSettleSec, 0, 1);
        var k = (1 - u) * (1 - u) * (1 - u);   // 1 minus a cubic ease-out
        static double To(double v, double rest, double k) => rest + (v - rest) * k;
        return new FlickerPose(To(p.ScaleX, 1, k), To(p.Scale, 1, k), p.Lift * k, p.RiseFrac * k,
            p.WobbleRad * k, p.PeekFrac * k, p.Crack, To(p.SquashY, 1, k), p.Sheen * k, p.SheenPos, p.CrackReach);
    }

    /// <summary>Idle sway that grows with the flips, plus a decaying ring after each flip.</summary>
    public static double Wobble(FlickerDeckState s, double amp = 1.0)
    {
        if (s.BreakAt <= 0) return 0;
        var a = Math.Pow(Math.Clamp((double)s.Flips / s.BreakAt, 0, 1), WobblePow) * WobbleMaxRad * amp;
        if (a <= 0) return 0;
        var w = Math.Sin(s.Clock * (WobbleRate + s.Index * WobbleRatePerIndex)) * a;
        if (s.SinceFlip >= 0 && s.SinceFlip < RingSec)
            w += Math.Sin(s.SinceFlip * RingRadPerSec) * a * RingShare * (1 - s.SinceFlip / RingSec);
        return w;
    }

    /// <summary>Subtle cracks on the flip before the last, a hint of them one earlier (0.6 and 0.25 of full).</summary>
    public static double CrackAmount(int flips, int breakAt)
        => flips == breakAt - 1 ? 0.6 : flips == breakAt - 2 ? 0.25 : 0.0;

    /// <summary>True when the card needs a redraw this frame even with nothing else moving.</summary>
    public static bool Animating(FlickerDeckState s)
        => s.Busy || s.Flips > 0 || (s.Peek > 0 && s.Peek < 1) || (s.Hover ? s.Peek < 1 : s.Peek > 0);

    /// <summary>
    /// Unit direction a buried card peeks along: away from the middle of the cluster it sits in.
    /// A card dead on the centre peeks up and out to the right.
    /// </summary>
    public static (double Dx, double Dy) PeekDirection(double cardCx, double cardCy, double clusterCx, double clusterCy)
    {
        double vx = cardCx - clusterCx, vy = cardCy - clusterCy;
        var len = Math.Sqrt(vx * vx + vy * vy);
        if (len < 1e-6) return (Math.Sqrt(0.5), -Math.Sqrt(0.5));
        return (vx / len, vy / len);
    }

    /// <summary>
    /// A few cracks radiating from an impact point near the middle, each a short jittered
    /// polyline (x0,y0,x1,y1,...) in 0..1 over the picture box.
    /// </summary>
    public static double[][] CrackLines(Random rng)
    {
        var n = 3 + rng.Next(2);
        double ix = 0.4 + rng.NextDouble() * 0.2, iy = 0.4 + rng.NextDouble() * 0.2;
        var lines = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var a = (i + rng.NextDouble() * 0.6) / n * Math.PI * 2;
            var segs = 3;
            var pts = new double[(segs + 1) * 2];
            pts[0] = ix; pts[1] = iy;
            double x = ix, y = iy, len = 0.07 + rng.NextDouble() * 0.07;
            for (var k = 1; k <= segs; k++)
            {
                a += (rng.NextDouble() - 0.5) * 0.7;
                x = Math.Clamp(x + Math.Cos(a) * len, 0, 1);
                y = Math.Clamp(y + Math.Sin(a) * len, 0, 1);
                pts[k * 2] = x; pts[k * 2 + 1] = y;
            }
            lines[i] = pts;
        }
        return lines;
    }

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
