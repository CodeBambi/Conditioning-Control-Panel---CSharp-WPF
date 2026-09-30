using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF BubbleService ambient field: the daily XP bucket, FloatUp motion + pop burst, the
// allocation-free step, and the engine arming the field.
[Collection(SessionStatics.Name)]
public sealed class AmbientBubblesTests
{
    [Fact]
    public void Bucket_pays_up_to_300_a_day_then_the_remainder_then_nothing()
    {
        var s = new AppSettings { AmbientBubbleXpDayKey = "2000-01-01", AmbientBubbleXpPaidToday = 999 };
        Assert.Equal(0, AmbientBubbleXp.PaidToday(s));          // stale day reads 0, untouched
        Assert.Equal(999, s.AmbientBubbleXpPaidToday);
        Assert.Equal(5, AmbientBubbleXp.Take(s, 5));            // rollover on first pop
        Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
        s.AmbientBubbleXpPaidToday = 290;
        Assert.Equal(10, AmbientBubbleXp.Take(s, 100));         // lucky near the ceiling pays the remainder
        Assert.Equal(0, AmbientBubbleXp.Take(s, 5));
        Assert.Equal(0, AmbientBubbleXp.RemainingToday(s));
    }

    [Fact]
    public void Bubble_floats_up_off_screen_and_a_pop_bursts_out_in_16_steps()
    {
        var s = new AppSettings();
        var f = new AmbientBubbleField();
        var b = AmbientBubble.Spawn(new Random(1), 0, 0, 0, 1920, 1080, 1, s, null, true);
        Assert.Equal(1080, b.Y);                                // WPF: starts at the bottom
        Assert.InRange(b.Size, BubbleSizing.BaseMinDip, BubbleSizing.BaseMaxDip);
        f.Bubbles.Add(b);
        var y = b.Y;
        f.Step();
        Assert.InRange(y - b.Y, 1.0, 2.0);                      // 1..2 DIP a step
        var missed = 0;
        for (var i = 0; i < 2000 && f.Bubbles.Count > 0; i++) missed += f.Step();
        Assert.Equal(1, missed);                                // left through the top

        var p = AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, s, null, true);
        f.Bubbles.Add(p);
        Assert.Equal(5, f.Pop(p, s));
        Assert.Equal(0, f.Pop(p, s));                           // never twice
        var steps = 0;
        while (f.Bubbles.Count > 0) { Assert.Equal(0, f.Step()); steps++; }
        Assert.Equal(16, steps);                                // fade 0.066 a step
        Assert.True(p.Scale > 1.6);
    }

    [Fact]
    public void Step_and_hit_test_allocate_nothing_with_a_full_field()
    {
        var s = new AppSettings();
        var f = new AmbientBubbleField();
        var r = new Random(3);
        while (f.CanSpawn(s)) f.Bubbles.Add(AmbientBubble.Spawn(r, 0, 0, 0, 1920, 50000, 1, s, null, true));
        Assert.Equal(40, f.Bubbles.Count);
        f.Step();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { f.Step(); f.HitTest(0, 500, 500); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Engine_arms_bubbles_by_the_saved_flag_and_applies_them_live()
    {
        var s = CoreSettings.Current;
        int start = 0, stop = 0;
        CoreBubbles.StartAction = () => start++;
        CoreBubbles.StopAction = () => stop++;
        try
        {
            s.BubblesEnabled = true;
            CoreEngine.Start();
            Assert.Equal(1, start);
            CoreEngine.ApplyLive("bubbles", false);
            Assert.Equal(1, stop);
            CoreEngine.Stop();
            Assert.Equal(2, stop);
            CoreEngine.ApplyLive("bubbles", true);                // stopped: nothing
            Assert.Equal(1, start);
        }
        finally { CoreEngine.Stop(); CoreBubbles.StartAction = CoreBubbles.StopAction = null; s.BubblesEnabled = false; }
    }
}
