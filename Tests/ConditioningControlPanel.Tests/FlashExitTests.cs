using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Leave animations for a popped flash (owner, 2026-09-28: Mix by default).</summary>
public class FlashExitTests
{
    private static FlashExitState At(FlashExitStyle style, double progress, MotionLevel motion = MotionLevel.Full)
    {
        var s = FlashExit.Begin(style, motion, seed: 4);
        s.ElapsedSec = s.DurationSec * progress;
        return s;
    }

    [Fact]
    public void DefaultIsMix() => Assert.Equal(FlashExitStyle.Mix, new AppSettings().FlashExitStyle);

    [Fact]
    public void NoneIsThePlainCut() => Assert.Null(FlashExit.Pick(FlashExitStyle.None, null, new Random(1)));

    [Fact]
    public void AFixedPickIsKept() => Assert.Equal(FlashExitStyle.Melt, FlashExit.Pick(FlashExitStyle.Melt, FlashExitStyle.Melt, new Random(1)));

    [Fact]
    public void MixNeverRepeatsBackToBackAndUsesEveryStyle()
    {
        var rng = new Random(7);
        FlashExitStyle? last = null;
        var seen = new System.Collections.Generic.HashSet<FlashExitStyle>();
        for (int i = 0; i < 200; i++)
        {
            var next = FlashExit.Pick(FlashExitStyle.Mix, last, rng)!.Value;
            Assert.NotEqual(last, next);
            Assert.Contains(next, FlashExit.Pool);
            seen.Add(next);
            last = next;
        }
        Assert.Equal(FlashExit.Pool.Length, seen.Count);
    }

    [Theory]
    [InlineData(FlashExitStyle.Pop)]
    [InlineData(FlashExitStyle.TvOff)]
    [InlineData(FlashExitStyle.Spiral)]
    [InlineData(FlashExitStyle.Melt)]
    [InlineData(FlashExitStyle.Glitch)]
    public void EveryStyleStartsWholeAndEndsGone(FlashExitStyle style)
    {
        var start = FlashExit.Sample(At(style, 0));
        Assert.Equal(1.0, start.Alpha, 3);
        Assert.Equal(1.0, start.ScaleX, 2);

        var end = FlashExit.Sample(At(style, 1));
        Assert.True(end.Alpha <= 0.01, $"{style} still visible at the end ({end.Alpha})");
        Assert.True(At(style, 1).Done);
    }

    [Theory]
    [InlineData(FlashExitStyle.Pop)]
    [InlineData(FlashExitStyle.Glitch)]
    public void ExitsAreShort(FlashExitStyle style) => Assert.InRange(FlashExit.DurationOf(style), 0.15, 0.5);

    [Fact]
    public void PopSwellsBeforeItCollapses()
    {
        Assert.True(FlashExit.Sample(At(FlashExitStyle.Pop, 0.3)).ScaleX > 1.05);
        Assert.Equal(10, FlashExit.SparkCount(At(FlashExitStyle.Pop, 0.5)));
        Assert.Equal(0, FlashExit.SparkCount(At(FlashExitStyle.Melt, 0.5)));
    }

    [Fact]
    public void MotionOffIsAShortFadeWhateverThePick()
    {
        var s = FlashExit.Begin(FlashExitStyle.Spiral, MotionLevel.Off, 1);
        Assert.Equal(0.12, s.DurationSec, 3);
        s.ElapsedSec = s.DurationSec / 2;
        var mid = FlashExit.Sample(s);
        Assert.Equal(0, mid.RotationDeg);
        Assert.Equal(1.0, mid.ScaleX);
        Assert.Equal(0, FlashExit.SparkCount(s));
    }

    [Fact]
    public void ReducedMotionDropsTheSpinSwellAndFlicker()
    {
        Assert.Equal(0, FlashExit.Sample(At(FlashExitStyle.Spiral, 0.5, MotionLevel.Reduced)).RotationDeg);
        Assert.True(FlashExit.Sample(At(FlashExitStyle.Pop, 0.3, MotionLevel.Reduced)).ScaleX <= 1.0);
        // No flicker: alpha only ever falls through a reduced glitch.
        double prev = 1.1;
        for (int i = 0; i <= 20; i++)
        {
            var a = FlashExit.Sample(At(FlashExitStyle.Glitch, i / 20.0, MotionLevel.Reduced)).Alpha;
            Assert.True(a <= prev + 1e-9);
            prev = a;
        }
    }

    [Fact]
    public void StepAdvancesAndStopsAtTheEnd()
    {
        var s = FlashExit.Begin(FlashExitStyle.Pop, MotionLevel.Full, 1);
        Assert.True(FlashExit.Step(s, 0.1));
        Assert.True(FlashExit.Step(s, 1.0));
        Assert.True(s.Done);
        Assert.False(FlashExit.Step(s, 0.1));
    }

    [Fact]
    public void MeltHangsFromItsTop() => Assert.Equal(0.0, FlashExit.Sample(At(FlashExitStyle.Melt, 0.5)).PivotY);
}
