using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>Two <c>overlay-clickthrough</c> safety invariants that the port asserted implicitly and had
/// never measured.</b>
///
/// <para><i>"Passive regions allow normal desktop click, type, drag, and scroll"</i>
/// (<c>.claude/skills/overlay-clickthrough/SKILL.md:26</c>) — only the CLICK quarter had evidence.
/// <i>"A handled overlay click does not unintentionally activate/click the underlying application"</i>
/// (<c>:28</c>) — nothing looked for that leak at all.</para>
///
/// <para><b>They are one run because they are each other's differential.</b> With click-through set,
/// all four channels must reach the window underneath; with it cleared, the identical click at the
/// identical point must reach nothing AND must leave the foreground where it was; with it set again,
/// that same click must both arrive and activate. The last leg is what makes the middle one a
/// measurement rather than a coincidence.</para>
///
/// <para><b>WINDOWS EVIDENCE.</b> Every reading is <c>user32.dll</c>'s — the window manager's hit
/// test, <c>SendInput</c>, <c>GetForegroundWindow</c>, and a real window procedure's message counts.
/// The X11 and Wayland halves of both invariants remain entirely unmeasured, and a green run here
/// says nothing about them.</para>
///
/// <para><b>THE ONE PRECONDITION THESE FACTS REFUSE ON, and it is a machine class rather than a
/// quarantine.</b> A window owned by ANOTHER PROCESS can hold a point of the drag path against every
/// re-assertion this run is able to make — a system shell surface did it here, named from a TRX:
/// <c>the window manager gives (1453,814) to 0x10420 (class "Windows.UI.Core.CoreWindow") even with
/// this run's own stack re-asserted over it</c>. The lease cannot exclude that (it is not this
/// port's harness) and the drag's own hold cannot win it (it is not a race this port can win). So
/// every leg's whole path is walked BEFORE anything is injected
/// (<see cref="PointerWindowProbe.HoldWholeDragPath"/>) and the four drag facts refuse by name
/// instead of reporting the desktop's contention as a broken drag channel. <b>It refuses on nothing
/// else</b>: a point held by a window of OURS is not this condition and still fails, and both
/// directions of that rule are measured against a real second process by
/// <see cref="AForeignWindowOverTheDragPathIsREFUSEDByName_AndAPathInsideOurOwnWindowIsNOTRefused"/>.
/// The refusal reports as <c>NotExecuted</c> carrying its reason, so each of the four names must be
/// pinned in <c>client/tests/floor/floor.json</c>'s <c>allowedSkips</c> under its admission
/// rule.</para>
///
/// <para><b>What a green run still does not prove.</b> That a human sees anything: composition,
/// rendering and the pointer's visible position are headed claims
/// (<c>client/docs/verification-harness.md</c>). And that a DISPLAY transition restores desktop
/// input — the other half of <c>SKILL.md:31</c> — which this suite cannot reach at all: nothing in
/// <c>client/src</c> subscribes to a display-change notification, and the only in-process way to
/// change the topology is to reconfigure the interactive user's real display.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayDesktopInputTests : RealDesktopFacts
{
    private static OverlayDesktopInputObservations.PassiveChannelRun Run =>
        OverlayDesktopInputObservations.PassiveChannels;

    /// <summary>
    /// <b>The vacuous case, closed first.</b> Every "the input arrived" below would be unreachable,
    /// and every "the input did not arrive" trivially true, on a machine where synthesised input goes
    /// nowhere — a locked workstation, the secure desktop, a session behind UIPI, or a build box with
    /// no interactive desktop at all. So the rig is read with NO overlay in existence: the window
    /// underneath holds the foreground and the keyboard, owns its own point, and receives a real
    /// click, a real drag, a real wheel notch and a real keystroke.
    /// </summary>
    [Fact]
    public void TheDesktopUnderThisPointTakesAllFourChannels_OrEveryReadingBelowIsATestOfNothingHappening()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;
        var baseline = run.BaselinePass;

        // THE MACHINE-CLASS REFUSAL, taken before any reading is judged. See the class remarks: a
        // window of ANOTHER PROCESS holding a point of this leg's drag path is a property of the
        // desktop that no in-process mechanism can take back, and the pre-flight that detects it ran
        // before this leg injected anything. It names the window; it is silent on a clean desktop.
        var foreign = OverlayDesktopInputObservations.ForeignHoldOnTheDragPath(baseline);
        Assert.SkipWhen(foreign.Contended, foreign.Refusal);

        Assert.True(run.UnderneathIsUp == expected,
            $"the probe's own counting window is not on the desktop at {OverlayDesktopInputObservations.UnderneathBounds}. "
            + "Every channel reading in this file would then be a statement about a window nothing could reach");
        Assert.True(run.KeeperIsUp == expected,
            $"the foreground keeper is not on the desktop at {OverlayDesktopInputObservations.KeeperBounds}, so the "
            + "handled-click leg has nowhere to leave the foreground and its 'the foreground did not move' would be "
            + "a statement about no foreground at all");

        Assert.True(run.UnderneathTookForegroundFirst == expected,
            "the window underneath could not take the foreground and the keyboard on this machine, so the TYPE "
            + "channel below is being asked of a window the OS would never have typed into anyway");

        Assert.True(PointerWindowProbe.SameWindow(baseline.Routed, run.UnderneathWindow) == expected,
            $"before any overlay existed the window manager routed the point to "
            + $"{PointerWindowProbe.DescribeWindow(baseline.Routed)} instead of the window underneath "
            + $"{PointerWindowProbe.DescribeWindow(run.UnderneathWindow)}. Something foreign owns this point and no "
            + "input may be injected at it");

        Assert.True(baseline.EveryInjectionAccepted == expected,
            "the OS refused one of the four injections — a locked workstation, the secure desktop, or UIPI. The "
            + $"instrument cannot inject, so it cannot measure delivery and nothing in this file proves anything. {baseline.Counts}");

        // Four separate legs, because four separate routing rules decide them. A click is hit-tested;
        // a drag is a WM_MOUSEMOVE carrying MK_LBUTTON; a wheel notch goes to the focus window or to
        // the window under the pointer depending on a per-user setting; a keystroke follows the
        // foreground thread's focus. An overlay could pass every click through and still hold the
        // keyboard, so none of these is a corollary of another.
        Assert.True(baseline.Downs > 0 == expected, $"the CLICK channel delivered nothing. {baseline.Counts}");
        Assert.True(baseline.DragMoves > 0 == expected,
            $"the DRAG channel delivered no WM_MOUSEMOVE carrying MK_LBUTTON. {baseline.Counts}");
        Assert.True(baseline.Wheel > 0 == expected, $"the SCROLL channel delivered no wheel notch. {baseline.Counts}");
        Assert.True(baseline.KeyDowns > 0 == expected, $"the TYPE channel delivered no WM_KEYDOWN. {baseline.Counts}");
    }

    /// <summary>
    /// <b>The drag instrument's own control, and the guard on the defect that made the four facts in
    /// this file unexplainable for a day.</b>
    ///
    /// <para>The four facts above each inject a drag over NINE points and, until this was measured,
    /// the harness had asked the window manager about exactly ONE of them — the press point. A
    /// second window over the PATH and not over the press point takes every move and leaves every
    /// click, because a button message is posted to the queue of the window under the cursor when
    /// the event is injected while a <c>WM_MOUSEMOVE</c> is synthesised at peek time for whatever
    /// owns the cursor's point then. The reading that produces — <c>downs=2 dragMoves=0 moves=2</c>
    /// with every injection accepted — is indistinguishable from a machine that cannot inject at
    /// all, and it cost this board three wrong diagnoses.</para>
    ///
    /// <para>So this fact builds that state deliberately, proves it built it, and then requires the
    /// drag to arrive anyway — which it does only because
    /// <c>PointerWindowProbe.InjectDragAt</c> now holds each of its own points. Delete the hold and
    /// this reds, naming the window that took the path.</para>
    ///
    /// <para>The contender is a window of THIS process, not a foreign one: a foreign topmost window
    /// is what really did this and no fact may depend on one existing
    /// (<see cref="RealDesktopCollection"/> says it can never be excluded). What is under test here
    /// is the Z-ORDER, and a window of ours reproduces that part exactly and deterministically.</para>
    /// </summary>
    [Fact]
    public void ADragHoldsItsOwnPath_OrAWindowOverPartOfTheRectangleTakesEveryMoveAndLeavesTheClicks()
    {
        var run = OverlayDesktopInputObservations.HeldPath;
        var expected = run.MachineHasInteractiveDesktop;

        Assert.True(run.TargetIsUp == expected,
            $"the drag target is not on the desktop at {OverlayDesktopInputObservations.ContendedBounds}. {run.Trace}");
        Assert.True(run.ContenderIsUp == expected,
            $"the contending window is not on the desktop, so nothing is over the drag path and this fact is a "
            + $"drag with nothing in the way. {run.Trace}");

        // Measured, not assumed: with the foreground elsewhere the drag's own button-down ACTIVATES
        // the target, activation raises it over the contender, and the whole construction below
        // dissolves — the first draft of this fact passed with the hold deleted for that reason.
        Assert.True(run.TargetTookForeground == expected,
            "the drag target could not take the foreground, so its own button-down would activate and raise it "
            + $"over the contender and this fact would be a drag with nothing in the way. {run.Trace}");

        // The two anti-vacuity clauses, and they are the whole construction: the press point must be
        // the target's and the path must NOT be, or the asymmetry under test does not exist.
        Assert.True(PointerWindowProbe.SameWindow(run.OwnerOfPressPoint, run.Target) == expected,
            $"the window manager gives the press point to {PointerWindowProbe.DescribeWindow(run.OwnerOfPressPoint)} "
            + $"rather than to the drag target, so the press below lands somewhere else. {run.Trace}");
        Assert.True(PointerWindowProbe.SameWindow(run.OwnerOfFirstStep, run.Contender) == expected,
            $"the window manager gives the drag's first step to "
            + $"{PointerWindowProbe.DescribeWindow(run.OwnerOfFirstStep)} rather than to the contender, so the path "
            + $"was never contested and the drag below had nothing to hold against. {run.Trace}");

        Assert.True(run.Drag.Accepted == expected, $"the OS refused an event of the drag. {run.Trace}");

        // The half that always worked, asserted so a reader never again mistakes this shape for an
        // injection that does not reach the desktop at all.
        Assert.True(run.Downs > run.DownsBefore == expected,
            $"the drag's PRESS did not reach the target, so this run is about a broken injection rather than about a "
            + $"contested path. {run.Trace}");

        // And the half that only arrives because the drag holds its own points.
        Assert.True(run.Drag.Delivered == (expected ? run.Drag.Steps : 0),
            $"the drag did not hold its path against a window this run put over it. {run.Trace}");
        Assert.True(run.DragMoves > 0 == expected,
            $"no WM_MOUSEMOVE carrying MK_LBUTTON reached the target. {run.Trace}");
    }

    /// <summary>
    /// <b>THE REFUSAL'S OWN CONTROL, IN BOTH DIRECTIONS, IN ONE RUN ON ONE DESKTOP.</b>
    ///
    /// <para>Four facts in this file now refuse when a window of ANOTHER PROCESS holds a point of
    /// the drag path (<see cref="PointerWindowProbe.HoldWholeDragPath"/>), because a system shell
    /// surface can hold one against every re-assertion this run is able to make — measured, and
    /// captured from a TRX rather than argued: <c>drag path 0/8 steps delivered — the first that did
    /// not arrive was aimed at (1453,814), which the window manager gives to 0x10420 (class
    /// "Windows.UI.Core.CoreWindow") even with this run's own stack re-asserted over it</c>.</para>
    ///
    /// <para><b>A precondition is only worth having if BOTH of its answers are measured.</b> One
    /// that never fires leaves the intermittent exactly where it was; one that fires readily deletes
    /// four facts and nobody finds out, which is the vacuous green this port keeps paying for. So a
    /// real second process places a real top-most window beside ours, and the SAME walk is taken
    /// twice with only the travel differing: across the foreign window it must refuse AND name that
    /// window's exact handle, and inside our own window it must not refuse — having walked every
    /// point to be able to say so.</para>
    ///
    /// <para><b>The index is asserted, and it is the finding in one number.</b> The press point and
    /// the first three steps are ours; the refusal comes from point
    /// <see cref="OverlayDesktopInputObservations.FirstCrossedPoint"/>. A pre-flight that only asked
    /// about the press point — which is all the callers ever did before this — would have passed
    /// this rig and let the drag run into somebody else's window.</para>
    ///
    /// <para><b>What it does NOT establish.</b> That the refusal fires against an OCCLUDING foreign
    /// window: two overlapping top-most windows are a race no user-mode process can win
    /// deterministically, and <see cref="OverlayDesktopInputObservations.InterloperBounds"/> states
    /// that limit rather than hiding it. The rule under test is "a foreign window owns a point of
    /// the path", and that is exactly what is constructed here. Windows only: <c>WindowFromPoint</c>
    /// and the top-most band have no X11 or Wayland counterpart, and every reading flips to the
    /// no-desktop shape there.</para>
    /// </summary>
    [Fact]
    public void AForeignWindowOverTheDragPathIsREFUSEDByName_AndAPathInsideOurOwnWindowIsNOTRefused()
    {
        var run = OverlayDesktopInputObservations.ForeignHold;
        var expected = run.MachineHasInteractiveDesktop;

        // ---- the rig, proved before either direction is read ----
        Assert.True(run.OursIsUp == expected,
            $"this run's own window is not on the desktop at {OverlayDesktopInputObservations.PreflightBounds}, so "
            + $"the press point below belongs to nobody. {run.Trace}");
        Assert.True(run.InterloperIsUp == expected,
            $"the child process put no window on the desktop at {OverlayDesktopInputObservations.InterloperBounds}, "
            + $"so there is nothing foreign for the pre-flight to refuse and both directions below are empty. "
            + $"{run.Trace}");
        Assert.True((run.InterloperProcess != 0 && run.InterloperProcess != Environment.ProcessId) == expected,
            "the interloper is not owned by ANOTHER process, and the pre-flight refuses on nothing else — a window "
            + $"of ours can never satisfy it, which is the whole reason it cannot become an escape hatch. "
            + $"{run.Trace}");
        Assert.True(PointerWindowProbe.SameWindow(run.OwnerOfInterloperCentre, run.Interloper) == expected,
            $"the window manager gives the interloper's own centre to "
            + $"{PointerWindowProbe.DescribeWindow(run.OwnerOfInterloperCentre)} rather than to the interloper, so "
            + $"the refusal below would be about some third window that happened to be there. {run.Trace}");

        // THE ANTI-VACUITY CLAUSE OF THE WHOLE FACT: the press point must be OURS. Without it the
        // refusal below could be earned by the very first point and would say nothing about a PATH.
        Assert.True(PointerWindowProbe.SameWindow(run.OwnerOfPressPoint, run.Ours) == expected,
            $"the window manager gives the press point to {PointerWindowProbe.DescribeWindow(run.OwnerOfPressPoint)} "
            + $"rather than to this run's own window, so the crossing path below starts foreign and its refusal "
            + $"would prove nothing about the path. {run.Trace}");

        // ---- direction one: it REFUSES, and it names the window ----
        Assert.True(run.AcrossTheInterloper.Contended == expected,
            "the pre-flight did NOT refuse a drag path that crosses a window owned by another process. The four "
            + $"drag facts in this file would then run their drag into that window and report the machine's "
            + $"contention as a broken drag channel, which is the intermittent this was built to replace. "
            + $"{run.Trace}");
        Assert.True(PointerWindowProbe.SameWindow(run.AcrossTheInterloper.Owner, run.Interloper) == expected,
            $"the pre-flight refused, but it names {PointerWindowProbe.DescribeWindow(run.AcrossTheInterloper.Owner)} "
            + $"instead of the interloper {PointerWindowProbe.DescribeWindow(run.Interloper)}. A refusal that names "
            + $"the wrong window is worse than none: it sends the next reader after the wrong process. {run.Trace}");
        Assert.True(
            run.AcrossTheInterloper.Index == (expected ? OverlayDesktopInputObservations.FirstCrossedPoint : -1),
            $"the refusal came from point {run.AcrossTheInterloper.Index} of the path where the geometry puts the "
            + $"first crossing at {OverlayDesktopInputObservations.FirstCrossedPoint}. Point 0 is the PRESS point, "
            + "so an index of 0 would mean this fact never measured a path at all — which is exactly the pre-flight "
            + $"a press-point-only check would have been. {run.Trace}");
        Assert.True(
            run.AcrossTheInterloper.Refusal.Contains($"0x{run.Interloper:X}", StringComparison.Ordinal) == expected,
            "the refusal text a skipped run reports does not contain the offending window's handle, so the reader "
            + $"gets a skip with no name in it — the thing this whole mechanism exists to deliver. It said: "
            + $"{run.AcrossTheInterloper.Refusal}");

        // ---- direction two: it does NOT refuse, and it walked the whole path to say so ----
        Assert.False(run.InsideOurOwnWindow.Contended,
            "the pre-flight refused an ordinary drag path wholly inside this run's OWN window, on the same desktop "
            + "moments after the crossing path was walked. A refusal that fires when the desktop is fine deletes "
            + $"four facts and nobody ever finds out. {run.Trace}");
        Assert.True(
            run.InsideOurOwnWindow.Walked == (expected ? OverlayDesktopInputObservations.DragPathPointCount : 0),
            $"the pre-flight answered CLEAR after walking {run.InsideOurOwnWindow.Walked} of "
            + $"{OverlayDesktopInputObservations.DragPathPointCount} points. A walk that stopped early and a path "
            + "that is genuinely clear must never read the same, or the refusal quietly stops looking at most of "
            + $"the path. {run.Trace}");
    }

    /// <summary>
    /// <b>THE INVARIANT THE ROW WAS OPENED FOR.</b> With a click-through overlay covering the point,
    /// the desktop underneath still takes a CLICK, a DRAG, a SCROLL and a keystroke it can TYPE.
    ///
    /// <para>Three of those four are new evidence. The overlay capability's own confirmation
    /// (<c>Win32OverlayPresence.ConfirmInputRouting</c>) asks the window manager whether the point
    /// routes PAST the surface, which means only "not to us" — it does not say the point reaches the
    /// window the user was aiming at, and it asks nothing whatever about the keyboard, the wheel or a
    /// drag.</para>
    ///
    /// <para><b>The z-order clause is the anti-vacuity half and is not decoration.</b> Both windows
    /// are in the topmost band, so the capability's own "above every ordinary window" does not order
    /// this pair — and an overlay that had slipped BELOW the window underneath would pass every
    /// channel for the one reason that proves nothing.</para>
    /// </summary>
    [Fact]
    public void AClickThroughOverlayPassesCLICKDRAGSCROLLandTYPE_NotOnlyTheClick()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;
        var before = run.BaselinePass;
        var during = run.PassThroughPass;

        // BOTH legs, because every count below is a comparison between them: a leg whose drag was
        // never driven contaminates the side of the comparison it sits on.
        var foreign = OverlayDesktopInputObservations.ForeignHoldOnTheDragPath(before, during);
        Assert.SkipWhen(foreign.Contended, foreign.Refusal);

        Assert.True(
            expected ? run.PresentState is CapabilityState.Available : run.PresentState is CapabilityState.Unavailable,
            "the overlay has to be genuinely up over the window underneath before its input policy means anything, "
            + $"and presenting it answered {PointerSurfaceObservations.Describe(run.PresentState)}");

        Assert.True((run.OverlayIndexWhilePassing >= 0
                && run.UnderneathIndexWhilePassing > run.OverlayIndexWhilePassing) == expected,
            $"the OS's own top-level z-order puts the overlay at {run.OverlayIndexWhilePassing} and the window "
            + $"underneath at {run.UnderneathIndexWhilePassing}, so the overlay was not above it when the pass-through "
            + "was read. Every channel below would then have reached the desktop because nothing was in the way");

        Assert.True(PointerWindowProbe.SameWindow(during.Routed, run.UnderneathWindow) == expected,
            $"with the click-through overlay up, the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(during.Routed)} rather than to the window underneath "
            + $"{PointerWindowProbe.DescribeWindow(run.UnderneathWindow)}");

        Assert.True(during.EveryInjectionAccepted == expected,
            $"the OS refused one of the four injections on the pass-through leg. {run.Trace}");

        // Compared against the BASELINE counts rather than against literals: a literal expectation
        // here would be a constant asserted against input driven by the same constant.
        Assert.True(during.Downs > before.Downs == expected,
            $"a CLICK inside a passive region did not reach the desktop underneath. {run.Trace}");
        Assert.True(during.DragMoves > before.DragMoves == expected,
            $"a DRAG inside a passive region did not reach the desktop underneath — no WM_MOUSEMOVE carrying "
            + $"MK_LBUTTON arrived. This is the channel a user drags a window or selects text with. {run.Trace}");
        Assert.True(during.Wheel > before.Wheel == expected,
            $"a SCROLL inside a passive region did not reach the desktop underneath. {run.Trace}");
        Assert.True(during.KeyDowns > before.KeyDowns == expected,
            "a KEYSTROKE did not reach the desktop underneath while a click-through overlay was up. A keystroke is "
            + "FOCUS-routed rather than hit-tested, so this does not follow from the click passing through: an "
            + $"overlay that took the keyboard would pass every click and still trap the user. {run.Trace}");

        // The other half of "overlays do not unexpectedly activate or steal focus" (SKILL.md:29),
        // measured at the moment it matters rather than at the moment of presentation.
        Assert.True(PointerWindowProbe.SameWindow(during.Foreground, run.UnderneathWindow) == expected,
            $"presenting the overlay moved the foreground away from the window underneath, to "
            + $"{PointerWindowProbe.DescribeWindow(during.Foreground)}. {run.Trace}");
    }

    /// <summary>
    /// <b>THE OTHER INVARIANT THE ROW WAS OPENED FOR, and the one nothing looked for.</b> With
    /// click-through cleared the overlay catches its own click — WPF's clickable-flash arm, which
    /// consumes <c>MouseLeftButtonDown</c> in its own handler over a <c>WS_EX_NOACTIVATE</c>,
    /// <c>ShowActivated = false</c> window (<c>Services/Flash/FlashService.cs:3632-3636</c>,
    /// <c>:3619-3620</c>, <c>:3662-3673</c>). None of that press may leak downwards.
    ///
    /// <para><b>Three readings, because a leak has three different shapes.</b> The message could be
    /// DELIVERED to the window underneath; the window underneath could be ACTIVATED without receiving
    /// the message (activation and delivery are separate OS decisions, and a <c>WM_MOUSEACTIVATE</c>
    /// answer decides them independently); or the FOREGROUND could move, which is what the user
    /// actually experiences as "my other app came forward and ate my typing". The keystroke after the
    /// click is that third reading taken as an outcome rather than as a state: it must still land
    /// where the user's keystrokes were already landing.</para>
    ///
    /// <para><b>The foreground is deliberately parked on a THIRD window first.</b> If the window
    /// underneath already held it, "the click did not activate it" would be unmeasurable — and the
    /// restore leg is what proves the identical click at the identical point CAN activate it.</para>
    /// </summary>
    [Fact]
    public void AHandledOverlayClickDoesNotLeakThrough_AndDoesNotActivateTheApplicationUnderneath()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;
        var during = run.PassThroughPass;

        Assert.True(run.KeeperTookForeground == expected,
            "the keeper could not take the foreground and the keyboard, so 'the handled click did not move the "
            + $"foreground' would be a statement about a foreground nobody held. {run.Trace}");

        // The rig's own correctness, and it was NOT obvious: Windows scopes the foreground to a
        // thread's input queue, so a keeper sharing the overlay's thread makes "the foreground
        // moved" a reading of that one queue's active window rather than of WS_EX_NOACTIVATE.
        // Measured while this file was written — same-thread keeper, the foreground moved to the
        // overlay; own-thread keeper, it did not — so this clause is what keeps the leg from
        // reporting the harness as a product defect.
        Assert.True(run.KeeperOnItsOwnInputQueue == expected,
            "the keeper and the overlay are on the SAME OS thread, so they share one input queue and the "
            + "foreground reading below would be about that queue's active window rather than about the overlay's "
            + $"WS_EX_NOACTIVATE. {run.Trace}");

        Assert.True(
            expected
                ? run.HandledState is CapabilityState.Available
                : run.HandledState is CapabilityState.Unavailable,
            $"clearing click-through answered {PointerSurfaceObservations.Describe(run.HandledState)}, so the overlay "
            + "was never in the handled polarity this fact is about");

        Assert.True(PointerWindowProbe.SameWindow(run.HandledRouted, run.OverlayWindow) == expected,
            $"with click-through cleared the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(run.HandledRouted)} rather than to the overlay "
            + $"{PointerWindowProbe.DescribeWindow(run.OverlayWindow)}, so the click below never landed on a handled "
            + "overlay at all");

        // Read immediately before the click as well as after it, so "it did not move" cannot be
        // satisfied by a foreground that moved away during the style flip and happened to come back.
        Assert.True(PointerWindowProbe.SameWindow(run.ForegroundBeforeHandledClick, run.KeeperWindow) == expected,
            $"the foreground had already left the keeper before the handled click, for "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundBeforeHandledClick)}, so the reading after the "
            + $"click cannot be attributed to the click. {run.Trace}");

        Assert.True(run.HandledClickAccepted == expected,
            "the handled click was not injected, so 'nothing leaked through' is a statement about a click that never "
            + $"happened. {run.Trace}");
        Assert.True(run.HandledKeyAccepted == expected,
            $"the keystroke after the handled click was not injected. {run.Trace}");

        // Leak shape 1: the message itself. Compared against the pass-through leg's count, which is
        // non-zero and was earned by the same injection at the same point.
        Assert.Equal(during.Downs, run.DownsAfterHandled);

        // Leak shape 2: activation without delivery.
        Assert.True(run.ActivationsAfterHandled == (expected ? during.Activations : 0),
            $"the application underneath was ACTIVATED by a click the overlay handled: its activation count went "
            + $"from {during.Activations} to {run.ActivationsAfterHandled}. The message may never have reached it — "
            + $"activation is a separate decision — and the user still lost whatever they were doing. {run.Trace}");

        // Leak shape 3: the foreground, and what that costs the user, read as an outcome.
        Assert.True(PointerWindowProbe.SameWindow(run.ForegroundAfterHandled, run.KeeperWindow) == expected,
            $"the handled overlay click moved the foreground to "
            + $"{PointerWindowProbe.DescribeWindow(run.ForegroundAfterHandled)} instead of leaving it on the keeper "
            + $"{PointerWindowProbe.DescribeWindow(run.KeeperWindow)}. {run.Trace}");
        Assert.Equal(during.KeyDowns, run.UnderneathKeysAfterHandled);
        Assert.True(run.KeeperKeysAfterHandled > during.KeeperKeyDowns == expected,
            "the keystroke after the handled click reached NEITHER window, so 'the typing still goes where it went "
            + $"before' is an absence rather than a measurement. {run.Trace}");
    }

    /// <summary>
    /// <b>The control that makes the leak fact a measurement.</b> Click-through is set again and the
    /// identical click is injected at the identical point, with the flag as the only difference. It
    /// now reaches the window underneath, ACTIVATES it, and takes the foreground off the keeper — so
    /// each of the three absences above is a property of the handled overlay and not of this rig.
    ///
    /// <para>It is also invariant (a) measured a second time, after a live polarity flip on a window
    /// that has already been shown. That is exactly the case WPF says it must handle, and says so
    /// because it got it wrong: a recycled shell carries the previous life's bits, so the style is
    /// rewritten on the LIVE hwnd every spawn (<c>Services/Flash/FlashService.cs:3662-3673</c>,
    /// <c>Services/BubbleService.cs:4881-4896</c>).</para>
    /// </summary>
    [Fact]
    public void RestoringClickThroughGivesAllFourChannelsBack_AndTheSameClickThenDOESActivateWhatIsUnderneath()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;
        var during = run.PassThroughPass;
        var restored = run.RestoredPass;

        // Both legs this fact compares, and only those two: a foreign window during the teardown
        // leg has nothing to do with this reading, and refusing more widely than the evidence
        // requires is how a precondition turns into an escape hatch.
        var foreign = OverlayDesktopInputObservations.ForeignHoldOnTheDragPath(during, restored);
        Assert.SkipWhen(foreign.Contended, foreign.Refusal);

        Assert.True(
            expected
                ? run.RestoreState is CapabilityState.Available
                : run.RestoreState is CapabilityState.Unavailable,
            $"restoring click-through answered {PointerSurfaceObservations.Describe(run.RestoreState)}");

        Assert.True(PointerWindowProbe.SameWindow(restored.Routed, run.UnderneathWindow) == expected,
            $"after the flip the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(restored.Routed)} rather than back to the window underneath");

        Assert.True(restored.EveryInjectionAccepted == expected,
            $"the OS refused one of the four injections on the restore leg. {run.Trace}");

        Assert.True(restored.Downs > run.DownsAfterHandled == expected,
            $"the CLICK channel did not come back after click-through was restored. {run.Trace}");
        Assert.True(restored.DragMoves > during.DragMoves == expected,
            $"the DRAG channel did not come back. {run.Trace}");
        Assert.True(restored.Wheel > during.Wheel == expected, $"the SCROLL channel did not come back. {run.Trace}");
        Assert.True(restored.KeyDowns > run.UnderneathKeysAfterHandled == expected,
            $"the TYPE channel did not come back. {run.Trace}");

        // THE CONTROL. Without these two the leak fact above is satisfied by a click that could never
        // have activated anything, which is the vacuous shape this whole file is built to refuse.
        Assert.True(restored.Activations > run.ActivationsAfterHandled == expected,
            "a click that passed THROUGH the overlay did not activate the application underneath either, so the "
            + "handled leg's 'it was not activated' proves nothing: this rig cannot activate that window at all. "
            + $"{run.Trace}");
        Assert.True(PointerWindowProbe.SameWindow(restored.Foreground, run.UnderneathWindow) == expected,
            $"a click that passed through the overlay left the foreground on "
            + $"{PointerWindowProbe.DescribeWindow(restored.Foreground)} rather than moving it to the window "
            + $"underneath, so the handled leg's 'the foreground did not move' proves nothing. {run.Trace}");
    }

    /// <summary>
    /// <b>The teardown half of <i>"Teardown and display/window transitions restore normal desktop
    /// input"</i> (<c>SKILL.md:31</c>), widened from one channel to four.</b>
    ///
    /// <para>The landed teardown evidence — <see cref="SurfaceTeardownTests"/> and
    /// <see cref="SurfaceExitTests"/> — reads the window manager's hit test and the foreground after
    /// the surfaces are gone. Both are the right questions and neither is the whole sentence:
    /// "normal desktop input" is four channels, and a withdrawn surface that had somehow retained the
    /// keyboard or the wheel would satisfy every landed reading.</para>
    ///
    /// <para><b>The DISPLAY half of that same sentence is NOT closed by this fact and is not closable
    /// here.</b> Nothing in <c>client/src</c> subscribes to a display-change notification; this
    /// machine has one monitor; and the only in-process API that changes the topology is
    /// <c>ChangeDisplaySettingsEx</c> against the interactive user's real desktop, which this suite
    /// will not call. It remains open with that reason.</para>
    /// </summary>
    [Fact]
    public void WithdrawingTheOverlayRestoresAllFourInputChannels_NotOnlyTheHitTest()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;
        var restored = run.RestoredPass;
        var after = run.WithdrawnPass;

        var foreign = OverlayDesktopInputObservations.ForeignHoldOnTheDragPath(restored, after);
        Assert.SkipWhen(foreign.Contended, foreign.Refusal);

        Assert.True(
            expected
                ? run.WithdrawState is CapabilityState.Available
                : run.WithdrawState is CapabilityState.Unavailable,
            $"taking the overlay down answered {PointerSurfaceObservations.Describe(run.WithdrawState)}");

        // Asked of the PROBE rather than of the capability, and it is not decoration: the routing
        // question below is asked after the window underneath has been raised to the top of the
        // topmost band (which it must be, to beat the foreign contention this collection exists to
        // absorb), and that raise would have won the point back over a surface that was still up. So
        // "the surface is genuinely gone" is asserted here, separately — the shape SKILL.md:30
        // forbids is an INVISIBLE input-blocking surface, and nothing about a hidden window
        // guarantees it stopped owning its point.
        Assert.False(run.OverlayVisibleAfterWithdraw && expected,
            $"the OS still reports the overlay visible after Withdraw returned "
            + $"{PointerSurfaceObservations.Describe(run.WithdrawState)}. {run.Trace}");
        Assert.True(run.OverlayZIndexAfterWithdraw < 0 || !expected,
            $"the overlay is still at position {run.OverlayZIndexAfterWithdraw} of the OS's own visible top-level "
            + $"z-order after being withdrawn. {run.Trace}");

        Assert.True(PointerWindowProbe.SameWindow(after.Routed, run.UnderneathWindow) == expected,
            $"after the overlay was withdrawn the window manager routes the point to "
            + $"{PointerWindowProbe.DescribeWindow(after.Routed)} rather than back to the window underneath");

        Assert.True(after.EveryInjectionAccepted == expected,
            $"the OS refused one of the four injections after teardown. {run.Trace}");

        Assert.True(after.Downs > restored.Downs == expected,
            $"the desktop did not get the CLICK channel back after teardown. {run.Trace}");
        Assert.True(after.DragMoves > restored.DragMoves == expected,
            $"the desktop did not get the DRAG channel back after teardown. {run.Trace}");
        Assert.True(after.Wheel > restored.Wheel == expected,
            $"the desktop did not get the SCROLL channel back after teardown. {run.Trace}");
        Assert.True(after.KeyDowns > restored.KeyDowns == expected,
            $"the desktop did not get the TYPE channel back after teardown. {run.Trace}");
    }
}
