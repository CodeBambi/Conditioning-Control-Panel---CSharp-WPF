using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Covers the manual intensity ramp port (WPF MainWindow.StartStop.cs:355-501):
/// multiplier math and caps, suppression of visual writes while a preset session is
/// active, EndSessionOnRampComplete latching, and baseline restore on stop.
/// [AvaloniaFact] because StartRamp creates a DispatcherTimer (never ticked here;
/// all assertions drive EvaluateTick with injected times).
/// </summary>
public class IntensityRampServiceTests
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

    private static readonly DateTime T0 = new(2026, 7, 6, 12, 0, 0);

    private static (IntensityRampService Service, AppSettings Settings) CreateRamp(
        bool sessionActive = false)
    {
        var settingsService = new FakeSettingsService();
        var s = settingsService.Current;
        s.IntensityRampEnabled = true;
        s.RampDurationMinutes = 60;      // clamped 10-180
        s.SchedulerMultiplier = 3.0;     // clamped 1.0-3.0
        s.FlashOpacity = 40;
        s.SpiralOpacity = 30;
        s.PinkFilterOpacity = 20;
        s.MasterVolume = 40;
        s.SubAudioVolume = 30;

        var service = new IntensityRampService(settingsService)
        {
            SessionActiveOverride = () => sessionActive
        };
        return (service, s);
    }

    [AvaloniaFact]
    public void MultiplierMath_At_0_50_100_Percent_WithCaps()
    {
        var (service, s) = CreateRamp();
        s.RampLinkFlashOpacity = true;
        s.RampLinkSpiralOpacity = true;
        s.RampLinkPinkFilterOpacity = true;
        s.RampLinkMasterAudio = true;
        s.RampLinkSubliminalAudio = true;

        service.StartRamp(T0);

        // progress 0 -> mult 1.0: everything stays at base.
        service.EvaluateTick(T0);
        Assert.Equal(40, s.FlashOpacity);
        Assert.Equal(30, s.SpiralOpacity);
        Assert.Equal(20, s.PinkFilterOpacity);
        Assert.Equal(40, s.MasterVolume);
        Assert.Equal(30, s.SubAudioVolume);

        // progress 0.5 -> mult 2.0. Spiral hits its 50 cap (30*2=60 -> 50).
        service.EvaluateTick(T0.AddMinutes(30));
        Assert.Equal(80, s.FlashOpacity);
        Assert.Equal(50, s.SpiralOpacity);
        Assert.Equal(40, s.PinkFilterOpacity);
        Assert.Equal(80, s.MasterVolume);
        Assert.Equal(60, s.SubAudioVolume);

        // progress 1.0 -> mult 3.0. Caps: flash 100, spiral 50, pink 50, master 100.
        service.EvaluateTick(T0.AddMinutes(60));
        Assert.Equal(100, s.FlashOpacity);   // 120 -> 100
        Assert.Equal(50, s.SpiralOpacity);   // 90 -> 50
        Assert.Equal(50, s.PinkFilterOpacity); // 60 -> 50
        Assert.Equal(100, s.MasterVolume);   // 120 -> 100
        Assert.Equal(90, s.SubAudioVolume);  // 90 (no cap hit)

        // Past the duration, progress stays capped at 1.0.
        service.EvaluateTick(T0.AddMinutes(120));
        Assert.Equal(100, s.FlashOpacity);
    }

    [AvaloniaFact]
    public void VisualRamps_SuppressedWhileSessionActive_AudioStillApplies()
    {
        var (service, s) = CreateRamp(sessionActive: true);
        s.RampLinkFlashOpacity = true;
        s.RampLinkSpiralOpacity = true;
        s.RampLinkPinkFilterOpacity = true;
        s.RampLinkMasterAudio = true;
        s.RampLinkSubliminalAudio = true;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30)); // mult 2.0

        // Visuals untouched: the session Lerp ramps own them (WPF StartStop.cs:432-434).
        Assert.Equal(40, s.FlashOpacity);
        Assert.Equal(30, s.SpiralOpacity);
        Assert.Equal(20, s.PinkFilterOpacity);
        // Audio links are not session-gated in WPF (StartStop.cs:470-484).
        Assert.Equal(80, s.MasterVolume);
        Assert.Equal(60, s.SubAudioVolume);
    }

    [AvaloniaFact]
    public void StopRamp_RestoresBaselines_ForMutatedFields()
    {
        var (service, s) = CreateRamp();
        s.RampLinkFlashOpacity = true;
        s.RampLinkMasterAudio = true;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30));
        Assert.Equal(80, s.FlashOpacity);
        Assert.Equal(80, s.MasterVolume);

        service.StopRamp();

        Assert.Equal(40, s.FlashOpacity);
        Assert.Equal(40, s.MasterVolume);
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public void StopRamp_LeavesUnmutatedFieldsAlone()
    {
        var (service, s) = CreateRamp();
        s.RampLinkMasterAudio = true; // only master is linked

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30));

        // User tweaks an unlinked slider mid-run; stop must not clobber it.
        s.FlashOpacity = 77;
        service.StopRamp();

        Assert.Equal(77, s.FlashOpacity);
        Assert.Equal(40, s.MasterVolume);
    }

    [AvaloniaFact]
    public void StopRamp_SkipsScopeManagedFields_WhenRampStartedDuringSession()
    {
        // Ramp started while a preset session is active: its baselines were captured
        // AFTER SessionSettingsScope.Apply, so they hold session values. StopRamp runs
        // after SessionSettingsScope.Restore and must not clobber the restored user
        // settings with those session-derived baselines (see IntensityRampService.StopRamp).
        var (service, s) = CreateRamp(sessionActive: true);
        s.RampLinkMasterAudio = true;
        s.RampLinkSubliminalAudio = true;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30));
        Assert.Equal(80, s.MasterVolume);
        Assert.Equal(60, s.SubAudioVolume);

        // Simulate SessionSettingsScope.Restore returning the user's own value.
        s.SubAudioVolume = 11;

        service.StopRamp();

        // MasterVolume is not scope-managed: restored to baseline.
        Assert.Equal(40, s.MasterVolume);
        // SubAudioVolume is scope-managed: the scope's restored value stands.
        Assert.Equal(11, s.SubAudioVolume);
    }

    [AvaloniaFact]
    public void RampCompleted_RaisedOnce_WhenConfigured_AndNoSessionActive()
    {
        var (service, s) = CreateRamp();
        s.EndSessionOnRampComplete = true;

        int completed = 0;
        service.RampCompleted += (_, _) => completed++;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30));
        Assert.Equal(0, completed);

        service.EvaluateTick(T0.AddMinutes(60));
        Assert.Equal(1, completed);

        // Latched: further ticks never re-raise (the head's stop path is async).
        service.EvaluateTick(T0.AddMinutes(62));
        Assert.Equal(1, completed);
    }

    [AvaloniaFact]
    public void RampCompleted_SuppressedWhileSessionActive()
    {
        // The preset owns its master timer and must run its full length
        // (WPF #444 fix, MainWindow.StartStop.cs:487-500).
        var (service, s) = CreateRamp(sessionActive: true);
        s.EndSessionOnRampComplete = true;

        int completed = 0;
        service.RampCompleted += (_, _) => completed++;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(60));

        Assert.Equal(0, completed);
    }

    [AvaloniaFact]
    public void RampCompleted_NotRaised_WhenSettingOff()
    {
        var (service, s) = CreateRamp();
        s.EndSessionOnRampComplete = false;

        int completed = 0;
        service.RampCompleted += (_, _) => completed++;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(60));

        Assert.Equal(0, completed);
    }

    [AvaloniaFact]
    public void StartRamp_IsIdempotent_KeepsOriginalBaselines()
    {
        var (service, s) = CreateRamp();
        s.RampLinkFlashOpacity = true;

        service.StartRamp(T0);
        service.EvaluateTick(T0.AddMinutes(30));
        Assert.Equal(80, s.FlashOpacity);

        // A preset joining the run calls StartRamp again; baselines must survive.
        service.StartRamp(T0.AddMinutes(30));
        service.EvaluateTick(T0.AddMinutes(30)); // still mult 2.0 from the ORIGINAL start
        Assert.Equal(80, s.FlashOpacity);

        service.StopRamp();
        Assert.Equal(40, s.FlashOpacity); // original baseline, not the 80 mid-run value
    }

    [AvaloniaFact]
    public void EvaluateTick_BeforeStart_DoesNothing()
    {
        var (service, s) = CreateRamp();
        s.RampLinkFlashOpacity = true;

        service.EvaluateTick(T0.AddMinutes(30));

        Assert.Equal(40, s.FlashOpacity);
        Assert.False(service.IsRunning);
    }
}
