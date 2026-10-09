using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF AchievementService:239/276: Pink Filter / Spiral quest minutes, 1 s ticks only while the
/// overlay shows, intervals of 6 s or more dropped. Stepped clock, no real timer wait (P08).</summary>
public sealed class OverlayQuestMinutesTests
{
    [Fact]
    public Task CreditsShownTimeOnlyAndTicksOnlyWhileShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var credited = new List<double>();
        var now = TimeSpan.Zero;
        var counter = new OverlayQuestMinutes(credited.Add) { Clock = () => now };

        Assert.False(counter.TickRunning);
        counter.Follow(true);                    // opens the stamp, starts the 1 s tick
        Assert.True(counter.TickRunning);
        for (var i = 0; i < 60; i++) { now += TimeSpan.FromSeconds(1); counter.Sample(true); }
        Assert.Equal(1.0, credited.Sum(), 6);

        now += TimeSpan.FromSeconds(30);         // a stalled tick: WPF's < 0.1 min sanity check drops it
        counter.Sample(true);
        Assert.Equal(1.0, credited.Sum(), 6);
        now += TimeSpan.FromSeconds(6);          // exactly 0.1 min: WPF's strict < drops it too
        counter.Sample(true);
        Assert.Equal(1.0, credited.Sum(), 6);

        counter.Follow(false);                   // hidden: the tick stops, later time counts nothing
        Assert.False(counter.TickRunning);
        now += TimeSpan.FromSeconds(1);
        counter.Sample(false);
        Assert.Equal(1.0, credited.Sum(), 6);
        return Task.CompletedTask;
    });
}
