using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-110. <b>This is the exact inverse of SP-099, and the trap is the same one facing the other
/// way.</b> That packet proved a surface is click-THROUGH, and its final review recorded that
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
        // Measured in exactly this shape before any product code was written (SP-110 plan.md §0,
        // run 1): SetForegroundWindow returned FALSE, zero injected keystrokes arrived, and
        // GetGUIThreadInfo(ourThread).hwndFocus still answered "our window". A capability built on
        // that read would be an OS API, correctly called, certifying nothing — the inverse-SP-099
        // fake this whole packet exists to avoid.
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
            + "process was measured returning FALSE with every keystroke going elsewhere (SP-110 plan.md §0). "
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
        // SP-099's fact with the sign flipped. There it was "the point must NOT be the surface";
        // here it must BE the card. The same instrument answers both, which is what makes the pair
        // meaningful: an oracle that always answered "the top window" would pass this and fail
        // SP-099's, and one that never did would fail this and pass that.
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
        // The content link, and it is the same grade of evidence as SP-100's overlay read-back: the
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
        // a record full of defaults. SP-109 found the same hole by mutation (M-c) and closed it the
        // same way; both observations here carry it and both are pinned.
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
            InkedPixels: 5, SampledPixels: 10, KeystrokesSeen: 1);

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

        Assert.False(update is CapabilityState.Available);
        Assert.Equal(InputReasonCodes.InputNothingPrompted, ((CapabilityState.Unavailable)update).Reason.Code);
        Assert.False(dismiss is CapabilityState.Available);
        Assert.Equal(InputReasonCodes.InputNothingPrompted, ((CapabilityState.Unavailable)dismiss).Reason.Code);
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
        Assert.Equal(0, presence.Pump(16));
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
