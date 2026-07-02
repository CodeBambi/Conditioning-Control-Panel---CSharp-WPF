using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Services.Scheduler;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Covers the WPF scheduler port: the pure window predicate
/// (MainWindow.StartStop.cs:587-643) and the auto start/stop flag machine
/// (MainWindow.StartStop.cs:92-103, 546-585).
/// </summary>
public class SchedulerServiceTests
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

    // Fixed reference dates (asserted below so the calendar math can't drift).
    private static readonly DateTime Monday = new(2026, 7, 6);
    private static readonly DateTime Tuesday = new(2026, 7, 7);

    [Fact]
    public void ReferenceDates_AreTheExpectedWeekdays()
    {
        Assert.Equal(DayOfWeek.Monday, Monday.DayOfWeek);
        Assert.Equal(DayOfWeek.Tuesday, Tuesday.DayOfWeek);
    }

    private static AppSettings DaySettings(string start = "08:00", string end = "20:00")
    {
        // All seven day flags default to true in AppSettings.
        return new AppSettings
        {
            SchedulerEnabled = true,
            SchedulerStartTime = start,
            SchedulerEndTime = end,
        };
    }

    #region IsInScheduledTimeWindow (pure)

    [Fact]
    public void Window_SameDay_InsideIsTrue_OutsideIsFalse()
    {
        var s = DaySettings("08:00", "20:00");

        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(12)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(7)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(21)));
    }

    [Fact]
    public void Window_BoundaryMinutes_StartInclusive_EndExclusive()
    {
        var s = DaySettings("08:00", "20:00");

        // currentTime >= start && currentTime < end (WPF StartStop.cs:636).
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(8)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(8).AddMinutes(-1)));
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(20).AddMinutes(-1)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(20)));
    }

    [Fact]
    public void Window_InactiveDay_ReturnsFalse()
    {
        var s = DaySettings("08:00", "20:00");
        s.SchedulerMonday = false;

        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(12)));
        // Other days unaffected.
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Tuesday.AddHours(12)));
    }

    [Fact]
    public void Window_Overnight_BothSidesOfMidnight()
    {
        var s = DaySettings("22:00", "02:00");

        // Before midnight (Monday 23:00) and after midnight (Tuesday 01:00).
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(23)));
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Tuesday.AddHours(1)));
        // Midday is outside.
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(12)));
        // Boundaries: start inclusive, end exclusive (WPF StartStop.cs:631).
        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(22)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Tuesday.AddHours(2)));
    }

    [Fact]
    public void Window_Overnight_AfterMidnight_ChecksTheCurrentDaysFlag()
    {
        // WPF gates on the flag of the CURRENT day (StartStop.cs:593-603): a Monday
        // 22:00-02:00 window's post-midnight half needs the TUESDAY flag.
        var s = DaySettings("22:00", "02:00");
        s.SchedulerTuesday = false;

        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(23)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Tuesday.AddHours(1)));
    }

    [Fact]
    public void Window_MalformedStartTime_FallsBackTo1600()
    {
        var s = DaySettings("garbage", "23:00");

        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(17)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(15)));
    }

    [Fact]
    public void Window_MalformedEndTime_FallsBackTo2200()
    {
        var s = DaySettings("10:00", "not a time");

        Assert.True(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(21).AddMinutes(59)));
        Assert.False(SchedulerService.IsInScheduledTimeWindow(s, Monday.AddHours(22)));
    }

    #endregion

    #region State flags (auto-start / auto-stop / manual escape)

    private static (SchedulerService Service, FakeSettingsService Settings,
        List<bool> Starts, List<int> Stops) CreateService(string start = "08:00", string end = "20:00")
    {
        var settings = new FakeSettingsService();
        settings.Current.SchedulerEnabled = true;
        settings.Current.SchedulerStartTime = start;
        settings.Current.SchedulerEndTime = end;

        var service = new SchedulerService(settings);
        var starts = new List<bool>();
        var stops = new List<int>();
        service.AutoStartRequested += (_, e) => starts.Add(e.MinimizeToTray);
        service.AutoStopRequested += (_, _) => stops.Add(1);
        return (service, settings, starts, stops);
    }

    private static readonly DateTime InWindow = Monday.AddHours(12);
    private static readonly DateTime OutOfWindow = Monday.AddHours(21);

    [Fact]
    public void Tick_InWindow_AutoStartsOnce()
    {
        var (service, _, starts, _) = CreateService();

        service.EvaluateTick(InWindow, isEngineRunning: false);

        Assert.Single(starts);
        Assert.True(starts[0]); // tick entry minimizes to tray (WPF StartStop.cs:560)
        Assert.True(service.SchedulerAutoStarted);

        // No re-trigger within the same window, running or not.
        service.EvaluateTick(InWindow.AddMinutes(1), isEngineRunning: true);
        service.EvaluateTick(InWindow.AddMinutes(2), isEngineRunning: false);
        Assert.Single(starts);
    }

    [Fact]
    public void Tick_WindowExit_StopsOnlyAutoStartedRuns()
    {
        var (service, _, _, stops) = CreateService();

        // Scheduler started this run: window exit stops it.
        service.EvaluateTick(InWindow, isEngineRunning: false);
        service.EvaluateTick(OutOfWindow, isEngineRunning: true);

        Assert.Single(stops);
        Assert.False(service.SchedulerAutoStarted);
    }

    [Fact]
    public void Tick_WindowExit_NeverStopsManualRuns()
    {
        var (service, _, starts, stops) = CreateService();

        // Manually started run (SchedulerAutoStarted == false) outlives the window.
        service.EvaluateTick(OutOfWindow, isEngineRunning: true);

        Assert.Empty(stops);
        Assert.Empty(starts);
        Assert.False(service.SchedulerAutoStarted);
    }

    [Fact]
    public void ManualStop_InWindow_EscapesForTheRestOfTheWindow()
    {
        var (service, _, starts, _) = CreateService();

        service.NotifyManualStop(InWindow);
        Assert.True(service.ManuallyStoppedDuringSchedule);

        // No auto-restart while the escape flag holds.
        service.EvaluateTick(InWindow.AddMinutes(5), isEngineRunning: false);
        Assert.Empty(starts);
    }

    [Fact]
    public void ManualStop_OutsideWindow_DoesNotSetTheFlag()
    {
        var (service, _, _, _) = CreateService();

        service.NotifyManualStop(OutOfWindow);

        Assert.False(service.ManuallyStoppedDuringSchedule);
    }

    [Fact]
    public void ManualStop_WhenSchedulerDisabled_DoesNotSetTheFlag()
    {
        var (service, settings, _, _) = CreateService();
        settings.Current.SchedulerEnabled = false;

        service.NotifyManualStop(InWindow);

        Assert.False(service.ManuallyStoppedDuringSchedule);
    }

    [Fact]
    public void Flags_ResetAfterLeavingTheWindow()
    {
        var (service, _, starts, _) = CreateService();

        service.NotifyManualStop(InWindow);
        Assert.True(service.ManuallyStoppedDuringSchedule);

        // Outside the window both flags reset (WPF StartStop.cs:579-584)...
        service.EvaluateTick(OutOfWindow, isEngineRunning: false);
        Assert.False(service.ManuallyStoppedDuringSchedule);
        Assert.False(service.SchedulerAutoStarted);

        // ...so the next window entry auto-starts again.
        service.EvaluateTick(Tuesday.AddHours(12), isEngineRunning: false);
        Assert.Single(starts);
    }

    [Fact]
    public void ManualStart_ClearsTheEscapeFlag()
    {
        var (service, _, starts, _) = CreateService();

        service.NotifyManualStop(InWindow);
        service.NotifyManualStart();
        Assert.False(service.ManuallyStoppedDuringSchedule);

        service.EvaluateTick(InWindow.AddMinutes(5), isEngineRunning: false);
        Assert.Single(starts);
    }

    [Fact]
    public void Tick_SchedulerDisabled_DoesNothing()
    {
        var (service, settings, starts, stops) = CreateService();
        settings.Current.SchedulerEnabled = false;

        service.EvaluateTick(InWindow, isEngineRunning: false);
        service.EvaluateTick(OutOfWindow, isEngineRunning: true);

        Assert.Empty(starts);
        Assert.Empty(stops);
    }

    [Fact]
    public void StartupCheck_InWindow_AutoStartsWithMinimize()
    {
        var (service, _, starts, _) = CreateService();

        service.CheckOnStartup(InWindow);

        Assert.Single(starts);
        Assert.True(starts[0]);
        Assert.True(service.SchedulerAutoStarted);
    }

    [Fact]
    public void StartupCheck_OutsideWindowOrDisabled_DoesNothing()
    {
        var (service, settings, starts, _) = CreateService();

        service.CheckOnStartup(OutOfWindow);
        Assert.Empty(starts);

        settings.Current.SchedulerEnabled = false;
        service.CheckOnStartup(InWindow);
        Assert.Empty(starts);
    }

    [Fact]
    public void SettingsChangeCheck_InWindowAndIdle_AutoStartsWithoutMinimize()
    {
        var (service, _, starts, _) = CreateService();

        service.CheckAfterSettingsChange(InWindow, isEngineRunning: false);

        Assert.Single(starts);
        Assert.False(starts[0]); // settings-change check does not minimize (WPF StartStop.cs:528-544)
        Assert.True(service.SchedulerAutoStarted);
    }

    [Fact]
    public void SettingsChangeCheck_WhileRunning_DoesNothing()
    {
        var (service, _, starts, _) = CreateService();

        service.CheckAfterSettingsChange(InWindow, isEngineRunning: true);

        Assert.Empty(starts);
        Assert.False(service.SchedulerAutoStarted);
    }

    #endregion
}
