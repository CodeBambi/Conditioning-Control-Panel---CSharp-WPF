using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the SupportsClickThrough routing (overlay-cap activation): Windows is always true,
/// Linux reports the X11 overlay probe result (X display reachable AND XFixes ≥ 2 — the
/// same gate that selects <c>X11InputShapeBackend</c> over the fallback), and every other
/// platform (macOS, mobile) stays false until an overlay backend exists there
/// (macos-overlay-contract.md SafeDegrade policy).
/// </summary>
public class ClickThroughCapabilityPlanTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_Windows_AlwaysTrue_RegardlessOfLinuxProbe(bool linuxProbe)
    {
        Assert.True(ClickThroughCapabilityPlan.Compute(
            isWindows: true, isLinux: false, linuxX11InputShapeAvailable: linuxProbe));
    }

    [Fact]
    public void Compute_Linux_ProbeAvailable_True()
    {
        // The dormant-capability regression this pins: the Linux X11 overlay backend
        // (XFixes input shape + _NET_WM_STATE_ABOVE) is built and selected at runtime,
        // so the capability must report true when the probe confirms availability.
        Assert.True(ClickThroughCapabilityPlan.Compute(
            isWindows: false, isLinux: true, linuxX11InputShapeAvailable: true));
    }

    [Fact]
    public void Compute_Linux_ProbeUnavailable_False()
    {
        // No X display / XFixes < 2 → FallbackBackend → no per-region click-through
        // (never-trap rule, linux-overlay-contract.md §1.4). Capability must say so.
        Assert.False(ClickThroughCapabilityPlan.Compute(
            isWindows: false, isLinux: true, linuxX11InputShapeAvailable: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compute_NeitherWindowsNorLinux_AlwaysFalse(bool linuxProbe)
    {
        // macOS (and mobile): overlay click-through backend not implemented yet.
        Assert.False(ClickThroughCapabilityPlan.Compute(
            isWindows: false, isLinux: false, linuxX11InputShapeAvailable: linuxProbe));
    }
}
