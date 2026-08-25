using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Pointer;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The arithmetic behind taking the ink read off the per-move path</b>, checked with no window
/// and no desktop, because none of it is about a window.
///
/// <para>Measured before any of this was written, on the running product at maximum settings: one
/// bubble placement costs paint 2.0-2.5 ms and an OS read-back of 7.7-9.1 ms, of which the z-order
/// walk is 0.07 ms, the hit test 0.09 ms and the <b>ink read 7.6 ms</b> — 98 % of the read-back,
/// roughly 400 <c>GetPixel</c> calls, running on the UI thread for up to three targets every 30 ms.
/// <see cref="PointerInkSweep"/> spreads that read over <see cref="PointerInkSweep.Phases"/>
/// placements. These facts are what say it may.</para>
///
/// <para><b>What is NOT proved here</b>, and it is most of what matters to a user: nothing in this
/// file touches a window, so nothing in it says a bubble is drawn, visible, hittable or smooth. The
/// real-window half is in <see cref="PointerCapabilityTests"/>, and the product-level before/after
/// at maximum settings is a headed measurement neither file takes.</para>
/// </summary>
public class PointerInkSweepTests
{
    /// <summary>
    /// The product's own legal target sizes: <c>PointerTargetRequest.MinimumSide</c> up to
    /// <c>BubblePopField.MaxSize</c>, which is the whole band <c>BubblePopField.SizeFor</c> can
    /// produce (<c>Services/BubbleSizing.cs:70,81</c>).
    /// </summary>
    private static IEnumerable<int> LegalSides
    {
        get
        {
            for (var side = PointerTargetRequest.MinimumSide; side <= BubblePopField.MaxSize; side++)
            {
                yield return side;
            }
        }
    }

    [Fact]
    public void TheGridIsTheDiscsOwnBoxAndStride_PinnedAsFiguresRatherThanReDerivedThroughTheProduct()
    {
        // LITERALS, not a re-derivation through DiscBox and SampleStep, which would be tautological
        // — the same reason the delivery run's 484 is a literal. For a 160 px target: the inset is
        // max(ControlMargin + 1, 160/10) = 16, so the disc box is 128x128; the stride that keeps the
        // read near 400 samples is floor(sqrt(128*128/400)) = 6; and 128/6 rounded up is 22 points
        // on each axis, 484 in all. That 484 is the figure the delivery run already pins through
        // the capability's own public Observe, so the two agree by construction or one of them reds.
        Assert.Equal(new PointerInkGrid(16, 16, 6, 22, 22), PointerInkSweep.GridFor(160, 160));
        Assert.Equal(484, PointerInkSweep.GridFor(160, 160).Points);

        // The smallest legal target: inset max(4, 6) = 6, box 48x48, stride floor(sqrt(2304/400)) = 2.
        Assert.Equal(new PointerInkGrid(6, 6, 2, 24, 24), PointerInkSweep.GridFor(60, 60));

        // The largest: inset max(4, 50) = 50, box 400x400, stride floor(sqrt(160000/400)) = 20.
        Assert.Equal(new PointerInkGrid(50, 50, 20, 20, 20), PointerInkSweep.GridFor(500, 500));

        // The read stays near its budget whatever the size — the property the stride exists for, and
        // the reason a 500 px target does not cost 70x a 60 px one. The band is 400..900 rather than
        // a flat 400 because the stride is an integer: at 73 px the box is 61 wide and the stride
        // floors from 3.05 to 3, which oversamples to 900. That is the WORST case over every legal
        // size, and the sweep divides it by eight like any other.
        var worst = LegalSides.Max(side => PointerInkSweep.GridFor(side, side).Points);
        var best = LegalSides.Min(side => PointerInkSweep.GridFor(side, side).Points);
        Assert.Equal(900, worst);
        Assert.Equal(400, best);
    }

    [Fact]
    public void TheWholeDiscReadStillVisitsEveryGridPointOnce_InTheOrderItAlwaysDid()
    {
        // A stride of 1 is the unswept read, and it must be EXACTLY the nested loop this class
        // replaced — same points, same order — or the change silently altered what "the whole disc"
        // means. The expectation is built here, independently, from the grid's own extents.
        //
        // Accumulated and asserted at depth 0, and the sides COUNTED, so an empty loop cannot pass.
        var checkedSides = 0;
        var disagreed = new List<string>();
        foreach (var side in new[] { 60, 97, 160, 313, 500 })
        {
            checkedSides++;
            var grid = PointerInkSweep.GridFor(side, side);
            var expected = new List<(int, int)>();
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var column = 0; column < grid.Columns; column++)
                {
                    expected.Add(grid.At(row, column));
                }
            }

