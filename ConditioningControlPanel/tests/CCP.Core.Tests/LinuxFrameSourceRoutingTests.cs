using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// End-to-end session-env → backend-kind routing: composes <see cref="LinuxSessionDetector"/>
/// (env vars → session type) with <see cref="LinuxFrameSourcePlan"/> (session type + backend
/// probes → backend kind). Pins the linux-framesource-contract.md §2.1/§2.2 acceptance
/// criteria: Wayland-first detection order over env permutations, no crash on any
/// permutation, and the 3-way X11 priority MIT-SHM &gt; XGetImage-basic &gt; fallback selected
/// ONLY for a native X11 session.
/// </summary>
public class LinuxFrameSourceRoutingTests
{
    /// <summary>
    /// For a native X11 session, the SHM fast path is preferred when its probe succeeded;
    /// otherwise the basic path; otherwise the black-frame fallback. Non-X11 sessions are
    /// fallback regardless of probe outcome (XWayland black-root rule).
    /// </summary>
    [Theory]
    [InlineData(null, null, null, LinuxSessionType.Unknown)]    // no env → unknown session
    [InlineData("x11", null, ":0", LinuxSessionType.X11)]       // native X11 + display
    [InlineData("x11", null, null, LinuxSessionType.X11)]       // XDG says x11 → X11 session
    [InlineData("wayland", "wayland-0", ":0", LinuxSessionType.XWayland)] // XWayland
    [InlineData("wayland", "wayland-0", null, LinuxSessionType.Wayland)]  // pure Wayland
    [InlineData(null, "wayland-1", ":1", LinuxSessionType.XWayland)]      // both vars → XWayland
    [InlineData(null, null, ":0", LinuxSessionType.X11)]        // DISPLAY only → X11
    [InlineData("tty", null, ":0", LinuxSessionType.X11)]       // unrecognized XDG + DISPLAY → X11
    public void Routing_Detector_To_Plan_AllProbeCombos(
        string? xdg, string? wl, string? disp, LinuxSessionType expectedSession)
    {
        var session = LinuxSessionDetector.Detect(xdg, wl, disp);
        Assert.Equal(expectedSession, session);

        // The plan's three probe outcomes for this session.
        var withShm = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: false, x11ShmAvailable: true);
        var withBasicOnly = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: true, x11ShmAvailable: false);
        var withNeither = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: false, x11ShmAvailable: false);
        var withBoth = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: true, x11ShmAvailable: true);

        if (session == LinuxSessionType.X11)
        {
            // 3-way priority: SHM &gt; basic &gt; fallback.
            Assert.Equal(LinuxFrameSourceBackendKind.X11Shm, withShm);
            Assert.Equal(LinuxFrameSourceBackendKind.X11Shm, withBoth); // SHM wins over basic
            Assert.Equal(LinuxFrameSourceBackendKind.X11Basic, withBasicOnly);
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withNeither);
        }
        else
        {
            // Wayland/XWayland/Unknown: ALWAYS fallback, never an X11 backend, no matter which
            // probe "succeeded" (contract §2.1 XWayland note).
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withShm);
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withBasicOnly);
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withNeither);
            Assert.Equal(LinuxFrameSourceBackendKind.Fallback, withBoth);
        }
    }

    [Fact]
    public void Routing_AllSessionTypes_AllProbes_NeverCrash()
    {
        // Contract §2.2 guarantee exercised over the full session × probe matrix: every
        // combination yields a valid backend kind (the selector's terminal fallback arm).
        foreach (LinuxSessionType session in System.Enum.GetValues(typeof(LinuxSessionType)))
        {
            foreach (bool shm in new[] { false, true })
            {
                foreach (bool basic in new[] { false, true })
                {
                    var choice = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: basic, x11ShmAvailable: shm);

                    Assert.True(
                        choice == LinuxFrameSourceBackendKind.X11Shm ||
                        choice == LinuxFrameSourceBackendKind.X11Basic ||
                        choice == LinuxFrameSourceBackendKind.Fallback);

                    // No non-X11 session ever lands on an X11 backend.
                    if (session != LinuxSessionType.X11)
                    {
                        Assert.Equal(LinuxFrameSourceBackendKind.Fallback, choice);
                    }
                }
            }
        }
    }

    [Fact]
    public void Routing_XShm_NeverSelectedOutsideNativeX11()
    {
        // The SHM fast path is X11-only: a Wayland/XWayland/Unknown session must never pick it,
        // even if the (irrelevant) SHM probe reported usable.
        foreach (LinuxSessionType session in System.Enum.GetValues(typeof(LinuxSessionType)))
        {
            var choice = LinuxFrameSourcePlan.Choose(session, x11BasicAvailable: true, x11ShmAvailable: true);
            if (session == LinuxSessionType.X11)
            {
                Assert.Equal(LinuxFrameSourceBackendKind.X11Shm, choice);
            }
            else
            {
                Assert.Equal(LinuxFrameSourceBackendKind.Fallback, choice);
            }
        }
    }
}
