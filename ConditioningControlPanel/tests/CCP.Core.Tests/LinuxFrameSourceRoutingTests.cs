using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// End-to-end session-env → backend-kind routing: composes <see cref="LinuxSessionDetector"/>
/// (env vars → session type) with <see cref="LinuxFrameSourcePlan"/> (session type +
/// backend probe → backend kind). Pins the linux-framesource-contract.md §2.1 acceptance
/// criterion: Wayland-first detection order over env permutations, no crash on any
/// permutation, and the X11 capture backend selected ONLY for a native X11 session with a
/// live X display.
/// </summary>
public class LinuxFrameSourceRoutingTests
{
    /// <param name="expectedWhenXAvailable">
    /// Expected backend when the X11 probe succeeded. Non-X11 sessions are fallback even
    /// when X is technically reachable (XWayland black-root rule).
    /// </param>
    [Theory]
    [InlineData(null, null, null, LinuxFrameSourceBackendKind.Fallback)]            // unknown session
    [InlineData("x11", null, ":0", LinuxFrameSourceBackendKind.X11)]                // native X11 + display
    [InlineData("x11", null, null, LinuxFrameSourceBackendKind.X11)]               // XDG says x11 even without DISPLAY → X11 session; probe result decides (see withoutX)
    [InlineData("wayland", "wayland-0", ":0", LinuxFrameSourceBackendKind.Fallback)] // XWayland → fallback (capture)
    [InlineData("wayland", "wayland-0", null, LinuxFrameSourceBackendKind.Fallback)] // pure Wayland → fallback
    [InlineData(null, "wayland-1", ":1", LinuxFrameSourceBackendKind.Fallback)]      // both vars, no XDG → XWayland → fallback
    [InlineData(null, null, ":0", LinuxFrameSourceBackendKind.X11)]                 // DISPLAY only → X11
    [InlineData("tty", null, ":0", LinuxFrameSourceBackendKind.X11)]                // unrecognized XDG + DISPLAY → X11
    public void Routing_Detector_To_Plan(
        string? xdg, string? wl, string? disp, LinuxFrameSourceBackendKind expectedWhenXAvailable)
    {
        var session = LinuxSessionDetector.Detect(xdg, wl, disp);

        // X11 sessions route to the X11 backend ONLY when the probe succeeded; everything
        // else is fallback regardless of probe outcome.
        var withX = LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: true);
        var withoutX = LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: false);

        Assert.Equal(expectedWhenXAvailable, withX);
        Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withoutX);
    }

    [Fact]
    public void Routing_AllSessionTypes_NeverCrash()
    {
        // Contract §2.2 guarantee exercised over the full session × probe matrix: every
        // combination yields a valid backend kind (the selector's terminal fallback arm).
        foreach (LinuxSessionType session in System.Enum.GetValues(typeof(LinuxSessionType)))
        {
            var withX = LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: true);
            var withoutX = LinuxFrameSourcePlan.Choose(session, x11BackendAvailable: false);

            Assert.True(withX == LinuxFrameSourceBackendKind.X11 || withX == LinuxFrameSourceBackendKind.Fallback);
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withoutX);
        }
    }
}
