namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Manual intensity ramp: while the engine runs, gradually multiplies the RampLink*-enabled
/// settings from their baseline up to SchedulerMultiplier over RampDurationMinutes.
/// Port of the WPF ramp timer (MainWindow.StartStop.cs:355-501), started from engine start
/// (StartStop.cs:244-247) for BOTH engine-only and preset runs, stopped (with baseline
/// restore) on engine stop (StartStop.cs:333).
/// Independent of the per-session Lerp ramps in <see cref="SessionService"/>: while a preset
/// session is active the visual writes are suppressed so the two systems never fight
/// (WPF StartStop.cs:432-434).
/// </summary>
public interface IIntensityRampService : IDisposable
{
    /// <summary>True while the ramp timer is running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Raised once when the ramp reaches 100% with EndSessionOnRampComplete enabled and
    /// no preset session active (WPF StartStop.cs:492-500). The head stops the
    /// engine-only run in response.
    /// </summary>
    event EventHandler? RampCompleted;

    /// <summary>
    /// Snapshot baselines and start the 2s ramp timer. Idempotent: a second call while
    /// running (e.g. a preset session joining a live engine-only run) keeps the original
    /// baselines, matching WPF where StartEngine is not re-entered for a joining preset
    /// (MainWindow.Presets.cs:1136-1139).
    /// </summary>
    void StartRamp();

    /// <summary>Stop the ramp timer and restore the baselines it mutated.</summary>
    void StopRamp();
}
