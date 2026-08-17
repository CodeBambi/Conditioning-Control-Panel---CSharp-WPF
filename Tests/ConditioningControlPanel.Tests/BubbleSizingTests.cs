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
        // 120% of a mod that already halves itself.
        Assert.Equal((int)Math.Round(200 * 1.2 * 0.5), BubbleSizing.Scale(200, 120, 0.5));
    }

    [Fact]
    public void CombinedScale_CannotLeaveTheRangeTheUserCouldHaveChosenAlone()
    {
        // A mod must not be able to push the field past the user's own ceiling or floor.
        var maxAlone = BubbleSizing.Scale(200, BubbleSizing.UserPercentMax, null);
        Assert.Equal(maxAlone, BubbleSizing.Scale(200, BubbleSizing.UserPercentMax, 1.5));

        var minAlone = BubbleSizing.Scale(200, BubbleSizing.UserPercentMin, null);
        Assert.Equal(minAlone, BubbleSizing.Scale(200, BubbleSizing.UserPercentMin, 0.5));
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
        var result = BubbleSizing.Scale(200, 100, scale);
        Assert.True(result >= BubbleSizing.ClickableFloorDip);
        Assert.True(result <= (int)Math.Round(200 * BubbleSizing.CombinedScaleMax));
    }

    [Fact]
    public void TheClickableFloorAlwaysHolds()
    {
        // A bubble is a MOVING click target. Whatever the settings and whatever the mod says, it
        // must not shrink below the floor the rest of the service already enforces.
        foreach (var baseSize in new[] { 1, 10, 60, 150, 250 })
            foreach (var percent in new[] { -100, 0, 50, 100, 150, 9999 })
                foreach (var mod in new double?[] { null, 0.0, 0.5, 1.0, 1.5, 99.0 })
                    Assert.True(BubbleSizing.Scale(baseSize, percent, mod) >= BubbleSizing.ClickableFloorDip,
                                $"base={baseSize} percent={percent} mod={mod} fell through the floor");
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
