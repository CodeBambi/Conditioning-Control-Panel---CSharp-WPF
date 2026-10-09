using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>studio#31 / studio#3: the session drives spiral, Brain Drain, bubbles (bursts), videos,
/// bubble count and its escalating Mind Wipe, and hands every setting back at Stop.</summary>
[Collection(SessionStatics.Name)]
public sealed class SessionRunnerPhasesTests : IDisposable
{
    private readonly SessionRunner _runner = new(new SessionLogService());
    private int _bdStarts, _bdStops, _bubbleStarts, _bubbleStops, _mwSessionBase = -1;
    private double _bdIntensity = -1, _mwSessionVolume;

    public SessionRunnerPhasesTests()
    {
        PhrasePoolCustody.Seed();
        CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
        CoreBrainDrain.StartProvider = () => _bdStarts++;
        CoreBrainDrain.StopProvider = () => _bdStops++;
        CoreBrainDrain.IntensityProvider = v => _bdIntensity = v;
        CoreBubbles.StartAction = () => _bubbleStarts++;
        CoreBubbles.StopAction = () => _bubbleStops++;
        CoreMindWipe.StartSessionProvider = (b, v) => { _mwSessionBase = b; _mwSessionVolume = v; };
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreBrainDrain.StartProvider = CoreBrainDrain.StopProvider = null;
        CoreBrainDrain.IntensityProvider = null;
        CoreBubbles.StartAction = CoreBubbles.StopAction = null;
        CoreMindWipe.StartSessionProvider = null;
    }

    private static Session Make(Action<SessionSettings> set)
    {
        var ss = new SessionSettings();
        set(ss);
        return new Session { Id = "phases-test", Name = "Phases", DurationMinutes = 30, Settings = ss };
    }

    [Fact]
    public void Spiral_StartsNearItsMinute_RampsWithoutWritingTheSetting_AndIsHandedBack()
    {
        var s = CoreSettings.Current;
        s.SpiralEnabled = false;
        s.SpiralOpacity = 33;
        _runner.Start(Make(ss => { ss.SpiralEnabled = true; ss.SpiralStartMinute = 10; ss.SpiralOpacity = 5; ss.SpiralOpacityEnd = 25; }));
        Assert.False(s.SpiralEnabled);   // delayed

        _runner.Tick(TimeSpan.FromMinutes(6.9));
        Assert.False(s.SpiralEnabled);   // never earlier than minute 7 (10 - 3)
        _runner.Tick(TimeSpan.FromMinutes(13.1));
        Assert.True(s.SpiralEnabled);
        Assert.NotNull(_runner.SpiralOpacity);
        Assert.InRange(_runner.SpiralOpacity!.Value, 5, 25);
        Assert.Equal(33, s.SpiralOpacity);   // ramp is the runner's, not the user's slider

        _runner.Stop();
        Assert.False(s.SpiralEnabled);
        Assert.Equal(33, s.SpiralOpacity);
        Assert.Null(_runner.SpiralOpacity);
    }

    [Fact]
    public void BrainDrain_StartsAndEndsAtItsMinutes_AndRampsTheLiveIntensity()
    {
        var s = CoreSettings.Current;
        s.BrainDrainEnabled = false;
        s.BrainDrainIntensity = 50;
        _runner.Start(Make(ss =>
        {
            ss.BrainDrainEnabled = true; ss.BrainDrainStartMinute = 5; ss.BrainDrainEndMinute = 20;
            ss.BrainDrainStartIntensity = 10; ss.BrainDrainEndIntensity = 60;
        }));
        Assert.False(s.BrainDrainEnabled);
        _bdStarts = 0;

        _runner.Tick(TimeSpan.FromMinutes(5));
        Assert.Equal(1, _bdStarts);
        Assert.True(s.BrainDrainEnabled);
        Assert.Equal(10, s.BrainDrainIntensity);

        _runner.Tick(TimeSpan.FromMinutes(17.5));   // half way from 5 to 30
        Assert.Equal(35, _bdIntensity, 3);

        var stops = _bdStops;
        _runner.Tick(TimeSpan.FromMinutes(20));
        Assert.Equal(stops + 1, _bdStops);
        Assert.False(s.BrainDrainEnabled);

        _runner.Stop();
        Assert.Equal(50, s.BrainDrainIntensity);
    }

    [Fact]
    public void MindWipe_StartsInSessionMode_WithTheBaseAndVolume()
    {
        _runner.Start(Make(ss => { ss.MindWipeEnabled = true; ss.MindWipeBaseMultiplier = 2; ss.MindWipeVolume = 45; }));
        Assert.Equal(2, _mwSessionBase);
        Assert.Equal(0.45, _mwSessionVolume, 3);
    }

