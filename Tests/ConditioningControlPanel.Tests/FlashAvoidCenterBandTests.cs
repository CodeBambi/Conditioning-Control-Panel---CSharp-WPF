using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #770 — "avoid the center of the screen" for gamers. The spawn point is chosen by BAND REMAP,
/// not rejection sampling: the legal area is split into 4 disjoint bands (left/right/above/below
/// the centered exclusion square), one is picked weighted by area, then the point is uniform
/// inside it. These tests pin the invariant that matters (nothing ever lands on the crosshair),
/// the uniformity of the remap, and the documented fallback when no band can fit the image.
/// </summary>
public class FlashAvoidCenterBandTests
{
    /// <summary>Mirrors the exclusion square FlashService builds, for assertion purposes.</summary>
    private static (int L, int T, int R, int B) Box(int monW, int monH, int pct)
    {
        double side = System.Math.Clamp(pct, 5, 60) / 100.0 * System.Math.Min(monW, monH);
        int l = (int)System.Math.Round((monW - side) / 2.0);
        int t = (int)System.Math.Round((monH - side) / 2.0);
        return (l, t, l + (int)System.Math.Round(side), t + (int)System.Math.Round(side));
    }

    private static bool Intersects(int x, int y, int w, int h, (int L, int T, int R, int B) b)
        => x < b.R && x + w > b.L && y < b.B && y + h > b.T;

    [Theory]
    [InlineData(1920, 1080, 300, 200, 25)]  // the common case
    [InlineData(1920, 1080, 300, 200, 60)]  // clamp ceiling
    [InlineData(1920, 1080, 300, 200, 5)]   // clamp floor
    [InlineData(3440, 1440, 500, 400, 40)]  // ultrawide: square sized off the SHORTER edge
    [InlineData(1080, 1920, 260, 180, 30)]  // portrait monitor
    [InlineData(800, 600, 200, 150, 25)]    // tiny monitor, image still fits a band
    public void PickedPoint_NeverTouchesTheExclusionBox(int monW, int monH, int w, int h, int pct)
    {
        var box = Box(monW, monH, pct);
        var rng = new System.Random(1234);

        for (int i = 0; i < 5000; i++)
        {
            Assert.True(FlashService.TryPickAvoidCenterPoint(monW, monH, w, h, pct, rng, out int x, out int y),
                "a legal band must exist for this configuration");
            Assert.False(Intersects(x, y, w, h, box),
                $"spawn {x},{y} ({w}x{h}) punched into the exclusion box {box}");
        }
    }

    [Theory]
    [InlineData(1920, 1080, 300, 200, 25)]
    [InlineData(800, 600, 200, 150, 25)]
    public void PickedPoint_StaysInsideTheEdgePadding(int monW, int monH, int w, int h, int pct)
    {
        var (minX, minY, maxX, maxY) = FlashService.SpawnBounds(monW, monH, w, h);
        var rng = new System.Random(99);

        for (int i = 0; i < 2000; i++)
        {
            Assert.True(FlashService.TryPickAvoidCenterPoint(monW, monH, w, h, pct, rng, out int x, out int y));
            Assert.InRange(x, minX, maxX - 1);
            Assert.InRange(y, minY, maxY - 1);
        }
    }

    [Fact]
    public void EdgePadding_SemanticsUnchanged()
    {
        // The pre-#770 rule: 50px in from every edge, with the Max(min+1, ...) floor so a huge
        // image still yields a (degenerate) legal range instead of throwing.
        Assert.Equal((50, 50, 1570, 830), FlashService.SpawnBounds(1920, 1080, 300, 200));
        Assert.Equal((50, 50, 51, 51), FlashService.SpawnBounds(400, 300, 9000, 9000));
    }

    [Fact]
    public void AllFourBands_AreReachable()
    {
        // A true band remap must be able to land above/below the box, not just left/right.
        var box = Box(1920, 1080, 25);
        var rng = new System.Random(7);
        bool left = false, right = false, above = false, below = false;

        for (int i = 0; i < 20000; i++)
        {
            Assert.True(FlashService.TryPickAvoidCenterPoint(1920, 1080, 300, 200, 25, rng, out int x, out int y));
            if (x + 300 <= box.L) left = true;
            else if (x >= box.R) right = true;
            else if (y + 200 <= box.T) above = true;
            else if (y >= box.B) below = true;
        }

        Assert.True(left && right && above && below,
            $"bands reachable: left={left} right={right} above={above} below={below}");
    }

    [Fact]
    public void HugeImage_NoLegalBand_SignalsFallback()
    {
        // Large ImageScale on a small monitor: every band collapses, so the caller must fall back
        // to the unconstrained pick rather than never spawning a flash.
        Assert.False(FlashService.TryPickAvoidCenterPoint(800, 600, 400, 300, 60, new System.Random(1), out _, out _));
    }

    // ── the adaptive shrink: real flash geometry, not toy sizes ────────────
    //
    // A flash is sized at 40% of the monitor's width/height (CalculateGeometry) before the user's
    // ImageScale, so at the DEFAULT scale a monitor-aspect image on 1920x1080 is 768x432 — not the
    // 300x200 the original rows used. At that size the raw band pick collapses: 30% leaves zero
    // legal area (the feature silently became a no-op and flashes went straight back to the
    // crosshair) and the default 25% leaves 1.4% — two ~8px-wide slivers. The adaptive wrapper
    // shrinks the effective box until at least MinLegalAreaFraction of the spawn area is legal.

