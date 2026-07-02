namespace ConditioningControlPanel.Core.Services.Scheduler;

/// <summary>
/// Time-window scheduler that auto-starts/auto-stops the engine based on the
/// per-day Scheduler* settings. Port of the WPF scheduler engine
/// (MainWindow.xaml.cs:443-465 timer setup, MainWindow.StartStop.cs:507-643 logic).
/// The service only decides; the UI head subscribes to the events and performs the
/// actual engine start/stop and minimize-to-tray.
/// </summary>
public interface ISchedulerService : IDisposable
{
    /// <summary>
    /// True while the current (or most recent) run inside the window was started by
    /// the scheduler. Blocks re-triggering within the same window and gates auto-stop
    /// so manually started runs are never killed (WPF MainWindow.StartStop.cs:553, 567).
    /// </summary>
    bool SchedulerAutoStarted { get; }

    /// <summary>
    /// True after the user manually stopped a run while inside the scheduled window.
    /// One manual stop escapes the window; the flag resets when the window is left
    /// (WPF MainWindow.StartStop.cs:92-97, 582-583).
    /// </summary>
    bool ManuallyStoppedDuringSchedule { get; }

    /// <summary>
    /// Raised when the scheduler wants the engine started (engine-only run).
    /// <see cref="SchedulerAutoStartEventArgs.MinimizeToTray"/> mirrors WPF: true for
    /// the startup check and window-entry tick, false for the settings-change check.
    /// </summary>
    event EventHandler<SchedulerAutoStartEventArgs>? AutoStartRequested;

    /// <summary>
    /// Raised on window exit for runs the scheduler itself started.
    /// </summary>
    event EventHandler? AutoStopRequested;

    /// <summary>
    /// Arms the scheduler: 60s startup grace period, then the startup check and a 30s
    /// check timer (WPF MainWindow.xaml.cs:443-465). Harness runs must never call this.
    /// </summary>
    /// <param name="isEngineRunning">
    /// Probe for the head's engine state; must cover both engine-only and preset runs.
    /// </param>
    void Start(Func<bool> isEngineRunning);

    /// <summary>
    /// Call when the user manually stops a run via the START/STOP button. Sets the
    /// escape flag only while the scheduler is enabled and inside the window
    /// (WPF MainWindow.StartStop.cs:92-97).
    /// </summary>
    void NotifyManualStop();

    /// <summary>
    /// Call when the user manually starts a run via the START/STOP button. Clears the
    /// escape flag (WPF MainWindow.StartStop.cs:101-103). WPF does NOT clear it from
    /// the start-menu shortcuts (MenuStartNormal/Jump right in), so neither do we.
    /// </summary>
    void NotifyManualStart();
}

/// <summary>
/// Payload for <see cref="ISchedulerService.AutoStartRequested"/>.
/// </summary>
public sealed class SchedulerAutoStartEventArgs : EventArgs
{
    public SchedulerAutoStartEventArgs(bool minimizeToTray)
    {
        MinimizeToTray = minimizeToTray;
    }

    /// <summary>
    /// True when the head should also minimize the dashboard to the tray
    /// (WPF MainWindow.StartStop.cs:520, 560; the settings-change check does not
    /// minimize, StartStop.cs:528-544).
    /// </summary>
    public bool MinimizeToTray { get; }
}
