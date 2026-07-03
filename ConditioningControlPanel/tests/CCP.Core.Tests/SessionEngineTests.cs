using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class SessionEngineTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private class FakeProgressionService : IProgressionService
    {
        public void AddXP(int amount, XPSource source) { }
        public double GetSessionXPMultiplier(int playerLevel) => 1.0 + playerLevel * 0.02;
        public double GetXPForLevel(int level) => 100.0;
        public double GetTotalXP(int level, double currentXP) => (level - 1) * 100.0 + currentXP;
        public double GetCurrentLevelXP(int level, double totalXP) => totalXP - (level - 1) * 100.0;
        public event EventHandler<int>? LevelUp { add { } remove { } }
    }

    private class FakeSessionLogService : ISessionLogService
    {
        public List<Session> Begun { get; } = new();
        public List<(bool Completed, TimeSpan Duration, int Xp)> Ended { get; } = new();
        public event EventHandler<SessionLogReadyEventArgs>? LogReady { add { } remove { } }
        public void BeginSession(Session session) => Begun.Add(session);
        public void EndSession(bool completed, TimeSpan duration, int xpEarned) => Ended.Add((completed, duration, xpEarned));
        public IReadOnlyList<ConditioningControlPanel.Models.SessionLog> LoadRecentLogs() => Array.Empty<ConditioningControlPanel.Models.SessionLog>();
    }

    private class FakeAchievementService : IAchievementService
    {
        public int SessionStartCount;
        public int SessionAbandonedCount;
        public int PanicPressedCount;
        public (string Name, double Minutes, bool NoPanic, bool StrictLock)? LastSessionComplete;

        public AchievementProgress Progress { get; } = new();
        public event EventHandler<Achievement>? AchievementUnlocked { add { } remove { } }
        public bool SuppressPopups { get; set; }
        public bool TryUnlock(string achievementId) => false;
        public void TrackFeatureUsed(string featureId, double amount = 1) { }
        public void TrackTimeProgress() { }
        public void CheckLevelAchievements(int level) { }
        public void CheckDailyMaintenance() { }
        public void TrackFlashImage() { }
        public void TrackBubblePopped() { }
        public void TrackBubbleCountResult(bool correct) { }
        public void TrackLockCardCompletion(double seconds, int totalChars, int errors, int phrases) { }
        public void TrackVideoWatched(double durationSeconds) { }
        public void TrackAttentionCheckFailed() { }
        public void TrackMindWipeDuration(double seconds) { }
        public void TrackCornerHit() { }
        public void TrackAvatarClick() { }
        public void TrackAltTab() { }
        public void TrackPanicPressed() => PanicPressedCount++;
        public void TrackSessionStart() => SessionStartCount++;
        public void CheckRelapse() { }
        public void TrackSessionComplete(string sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled)
            => LastSessionComplete = (sessionName, durationMinutes, noPanicEnabled, strictLockEnabled);
        public void TrackAttentionCheckPassed(bool isVideo = false) { }
        public void TrackVideoAttentionCheckFailed() { }
        public void TrackBubbleCountGameStarted() { }
        public void TrackBubbleCountGameResult(bool success) { }
        public void TrackSessionStarted() { }
        public void TrackSessionAbandoned() => SessionAbandonedCount++;
        public void TrackXPEarned(double amount) { }
        public void TrackSkillPointsEarned(int amount) { }
        public void MarkDirty() { }
        public void ResetProgress() { }
        public int GetUnlockedCount() => 0;
        public int GetTotalCount() => 0;
        public int GetUnlockedCount(bool exclusive) => 0;
        public int GetTotalCount(bool exclusive) => 0;
        public bool CanUnlockExclusive => false;
        public bool TryUnlockExclusive(string achievementId) => false;
        public void Save() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeOverlayService : IOverlayService
    {
        public double? LastPink;
        public double? LastSpiral;
        public int ReleaseCount;

        public bool IsRunning => false;
        public bool BypassLevelCheck { get; set; }
        public void Start() { }
        public void Stop() { }
        public void RefreshOverlays() { }
        public void PulseOverlays() { }
        public void RefreshForMultiMonitorChange() { }
        public void ShowOverlayTimed(string kind, int durationMs, double opacity) { }
        public void ShowOverlaySustained(string kind, double opacity) { }
        public void HideOverlaySustained(string kind) { }
        public void SetSustainedOverlayOpacity(string kind, double opacity)
        {
            if (kind == "pink_filter") LastPink = opacity;
            else if (kind == "spiral") LastSpiral = opacity;
        }
        public void ReleaseOpacityRampHolds() => ReleaseCount++;
        public void WarmSpiralCache() { }
        public void NotifyTopWindowOpened() { }
        public void NotifyTopWindowClosed() { }
    }

    private static Session CreateSession(int durationMinutes = 10, int bonusXP = 400)
    {
        return new Session
        {
            Id = "test",
            Name = "Test Session",
            DurationMinutes = durationMinutes,
            BonusXP = bonusXP,
            Settings = new SessionSettings(),
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Start" },
                new() { StartMinute = 5, Name = "Middle" },
                new() { StartMinute = 10, Name = "End" }
            }
        };
    }

    private static void Tick(SessionService service)
    {
        var method = typeof(SessionService).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);
    }

    [AvaloniaFact]
    public async Task SessionRamp_DirectDrivesOverlay_DoesNotMutatePersistedOpacity()
    {
        // Lot-2 re-visit (merge 5ce70de6): the session pink/spiral ramp must direct-drive the
        // overlay (WPF SessionEngine #471/#476) and NOT write the ramped value into the
        // auto-saving settings.Current, which used to freeze the ramp maximum into settings.json
        // on a crash/kill mid-session. IntensityRampService (the MANUAL ramp) intentionally still
        // writes settings — that matches WPF RampTimer_Tick and is deliberately not touched here.
        var settings = new FakeSettingsService();
        var overlay = new FakeOverlayService();
        var service = new SessionService(settings, new FakeProgressionService(), overlay: overlay);

        var session = CreateSession();
        session.Settings.PinkFilterEnabled = true;
        session.Settings.PinkFilterStartMinute = 0;
        session.Settings.PinkFilterStartOpacity = 10;
        session.Settings.PinkFilterEndOpacity = 50;
        session.Settings.SpiralEnabled = true;
        session.Settings.SpiralStartMinute = 0;
        session.Settings.SpiralOpacity = 5;
        session.Settings.SpiralOpacityEnd = 45;

        await service.StartSessionAsync(session);

        // Scope.Apply seeds the START opacity into settings for immediate (StartMinute==0) ramps.
        Assert.Equal(10, settings.Current.PinkFilterOpacity);
        Assert.Equal(5, settings.Current.SpiralOpacity);

        // Drive the ramp to 50% (5 of 10 minutes). StartMinute==0 => randomized start == 0.
        InvokeRamp(service, 5.0, 10.0);

        // Overlay was direct-driven with the lerped value, normalized 0..1.
        Assert.NotNull(overlay.LastPink);
        Assert.Equal(0.30, overlay.LastPink!.Value, 3);   // Lerp(10,50,0.5)=30 -> 0.30
        Assert.NotNull(overlay.LastSpiral);
        Assert.Equal(0.25, overlay.LastSpiral!.Value, 3);  // Lerp(5,45,0.5)=25 -> 0.25

        // The persisted settings were NOT mutated by the ramp (still the seeded START values);
        // under the pre-fix bug these would read 30 and 25 and auto-save to disk on crash.
        Assert.Equal(10, settings.Current.PinkFilterOpacity);
        Assert.Equal(5, settings.Current.SpiralOpacity);
    }

    [AvaloniaFact]
    public async Task StopSession_ReleasesOverlayRampHolds()
    {
        var overlay = new FakeOverlayService();
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService(), overlay: overlay);
        await service.StartSessionAsync(CreateSession());

        service.StopSession(completed: false);

        Assert.True(overlay.ReleaseCount >= 1);
    }

    [AvaloniaFact]
    public async Task StartSession_SetsRunningAndRaisesStarted()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());

        bool started = false;
        service.SessionStarted += (_, _) => started = true;

        await service.StartSessionAsync(CreateSession());

        Assert.Equal(SessionState.Running, service.State);
        Assert.NotNull(service.CurrentSession);
        Assert.True(started);
        Assert.Equal(0, service.CurrentPhaseIndex);
    }

    [AvaloniaFact]
    public async Task StartSession_RaisesPhaseChanged_WhenPhasesExist()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());

        SessionPhase? phase = null;
        service.PhaseChanged += (_, e) => phase = e.Phase;

        await service.StartSessionAsync(CreateSession());

        Assert.NotNull(phase);
        Assert.Equal("Start", phase!.Name);
    }

    [AvaloniaFact]
    public async Task StartSession_AlreadyRunning_Throws()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartSessionAsync(CreateSession()));
    }

    [AvaloniaFact]
    public async Task StopSession_NotCompleted_DoesNotRaiseCompleted()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        bool completed = false;
        service.SessionCompleted += (_, _) => completed = true;

        service.StopSession(completed: false);

        Assert.Equal(SessionState.Idle, service.State);
        Assert.False(completed);
    }

    [AvaloniaFact]
    public async Task StopSession_Completed_RaisesCompletedWithExpectedXP()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(bonusXP: 400);
        await service.StartSessionAsync(session);

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        Assert.NotNull(args);
        Assert.Equal(session, args!.Session);
        // base 400 * multiplier 1.02 = 408, no duration bonus.
        Assert.Equal(408, args.XPEarned);
        Assert.Equal(0, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task PauseSession_IncrementsPauseCountAndAppliesPenalty()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        service.PauseSession();

        Assert.Equal(SessionState.Paused, service.State);
        Assert.Equal(1, service.PauseCount);
        Assert.Equal(100, service.XPPenalty);
    }

    [AvaloniaFact]
    public async Task ResumeSession_ReturnsToRunning()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());
        service.PauseSession();

        service.ResumeSession();

        Assert.Equal(SessionState.Running, service.State);

        // Ensure the timer tick path is wired up again.
        Tick(service);
        Assert.Equal(SessionState.Running, service.State);
    }

    [AvaloniaFact]
    public async Task StopSession_AfterPause_AppliesXPPenalty()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(bonusXP: 400);
        await service.StartSessionAsync(session);
        service.PauseSession();

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        // base 400 - 100 penalty = 300; * 1.02 = 306
        Assert.Equal(306, args!.XPEarned);
        Assert.Equal(1, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task CompletedXP_IncludesDurationBonus()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(durationMinutes: 10, bonusXP: 400);
        await service.StartSessionAsync(session);
        service.PauseSession();

        // Fake 5 minutes of elapsed time so the duration bonus applies.
        var elapsedField = typeof(SessionService).GetField("_pausedElapsedTime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        elapsedField.SetValue(service, TimeSpan.FromMinutes(5));

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        // base (400 - 100 pause penalty) * 1.02 = 306; duration minutes = 5 - 2 = 3; bonus = round(3 * (8 + 0.15)) = 24
        Assert.Equal(330, args!.XPEarned);
        Assert.Equal(1, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task PhaseTransition_AdvancesCurrentPhase()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        var phaseChanges = new List<string>();
        service.PhaseChanged += (_, e) => phaseChanges.Add(e.Phase.Name);

        var checkPhase = typeof(SessionService).GetMethod("CheckPhaseTransition", BindingFlags.NonPublic | BindingFlags.Instance)!;
        checkPhase.Invoke(service, new object[] { 5.0 });

        Assert.Equal(1, service.CurrentPhaseIndex);
        Assert.Equal("Middle", phaseChanges[^1]);
    }

    [AvaloniaFact]
    public async Task ProgressPercent_ComputesCorrectly()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession(durationMinutes: 10));
        service.PauseSession();

        var elapsedField = typeof(SessionService).GetField("_pausedElapsedTime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        elapsedField.SetValue(service, TimeSpan.FromMinutes(2.5));

        Assert.Equal(25.0, service.ProgressPercent, precision: 1);
    }

    private static void SetPausedElapsed(SessionService service, TimeSpan elapsed)
    {
        var elapsedField = typeof(SessionService).GetField("_pausedElapsedTime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        elapsedField.SetValue(service, elapsed);
    }

    private static void InvokeRamp(SessionService service, double elapsedMinutes, double totalMinutes)
    {
        var method = typeof(SessionService).GetMethod("UpdateRampingValues", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, new object[] { elapsedMinutes, totalMinutes });
    }

    [AvaloniaFact]
    public async Task SessionSettings_AppliedAtStart_AndRestoredAtStop()
    {
        var settings = new FakeSettingsService();
        var current = settings.Current;
        current.FlashEnabled = false;
        current.FlashFrequency = 10;
        current.FlashOpacity = 50;
        current.SubliminalEnabled = false;
        current.SubliminalPool.Clear();
        current.SubliminalPool["USER PHRASE"] = true;

        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession();
        session.Settings = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 120,
            FlashOpacity = 90,
            FlashImages = 3,
            SubliminalEnabled = true,
            SubliminalPerMin = 12,
            SubliminalPhrases = new List<string> { "SESSION PHRASE" }
        };

        await service.StartSessionAsync(session);

        Assert.True(current.FlashEnabled);
        Assert.Equal(120, current.FlashFrequency);
        Assert.Equal(90, current.FlashOpacity);
        Assert.Equal(3, current.SimultaneousImages);
        Assert.True(current.SubliminalEnabled);
        Assert.Equal(12, current.SubliminalFrequency);
        Assert.False(current.SubliminalPool["USER PHRASE"]);
        Assert.True(current.SubliminalPool["SESSION PHRASE"]);

        service.StopSession(completed: false);

        Assert.False(current.FlashEnabled);
        Assert.Equal(10, current.FlashFrequency);
        Assert.Equal(50, current.FlashOpacity);
        Assert.False(current.SubliminalEnabled);
        Assert.True(current.SubliminalPool["USER PHRASE"]);
        Assert.False(current.SubliminalPool.ContainsKey("SESSION PHRASE"));
    }

    [AvaloniaFact]
    public async Task RampInterpolation_WritesFlashLerpIntoSettings_ButDirectDrivesPinkOverlay()
    {
        var settings = new FakeSettingsService();
        var overlay = new FakeOverlayService();
        var service = new SessionService(settings, new FakeProgressionService(), overlay: overlay);

        var session = CreateSession(durationMinutes: 10);
        session.Settings = new SessionSettings
        {
            FlashEnabled = true,
            FlashOpacity = 20,
            FlashOpacityEnd = 100,
            FlashPerHour = 10,
            FlashPerHourEnd = 110,
            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0, // no jitter when 0, so the ramp is deterministic
            PinkFilterStartOpacity = 10,
            PinkFilterEndOpacity = 50
        };

        await service.StartSessionAsync(session);

        // Flash opacity/frequency still ramp INTO settings (WPF parity — only pink/spiral were
        // moved off settings by #471/#476). Pink is direct-driven to the overlay and must NOT
        // mutate the persisted PinkFilterOpacity, which stays at the scope-seeded start of 10.
        InvokeRamp(service, 0, 10);
        Assert.Equal(20, settings.Current.FlashOpacity);
        Assert.Equal(10, settings.Current.FlashFrequency);
        Assert.Equal(10, settings.Current.PinkFilterOpacity);
        Assert.Equal(0.10, overlay.LastPink!.Value, 3);

        InvokeRamp(service, 5, 10);
        Assert.Equal(60, settings.Current.FlashOpacity);
        Assert.Equal(60, settings.Current.FlashFrequency);
        Assert.Equal(10, settings.Current.PinkFilterOpacity);   // unchanged by the ramp
        Assert.Equal(0.30, overlay.LastPink!.Value, 3);

        InvokeRamp(service, 10, 10);
        Assert.Equal(100, settings.Current.FlashOpacity);
        Assert.Equal(110, settings.Current.FlashFrequency);
        Assert.Equal(10, settings.Current.PinkFilterOpacity);   // unchanged by the ramp
        Assert.Equal(0.50, overlay.LastPink!.Value, 3);

        service.StopSession(completed: false);

        // Restore puts the pre-session flash values back after ramping mutated them; the pink
        // ramp holds are released so the overlay's settings-sync re-takes ownership.
        Assert.Equal(80, settings.Current.FlashOpacity);
        Assert.Equal(10, settings.Current.FlashFrequency);
        Assert.True(overlay.ReleaseCount >= 1);
    }

    [AvaloniaFact]
    public async Task StopSession_Completed_EndsLogExactlyOnce_WithRealElapsedAndXP()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var log = new FakeSessionLogService();
        var service = new SessionService(settings, new FakeProgressionService(), sessionLog: log);

        var session = CreateSession(bonusXP: 400);
        await service.StartSessionAsync(session);
        Assert.Single(log.Begun);

        service.PauseSession();
        SetPausedElapsed(service, TimeSpan.FromMinutes(5));

        service.StopSession(completed: true);

        var entry = Assert.Single(log.Ended);
        Assert.True(entry.Completed);
        Assert.Equal(TimeSpan.FromMinutes(5), entry.Duration);
        // base (400 - 100 pause penalty) * 1.02 = 306; duration bonus = round(3 * 8.15) = 24
        Assert.Equal(330, entry.Xp);
    }

    [AvaloniaFact]
    public async Task StopSession_Aborted_EndsLogExactlyOnce_WithRealElapsed()
    {
        var log = new FakeSessionLogService();
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService(), sessionLog: log);

        await service.StartSessionAsync(CreateSession());
        service.PauseSession();
        SetPausedElapsed(service, TimeSpan.FromMinutes(3));

        service.StopSession(completed: false);

        var entry = Assert.Single(log.Ended);
        Assert.False(entry.Completed);
        Assert.Equal(TimeSpan.FromMinutes(3), entry.Duration);
        Assert.Equal(0, entry.Xp);
    }

    [AvaloniaFact]
    public async Task PauseAndResume_RaiseEvents()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        int paused = 0, resumed = 0;
        service.SessionPaused += (_, _) => paused++;
        service.SessionResumed += (_, _) => resumed++;

        service.PauseSession();
        Assert.Equal(1, paused);
        Assert.Equal(0, resumed);

        service.ResumeSession();
        Assert.Equal(1, paused);
        Assert.Equal(1, resumed);
    }

    [AvaloniaFact]
    public async Task SessionStopped_CarriesFinalElapsedAndCompletedFlag()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());
        service.PauseSession();
        SetPausedElapsed(service, TimeSpan.FromMinutes(4));

        SessionStoppedEventArgs? args = null;
        service.SessionStopped += (_, e) => args = e;

        service.StopSession(completed: true);

        Assert.NotNull(args);
        Assert.True(args!.Completed);
        Assert.Equal(TimeSpan.FromMinutes(4), args.FinalElapsed);
    }

    [AvaloniaFact]
    public async Task StartSession_IncrementsTotalSessionsOncePerStart()
    {
        var settings = new FakeSettingsService();
        Assert.Equal(0, settings.Current.TotalSessions);
        var service = new SessionService(settings, new FakeProgressionService());

        await service.StartSessionAsync(CreateSession());
        Assert.Equal(1, settings.Current.TotalSessions);

        service.StopSession(completed: false);
        Assert.Equal(1, settings.Current.TotalSessions);

        await service.StartSessionAsync(CreateSession());
        Assert.Equal(2, settings.Current.TotalSessions);
        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task AchievementSnapshot_ImmuneToMidSessionSettingsFlips()
    {
        var settings = new FakeSettingsService();
        settings.Current.StrictLockEnabled = true;
        settings.Current.PanicKeyEnabled = false;
        var achievements = new FakeAchievementService();
        var service = new SessionService(settings, new FakeProgressionService(), achievements: achievements);

        await service.StartSessionAsync(CreateSession());
        Assert.Equal(1, achievements.SessionStartCount);

        // Flip both mid-session (e.g. autonomy toggles strict lock); the completion
        // check must use the values captured at session start.
        settings.Current.StrictLockEnabled = false;
        settings.Current.PanicKeyEnabled = true;

        service.StopSession(completed: true);

        Assert.NotNull(achievements.LastSessionComplete);
        var complete = achievements.LastSessionComplete!.Value;
        Assert.Equal("Test Session", complete.Name);
        Assert.True(complete.NoPanic);      // panic key was disabled at start
        Assert.True(complete.StrictLock);   // strict lock was enabled at start
        Assert.Equal(0, achievements.SessionAbandonedCount);
        Assert.Equal(0, achievements.PanicPressedCount);
    }

    [AvaloniaFact]
    public async Task StopSession_Aborted_TracksAbandonedAndPanic()
    {
        var achievements = new FakeAchievementService();
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService(), achievements: achievements);

        await service.StartSessionAsync(CreateSession());
        service.StopSession(completed: false);

        Assert.Equal(1, achievements.SessionAbandonedCount);
        Assert.Equal(1, achievements.PanicPressedCount);
        Assert.Null(achievements.LastSessionComplete);
    }
}
