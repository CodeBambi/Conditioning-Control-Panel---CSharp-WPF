using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// WHERE THE SIX CARDS GO. Pure geometry, no WPF surface, no window: the ring window hands it a
/// body rect and a work area and gets back one top-left point per card.
///
/// <para>It lives here rather than inside <c>EmiRingWindow</c> for one reason: the corner case is
/// the one that broke (QA 2026-08-29 - parked bottom right, three cards stacked on top of each
/// other and half of them under the taskbar), and a layout that can only be checked by parking a
/// widget in a corner by hand is a layout that regresses. Everything below is a pure function of
/// (centre, body, work area, count), so <c>EmiRingLayoutTests</c> can walk every corner of every
/// plausible desktop in a millisecond.</para>
///
/// <para>THE TWO LAWS, in this order:</para>
/// <list type="number">
///   <item>No card may leave the WORK AREA (the taskbar is not screen space).</item>
///   <item>No card may overlap another card, ever, with <see cref="MinCardGap"/> DIPs of air.</item>
/// </list>
///
/// <para>The old code fanned on a fixed radius and then CLAMPED every card into the work area,
/// which honours law 1 by breaking law 2: three cards whose ideal spots were all off the bottom
/// right corner clamped onto the same few pixels. The solver instead finds the arc of angles that
/// keeps a whole card inside the work area, spreads the cards evenly along it, and grows the radius
/// until they clear each other. When even the largest sane radius cannot do it, it gives up on the
/// circle honestly and lays a column (or an L of columns) down the free side.</para>
///
/// <para>Coordinates: everything is DIPs in WORK-AREA space, origin at the work area's top-left.
/// The caller owns the physical-pixel conversion (THE COORDINATE TRAP, primer 10.1).</para>
/// </summary>
public static class EmiRingLayout
{
    /// <summary>Air between two neighbouring cards, in DIPs. Owner floor, 2026-08-29.</summary>
    public const double MinCardGap = 10.0;

    /// <summary>Air between a card and the edge of the work area, in DIPs.</summary>
    public const double EdgeMargin = 6.0;

    /// <summary>How the cards ended up placed. <see cref="Column"/> is the honest give-up.</summary>
    public enum Shape
    {
        /// <summary>A full circle around her.</summary>
        Circle,
        /// <summary>An arc: the part of the circle that fits on screen.</summary>
        Arc,
        /// <summary>Columns down her free side, when no arc could hold them all.</summary>
        Column
    }

    /// <summary>
    /// One solved fan. <see cref="Cards"/> are TOP-LEFT points in work-area DIPs, in slot order.
    /// </summary>
    public sealed record Plan(IReadOnlyList<Point> Cards, Shape Shape, double Radius, double StartDeg, double SpanDeg);

    /// <summary>
    /// Solve the fan.
    /// </summary>
    /// <param name="cx">Fan centre X, work-area DIPs (her body centre, or near it).</param>
    /// <param name="cy">Fan centre Y, work-area DIPs.</param>
    /// <param name="bodyW">Her silhouette's width in DIPs (the inner radius comes off it).</param>
    /// <param name="bodyH">Her silhouette's height in DIPs (only the column fallback needs it).</param>
    /// <param name="workW">Work-area width in DIPs.</param>
    /// <param name="workH">Work-area height in DIPs.</param>
    /// <param name="count">How many cards.</param>
    /// <param name="cardW">Card width in DIPs.</param>
    /// <param name="cardH">Card height in DIPs.</param>
    /// <param name="bodyGap">Air between her silhouette and a card's inner edge.</param>
    public static Plan Solve(
        double cx, double cy, double bodyW, double bodyH,
        double workW, double workH,
        int count, double cardW, double cardH, double bodyGap)
    {
        if (count <= 0) return new Plan(Array.Empty<Point>(), Shape.Circle, 0, 0, 0);

        // A desktop too small to hold one card is not a layout problem. Stack them at the margin
        // and let the caller's own clamp deal with it; nothing below can be honest here.
        if (workW < cardW + 2 * EdgeMargin || workH < cardH + 2 * EdgeMargin)
        {
            var flat = new Point[count];
            for (int i = 0; i < count; i++) flat[i] = new Point(EdgeMargin, EdgeMargin);
            return new Plan(flat, Shape.Column, 0, 0, 0);
        }

        double baseR = bodyW * 0.5 + cardW * 0.5 + bodyGap;
        double maxR = Math.Max(baseR + 1, Math.Min(workW, workH) * 0.5);

        for (double r = baseR; r <= maxR + 0.5; r += 6.0)
        {
            var (startDeg, spanDeg) = FeasibleArc(cx, cy, r, workW, workH, cardW, cardH);
            if (spanDeg <= 0) continue;

            bool full = spanDeg >= 359.0;
            var pts = new Point[count];

            for (int i = 0; i < count; i++)
            {
                double deg = full
                    ? -90.0 + i * (360.0 / count)
                    : startDeg + (i + 0.5) * (spanDeg / count);
                double a = deg * Math.PI / 180.0;
                pts[i] = new Point(
                    cx + Math.Cos(a) * r - cardW * 0.5,
                    cy + Math.Sin(a) * r - cardH * 0.5);
            }

            if (!AllInside(pts, workW, workH, cardW, cardH)) continue;
            if (!AllSeparated(pts, cardW, cardH)) continue;

            return new Plan(pts, full ? Shape.Circle : Shape.Arc, r,
                            full ? -90.0 : startDeg, full ? 360.0 : spanDeg);
        }

        return ColumnPlan(cx, cy, bodyW, bodyH, workW, workH, count, cardW, cardH, bodyGap);
    }

