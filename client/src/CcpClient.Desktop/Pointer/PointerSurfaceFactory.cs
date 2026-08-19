namespace CcpClient.Desktop.Pointer;

/// <summary>The host platform a pointer backend is selected for.</summary>
public enum PointerHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the pointer surface for a platform.
///
/// <para><b>Selection is not availability.</b> <c>runtime-capability-contract.md</c> §2 rule 2 bans
/// a platform check from producing <c>Available</c>, and nothing here produces it — only
/// <see cref="Win32PointerSurface.Open"/> and <see cref="Win32PointerSurface.Move"/> do, and only
/// after the operating system reported the target on screen at the exact rectangle, carrying
/// <c>WS_EX_NOACTIVATE</c> and not <c>WS_EX_TRANSPARENT</c>, above every ordinary window, answering
/// its own hit test, with the foreground exactly where it already was.</para>
/// </summary>
public static class PointerSurfaceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux pointer-target claim is made.
    /// A constant so it travels with the refusal, the same precedent as
    /// <c>Overlay.OverlayPresenceFactory.LinuxManualGate</c> and
    /// <c>Input.InputPresenceFactory.LinuxManualGate</c>.
    ///
    /// <para><b>Why this gate is not the overlay's gate with the polarity flipped.</b> The overlay's
    /// Linux route is an EMPTY input shape — the surface asks the X server to route everything past
    /// it, and X11 has a direct mechanism for that (<c>XFixesSetWindowShapeRegion</c> on
    /// <c>ShapeInput</c>). A pointer target needs the opposite AND a second thing X11 does not
    /// express in one call: it must receive the button press <i>without the click raising or
    /// focusing it</i>. On X11 that is the window manager's decision, not the client's: the
    /// click-to-focus policy lives in the WM, and the nearest a client gets is
    /// <c>_NET_WM_WINDOW_TYPE_DOCK</c> plus <c>WM_HINTS.input = False</c> plus an override-redirect
    /// window that the WM ignores entirely — three hints, honoured differently by Mutter, KWin and
    /// wlroots, none of them a guarantee. So the Linux answer is not "the same code with a different
    /// flag"; it is an unproven negotiation with whatever WM is running.</para>
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real X11 desktop session — NOT WSLg — put a pointer target on "
        + "screen and prove it FIVE ways, and then repeat the whole run on a native Wayland session, because they "
        + "answer differently: "
        + "(1) `xwininfo -root -children` and `xprop -root _NET_CLIENT_LIST_STACKING` put the target above every "
        + "ordinary window, and `xprop -id <xid> _NET_WM_STATE` lists _NET_WM_STATE_ABOVE; "
        + "(2) `xprop -id <xid> WM_HINTS` shows input=False AND `xprop -id <xid> _NET_WM_WINDOW_TYPE` is "
        + "_NET_WM_WINDOW_TYPE_DOCK (or the window is override-redirect), which together are X11's nearest "
        + "equivalent of WS_EX_NOACTIVATE — there is no single flag; "
        + "(3) with the pointer over the target, `xdotool getmouselocation --shell` reports WINDOW=<xid>: the "
        + "server routes the point TO it, which is the analogue of WindowFromPoint and the fact that would earn "
        + "Available; "
        + "(4) `xprop -root _NET_ACTIVE_WINDOW` names THE SAME WINDOW BEFORE AND AFTER a click on the target, and "
        + "never names the target — this is the whole capability and it is the step most likely to fail, because "
        + "click-to-focus is the window manager's policy and not the client's; "
        + "(5) a HUMAN clicks the target and reports that the click popped it AND that whatever they were typing "
        + "into kept the keyboard — which no automated step on ANY platform discharges, Windows included. "
        + "This machine cannot discharge it: the port's Linux environment is WSLg, whose XWayland root has no "
        + "_NET_CLIENT_LIST (client/docs/port-lessons.md:52), so window enumeration, stacking and _NET_ACTIVE_WINDOW "
        + "cannot be trusted there at all.";

    /// <summary>
    /// Why a native Wayland session is a refusal and not a harder Linux. Kept beside the gate
    /// because "Linux" is not one target.
    /// </summary>
    public const string WaylandNote =
        "Under a native Wayland compositor a client cannot place a surface at a chosen desktop coordinate, cannot "
        + "guarantee its stacking, and cannot enumerate what is above it — the protocol denies all three to ordinary "
        + "clients on purpose. The non-activation half is the one part Wayland gets RIGHT by default (a client "
        + "cannot take activation at all without an xdg-activation-v1 token minted from real user interaction), "
        + "which is precisely why the rest is unavailable: the compositor owns placement and focus policy together. "
        + "The layer-shell protocol that would give positioning and stacking (wlr-layer-shell) is a wlroots "
        + "extension Mutter does not implement. The practical route on a Wayland session is XWayland — the pinned "
        + "Avalonia 12.1.1 graph ships Avalonia.X11 and Avalonia.FreeDesktop and no Wayland package at all — which "
        + "makes the X11 route above the only one worth implementing, and its guarantees under XWayland are "
        + "themselves compositor-dependent.";

    /// <summary>The backend for the platform this process is running on.</summary>
    public static IPointerSurface Create() => CreateFor(CurrentPlatform());

    /// <summary>
    /// The backend for a named platform. Both branches are reachable from either OS, so the refusal
    /// path is testable on a Windows box and the Windows path is testable nowhere else — the honest
    /// asymmetry, stated rather than hidden (the SP-093 precedent).
    /// </summary>
    public static IPointerSurface CreateFor(PointerHostPlatform platform) => platform switch
    {
        PointerHostPlatform.Windows => new Win32PointerSurface(),

        PointerHostPlatform.Linux => new UnsupportedPointerSurface(
            PointerReasonCodes.PointerMechanismAbsent,
            "no Linux pointer-target backend is implemented or verified in this build, and nothing was attempted. "
            + "The route on an X11 session is an override-redirect (or _NET_WM_WINDOW_TYPE_DOCK) top-level window "
            + "with _NET_WM_STATE_ABOVE and WM_HINTS.input = False, so the server routes button presses to it while "
            + "the window manager's click-to-focus policy leaves the active window alone. " + WaylandNote + " Until "
            + "the gate below runs, a caller must treat its clickable half as absent and say so — never put a "
            + "target on a desktop whose focus behaviour nobody has measured. " + LinuxManualGate),

        PointerHostPlatform.MacOs => new UnsupportedPointerSurface(
            PointerReasonCodes.PointerMechanismAbsent,
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no pointer "
            + "backend exists here and nothing was attempted"),

        _ => new UnsupportedPointerSurface(
            PointerReasonCodes.PointerMechanismAbsent,
            "this platform is not one this build has a pointer backend for, and nothing was attempted"),
    };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static PointerHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return PointerHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return PointerHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return PointerHostPlatform.MacOs;
        }

        return PointerHostPlatform.Unknown;
    }
}
