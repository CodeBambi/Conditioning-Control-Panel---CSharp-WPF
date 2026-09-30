using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Takeover's schedule on a fake clock (Core AutonomyScheduler, WPF AutonomyService rules).</summary>
[Collection(SessionStatics.Name)]
public class AutonomySchedulerTests : IDisposable
{
    private DateTime _now = new(2026, 1, 1, 14, 0, 0);   // afternoon: Attentive, no mood skew
    private readonly List<AutonomyActionType> _performed = new();

    public AutonomySchedulerTests()
    {
        Reset();
        var s = CoreSettings.Current;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = s.AutonomyRandomTriggerEnabled = s.AutonomyCanTriggerFlash = true;
        s.AutonomyRandomIntervalSeconds = 60;
        s.AutonomyCooldownSeconds = 30;
        s.AutonomyAnnouncementChance = 0;
        CoreEntitlement.HasPremiumProvider = () => true;
    }

    public void Dispose()
    {
        CoreEntitlement.HasPremiumProvider = null;
        Reset();
    }

    private static void Reset()
    {
        var s = CoreSettings.Current;
        var d = new AppSettings();
        s.AutonomyModeEnabled = d.AutonomyModeEnabled;
        s.AutonomyConsentGiven = d.AutonomyConsentGiven;
        s.AutonomyRandomTriggerEnabled = d.AutonomyRandomTriggerEnabled;
        s.AutonomyIdleTriggerEnabled = false;
        s.AutonomyTimeAwareEnabled = false;
        s.AutonomyRandomIntervalSeconds = d.AutonomyRandomIntervalSeconds;
        s.AutonomyCooldownSeconds = d.AutonomyCooldownSeconds;
        s.AutonomyAnnouncementChance = d.AutonomyAnnouncementChance;
        s.AutonomyCanTriggerFlash = d.AutonomyCanTriggerFlash;
        s.AutonomyCanTriggerMindWipe = false;
        s.AutonomyCanTriggerVideo = s.AutonomyCanTriggerSubliminal = s.AutonomyCanTriggerBubbles = false;
        s.AutonomyCanComment = s.AutonomyCanTriggerLockCard = s.AutonomyCanTriggerPinkFilter = false;
        s.AutonomyCanTriggerBouncingText = s.AutonomyCanTriggerWebVideo = s.AutonomyCanTriggerWallpaper = false;
        s.AutonomyCanTriggerVoiceCommand = false;
    }

    private AutonomyScheduler Make() => new()
    {
        Clock = () => _now, Rng = new Random(1),
        CanPerform = a => a == AutonomyActionType.Flash,
        Perform = (a, _) => _performed.Add(a),
    };

    private void Advance(AutonomyScheduler s, int seconds)
    {
        for (var i = 0; i < seconds; i++) { _now = _now.AddSeconds(1); s.Tick(); }
    }

    [Fact]
    public void RandomTriggerFiresWithinTheJitteredInterval()
    {
        var s = Make();
        Assert.True(s.Start(withTimer: false));
        Advance(s, 39);
        Assert.Empty(_performed);   // 60 s ±33 % never fires before 40 s
        Advance(s, 41);
        Assert.Equal(new[] { AutonomyActionType.Flash }, _performed);
    }

    [Fact]
    public void NothingArmsWithoutEnableConsentAndEntitlement()
    {
        CoreSettings.Current.AutonomyConsentGiven = false;
        Assert.False(Make().Start(withTimer: false));
        CoreSettings.Current.AutonomyConsentGiven = true;
        CoreEntitlement.HasPremiumProvider = () => false;
        Assert.False(Make().Start(withTimer: false));
        CoreEntitlement.IsFreeTodayProvider = k => k == "takeover";   // the ? box's free day
        try { Assert.True(Make().Start(withTimer: false)); }
        finally { CoreEntitlement.IsFreeTodayProvider = null; }
    }

    [Fact]
    public void LapsedEntitlementStopsHerActing()
    {
        var s = Make();
        s.Start(withTimer: false);
        CoreEntitlement.HasPremiumProvider = () => false;
        Advance(s, 200);
        Assert.Empty(_performed);
    }

    [Fact]
    public void BusyAndCooldownDeferToAShortRetry()
    {
        var s = Make();
        var busy = true;
        s.IsBusy = () => busy;
        s.Start(withTimer: false);
        Advance(s, 80);
        Assert.Empty(_performed);
        busy = false;
        Advance(s, 24);   // retry window is 12-24 s
        Assert.Single(_performed);
    }

    [Fact]
    public void StopDropsTheAnnouncedAction()
    {
        CoreSettings.Current.AutonomyAnnouncementChance = 100;
        var s = Make();
        string? said = null;
        s.AnnouncementMade += (_, p) => said = p;
        s.Start(withTimer: false);
        for (var i = 0; i < 100 && said == null; i++) Advance(s, 1);
        Assert.NotNull(said);
        Assert.Empty(_performed);   // 2 s announce delay
        s.Stop();                   // panic / Stop
        Advance(s, 5);
        Assert.Empty(_performed);
        Assert.False(s.IsEnabled);
    }

    [Fact]
    public void UnperformableActionsAreNeverPicked()
    {
        CoreSettings.Current.AutonomyCanTriggerFlash = false;
        CoreSettings.Current.AutonomyCanTriggerMindWipe = true;   // not on this head
        var s = Make();
        s.Start(withTimer: false);
        Advance(s, 300);
        Assert.Empty(_performed);
    }

    [Fact]
    public void IntervalRulesMatchWpf()
    {
        var st = new AppSettings { AutonomyRandomIntervalSeconds = 60, AutonomyTimeAwareEnabled = true, AutonomyNightMultiplier = 2 };
        Assert.Equal(20, AutonomyScheduler.NextRandomSeconds(st, false, false, 23, 0.0), 3);   // 40 s / 2 at night
        Assert.Equal(24, AutonomyScheduler.NextRandomSeconds(st, true, false, 23, 1.0), 3);    // retry ceiling
        Assert.Equal(AutonomyMood.Mischievous, AutonomyScheduler.MoodAt(3));
        Assert.Equal(AutonomyMood.Gentle, AutonomyScheduler.MoodAt(8));
    }
}
