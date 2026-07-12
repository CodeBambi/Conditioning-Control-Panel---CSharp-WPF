using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the hardened backend-selection invariant (linux-overlay-contract.md §7.1-RESOLVED):
/// Avalonia 12.0.x is X11-only on Linux, so the ONLY routing input is whether an X display
/// is reachable (native X11 or XWayland). The session type never routes — in particular a
/// Wayland session must still select the X11 backend when X is reachable, and no session
/// type may ever select a Wayland backend (there is none; that is the point).
/// </summary>
public class LinuxOverlayBackendPlanTests
{
    [Theory]
    [InlineData(LinuxSessionType.X11)]
    [InlineData(LinuxSessionType.Wayland)]
    [InlineData(LinuxSessionType.XWayland)]
    [InlineData(LinuxSessionType.Unknown)]
    public void Choose_XDisplayReachable_AlwaysSelectsX11_RegardlessOfSession(LinuxSessionType session)
    {
        var choice = LinuxOverlayBackendPlan.Choose(session, xDisplayReachable: true);

        Assert.Equal(LinuxOverlayBackendKind.X11InputShape, choice);
    }

    [Theory]
    [InlineData(LinuxSessionType.X11)]
    [InlineData(LinuxSessionType.Wayland)]
    [InlineData(LinuxSessionType.XWayland)]
    [InlineData(LinuxSessionType.Unknown)]
    public void Choose_NoXDisplay_AlwaysSelectsFallback_RegardlessOfSession(LinuxSessionType session)
    {
        var choice = LinuxOverlayBackendPlan.Choose(session, xDisplayReachable: false);

        Assert.Equal(LinuxOverlayBackendKind.Fallback, choice);
    }

    [Fact]
    public void Choose_WaylandSessionWithXWayland_RoutesToX11NotWayland()
    {
        // The regression this pins: the draft selector routed Wayland sessions to a
        // Wayland backend that can never obtain a valid surface (Avalonia creates an
        // XWayland/X11 window, TryGetPlatformHandle always returns an XID).
        var choice = LinuxOverlayBackendPlan.Choose(LinuxSessionType.Wayland, xDisplayReachable: true);

        Assert.Equal(LinuxOverlayBackendKind.X11InputShape, choice);
    }

    [Fact]
    public void BackendKind_HasNoWaylandMember()
    {
        // Structural guard: adding a Wayland member back requires deleting this test and
        // confronting §7.1-RESOLVED (dead code while Avalonia stays X11-only).
        var names = System.Enum.GetNames(typeof(LinuxOverlayBackendKind));

        Assert.Equal(2, names.Length);
        Assert.DoesNotContain(names, n => n.Contains("Wayland", System.StringComparison.OrdinalIgnoreCase));
    }
}
