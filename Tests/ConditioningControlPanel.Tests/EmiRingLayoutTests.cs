using System;
using System.Collections.Generic;
using System.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE CORNER CASE, which is the one that broke.
///
/// <para>QA 2026-08-29: parked bottom right, the ring came up as three cards stacked on top of
/// each other with half of them under the taskbar. The old layout fanned on a fixed radius and
/// then clamped every card into the work area, which honours "on screen" by breaking "not on top
/// of each other" - and it was only ever visible by dragging a widget into a corner by hand.</para>
///
/// <para>So the solver is a pure function of (centre, body, work area, count) and this file walks
/// every corner of every plausible desktop in a millisecond. The two laws it asserts are the two
/// the owner stated: no card leaves the WORK AREA (the taskbar is not screen space) and no two
/// cards overlap, with <see cref="EmiRingLayout.MinCardGap"/> DIPs of air.</para>
/// </summary>
public class EmiRingLayoutTests
{
    // The real numbers from EmiRingWindow, and they have to STAY the real numbers: they had
    // drifted to a 132 x 44 card off an 18 DIP gap while the window shipped 112 x 84 and 14, so
    // the sweep below was walking a card shape that did not exist. The card grew again on
    // 2026-08-30 (the label was too small to read, and the card is sized off the label), which is
    // exactly what this file is for: a bigger card must still find a feasible radius everywhere.
    private const double CardW = 136.0;
    private const double CardH = 102.0;
    private const double BodyGap = 14.0;

    // Her body at the SMALL end of AppSettings.EmiDeskWidth (152 .. 420). The big end gets its own
    // sweep below - a wide body pushes the fan outwards, which is the direction that runs out of
    // desktop first.
    private const double BodyW = 152.0;
    private const double BodyH = 154.0;

    private static void AssertLegal(EmiRingLayout.Plan plan, double workW, double workH, int count)
    {
        Assert.Equal(count, plan.Cards.Count);

        for (int i = 0; i < plan.Cards.Count; i++)
        {
            var p = plan.Cards[i];
            Assert.True(p.X >= EmiRingLayout.EdgeMargin - 1.0,
                $"card {i} at {p} ran off the left of a {workW}x{workH} work area");
            Assert.True(p.Y >= EmiRingLayout.EdgeMargin - 1.0,
                $"card {i} at {p} ran off the top of a {workW}x{workH} work area");
            Assert.True(p.X + CardW <= workW - EmiRingLayout.EdgeMargin + 1.0,
                $"card {i} at {p} ran off the right of a {workW}x{workH} work area");
            Assert.True(p.Y + CardH <= workH - EmiRingLayout.EdgeMargin + 1.0,
                $"card {i} at {p} ran under the taskbar of a {workW}x{workH} work area");
        }

        Assert.True(EmiRingLayout.AllSeparated(plan.Cards, CardW, CardH),
            $"cards overlapped in a {plan.Shape} plan on a {workW}x{workH} work area: "
            + string.Join(" ", plan.Cards));
    }

    private static EmiRingLayout.Plan Solve(double cx, double cy, double workW, double workH, int count)
        => EmiRingLayout.Solve(cx, cy, BodyW, BodyH, workW, workH, count, CardW, CardH, BodyGap);

    /// <summary>THE REGRESSION. Parked hard into the bottom-right corner, six cards, 1080p.</summary>
    [Fact]
    public void BottomRightCornerNeverStacksAndNeverGoesUnderTheTaskbar()
    {
        const double workW = 1920, workH = 1032;          // 1080p with a 48 DIP taskbar
        double cx = workW - BodyW * 0.5 - 4;
        double cy = workH - BodyH * 0.5 - 4;

        var plan = Solve(cx, cy, workW, workH, 6);
        AssertLegal(plan, workW, workH, 6);
    }

    /// <summary>Every corner, and the middle of every edge, at one to six cards.</summary>
    [Fact]
    public void EveryCornerAndEveryEdgeIsLegalAtEveryCount()
    {
        var areas = new (double W, double H)[]
        {
            (1920, 1032), (2560, 1392), (3840, 2112), (1366, 720), (1280, 984)
        };

        foreach (var (workW, workH) in areas)
        {
            var spots = new List<Point>
            {
                new(BodyW * 0.5 + 4, BodyH * 0.5 + 4),                        // top left
                new(workW - BodyW * 0.5 - 4, BodyH * 0.5 + 4),                // top right
                new(BodyW * 0.5 + 4, workH - BodyH * 0.5 - 4),                // bottom left
                new(workW - BodyW * 0.5 - 4, workH - BodyH * 0.5 - 4),        // bottom right
                new(workW * 0.5, 4 + BodyH * 0.5),                            // top edge
                new(workW * 0.5, workH - BodyH * 0.5 - 4),                    // bottom edge
                new(4 + BodyW * 0.5, workH * 0.5),                            // left edge
                new(workW - BodyW * 0.5 - 4, workH * 0.5),                    // right edge
                new(workW * 0.5, workH * 0.5),                                // dead centre
            };

            foreach (var spot in spots)
            {
                for (int n = 1; n <= 6; n++)
                {
                    var plan = Solve(spot.X, spot.Y, workW, workH, n);
                    AssertLegal(plan, workW, workH, n);
                }
            }
        }
    }

