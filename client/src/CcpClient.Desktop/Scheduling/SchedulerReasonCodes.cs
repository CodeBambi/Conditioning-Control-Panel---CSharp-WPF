namespace CcpClient.Desktop.Scheduling;

/// <summary>
/// Stable machine-readable reason codes for what one scheduler tick did, or refused to do
/// (runtime-capability-contract §1: "codes are additive; new codes land with their consumer row").
/// They live beside their consumer, as <see cref="Session.EffectReasonCodes"/> and
/// <see cref="Tray.TrayReasonCodes"/> do.
///
/// <para><b>Every code below except two is a REFUSAL, and that is the shape of this module.</b>
/// Thirteen ported rows describe what they did; this one mostly describes what it declined to do,
/// because it is the only thing in the port that can start a conditioning session with nobody at
/// the keyboard. A tick whose outcome could not be named would be a start nobody could audit.</para>
/// </summary>
public static class SchedulerReasonCodes
{
    /// <summary>The enable is off, so the tick returned before it even read the clock — WPF's
    /// <c>if (!settings.SchedulerEnabled) return;</c> (<c>MainWindow/MainWindow.StartStop.cs:604</c>).
    /// It is the FIRST clause, which is why a disabled scheduler also clears no flags and stops
    /// nothing.</summary>
    public const string SchedulerDisabled = "scheduler-disabled";

    /// <summary>Everything lined up and the session really started — WPF's <c>:608-620</c>.</summary>
    public const string SchedulerStartedSession = "scheduler-started-session";

    /// <summary>The window closed on a session THIS scheduler started, and it really stopped —
    /// WPF's <c>:622-632</c>.</summary>
    public const string SchedulerStoppedSession = "scheduler-stopped-session";

    /// <summary>Outside the window with nothing of the scheduler's own running, so both flags were
    /// cleared for the next opening — WPF's <c>:635-639</c>.</summary>
    public const string SchedulerClearedFlags = "scheduler-cleared-flags";

    /// <summary>Inside the window, and the tick deliberately did nothing: a session is already
    /// running, or this window opening has already been served, or the user stopped by hand and
    /// has not been overridden. WPF reaches the same state by falling out of all three branches
    /// (<c>:608</c>, <c>:622</c>, <c>:635</c> are all false).</summary>
    public const string SchedulerHeld = "scheduler-held";

    /// <summary>
    /// The start conditions held and the SESSION refused the start, so nothing was marked
    /// auto-started.
    ///
    /// <para><b>Port-only, and it exists because WPF has a hole here.</b> <c>StartEngine()</c>
    /// (<c>MainWindow.StartStop.cs:161</c>) has no <c>_isRunning</c> guard of its own, so WPF's
    /// start-up check (<c>:570-580</c>) can re-arm every service over a session the user started
    /// during the 60 s grace, and then set <c>_schedulerAutoStarted = true</c> — after which the
    /// window closing stops a session the scheduler never started. The port's
    /// <see cref="Session.SessionEngine.Start"/> returns <c>false</c> instead, and this code is
    /// what the scheduler records rather than claiming a start it did not make.</para>
    /// </summary>
    public const string SchedulerStartRefusedBySession = "scheduler-start-refused-by-session";

    /// <summary>The scheduler is not polling: the 60 s start-up grace has not elapsed yet, or the
    /// participant has stopped. WPF's equivalents are the delayed
    /// <c>_schedulerTimer.Start()</c> (<c>MainWindow.xaml.cs:624-635</c>) and
    /// <c>_schedulerTimer?.Stop()</c> at close (<c>MainWindow.WindowChrome.cs:166</c>).</summary>
    public const string SchedulerNotPolling = "scheduler-not-polling";

    /// <summary>
    /// The tray balloons WPF raises on an auto-start and an auto-stop
    /// (<c>MainWindow.StartStop.cs:616</c>, <c>:631</c>) are not sent by this build.
    /// <c>Tray/ShellTray</c> exposes no arbitrary-notification entry and <c>Tray/**</c> is outside
    /// SP-118's File Scope, so the absence is NAMED rather than faked. The minimize half IS
    /// ported: <see cref="Tray.ShellTray.Duck"/> is the port's landed analogue of
    /// <c>MinimizeToTray()</c> (<c>:614</c>).
    /// </summary>
    public const string SchedulerBalloonAbsent = "scheduler-balloon-absent";
}
