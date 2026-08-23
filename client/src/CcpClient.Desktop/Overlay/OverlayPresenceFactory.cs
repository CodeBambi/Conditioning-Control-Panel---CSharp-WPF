namespace CcpClient.Desktop.Overlay;

/// <summary>The host platform an overlay backend is selected for.</summary>
public enum OverlayHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the overlay backend for a platform.
///
/// <para><b>Selection is not availability.</b> Picking a backend by platform is allowed;
/// <c>runtime-capability-contract.md</c> §2 rule 2 bans platform checks from producing
/// <c>Available</c>, and nothing here produces it — only <see cref="Win32OverlayPresence.Present"/>
/// does, and only after the operating system confirmed the surface. The first attempt broke that
/// rule in one line: <c>CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs:29-30</c> is
/// <c>SupportsOverlays = IsDesktop;</c>, a platform check promoted straight to a capability
/// claim.</para>
/// </summary>
public static class OverlayPresenceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux overlay claim is made. Held as
    /// a constant because it travels with the refusal to whoever hits it, not only in a record file
    /// nobody reads at 2am (the <see cref="Tray.TrayPresenceFactory.LinuxManualGate"/> precedent).
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real X11 desktop session — NOT WSLg — present the surface and "
        + "prove it four ways: "
        + "(1) `xprop -id <xid> _NET_WM_STATE` lists _NET_WM_STATE_ABOVE, and `xwininfo -root -children` puts the "
        + "surface above the window it is meant to cover; "
        + "(2) `xprop -id <xid>` shows a bounding input shape of zero extent (XFixesSetWindowShapeRegion with an "
        + "empty ShapeInput region), which is X11's click-through mechanism; "
        + "(3) a human clicks inside the surface and the click lands on the application underneath, and reports "
        + "that the surface is VISIBLE where it is drawn; "
        + "(4) the same run repeated under a native Wayland session, where the expected outcome is a REFUSAL, not "
        + "a surface. "
        + "This machine cannot discharge it: the port's Linux environment is WSLg, whose XWayland root has no "
        + "_NET_CLIENT_LIST (client/docs/port-lessons.md:52 @ a8d32c219), so window enumeration and z-order cannot be trusted "
        + "there at all.";

    /// <summary>
    /// Why Wayland is a refusal and not a harder Linux. Kept beside the gate because "Linux" is not
    /// one target: an X11 session has a mechanism this build has not implemented, and a native
    /// Wayland session has no mechanism an ordinary client may use.
    /// </summary>
    public const string WaylandNote =
        "Under a native Wayland compositor there is no protocol an ordinary application can use to guarantee an "
        + "always-on-top click-through surface: the layer-shell protocol that would do it (wlr-layer-shell) is a "
        + "wlroots extension that Mutter does not implement, and Wayland deliberately denies clients global "
        + "positioning and stacking. Claiming an overlay there because the code compiles is the banned shape. The "
        + "practical route on a Wayland session is XWayland — the pinned Avalonia 12.1.1 graph ships Avalonia.X11 "
        + "and Avalonia.FreeDesktop and no Wayland package at all — which makes the X11 route above the only one "
        + "worth implementing, and its guarantees under XWayland are themselves compositor-dependent.";

    /// <summary>The backend for the platform this process is running on.</summary>
    public static IOverlayPresence Create() => CreateFor(CurrentPlatform());

    /// <summary>
    /// The backend for a named platform. Both branches are reachable from either OS, so the refusal
    /// path is testable on a Windows box and the Windows path is testable nowhere else — which is
    /// the honest asymmetry, not a hidden one (an established precedent).
    /// </summary>
    public static IOverlayPresence CreateFor(OverlayHostPlatform platform) => platform switch
    {
        OverlayHostPlatform.Windows => new Win32OverlayPresence(),

        OverlayHostPlatform.Linux => new UnsupportedOverlayPresence(
            OverlayReasonCodes.OverlayMechanismAbsent,
            "no Linux overlay backend is implemented or verified in this build, and nothing was attempted. The "
            + "route on an X11 session is an override-redirect top-level window with _NET_WM_STATE_ABOVE and an "
            + "empty XFixes ShapeInput region for click-through. " + WaylandNote + " Until the gate below runs, a "
            + "caller must treat the visual half of its effect as absent and say so — never draw into a surface "
            + "nobody can confirm. " + LinuxManualGate),

        OverlayHostPlatform.MacOs => new UnsupportedOverlayPresence(
            OverlayReasonCodes.OverlayMechanismAbsent,
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no overlay "
            + "backend exists here and nothing was attempted"),

        _ => new UnsupportedOverlayPresence(
            OverlayReasonCodes.OverlayMechanismAbsent,
            "this platform is not one this build has an overlay backend for, and nothing was attempted"),
    };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static OverlayHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return OverlayHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return OverlayHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OverlayHostPlatform.MacOs;
        }

        return OverlayHostPlatform.Unknown;
    }
}
