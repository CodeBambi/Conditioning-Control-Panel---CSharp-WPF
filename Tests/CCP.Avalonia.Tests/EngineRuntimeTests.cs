using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave A engine lane: the Intensity Ramp runtime (studio#1), the Scheduler runtime
/// (studio#2) and the tutorial event bus (progression#6), against WPF StartStop.cs numbers.</summary>
public sealed class EngineRuntimeTests
{
    private static AppSettings RampSettings()
    {
        var s = new AppSettings
        {
            FlashOpacity = 40, SpiralOpacity = 40, PinkFilterOpacity = 40, MasterVolume = 80,
            RampDurationMinutes = 10, RampMode = RampMode.Range, RampCurve = RampCurve.Linear,
            RampStartPercent = 50, RampEndPercent = 100,
            RampLinkFlashOpacity = true, RampLinkPinkFilterOpacity = true, RampLinkMasterAudio = true,
            RampLinkSpiralOpacity = false, EndSessionOnRampComplete = true,
        };
        return s;
    }

    [Fact]
    public void Ramp_ClimbsLinkedValues_CapsPink_AndRestoresOnStop()
    {
        var s = RampSettings();
        var ramp = new IntensityRampRun();
        var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
        ramp.Start(s, t0);

        Assert.False(ramp.Tick(s, t0.AddMinutes(5), sessionActive: false));   // half way: 75 %
        Assert.Equal(30, s.FlashOpacity);
        Assert.Equal(30, s.PinkFilterOpacity);
        Assert.Equal(60, s.MasterVolume);
        Assert.Equal(40, s.SpiralOpacity);   // not linked

        Assert.True(ramp.Tick(s, t0.AddMinutes(11), sessionActive: false));  // complete -> stop the engine
        ramp.Stop(s);
        Assert.Equal(40, s.FlashOpacity);
        Assert.Equal(40, s.PinkFilterOpacity);
        Assert.Equal(80, s.MasterVolume);
        Assert.False(ramp.IsActive);
    }

    [Fact]
    public void Ramp_UnderASession_LeavesVisualsAlone_AndNeverEndsTheEngine()
    {
        var s = RampSettings();
        var ramp = new IntensityRampRun();
        var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
        ramp.Start(s, t0);
        Assert.False(ramp.Tick(s, t0.AddMinutes(20), sessionActive: true));   // #444
        Assert.Equal(40, s.FlashOpacity);
        Assert.Equal(80, s.MasterVolume);   // audio still rides at 100 % of base
    }

    private static AppSettings SchedulerSettings(string start, string end)
    {
        var s = new AppSettings { SchedulerEnabled = true, SchedulerStartTime = start, SchedulerEndTime = end };
        s.SchedulerMonday = s.SchedulerTuesday = s.SchedulerWednesday = s.SchedulerThursday =
            s.SchedulerFriday = s.SchedulerSaturday = s.SchedulerSunday = true;
        return s;
    }

    [Fact]
    public void Scheduler_OvernightWindow_AndDisabledDay()
    {
        var s = SchedulerSettings("22:00", "02:00");
        Assert.True(SchedulerRun.IsInWindow(s, new DateTime(2026, 10, 9, 23, 0, 0)));
        Assert.True(SchedulerRun.IsInWindow(s, new DateTime(2026, 10, 9, 1, 59, 0)));
        Assert.False(SchedulerRun.IsInWindow(s, new DateTime(2026, 10, 9, 2, 0, 0)));
        s.SchedulerFriday = false;   // 2026-10-09 is a Friday
        Assert.False(SchedulerRun.IsInWindow(s, new DateTime(2026, 10, 9, 23, 0, 0)));
    }

    [Fact]
    public void Scheduler_StartsInWindow_StopsAfter_AndRemembersAHandStop()
    {
        var s = SchedulerSettings("16:00", "22:00");
        var run = new SchedulerRun();
        var inside = new DateTime(2026, 10, 9, 17, 0, 0);
        var outside = new DateTime(2026, 10, 9, 22, 30, 0);

        Assert.Equal(SchedulerRun.Action.Start, run.Tick(s, inside, engineRunning: false));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, inside, engineRunning: true));
        Assert.Equal(SchedulerRun.Action.Stop, run.Tick(s, outside, engineRunning: true));

        // A hand stop inside the window is not undone by the next poll.
        var run2 = new SchedulerRun();
        run2.NoteManualStop(s, inside);
        Assert.Equal(SchedulerRun.Action.None, run2.Tick(s, inside, engineRunning: false));
        Assert.Equal(SchedulerRun.Action.None, run2.Tick(s, outside, engineRunning: false));   // window ends: memory clears
        Assert.Equal(SchedulerRun.Action.Start, run2.Tick(s, inside.AddDays(1), engineRunning: false));

        s.SchedulerEnabled = false;
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().Tick(s, inside, engineRunning: false));
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().CheckOnStartup(s, inside));
    }

    [Fact]
    public void TutorialBus_DeliversNamedEvents()
    {
        string? got = null;
        EventHandler<string> h = (_, n) => got = n;
        CoreTutorialEvents.Event += h;
        try { CoreTutorialEvents.Emit("FileSaved"); }
        finally { CoreTutorialEvents.Event -= h; }
        Assert.Equal("FileSaved", got);
    }
}