    [Fact]
    public void MindWipe_DelayedStart_FiresAtItsMinute()
    {
        _runner.Start(Make(ss => { ss.MindWipeEnabled = true; ss.MindWipeStartMinute = 4; ss.MindWipeBaseMultiplier = 3; }));
        Assert.Equal(-1, _mwSessionBase);
        _runner.Tick(TimeSpan.FromMinutes(4));
        Assert.Equal(3, _mwSessionBase);
    }

    [Fact]
    public void IntermittentBubbles_RunInBursts_AtTwiceThePerBurstCount()
    {
        var s = CoreSettings.Current;
        s.BubblesEnabled = false;
        s.BubblesFrequency = 7;
        _runner.Start(Make(ss =>
        {
            ss.BubblesEnabled = true; ss.BubblesIntermittent = true; ss.BubblesBurstCount = 3;
            ss.BubblesPerBurst = 2; ss.BubblesGapMin = 6; ss.BubblesGapMax = 6;
        }));
        Assert.False(s.BubblesEnabled);
        _bubbleStarts = 0;

        _runner.Tick(TimeSpan.FromMinutes(1.9));
        Assert.Equal(0, _bubbleStarts);   // first burst is 2-4 min in
        _runner.Tick(TimeSpan.FromMinutes(4.1));
        Assert.Equal(1, _bubbleStarts);
        Assert.True(s.BubblesEnabled);
        Assert.Equal(4, s.BubblesFrequency);

        _runner.Tick(TimeSpan.FromMinutes(6.2));   // a burst lasts 1-2 min
        Assert.False(s.BubblesEnabled);

        _runner.Stop();
        Assert.Equal(7, s.BubblesFrequency);
    }

    [Fact]
    public void DelayedBubbles_RampOnePerFiveMinutes()
    {
        var s = CoreSettings.Current;
        s.BubblesEnabled = false;
        _runner.Start(Make(ss => { ss.BubblesEnabled = true; ss.BubblesStartMinute = 5; ss.BubblesFrequency = 1; }));
        Assert.False(s.BubblesEnabled);
        _runner.Tick(TimeSpan.FromMinutes(5));
        Assert.True(s.BubblesEnabled);
        Assert.Equal(1, s.BubblesFrequency);
        _runner.Tick(TimeSpan.FromMinutes(15.5));
        Assert.Equal(3, s.BubblesFrequency);
    }

    [Fact]
    public void Pause_StopsThePhaseServices_ResumeBringsBrainDrainBack()
    {
        _runner.Start(Make(ss => { ss.BrainDrainEnabled = true; ss.BrainDrainStartIntensity = 20; }));
        Assert.True(CoreSettings.Current.BrainDrainEnabled);
        var starts = _bdStarts; var stops = _bdStops;
        _runner.Pause();
        Assert.Equal(stops + 1, _bdStops);
        _runner.Resume();
        Assert.Equal(starts + 1, _bdStarts);
    }

    [Fact]
    public void BrainDrainSchedule_MatchesWpfNumbers()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), BrainDrainSchedule.TickInterval(false));
        Assert.Equal(TimeSpan.FromMilliseconds(500), BrainDrainSchedule.TickInterval(true));
        Assert.Equal(50 / 100.0 / 12.0, BrainDrainSchedule.Probability(50, TimeSpan.FromSeconds(5)), 6);
        Assert.Equal(0.25f, BrainDrainSchedule.EffectiveVolume(50, 50), 3);
        Assert.Equal(1, BrainDrainSchedule.ClampIntensity(0));

        var root = Path.Combine(Path.GetTempPath(), "bd-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "a"); var b = Path.Combine(root, "b");
        Directory.CreateDirectory(b);
        try
        {
            Directory.CreateDirectory(a);
            File.WriteAllText(Path.Combine(a, "one.mp3"), "");
            File.WriteAllText(Path.Combine(b, "one.mp3"), "");
            File.WriteAllText(Path.Combine(b, "two.ogg"), "");
            File.WriteAllText(Path.Combine(b, "note.txt"), "");
            var clips = BrainDrainSchedule.DiscoverClips(a, b);
            Assert.Equal(2, clips.Length);
            Assert.Contains(Path.Combine(a, "one.mp3"), clips);   // the assets copy wins
        }
        finally { Directory.Delete(root, true); }
    }
}
