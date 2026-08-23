using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Pointer;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The third thing this port has had to prove about a window, and the hardest.</b>
///
/// <para>The overlay packet proved a surface is click-THROUGH, and its review recorded that as the
/// property most easily faked: a window that does not exist satisfies it too. The input packet
/// proved the inverse for a keyboard-taking window and discovered that
/// <c>GetGUIThreadInfo(<i>ourThread</i>)</c> LIES — it is thread-local, and answered "our window
/// has focus" while every injected keystroke went to another application. <b>This packet must
/// prove a click LANDS on a window that must NOT take the foreground</b>, so it cannot lean on
/// either fact the input packet's chain leaned on: for this capability,
/// "we are the foreground" is a BUG.</para>
///
/// <para><b>So every fact here compares three things</b>: a fact about the MACHINE (does this
/// session have an interactive desktop, and will it accept synthesised input at all — established by
/// <see cref="PointerWindowProbe"/>, independent P/Invokes, never the product's), a fact about the
/// OPERATING SYSTEM (where does it route a point, which window procedure does a synthesised click
/// reach, and what is the foreground before and after), and only then the surface's CLAIM.</para>
///
/// <para><b>Where the chain stops, and it stops early.</b> No human clicked anything: every press
/// here is <c>SendInput</c>, and "a person popped a bubble" is a manual gate no automated step on any
/// platform discharges, Windows included. Injected input is not physical input — UIPI, the secure
/// desktop and a locked workstation each refuse it, and the fixture DETECTS those and flips its
/// expectations rather than skipping. The routing answer is momentary: any process can cover the
/// point the instant after it is given. <c>Available</c> deliberately does NOT include
/// <c>WM_MOUSEACTIVATE</c>, because that message only exists when a click really happens and the
/// product must never synthesise one — so the product's claim is "the OS will route a click here and
/// this window is configured and positioned to take nothing", never "a click was delivered". And
/// nothing here says a bubble is legible, aimable, or on a screen anybody is looking at:
/// <c>presentation-verified</c> is untouched.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class PointerCapabilityTests : RealDesktopFacts
{
    [Fact]
    public void TheDeliveryOracle_CanSeeAClickArriveAtAWindowItBuiltItself_OrNoFactBelowMeansAnything()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        // The instrument's own control, run before the product is touched. Every fact in this file
        // trusts that a synthesised click reaching a window procedure MEANS something. An oracle
        // that had degenerated into "always report delivered" would certify a capability that
        // receives nothing.
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.ProbeControlCreated);
        Assert.True(run.ProbeControlClickInjected == run.MachineHasInteractiveDesktop,
            "SendInput was refused by the OS, or the probe's own scratch target did not own its point — a locked "
            + "workstation, the secure desktop, or UIPI. The delivery oracle cannot inject, so it cannot measure "
            + "delivery and no fact in this file proves anything");
        Assert.True(run.ProbeControlSawDown == run.MachineHasInteractiveDesktop,
            "a synthesised click did not reach WM_LBUTTONDOWN in the probe's OWN non-activating window procedure. "
            + "The oracle cannot detect delivery at all");
        Assert.True(run.ProbeControlSawUp == run.MachineHasInteractiveDesktop,
            "the DOWN arrived and the UP did not. A button that goes down and never comes up is a drag or a "
            + "half-arrived injection, and the two halves are different facts");
        Assert.True(run.ProbeControlRefusedActivation == run.MachineHasInteractiveDesktop,
            "the probe's own window procedure never saw WM_MOUSEACTIVATE, so the ONE observation that shows a "
            + "procedure refusing activation with a click in its hand is not available to this suite");
        Assert.True(run.ProbeControlForegroundHeld,
            "the foreground moved across a click on the probe's own WS_EX_NOACTIVATE window. Non-activation is "
            + "then not a property of the mechanism at all, and the product's version of it proves nothing");
    }

    [Fact]
    public void TheOperatingSystemPutsTheTargetOnScreen_AtTheExactRectangle_WithUpstreamsOwnTwoStyleBits()
    {
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsSeesWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsSeesWindowVisible);
        Assert.True(!run.MachineHasInteractiveDesktop || run.OsHeldBounds == run.AskedBounds,
            $"the OS holds {run.OsHeldBounds} and {run.AskedBounds} was asked for. A routing answer about the "
            + "wrong rectangle is an answer about somewhere else");

        // Upstream's own two flags, read back OUT of the operating system rather than remembered:
        // WS_EX_NOACTIVATE (Services/BubbleService.cs:4887, constant :4899) present, and
        // WS_EX_TRANSPARENT (:4900, applied at :4891-4892) absent. Its own comment says why the
        // second one matters: "WPF's IsHitTestVisible alone doesn't prevent the window from eating
        // clicks" (:4889-4890), and its inverse leaves "a now-clickable bubble stuck click-through
        // (unpoppable)" (:4880-4884).
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsHoldsNonActivatingStyle);
        Assert.False(run.OsHoldsClickThroughStyle,
            "the OS holds WS_EX_TRANSPARENT for a target that exists to CATCH clicks. Every click aimed at it "
            + "falls through to the desktop and the bubble is unpoppable");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsPutsItAboveEveryOrdinaryWindow);
    }

    [Fact]
    public void TheWindowManagerRoutesTheTargetsOwnPointTOIt_WhichIsTheExactInverseOfSP099()
    {
        var run = PointerSurfaceObservations.Delivery;

        Assert.True(!run.MachineHasInteractiveDesktop || run.HitTestWinner == run.Window,
            $"WindowFromPoint at the target's own centre returned "
            + $"{PointerWindowProbe.DescribeWindow(run.HitTestWinner)} instead of the target "
            + $"{PointerWindowProbe.DescribeWindow(run.Window)}. The target is on screen and cannot be clicked");
    }

    [Fact]
    public void PLACINGATargetTakesNothing_TheForegroundIsTheSameWindowBeforeAndAfterAndIsNeverTheTarget()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        var run = PointerSurfaceObservations.Delivery;

        // BOTH halves, and they are different claims. "Not us" alone is satisfied by a target that
        // stole the foreground and lost it a microsecond later; "unchanged" alone is satisfied by a
        // target that was already the foreground when it was asked for.
        Assert.True(run.ForegroundBeforeOpen == run.ForegroundAfterOpen,
            $"placing a pointer target moved the foreground from "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundBeforeOpen)} to "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundAfterOpen)}. A bubble that takes the foreground "
            + "has taken the user's typing, their game or their call, from inside a process that reports success");
        Assert.NotEqual(run.Window, run.ForegroundAfterOpen);
    }

    [Fact]
    public void ACLICKARRIVESATTHETARGETSOWNWINDOWPROCEDURE_WHILETHEFOREGROUNDDOESNOTMOVE()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        // THE FACT WORTH HAVING, and the packet's whole subject. Three things at once: the OS
        // delivered both halves of a real click to THIS window's procedure; the procedure answered
        // WM_MOUSEACTIVATE with MA_NOACTIVATE while holding that click; and the foreground window is
        // byte-identical either side of it.
        var run = PointerSurfaceObservations.Delivery;

        Assert.True(run.ClickInjected == run.MachineHasInteractiveDesktop,
            "the click was not injected — either the OS refused SendInput, or the OS was not routing the target's "
            + "own point to it at the moment of the attempt. Nothing below is claimed");
        Assert.True(run.DownsSeen == (run.MachineHasInteractiveDesktop ? 1 : 0),
            $"the target's own window procedure received {run.DownsSeen} WM_LBUTTONDOWN. A handler being attached "
            + "is not a click landing, which is the exact fake this capability exists to refuse");
        Assert.True(run.UpsSeen == (run.MachineHasInteractiveDesktop ? 1 : 0),
            $"the target received {run.UpsSeen} WM_LBUTTONUP");
        Assert.True(!run.MachineHasInteractiveDesktop || run.PressedTarget == run.OpenedTarget,
            $"the press named target {run.PressedTarget} and {run.OpenedTarget} was opened. The OPERATING SYSTEM "
            + "chooses which target a click reaches, and it named the wrong one");
        Assert.True(run.PressesAfterClick - run.PressesBeforeClick == (run.MachineHasInteractiveDesktop ? 2 : 0),
            $"the surface counted {run.PressesAfterClick - run.PressesBeforeClick} delivered mouse messages for "
            + "one click; a down and an up are two");

        // The activation refusal, observed AT THE MOMENT OF A REAL CLICK. No style read-back can
        // show this: WS_EX_NOACTIVATE is a configuration, and this is the window procedure answering
        // the question the OS asks before it would activate anything.
        Assert.True(
            run.ActivationsRefusedAfter - run.ActivationsRefusedBefore
                >= (run.MachineHasInteractiveDesktop ? 1 : 0),
            $"WM_MOUSEACTIVATE arrived {run.ActivationsRefusedAfter - run.ActivationsRefusedBefore} time(s) during "
            + "a real click and was answered MA_NOACTIVATE none of them");

        // AND THE FOREGROUND DID NOT MOVE.
        Assert.True(run.ForegroundBeforeClick == run.ForegroundAfterClick,
            $"a click on the target moved the foreground from "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundBeforeClick)} to "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundAfterClick)}. THAT is the failure this whole "
            + "capability exists to make impossible");
        Assert.NotEqual(run.Window, run.ForegroundAfterClick);
    }

    [Fact]
    public void AClosedTargetReceivesNOTHING_AndTheCLICKISPROVENTOHAVELANDEDSOMEWHEREELSE()
    {
        // The negative leg, and it is deliberately NOT "the count did not move". A click injected
        // where nothing of ours listens is indistinguishable from an injection that never happened —
        // which is the overlay fake in a new costume — so a probe-owned decoy is put at the point and
        // asserted to have CAUGHT the click.
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.DecoyCreated);
        Assert.True(run.NegativeClickInjected == run.MachineHasInteractiveDesktop,
            "the negative click was not injected, so 'the closed target received nothing' is a statement about a "
            + "click that never happened");
        Assert.True(run.DecoySawDown == run.MachineHasInteractiveDesktop,
            "the probe's decoy did not catch the negative click. Without it, the assertion below is satisfied by "
            + "an injection the OS refused");
        // Compared against the count AFTER the unlistened click above, which is the last thing that
        // legitimately moved it.
        Assert.Equal(run.PressesAfterUnlistenedClick, run.PressesAfterNegativeClick);
    }

    [Fact]
    public void ClosingATargetTakesItOffTheDesktop_StopsTheRouting_AndStillCostsTheUserNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        var run = PointerSurfaceObservations.Delivery;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.CloseState is CapabilityState.Available
                : run.CloseState is CapabilityState.Unavailable,
            $"Close returned {PointerSurfaceObservations.Describe(run.CloseState)} on a machine whose interactive "
            + $"desktop is {run.MachineHasInteractiveDesktop}");
        Assert.NotEqual(run.Window, run.HitTestAfterClose);
        Assert.Equal(run.ForegroundAfterClick, run.ForegroundAfterClose);
        Assert.False(run.WindowExistsAfterDispose,
            "the surface's window survived its own dispose, so the window-existence check in every fact above "
            + "cannot say 'no'");
        Assert.Null(run.TeardownDiagnostic);
    }

    [Fact]
    public void OPENEarnsAvailableFromTheOperatingSystemAndNeverFromACallReturning()
    {
        var run = PointerSurfaceObservations.Delivery;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.OpenState is CapabilityState.Available
                : run.OpenState is CapabilityState.Unavailable,
            $"Open returned {PointerSurfaceObservations.Describe(run.OpenState)} on a machine whose interactive "
            + $"desktop is {run.MachineHasInteractiveDesktop}");

        // And the claim is not broader than the evidence: Available here rests on the six OS answers
        // asserted separately above, and on nothing else.
        Assert.True(
            run.OpenState is not CapabilityState.Available
            || (run.OsSeesWindow && run.OsSeesWindowVisible && run.OsHeldBounds == run.AskedBounds
                && run.OsHoldsNonActivatingStyle && !run.OsHoldsClickThroughStyle
                && run.OsPutsItAboveEveryOrdinaryWindow && run.HitTestWinner == run.Window
                && run.ForegroundBeforeOpen == run.ForegroundAfterOpen),
            "Open claimed Available while at least one of the six things it rests on was false. The claim is "
            + "wider than the evidence, which is the failure the whole capability contract exists to prevent");
    }

    // ---------------------------------------------------------------------------------------
    //  THE RACE
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ONESTEPNeverCarriesABubbleOffItsOwnCentre_AndTheBoundIsArithmeticOverUpstreamsConstants()
    {
        // The inequality the whole race argument rests on, pinned against the module's own numbers
        // so a later change to the speed band, the boost ceiling or the size floor reddens here
        // rather than quietly widening the race.
        Assert.True(BubblePopField.OneStepNeverCarriesABubbleOffItsOwnCentre,
            $"one animation step can move a bubble {BubblePopField.MaxStepDisplacement:0.###} px while the "
            + $"smallest legal bubble's radius is {BubblePopField.MinSize / 2.0:0.###} px. A routing answer would "
            + "then be able to go stale within one step, which is exactly the race this design removed");

        // The figures themselves, so the bound cannot be satisfied by constants that quietly moved.
        Assert.Equal(4.5, BubblePopField.MaxWobbleStep, 6);
        Assert.Equal(Math.Sqrt((12 * 12) + (4.5 * 4.5)), BubblePopField.MaxStepDisplacement, 6);
        Assert.Equal(30, BubblePopField.StepInterval.TotalMilliseconds);
    }

    [Fact]
    public void AClickAtAPointAnsweredBEFORETheTargetMoved_StillReachesTheSAMETargetOneStepLater()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        var run = PointerSurfaceObservations.Race;

        Assert.True(run.StepPixels < run.Radius,
            $"the run applied a {run.StepPixels} px displacement to a target whose radius is {run.Radius} px, so "
            + "this leg is not testing what it claims to test");
        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.OpenState is CapabilityState.Available
                : run.OpenState is CapabilityState.Unavailable,
            $"the race run could not place its target: {PointerSurfaceObservations.Describe(run.OpenState)}");
        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.SmallMoveState is CapabilityState.Available
                : run.SmallMoveState is CapabilityState.Unavailable,
            $"the one-step move did not earn Available: "
            + $"{PointerSurfaceObservations.Describe(run.SmallMoveState)}");
        Assert.True(run.SmallMoveClickInjected == run.MachineHasInteractiveDesktop,
            "the click after the one-step move was not injected, so nothing below is claimed");
        Assert.True(run.DownsAfterSmallMove == (run.MachineHasInteractiveDesktop ? 1 : 0),
            $"after moving the target one worst-case animation step, a click at the point the OS had ALREADY "
            + $"answered for it arrived {run.DownsAfterSmallMove} time(s). The bound is not holding in practice");
        Assert.True(!run.MachineHasInteractiveDesktop || run.TargetAfterSmallMove > 0);
    }

    [Fact]
    public void AClickAtAPointTheTargetHasLEFTDoesNotReachIt_WhichIsWhatMakesTheFactAboveMeanSomething()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the delivery run injects a REAL click with SendInput and asks the Win32 window manager where it "
            + "landed, then reads the foreground back. Off Windows nothing is placed, every handle is 0, and a "
            + "0 == 0 comparison would answer YES about a window that does not exist; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        // Without this, "the click still arrived" is equally true of a window that never moved.
        var run = PointerSurfaceObservations.Race;

        Assert.True(run.BigMovePixels > run.Radius * 2,
            $"the large move was {run.BigMovePixels} px against a {run.Radius * 2} px side, so the original point "
            + "may still be inside the target and this leg proves nothing");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.DecoyCreated);
        Assert.True(run.BigMoveClickInjected == run.MachineHasInteractiveDesktop,
            "the second click was not injected, so 'it did not arrive' is a statement about a click that never "
            + "happened");
        Assert.True(run.DecoySawDown == run.MachineHasInteractiveDesktop,
            "the probe's decoy did not catch the second click, so the OS may simply have swallowed it");
        Assert.Equal(run.DownsAfterSmallMove, run.DownsAfterBigMove);
        Assert.True(run.BoundHoldsArithmetically);
    }

    // ---------------------------------------------------------------------------------------
    //  Refusals, and the things Available may never rest on
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ASurfaceThatWasDisposed_RefusesEveryOperationInType_AndNeverReportsSuccessFromNothing()
    {
        var surface = new Win32PointerSurface();
        surface.Dispose();

        var open = surface.Open(
            new PointerTargetRequest(new PointerBounds(0, 0, 120, 120), 0x00201020, 0x00E0C0FF), out var target);
        var move = surface.Move(1, new PointerBounds(0, 0, 120, 120));

        Assert.Equal(0, target);
        var openReason = Assert.IsType<CapabilityState.Unavailable>(open);
        Assert.Equal(PointerReasonCodes.PointerSurfaceDisposed, openReason.Reason.Code);
        var moveReason = Assert.IsType<CapabilityState.Unavailable>(move);
        Assert.Equal(PointerReasonCodes.PointerSurfaceDisposed, moveReason.Reason.Code);
        Assert.Equal(0, surface.OpenTargets);
    }

    [Fact]
    public void AHandleThisSurfaceDoesNotHold_IsACALLERFaultReportedInType_NotAPlatformState()
    {
        using var surface = new Win32PointerSurface();

        var move = surface.Move(4242, new PointerBounds(0, 0, 120, 120));
        var close = surface.Close(4242);

        Assert.Equal(PointerReasonCodes.PointerTargetUnknown,
            Assert.IsType<CapabilityState.Unavailable>(move).Reason.Code);
        Assert.Equal(PointerReasonCodes.PointerTargetUnknown,
            Assert.IsType<CapabilityState.Unavailable>(close).Reason.Code);
        Assert.Equal(PointerTargetObservation.NotAsked, surface.Observe(4242));
    }

    [Fact]
    public void ASurfaceRefusesAFourthTarget_RatherThanSilentlyDroppingIt()
    {
        // Upstream's own per-window cap is three and its stated reason is that every move is a
        // SetWindowPos (Services/BubbleService.cs:26). Upstream logs and returns; this port refuses
        // in type, because a caller that keeps opening targets and never notices they stopped
        // appearing is the failure the whole contract forbids.
        using var surface = new Win32PointerSurface();
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;
        var opened = new List<int>();
        CapabilityState? fourth = null;

        for (var i = 0; i < Win32PointerSurface.MaxConcurrentTargets + 1; i++)
        {
            var bounds = new PointerBounds(
                Math.Max(0, (screenWidth / 4) + (i * 200)), Math.Max(0, screenHeight / 4), 120, 120);
            var state = surface.Open(new PointerTargetRequest(bounds, 0x00201020, 0x00E0C0FF), out var handle);
            if (i == Win32PointerSurface.MaxConcurrentTargets)
            {
                fourth = state;
            }
            else if (handle != 0)
            {
                opened.Add(handle);
            }
        }

        foreach (var handle in opened)
        {
            surface.Close(handle);
        }

        Assert.True(PointerWindowProbe.MachineHasInteractiveDesktop
            ? opened.Count == Win32PointerSurface.MaxConcurrentTargets
            : opened.Count == 0);
        Assert.True(fourth is CapabilityState.Unavailable);
        var reason = Assert.IsType<CapabilityState.Unavailable>(fourth).Reason;

        // NO SKIP: three machine classes, three honest codes, and the fourth target is refused in
        // ALL of them. Win32PointerSurface.Open checks the mechanism first, the station second and
        // the cap third, so a Windows desktop reaches the cap, a Windows session with no station
        // stops at the station, and a machine with no USER32 at all stops at the mechanism.
        Assert.Equal(
            PointerWindowProbe.MachineHasInteractiveDesktop
                ? PointerReasonCodes.PointerTargetLimitReached
                : OperatingSystem.IsWindows()
                    ? PointerReasonCodes.PointerNoInteractiveStation
                    : PointerReasonCodes.PointerMechanismAbsent,
            reason.Code);
    }

    [Fact]
    public void AMoveThatWouldRESIZEAPlacedWindowIsRefused_BecauseUpstreamsOwnDeadlockLivesThere()
    {
        // Upstream's #494: resizing a rented shell triggers HwndTarget.OnResize and a synchronous
        // CompleteRender on the UI thread (docs/primers/BUBBLE_POP_PRIMER.md §9.1). Upstream avoids
        // it by pooling windows in quantised size buckets; this port avoids it by making the extents
        // part of a target's identity.
        using var surface = new Win32PointerSurface();
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;
        var bounds = new PointerBounds(Math.Max(0, screenWidth / 4), Math.Max(0, screenHeight / 4), 120, 120);

        var open = surface.Open(new PointerTargetRequest(bounds, 0x00201020, 0x00E0C0FF), out var handle);
        var resize = surface.Move(handle, bounds with { Width = 160, Height = 160 });
        var slide = surface.Move(handle, bounds with { X = bounds.X + 10 });
        if (handle != 0)
        {
            surface.Close(handle);
        }

        Assert.True(open is CapabilityState.Available == PointerWindowProbe.MachineHasInteractiveDesktop);
        // KEYED, because the two platforms refuse at DIFFERENT RUNGS and both are honest. On Windows a
        // target really was placed, so the resize guard is what refuses. Off Windows nothing was ever
        // placed - Open refused on the absent mechanism and handed back handle 0 - so the surface is
        // asked to move a target it does not know, and pointer-target-unknown is the truthful answer.
        // Demanding the Windows code there would be demanding a refusal about a window that never existed.
        Assert.Equal(
            PointerWindowProbe.MachineHasInteractiveDesktop
                ? PointerReasonCodes.PointerTargetNotPlaced
                : PointerReasonCodes.PointerTargetUnknown,
            Assert.IsType<CapabilityState.Unavailable>(resize).Reason.Code);
        Assert.True(slide is CapabilityState.Available == PointerWindowProbe.MachineHasInteractiveDesktop,
            "a pure move at the same extents was refused, so the resize guard is refusing more than resizes");
    }

    [Theory]
    [InlineData(0, 0, 60, 59)]
    [InlineData(0, 0, 59, 60)]
    [InlineData(0, 0, 0, 0)]
    public void ATargetSmallerThanUpstreamsOwnClickableFloorIsAnEXCEPTION_NotASmallBubble(
        int x, int y, int width, int height)
    {
        // Upstream's floor is 60 DIP and its own reason is the one that matters: "a bubble is a
        // MOVING click target, and below roughly this it stops being fair to ask someone to hit it"
        // (Services/BubbleSizing.cs:70). A capability that placed a four-pixel target would satisfy
        // every OS question this file asks and be unhittable — which is a caller bug rather than a
        // platform state, so it is an exception at the boundary.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PointerTargetRequest(new PointerBounds(x, y, width, height), 0x00201020, 0x00E0C0FF));
    }

    [Fact]
    public void ATargetWhoseInkIsItsOwnFillIsAnEXCEPTION_BecauseTheContentReadBackCouldNeverTellThemApart()
    {
        Assert.Throws<ArgumentException>(() =>
            new PointerTargetRequest(new PointerBounds(0, 0, 120, 120), 0x00201020, 0x00201020));
    }

    [Fact]
    public void THEINKREADSCANSTheDISCSOWNBOX_AtAStrideDerivedFromIt_AndBothAreObservable()
    {
        // DiscBox has TWO consumers, and an earlier equivalence proof for the mutation that zeroes
        // its inset enumerated only the painter. ReadInk derives the SCAN RECTANGLE and the STRIDE
        // from the same box, and both surface through the public Observe as SampledPixels — so the
        // disc's geometry is observable from outside after all.
        //
        // The figure is a LITERAL rather than a re-derivation through the product's own DiscBox and
        // SampleStep, which would be tautological. For the run's 160 px target: inset
        // max(3+1, 160/10) = 16, box (16,16,144,144), stride (int)sqrt(128*128/400) = 6, and
        // ceil(128/6) = 22 rows of 22 columns. With the inset zeroed it is a 160-wide box at stride
        // 8, which is 20 by 20 = 400 — a different number, which is what makes this fact bite.
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(160, PointerSurfaceObservations.TargetSide);
        Assert.Equal(run.MachineHasInteractiveDesktop ? 484 : 0, run.ObservedSampledPixels);
        Assert.True(run.ObservedInkedPixels > 0 == run.MachineHasInteractiveDesktop,
            $"the capability sampled {run.ObservedSampledPixels} pixels of its own client area and found "
            + $"{run.ObservedInkedPixels} that were not the fill");
        Assert.True(run.ObservedInkedPixels <= run.ObservedSampledPixels,
            "more pixels came back inked than were ever sampled");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.ObservedBackgroundHeld);
    }

    [Fact]
    public void DIRTYINGTheFARCornerFromOutsideTurnsBACKGROUNDHELDFalse_WhichIsWhyThereAreFOUROfThem()
    {
        // The ink differential exists BECAUSE a window's device context is not guaranteed to hold
        // what the painter last drew — the capability's own remarks say an unpainted window's DC
        // "holds whatever the OS left in it", and that one control point "is satisfied by a single
        // stray pixel of the right colour". Both statements are about a DC something else touched,
        // and no reasoning from the painter's geometry can stand in for constructing that state.
        //
        // So the harness constructs it: one foreign pixel, at the FAR corner only. A check that
        // looked at the near corner alone would still say the background is held.
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.FarCornerDirtied);
        Assert.True(run.ObservedBackgroundHeld == run.MachineHasInteractiveDesktop,
            "the background was not held BEFORE the corner was dirtied, so the reading after it says nothing");
        Assert.False(run.BackgroundHeldAfterFarCornerDirtied,
            "one foreign pixel at the far corner control point did not turn BackgroundHeld false. The four "
            + "control points are then not four, and 'these pixels are not the background' is satisfied by a "
            + "window something else has scribbled on");
    }

    [Fact]
    public void APRESSNOBODYISLISTENINGFORIsCOUNTED_NeverQueuedAndNeverSilentlyDiscarded()
    {
        var run = PointerSurfaceObservations.Delivery;

        Assert.True(run.UnlistenedClickInjected == run.MachineHasInteractiveDesktop,
            "the unlistened click was not injected, so nothing below is claimed");
        Assert.True(
            run.PressesAfterUnlistenedClick - run.PressesAfterClick
                == (run.MachineHasInteractiveDesktop ? 2 : 0),
            "the operating system still delivered both halves of a click to a window with no listener "
            + "attached, and the surface did not count them");
        Assert.True(run.PressesDropped == (run.MachineHasInteractiveDesktop ? 2 : 0),
            $"the surface counted {run.PressesDropped} dropped presses. A press is an event in time and "
            + "replaying it later would be a lie about when it happened, so it is counted rather than queued — "
            + "and a press that is neither delivered nor counted is a defect nobody can see");
    }

    [Fact]
    public void ASTYLESOMETHINGELSECLEAREDIsCaughtOnTheNextMove_WhichIsWhyTheReadBackIsAReadBack()
    {
        // The capability reads WS_EX_NOACTIVATE back OUT of the operating system rather than
        // remembering that it wrote it, and this is the state that makes the difference: something
        // with the handle cleared it. Upstream met exactly this with recycled pooled shells and
        // answered it with a comment (Services/BubbleService.cs:4880-4884); here it is a typed
        // refusal with its own code.
        var run = PointerSurfaceObservations.Delivery;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.StyleWasCleared);
        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.MoveAfterStyleCleared is CapabilityState.Unavailable
                : run.MoveAfterStyleCleared is CapabilityState.Unavailable,
            $"a move on a target the OS no longer holds WS_EX_NOACTIVATE for returned "
            + $"{PointerSurfaceObservations.Describe(run.MoveAfterStyleCleared)}");
        Assert.Equal(
            run.MachineHasInteractiveDesktop
                ? PointerReasonCodes.PointerTargetStyleWrong
                // Same rung difference as the resize fact above: with no desktop nothing was placed, so
                // the surface is asked about a target it does not know rather than about a mechanism.
                : PointerReasonCodes.PointerTargetUnknown,
            Assert.IsType<CapabilityState.Unavailable>(run.MoveAfterStyleCleared).Reason.Code);
    }

    [Fact]
    public void ANotAskedObservationClaimsNOTHING_NotEvenARoutingAnswerAboutAWindowItDoesNotHave()
    {
        // 0 == 0 is the trap: an observation with no window and no hit-test winner would satisfy
        // "the winner IS the window" without the non-zero guard, and Confirmed would follow it.
        // Same shape and same lesson as the input packet's own M-l.
        var nothing = PointerTargetObservation.NotAsked;
        Assert.False(nothing.HitTestRoutesHere);
        Assert.False(nothing.Confirmed);
        Assert.False(nothing.Inked);

        var windowless = Full() with { Window = 0, HitTestWinner = 0 };
        Assert.False(windowless.HitTestRoutesHere);
        Assert.False(windowless.Confirmed);
    }

    [Fact]
    public void EveryClauseOfTheSTATIONObservationIsLoadBearing_BecauseEachOneAloneReachesNobody()
    {
        var whole = new PointerStationObservation(
            Asked: true, WindowStationVisible: true, DisplayCount: 2, DesktopReachable: true);

        Assert.True(whole.Confirmed);
        Assert.False((whole with { Asked = false }).Confirmed);
        Assert.False((whole with { WindowStationVisible = false }).Confirmed);
        Assert.False((whole with { DisplayCount = 0 }).Confirmed);
        Assert.False((whole with { DesktopReachable = false }).Confirmed);
        Assert.False(PointerStationObservation.NotAsked.Confirmed);
    }

    [Fact]
    public void ATargetsRadiusIsHalfItsSHORTERSide_BecauseThatIsTheSideAMoveCanCrossFirst()
    {
        // The race bound compares a displacement against the distance from the centre to the
        // NEAREST edge. A square target hides the distinction; an oblong one does not.
        Assert.Equal(40, new PointerBounds(0, 0, 200, 80).Radius);
        Assert.Equal(40, new PointerBounds(0, 0, 80, 200).Radius);
        Assert.Equal(60, new PointerBounds(10, 10, 120, 120).Radius);
        Assert.Equal((110, 50), new PointerBounds(10, 10, 200, 80).Centre);
    }

    [Fact]
    public void TheWindowsBranchOfTheFactoryIsTheWin32Backend_AndItIsTheOnlyOneThatCanEarnAnything()
    {
        // Selection by platform is allowed; availability by platform is not. This assertion lives
        // HERE rather than beside its Linux/macOS siblings because naming the Win32 backend makes a
        // file a real-desktop class in the census's eyes, and that census is lexical on purpose.
        using var surface = PointerSurfaceFactory.CreateFor(PointerHostPlatform.Windows);
        Assert.IsType<Win32PointerSurface>(surface);
        Assert.Equal(PointerWindowProbe.MachineHasInteractiveDesktop, surface.CanReachAPointer);
    }

    [Fact]
    public void TheSTATIONQuestionIsAskedOfTheOperatingSystem_AndNotAssumedFromThePlatform()
    {
        using var surface = new Win32PointerSurface();
        var station = surface.ObserveStation();

        Assert.Equal(PointerWindowProbe.WindowsHost, station.Asked);
        Assert.Equal(PointerWindowProbe.MachineHasInteractiveDesktop, station.Confirmed);
        Assert.Equal(station.Confirmed, surface.CanReachAPointer);
        Assert.True(!station.Asked || station.DisplayCount >= 0);
    }

    [Fact]
    public void EveryClauseOfConfirmed_IsLoadBearing_AndNoneOfThemIsInk()
    {
        // Each clause dropped one at a time, on a record built by hand. Confirmed is what Available
        // rests on, so a clause that could be false while Confirmed stayed true would be a claim
        // with nothing behind it.
        var whole = Full();

        Assert.True(whole.Confirmed);
        Assert.False((whole with { Asked = false }).Confirmed);
        Assert.False((whole with { Window = 0 }).Confirmed);
        Assert.False((whole with { WindowExists = false }).Confirmed);
        Assert.False((whole with { WindowVisible = false }).Confirmed);
        Assert.False((whole with { NonActivatingStyleHeld = false }).Confirmed);
        Assert.False((whole with { ClickThroughStyleHeld = true }).Confirmed);
        Assert.False((whole with { AboveEveryOrdinaryWindow = false }).Confirmed);
        Assert.False((whole with { HitTestWinner = 99 }).Confirmed);
        Assert.False((whole with { ForegroundAfter = 7 }).Confirmed);
        Assert.False((whole with { ForegroundBefore = 5, ForegroundAfter = 5, Window = 5 }).Confirmed);

        // INK is deliberately NOT a conjunct: an un-inked target holds everything it claimed about
        // routing and one non-fatal check failed, which is Degraded and the caller's decision.
        Assert.True((whole with { InkedPixels = 0 }).Confirmed);
        Assert.True((whole with { BackgroundHeld = false }).Confirmed);
        Assert.False((whole with { InkedPixels = 0 }).Inked);
        Assert.False((whole with { BackgroundHeld = false }).Inked);
        Assert.True(whole.Inked);
    }

    [Fact]
    public void BOTHHalvesOfTheNonActivationClaimAreLoadBearing_BecauseEitherAloneIsSatisfiedByAFailure()
    {
        var whole = Full();

        // "unchanged" alone would be satisfied by a target that was ALREADY the foreground.
        Assert.False((whole with { ForegroundBefore = 42, ForegroundAfter = 42, Window = 42 })
            .ForegroundUndisturbed);

        // "not us" alone would be satisfied by a target that stole the foreground and handed it to
        // a third window.
        Assert.False((whole with { ForegroundBefore = 3, ForegroundAfter = 9 }).ForegroundUndisturbed);

        Assert.True(whole.ForegroundUndisturbed);
    }

    /// <summary>
    /// <b>The pump drains THIS surface and nothing else on the thread.</b>
    ///
    /// <para><c>Pump</c> used to call <c>PeekMessageW(hWnd 0)</c>, which is not "my windows" — it is
    /// THE WHOLE CALLING THREAD. In the product that thread is Avalonia's UI thread:
    /// <c>CompositionRoot.cs:263</c> builds <c>SessionParticipant</c>, which builds
    /// <c>BubblePopSurfacePresenter.ForProduct</c> with a dispatch that is
    /// <c>Dispatcher.UIThread.Post</c> (<c>Session/SessionParticipant.cs:213-219</c>, <c>:269</c>),
    /// and <c>StepOnce</c> pumps from inside it (<c>Effects/BubblePopSurfacePresenter.cs:374</c>).
    /// So the invariant those two methods leaned on — "this thread owns nothing but mine" — was
    /// FALSE in the product, and false in this suite too, where
    /// <c>RealDesktopWindowFloor</c> puts a hidden window on every fact's thread.</para>
    ///
    /// <para><b>Why this is deterministic and not a timing fact.</b> Nothing is injected and nothing
    /// is waited for: one <c>WM_LBUTTONDOWN</c> is POSTED to the surface's own target and one to a
    /// window the surface does not own. Both are in their queues before the pump runs, so "who
    /// drained which" is decided entirely by the filter.</para>
    ///
    /// <para><b>Both halves are load-bearing.</b> A pump that drained nothing at all would satisfy
    /// the second clause, so the first — the surface still sees its OWN press — is asserted beside
    /// it; and a pump that merely SWALLOWED the foreign message without dispatching it would satisfy
    /// the second too, so the third clause takes that message afterwards through the probe's own
    /// unfiltered drain and proves it was still there to be taken.</para>
    /// </summary>
    [Fact]
    public void ThePumpDrainsThisSurfacesOwnTargets_AndLeavesEveryOtherWindowOnTheThreadUntouched()
    {
        var machine = PointerWindowProbe.MachineHasInteractiveDesktop;
        var (screenWidth, screenHeight) = PointerWindowProbe.PrimarySize;

        using var surface = new Win32PointerSurface();
        var bounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) - 240),
            Math.Max(0, (screenHeight / 2) + 40),
            PointerSurfaceObservations.TargetSide,
            PointerSurfaceObservations.TargetSide);

        var openState = surface.Open(new PointerTargetRequest(bounds, 0x00201020, 0x00E0C0FF), out var target);
        var ownWindow = surface.NativeHandlesFor(target).Window;

        // A window on the SAME THREAD that this surface does not own, and which counts what reaches
        // its procedure. Placed clear of the target so nothing about z-order or hit testing enters.
        using var foreign = PointerWindowProbe.ScratchTarget.Create(
            bounds.X + (PointerSurfaceObservations.TargetSide * 2),
            bounds.Y,
            PointerSurfaceObservations.TargetSide,
            PointerSurfaceObservations.TargetSide);
        var foreignWindow = foreign?.Window ?? 0;

        var ownPosted = PointerWindowProbe.PostLeftDown(ownWindow);
        var foreignPosted = PointerWindowProbe.PostLeftDown(foreignWindow);

        var dispatched = surface.Pump(64);
        var ownPresses = surface.PressesSeen;
        var foreignDuringSurfacePump = foreign?.Downs ?? 0;

        // The probe's OWN drain is deliberately unfiltered: it is the thread's real owner here, and
        // it is what proves the foreign message was left rather than eaten.
        PointerWindowProbe.Pump(64);
        var foreignAfterOwnPump = foreign?.Downs ?? 0;

        Assert.True(machine == (ownWindow != 0),
            $"the surface did not place a target to pump for: {PointerSurfaceObservations.Describe(openState)}. "
            + "Every reading below would then be about a surface with no windows, which drains nothing for "
            + "reasons that have nothing to do with the filter");
        Assert.True(machine == ownPosted && machine == foreignPosted,
            $"PostMessage placed own={ownPosted} foreign={foreignPosted}; a message that never reached a queue "
            + "cannot show who drains it");

        // ---- it STILL drains its own ----
        Assert.Equal(machine ? 1 : 0, ownPresses);
        Assert.True(machine == (dispatched > 0),
            "the surface's pump dispatched nothing at all, so 'it left the foreign window alone' is the trivial "
            + "truth about a pump that does nothing");

        // ---- and it takes nobody else's ----
        Assert.True(
            foreignDuringSurfacePump == 0,
            $"the surface's pump dispatched {foreignDuringSurfacePump} message(s) into a window it does not own. "
            + "PeekMessageW is draining the whole calling thread again, which in the product is Avalonia's UI "
            + "thread");

        // ---- and it did not silently swallow it either ----
        Assert.Equal(machine ? 1 : 0, foreignAfterOwnPump);
    }

    /// <summary>
    /// <b>The same invariant, pinned where a desktop is not required to see it break.</b>
    ///
    /// <para>The fact above is the real one, but it is machine-keyed: on Linux, and on any Windows
    /// session with no interactive desktop, no target is placed and its clauses degenerate to
    /// <c>0 == 0</c>. This one reads the two <c>Pump</c> bodies as TEXT and asserts the window
    /// argument of their <c>PeekMessageW</c> call is not the literal <c>0</c> — the exact character
    /// that turns "drain my windows" into "drain the whole calling thread". It holds on every
    /// platform, in every session, and it is the thing that was missing when a single added hidden
    /// window changed what these methods swallowed and nobody had asserted the difference.</para>
    ///
    /// <para><b><c>Win32InputPresence</c> is here even though the product does not call its pump</b>
    /// (<c>IInputPresence.Pump</c> says so, and it is true — there is no call site under
    /// <c>client/src</c>). The suite calls it, with the harness's own floor window and live scratch
    /// rigs on the same thread, so the breadth was never harmless: it is what made this method's
    /// disposal fact read 2 where it asserts 0. Same defect, same pin.</para>
    ///
    /// <para><b>Honesty.</b> This is LEXICAL. It cannot see a filter that is a variable holding
    /// zero, and it says nothing about whether the handle chosen is the right one — that is what the
    /// fact above measures against a real window manager.</para>
    /// </summary>
    [Fact]
    public void NeitherPumpFiltersOnHwndZero_WhichWouldDrainTheWholeCallingThreadAgain()
    {
        // The call token is interop-QUALIFIED so the scan lands on the call and not on the prose
        // above it, which names the bare API on purpose.
        (string[] Parts, string Signature, string Call)[] pumps =
        [
            (["client", "src", "CcpClient.Desktop", "Pointer", "Win32PointerSurface.cs"],
                "public int Pump(int max)", "Win32PointerInterop.PeekMessageW("),
            (["client", "src", "CcpClient.Desktop", "Input", "Win32InputPresence.cs"],
                "public int Pump(int maxMessages)", "Win32InputInterop.PeekMessageW("),
        ];

        // TOP-LEVEL, before the loop: BOTH pumps are covered, and an empty or trimmed table is a
        // red rather than a fact that quietly asserts nothing (the vacuous-shape class).
        Assert.Equal(2, pumps.Length);

        var root = FindRepoRoot();
        foreach (var (parts, signature, call) in pumps)
        {
            var path = Path.Combine([root, .. parts]);
            var source = File.ReadAllText(path);

            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0,
                $"{string.Join('/', parts)} no longer declares '{signature}', so this guard is reading nothing "
                + "and the invariant it pins is unpinned. Retarget it rather than deleting it");

            var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
            Assert.True(end > start, $"could not find the end of {signature} in {string.Join('/', parts)}");
            var body = source[start..end];

            var callAt = body.IndexOf(call, StringComparison.Ordinal);
            Assert.True(callAt >= 0,
                $"{signature} in {string.Join('/', parts)} no longer calls {call}); this guard is a detector "
                + "of nothing until it is retargeted at whatever drains the queue now");

            var argsFrom = callAt + call.Length;
            var argsTo = body.IndexOf(')', argsFrom);
            var args = body[argsFrom..argsTo].Split(',');

            Assert.True(args.Length == 5,
                $"PeekMessageW in {string.Join('/', parts)} was called with {args.Length} arguments, not 5; the "
                + "window argument is no longer the second one and this guard would be reading the wrong thing");
            Assert.True(args[1].Trim() != "0",
                $"{signature} in {string.Join('/', parts)} passes hWnd 0 to PeekMessageW. That is not 'this "
                + "object's windows' — it removes, translates and dispatches EVERY message queued for ANY window "
                + "on the calling thread. In the product that thread is Avalonia's UI thread "
                + "(CompositionRoot.cs:263 -> SessionParticipant.cs:269 -> BubblePopSurfacePresenter.cs:374, "
                + "dispatched through Dispatcher.UIThread.Post), and in this suite it is a thread carrying the "
                + "harness's floor window and whatever scratch rigs a fact has up");
        }
    }

    private static string FindRepoRoot()
    {
        string[] anchor = ["client", "CcpClient.sln"];
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. anchor])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} (anchor: client/CcpClient.sln) — "
            + "the pump-filter guard refuses to skip");
    }

    private static PointerTargetObservation Full() => new(
        Asked: true,
        Window: 11,
        WindowExists: true,
        WindowVisible: true,
        HeldBounds: new PointerBounds(10, 10, 120, 120),
        NonActivatingStyleHeld: true,
        ClickThroughStyleHeld: false,
        AboveEveryOrdinaryWindow: true,
        HitTestWinner: 11,
        HitTestPoint: (70, 70),
        ForegroundBefore: 3,
        ForegroundAfter: 3,
        BackgroundHeld: true,
        InkedPixels: 12,
        SampledPixels: 400);
}
