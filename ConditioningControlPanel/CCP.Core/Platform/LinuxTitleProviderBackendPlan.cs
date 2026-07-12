namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// The foreground-window-title backends that can be selected on Linux in THIS build
/// (slices A+B of docs/linux-foreground-title-contract.md). The native Wayland backends
/// (wlr-foreign-toplevel-management, ext-foreign-toplevel-list, GNOME extension D-Bus) are
/// documented WAVE-3 gaps and are intentionally NOT members here: a Wayland session
/// resolves to <see cref="Fallback"/> (honest "unknown") until they land.
/// </summary>
public enum LinuxTitleBackendKind
{
    /// <summary>
    /// X11 backend: <c>_NET_ACTIVE_WINDOW</c> → <c>_NET_WM_NAME</c>/<c>WM_NAME</c> via
    /// <c>XGetWindowProperty</c>. Full functionality on a native X11 session only.
    /// </summary>
    X11Title,

    /// <summary>
    /// Returns <c>null</c>; the awareness engine runs and classifies activity as Unknown.
    /// </summary>
    Fallback
}

/// <summary>
/// Pure backend-selection logic for the Linux foreground-window-title provider,
/// unit-testable without a display. Mirrors <see cref="LinuxOverlayBackendPlan"/> in shape
/// but inverts its routing invariant (see below).
/// </summary>
/// <remarks>
/// <para><b>Selection order (contract §2.1):</b> Wayland is probed FIRST, then X11. In this
/// wave the Wayland probe always resolves to <see cref="LinuxTitleBackendKind.Fallback"/>
/// (no native Wayland backends present), so the effective routing is: a native X11 session
/// with a reachable X display → <see cref="LinuxTitleBackendKind.X11Title"/>; everything else
/// (Wayland, XWayland, unknown, or headless) → <see cref="LinuxTitleBackendKind.Fallback"/>.</para>
///
/// <para><b>This is the OPPOSITE of the overlay selector.</b> The overlay
/// (<see cref="LinuxOverlayBackendPlan"/>) deliberately routes Wayland sessions to the X11
/// backend because Avalonia 12.0.x is X11-only on Linux — the app's OWN overlay window is
/// always an XWayland/X11 window, so X11 machinery applies to it (overlay contract
/// §7.1-RESOLVED). Foreground-window-title is fundamentally different: it reads the ROOT
/// window's <c>_NET_ACTIVE_WINDOW</c>, which on a Wayland session reflects only XWayland
/// windows. Native Wayland apps (most browsers/editors) are INVISIBLE to X11, so the X11
/// backend would report a stale or missing foreground — misleading for awareness
/// classification. Honest <c>null</c> (Unknown) beats plausible-but-wrong activity (contract
/// §2.1 "Deliberate non-fallback"). Do NOT collapse this routing onto the overlay's.</para>
/// </remarks>
public static class LinuxTitleProviderBackendPlan
{
    /// <summary>
    /// Chooses the foreground-title backend. X11 is selected ONLY for a native X11 session
    /// with a reachable X display; every Wayland/XWayland/unknown session resolves to
    /// <see cref="LinuxTitleBackendKind.Fallback"/> in this wave (Wayland backends are
    /// wave-3). The <paramref name="xDisplayReachable"/> flag is authoritative for the X11
    /// branch — it reflects an actual <c>XOpenDisplay</c> probe, not the session env.
    /// </summary>
    /// <param name="sessionType">Detected session type (drives routing — Wayland → fallback).</param>
    /// <param name="xDisplayReachable">
    /// True when an X display was opened successfully. Only consulted on the X11-session
    /// branch; ignored for Wayland/XWayland (those never route to X11 here).
    /// </param>
    public static LinuxTitleBackendKind Choose(LinuxSessionType sessionType, bool xDisplayReachable)
    {
        // §2.1: Wayland probe FIRST. On a Wayland or XWayland session the native Wayland
        // backends are WAVE-3 and not present in this build, so the probe resolves to
        // Fallback (honest "unknown"). We deliberately do NOT fall back to the X11 backend
        // here (§2.1 "Deliberate non-fallback") — see the class XML doc for the full
        // rationale (X11 sees only XWayland windows on a Wayland session → misleading).
        if (sessionType == LinuxSessionType.X11 && xDisplayReachable)
        {
            return LinuxTitleBackendKind.X11Title;
        }

        return LinuxTitleBackendKind.Fallback;
    }
}
