using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The ambient bubble field had no size control at all — <c>random.Next(150, 250)</c> DIP, hardcoded
/// — so "is there a way to make the floating bubbles smaller? this is kinda wild" had no answer.
/// <see cref="BubbleSizing"/> adds the user setting and honours a mod's <c>bubbleScale</c> (full-bleed
/// sprite art reads far bigger than the padded stock bubble at an identical box size).
///
/// <para><see cref="DefaultSettings_ReproduceTheShippedBandExactly"/> is the one that matters most:
/// this touches a spawn path in a service with a history of render-thread hangs, so "nothing moves
/// unless the user moved it" has to be provable.</para>
///
/// <para><see cref="ShrinkingMod_LeavesNoDeadZoneAcrossTheSlider"/> is the one that exists because we
/// got it wrong once: clamping the product of the two factors made the slider inert on any mod with
/// <c>bubbleScale</c> at or below 0.5.</para>
/// </summary>
public class BubbleSizingTests
{
    [Fact]
    public void DefaultSettings_ReproduceTheShippedBandExactly()
    {
        for (int size = BubbleSizing.BaseMinDip; size < BubbleSizing.BaseMaxDip; size++)
            Assert.Equal(size, BubbleSizing.Scale(size, BubbleSizing.UserPercentDefault, null));
    }

    [Fact]
    public void DefaultSettings_WithAModScaleOfOne_AlsoChangeNothing()
    {
        Assert.Equal(200, BubbleSizing.Scale(200, BubbleSizing.UserPercentDefault, 1.0));
    }

    [Theory]
    [InlineData(50, 200, 100)]
    [InlineData(75, 200, 150)]
    [InlineData(100, 200, 200)]
    [InlineData(150, 200, 300)]
    public void UserPercent_ScalesLinearly(int percent, int baseSize, int expected)
        => Assert.Equal(expected, BubbleSizing.Scale(baseSize, percent, null));

    [Fact]
    public void UserPercent_IsClampedSoAHandEditedSettingsFileCannotBreakTheField()
    {
        // Settings are JSON on disk; someone will eventually type 5000.
        Assert.Equal(BubbleSizing.Scale(200, BubbleSizing.UserPercentMax, null),
                     BubbleSizing.Scale(200, 10_000, null));
        Assert.Equal(BubbleSizing.Scale(200, BubbleSizing.UserPercentMin, null),
                     BubbleSizing.Scale(200, -40, null));
    }

    [Fact]
    public void ModScale_ShrinksFullBleedArtWithoutTheUserTouchingAnything()
    {
        // The whole point of bubbleScale: an author whose bubble.png fills its canvas corrects it
        // once instead of every user discovering the slider.
        Assert.Equal(100, BubbleSizing.Scale(200, BubbleSizing.UserPercentDefault, 0.5));
    }

    [Fact]
    public void ModScale_ComposesWithTheUserSetting()
    {
        // 120% of a mod that already halves itself. The factors multiply, full stop - nothing
        // renormalises the product back into the user's own range.
        Assert.Equal((int)Math.Round(200 * 1.2 * 0.5), BubbleSizing.Scale(200, 120, 0.5));
    }

    [Fact]
    public void ShrinkingMod_LeavesNoDeadZoneAcrossTheSlider()
    {
        // THE regression test for this file. Under the old product clamp, a mod at bubbleScale 0.5
        // made every user setting from 100% down to 50% multiply out at or below 0.5, clamp to
        // exactly 0.5, and render identically: the slider saved a value and changed nothing. Now
        // every single step down the slider must produce a strictly smaller bubble, right up until
        // the absolute clickable floor - a physical rail, not an arithmetic one - takes over.
        const double mod = 0.5;
        var previous = int.MaxValue;
        var atFloor = false;

        for (int percent = BubbleSizing.UserPercentMax; percent >= BubbleSizing.UserPercentMin; percent--)
        {
            var size = BubbleSizing.Scale(BubbleSizing.BaseMaxDip, percent, mod);

            if (atFloor || size == BubbleSizing.ClickableFloorDip)
            {
                // Once the floor is reached the value is allowed to flatten, and must stay pinned
                // there rather than dipping under it.
                Assert.Equal(BubbleSizing.ClickableFloorDip, size);
                atFloor = true;
            }
            else
            {
                Assert.True(size < previous,
                            $"dead zone at {percent}%: {size} DIP is not smaller than {previous} DIP");
            }

            previous = size;
        }

        // And on the shipped band's top draw the floor is never reached at all, so the slider is
        // live from end to end even on a halving mod - the user keeps the final say.
        Assert.True(BubbleSizing.Scale(BubbleSizing.BaseMaxDip, BubbleSizing.UserPercentMin, mod)
                        > BubbleSizing.ClickableFloorDip);
    }

