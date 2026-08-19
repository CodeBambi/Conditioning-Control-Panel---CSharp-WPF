namespace CcpClient.Desktop.Input;

/// <summary>The host platform an input backend is selected for.</summary>
public enum InputHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the input presence for a platform.
///
/// <para><b>Selection is not availability.</b> <c>runtime-capability-contract.md</c> §2 rule 2 bans
/// a platform check from producing <c>Available</c>, and nothing here produces it — only
/// <see cref="Win32InputPresence.Prompt"/> does, and only after the operating system reported the
/// card as the foreground window, as the foreground thread's keyboard focus, and as the answer to a
/// hit test at a point inside it. A platform this build cannot PROVE gets an
/// <see cref="UnsupportedInputPresence"/> whose reason names the route and the gate that would
/// settle it.</para>
/// </summary>
public static class InputPresenceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux input-capture claim is made.
    /// A constant, so it travels with the refusal to whoever hits it rather than living only in a
    /// record file nobody reads at 2am — the same reason
    /// <c>Tray.TrayPresenceFactory.LinuxManualGate</c>, <c>Overlay</c>'s and
    /// <c>Audio.AudioPresenceFactory.LinuxManualGate</c> are constants.
    ///
    /// <para><b>And the reason this gate is HARDER than the audio one, stated rather than
    /// implied.</b> On Wayland there is no portable "make my window the foreground and give it the
    /// keyboard" call at all: activation is the compositor's to grant, and a client may only ASK
    /// through <c>xdg-activation-v1</c> with a token it was given by a real user interaction. A
    /// Wayland client that could take focus whenever it liked would be a keylogger, so the protocol
    /// declines to offer it. This is not a missing implementation — it is a different answer to the
    /// same question, and until a human runs the gate below nobody knows which of the five steps a
    /// given desktop will refuse.</para>
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real Linux desktop session — NOT WSLg — start a session "
        + "with Lock Card enabled and prove the capture five ways, on BOTH an X11 and a Wayland session, "
        + "because they answer differently: "
        + "(1) the card appears above every ordinary window (X11: `xprop -root _NET_CLIENT_LIST_STACKING`; "
        + "Wayland: the compositor's own layer/stacking dump, e.g. `swaymsg -t get_tree`); "
        + "(2) the OS names it the ACTIVE window (X11: `xprop -root _NET_ACTIVE_WINDOW` resolves to this "
        + "window's id; Wayland: `swaymsg -t get_tree` reports focused=true on this surface) — this is the "
        + "analogue of GetForegroundWindow and it is the fact that would earn Available; "
        + "(3) the keyboard FOCUS is this surface, not merely the active toplevel (X11: `xdotool getwindowfocus`; "
        + "Wayland: wl_keyboard.enter was delivered to this surface, which needs a protocol-level trace such as "
        + "`WAYLAND_DEBUG=1`) — the analogue of GetGUIThreadInfo(0).hwndFocus, and the read this port had to "
        + "correct on Windows because the thread-local one answers yes for a window nobody can type into; "
        + "(4) a pointer hit test at a point inside the card resolves to the card (X11: `xdotool getmouselocation "
        + "--shell` while the pointer is over it); "
        + "(5) a keystroke typed by a HUMAN reaches it and completes a repeat — which no automated step on ANY "
        + "platform discharges, Windows included. "
        + "WAYLAND WILL PROBABLY FAIL STEP 2 BY DESIGN: there is no protocol by which a client takes activation "
        + "unprompted; xdg-activation-v1 requires a token minted from a real user interaction, and a compositor "
        + "that granted it on request would be granting a focus-stealing primitive. If the gate confirms that, "
        + "the honest outcome for Wayland is a NAMED refusal — a card that cannot take the keyboard is a question "
        + "nobody is being asked — and never a window shown anyway with a success reported.";

    /// <summary>Builds the presence for the platform this process is on.</summary>
    public static IInputPresence Create() => CreateFor(CurrentPlatform());

    /// <summary>
    /// The presence for a named platform. Both branches are reachable from either OS, so the
    /// refusal path is testable on a Windows box and the Windows path is testable nowhere else — the
    /// honest asymmetry, stated rather than hidden (the <c>TrayPresenceFactory</c> precedent).
    /// </summary>
    public static IInputPresence CreateFor(InputHostPlatform platform) =>
        platform switch
        {
            InputHostPlatform.Windows => new Win32InputPresence(),

            // Linux CAN show a window and CAN receive keystrokes; that is not what is missing. What
            // is missing is the ability to EARN the claim: Available here means the operating system
            // reported this window as the foreground and as the keyboard focus, and on Linux that
            // proof is display-server-specific and unimplemented — EWMH _NET_ACTIVE_WINDOW plus an
            // input-focus query on X11, and on Wayland a protocol that deliberately does not offer
            // unprompted activation at all. Showing a card anyway and reporting success would be
            // exactly the stub this capability exists to make impossible, and it is the worst stub
            // available: a window the user cannot answer and nothing can dismiss.
            InputHostPlatform.Linux => new UnsupportedInputPresence(
                InputReasonCodes.InputMechanismAbsent,
                "no Linux input-capture read-back is implemented or verified in this build, so no capture claim "
                + "can be earned here and no card is shown. What is absent is the PROOF, not the window: on "
                + "Windows the proof is GetForegroundWindow plus GetGUIThreadInfo(0).hwndFocus plus a "
                + "WindowFromPoint hit test, and the Linux analogues are per-display-server (EWMH "
                + "_NET_ACTIVE_WINDOW and the input focus on X11; wl_keyboard.enter under Wayland, where "
                + "unprompted activation is not offered by the protocol at all). Until the gate below runs, a "
                + "caller must not ask the user anything and must report its module as NOT running — never show "
                + "a card anyway and call that success. " + LinuxManualGate),

            InputHostPlatform.MacOs => new UnsupportedInputPresence(
                InputReasonCodes.InputMechanismAbsent,
                "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no AppKit "
                + "capture or read-back exists here and nothing was attempted"),

            _ => new UnsupportedInputPresence(
                InputReasonCodes.InputMechanismAbsent,
                "this platform is not one this build has an input-capture read-back for, and nothing was attempted"),
        };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static InputHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return InputHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return InputHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return InputHostPlatform.MacOs;
        }

        return InputHostPlatform.Unknown;
    }
}
