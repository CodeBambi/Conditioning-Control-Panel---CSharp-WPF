using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Juice round (2026-10-01) for the gold v2 box and its switch. Everything here is
/// cosmetic: none of it changes what a switch does or who may flip it. The pins
/// are the sizes (subtle), the motion branches (Reduced = half, Off = still) and photosafe.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SuperChromeJuiceTests
{
    // ---- The gold v2 box and its switch ----

    [Theory]
    [InlineData(MotionLevel.Full, 1.0)]
    [InlineData(MotionLevel.Reduced, 0.5)]
    [InlineData(MotionLevel.Off, 0.0)]
    public void Switch_thud_and_lock_pop_are_small_and_size_with_motion(MotionLevel level, double share)
    {
        double a = SuperChromeJuice.Amount(level);
        Assert.Equal(share, a);
        var thud = SuperChromeJuice.ThudKeys(a);
        var pop = SuperChromeJuice.LockPopKeys(a);
        if (a == 0) { Assert.Empty(thud); Assert.Empty(pop); return; }
        Assert.Equal((0.0, 1.0, 1.0), thud.First());
        Assert.Equal((1.0, 1.0, 1.0), thud.Last());
        Assert.Equal((0.0, 1.0), pop.First());
        Assert.Equal((1.0, 1.0), pop.Last());
        double thudDev = thud.Max(k => Math.Max(Math.Abs(k.X - 1), Math.Abs(k.Y - 1)));
        Assert.InRange(thudDev, 0.079 * a, 0.08 * a + 1e-9); // the overshoot rule: 3-8%, never rubber
        double popDev = pop.Max(k => Math.Abs(k.S - 1));
        Assert.InRange(popDev, 0.17 * a, SuperChromeJuice.LockPopPeak * a + 1e-9);
        Assert.True(SuperChromeJuice.ThudDelayMs + SuperChromeJuice.ThudMs <= 450);
    }

    [Fact]
    public void Sheen_is_rare_ambient_and_never_runs_when_motion_is_off()
    {
        Assert.False(SuperChromeJuice.SheenAllowed(true, MotionLevel.Off));
        Assert.False(SuperChromeJuice.SheenAllowed(false, MotionLevel.Full));
        Assert.True(SuperChromeJuice.SheenAllowed(true, MotionLevel.Full));
        Assert.Equal(8, SuperChromeJuice.SheenDelay(0));
        Assert.Equal(13, SuperChromeJuice.SheenDelay(1));
        Assert.Equal(-0.4, SuperChromeJuice.SheenOffset(0), 9);
        Assert.Equal(1.4, SuperChromeJuice.SheenOffset(1), 9);
        Assert.True(SuperChromeJuice.SheenPeak <= 0.35, "a sheen, not a flash");
        Assert.Equal(220, SuperChromeJuice.ChangeMs(MotionLevel.Full));
        Assert.Equal(220, SuperChromeJuice.ChangeMs(MotionLevel.Reduced));
        Assert.Equal(120, SuperChromeJuice.ChangeMs(MotionLevel.Off));
    }

    [Fact]
    public void Tween_writes_the_base_at_once_so_a_repaint_never_fights_it()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var b = new SolidColorBrush(Colors.Black);
            SuperChrome.Tween(b, SolidColorBrush.ColorProperty, Colors.Gold, 220);
            Assert.Equal(Colors.Gold, (Color)b.ReadLocalValue(SolidColorBrush.ColorProperty));
            Assert.True(b.HasAnimatedProperties);
            // A silent repaint to the same target leaves the running tween alone.
            SuperChrome.Tween(b, SolidColorBrush.ColorProperty, Colors.Gold, 0);
            Assert.True(b.HasAnimatedProperties);
            // A silent repaint somewhere else lands at once.
            SuperChrome.Tween(b, SolidColorBrush.ColorProperty, Colors.Red, 0);
            Assert.False(b.HasAnimatedProperties);
            Assert.Equal(Colors.Red, b.Color);

            var e = new UIElement();
            SuperChrome.TweenOpacity(e, 0.5, 0);
            Assert.Equal(0.5, e.Opacity);
        });
    }

    [Fact]
    public void Box_paint_offscreen_lands_at_once_and_the_sheen_is_safe_before_layout()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var box = new SuperBox { Effect = SuperEffect.LightsDown };
            box.Paint(lit: true);
            Assert.Equal(0xB3, ((SolidColorBrush)box.BorderBrush).Color.A);
            Assert.False(((SolidColorBrush)box.BorderBrush).HasAnimatedProperties);
            box.PlaySheen(); // no layout yet: nothing to sweep, nothing thrown
        });
    }
}