    // ------------------------------------------------------------------ the arc

    /// <summary>
    /// The longest run of angles, at this radius, where a WHOLE card sits inside the work area.
    /// Returned as (start degrees, span degrees); span 0 means there is no such angle at all and
    /// span &gt;= 359 means the whole circle fits.
    ///
    /// <para>Sampled rather than solved analytically: the feasible set is the intersection of four
    /// half-planes with a circle, which has a closed form and four sign cases per edge, and the
    /// closed form is where a corner park would go wrong again. 720 samples of a cosine cost
    /// nothing and cannot be subtly wrong.</para>
    /// </summary>
    private static (double StartDeg, double SpanDeg) FeasibleArc(
        double cx, double cy, double r, double workW, double workH, double cardW, double cardH)
    {
        const int steps = 720;                  // half a degree
        const double stepDeg = 360.0 / steps;

        double minX = EdgeMargin + cardW * 0.5;
        double maxX = workW - EdgeMargin - cardW * 0.5;
        double minY = EdgeMargin + cardH * 0.5;
        double maxY = workH - EdgeMargin - cardH * 0.5;

        var ok = new bool[steps];
        int okCount = 0;
        for (int i = 0; i < steps; i++)
        {
            double a = (i * stepDeg) * Math.PI / 180.0;
            double x = cx + Math.Cos(a) * r;
            double y = cy + Math.Sin(a) * r;
            ok[i] = x >= minX && x <= maxX && y >= minY && y <= maxY;
            if (ok[i]) okCount++;
        }

        if (okCount == 0) return (0, 0);
        if (okCount == steps) return (-90.0, 360.0);

        // Longest CIRCULAR run. Walking twice round is the whole trick: a run that straddles 0
        // degrees is the common case for a widget parked on the right-hand edge.
        int bestStart = 0, bestLen = 0, curStart = -1, curLen = 0;
        for (int i = 0; i < steps * 2; i++)
        {
            int k = i % steps;
            if (ok[k])
            {
                if (curLen == 0) curStart = k;
                curLen++;
                if (curLen > bestLen && curLen <= steps) { bestLen = curLen; bestStart = curStart; }
            }
            else
            {
                curLen = 0;
            }
        }

        return (bestStart * stepDeg, bestLen * stepDeg);
    }

    // ------------------------------------------------------------------ the laws

    /// <summary>Law 1: every card wholly inside the work area.</summary>
    private static bool AllInside(IReadOnlyList<Point> pts, double workW, double workH, double cardW, double cardH)
    {
        foreach (var p in pts)
        {
            if (p.X < EdgeMargin - 0.51 || p.Y < EdgeMargin - 0.51) return false;
            if (p.X + cardW > workW - EdgeMargin + 0.51) return false;
            if (p.Y + cardH > workH - EdgeMargin + 0.51) return false;
        }
        return true;
    }

    /// <summary>
    /// Law 2: no two cards overlap, with <see cref="MinCardGap"/> DIPs of air. Two axis-aligned
    /// rectangles are disjoint when they are separated on EITHER axis, so this is the exact test
    /// and not the old chord approximation (which only ever knew about the horizontal one).
    /// </summary>
    public static bool AllSeparated(IReadOnlyList<Point> pts, double cardW, double cardH)
    {
        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
            {
                double dx = Math.Abs(pts[i].X - pts[j].X);
                double dy = Math.Abs(pts[i].Y - pts[j].Y);
                if (dx < cardW + MinCardGap - 0.51 && dy < cardH + MinCardGap - 0.51) return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ the fallback

    /// <summary>
    /// No arc could hold them: lay the cards in a column down whichever side of her has the most
    /// room, and start a second column further out when one runs out of height. Ugly on purpose -
    /// it is the shape that says "you have parked her somewhere with no room", and it still obeys
    /// both laws.
    /// </summary>
    private static Plan ColumnPlan(
        double cx, double cy, double bodyW, double bodyH,
        double workW, double workH,
        int count, double cardW, double cardH, double bodyGap)
    {
        double pitchY = cardH + MinCardGap;
        double pitchX = cardW + MinCardGap;

        int rows = Math.Max(1, (int)Math.Floor((workH - 2 * EdgeMargin + MinCardGap) / pitchY));
        int cols = (int)Math.Ceiling(count / (double)rows);

        bool toLeft = cx > workW * 0.5;
        double firstColX = toLeft
            ? cx - bodyW * 0.5 - bodyGap - cardW
            : cx + bodyW * 0.5 + bodyGap;

        int perCol = (int)Math.Ceiling(count / (double)cols);
        double colHeight = perCol * pitchY - MinCardGap;
        double topY = cy - colHeight * 0.5;
        topY = Math.Max(EdgeMargin, Math.Min(workH - EdgeMargin - colHeight, topY));
        if (topY < EdgeMargin) topY = EdgeMargin;

        var pts = new Point[count];
        for (int i = 0; i < count; i++)
        {
            int col = i / perCol;
            int row = i % perCol;

            double x = toLeft ? firstColX - col * pitchX : firstColX + col * pitchX;
            double y = topY + row * pitchY;

            x = Math.Max(EdgeMargin, Math.Min(workW - EdgeMargin - cardW, x));
            y = Math.Max(EdgeMargin, Math.Min(workH - EdgeMargin - cardH, y));
            pts[i] = new Point(x, y);
        }

        return new Plan(pts, Shape.Column, 0, 0, 0);
    }
}