            var walked = Walk(grid, phase: PointerInkSweep.WholeDisc, stride: 1);
            if (!expected.SequenceEqual(walked.Select(p => ((int)p.X, (int)p.Y))))
            {
                disagreed.Add($"{side}x{side}: expected {expected.Count} points, walked {walked.Count}");
            }
        }

        Assert.Equal(5, checkedSides);
        Assert.Empty(disagreed);
    }

    [Fact]
    public void TheEightPhasesPARTITIONTheDisc_SoOneSweepReadsEveryPointExactlyOnceAndNoneTwice()
    {
        // The bounded re-proof cadence, stated as a set identity rather than a hope: eight
        // consecutive placements read the whole disc between them, and no placement re-reads a
        // point another one already had. At the 30 ms step that is 240 ms end to end.
        var checkedSides = 0;
        var broken = new List<string>();
        foreach (var side in new[] { 60, 97, 160, 313, 500 })
        {
            checkedSides++;
            var grid = PointerInkSweep.GridFor(side, side);
            var seen = new List<(int X, int Y)>();
            for (var phase = 0; phase < PointerInkSweep.Phases; phase++)
            {
                seen.AddRange(Walk(grid, phase, PointerInkSweep.Phases));
            }

            if (seen.Count != grid.Points || seen.Distinct().Count() != grid.Points
                || !seen.ToHashSet().SetEquals(Walk(grid, PointerInkSweep.WholeDisc, 1)))
            {
                broken.Add($"{side}x{side}: {PointerInkSweep.Phases} phases read {seen.Count} points "
                    + $"({seen.Distinct().Count()} distinct) of a {grid.Points}-point grid");
            }
        }

        Assert.Equal(5, checkedSides);
        Assert.Empty(broken);
    }

    [Fact]
    public void EveryPhaseReadsTheDiscsOwnCENTRE_AtEveryLegalSize_OrAGoodBubbleWouldReadBlank()
    {
        // THE NO-FALSE-BLANK GUARANTEE. A phase that only ever sampled the disc box's corners would
        // find no ink on a perfectly painted bubble and report it blank, so every phase must reach
        // the region that is unconditionally painted. The central third of the box qualifies: its
        // furthest corner sits at (1/3, 1/3) of the ellipse's semi-axes, and (1/3)^2 + (1/3)^2 =
        // 0.22 < 1, so the whole of it is inside the disc at every size.
        Assert.True((1 / 3.0 * (1 / 3.0)) + (1 / 3.0 * (1 / 3.0)) < 1.0);

        var checkedPhases = 0;
        var blind = new List<string>();
        foreach (var side in LegalSides)
        {
            var grid = PointerInkSweep.GridFor(side, side);
            for (var phase = 0; phase < PointerInkSweep.Phases; phase++)
            {
                checkedPhases++;
                if (CentralPoints(grid, phase) == 0)
                {
                    blind.Add($"phase {phase} of a {side}x{side} target reads no point in the central third of its "
                        + "disc, so it would count zero ink on a bubble that is drawn perfectly and the capability "
                        + "would call a good target blank");
                }
            }
        }

        // COUNTED at depth 0: an empty legal-size band would otherwise pass this without checking one.
        Assert.Equal(441 * PointerInkSweep.Phases, checkedPhases);
        Assert.Empty(blind);
    }

    [Fact]
    public void ThatGuaranteeIsTheDIAGONALS_AndAPlainRowMajorStrideWouldNotHoldIt()
    {
        // The refutation the design rests on, run rather than asserted. Flattening the grid
        // row-major and taking every eighth point makes a row's phase depend only on its column
        // wherever the column count is a multiple of eight, and a central band narrower than eight
        // columns then misses phases outright. Shifting the start by the row removes it.
        var diagonalFailures = 0;
        var rowMajorFailures = 0;
        var firstRowMajorFailure = string.Empty;

        for (var width = 60; width <= 500; width += 1)
        {
            for (var height = 60; height <= 500; height += 7)
            {
                var grid = PointerInkSweep.GridFor(width, height);
                for (var phase = 0; phase < PointerInkSweep.Phases; phase++)
                {
                    if (CentralPoints(grid, phase) == 0)
                    {
                        diagonalFailures++;
                    }

                    if (CentralPointsRowMajor(grid, phase) == 0)
                    {
                        rowMajorFailures++;
                        if (firstRowMajorFailure.Length == 0)
                        {
                            firstRowMajorFailure = $"{width}x{height} phase {phase} "
                                + $"({grid.Columns} columns, {grid.Rows} rows)";
                        }
                    }
                }
            }
        }

        Assert.Equal(0, diagonalFailures);
        Assert.True(rowMajorFailures > 0,
            "a plain row-major index % Phases sweep covered every phase's centre at every size tried, so the "
            + "diagonal in PointerInkSweep.FirstColumn is buying nothing and its remarks are wrong");

        // Named, so the claim is a measurement and not a shrug. The shape of every one of them is
        // the same: a column count that is a multiple of eight, which makes the row contribute
        // nothing to a row-major phase.
        Assert.Equal(4718, rowMajorFailures);
        Assert.Equal("60x88 phase 2 (16 columns, 26 rows)", firstRowMajorFailure);
    }

    [Fact]
    public void ASweptReadIsAnEIGHTHOfTheWholeOne_WhichIsTheWholePointOfIt()
    {
        var checkedPhases = 0;
        var offShare = new List<string>();
        var worstFraction = 0.0;
        foreach (var side in LegalSides)
        {
            var grid = PointerInkSweep.GridFor(side, side);
            for (var phase = 0; phase < PointerInkSweep.Phases; phase++)
            {
                checkedPhases++;
                var swept = Walk(grid, phase, PointerInkSweep.Phases).Count;
                var whole = grid.Points;
                worstFraction = Math.Max(worstFraction, swept / (double)whole);
                if (swept > (whole / PointerInkSweep.Phases) + grid.Rows
                    || swept * PointerInkSweep.Phases < whole - (grid.Rows * PointerInkSweep.Phases))
                {
                    offShare.Add($"phase {phase} of {side}x{side} reads {swept} of {whole}");
                }
            }
        }

        Assert.Equal(441 * PointerInkSweep.Phases, checkedPhases);
        Assert.Empty(offShare);
        Assert.True(worstFraction < 0.2,
            $"the greediest phase over every legal size still reads {worstFraction:P1} of the disc, which is not "
            + "the eighth the 7.6 ms measurement says this had to become");
    }

    [Fact]
    public void ThePhaseCursorCyclesAndAWholeDiscReadIsNotAPhase()
    {
        // A whole-disc read parks the cursor at WholeDisc, and the next sweep starts at phase 0, so
        // the read after any full re-proof begins the sweep rather than resuming mid-cycle.
        Assert.Equal(PointerInkSweep.WholeDisc, PointerInkSweep.Next(4, wholeDisc: true));
        Assert.Equal(0, PointerInkSweep.Next(PointerInkSweep.WholeDisc, wholeDisc: false));

        var phase = PointerInkSweep.WholeDisc;
        var visited = new List<int>();
        for (var step = 0; step < PointerInkSweep.Phases * 2; step++)
        {
            phase = PointerInkSweep.Next(phase, wholeDisc: false);
            visited.Add(phase);
        }

        Assert.Equal(Enumerable.Range(0, PointerInkSweep.Phases), visited.Take(PointerInkSweep.Phases));
        Assert.Equal(visited.Take(PointerInkSweep.Phases), visited.Skip(PointerInkSweep.Phases));

        // And a stride of one ignores the phase entirely, which is what lets the swept and unswept
        // reads be one loop in ReadInk rather than two that can drift apart.
        foreach (var any in new[] { PointerInkSweep.WholeDisc, 0, 3, 7 })
        {
            Assert.Equal(0, PointerInkSweep.FirstColumn(row: 5, phase: any, stride: 1));
        }
    }

    /// <summary>Every point one read visits, in the order it visits them — the product's own walk,
    /// driven here exactly as <c>Win32PointerSurface.ReadInk</c> drives it.</summary>
    private static List<(int X, int Y)> Walk(PointerInkGrid grid, int phase, int stride)
    {
        var points = new List<(int X, int Y)>();
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var column = PointerInkSweep.FirstColumn(row, phase, stride);
                 column < grid.Columns;
                 column += stride)
            {
                points.Add(grid.At(row, column));
            }
        }

        return points;
    }

    /// <summary>
    /// How many of this phase's points — the PRODUCT's own walk, not a restatement of it — fall in
    /// the central third of the disc's box, the region inside the inscribed ellipse at every size
    /// and therefore ink on any target whose paint landed.
    /// </summary>
    private static int CentralPoints(PointerInkGrid grid, int phase)
    {
        var left = grid.Left + (grid.Columns / 3 * grid.Step);
        var right = grid.Left + (2 * grid.Columns / 3 * grid.Step);
        var top = grid.Top + (grid.Rows / 3 * grid.Step);
        var bottom = grid.Top + (2 * grid.Rows / 3 * grid.Step);
        return Walk(grid, phase, PointerInkSweep.Phases)
            .Count(p => p.X >= left && p.X < right && p.Y >= top && p.Y < bottom);
    }

    /// <summary>The same count under the scheme this design REJECTED: flatten row-major, take every
    /// <see cref="PointerInkSweep.Phases"/>th point. Written here and nowhere in the product.</summary>
    private static int CentralPointsRowMajor(PointerInkGrid grid, int phase)
    {
        var count = 0;
        for (var row = grid.Rows / 3; row < 2 * grid.Rows / 3; row++)
        {
            for (var column = grid.Columns / 3; column < 2 * grid.Columns / 3; column++)
            {
                if (((row * grid.Columns) + column) % PointerInkSweep.Phases == phase)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
