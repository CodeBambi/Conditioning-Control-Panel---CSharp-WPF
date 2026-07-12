using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the frame-source backend-selection invariant
/// (linux-framesource-contract.md §2.1 / §2.2): Wayland-first, with the 3-way X11 priority
/// MIT-SHM &gt; XGetImage-basic &gt; fallback. Only a NATIVE X11 session with a reachable X display
/// selects an X11 capture backend; Wayland/XWayland must NOT (the X11 root holds only XWayland
/// content — a black root — so capture would silently return the wrong image). This is the
/// CAPTURE-side rule and intentionally differs from the overlay selector
/// (<see cref="LinuxOverlayBackendPlanTests"/>), which routes Wayland sessions to X11 because
/// the overlay window is OUR X11 window.
/// </summary>
public class LinuxFrameSourcePlanTests
{
    [Fact]
    public void Choose_NativeX11_ShmAvailable_SelectsX11Shm()
    {
        // SHM probed usable → fast path selected even when basic is also available.
        Assert.Equal(
            LinuxFrameSourceBackendKind.X11Shm,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BasicAvailable: true, x11ShmAvailable: true));
    }

    [Fact]
    public void Choose_NativeX11_ShmAvailable_BasicFalse_StillSelectsX11Shm()
    {
        // SHM implies the X11 path is reachable; the selector never probes basic when SHM is
        // usable, so basic=false here is normal and SHM still wins.
        Assert.Equal(
            LinuxFrameSourceBackendKind.X11Shm,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BasicAvailable: false, x11ShmAvailable: true));
    }

    [Fact]
    public void Choose_NativeX11_BasicOnly_SelectsX11Basic()
    {
        // SHM unavailable (remote display / disabled / probe failed) but basic XGetImage works.
        Assert.Equal(
            LinuxFrameSourceBackendKind.X11Basic,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BasicAvailable: true, x11ShmAvailable: false));
    }

    [Fact]
    public void Choose_NativeX11_NeitherUsable_SelectsFallback()
    {
        // Native X11 session but both X11 probes failed (no libX11/libXext, X closed) → fallback.
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BasicAvailable: false, x11ShmAvailable: false));
    }

    [Theory]
    [InlineData(LinuxSessionType.Wayland)]
    [InlineData(LinuxSessionType.XWayland)]
    public void Choose_WaylandOrXWayland_AlwaysFallback_NeverX11(LinuxSessionType session)
    {
        // Contract §2.1 XWayland note: never fall from a Wayland probe to the X11 backend,
        // regardless of which X11 probe "succeeded" (the X11 root is black on a Wayland
        // desktop — XGetImage/XShmGetImage would return the wrong image).
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: true, x11ShmAvailable: true));
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: true, x11ShmAvailable: false));
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: false, x11ShmAvailable: false));
    }

    [Fact]
    public void Choose_UnknownSession_SelectsFallback()
    {
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.Unknown, x11BasicAvailable: false, x11ShmAvailable: false));
        Assert.Equal(
            LinuxFrameSourceBackendKind.Fallback,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.Unknown, x11BasicAvailable: true, x11ShmAvailable: true));
    }

    [Fact]
    public void Choose_ShmStrictlyPreferredOverBasic()
    {
        // Priority invariant (contract §2.2): when both probes succeed, SHM wins.
        Assert.Equal(
            LinuxFrameSourceBackendKind.X11Shm,
            LinuxFrameSourcePlan.Choose(LinuxSessionType.X11, x11BasicAvailable: true, x11ShmAvailable: true));
    }

    [Fact]
    public void BackendKind_HasExactlyShmBasicFallback()
    {
        // Slice A+B+C scope: X11Shm + X11Basic + Fallback. Slices D-F (Wayland backends)
        // extend this enum when they land — update this test then.
        var names = System.Enum.GetNames(typeof(LinuxFrameSourceBackendKind));
        Assert.Equal(3, names.Length);
        Assert.Contains(nameof(LinuxFrameSourceBackendKind.X11Shm), names);
        Assert.Contains(nameof(LinuxFrameSourceBackendKind.X11Basic), names);
        Assert.Contains(nameof(LinuxFrameSourceBackendKind.Fallback), names);
    }
}
