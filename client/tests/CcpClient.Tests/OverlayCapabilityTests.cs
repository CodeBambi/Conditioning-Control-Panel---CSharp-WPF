using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// An overlay is the easiest capability in this codebase to fake, because every one of its
/// failure modes is invisible from inside the process: a window that is present and invisible,
/// present and buried, or present and swallowing every click all look identical to the code that
/// created it. A test that only checked the return value of <c>Present</c> would certify all three
/// — and the first port attempt shipped exactly that, a seam where every operation returns
/// <c>void</c> (<c>CCP.Core/Platform/IOverlaySurface.cs</c>) with a human-judged
/// "tell me what you saw" harness as its only verification
/// (<c>tests/CCP.Avalonia.Desktop.Windows.Smoke/VisibleOverlayVerification.cs</c>).
///
/// <para><b>So the shape of every effect fact here is the same:</b> compare a fact about the
/// MACHINE (does this session have an interactive desktop — established by
/// <see cref="OverlayWindowProbe"/>, independent P/Invokes, never the product's) against a fact
/// about the WINDOW MANAGER (does it hold this window, at this rectangle, with this alpha, at this
/// z position, and where does it route a click), and only then against the backend's CLAIM. A
/// backend that reports Available without a window on screen fails the second comparison; one that
/// degenerates into always refusing fails the first. Neither branch is a skip and no assertion sits
/// behind a conditional.</para>
///
/// <para><b>What these facts do NOT prove.</b> That anything is DRAWN — nothing is; this capability
/// puts no content on the surface and is wired to no effect. That a human SEES it: composited
/// pixels depend on DWM, on exclusive-fullscreen and DirectX applications, on Magnifier, on RDP and
/// mirror drivers, and every query below can answer yes while a screen shows nothing. That a real
/// pointer passes through: <c>WindowFromPoint</c> is the window manager's routing question asked,
/// not delivered input. Multi-monitor placement, cross-DPI behaviour, sustained topmost under
/// minutes of contention, and every part of Linux. Those are headed claims and a named manual gate
/// (this packet's record.md).</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayCapabilityTests
{
    /// <summary>
    /// A stripped WS_EX_TOPMOST must be re-asserted, and a stripped WS_EX_LAYERED must still refuse.
    ///
    /// <para>This is the fact behind the four-surface coexistence run going red in a full suite pass
    /// while passing in isolation. Topmost is not a write that stays written: another process
    /// asserting it can leave this window without the bit between SetWindowPos returning TRUE and
    /// the very next read-back. Present used to read the style once and refuse, returning BEFORE the
    /// bounded re-assertion this class already owned. The requirement is unchanged — every required
    /// bit must be present in the OS's own read-back — so the second leg matters as much as the
    /// first: a bit that no re-assertion can restore must still fail, or the fix would have been a
    /// weakened check wearing a loop.</para>
    /// </summary>
    [Fact]
    public void AStrippedTopmostIsReasserted_ButAStrippedLayeredStillRefuses()
    {
        using var overlay = new Win32OverlayPresence();
        var bounds = new OverlayBounds(120, 120, 220, 160);
        var first = overlay.Present(new OverlaySurfaceRequest(bounds, 0.6, ClickThrough: true));

        if (!OverlayWindowProbe.MachineHasInteractiveDesktop)
        {
            Assert.IsNotType<CapabilityState.Available>(first);
            return;
        }

        Assert.IsType<CapabilityState.Available>(first);
        var window = overlay.NativeHandles.Window;

        // Another process wins the topmost adjudication: the bit is gone, everything else intact.
        OverlayWindowProbe.DemoteFromTopmost(window);
        Assert.True((OverlayWindowProbe.ExStyleOf(window) & OverlayWindowProbe.TopmostBit) == 0,
            "the strip did not take, so this fact would prove nothing");

        var recovered = overlay.Present(new OverlaySurfaceRequest(bounds, 0.6, ClickThrough: true));
        Assert.True(recovered is CapabilityState.Available,
            "a stripped topmost bit must be re-asserted rather than refused, because that is the state a "
            + "full suite pass and any real desktop with another topmost window both produce: "
            + PointerSurfaceObservations.Describe(recovered));
        Assert.True((OverlayWindowProbe.ExStyleOf(window) & OverlayWindowProbe.TopmostBit) != 0,
            "Present returned Available while the OS still holds no topmost bit for the window");

        // The other half: a bit no re-assertion can restore must still refuse, so the loop above
        // cannot be mistaken for a relaxed requirement.
        OverlayWindowProbe.ClearExStyle(window, OverlayWindowProbe.LayeredBit);
        var refused = overlay.Present(new OverlaySurfaceRequest(bounds, 0.6, ClickThrough: true));
        Assert.True(refused is not CapabilityState.Available,
            "a window stripped of WS_EX_LAYERED is not the window that was asked for and must refuse: "
            + PointerSurfaceObservations.Describe(refused));
    }

    [Fact]
    public void TheHitTestOracle_CanTellAClickThroughWindowFromAnOpaqueOne()
    {
        // The instrument's own control. Every input fact in this file trusts WindowFromPoint to
        // honour WS_EX_TRANSPARENT. An oracle that had degenerated into "always answer the top
        // window" — or into "never answer the top window" — would silently certify a capability
        // that passes nothing through, so it is asked, on every run, about a rig this probe builds
        // and owns: an opaque window, a click-through window raised above it, and the same flag
        // cleared again.
        var control = OverlayWindowProbe.RunNegativeControl();

        Assert.Equal(OverlayWindowProbe.MachineHasInteractiveDesktop, control.OpaqueWindowCreated);
        Assert.Equal(OverlayWindowProbe.MachineHasInteractiveDesktop, control.TransparentWindowCreated);
        Assert.Equal(control.MachineHasInteractiveDesktop, control.OpaqueWindowWinsItsOwnPoint);
        Assert.True(control.TransparentWindowAboveIsSkipped == control.MachineHasInteractiveDesktop,
            "a WS_EX_TRANSPARENT window raised ABOVE an opaque one did not let the point through to it "
            + $"(machine has an interactive desktop = {control.MachineHasInteractiveDesktop}, hit test skipped the "
            + $"transparent window = {control.TransparentWindowAboveIsSkipped}). The oracle every click-through "
            + "fact in this suite depends on cannot detect click-through, so none of them prove anything");
        Assert.True(control.ClearingTheFlagHandsThePointBack == control.MachineHasInteractiveDesktop,
            "clearing WS_EX_TRANSPARENT did not hand the point back to the upper window — the oracle cannot say "
            + "'this window WOULD have caught the click', which is the half that stops 'the point went elsewhere' "
            + "from being satisfied by a window that is not there");
        Assert.False(control.ScratchWindowsExistAfterCleanup,
            "the probe's scratch windows survived their own teardown — the window-existence check cannot say 'no'");
    }

    [Fact]
    public void TheAlphaOracle_TellsALayeredWindowWithNoAttributesFromARealOne()
    {
        // The ghost detector's control, and the measurement that makes the whole capability
        // necessary. CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45 sets WS_EX_LAYERED
        // and never calls SetLayeredWindowAttributes. The OS calls the result VISIBLE and composites
        // nothing: a window that exists, is on top, and cannot be seen.
        var control = OverlayWindowProbe.RunNegativeControl();

        Assert.Equal(control.MachineHasInteractiveDesktop ? OverlayWindowProbe.ControlAlpha : -1,
            control.AlphaOfWindowWithAttributesSet);
        Assert.Equal(-1, control.AlphaOfLayeredWindowWithNoAttributes);
        Assert.True(control.GhostWindowStillReportsVisible == control.MachineHasInteractiveDesktop,
            "a WS_EX_LAYERED window whose attributes were never set did not report visible "
            + $"({control.GhostWindowStillReportsVisible}) on a machine whose interactive-desktop state is "
            + $"{control.MachineHasInteractiveDesktop}. If the OS refused to call that window visible there would "
            + "be no ghost to detect and this capability's alpha check would be decoration");
    }

    [Fact]
    public void PresentingTheSurface_IsConfirmedByTheOperatingSystem_NotByTheBackendsOwnSayso()
    {
        var run = OverlayObservations.Lifecycle;

        Assert.True(run.OsSeesWindowVisible == run.MachineHasInteractiveDesktop,
            $"this session has an interactive desktop = {run.MachineHasInteractiveDesktop}, but after Present the "
            + $"OS reporting the surface visible = {run.OsSeesWindowVisible}. On a machine with a desktop the "
            + $"window must really be on it; with no desktop nothing may be claimed. Backend said: "
            + $"{OverlayObservations.Describe(run.PresentState)}");
        Assert.True(run.ClaimedAvailableOnPresent == run.OsSeesWindowVisible,
            $"the backend claimed Available = {run.ClaimedAvailableOnPresent} while the OS's own answer to 'is this "
            + $"window visible' = {run.OsSeesWindowVisible}. A claim that outruns the effect is the exact failure "
            + $"this capability exists to prevent. Backend said: {OverlayObservations.Describe(run.PresentState)}");
        Assert.True(run.BackendReportsPresenting == run.OsSeesWindowVisible,
            $"IsPresenting = {run.BackendReportsPresenting} disagrees with the OS ({run.OsSeesWindowVisible})");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsSeesWindow);
    }

    [Fact]
    public void TheOperatingSystemHoldsExactlyTheRectangleThatWasAskedFor()
    {
        var run = OverlayObservations.Lifecycle;

        Assert.Equal(
            run.MachineHasInteractiveDesktop ? run.RequestedRect : (-1, -1, -1, -1),
            run.OsHeldRect);
    }

    [Fact]
    public void TheOperatingSystemHoldsANonZeroAlphaForTheSurface_SoTheCompositorHasSomethingToDraw()
    {
        // The check the first attempt would have failed. GetLayeredWindowAttributes returning the
        // requested alpha is the only in-process evidence that this window is not the ghost, and it
        // is one of the confirmations Available is downstream of.
        var run = OverlayObservations.Lifecycle;

        Assert.Equal(run.MachineHasInteractiveDesktop ? run.RequestedAlpha : -1, run.OsHeldAlpha);
        Assert.True(run.OsHeldAlpha > 0 == run.MachineHasInteractiveDesktop,
            $"the OS holds alpha {run.OsHeldAlpha} for the surface; a layered window the OS holds no alpha for is "
            + "present, on top and invisible — the exact state CCP.Avalonia.Desktop.Windows/"
            + $"WindowsOverlaySurface.cs:26-45 leaves a window in. Backend said: "
            + $"{OverlayObservations.Describe(run.PresentState)}");
    }

    [Fact]
    public void TheSurfaceSitsAboveEveryOrdinaryWindow_InTheOperatingSystemsOwnZOrder()
    {
        // The ORDERING, walked from GetTopWindow, not the WS_EX_TOPMOST flag. A flag is what the
        // first attempt trusted; a flag is also exactly what a broken SetWindowPos leaves behind.
        // Deliberately not "above every window": other topmost windows exist and contest, which is
        // why WPF re-raises its own (Services/Flash/FlashService.cs:206-243).
        var run = OverlayObservations.Lifecycle;

        Assert.True(run.ZOrder.AboveEveryOrdinaryWindow == run.MachineHasInteractiveDesktop,
            $"the OS puts the surface at z position {run.ZOrder.Index} of {run.ZOrder.VisibleCount} visible "
            + $"top-level windows, and the first ordinary (non-topmost) window is at {run.ZOrder.FirstOrdinaryIndex}. "
            + $"On a machine with an interactive desktop ({run.MachineHasInteractiveDesktop}) the surface must come "
            + $"first. Backend said: {OverlayObservations.Describe(run.PresentState)}");
        Assert.Equal(run.MachineHasInteractiveDesktop, (run.OsHeldExStyle & 0x00000008) != 0);
    }

    [Fact]
    public void InputPassesThroughTheSurface_MeasuredInBothPolaritiesAtTheSamePoint()
    {
        // The load-bearing fact, and the one the packet named as the easiest to fake. It is NOT
        // "WS_EX_TRANSPARENT was set". It is: the window manager, asked about one fixed point at
        // the surface's centre, answers a DIFFERENT window while clicks pass through, answers the
        // SURFACE when click-through is off, and answers a different window again when it is
        // restored. The middle leg is what stops the outer two from being satisfied by a window
        // that was never there.
        var run = OverlayObservations.Lifecycle;

        Assert.True(run.PointPassesThroughWhilePresented == run.MachineHasInteractiveDesktop,
            $"with the surface presented click-through, the window manager routes its centre to "
            + $"{run.WinnerWhileClickThrough}; it must not be the surface. Machine has an interactive desktop = "
            + $"{run.MachineHasInteractiveDesktop}. Backend said: {OverlayObservations.Describe(run.PresentState)}");
        Assert.True(run.SurfaceTakesPointWhenClickThroughOff == run.MachineHasInteractiveDesktop,
            $"with click-through switched OFF the same point must route TO the surface, and it does not "
            + $"({run.SurfaceTakesPointWhenClickThroughOff}). Without this half, 'the point went elsewhere' is also "
            + $"true of a window that does not exist, and the click-through claim proves nothing. Backend said: "
            + $"{OverlayObservations.Describe(run.ClickThroughOffState)}");
        Assert.True(run.PointPassesThroughAgainAfterRestoring == run.MachineHasInteractiveDesktop,
            "restoring click-through must send the point away again; the differential has to close both ways");
        Assert.True(run.ClaimedAvailableOnClickThroughOff == run.MachineHasInteractiveDesktop,
            $"SetClickThrough(false) claimed Available = {run.ClaimedAvailableOnClickThroughOff}: "
            + OverlayObservations.Describe(run.ClickThroughOffState));
        Assert.Equal(run.MachineHasInteractiveDesktop, run.ClaimedAvailableOnClickThroughOn);
    }

    [Fact]
    public void PresentingTheSurface_NeverTakesTheForeground()
    {
        // WS_EX_NOACTIVATE + SWP_NOACTIVATE, as WPF's flash windows carry them
        // (Services/Flash/FlashService.cs:3617, :3666, :3826). An overlay that steals focus
        // interrupts whatever the user was typing into, which is a worse desktop than no overlay.
        var run = OverlayObservations.Lifecycle;

        Assert.False(run.SurfaceIsForeground,
            "the overlay surface became the foreground window when it was shown");
    }

    [Fact]
    public void WithdrawingTheSurface_TakesItOffTheScreenForReal()
    {
        var run = OverlayObservations.Lifecycle;

        Assert.False(run.OsSeesWindowVisibleAfterWithdraw,
            $"after Withdraw the OS still reports the surface visible — it was not withdrawn, only reported "
            + $"withdrawn. Backend said: {OverlayObservations.Describe(run.WithdrawState)}");
        Assert.False(run.SurfaceTakesPointAfterWithdraw,
            "after Withdraw the window manager still routes the surface's centre to it: a hidden window that is "
            + "still eating input is the worst of both states");
        Assert.False(run.BackendReportsPresentingAfterWithdraw);
        Assert.True(run.ClaimedAvailableOnWithdraw == run.MachineHasInteractiveDesktop,
            $"Withdraw claimed Available = {run.ClaimedAvailableOnWithdraw} on a machine whose interactive-desktop "
            + $"state is {run.MachineHasInteractiveDesktop}; with nothing ever presented there is nothing to "
            + $"succeed at. Backend said: {OverlayObservations.Describe(run.WithdrawState)}");
    }

    [Fact]
    public void DisposingThePresence_LeavesNoTopLevelWindowBehind()
    {
        // The path a crash-adjacent shutdown takes. A leaked overlay window is an always-on-top
        // window for the life of the process that no code can reach any more.
        var run = OverlayObservations.Lifecycle;

        Assert.False(run.OsSeesWindowAfterDispose,
            "Dispose left a top-level window behind — an unreachable always-on-top window for the life of the "
            + "process");
        Assert.Null(run.TeardownDiagnostic);
    }

    [Fact]
    public void AskingAPresenceThatHasPresentedNothing_NeverReadsAsSuccess()
    {
        var run = OverlayObservations.NothingPresented();

        Assert.False(run.WithdrawClaimedAvailable);
        Assert.Equal(OverlayReasonCodes.OverlayNothingPresented, run.WithdrawCode);
        Assert.False(run.ClickThroughClaimedAvailable);
        Assert.Equal(OverlayReasonCodes.OverlayNothingPresented, run.ClickThroughCode);
        Assert.False(run.DisposedPresentClaimedAvailable);
        Assert.Equal(OverlayReasonCodes.OverlayPresenceDisposed, run.DisposedPresentCode);
    }

    [Fact]
    public void TheLinuxBranchRefusesHonestly_AndCarriesItsManualGateWithIt()
    {
        // Reachable from this Windows box on purpose: the refusal path is testable here and the
        // Windows path is testable nowhere else, which is the honest asymmetry rather than a hidden
        // one. The first attempt's Linux overlay is the counter-example — a documented never-throw
        // seam that "degrades to logged no-ops"
        // (CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs:9-78) behind a selector
        // "guaranteed never to throw and never to return null" (LinuxOverlayBackendSelector.cs:41-88).
        var run = OverlayObservations.RefusalFor(OverlayHostPlatform.Linux);

        Assert.False(run.ClaimedAvailable);
        Assert.False(run.ReportsPresenting);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, run.Code);
        Assert.Contains("MANUAL GATE (Linux, undischarged)", run.Detail, StringComparison.Ordinal);
        Assert.Contains("XFixes", run.Detail, StringComparison.Ordinal);
        Assert.Contains("wlr-layer-shell", run.Detail, StringComparison.Ordinal);
        Assert.True(run.WithdrawAlsoRefused,
            "the Linux refusal covers Present but not Withdraw — a partial refusal is a path a caller can mistake "
            + "for a presented surface");
        Assert.True(run.ClickThroughAlsoRefused,
            "the Linux refusal covers Present but not SetClickThrough");
    }

    [Fact]
    public void EveryPlatformWithoutABackend_RefusesRatherThanNoOps()
    {
        var macOs = OverlayObservations.RefusalFor(OverlayHostPlatform.MacOs);
        var unknown = OverlayObservations.RefusalFor(OverlayHostPlatform.Unknown);

        Assert.False(macOs.ClaimedAvailable);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, macOs.Code);
        Assert.Contains("nothing was attempted", macOs.Detail, StringComparison.Ordinal);
        Assert.False(unknown.ClaimedAvailable);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, unknown.Code);
        Assert.Contains("nothing was attempted", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvisibleSurfaceCannotEvenBeRequested()
    {
        // Opacity zero is the ghost at the request level. Clamping it would put pixels on screen
        // nobody asked for; accepting it would ship the window that exists and cannot be seen.
        var bounds = new OverlayBounds(0, 0, 100, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlaySurfaceRequest(bounds, 0.0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlaySurfaceRequest(bounds, -0.5, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlaySurfaceRequest(bounds, 1.5, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlaySurfaceRequest(new OverlayBounds(0, 0, 0, 10), 1.0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlaySurfaceRequest(new OverlayBounds(0, 0, 10, 0), 1.0, true));

        // And a legal-but-tiny opacity must never round down INTO the ghost.
        Assert.Equal(OverlaySurfaceRequest.MinimumAlpha, new OverlaySurfaceRequest(bounds, 0.0001, true).Alpha);
        Assert.Equal(255, new OverlaySurfaceRequest(bounds, 1.0, true).Alpha);
    }

    [Fact]
    public void TheDisplayEnumeration_AgreesWithTheOperatingSystemsOwnMonitorCount()
    {
        // WPF places overlays per monitor, not on the primary one
        // (Services/Flash/FlashService.cs:2204-2245). The probe reads the count and the primary
        // size through its own GetSystemMetrics, so this is a cross-check and not a tautology.
        var displays = OverlayDisplays.Enumerate();
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;

        Assert.Equal(OverlayWindowProbe.MonitorCount, displays.Count);
        Assert.Equal(OverlayWindowProbe.MachineHasInteractiveDesktop, displays.Count > 0);
        Assert.Equal(
            OverlayWindowProbe.MachineHasInteractiveDesktop ? (screenWidth, screenHeight) : (0, 0),
            displays.Count > 0 ? (displays[0].Bounds.Width, displays[0].Bounds.Height) : (0, 0));
        Assert.Equal(
            OverlayWindowProbe.MachineHasInteractiveDesktop,
            displays.Count > 0 && displays[0].IsPrimary);
    }

    [Fact]
    public void NothingInThisCapabilityDrawsAnything_AndTheContractSaysSo()
    {
        // Deliberate and load-bearing: this packet delivers a surface and wires it to no effect. The
        // claim string is part of the contract because it is what a reader of a log sees, and it
        // must not let anyone believe a flash was shown.
        var run = OverlayObservations.Lifecycle;
        var detail = run.PresentState is CapabilityState.Available available
            ? available.Detail
            : OverlayObservations.Describe(run.PresentState);

        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            detail.Contains("nothing is drawn on this surface", StringComparison.Ordinal));
        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            detail.Contains("headed claim", StringComparison.Ordinal));
    }
}
