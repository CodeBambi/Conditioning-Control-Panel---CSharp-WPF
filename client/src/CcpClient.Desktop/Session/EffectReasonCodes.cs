namespace CcpClient.Desktop.Session;

/// <summary>
/// Stable machine-readable reason codes for what a session module says when it is armed
/// (runtime-capability-contract §1: "codes are additive; new codes land with their consumer row").
/// They live beside their consumer, as <see cref="Overlay.OverlayReasonCodes"/> and
/// <see cref="Tray.TrayReasonCodes"/> do.
///
/// <para><b>Why arming has reason codes at all (SP-101, hazard 1 from SP-098's review).</b>
/// <c>Arm()</c> returned <c>void</c>, so "this module took the session and is paced" and "this
/// module did nothing" were the same observation. That is survivable for two modules whose only
/// precondition is a persisted flag. It is not survivable for the modules still to come, whose
/// preconditions are an audio device and a webcam: a module that cannot run must be able to SAY so,
/// in the typed vocabulary the rest of the port already refuses in, or the session will report
/// itself running with a silent hole in it.</para>
/// </summary>
public static class EffectReasonCodes
{
    /// <summary>
    /// The module's own persisted dial is off, so nothing was scheduled. <b>Not a refusal</b> —
    /// it is the user's setting, and WPF's own start body simply does not call a disabled module
    /// (<c>MainWindow/MainWindow.StartStop.cs:186</c>). It is typed because a caller that cannot
    /// tell it from a successful arm cannot report which modules a session actually took.
    /// </summary>
    public const string EffectDialOff = "effect-dial-off";

    /// <summary>
    /// The generation behind the schedule was already cancelled when the arm reached the clock, so
    /// nothing was scheduled. This is the teardown-races-arm window, and it is an outcome rather
    /// than a fault (async-lifecycle-fault-contract §5.5).
    /// </summary>
    public const string EffectGenerationCancelled = "effect-generation-cancelled";

    /// <summary>
    /// Subliminals: the phrase pool has nothing active in it, so the schedule runs and every firing
    /// shows nothing. Upstream's own outcome — <c>FlashSubliminal</c> logs "No active subliminal
    /// texts" and returns before anything is counted or displayed
    /// (<c>Services/Subliminal/SubliminalService.cs:207-212</c>) — and the reason the arm result is
    /// <c>Degraded</c> rather than <c>Available</c> or <c>Unavailable</c>: the paced half really
    /// holds, the visible half really does not.
    /// </summary>
    public const string SubliminalNoActivePhrase = "subliminal-no-active-phrase";
}
