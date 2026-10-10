using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The lifetime counters WPF 7.1.5 AchievementService / GamificationBridge move and the port never did
/// (k23 audit): each moves at its WPF moment, EMI's two once-ever moments ride the first count, a
/// system-initiated engine start counts no relapse, and the wallet is never touched by a stat.
/// </summary>
[Collection(SessionStatics.Name)]   // swaps EmiDeskBus.Sink and AchievementEngine.Current
public sealed class AchievementLifetimeCountersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-ach-life-").FullName;
    private readonly Action<string, object?>? _sink = EmiDeskBus.Sink;
    private readonly AchievementEngine? _current = AchievementEngine.Current;
    private readonly List<string> _moments = new();

    public AchievementLifetimeCountersTests() => EmiDeskBus.Sink = (id, _) => _moments.Add(id);

    public void Dispose()
    {
        EmiDeskBus.Sink = _sink;
        AchievementEngine.Attach(_current);
        Directory.Delete(_dir, recursive: true);
    }

    private AchievementEngine Engine() => new(new AchievementStore(Path.Combine(_dir, "achievements.json")));

    [Fact]
    public void AFlashCounts_AndOnlyTheFirstEverTellsEmi()
    {
        var e = Engine();
        e.TrackFlashImage();
        e.TrackFlashImage();
        Assert.Equal(2, e.Progress.TotalFlashImages);
        Assert.True(e.IsDirty);
        Assert.Single(_moments, "firstFlashEver");
        Assert.False(e.Progress.IsUnlocked("retinal_burn"));

        e.Progress.TotalFlashImages = AchievementRules.RetinalBurnFlashImages - 1;
        e.TrackFlashImage();
        Assert.True(e.Progress.IsUnlocked("retinal_burn"));
        Assert.Single(_moments, "firstFlashEver");   // an account already past one never hears it
    }

    [Fact]
    public void ASessionStartCounts_ResetsTheAltTabFlag_AndOnlyTheFirstEverTellsEmi()
    {
        var e = Engine();
        e.TrackAltTab();
        e.TrackSessionStart();
        Assert.Equal(1, e.Progress.TotalSessionsStarted);
        Assert.False(e.Progress.AltTabPressedThisSession);
        e.TrackSessionStart();
        Assert.Equal(2, e.Progress.TotalSessionsStarted);
        Assert.Single(_moments, "firstSessionEver");

        e.TrackSessionAbandoned();
        Assert.Equal(1, e.Progress.TotalSessionsAbandoned);
    }

    [Fact]
    public void Relapse_IsAStartInsideTenSecondsOfAPanicPress()
    {
        var e = Engine();
        var t = new DateTime(2026, 10, 10, 12, 0, 0);
        e.Now = () => t;
        e.CheckRelapse();                                   // never pressed
        Assert.False(e.Progress.IsUnlocked("relapse"));
        e.TrackPanicPressed();
        t = t.AddSeconds(11);
        e.CheckRelapse();
        Assert.False(e.Progress.IsUnlocked("relapse"));
        e.TrackPanicPressed();
        t = t.AddSeconds(9);
        e.TrackSessionStart();                              // a session start checks it too
        Assert.True(e.Progress.IsUnlocked("relapse"));
    }

    [Fact]
    public void ASystemInitiatedEngineStart_NeverChecksRelapse()
    {
        var e = Engine();
        AchievementEngine.Attach(e);
        bool wasRunning = CoreEngine.IsRunning;
        if (wasRunning) return;                             // never stop somebody else's engine
        int sessions = CoreSettings.Current.TotalSessions;
        try
        {
            e.TrackPanicPressed();
            CoreEngine.Start(systemInitiated: true);
            Assert.False(e.Progress.IsUnlocked("relapse"));
            Assert.Equal(0, e.Progress.TotalSessionsStarted);   // an engine start is not a session start
            Assert.Equal(sessions, CoreSettings.Current.TotalSessions);
        }
        finally { CoreEngine.Stop(); }
    }

    [Fact]
    public void ASessionComplete_RecordsTheNameTheLongestRunAndTheStartFlags()
    {
        var e = Engine();
        e.Now = () => new DateTime(2026, 10, 10, 7, 30, 0);
        e.TrackSessionComplete("Morning Drift", 42, noPanicEnabled: true, strictLockEnabled: false);
        Assert.Contains("Morning Drift", e.Progress.CompletedSessions);
        Assert.Equal(42, e.Progress.LongestSessionMinutes);
        Assert.True(e.Progress.CompletedSessionWithNoPanic);
        Assert.True(e.Progress.CompletedMorningDriftInMorning);
        Assert.False(e.Progress.IsUnlocked("deep_sleep"));

        e.TrackSessionComplete("Good Girls Don't Cum", 10, noPanicEnabled: false, strictLockEnabled: true);
        Assert.True(e.Progress.CompletedGoodGirlsWithStrictLock);
        Assert.Equal(42, e.Progress.LongestSessionMinutes);   // a shorter run never lowers it

        e.TrackAltTab();
        e.TrackSessionComplete("Gamer Girl", 5, false, false);
        Assert.False(e.Progress.CompletedGamerGirlNoAltTab);
        e.TrackSessionStart();
        e.TrackSessionComplete("Gamer Girl", 5, false, false);
        Assert.True(e.Progress.CompletedGamerGirlNoAltTab);
    }

    [Fact]
    public void ALockCard_CountsAndSavesAtOnce_PerfectAndFastAreTheirOwnFlags()
    {
        var e = Engine();
        e.TrackLockCardCompletion(seconds: 40, errors: 2, phrases: 3);
        Assert.Equal(1, e.Progress.TotalLockCardsCompleted);
        Assert.False(e.Progress.HasPerfectLockCard);
        Assert.False(e.IsDirty);                            // saved, so sync picks the count up
        Assert.Equal(1, new AchievementStore(Path.Combine(_dir, "achievements.json")).Load().TotalLockCardsCompleted);

        e.TrackLockCardCompletion(seconds: 12, errors: 0, phrases: 3);
        Assert.True(e.Progress.HasPerfectLockCard);
        Assert.Equal(12, e.Progress.FastestLockCardSeconds);
        e.TrackLockCardCompletion(seconds: 5, errors: 0, phrases: 1);   // one phrase is not the speed run
        Assert.Equal(12, e.Progress.FastestLockCardSeconds);
    }

    [Fact]
    public void EarnedTotalsAreStats_TheWalletIsNeverTouched()
    {
        var e = Engine();
        int wallet = CoreSettings.Current.SkillPoints;
        e.TrackSkillPointsEarned(3);
        e.TrackSkillPointsEarned(-5);
        e.TrackXPEarned(120.5);
        e.TrackXPEarned(-10);
        Assert.Equal(3, e.Progress.TotalSkillPointsEarned);
        Assert.Equal(120.5, e.Progress.TotalXPEarned);
        Assert.Equal(wallet, CoreSettings.Current.SkillPoints);
    }

    [Fact]
    public void ALevelUp_CountsItsPointsAsEarned()
    {
        var e = Engine();
        AchievementEngine.Attach(e);
        var s = new AppSettings();
        SkillPointsBank.OnLevelUp(s, newLevel: 4, levelsGained: 2);
        Assert.Equal(2 * SkillPointsBank.PointsPerLevel, e.Progress.TotalSkillPointsEarned);
    }

    [Fact]
    public void OverlayMinutesAccumulate_TheContinuousSpiralStartsOverWhenItHides()
    {
        var e = Engine();
        e.TrackPinkFilterMinutes(1.5);
        e.TrackSpiralMinutes(19);
        Assert.Equal(1.5, e.Progress.TotalPinkFilterMinutes);
        Assert.False(e.Progress.IsUnlocked("spiral_eyes"));
        e.ResetContinuousSpiral();
        e.TrackSpiralMinutes(2);
        Assert.Equal(21, e.Progress.TotalSpiralMinutes);
        Assert.Equal(2, e.Progress.ContinuousSpiralMinutes);
        Assert.False(e.Progress.IsUnlocked("spiral_eyes"));
        e.TrackSpiralMinutes(18);
        Assert.True(e.Progress.IsUnlocked("spiral_eyes"));
        e.TrackDeeperMinutes(3);
        Assert.Equal(3, e.Progress.DeeperMinutes);
    }

    [Fact]
    public void SettingCombos_LatchOnce_AndNeverWriteASetting()
    {
        var e = Engine();
        var s = new AppSettings { BubblesEnabled = true, BouncingTextEnabled = true, SpiralEnabled = false };
        e.CheckSettingCombos(s);
        Assert.False(e.Progress.HasSystemOverload);
        s.SpiralEnabled = true;
        e.CheckSettingCombos(s);
        Assert.True(e.Progress.HasSystemOverload);
        Assert.True(s.SpiralEnabled && s.BubblesEnabled && s.BouncingTextEnabled);

        s.StrictLockEnabled = true; s.PinkFilterEnabled = true; s.PanicKeyEnabled = true;
        e.CheckSettingCombos(s);
        Assert.False(e.Progress.HasTotalLockdown);          // panic still on
        Assert.True(s.PanicKeyEnabled);                      // and the check never switched it
    }

    [Fact]
    public void BridgeCounters_ModsMessagesBlinksGazeAndPlays()
    {
        var e = Engine();
        e.TrackModActivated("a", builtIn: true);
        e.TrackModActivated("a", builtIn: true);
        e.TrackModActivated("b", builtIn: false);
        Assert.Equal(2, e.Progress.ActivatedModIds.Count);
        Assert.Single(e.Progress.CommunityModIds);
        e.TrackModInstalled();
        Assert.Equal(1, e.Progress.ModsInstalled);

        e.TrackCompanionMessage();
        Assert.Equal(1, e.Progress.CompanionMessages);
        Assert.True(e.Progress.IsUnlocked("pleased_to_meet_you"));
        e.TrackBlinkTrainerBlink();
        e.TrackGazePop();
        e.TrackEnhancementPlayed(2);
        Assert.Equal(1, e.Progress.BlinkTrainerBlinks);
        Assert.Equal(1, e.Progress.GazePops);
        Assert.Equal(1, e.Progress.EnhancementsPlayed);
        Assert.True(e.Progress.IsUnlocked("going_deeper"));
        Assert.False(e.Progress.IsUnlocked("on_rails"));

        e.TrackRemoteCommand();
        e.TrackRemoteCommand();
        Assert.Equal(2, e.Progress.RemoteCommandsThisSession);
        e.ResetRemoteSession();
        Assert.Equal(0, e.Progress.RemoteCommandsThisSession);   // one session's count, never carried over
    }

    [Fact]
    public void Attach_HooksXpAndIsUndoneByNull()
    {
        var e = Engine();
        AchievementEngine.Attach(e);
        Assert.Same(e, AchievementEngine.Current);
        AchievementEngine.Attach(null);
        Assert.Null(AchievementEngine.Current);
        try { AchievementEngine.Current?.TrackFlashImage(); } catch { }
        Assert.Equal(0, e.Progress.TotalFlashImages);
    }
}
