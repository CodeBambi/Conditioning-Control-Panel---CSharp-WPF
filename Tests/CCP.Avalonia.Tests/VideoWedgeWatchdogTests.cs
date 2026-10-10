using System;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Hunt IB8 (HB19): the mandatory video's UI-thread wedge watchdog, WPF's ladder on a stepped clock.
/// No dispatcher and no LibVLC: Tick is what the threadpool timer calls, Beat what the UI heartbeat calls.</summary>
public sealed class VideoWedgeWatchdogTests
{
    private sealed class Rig
    {
        public long Now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).Ticks;
        public bool Armed = true, Live = true, Cleaning, StopReturns = true;
        public int Stops, Teardowns, Hatches;
        public readonly ManualResetEventSlim Hatched = new(false);
        public readonly VideoWedgeWatchdog Dog;

        public Rig()
        {
            Dog = new VideoWedgeWatchdog
            {
                NowTicks = () => Now,
                Armed = () => Armed,
                Live = () => Live,
                Cleaning = () => Cleaning,
                StopPlayers = () => { Stops++; return StopReturns; },
                PostTeardown = () => Teardowns++,
                EscapeHatch = () => { Interlocked.Increment(ref Hatches); Hatched.Set(); },
            };
            Dog.Beat();
        }

        public void StallTo(int ms)
        {
            Now += (ms - (int)((Now - Start) / TimeSpan.TicksPerMillisecond)) * TimeSpan.TicksPerMillisecond;
            Dog.Tick();
        }

        public long Start = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).Ticks;
    }

    [Fact]
    public void Ladder_StopsThePlayerAtEightSeconds_ThenReleasesTheWindowsAtTwentyEight()
    {
        var r = new Rig();
        r.StallTo(7900);
        Assert.Equal((0, 0, 0, false), (r.Dog.Rung, r.Stops, r.Teardowns, r.Dog.StallSeen));
        r.StallTo(8000);
        Assert.Equal((1, 1, 1, true), (r.Dog.Rung, r.Stops, r.Teardowns, r.Dog.StallSeen));   // rung 1: stop + queued teardown
        r.StallTo(11000);
        Assert.Equal((1, 1, 1), (r.Dog.Rung, r.Stops, r.Teardowns));                          // once
        r.StallTo(18000);
        Assert.Equal((2, 1, 0), (r.Dog.Rung, r.Stops, r.Hatches));                            // rung 2 retires nothing here
        r.StallTo(27000);
        Assert.Equal(0, r.Hatches);
        r.StallTo(28000);
        Assert.True(r.Hatched.Wait(5000));                                                    // rung 3, on its own task
        Assert.Equal(3, r.Dog.Rung);
        r.StallTo(40000);
        Assert.Equal((1, 1, 1), (r.Stops, r.Teardowns, r.Hatches));                           // ladder exhausted
    }

    [Fact]
    public void AHeartbeat_KeepsItQuiet_AndNothingRunsWithoutAVideo()
    {
        var r = new Rig();
        for (int i = 1; i <= 40; i++)
        {
            r.Now += TimeSpan.TicksPerSecond;
            r.Dog.Beat();                                    // the UI thread drains: never a stall
            r.Dog.Tick();
        }
        Assert.Equal((0, 0, false), (r.Dog.Rung, r.Stops, r.Dog.StallSeen));

        var idle = new Rig { Armed = false };
        idle.StallTo(60000);                                 // a frozen UI with no video on screen is not this guard's
        Assert.Equal((0, 0, 0, false), (idle.Dog.Rung, idle.Stops, idle.Hatches, idle.Dog.StallSeen));
    }

    [Fact]
    public void PreRollAndTeardown_OnlyEverUseTheEscapeHatch()
    {
        var pre = new Rig { Live = false };
        pre.StallTo(9000);
        pre.StallTo(20000);
        Assert.Equal((0, 0, 0, true), (pre.Dog.Rung, pre.Stops, pre.Teardowns, pre.Dog.StallSeen));
        pre.StallTo(28000);
        Assert.True(pre.Hatched.Wait(5000));
        Assert.Equal((3, 0, 0), (pre.Dog.Rung, pre.Stops, pre.Teardowns));

        var down = new Rig { Armed = false, Cleaning = true };   // _videoPlaying is cleared at the top of the teardown
        down.StallTo(9000);
        Assert.Equal(0, down.Stops);
        down.StallTo(29000);
        Assert.True(down.Hatched.Wait(5000));
        Assert.Equal(0, down.Stops);
    }

    [Fact]
    public void AStopThatNeverReturns_CostsOnlyItsOwnTask_AndDisposeSilencesTheDog()
    {
        using var never = new ManualResetEventSlim(false);
        Assert.False(VideoWedgeWatchdog.StopOffThread(() => never.Wait(10000), budgetMs: 100));
        never.Set();
        Assert.True(VideoWedgeWatchdog.StopOffThread(() => { }, budgetMs: 2000));
        Assert.True(VideoWedgeWatchdog.StopOffThread(() => throw new InvalidOperationException("native"), budgetMs: 2000));

        var r = new Rig { StopReturns = false };
        r.StallTo(9000);
        Assert.Equal((1, 1, 1), (r.Dog.Rung, r.Stops, r.Teardowns));   // the teardown is still queued
        r.Dog.Dispose();
        r.StallTo(30000);
        Assert.Equal((1, 0), (r.Dog.Rung, r.Hatches));

        Assert.Equal(OperatingSystem.IsWindows(), VideoWedgeWatchdog.ReleaseTopmost(Array.Empty<IntPtr>()));
        Assert.Equal(8000, VideoWedgeWatchdog.StallMs);
        Assert.Equal(28000, VideoWedgeWatchdog.Rung3Ms);
    }
}
