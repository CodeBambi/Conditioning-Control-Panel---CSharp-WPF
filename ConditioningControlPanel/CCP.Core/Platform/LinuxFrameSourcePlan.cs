namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// The frame-source backends selectable on Linux (linux-framesource-contract.md §2.2).
/// Slices A+B implement X11 (XGetImage) + Fallback; the MIT-SHM fast path (Slice C / wave 2)
/// and the native Wayland capture backends (slices D-F) extend this enum as they land.
/// </summary>
public enum LinuxFrameSourceBackendKind
{
    /// <summary>X11 backend: XGetImage (Slice B) / MIT-SHM (Slice C, wave 2).</summary>
    X11,

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
/// session with a successfully probed X display selects the X11 backend; Wayland, XWayland
/// and Unknown sessions select the fallback until the native Wayland capture backends
/// (slices D-F) are implemented.</para>
/// <para>This is the CAPTURE-side selection and intentionally differs from the overlay
/// selector (<see cref="LinuxOverlayBackendPlan"/>): the overlay window is OUR X11 window
/// (Avalonia 12.0.x is X11-only on Linux, so it always exists under XWayland), but screen
/// CAPTURE of a Wayland desktop via the X11 root returns black — two different problems with
/// two different routing rules (linux-framesource-contract.md §2.1 XWayland note).</para>
/// </remarks>
public static class LinuxFrameSourcePlan
{
    /// <summary>
    /// Chooses the frame-source backend. Native X11 session with a reachable X display and
    /// a successful backend probe → X11; everything else → fallback.
    /// </summary>
    /// <param name="sessionType">Detected session type (the Wayland-first routing input).</param>
    /// <param name="x11BackendAvailable">
    /// True when the X11 backend successfully opened a display and is ready to capture.
    /// Only meaningful for <see cref="LinuxSessionType.X11"/>.
    /// </param>
    public static LinuxFrameSourceBackendKind Choose(LinuxSessionType sessionType, bool x11BackendAvailable)
    {
        if (sessionType == LinuxSessionType.X11 && x11BackendAvailable)
        {
            return LinuxFrameSourceBackendKind.X11;
        }

        return LinuxFrameSourceBackendKind.Fallback;
    }
}
