using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The video surface swallows the user's click, and that is a decision rather than an
/// accident.</b> These facts pin the OUTCOME — where the window manager routes a point, and where a
/// REAL synthesised click actually lands — never the extended style the surface asked for.
///
/// <para><b>Upstream's default is a click sink, established from source before anything here was
/// written.</b> All three of its video render paths swallow the press at the window level:
/// <c>Services/Video/VideoService.cs:2894-2907</c> (the LibVLC path's <c>PreviewMouseDown</c>, whose
/// own comment is "it swallows every one of them"), <c>:4162-4166</c> (the MediaElement fallback) and
/// <c>:4226-4230</c> (the mirror), above a hit-testable transparent rectangle placed to catch "all
/// clicks before they reach the video surface" (<c>:2862-2874</c>) on an opaque black topmost window
/// (<c>:2619-2636</c>). Upstream then keeps that message on purpose while refusing the z-order raise
/// it would cause (<c>PreventClickRaise</c>, <c>:7205-7255</c>) and disables LibVLC's native children
/// so the press lands on the top-level window (<c>:7264-7295</c>).</para>
///
/// <para><b>The strict lock is not the click policy.</b> It governs DISMISSAL — the <c>Closing</c>
/// veto at <c>:4274</c> and the panic/Alt+F4 block at <c>:4276-4306</c>, against the non-strict
/// branch's "ESC dismisses this video" at <c>:4328-4345</c>. Both branches assume the window already
/// owns the input.</para>
///
/// <para><b>What is deliberately NOT asserted here.</b> Whether this surface appears in a screen
/// recording. Capture inclusion is per feature contract
/// (<c>.claude/skills/overlay-clickthrough/SKILL.md:60-62</c>), it is an unresolved owner question
/// under A-001, and no <c>SetWindowDisplayAffinity</c> call exists anywhere in <c>client/src</c>.
/// Nothing in this file infers capture from input.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class VideoInputRoutingTests : RealDesktopFacts
{
    private static VideoSurfaceObservations.InputRoutingRun Run => VideoSurfaceObservations.InputRouting;

    [Fact]
    public void TheInstrumentCanSeeAClickARRIVEAtThisPointAtAll_WithoutWhichEveryFactBelowIsAboutAWindowNothingCanReach()
    {
        var run = Run;

        Assert.True(run.UnderneathIsUp == run.MachineHasInteractiveDesktop,
            $"the probe's own click-counting window is not on the desktop at {VideoSurfaceObservations.RoutingBounds}. "
            + "Every 'the click did not arrive' below would then be a statement about a window nothing could ever "
            + "reach, which is the overlay fake in a new costume");

        Assert.True(run.RoutesToUnderneathBefore == run.MachineHasInteractiveDesktop,
            $"before any video surface existed the window manager routed the point to "
            + $"{PointerWindowProbe.DescribeWindow(run.RoutedToBefore)} instead of the probe's own window "
            + $"{PointerWindowProbe.DescribeWindow(run.UnderneathWindow)}. Something foreign owns this point and no "
            + "click may be injected at it");

        Assert.True(run.ClickInjectedBefore == run.MachineHasInteractiveDesktop,
            "SendInput was refused — a locked workstation, the secure desktop, or UIPI. The instrument cannot "
            + "inject, so it cannot measure delivery and nothing in this file proves anything");

        Assert.True(run.DownsBefore == (run.MachineHasInteractiveDesktop ? 1 : 0),
            $"the window underneath received {run.DownsBefore} WM_LBUTTONDOWN from the control click. One is the "
            + "whole point: it is what makes 'zero later' mean the surface took it");
    }

    [Fact]
    public void TheWindowManagerRoutesAPointInsideTheSurfaceTOTheSurface_WhichIsTheOUTCOMEAndNotTheStyleItAskedFor()
    {
        var run = Run;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.PresentState is CapabilityState.Available
                : run.PresentState is CapabilityState.Unavailable,
            $"the surface has to be genuinely up over the window underneath before its input policy means anything, "
            + $"and placing it answered {PointerSurfaceObservations.Describe(run.PresentState)}");

        // THE FACT THE ROW WAS OPENED FOR. Not "WS_EX_TRANSPARENT is clear" — the overlay measured a
        // run where every style write succeeded and the ex-style read back wrong anyway
        // (Win32OverlayPresence.cs:504-511) — but the window manager's own answer to "whose point is
        // this", taken at the same point that answered the probe's window one moment earlier.
        Assert.True(run.RoutesToSurfaceDuring == run.MachineHasInteractiveDesktop,
            $"with the video surface up, the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(run.RoutedToDuring)} rather than to the surface "
            + $"{PointerWindowProbe.DescribeWindow(run.SurfaceWindow)}. This surface is a deliberate click sink — "
            + "upstream's own video window swallows every press (VideoService.cs:2894-2907) — so a point that "
            + "routes past it is the port silently becoming click-through");
    }

    [Fact]
    public void ARealClickInsideTheSurfaceDoesNotReachTheWindowUNDERNEATHIt()
    {
        var run = Run;

        Assert.True(run.ClickInjectedDuring == run.MachineHasInteractiveDesktop,
            "the second click was not injected, so 'the window underneath received nothing' is a statement about a "
            + "click that never happened");

        // The count is compared against the leg-one count rather than against zero: zero would also be
        // satisfied by an instrument that never counted anything, and leg one already proved it does.
        Assert.Equal(run.DownsBefore, run.DownsDuring);
    }

    [Fact]
    public void WithdrawingTheSurfaceGivesThePointANDTheClickBackToTheDesktop()
    {
        var run = Run;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.WithdrawState is CapabilityState.Available
                : run.WithdrawState is CapabilityState.Unavailable,
            $"taking the surface down answered {PointerSurfaceObservations.Describe(run.WithdrawState)}");

        // "No failure leaves an invisible input-blocking surface" and "teardown restores normal
        // desktop input" are the click-through skill's own safety invariants
        // (overlay-clickthrough/SKILL.md:30-31), and neither follows from the surface being hidden:
        // a hidden window that still owned the point would be exactly the shape they name.
        Assert.True(run.RoutesToUnderneathAfter == run.MachineHasInteractiveDesktop,
            $"after the surface was withdrawn the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(run.RoutedToAfter)} rather than back to the window underneath "
            + $"{PointerWindowProbe.DescribeWindow(run.UnderneathWindow)}");

        Assert.True(run.ClickInjectedAfter == run.MachineHasInteractiveDesktop,
            "the third click was not injected, so the desktop getting its input back is unmeasured");

        Assert.True(run.DownsAfter == (run.MachineHasInteractiveDesktop ? 2 : 0),
            $"the window underneath has received {run.DownsAfter} clicks in total. Two is the shape of the whole "
            + "run: one before the surface existed, none while it was up, one after it came down");
    }
}
