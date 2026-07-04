using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Timeline "start at minute X" deferral (#483, WPF SessionEngine.cs:857-875 pending-starts
/// queue). Covers: (1) delayed features stay disabled at session start (scope flag gating),
/// (2) queued starts fire when the session clock reaches their minute, (3) Stop drops
/// unfired entries, (4) StartMinute == 0 features enable immediately (regression), and
/// (5) pause/resume preserves pending entries without firing them prematurely.
/// Ticks are driven via reflection like SessionEngineTests (real DispatcherTimer time is
/// not steerable in headless tests).
/// </summary>
public class SessionDeferredStartTests
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
        public double GetSessionXPMultiplier(int playerLevel) => 1.0;
        public double GetXPForLevel(int level) => 100.0;
        public double GetTotalXP(int level, double currentXP) => (level - 1) * 100.0 + currentXP;
        public double GetCurrentLevelXP(int level, double totalXP) => totalXP - (level - 1) * 100.0;
        public event EventHandler<int>? LevelUp { add { } remove { } }
    }

    private static Session CreateSession(SessionSettings? settings = null)
    {
        return new Session
        {
            Id = "deferred-test",
            Name = "Deferred Start Test",
            DurationMinutes = 30,
            BonusXP = 100,
            Settings = settings ?? new SessionSettings(),
            Phases = new List<SessionPhase>()
        };
    }

    /// <summary>All deferrable flag-backed features on, each with a distinct StartMinute.</summary>
    private static SessionSettings AllFeaturesWithStartMinutes() => new()
    {
        FlashEnabled = true,
        FlashStartMinute = 5,
        SubliminalEnabled = true,
        SubliminalStartMinute = 3,
        AudioWhispersEnabled = true,
        AudioWhispersStartMinute = 2,
        BouncingTextEnabled = true,
        BouncingTextStartMinute = 4,
        MandatoryVideosEnabled = true,
        MandatoryVideosStartMinute = 6,
        LockCardEnabled = true,
        LockCardStartMinute = 7,
        BubbleCountEnabled = true,
        BubbleCountStartMinute = 8
    };

    private static void Tick(SessionService service)
    {
        var method = typeof(SessionService).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);
    }

    /// <summary>Drives the deferred-starts check with a fake session clock (minutes).</summary>
    private static void CheckDeferred(SessionService service, double elapsedMinutes)
    {
        var method = typeof(SessionService).GetMethod("CheckDeferredFeatureStarts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, new object[] { elapsedMinutes });
    }

    [AvaloniaFact]
    public async Task DelayedFeatures_StayDisabledAtSessionStart()
    {
        var settings = new FakeSettingsService();
        var current = settings.Current;
        var service = new SessionService(settings, new FakeProgressionService());

        await service.StartSessionAsync(CreateSession(AllFeaturesWithStartMinutes()));

        // Scope.Apply gates every live enable flag whose StartMinute > 0
        // (WPF SessionEngine.cs:892-1153 defer branches).
        Assert.False(current.FlashEnabled);
        Assert.False(current.SubliminalEnabled);
        Assert.False(current.SubAudioEnabled);
        Assert.False(current.BouncingTextEnabled);
        Assert.False(current.MandatoryVideosEnabled);
        Assert.False(current.LockCardEnabled);
        Assert.False(current.BubbleCountEnabled);

        // The tick at ~0 elapsed minutes must not fire anything queued either.
        service.DeferFeatureStart("flash", 5, () => current.FlashEnabled = true);
        Tick(service);
        Assert.False(current.FlashEnabled);
        Assert.True(service.IsFeaturePending("flash"));

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task DeferredStart_FiresWhenSessionClockReachesMinute()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        int fired = 0;
        service.DeferFeatureStart("flash", 5, () => fired++);
        Assert.True(service.IsFeaturePending("flash"));

        CheckDeferred(service, 4.9);
        Assert.Equal(0, fired);
        Assert.True(service.IsFeaturePending("flash"));

        CheckDeferred(service, 5.0);
        Assert.Equal(1, fired);
        Assert.False(service.IsFeaturePending("flash"));

        // Fired entries are removed in place; later ticks must not re-fire them.
        CheckDeferred(service, 6.0);
        Assert.Equal(1, fired);

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task StopSession_ClearsPendingStarts_NothingFiresAfter()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        int fired = 0;
        service.DeferFeatureStart("subliminal", 1, () => fired++);
        Assert.True(service.IsFeaturePending("subliminal"));

        service.StopSession(completed: false);

        // Stop drops unfired entries (WPF SessionEngine.cs:277-279); even a check far past
        // the target minute fires nothing.
        Assert.False(service.IsFeaturePending("subliminal"));
        CheckDeferred(service, 999.0);
        Assert.Equal(0, fired);
    }

    [AvaloniaFact]
    public async Task StartSession_DropsStalePendingFromPreviousRun()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());
        int fired = 0;
        service.DeferFeatureStart("video", 1, () => fired++);
        service.StopSession(completed: false);

        // A fresh start also clears before applying the scope
        // (WPF ApplySessionSettings clears first, SessionEngine.cs:879-881).
        await service.StartSessionAsync(CreateSession());
        Assert.False(service.IsFeaturePending("video"));
        CheckDeferred(service, 999.0);
        Assert.Equal(0, fired);

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task ImmediateFeatures_EnabledAtSessionStart()
    {
        var settings = new FakeSettingsService();
        var current = settings.Current;
        var service = new SessionService(settings, new FakeProgressionService());

        // All features on with the default StartMinute of 0: regression guard that the
        // scope still enables everything at t=0.
        var session = CreateSession(new SessionSettings
        {
            FlashEnabled = true,
            SubliminalEnabled = true,
            AudioWhispersEnabled = true,
            BouncingTextEnabled = true,
            MandatoryVideosEnabled = true,
            LockCardEnabled = true,
            BubbleCountEnabled = true
        });

        await service.StartSessionAsync(session);

        Assert.True(current.FlashEnabled);
        Assert.True(current.SubliminalEnabled);
        Assert.True(current.SubAudioEnabled);
        Assert.True(current.BouncingTextEnabled);
        Assert.True(current.MandatoryVideosEnabled);
        Assert.True(current.LockCardEnabled);
        Assert.True(current.BubbleCountEnabled);

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task PauseResume_PreservesPendingStarts_WithoutFiringEarly()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        int fired = 0;
        service.DeferFeatureStart("bouncing text", 5, () => fired++);

        // Pause and resume must neither drop nor prematurely fire the entry — the resume
        // path checks IsFeaturePending to skip it (WPF SessionEngine.cs:434-452).
        service.PauseSession();
        Assert.True(service.IsFeaturePending("bouncing text"));
        service.ResumeSession();
        Assert.True(service.IsFeaturePending("bouncing text"));
        Assert.Equal(0, fired);

        // Once the session clock reaches the minute, it still fires.
        CheckDeferred(service, 5.0);
        Assert.Equal(1, fired);
        Assert.False(service.IsFeaturePending("bouncing text"));

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task DeferredStart_FailureDoesNotBlockOtherPendingStarts()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        int fired = 0;
        service.DeferFeatureStart("lock card", 3, () => throw new InvalidOperationException("boom"));
        service.DeferFeatureStart("bubble count", 3, () => fired++);

        // A throwing fire action is swallowed and removed (WPF SessionEngine.cs:612-619);
        // the other due entry still fires on the same check.
        CheckDeferred(service, 3.0);
        Assert.Equal(1, fired);
        Assert.False(service.IsFeaturePending("lock card"));
        Assert.False(service.IsFeaturePending("bubble count"));

        service.StopSession(completed: false);
    }

    [AvaloniaFact]
    public async Task MixedStartMinutes_OnlyDelayedFeaturesAreGated()
    {
        var settings = new FakeSettingsService();
        var current = settings.Current;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(new SessionSettings
        {
            FlashEnabled = true,           // immediate
            SubliminalEnabled = true,
            SubliminalStartMinute = 10,    // delayed
            AudioWhispersEnabled = true,   // immediate (WPF SessionEngine.cs:957 exact gate)
            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 15 // delayed
        });

        await service.StartSessionAsync(session);

        Assert.True(current.FlashEnabled);
        Assert.False(current.SubliminalEnabled);
        Assert.True(current.SubAudioEnabled);
        Assert.False(current.MandatoryVideosEnabled);

        service.StopSession(completed: false);
    }
}