    private const int RealW = 768, RealH = 432;   // 1920x1080 monitor, ImageScale = 100

    [Theory]
    [InlineData(25)]   // the default — 1.4% legal before the shrink
    [InlineData(30)]   // ZERO legal area before the shrink
    [InlineData(60)]   // clamp ceiling, also zero
    public void RealFlashGeometry_AdaptiveShrink_StillPlacesOutsideTheEffectiveBox(int pct)
    {
        var rng = new System.Random(2024);
        int firstEffective = -1;

        for (int i = 0; i < 5000; i++)
        {
            Assert.True(
                FlashService.TryPickAvoidCenterPointAdaptive(1920, 1080, RealW, RealH, pct, rng,
                    out int x, out int y, out int effectivePct),
                "the adaptive pick must succeed for an ordinary flash on an ordinary monitor");

            if (firstEffective < 0) firstEffective = effectivePct;
            Assert.Equal(firstEffective, effectivePct);          // deterministic: geometry only
            Assert.True(effectivePct <= System.Math.Min(pct, 60),
                "the effective box may shrink, never grow past what the user asked for");
            Assert.True(effectivePct >= 5, "5% is the floor");

            // The point must clear the EFFECTIVE box — that is what the user actually gets.
            Assert.False(Intersects(x, y, RealW, RealH, Box(1920, 1080, effectivePct)),
                $"spawn {x},{y} punched into the effective {effectivePct}% box");
        }

        // …and the box it settled on must leave a genuinely usable region, not a sliver.
        Assert.True(FlashService.LegalAreaFraction(1920, 1080, RealW, RealH, firstEffective) >= 0.10,
            $"effective {firstEffective}% left only " +
            $"{FlashService.LegalAreaFraction(1920, 1080, RealW, RealH, firstEffective):P2} of the spawn area");
    }

    [Fact]
    public void RealFlashGeometry_WithoutTheShrink_IsTheBugThisFixes()
    {
        // Pin the numbers the fix exists for, so a change in the sizing rule re-opens this test and
        // not a silent regression: at 30% the raw pick fails outright, at the default 25% it
        // "succeeds" with 1.4% of the screen legal.
        Assert.False(FlashService.TryPickAvoidCenterPoint(1920, 1080, RealW, RealH, 30, new System.Random(1), out _, out _));
        Assert.Equal(0.0, FlashService.LegalAreaFraction(1920, 1080, RealW, RealH, 30), 6);

        var at25 = FlashService.LegalAreaFraction(1920, 1080, RealW, RealH, 25);
        Assert.True(at25 > 0 && at25 < 0.02, $"expected a ~1.4% sliver at 25%, got {at25:P2}");
    }

    [Fact]
    public void AdaptiveShrink_KeepsTheRequestedBox_WhenItIsAlreadyRoomy()
    {
        // A small image never triggers the shrink — the user's setting is used verbatim.
        Assert.True(FlashService.TryPickAvoidCenterPointAdaptive(1920, 1080, 300, 200, 25,
            new System.Random(5), out _, out _, out int effectivePct));
        Assert.Equal(25, effectivePct);
    }

    [Fact]
    public void AdaptiveShrink_FallsBack_OnlyWhenEvenTheFloorIsHopeless()
    {
        // Image wider/taller than the whole padded spawn area: no percentage can help, so the caller
        // must still get its unconstrained-placement signal.
        Assert.False(FlashService.TryPickAvoidCenterPointAdaptive(800, 600, 900, 700, 60,
            new System.Random(1), out _, out _, out _));
    }

    [Fact]
    public void LegalArea_IsMonotonic_InThePercentage()
    {
        // The shrink loop relies on this: a smaller exclusion square is a subset of a bigger one, so
        // the FIRST percentage that clears the bar is also the largest one that does.
        double previous = 0;
        for (int pct = 60; pct >= 5; pct -= 5)
        {
            var f = FlashService.LegalAreaFraction(1920, 1080, RealW, RealH, pct);
            Assert.True(f >= previous, $"legal area shrank going from a bigger box to {pct}%");
            previous = f;
        }
    }

    [Fact]
    public void ExclusionPercent_ClampedTo_5_60()
    {
        Assert.Equal(5, new AppSettings { FlashCenterExclusionPercent = 0 }.FlashCenterExclusionPercent);
        Assert.Equal(60, new AppSettings { FlashCenterExclusionPercent = 100 }.FlashCenterExclusionPercent);
        Assert.Equal(25, new AppSettings().FlashCenterExclusionPercent);
        Assert.False(new AppSettings().FlashAvoidCenter); // opt-in only
    }

    [Fact]
    public void BiggerBox_PushesFlashesFurtherOut()
    {
        // Sanity: raising the percentage must strictly shrink the reachable central region.
        var rng = new System.Random(42);
        int nearCentreSmall = 0, nearCentreBig = 0;
        var probe = Box(1920, 1080, 60);

        for (int i = 0; i < 5000; i++)
        {
            FlashService.TryPickAvoidCenterPoint(1920, 1080, 200, 150, 10, rng, out int x1, out int y1);
            if (Intersects(x1, y1, 200, 150, probe)) nearCentreSmall++;

            FlashService.TryPickAvoidCenterPoint(1920, 1080, 200, 150, 60, rng, out int x2, out int y2);
            if (Intersects(x2, y2, 200, 150, probe)) nearCentreBig++;
        }

        Assert.True(nearCentreSmall > 0, "a 10% box should still allow spawns inside the 60% probe area");
        Assert.Equal(0, nearCentreBig);
    }
}
