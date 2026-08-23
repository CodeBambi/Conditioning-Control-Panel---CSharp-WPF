namespace CcpClient.Desktop.Tray;

/// <summary>The host platform a tray backend is selected for.</summary>
public enum TrayHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the tray backend for a platform.
///
/// <para><b>Selection is not availability.</b> Picking a backend by platform is allowed;
/// <c>runtime-capability-contract.md</c> §2 rule 2 bans platform checks from producing
/// <c>Available</c>, and nothing here produces <c>Available</c> — only
/// <see cref="Win32TrayPresence.Place"/> does, and only after the shell confirmed the icon.
/// A platform this build cannot drive gets an <see cref="UnsupportedTrayPresence"/> whose
/// reason names the route that would work and the gate that would prove it.</para>
/// </summary>
public static class TrayPresenceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux tray claim is made. Held as a
    /// constant because it travels with the refusal to whoever hits it, not only in a record file
    /// nobody reads at 2am.
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real Linux desktop session with a status-notifier host "
        + "(GNOME with AppIndicator, KDE Plasma, XFCE) — NOT WSLg — place the icon, then prove it three ways: "
        + "(1) `busctl --user list` shows an org.kde.StatusNotifierWatcher owner; "
        + "(2) `busctl --user get-property org.kde.StatusNotifierWatcher /StatusNotifierWatcher "
        + "org.kde.StatusNotifierWatcher RegisteredStatusNotifierItems` lists THIS process's item, and stops "
        + "listing it after Remove(); "
        + "(3) a human confirms the icon is visible in the panel and that clicking it raises Activated. "
        + "This machine cannot discharge it: the port's WSLg session has no _NET_CLIENT_LIST on the XWayland root "
        + "(client/docs/port-lessons.md:52 @ a8d32c219), so there is no reliable way to prove a panel icon exists from here.";

    /// <summary>The backend for the platform this process is running on.</summary>
    public static ITrayPresence Create() => CreateFor(CurrentPlatform());

    /// <summary>The backend for a named platform. Both branches are reachable from either OS, so
    /// the refusal path is testable on a Windows box and the Windows path is testable nowhere
    /// else — which is the honest asymmetry, not a hidden one.</summary>
    public static ITrayPresence CreateFor(TrayHostPlatform platform) => platform switch
    {
        TrayHostPlatform.Windows => new Win32TrayPresence(),

        // Linux HAS a tray mechanism and Avalonia 12.1.1 even ships backends for it
        // (Avalonia.FreeDesktop.DBusTrayIconImpl + StatusNotifierItemDbusObj for SNI over DBus,
        // Avalonia.X11.XEmbedTrayIconImpl for the older XEmbed systray — both internal, verified
        // in the 12.1.1 assemblies). This build has NOT implemented or verified one, and no
        // Avalonia surface can report whether an icon was really placed, so the honest answer is
        // "nothing was attempted" plus the gate that would settle it.
        TrayHostPlatform.Linux => new UnsupportedTrayPresence(
            TrayReasonCodes.TrayMechanismAbsent,
            "no Linux tray backend is implemented or verified in this build. The route is StatusNotifierItem over "
            + "DBus (org.kde.StatusNotifierWatcher; Avalonia 12.1.1 ships Avalonia.FreeDesktop.DBusTrayIconImpl and "
            + "Avalonia.X11.XEmbedTrayIconImpl internally), and it is seam-heavy: Wayland has no XEmbed systray at "
            + "all, and GNOME needs an AppIndicator extension for SNI to be hosted. Until the gate below runs, a "
            + "caller must fall back to a plain window minimize that keeps the taskbar button — never to hiding the "
            + "window. " + LinuxManualGate),

        TrayHostPlatform.MacOs => new UnsupportedTrayPresence(
            TrayReasonCodes.TrayMechanismAbsent,
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no status-item "
            + "backend exists here and nothing was attempted"),

        _ => new UnsupportedTrayPresence(
            TrayReasonCodes.TrayMechanismAbsent,
            "this platform is not one this build has a tray backend for, and nothing was attempted"),
    };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static TrayHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return TrayHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return TrayHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return TrayHostPlatform.MacOs;
        }

        return TrayHostPlatform.Unknown;
    }
}
