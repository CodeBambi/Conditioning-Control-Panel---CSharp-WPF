using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>One pendulum already hanging on a monitor: where it hangs from, and where in its swing.</summary>
public readonly record struct PendulumNeighbour(double PivotX, double Phase);

/// <summary>
/// Where the NEXT pendulum hangs from, given the ones already swinging on that monitor.
///
/// <para><b>Why this exists.</b> Every pendulum used to hang from <c>bx + bw / 2</c> - the exact
/// top centre of the monitor - so a burst of them swung through one arc and stacked on top of each
/// other ("multiple pendulums often playing on top of each other", Tock, tier2 2026-09-19). Only
/// the pivot was shared: amplitude, period, phase and rope were already rolled per flash, and
/// none of that separates two pictures hanging from the same nail.</para>
///
/// <para>Pure maths, no WPF: <see cref="FlashMotion.Create"/> is the only caller.</para>
/// </summary>
public static class FlashPendulumRig
{
    /// <summary>How many places along the legal band are considered. Odd, so the centre is one of them.</summary>
    private const int Candidates = 65;

    /// <summary>
    /// The furthest a pivot may sit from the monitor's horizontal centre and still keep the whole
    /// swing on screen. Negative means the picture is too wide to hang anywhere but the middle,
    /// which is the pre-6.10 behaviour and still the only fit.
    /// </summary>
    /// <param name="bw">Monitor width, world px.</param>
    /// <param name="mw">Unrotated media width.</param>
    /// <param name="mh">Unrotated media height.</param>
    /// <param name="rope">Pivot to media centre, world px.</param>
    /// <param name="ampRad">Swing amplitude either side of straight down.</param>
    public static double PivotSpan(double bw, double mw, double mh, double rope, double ampRad)
    {
        var sin = Math.Abs(Math.Sin(ampRad));
        var cos = Math.Abs(Math.Cos(ampRad));
        // Half-width of the picture at its most tilted, plus how far the rope carries it sideways:
        // the whole horizontal reach of this pendulum, measured from its own pivot.
        var reach = (mw * cos + mh * sin) / 2.0 + Math.Abs(rope) * sin;
        return bw / 2.0 - reach;
    }

    /// <summary>
    /// A pivot inside <c>centre +- span</c>, as far as that band reaches from every pivot already
    /// in use. With nothing else up it returns the centre, which is where a lone pendulum has
    /// always hung; with no room it returns the centre too, and the phase rule below is what
    /// keeps that case apart.
    /// </summary>
    public static double ChoosePivot(double centre, double span,
        IReadOnlyList<PendulumNeighbour>? taken, Random rng)
    {
        if (span <= 0 || taken == null || taken.Count == 0) return centre;

        var best = centre;
        var bestClearance = double.NegativeInfinity;
        for (int i = 0; i < Candidates; i++)
        {
            var candidate = centre - span + 2.0 * span * i / (Candidates - 1.0);
            var clearance = double.PositiveInfinity;
            foreach (var n in taken)
                clearance = Math.Min(clearance, Math.Abs(candidate - n.PivotX));

            // Strictly better wins outright; a tie takes a coin flip, so the symmetric band -
            // the usual case, one neighbour in the middle and two equally good ends - does not
            // always go left.
            if (clearance > bestClearance + 1e-9)
            {
                bestClearance = clearance;
                best = candidate;
            }
            else if (clearance >= bestClearance - 1e-9 && rng.Next(2) == 0)
            {
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// Where in its swing the new pendulum starts.
    ///
    /// <para>Two rules, and which one applies is decided by the gap the pivot ended up with.
    /// FAR APART (at least a picture's width): copy the nearest neighbour's phase, so the two
    /// swing in step and the gap between them HOLDS - opposite phases would walk them into each
    /// other every half period. SHARING THE ROPE LINE (a picture too wide to move the pivot, or a
    /// band already full): take the opposite phase, so at least they are never in the same place
    /// at the same time. Periods are rolled per flash, so either pairing drifts apart within a
    /// few seconds anyway; this is about the first swing, which is the one that gets looked
    /// at.</para>
    /// </summary>
    public static double ChoosePhase(double pivotX, double mediaW,
        IReadOnlyList<PendulumNeighbour>? taken, Random rng)
    {
        if (taken == null || taken.Count == 0) return rng.NextDouble() * 2.0 * Math.PI;

        var nearest = taken[0];
        var nearestGap = Math.Abs(pivotX - nearest.PivotX);
        for (int i = 1; i < taken.Count; i++)
        {
            var gap = Math.Abs(pivotX - taken[i].PivotX);
            if (gap < nearestGap) { nearestGap = gap; nearest = taken[i]; }
        }

        var phase = nearestGap >= Math.Abs(mediaW) ? nearest.Phase : nearest.Phase + Math.PI;
        return Wrap(phase);
    }

    /// <summary>Into [0, 2pi).</summary>
    public static double Wrap(double phase)
    {
        var twoPi = 2.0 * Math.PI;
        phase %= twoPi;
        return phase < 0 ? phase + twoPi : phase;
    }
}