    [Fact]
    public void ShrinkingMod_OnASmallDrawStillMovesBeforeItRestsOnTheFloor()
    {
        // Bottom of the band plus a halving mod is the worst case: 50% lands under the floor. The
        // acceptable outcome is that the slider still does something over most of its travel and
        // only flattens on the last stretch, where the bubble would be unhittable anyway.
        var top = BubbleSizing.Scale(BubbleSizing.BaseMinDip, BubbleSizing.UserPercentMax, 0.5);
        var bottom = BubbleSizing.Scale(BubbleSizing.BaseMinDip, BubbleSizing.UserPercentMin, 0.5);

        Assert.Equal(BubbleSizing.ClickableFloorDip, bottom);
        Assert.True(top > bottom, "even the smallest draw must respond to the slider");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ModScale_GarbageIsTreatedAsUnset(double scale)
    {
        // mod.json is hand-authored; an authoring mistake must not draw an invisible or absurd bubble.
        Assert.Equal(BubbleSizing.Scale(200, 100, null), BubbleSizing.Scale(200, 100, scale));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(50.0)]
    public void ModScale_OutOfRangeIsClampedNotObeyed(double scale)
    {
        // Out of range is pulled to the nearest end of the mod range, so the result is exactly what
        // an honest manifest at that end would have produced - never the literal number.
        var expected = BubbleSizing.Scale(200, 100, scale <= 0 ? BubbleSizing.ModScaleMin
                                                              : BubbleSizing.ModScaleMax);
        Assert.Equal(expected, BubbleSizing.Scale(200, 100, scale));
    }

    [Fact]
    public void TheAbsoluteRailsAreTheOnlyLimitsAndTheyHoldEverywhere()
    {
        // A bubble is a MOVING click target at one end and has to leave a play field at the other.
        // Whatever the settings and whatever the mod says, the result stays between the two rails.
        foreach (var baseSize in new[] { 1, 10, 60, 150, 250, 10_000 })
            foreach (var percent in new[] { -100, 0, 50, 100, 150, 9999 })
                foreach (var mod in new double?[] { null, 0.0, 0.5, 1.0, 1.5, 99.0 })
                {
                    var size = BubbleSizing.Scale(baseSize, percent, mod);
                    Assert.InRange(size, BubbleSizing.ClickableFloorDip, BubbleSizing.PlayfieldCeilingDip);
                }
    }

    [Fact]
    public void TheCeilingOnlyBitesOnTheCompoundExtreme()
    {
        // 150% of a 1.5 mod at the top of the band is ~560 DIP, taller than half a 1366x768 laptop
        // screen, so it gets trimmed...
        Assert.Equal(BubbleSizing.PlayfieldCeilingDip,
                     BubbleSizing.Scale(BubbleSizing.BaseMaxDip, BubbleSizing.UserPercentMax, BubbleSizing.ModScaleMax));

        // ...but the plain no-mod slider never reaches it, so nobody's existing setting is clipped
        // by the ceiling's introduction.
        Assert.Equal(375, BubbleSizing.Scale(BubbleSizing.BaseMaxDip, BubbleSizing.UserPercentMax, null));
        Assert.True(BubbleSizing.PlayfieldCeilingDip > 375);
    }

    [Fact]
    public void TheFloorMatchesTheOneBubbleServiceAlreadyImposes()
    {
        // Deliberately the same 60 as the spec-driven branch rather than a second, competing
        // minimum. If one moves, this pins the other to move with it.
        Assert.Equal(60, BubbleSizing.ClickableFloorDip);
    }

    [Fact]
    public void TheBandConstantsStillDescribeTheShippedBand()
    {
        // These feed Random.Next(min, max) — max EXCLUSIVE — so they must stay the literal pair
        // that shipped or the field silently changes for everyone.
        Assert.Equal(150, BubbleSizing.BaseMinDip);
        Assert.Equal(250, BubbleSizing.BaseMaxDip);
        Assert.Equal(100, BubbleSizing.UserPercentDefault);
    }

    [Fact]
    public void TheUserRangeBracketsTheDefault()
    {
        Assert.True(BubbleSizing.UserPercentMin < BubbleSizing.UserPercentDefault);
        Assert.True(BubbleSizing.UserPercentMax > BubbleSizing.UserPercentDefault);
    }
}
