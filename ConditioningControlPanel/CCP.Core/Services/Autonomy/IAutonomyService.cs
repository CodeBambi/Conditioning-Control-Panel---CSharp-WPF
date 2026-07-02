namespace ConditioningControlPanel.Core.Services.Autonomy;

/// <summary>
/// Cross-platform service that enables autonomous companion behavior.
/// The avatar can trigger effects on her own based on idle time, random intervals,
/// context awareness, and time of day.
/// </summary>
public interface IAutonomyService
{
    bool IsEnabled { get; }
    bool IsActionInProgress { get; }
    bool IsIdleTimerRunning { get; }
    bool IsRandomTimerRunning { get; }

    /// <summary>
    /// Fraction (0..1) of the interval remaining until the next random-action fire — drives the avatar
    /// countdown bar. Null when the random timer isn't running (nothing to count down to).
    /// </summary>
    double? NextRandomFireFraction { get; }

    /// <summary>Raised when autonomous mode is started (true) or stopped (false).</summary>
    event EventHandler<bool>? EnabledChanged;

    /// <summary>Raised when an autonomous action is selected and about to run.</summary>
    event EventHandler<AutonomyActionEventArgs>? ActionTriggered;

    /// <summary>Raised when the companion makes a verbal announcement.</summary>
    event EventHandler<string>? AnnouncementMade;

    /// <summary>
    /// Starts autonomous mode if requirements are met (Patreon access, consent, enabled).
    /// </summary>
    void Start();

    /// <summary>Stops autonomous mode and cancels active pulses.</summary>
    void Stop();

    /// <summary>True when the user has armed a self-initiated mic mode (wake word or push-to-talk).</summary>
    bool UserDrivenVoiceArmed { get; }

    /// <summary>Reconcile the "Hey Bambi" wake-word loop with current settings (consent + engine + toggles).</summary>
    void RefreshVoiceInputModes();

    /// <summary>User-initiated "stop the mic": cut any capture and tear down the wake-word loop.</summary>
    void StopVoiceInput();

    /// <summary>Force-starts autonomous mode in test mode (short intervals), bypassing checks.</summary>
    void ForceStart();

    /// <summary>Manually triggers a single autonomous action for testing.</summary>
    void TestTrigger();

    /// <summary>Cancels any active spiral/pink/bubbles/bouncing-text pulses and restores settings.</summary>
    void CancelActivePulses();

    /// <summary>Returns a diagnostic status string for debugging.</summary>
    string GetDiagnosticStatus();
}
