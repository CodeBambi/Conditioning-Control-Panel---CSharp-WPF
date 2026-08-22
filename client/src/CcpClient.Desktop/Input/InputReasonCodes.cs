namespace CcpClient.Desktop.Input;

/// <summary>
/// Stable machine-readable reasons an input-capturing window refused
/// (<c>runtime-capability-contract.md</c> §1). Additive; every code lands with the consumer that
/// reads it.
///
/// <para><b>Each of these names a DIFFERENT link of the capture chain</b>, and that is the point of
/// having six of them rather than one "input-unavailable": a card that is on screen and buried, a
/// card that is on screen and un-focused, and a card that never came up at all are three different
/// desktops, and a caller (and a bug report) must be able to tell them apart.</para>
/// </summary>
public static class InputReasonCodes
{
    /// <summary>No mechanism on this platform: nothing was attempted. The Linux/macOS branch, and
    /// the shape <c>ISecretStore</c>/<c>ITrayPresence</c>/<c>IOverlayPresence</c>/
    /// <c>IAudioPresence</c> all use for the same situation.</summary>
    public const string InputMechanismAbsent = "input-mechanism-absent";

    /// <summary>The window could not be created at all (class registration or
    /// <c>CreateWindowEx</c> failed).</summary>
    public const string InputWindowCreationFailed = "input-window-creation-failed";

    /// <summary>
    /// This process has no interactive window station or no display, so no window it creates can be
    /// put in front of anybody — a service in session 0, a non-interactive station, a session with
    /// no monitor. Read from the OS (<c>GetProcessWindowStation</c> +
    /// <c>GetUserObjectInformation(UOI_FLAGS)</c> and the OS's own monitor count), never inferred
    /// from a platform check.
    /// </summary>
    public const string InputNoInteractiveStation = "input-no-interactive-station";

    /// <summary>The OS placed the window but does not report it visible, at the requested rectangle,
    /// or above every ordinary window. A prompt nobody can see is not a prompt.</summary>
    public const string InputPromptNotOnScreen = "input-prompt-not-on-screen";

    /// <summary>
    /// <b>The refusal this capability exists for.</b> The window is on screen and the operating
    /// system did NOT give it the input: it is not the foreground window, or the foreground thread's
    /// keyboard focus is some other window, or the window manager routes a point inside it
    /// elsewhere. The foreground is lent by the OS and can be refused — measured: a plain
    /// <c>SetForegroundWindow</c> from a process that does not already own the foreground returns
    /// FALSE and no keystroke arrives (the packet plan.md §0, run 1).
    /// </summary>
    public const string InputNotCaptured = "input-not-captured";

    /// <summary>The OS holds no ink for the prompt's own client area, so the window is up, focused
    /// and blank — a question the user cannot read is one they cannot answer.</summary>
    public const string InputPromptNotInked = "input-prompt-not-inked";

    /// <summary>Asked to update or dismiss a presence that has nothing on screen.</summary>
    public const string InputNothingPrompted = "input-nothing-prompted";

    /// <summary>The presence was disposed; its window is gone and it will never prompt again.</summary>
    public const string InputPresenceDisposed = "input-presence-disposed";
}