    /// <summary>
    /// The same walk with her at the WIDE end of the size slider (420 DIP). The inner radius comes
    /// off her body, so a big EMI is the case where a bigger card runs out of desktop first - and
    /// the card grew on 2026-08-30. Two of the five desktops here cannot hold a 420 DIP body and a
    /// six-card fan at all; the solver is allowed to give up on the circle, it is not allowed to
    /// stack cards or put one under the taskbar.
    /// </summary>
    [Fact]
    public void AWideBodyIsStillLegalInEveryCornerOfEveryDesktop()
    {
        const double bigW = 420.0, bigH = 425.0;

        var areas = new (double W, double H)[]
        {
            (1920, 1032), (2560, 1392), (3840, 2112), (1366, 720), (1280, 984)
        };

        foreach (var (workW, workH) in areas)
        {
            var spots = new List<Point>
            {
                new(bigW * 0.5 + 4, bigH * 0.5 + 4),
                new(workW - bigW * 0.5 - 4, bigH * 0.5 + 4),
                new(bigW * 0.5 + 4, workH - bigH * 0.5 - 4),
                new(workW - bigW * 0.5 - 4, workH - bigH * 0.5 - 4),
                new(workW * 0.5, workH * 0.5),
            };

            foreach (var spot in spots)
            {
                for (int n = 1; n <= 6; n++)
                {
                    var plan = EmiRingLayout.Solve(spot.X, spot.Y, bigW, bigH, workW, workH,
                                                   n, CardW, CardH, BodyGap);
                    AssertLegal(plan, workW, workH, n);
                }
            }
        }
    }

    /// <summary>
    /// Parked dead centre with room on every side, the fan is a full circle: the fallback shapes
    /// exist for the corners and must not creep into the ordinary case.
    /// </summary>
    [Fact]
    public void TheOpenDesktopStillGetsARing()
    {
        var plan = Solve(960, 500, 1920, 1032, 6);
        Assert.Equal(EmiRingLayout.Shape.Circle, plan.Shape);
        AssertLegal(plan, 1920, 1032, 6);
    }

    /// <summary>
    /// A work area too small for a circle still returns six legal cards: the column fallback is
    /// the honest give-up, not an exception and not a stack.
    /// </summary>
    [Fact]
    public void ACrampedWorkAreaFallsBackToColumnsAndStaysLegal()
    {
        const double workW = 700, workH = 500;
        var plan = Solve(workW - BodyW * 0.5 - 2, workH - BodyH * 0.5 - 2, workW, workH, 6);
        AssertLegal(plan, workW, workH, 6);
    }

    /// <summary>
    /// A desktop smaller than one card cannot be solved honestly, so the solver says so rather
    /// than looping: it returns the asked-for number of points at the margin and gives up.
    /// </summary>
    [Fact]
    public void ADesktopSmallerThanACardDoesNotThrow()
    {
        var plan = EmiRingLayout.Solve(20, 20, BodyW, BodyH, 90, 30, 6, CardW, CardH, BodyGap);
        Assert.Equal(6, plan.Cards.Count);
        Assert.Equal(EmiRingLayout.Shape.Column, plan.Shape);
    }

    /// <summary>Zero cards is a no-op, not an empty circle with a radius.</summary>
    [Fact]
    public void ZeroCardsSolvesToNothing()
    {
        var plan = Solve(960, 500, 1920, 1032, 0);
        Assert.Empty(plan.Cards);
    }

    /// <summary>
    /// The separation test itself, since every other assertion here leans on it: two cards that
    /// share a row must be <see cref="EmiRingLayout.MinCardGap"/> apart, and two that share a
    /// column must clear each other vertically instead.
    /// </summary>
    [Fact]
    public void SeparationIsARectangleTestAndNotAHorizontalOne()
    {
        var touching = new[] { new Point(0, 0), new Point(CardW + 2, 0) };
        Assert.False(EmiRingLayout.AllSeparated(touching, CardW, CardH));

        var apartSideways = new[] { new Point(0, 0), new Point(CardW + EmiRingLayout.MinCardGap + 1, 0) };
        Assert.True(EmiRingLayout.AllSeparated(apartSideways, CardW, CardH));

        var apartVertically = new[] { new Point(0, 0), new Point(4, CardH + EmiRingLayout.MinCardGap + 1) };
        Assert.True(EmiRingLayout.AllSeparated(apartVertically, CardW, CardH));

        var stacked = new[] { new Point(0, 0), new Point(4, 6) };
        Assert.False(EmiRingLayout.AllSeparated(stacked, CardW, CardH));
    }
}
