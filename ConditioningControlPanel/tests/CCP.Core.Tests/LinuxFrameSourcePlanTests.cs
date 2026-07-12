using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the frame-source backend-selection invariant
/// (linux-framesource-contract.md §2.1 / §2.2): Wayland-first. Only a NATIVE X11 session
/// with a reachable X display selects the X11 capture backend; Wayland/XWayland must NOT
/// (the X11 root holds only XWayland content — a black root — so XGetImage would silently
/// return the wrong image). Unknown sessions and failed X probes fall back. This is the
/// CAPTURE-side rule and intentionally differs from the overlay selector
/// (<see cref="LinuxOverlayBackendPlanTests"/>), which routes Wayland sessions to X11 because
/// the overlay window is OUR X11 window.
/// </summary>
public class LinuxFrameSourcePlanTests
{
    [Fact]
    public void Choose_NativeX11_WithDisplay_SelectsX11()
    {
        Assert.Equal(
            LinuxFrameSourceBackendKind.X11,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BackendAvailable: true));
    }

    [Fact]
    public void Choose_NativeX11_NoDisplay_SelectsFallback()
    {
        // Native X11 session but the X backend probe failed (no libX11, X closed) → fallback.
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BackendAvailable: false));
    }

    [Theory]
    [InlineData(LinuxSessionType.Wayland)]
    [InlineData(LinuxSessionType.XWayland)]
    public void Choose_WaylandOrXWayland_AlwaysFallback_NeverX11(LinuxSessionType session)
    {
        // Contract §2.1 XWayland note: never fall from a Wayland probe to the X11 backend.
        // Even if an X display is technically reachable (XWayland sets DISPLAY), the X11 root
        // is black on a Wayland desktop — XGetImage would return the wrong image.
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: true));
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: false));
    }

    [Fact]
    public void Choose_UnknownSession_SelectsFallback()
    {
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.Unknown, x11BackendAvailable: false));
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.Unknown, x11BackendAvailable: true));
    }

    [Fact]
    public void BackendKind_HasX11AndFallbackOnly()
    {
        // Slice A+B scope: only X11 + Fallback members. Wave 2 (MIT-SHM) and slices D-F
        // (Wayland backends) extend this enum — update this test when they land.
        var names = System.Enum.GetNames(typeof(LinuxFrameSourceBackendKind));
        Assert.Equal(2, names.Length);
        Assert.Contains(nameof(LinuxFrameSourceBackendKind.X11), names);
        Assert.Contains(nameof(LinuxFrameSourceBackendKind.Fallback), names);
    }
}
