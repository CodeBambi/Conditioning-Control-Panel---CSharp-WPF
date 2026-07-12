using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the foreground-window-title backend-selection contract
/// (docs/linux-foreground-title-contract.md §2.1) for slices A+B. The invariant is the
/// OPPOSITE of the overlay selector (<see cref="LinuxOverlayBackendPlanTests"/>): for
/// foreground-title the X11 backend is selected ONLY for a native X11 session with a
/// reachable X display. Every Wayland/XWayland/unknown session resolves to Fallback in
/// this wave, because the native Wayland backends (wlr-foreign-toplevel-management etc.)
/// are documented WAVE-3 gaps and an X11 connection on a Wayland session sees only
/// XWayland windows (native Wayland apps are invisible) — honest Unknown beats
/// misleading-but-wrong titles (§2.1 "Deliberate non-fallback").
/// </summary>
public class LinuxTitleProviderBackendPlanTests
{
    [Fact]
    public void Choose_X11Session_WithXDisplay_ReturnsX11Title()
    {
        var choice = LinuxTitleProviderBackendPlan.Choose(LinuxSessionType.X11, xDisplayReachable: true);
        Assert.Equal(LinuxTitleBackendKind.X11Title, choice);
    }

    [Fact]
    public void Choose_X11Session_WithoutXDisplay_ReturnsFallback()
    {
        var choice = LinuxTitleProviderBackendPlan.Choose(LinuxSessionType.X11, xDisplayReachable: false);
        Assert.Equal(LinuxTitleBackendKind.Fallback, choice);
    }

    [Theory]
    [InlineData(LinuxSessionType.Wayland, true)]
    [InlineData(LinuxSessionType.Wayland, false)]
    [InlineData(LinuxSessionType.XWayland, true)]
    [InlineData(LinuxSessionType.XWayland, false)]
    [InlineData(LinuxSessionType.Unknown, true)]
    [InlineData(LinuxSessionType.Unknown, false)]
    public void Choose_NonX11Session_AlwaysReturnsFallback_EvenWhenXReachable(
        LinuxSessionType session, bool xDisplayReachable)
    {
        // The XWayland regression this pins: even with a reachable X display (XWayland),
        // a Wayland/XWayland session must NOT route to the X11 backend — that is the
        // overlay's rule, deliberately NOT applied here (see plan XML doc).
        var choice = LinuxTitleProviderBackendPlan.Choose(session, xDisplayReachable);

        Assert.Equal(LinuxTitleBackendKind.Fallback, choice);
    }

    [Fact]
    public void Choose_WaylandSession_WithXWayland_RoutesToFallback_NotX11()
    {
        // The deliberate non-fallback (§2.1): XWayland (both WAYLAND_DISPLAY and DISPLAY
        // set) is a Wayland session. The X11 backend would see only XWayland windows and
        // report wrong/missing foregrounds for native Wayland apps, so we report honest
        // Unknown instead. This MUST differ from the overlay plan, which routes this same
        // input to X11.
        var titleChoice = LinuxTitleProviderBackendPlan.Choose(LinuxSessionType.Wayland, xDisplayReachable: true);
        var overlayChoice = LinuxOverlayBackendPlan.Choose(LinuxSessionType.Wayland, xDisplayReachable: true);

        Assert.Equal(LinuxTitleBackendKind.Fallback, titleChoice);
        Assert.Equal(LinuxOverlayBackendKind.X11InputShape, overlayChoice);
    }

    [Fact]
    public void BackendKind_HasNoWaylandMember_InThisWave()
    {
        // Structural guard: the wave-3 Wayland backends (wlr-foreign-toplevel-management,
        // ext-foreign-toplevel-list, GNOME extension) are intentionally NOT members in
        // slices A+B. Adding one is a wave-3 slice and requires extending this enum.
        var names = System.Enum.GetNames(typeof(LinuxTitleBackendKind));

        Assert.Equal(2, names.Length);
        Assert.DoesNotContain(names, n => n.Contains("Wayland", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Wlr", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Gnome", System.StringComparison.OrdinalIgnoreCase));
    }
}
