namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// The frame-source backends selectable on Linux, in priority order
/// (linux-framesource-contract.md §2.2). Slices A-C implement the X11 paths + Fallback; the
/// native Wayland capture backends (slices D-F) extend this enum as they land.
/// </summary>
public enum LinuxFrameSourceBackendKind
{
    /// <summary>
    /// X11 MIT-SHM shared-memory fast path (Slice C). Preferred on a native X11 session when
    /// the SHM attach probe succeeds — avoids the per-pixel socket copy of the basic path.
    /// </summary>
    X11Shm,

    /// <summary>
    /// X11 basic path: <c>XGetImage</c> over the wire (Slice B). Universal but slower; used
    /// when MIT-SHM is unavailable (remote display, SHM disabled, probe failed).
    /// </summary>
    X11Basic,

    /// <summary>Black-frame fallback: no usable capture backend (contract §5).</summary>
    Fallback
}

/// <summary>
/// Pure frame-source backend-selection logic, unit-testable without a display. Mirrors the
/// overlay selector's split into <see cref="LinuxOverlayBackendPlan"/>.
/// </summary>
/// <remarks>
/// <para><b>Wayland-first selection (contract §2.1):</b> on a Wayland or XWayland session
/// the X11 root window holds only XWayland content (typically a black root), so an X11
/// capture backend would silently return the wrong image. Therefore only a NATIVE X11
/// session with a successfully probed backend selects an X11 backend; Wayland, XWayland
/// and Unknown sessions select the fallback until the native Wayland capture backends
/// (slices D-F) are implemented.</para>
/// <para><b>Priority within X11 (contract §2.2):</b> MIT-SHM (fast) &gt; XGetImage (basic) &gt;
/// fallback. SHM is preferred whenever its attach probe succeeded; otherwise the basic path;
/// otherwise the black-frame fallback. The selector performs the native probes and feeds the
/// booleans; this method is the pure, unit-tested decision.</para>
/// <para>This is the CAPTURE-side selection and intentionally differs from the overlay
/// selector (<see cref="LinuxOverlayBackendPlan"/>): the overlay window is OUR X11 window
/// (Avalonia 12.0.x is X11-only on Linux, so it always exists under XWayland), but screen
/// CAPTURE of a Wayland desktop via the X11 root returns black — two different problems with
/// two different routing rules (linux-framesource-contract.md §2.1 XWayland note).</para>
/// </remarks>
public static class LinuxFrameSourcePlan
{
    /// <summary>
    /// Chooses the frame-source backend. Priority: MIT-SHM (fast) &gt; XGetImage (basic) &gt;
    /// fallback, and ONLY for a native X11 session. Every non-X11 session is the fallback
    /// regardless of probe outcome (contract §2.1 XWayland note).
    /// </summary>
    /// <param name="sessionType">Detected session type (the Wayland-first routing input).</param>
    /// <param name="x11BasicAvailable">
    /// True when the basic X11 (<c>XGetImage</c>) backend successfully opened a display and is
    /// ready to capture. Only meaningful for <see cref="LinuxSessionType.X11"/>.
    /// </param>
    /// <param name="x11ShmAvailable">
    /// True when the MIT-SHM fast path probed usable (extension present, display local, attach
    /// round-trip succeeded). Implies the X11 path is reachable; when true this is selected
    /// over <paramref name="x11BasicAvailable"/>.
    /// </param>
    public static LinuxFrameSourceBackendKind Choose(
        LinuxSessionType sessionType, bool x11BasicAvailable, bool x11ShmAvailable)
    {
        // Contract §2.1: never route a Wayland/XWayland/Unknown session to an X11 backend —
        // the X11 root holds only XWayland content there (a black root).
        if (sessionType != LinuxSessionType.X11)
        {
            return LinuxFrameSourceBackendKind.Fallback;
        }

        // Contract §2.2: SHM preferred where usable, else the universal basic path, else
        // black-frame fallback.
        if (x11ShmAvailable)
        {
            return LinuxFrameSourceBackendKind.X11Shm;
        }

        if (x11BasicAvailable)
        {
            return LinuxFrameSourceBackendKind.X11Basic;
        }

        return LinuxFrameSourceBackendKind.Fallback;
    }
}
