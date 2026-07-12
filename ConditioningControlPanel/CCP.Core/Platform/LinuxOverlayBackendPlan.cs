namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// The overlay backends that can be selected on Linux.
/// Per docs/linux-overlay-contract.md §7.1-RESOLVED (web-verified 2026-07-12): Avalonia
/// 12.0.x is X11-only on Linux — on Wayland sessions the app runs under XWayland, so the
/// overlay window is still an X11 window and <see cref="X11InputShape"/> applies. There is
/// deliberately no Wayland member: a native Wayland backend can never obtain a valid
/// surface while Avalonia stays X11-only.
/// </summary>
public enum LinuxOverlayBackendKind
{
    /// <summary>X11 backend: XFixes input shape (per-region click-through) + EWMH topmost.</summary>
    X11InputShape,

    /// <summary>No-X fallback: plain Avalonia window, no click-through (never-trap rule applies).</summary>
    Fallback
}

/// <summary>
/// Pure backend-selection logic for the Linux overlay, unit-testable without a display.
/// </summary>
public static class LinuxOverlayBackendPlan
{
    /// <summary>
    /// Chooses the overlay backend. The ONLY input that matters is whether an X display is
    /// reachable (native X11 or XWayland): reachable → X11 input-shape backend; otherwise →
    /// fallback. The session type is accepted for logging/diagnostic symmetry but is
    /// intentionally ignored — routing by session type is exactly the bug class the hardened
    /// contract removed (a Wayland session still gets the X11 backend via XWayland,
    /// contract §7.1-RESOLVED; unit tests pin this invariant).
    /// </summary>
    /// <param name="sessionType">Detected session type (diagnostic only; never routes).</param>
    /// <param name="xDisplayReachable">
    /// True when an X display was opened AND the required extensions (XFixes ≥ 2) probed OK.
    /// </param>
    public static LinuxOverlayBackendKind Choose(LinuxSessionType sessionType, bool xDisplayReachable)
        => xDisplayReachable ? LinuxOverlayBackendKind.X11InputShape : LinuxOverlayBackendKind.Fallback;
}
