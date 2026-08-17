namespace CcpClient.Desktop.Tray;

/// <summary>
/// Stable machine-readable reason codes for the tray capability (runtime-capability-contract
/// §1: "codes are additive; new codes land with their consumer row"). They live beside their
/// consumer rather than in <c>Capabilities/CapabilityReasonCodes.cs</c> for the same reason
/// <see cref="Features.Dtrh.DtrhCapabilityProbes"/> carries its own strings
/// (<c>DtrhCapabilityProbes.cs:41,52,72</c>): a feature-local code needs no edit to a shared file.
///
/// <para>The pair that carries the honesty: <see cref="TrayMechanismAbsent"/> says the PLATFORM
/// has no tray mechanism this build can drive, and <see cref="TrayMechanismRefused"/> says the
/// mechanism exists, was really asked, and said no. Conflating them is how a tray capability
/// lies — "we never tried" and "we tried and were refused" are different facts and a caller
/// picks a different fallback for each.</para>
/// </summary>
public static class TrayReasonCodes
{
    /// <summary>
    /// No tray backend in this build can drive this platform, so nothing was attempted.
    /// Never means "the platform cannot have a tray" — on Linux it emphatically can
    /// (StatusNotifierItem over DBus); it means this build has not implemented and verified
    /// one, and the detail names the route and the manual gate that would prove it.
    /// </summary>
    public const string TrayMechanismAbsent = "tray-mechanism-absent";

    /// <summary>
    /// The platform's tray mechanism exists, was invoked for real, and refused — e.g.
    /// <c>Shell_NotifyIcon</c> returned FALSE because the session has no notification area
    /// (session 0, no shell, a wedged Explorer). The detail carries the failing call and the
    /// Win32 last-error.
    /// </summary>
    public const string TrayMechanismRefused = "tray-mechanism-refused";

    /// <summary>
    /// A prerequisite failed before the tray mechanism could be asked: the hidden owner window
    /// the notification area sends its click callbacks to could not be created. Distinct from
    /// <see cref="TrayMechanismRefused"/> because the shell never got a chance to answer.
    /// </summary>
    public const string TrayOwnerWindowFailed = "tray-owner-window-failed";

    /// <summary>Remove was asked while no icon is placed. Reporting Available here would be a
    /// success claim for work that did not happen.</summary>
    public const string TrayNothingPlaced = "tray-nothing-placed";

    /// <summary>The presence was disposed; its owner window and icon are gone and it will never
    /// place another. Truthful terminal state, never a silent no-op.</summary>
    public const string TrayPresenceDisposed = "tray-presence-disposed";
}
