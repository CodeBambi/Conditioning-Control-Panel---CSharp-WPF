using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>This is the exact inverse of the overlay packet, and the trap is the same one facing the
/// other way.</b> That packet proved a surface is click-THROUGH, and its final review recorded that
/// "input passes through" is the property most easily faked — a window that does not exist satisfies
/// it too. Proving input is CAPTURED is faked just as cheaply, and in more ways: a handler was
/// attached, <c>WS_EX_TRANSPARENT</c> was not set, <c>SetForegroundWindow</c> returned, a focus API
/// answered yes.
///
/// <para><b>So every fact here compares three things</b>: a fact about the MACHINE (does this
/// session have an interactive desktop — established by <see cref="InputWindowProbe"/>, independent
/// P/Invokes, never the product's), a fact about the OPERATING SYSTEM (does it call this window the
/// foreground, does it call it the foreground THREAD's keyboard focus, where does it route a point,
/// and does a synthesised keystroke actually arrive), and only then the presence's CLAIM.</para>
///
/// <para><b>What these facts do NOT prove.</b> That a human pressed anything — every keystroke here
/// is <c>SendInput</c>, which enters below the driver and above the hardware, and "a person
/// answered" is a manual gate no automated step on any platform discharges. That the card is
/// legible: the ink read-back says the OS holds non-background pixels in the question band, not that
/// the phrase is readable, unclipped, or on a screen anyone is looking at — that is
/// <c>presentation-verified</c> and undischarged. That the host's own message loop translates
/// keystrokes for this window: the evidence here is taken on the presence's own bounded pump. That
/// the foreground will still be ours a millisecond later: it is lent, and every other process on the
/// desktop can take it. And every part of Linux.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class InputCapabilityTests
{
    [Fact]
    public void TheDeliveryOracle_CanSeeAKeystrokeArrive_AndCanSeeItNotArrive()
    {
        // The instrument's own control. Every capture fact in this file trusts that a synthesised
        // keystroke reaching a window procedure MEANS something. An oracle that had degenerated into
        // "always report delivered" would certify a capability that receives nothing, so it is asked,
        // on every run, about a rig this probe builds and owns: a catcher that takes the foreground,
        // and a thief that takes it away again.
        var control = InputWindowProbe.RunNegativeControl();

        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, control.CatcherCreated);
        Assert.True(control.CatcherTookForeground == control.MachineHasInteractiveDesktop,
            $"the probe's own catcher could not take the foreground (machine has an interactive desktop = "
            + $"{control.MachineHasInteractiveDesktop}). Every capture fact in this suite rests on being able to "
            + "do that at all, so none of them prove anything until this does");
        Assert.True(control.KeyInjected == control.MachineHasInteractiveDesktop,
            "SendInput was refused by the OS — a locked workstation, the secure desktop, or UIPI. The delivery "
            + "oracle cannot inject, so it cannot measure delivery");
        Assert.True(control.KeyDeliveredToCatcher == control.MachineHasInteractiveDesktop,
            "a synthesised VK_F13 did not reach the catcher's own window procedure while the OS reported the "
            + "catcher as both the foreground window and the foreground thread's focus. The oracle cannot detect "
            + "delivery at all, so every input fact here is worthless");
        Assert.True(control.CharacterDeliveredToCatcher == control.MachineHasInteractiveDesktop,
            "a synthesised VK_A did not arrive as a translated WM_CHAR 'a'. Delivery of a KEY is not delivery of a "
            + "CHARACTER, and a lock card is typed");
        Assert.True(control.ThiefTookForeground == control.MachineHasInteractiveDesktop,
            "the probe could not hand the foreground to a second window, so the negative leg below is untested");
        Assert.False(control.KeyDeliveredWhileThiefHoldsForeground,
            "a keystroke injected while a DIFFERENT window held the foreground still reached the catcher. The "
            + "oracle counts something other than delivery, and 'the keystroke arrived' proves nothing");
        Assert.False(control.ScratchWindowsExistAfterCleanup,
            "the probe's scratch windows survived their own teardown — the window-existence check cannot say 'no'");
    }

    [Fact]
    public void TheThreadLocalFocusRead_ClaimsAWindowThatReceivesNothing_WhichIsWhyTheSystemReadIsTheOneUsed()
    {
        // THE TRAP, reproduced deterministically rather than described. A window parked on a second
        // thread calls SetFocus on itself, so THAT thread's input queue considers it focused — for
        // as long as it lives, whatever the desktop is doing. Meanwhile the foreground, and every
        // keystroke, belong to the catcher on the first thread.
        //
        // Measured in exactly this shape before any product code was written (this packet's plan.md §0,
        // run 1): SetForegroundWindow returned FALSE, zero injected keystrokes arrived, and
        // GetGUIThreadInfo(ourThread).hwndFocus still answered "our window". A capability built on
        // that read would be an OS API, correctly called, certifying nothing — the inverse of the
        // overlay packet's own fake, which this whole packet exists to avoid.
        var control = InputWindowProbe.RunNegativeControl();

        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, control.ParkedWindowCreated);
        Assert.True(control.ThreadLocalFocusClaimsTheParkedWindow == control.MachineHasInteractiveDesktop,
            "GetGUIThreadInfo(thread).hwndFocus did NOT name the parked window. If the thread-local read cannot be "
            + "made to lie, there is no trap here and the system-wide read this capability uses is decoration — "
            + "which would be a finding, not a pass");
        Assert.True(control.SystemFocusRejectsTheParkedWindow == control.MachineHasInteractiveDesktop,
            "GetGUIThreadInfo(0).hwndFocus named the parked window at the same moment the catcher held the "
            + "foreground. The system-wide read is supposed to be the FOREGROUND thread's focus; if it answers a "
            + "background thread's window then it is not the fact this capability claims it is");
        Assert.False(control.KeyDeliveredToParkedWindow,
            "a window the thread-local focus read calls 'focused' received an injected keystroke while another "
            + "window held the foreground. That would mean the thread-local read was right after all and the "
            + "distinction this capability is built on does not exist");
    }

    [Fact]
    public void THENEGATIVECONTROLIsIDEMPOTENT_BecauseASecondCallInOneProcessUsedToDestroyItsOwnTrap()
    {
        // This was found by landing a module that changed nothing in this file and reddened it
        // anyway: adding test classes moved this class's cases in xunit's within-class order, and
        // the ORDER is the determinant. RunNegativeControl was not idempotent — the SECOND call in
        // a process reported ThreadLocalFocusClaimsTheParkedWindow = FALSE.
        //
        // The mechanism, measured rather than supposed: SetFocus ACTIVATES the top-level window it
        // focuses, so the parked rig took the foreground as soon as this process had the rights to
        // do it (which it does not on the first call and does on every one after). The catcher then
        // took the foreground back, the parked thread was DEACTIVATED, and the system cleared its
        // focus — so the clause the trap turns on failed for a reason that has nothing to do with
        // the trap. The parked rig now carries WS_EX_NOACTIVATE (ScratchWindow.Create's
        // `activatable`), which is the same flag and the same reason as the port's own video
        // surface (D122).
        //
        // Two consecutive calls, in one process, asserting the SAME thing both times. Without this
        // the suite can only find the defect by re-ordering itself, which is how it was found.
        var first = InputWindowProbe.RunNegativeControl();
        var second = InputWindowProbe.RunNegativeControl();

        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, first.ParkedWindowCreated);
        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, second.ParkedWindowCreated);

        Assert.True(
            first.ThreadLocalFocusClaimsTheParkedWindow == first.MachineHasInteractiveDesktop,
            "the FIRST negative control lost its own parked focus");
        Assert.True(
            second.ThreadLocalFocusClaimsTheParkedWindow == second.MachineHasInteractiveDesktop,
            "the SECOND negative control lost its parked focus, so this instrument's answer depends "
            + "on how many times it has already run in this process — which makes every fact built "
            + "on it order-dependent");

        // And the other half of the trap survives the repeat too: the SYSTEM read must go on
        // rejecting the parked window on both calls, or the two reads have stopped disagreeing and
        // there is no trap left to measure.
        Assert.True(
            first.SystemFocusRejectsTheParkedWindow == first.MachineHasInteractiveDesktop);
        Assert.True(
            second.SystemFocusRejectsTheParkedWindow == second.MachineHasInteractiveDesktop);
    }

    [Fact]
    public void ThePromptIsOnScreen_AboveEveryOrdinaryWindow_ConfirmedByTheOperatingSystem()
    {
        var run = InputCaptureObservations.Lifecycle;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OsSeesWindow);
        Assert.True(run.OsSeesWindowVisible == run.MachineHasInteractiveDesktop,
            $"this session has an interactive desktop = {run.MachineHasInteractiveDesktop}, but after Prompt the OS "
            + $"reporting the card visible = {run.OsSeesWindowVisible}. Presence said: "
            + InputCaptureObservations.Describe(run.PromptState));
        Assert.True(run.ZOrder.AboveEveryOrdinaryWindow == run.MachineHasInteractiveDesktop,
            $"the OS puts the card at z position {run.ZOrder.Index} of {run.ZOrder.VisibleCount} visible top-level "
            + $"windows, and the first ordinary (non-topmost) window is at {run.ZOrder.FirstOrdinaryIndex}. A card "
            + "under an ordinary window is a question nobody is being asked");
    }

    [Fact]
    public void TheOperatingSystemReportsThePromptAsTheForegroundWindow_AndAsTheForegroundThreadsKeyboardFocus()
    {
        // The two OS-level facts that separate a card the user can answer from one they cannot, and
        // they are BOTH required: foreground without focus routes keystrokes to a child or to
        // nothing, and a focus read without foreground is the thread-local lie pinned above.
        //
        // Both are taken by the PROBE's own declarations, not the product's, so the presence and the
        // measurement cannot be wrong together.
        var run = InputCaptureObservations.Lifecycle;

        Assert.True(run.OsSaysCardIsForeground == run.MachineHasInteractiveDesktop,
            $"GetForegroundWindow() answers {run.ForegroundWinner}, not the card 0x{run.Window:X}. The foreground is "
            + "LENT by the operating system and it can refuse: a plain SetForegroundWindow from a non-foreground "
            + "process was measured returning FALSE with every keystroke going elsewhere (this packet's plan.md §0). "
            + "Presence said: " + InputCaptureObservations.Describe(run.PromptState));
        Assert.True(run.OsSaysSystemFocusIsCard == run.MachineHasInteractiveDesktop,
            $"GetGUIThreadInfo(0).hwndFocus is not the card 0x{run.Window:X}: the OS is routing the keyboard "
            + "somewhere else. Presence said: " + InputCaptureObservations.Describe(run.PromptState));
        Assert.True(run.ClaimedAvailableOnPrompt == run.MachineHasInteractiveDesktop,
            $"the presence claimed Available = {run.ClaimedAvailableOnPrompt} while the OS's own answers were "
            + $"foreground={run.OsSaysCardIsForeground}, system-focus={run.OsSaysSystemFocusIsCard}. A claim that "
            + "outruns the effect is the exact failure this capability exists to prevent. Presence said: "
            + InputCaptureObservations.Describe(run.PromptState));
        Assert.Equal(run.MachineHasInteractiveDesktop, run.PresenceReportsPrompting);
    }

    [Fact]
    public void TheWindowManagerRoutesAPointInsideThePrompt_ToThePrompt()
    {
        // The overlay's fact with the sign flipped. There it was "the point must NOT be the surface";
        // here it must BE the card. The same instrument answers both, which is what makes the pair
        // meaningful: an oracle that always answered "the top window" would pass this and fail
        // the overlay's, and one that never did would fail this and pass that.
        var run = InputCaptureObservations.Lifecycle;

        Assert.True((run.HitTestWinnerWhilePrompting == run.Window && run.Window != 0)
            == run.MachineHasInteractiveDesktop,
            $"the window manager routes the card's centre to {InputWindowProbe.DescribeWindow(run.HitTestWinnerWhilePrompting)} "
            + $"instead of the card 0x{run.Window:X}. Something is over it, and a card that cannot be clicked is a "
            + "card whose input behaviour cannot be distinguished from being buried");
    }

    [Fact]
    public void ASynthesisedKeystrokeReachesThePromptsOwnWindowProcedure_AndACharacterReachesTheCaller()
    {
        // THE LINK NOTHING ELSE COVERS. Everything above is the OS answering questions ABOUT the
        // window; this is the OS DELIVERING something TO it. VK_F13 first, because it produces no
        // character and so cannot type anything into another window if it escapes; then VK_A, which
        // must arrive at the CALLER as the character 'a' — the whole path, from the OS's raw input
        // thread to the module's own keystroke callback.
        //
        // Where this stops: SendInput is not a human and not a physical key. `a person answered` is
        // a manual gate and nothing here discharges it.
        var run = InputCaptureObservations.Lifecycle;

        Assert.True(run.KeyInjected == run.MachineHasInteractiveDesktop,
            "SendInput was refused by the operating system, so nothing about delivery is claimed");
        Assert.True(run.KeyReachedTheCard == run.MachineHasInteractiveDesktop,
            $"a synthesised VK_F13 did not reach the card's own window procedure while the OS reported it as the "
            + $"foreground window ({run.OsSaysCardIsForeground}) and as the foreground thread's keyboard focus "
            + $"({run.OsSaysSystemFocusIsCard}). Every other fact here is the OS answering questions about the "
            + "window; this is the only one where it hands the window something");
        Assert.True(run.TypedCharacterReachedTheModel == run.MachineHasInteractiveDesktop,
            "a synthesised VK_A did not arrive at the caller's keystroke callback as the character 'a'. A lock card "
            + "is TYPED: delivery of a key that never becomes a character is not the capability this module needs");
        Assert.True(run.EscapeReachedTheModel == run.MachineHasInteractiveDesktop,
            "a synthesised VK_ESCAPE did not arrive as a Cancel keystroke. Escape is the one key whose meaning "
            + "cannot be recovered from a character, and it is the module's only exit");
        Assert.Equal(0, run.CallbackFaults);
    }

    [Fact]
    public void TheOperatingSystemHoldsInkForThePromptsOwnClientArea()
    {
        // The content link, and it is the same grade of evidence as the overlay read-back: the
        // pixels are read back OUT of the window's device context, not counted in the buffer that
        // was written. A card that is focused and blank is a question the user cannot read.
        var run = InputCaptureObservations.Lifecycle;

        Assert.True(run.SampledPixels > 0 == run.MachineHasInteractiveDesktop,
            $"the ink read-back sampled {run.SampledPixels} pixels; a check that samples nothing certifies "
            + "everything");
        Assert.True(run.InkedPixels > 0 == run.MachineHasInteractiveDesktop,
            $"the OS holds {run.InkedPixels} non-background pixels of {run.SampledPixels} sampled in the card's "
            + "question band. Zero means a focused, blank window: the user has the keyboard and no question. "
            + "Presence said: " + InputCaptureObservations.Describe(run.PromptState));
        Assert.True(run.ClaimedAvailableOnUpdate == run.MachineHasInteractiveDesktop,
            "repainting the card claimed " + InputCaptureObservations.Describe(run.UpdateState));
    }

    [Fact]
    public void DismissingThePrompt_TakesItDownAndGivesTheKeyboardBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the lifecycle rig puts a REAL card on the desktop, takes the foreground and the system keyboard "
            + "focus, and asks WindowFromPoint about it. Off Windows no card is created, the handle is 0, and "
            + "a comparison against 0 answers about nothing; "
            + "TheLinuxBranchRefusesHonestly_AndCarriesItsManualGateWithIt asserts the honest off-Windows "
            + "answer on every platform");

        var run = InputCaptureObservations.Lifecycle;

        Assert.False(run.OsSeesWindowVisibleAfterDismiss,
            "after Dismiss the OS still reports the card visible — it was not taken down, only reported taken down. "
            + "Presence said: " + InputCaptureObservations.Describe(run.DismissState));
        Assert.False(run.CardStillForegroundAfterDismiss,
            "after Dismiss the OS still calls the card the foreground window: the user's keyboard is pointed at a "
            + "window they cannot see");
        Assert.NotEqual(run.Window, run.HitTestWinnerAfterDismiss);
        Assert.False(run.PresenceReportsPromptingAfterDismiss);
        Assert.False(run.KeyAfterDismissReachedTheModel,
            "a keystroke injected after the card was dismissed still reached the caller's callback — the presence "
            + "kept feeding a module that had stopped asking");
        Assert.True(run.ClaimedAvailableOnDismiss == run.MachineHasInteractiveDesktop,
            "Dismiss claimed " + InputCaptureObservations.Describe(run.DismissState));
    }

    [Fact]
    public void DisposingThePresence_LeavesNoTopLevelWindowBehind()
    {
        // The path a crash-adjacent shutdown takes. A leaked card is a topmost window holding the
        // user's keyboard for the life of the process that no code can reach any more — strictly
        // worse than the overlay's leak, which at least passes clicks through.
        var run = InputCaptureObservations.Lifecycle;

        Assert.False(run.OsSeesWindowAfterDispose,
            "Dispose left a top-level window behind — an unreachable window that has held the foreground");
        Assert.Null(run.TeardownDiagnostic);
    }

    [Fact]
    public void TheLinuxBranchRefusesHonestly_AndCarriesItsManualGateWithIt()
    {
        // Reachable from this Windows box on purpose: the refusal path is testable here and the
        // Windows path is testable nowhere else, which is the honest asymmetry rather than a hidden
        // one. The gate names X11 and Wayland separately because they give DIFFERENT answers, and
        // the Wayland answer is probably "no by design" — a client cannot take activation unprompted.
        var run = InputCaptureObservations.RefusalFor(InputHostPlatform.Linux);

        Assert.False(run.ClaimedAvailable);
        Assert.False(run.ReportsPrompting);
        Assert.False(run.ReportsHoldingTheInput);
        Assert.False(run.ReportsCanReachAUser);
        Assert.Equal(InputReasonCodes.InputMechanismAbsent, run.Code);
        Assert.Contains("MANUAL GATE (Linux, undischarged)", run.Detail, StringComparison.Ordinal);
        Assert.Contains("_NET_ACTIVE_WINDOW", run.Detail, StringComparison.Ordinal);
        Assert.Contains("wl_keyboard.enter", run.Detail, StringComparison.Ordinal);
        Assert.Contains("xdg-activation-v1", run.Detail, StringComparison.Ordinal);
        Assert.True(run.UpdateAlsoRefused,
            "the Linux refusal covers Prompt but not Update — a partial refusal is a path a caller can mistake for "
            + "a card that is up");
        Assert.True(run.DismissAlsoRefused,
            "the Linux refusal covers Prompt but not Dismiss");
        Assert.False(run.StationWasAsked,
            "the refusal answered its station observation as ASKED. 'We did not ask' and 'we asked and nobody was "
            + "there' are different facts and a refusal must never report the second");
        Assert.False(run.ObservationWasAsked);
    }

    [Fact]
    public void EveryPlatformWithoutABackend_RefusesRatherThanNoOps()
    {
        var macOs = InputCaptureObservations.RefusalFor(InputHostPlatform.MacOs);
        var unknown = InputCaptureObservations.RefusalFor(InputHostPlatform.Unknown);

        Assert.False(macOs.ClaimedAvailable);
        Assert.Equal(InputReasonCodes.InputMechanismAbsent, macOs.Code);
        Assert.Contains("nothing was attempted", macOs.Detail, StringComparison.Ordinal);
        Assert.False(unknown.ClaimedAvailable);
        Assert.Equal(InputReasonCodes.InputMechanismAbsent, unknown.Code);
        Assert.Contains("nothing was attempted", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObservationThatWasNeverAsked_IsNotConfirmed_WhateverItsOtherFieldsSay()
    {
        // The Asked clause exists so a build with no mechanism cannot produce a confirmation out of
        // a record full of defaults. An earlier packet found the same hole by mutation (M-c) and
        // closed it the same way; both observations here carry it and both are pinned.
        var neverAsked = InputCaptureObservation.NotAsked with
        {
            WindowExists = true,
            WindowVisible = true,
            AboveEveryOrdinaryWindow = true,
            IsForegroundWindow = true,
            SystemKeyboardFocusIsThisWindow = true,
            Window = 42,
            HitTestWinner = 42,
        };

        Assert.False(neverAsked.Confirmed,
            "an observation with every capture field true but Asked false was Confirmed. That is a claim assembled "
            + "from defaults on a platform that has no way to ask");

        var stationNeverAsked = InputStationObservation.NotAsked with
        {
            WindowStationVisible = true,
            DisplayCount = 3,
            DesktopReachable = true,
        };

        Assert.False(stationNeverAsked.Confirmed);
    }

    [Fact]
    public void EveryClauseOfConfirmed_IsLoadBearing_AndDroppingAnyOneOfThemBreaksIt()
    {
        // Pinned on the predicate itself rather than only through a live window, so a mutation to any
        // conjunct reds on EVERY run instead of only on the runs where the desktop happens to
        // disagree. Six clauses, six single-field mutations, and the base case proves the fixture is
        // not simply always-false.
        var confirmed = new InputCaptureObservation(
            Asked: true, Window: 7, WindowExists: true, WindowVisible: true,
            HeldBounds: new InputBounds(0, 0, 10, 10), AboveEveryOrdinaryWindow: true,
            IsForegroundWindow: true, SystemKeyboardFocusIsThisWindow: true, HitTestWinner: 7,
            InkedPixels: 5, SampledPixels: 10, BackgroundHeld: true, KeystrokesSeen: 1);

        Assert.True(confirmed.Confirmed);
        Assert.False((confirmed with { Asked = false }).Confirmed);
        Assert.False((confirmed with { WindowExists = false }).Confirmed);
        Assert.False((confirmed with { WindowVisible = false }).Confirmed);
        Assert.False((confirmed with { AboveEveryOrdinaryWindow = false }).Confirmed);
        Assert.False((confirmed with { IsForegroundWindow = false }).Confirmed,
            "Confirmed survived the card not being the foreground window — the clause that makes this capability "
            + "different from 'a window is on screen'");
        Assert.False((confirmed with { SystemKeyboardFocusIsThisWindow = false }).Confirmed,
            "Confirmed survived the OS routing the keyboard to another window. This is the clause the thread-local "
            + "focus read would have satisfied while nothing arrived");
        Assert.False((confirmed with { HitTestWinner = 9 }).Confirmed);

        // And the hit test's own non-zero guard, which is NOT redundant: without it an observation
        // about NO window reports that a hit test routes to it, because 0 == 0.
        Assert.False(InputCaptureObservation.NotAsked.HitTestRoutesHere,
            "an observation with no window at all claims the window manager routes a point to it");
        Assert.False((confirmed with { Window = 0, HitTestWinner = 0 }).HitTestRoutesHere);

        // The ink differential: a count with no background behind it is not ink.
        Assert.True(confirmed.Inked);
        Assert.False((confirmed with { BackgroundHeld = false }).Inked,
            "pixels differing from a background that was never painted were counted as ink — which is what an "
            + "UNPAINTED window's device context looks like");
        Assert.False((confirmed with { InkedPixels = 0 }).Inked);

        var station = new InputStationObservation(true, true, 1, true);
        Assert.True(station.Confirmed);
        Assert.False((station with { WindowStationVisible = false }).Confirmed);
        Assert.False((station with { DisplayCount = 0 }).Confirmed);
        Assert.False((station with { DesktopReachable = false }).Confirmed);
    }

    [Fact]
    public void ThisProcessCanReachAUser_AndTheStationReadIsTheOperatingSystemsOwnAnswer()
    {
        using var presence = InputPresenceFactory.Create();
        var station = presence.ObserveStation();

        Assert.Equal(InputWindowProbe.WindowsHost, station.Asked);
        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, station.Confirmed);
        Assert.Equal(InputWindowProbe.MachineHasInteractiveDesktop, presence.CanReachAUser);
        Assert.True(station.DisplayCount >= 1 == InputWindowProbe.MachineHasInteractiveDesktop,
            $"the OS reports {station.DisplayCount} display(s) while the probe's own reading of the machine says "
            + $"interactive-desktop={InputWindowProbe.MachineHasInteractiveDesktop}");
    }

    [Fact]
    public void AskingAPresenceThatHasPromptedNothing_NeverReadsAsSuccess()
    {
        using var presence = new Win32InputPresence();

        var update = presence.Update(new InputPromptContent("q", "1 of 1", string.Empty, "hint"));
        var dismiss = presence.Dismiss();

        // NO SKIP: Win32InputPresence checks the MECHANISM before it checks whether anything is
        // prompted, so off Windows both answers are input-mechanism-absent. Either way the property
        // this fact exists for holds on every platform — asking a presence that has prompted nothing
        // never reads as success — and asserting the platform's own code keeps it measured there.
        var expected = OperatingSystem.IsWindows()
            ? InputReasonCodes.InputNothingPrompted
            : InputReasonCodes.InputMechanismAbsent;

        Assert.False(update is CapabilityState.Available);
        Assert.Equal(expected, ((CapabilityState.Unavailable)update).Reason.Code);
        Assert.False(dismiss is CapabilityState.Available);
        Assert.Equal(expected, ((CapabilityState.Unavailable)dismiss).Reason.Code);
        Assert.False(presence.IsPrompting);
        Assert.False(presence.HoldsTheInput);
        Assert.Null(presence.LastPrompt);
        Assert.False(presence.LastObservation.Asked);
    }

    [Fact]
    public void ADisposedPresence_RefusesEveryPathAndNeverPromptsAgain()
    {
        var presence = new Win32InputPresence();
        presence.Dispose();

        var prompt = presence.Prompt(new InputPromptRequest(
            new InputBounds(0, 0, 100, 100),
            new InputPromptContent("q", "1 of 1", string.Empty, "hint"),
            _ => { }));

        Assert.False(prompt is CapabilityState.Available);
        Assert.Equal(InputReasonCodes.InputPresenceDisposed, ((CapabilityState.Unavailable)prompt).Reason.Code);
        Assert.False(presence.HoldsTheInput);
        Assert.False(presence.IsPrompting);

        // A DISPOSED PUMP MUST DISPATCH NOTHING, and "nothing" is only a measurement if there was
        // something to dispatch. This posts one message to THIS thread and then asks: an empty
        // queue would satisfy the assertion below for reasons that have nothing to do with
        // disposal, and it did — the fact used to read whatever else happened to be on the thread
        // (a WM_TIMER, another component's window message) and reddened on a busy thread while
        // passing on a quiet one. With the message posted, removing the _disposed guard from
        // Win32InputPresence.Pump reds this every time instead of only sometimes.
        var posted = PostOwnThreadMessage();

        Assert.Equal(0, presence.Pump(16));

        // And take the posted message back off the queue, filtered to exactly the one posted, so
        // this fact leaves the thread as it found it for whatever runs next on it.
        if (posted)
        {
            Assert.True(DrainOwnThreadMessage(),
                "the message this fact posted to its own thread was gone before it could be taken back — "
                + "something else drained it, so the zero above was not this presence refusing to pump");
        }
    }

    private const uint ProbeMessage = 0x8000;   // WM_APP: never a system message, never anyone else's.
    private const uint PmRemove = 0x0001;

    private static bool PostOwnThreadMessage() =>
        OperatingSystem.IsWindows() && PostThreadMessageW(GetCurrentThreadId(), ProbeMessage, 0, 0);

    private static bool DrainOwnThreadMessage() =>
        PeekMessageW(out _, 0, ProbeMessage, ProbeMessage, PmRemove);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeMsg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out ProbeMsg message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [Fact]
    public void ACardOnNoDisplayAtAll_NeverClaimsAvailable_AndTheINKReadBackIsWhatCatchesIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the lifecycle rig puts a REAL card on the desktop, takes the foreground and the system keyboard "
            + "focus, and asks WindowFromPoint about it. Off Windows no card is created, the handle is 0, and "
            + "a comparison against 0 answers about nothing; "
            + "TheLinuxBranchRefusesHonestly_AndCarriesItsManualGateWithIt asserts the honest off-Windows "
            + "answer on every platform");

        // A card asked for at a rectangle no monitor covers. THE MEASUREMENT THAT SURPRISED THIS
        // PACKET: the window manager still routes that rectangle's centre to the window —
        // WindowFromPoint walks the window tree, not the monitors — so every capture check passes.
        // The OS reports it visible, at the right rectangle, above every ordinary window, as the
        // foreground and as the keyboard focus. What CANNOT pass is the content read-back: the OS
        // holds no painted background for a window on no display, so the ink differential answers no
        // and the state is Degraded.
        //
        // That is the honest shape of this edge and it is recorded rather than dressed up as the
        // refusal it is not: the ink link is the only one of the six that a card nobody can see
        // fails.
        //
        // AND THE PRESENCE'S SIDE OF THE CONTRACT IS ASSERTED HERE, not merely measured. A Degraded
        // card is DELIBERATELY left up by the presence — the OS gave it the foreground and the
        // keyboard, and only the content check said no, so the presence is still prompting and the
        // window is still on screen. That is the state the MODULE has to clear, and this fact pins
        // the presence's half of it so the module's half (LockCardModuleTests.
        // ACardThatIsFocusedAndBLANK_IsAlsoTakenBackDown...) is testing something real rather than a
        // window that was never up.
        var run = InputCaptureObservations.Edges;

        Assert.False(run.OffScreenPromptClaimedAvailable,
            "a card on no display at all claimed Available. Every OS-routing check passes for it, so the ink "
            + "read-back is the ONLY thing standing between this and a green claim about a card nobody can see");
        Assert.Equal("none", run.OffScreenPromptCode);
        Assert.True(run.OffScreenWindowLeftVisible == InputWindowProbe.MachineHasInteractiveDesktop,
            $"the presence's window visibility after a DEGRADED prompt is {run.OffScreenWindowLeftVisible}. "
            + "Degraded means the OS gave the card the input and only the ink check refused, so the presence "
            + "leaves it up ON PURPOSE — and the module is what must take it down. If this is false the presence "
            + "has started withdrawing on Degraded too, and the module-side fact is no longer testing anything");
        Assert.Equal(run.OffScreenWindowLeftVisible, run.OffScreenPresenceStillPrompting);
    }

    [Fact]
    public void ACardWithNothingWrittenOnIt_IsDEGRADED_AndTheInkCheckKnowsAPaintedBackgroundFromAnUnpaintedWindow()
    {
        // The ink DIFFERENTIAL, and the mutation that forced it. The card's window class registers no
        // background brush, so an UNPAINTED window's device context holds whatever the OS left in it
        // — which differs from the background colour just as reliably as text does. A bare count
        // therefore reported "inked" for a window nothing had ever drawn on, and the mutation that
        // deletes the paint call entirely SURVIVED the first sweep.
        //
        // So the check is now two-sided: a control point in the filled margin must read back exactly
        // the background, and only then does a differing pixel in the question band mean ink. This
        // fact drives a card whose every band is empty: the background IS held, and there is no ink.
        var run = InputCaptureObservations.Edges;

        Assert.False(run.BlankPromptClaimedAvailable,
            "a card with nothing written on it claimed Available. The OS gave it the keyboard and there is nothing "
            + "on it to read");
        Assert.True(run.BlankPromptWasDegraded == InputWindowProbe.MachineHasInteractiveDesktop,
            $"a blank card must be Degraded (it holds the input and shows nothing), and it was not: "
            + $"code={run.BlankPromptCode}");
        Assert.Equal(
            InputWindowProbe.MachineHasInteractiveDesktop ? InputReasonCodes.InputPromptNotInked : "none",
            run.BlankPromptCode);
        Assert.True(run.BlankPromptBackgroundHeld == InputWindowProbe.MachineHasInteractiveDesktop,
            "the painted background was NOT read back from the blank card's own device context. Without that the "
            + "ink count means nothing: an unpainted window's DC differs from the background too");
        Assert.Equal(0, run.BlankPromptInkedPixels);

        // The REPAINT path carries its own copy of the check, and its own copy of the background
        // control. Found by mutation: deleting the control from Update alone survived a sweep whose
        // only blank-card fact went through Prompt.
        Assert.True(run.BlankRepaintWasDegraded == InputWindowProbe.MachineHasInteractiveDesktop,
            $"repainting a live card with nothing on it claimed success: code={run.BlankRepaintCode}");
        Assert.Equal(
            InputWindowProbe.MachineHasInteractiveDesktop ? InputReasonCodes.InputPromptNotInked : "none",
            run.BlankRepaintCode);
    }

    [Fact]
    public void AControlCharacterIsNotTyping_AndAPrintableOneIs()
    {
        // Upstream's whole clipboard-gesture list (LockCardWindow.xaml.cs:246-264) exists because its
        // card is a TextBox. This port's card has no edit control, so the list reduces to ONE rule:
        // a control character is not typing. Ctrl+V arrives as WM_CHAR 0x16 and must be ignored.
        //
        // Delivered by PostMessage, and labelled as such: this is NOT the OS routing input (that
        // claim is the SendInput leg above). It is the only way to hand the window a control
        // character without pressing Ctrl+V on the user's desktop, where an escaped keystroke would
        // paste into whatever window caught it.
        var run = InputCaptureObservations.Edges;

        Assert.True(run.PrintableCharacterReachedTheCaller == InputWindowProbe.MachineHasInteractiveDesktop,
            "a printable character posted to the card did not reach the caller — so the control-character "
            + "assertion below is also true of a window that ignores everything");
        Assert.False(run.ControlCharacterReachedTheCaller,
            "WM_CHAR 0x16 — what Ctrl+V produces in a window with no edit control — arrived at the caller as "
            + "typing. Upstream's anti-cheat reduces to exactly this one rule here");
    }

    [Fact]
    public void AfterDismissTheCardStopsFeedingTheCaller_EvenForAMessageAlreadyInItsQueue()
    {
        // The guard that survives the window being hidden: a message already queued for the card can
        // still be dispatched to its window procedure, and the callback must be gone by then. Not
        // reachable through the OS's own routing — a hidden window is not the focus — so it is
        // reached by PostMessage, and labelled as such.
        var run = InputCaptureObservations.Edges;

        Assert.False(run.CharacterAfterDismissReachedTheCaller,
            "a character posted to the card AFTER it was dismissed reached the caller. The presence hid the window "
            + "but kept feeding a module that had stopped asking");
    }

    [Fact]
    public void HoldingTheInputRequiresTheFOREGROUND_NotJustAFocusRead()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the lifecycle rig puts a REAL card on the desktop, takes the foreground and the system keyboard "
            + "focus, and asks WindowFromPoint about it. Off Windows no card is created, the handle is 0, and "
            + "a comparison against 0 answers about nothing; "
            + "TheLinuxBranchRefusesHonestly_AndCarriesItsManualGateWithIt asserts the honest off-Windows "
            + "answer on every platform");

        // The mutation that survived the first sweep and is the sharpest of them: dropping the
        // FOREGROUND clause from HoldsTheInput. It is not redundant with the focus read, because
        // GetGUIThreadInfo(0).hwndFocus can name a window of the foreground thread that is NOT the
        // foreground window — the case a process with several top-level windows on one thread
        // produces, which is exactly this test process.
        //
        // The rig: prompt (the card takes the foreground), then hand the foreground to a scratch
        // window this probe owns. The card is still visible, still a window, still focused inside its
        // own thread — and HoldsTheInput must be FALSE, because nothing the user types reaches it.
        using var presence = new Win32InputPresence();
        var state = presence.Prompt(new InputPromptRequest(
            InputCaptureObservations.CentreBounds,
            new InputPromptContent("HOLDING THE INPUT", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));

        var heldWhileForeground = presence.HoldsTheInput;
        var window = presence.NativeHandles.Window;

        using var thief = InputWindowProbe.ScratchWindow.Create(
            "CcpInputHoldThief", 20, 20, 200, 140, show: true);
        var thiefTook = InputWindowProbe.TakeForeground(thief.Handle);
        var heldAfterTheft = presence.HoldsTheInput;
        var cardStillVisible = InputWindowProbe.WindowIsVisible(window);

        Assert.True(heldWhileForeground == InputWindowProbe.MachineHasInteractiveDesktop,
            "the card did not hold the input immediately after a successful Prompt: "
            + InputCaptureObservations.Describe(state));
        Assert.True(thiefTook == InputWindowProbe.MachineHasInteractiveDesktop,
            "the probe could not take the foreground away, so the assertion below is untested");
        Assert.True(cardStillVisible == InputWindowProbe.MachineHasInteractiveDesktop,
            "the card stopped being visible when the foreground moved, which would make the assertion below true "
            + "for the wrong reason");
        Assert.False(heldAfterTheft,
            "the card still reports holding the input while ANOTHER window holds the foreground. Every keystroke "
            + "is going somewhere else and the module's dot would claim the user is being asked a question");

        // AND THE MEASUREMENT THAT SAYS WHY THE TWO CLAUSES CANNOT BE PRISED APART, which is a
        // finding rather than an assertion of convenience. The mutation sweep asked whether the
        // FOREGROUND clause is separable from the system-focus one; the attempt to build the state
        // that would separate them — the foreground on the thief, the foreground thread's focus on
        // the card, both windows on one thread — is IMPOSSIBLE for a childless top-level window:
        // SetFocus ACTIVATES the top-level window it focuses, so the two move together.
        //
        // That is measured here, not assumed: after SetFocus(card) the card is BOTH the focus and
        // the foreground again. The consequence is recorded honestly in the sweep — dropping either
        // clause is an EQUIVALENT mutation for this window shape, and both are kept because they are
        // two different questions that would diverge the moment the card grew a child control.
        InputWindowProbe.FocusWindow(window);
        var focusFollowed = InputWindowProbe.SystemKeyboardFocus() == window;
        var foregroundFollowed = InputWindowProbe.Foreground() == window;

        Assert.True(focusFollowed == InputWindowProbe.MachineHasInteractiveDesktop,
            "SetFocus did not give the card the foreground thread's focus back");
        Assert.Equal(focusFollowed, foregroundFollowed);
        Assert.True(presence.HoldsTheInput == InputWindowProbe.MachineHasInteractiveDesktop,
            "the card does not hold the input after being focused again, though the OS reports it as both the "
            + "focus and the foreground");
    }

    [Fact]
    public void ABoundsWithNoArea_CannotEvenBeRequested()
    {
        // A zero-size card is the input analogue of the overlay's zero-opacity ghost: a window the OS
        // agrees exists and that nobody can read or click. Refused at the boundary, because it
        // describes a caller bug rather than a platform.
        var content = new InputPromptContent("q", "1 of 1", string.Empty, "hint");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InputPromptRequest(new InputBounds(0, 0, 0, 10), content, _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InputPromptRequest(new InputBounds(0, 0, 10, 0), content, _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InputPromptRequest(new InputBounds(0, 0, 10, -4), content, _ => { }));
    }
}
