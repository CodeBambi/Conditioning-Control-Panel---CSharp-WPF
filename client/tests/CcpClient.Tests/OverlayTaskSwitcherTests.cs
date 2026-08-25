using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b><i>"Overlays do not ... appear as ordinary task-switching windows"</i>
/// (<c>.claude/skills/overlay-clickthrough/SKILL.md:29</c>), measured instead of implied.</b>
///
/// <para>Until this file the claim rested entirely on <c>WS_EX_TOOLWINDOW</c> being requested at
/// window creation. That is a WRITE, and this port already has a measured case where every style
/// write succeeded and the extended style read back wrong anyway
/// (<c>Overlay/Win32OverlayPresence.cs:504-511</c>); <c>SKILL.md:48</c> separately forbids treating
/// those bits as approved. So these facts ask the OPERATING SYSTEM for the four properties the
/// shell's own task-window rule is built from, about real windows that are really on screen, and
/// apply the rule.</para>
///
/// <para><b>Upstream reached the same outcome by the same two bits and said so in the method's
/// name.</b> Every WPF flash window is <c>ShowInTaskbar = false, ShowActivated = false</c>
/// (<c>Services/Flash/FlashService.cs:3619-3620</c>) and is then given
/// <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> on its live hwnd by <c>HideFromAltTab</c>
/// (<c>:3819-3828</c>, wired at <c>:3653</c>); the bubbles do it too, rebuilding the ex-style from a
/// known base every re-show because a pooled shell carries its previous life's bits
/// (<c>Services/BubbleService.cs:4881-4896</c>).</para>
///
/// <para><b>WHAT THIS CANNOT SAY, stated first because the temptation is to over-read it.</b> No
/// public API exposes the Alt-Tab list or the taskbar's contents. The shell builds both itself and
/// may filter further than the published rule. These facts are about window STATE under that rule;
/// what a human sees when they hold Alt, and what sits on their taskbar, are HEADED claims
/// (<c>client/docs/verification-harness.md</c>) and nothing here discharges them.</para>
///
/// <para><b>THE ANTI-VACUITY CONTROL IS NOT KEYED, AND THAT IS THE POINT OF THIS FILE'S SHAPE.</b>
/// Both facts below used to compare every reading against
/// <c>run.MachineHasInteractiveDesktop</c>. On a machine with no interactive desktop — every Linux
/// host, and a Windows session with no display — that key made the CONTROL expect its own failure:
/// <c>Assert.True(run.Control.Visible == false)</c> passed about a window that was never created,
/// and the invariant beneath it then read <c>0 == 0</c> for all three surfaces and passed too. Two
/// green facts, nothing measured. A control that can itself be switched off controls nothing, so
/// the machine question is asked ONCE, as a gate, and every reading after it is unconditional.</para>
///
/// <para><b>REFUSAL, NOT A BLOCKED SUITE.</b> The gate is <c>Assert.SkipUnless</c>, which yields a
/// <c>NotExecuted</c> result carrying the reason text below — a named non-result the floor refuses
/// to accept unless the name is pinned in <c>allowedSkips</c> under its machine/OS admission rule
/// (<c>client/tests/floor/check-floor.mjs:240-251</c>). It is deliberately NOT an off-platform
/// assertion failure: a fact that reds on Linux for being Windows-only recreates the bring-up that
/// left roughly 66 facts red at once, and a suite nobody can run green teaches nothing. The port's
/// established pairing is the same one used here — the mechanism-driving fact refuses by name,
/// and the platform's own answer is carried by facts that are unconditional BECAUSE they assert a
/// typed <c>CapabilityState.Unavailable</c> refusal rather than a window reading
/// (<c>GlyphCapabilityTests.cs:144-148</c> names both halves in the skip reason itself).</para>
///
/// <para><b>WINDOWS EVIDENCE ONLY.</b> <c>WS_EX_TOOLWINDOW</c>, <c>WS_EX_APPWINDOW</c>, window
/// ownership and DWM cloaking are Win32 concepts. The X11 and Wayland halves of this invariant —
/// taskbar hiding via <c>_NET_WM_STATE_SKIP_TASKBAR</c> or a layer-shell surface — are entirely
/// unmeasured, and a refusal here says nothing whatever about them. No fact in this port carries
/// that half yet, which is exactly why the refusal must be visible in the run rather than
/// absorbed into a green.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayTaskSwitcherTests : RealDesktopFacts
{
    /// <summary>
    /// The one machine question this file asks, asked once and never again. Held as a constant so
    /// both facts refuse with identical text and neither can drift into a weaker reason.
    /// </summary>
    private const string RefusalReason =
        "the task-switcher run places FOUR real top-level windows on the interactive desktop and then asks USER32 "
        + "and DWM for WS_EX_TOOLWINDOW, WS_EX_APPWINDOW, window ownership and DWM cloaking about each of them "
        + "(OverlayDesktopInputObservations.TaskSwitcher). None of those exist off Windows, and none of them can be "
        + "read in a Windows session with no desktop. The probe folds that in and answers all-false for every "
        + "handle (PointerWindowProbe.cs:302-306), so every reading here would be 0 == 0 about windows that were "
        + "never created — which is a PASS with nothing behind it. This refuses by name instead. The X11 and "
        + "Wayland halves of the invariant (_NET_WM_STATE_SKIP_TASKBAR, wlr-layer-shell) are measured by nothing "
        + "in this port, and this refusal is where that shows.";

    private static OverlayDesktopInputObservations.TaskSwitcherRun Run =>
        OverlayDesktopInputObservations.TaskSwitcher;

    /// <summary>
    /// <b>The vacuous case, closed first, and it has two halves.</b>
    ///
    /// <para>A predicate that answers NO to everything would satisfy the invariant below perfectly,
    /// so an ordinary unowned activatable window with no <c>WS_EX_TOOLWINDOW</c> is placed beside the
    /// surfaces and must come back YES. And a surface that never reached the screen is not a
    /// task-switching window for the worst possible reason, so all three are read back from the OS as
    /// visible while they are up.</para>
    ///
    /// <para><b>Every assertion here is unconditional.</b> There is no machine reading to compare
    /// against, because a control whose expected answer flips with the machine cannot detect a rule
    /// that has stopped answering. Either this ran on a desktop and the readings are real, or it
    /// refused above.</para>
    ///
    /// <para><b>Mutation that reds it:</b> make <c>PointerWindowProbe.ReadTaskSwitcherState</c>
    /// return the all-false reading unconditionally — the exact state every handle has off Windows.
    /// The control then comes back NO and this fact names the clause that decided.</para>
    /// </summary>
    [Fact]
    public void TheShellsRuleOffersAnOrdinaryWindow_AndAllThreeSurfacesReallyReachedTheDesktop()
    {
        Assert.SkipUnless(PointerWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        Assert.True(run.Control.Visible,
            $"the control window is not on the desktop, so the rule has nothing ordinary to answer YES about. "
            + $"{run.Trace}");
        Assert.True(run.Control.Reading.IsOrdinaryTaskSwitchingWindow,
            "the shell's own task-window rule refuses an ORDINARY visible unowned non-tool window "
            + $"({run.Control.Reading.Clause}). The rule is answering NO to everything on this machine, so the "
            + $"invariant below would pass without measuring anything. {run.Trace}");

        Assert.True(run.OverlayPresentState is CapabilityState.Available,
            $"presenting the overlay answered {PointerSurfaceObservations.Describe(run.OverlayPresentState)}");
        Assert.True(run.PointerOpenState is CapabilityState.Available,
            $"opening the pointer target answered {PointerSurfaceObservations.Describe(run.PointerOpenState)}");
        Assert.True(run.CardPromptState is CapabilityState.Available,
            $"prompting the lock card answered {PointerSurfaceObservations.Describe(run.CardPromptState)}");

        Assert.True(run.Overlay.Visible, $"the overlay is not visible. {run.Trace}");
        Assert.True(run.PointerTarget.Visible, $"the pointer target is not visible. {run.Trace}");
        Assert.True(run.Card.Visible, $"the lock card is not visible. {run.Trace}");

        // Four windows placed, four expected. A run that gained fewer gained a window that is not
        // separately countable, and the census below would then be reading the wrong set.
        Assert.True(run.NewWindowCount >= 4,
            $"this process gained only {run.NewWindowCount} visible top-level window(s) where four were placed "
            + $"(baseline {run.BaselineWindows}). {run.Trace}");
    }

    /// <summary>
    /// <b>THE INVARIANT.</b> Not one native surface this port puts on the user's desktop is an
    /// ordinary task-switching window by the shell's own documented rule — and the ONLY window of
    /// ours the rule offers is the control that was placed to prove the rule still says yes to
    /// something.
    ///
    /// <para>Three surfaces, chosen for what each risks. The click-through OVERLAY is the invariant's
    /// literal subject. The POINTER TARGET is deliberately not click-through
    /// (<c>Pointer/Win32PointerSurface.cs:850-852</c>), so it is a window that really does eat input.
    /// The LOCK CARD is the one surface that deliberately takes the foreground and the keyboard
    /// (<c>Input/Win32InputPresence.cs:1097-1099</c>), which makes it the likeliest of the three to be
    /// offered if its tool-window bit did not hold.</para>
    ///
    /// <para><b>The census clause is the one that cannot rot.</b> Naming three handles proves three
    /// things; asking the rule about EVERY visible top-level window this process gained catches the
    /// fourth surface somebody adds later without touching this file. It expects EXACTLY the control
    /// — unconditionally. Asserting the set is empty would be wrong, and expecting an empty set off
    /// the desktop (which is what this used to do) was the same error wearing a machine reading.</para>
    ///
    /// <para><b>Mutation that reds it:</b> drop <c>WS_EX_TOOLWINDOW</c> from any of the three
    /// surfaces' creation styles, or add <c>WS_EX_APPWINDOW</c> to one.</para>
    /// </summary>
    [Fact]
    public void NoNativeSurfaceOfThisPort_IsAnOrdinaryTaskSwitchingWindow_ByTheShellsOwnRule()
    {
        Assert.SkipUnless(PointerWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        foreach (var surface in new[] { run.Overlay, run.PointerTarget, run.Card })
        {
            Assert.False(surface.Reading.IsOrdinaryTaskSwitchingWindow,
                $"{surface.Name} is an ordinary task-switching window by the shell's own rule "
                + $"({surface.Reading.Clause}). It would clutter the user's Alt-Tab list with a surface they cannot "
                + "meaningfully switch to — and in the pointer target's and the lock card's case, with one that eats "
                + $"their input. {run.Trace}");
        }

        // The whole set, so a surface added later is caught without editing this file. Asserting the
        // set is EMPTY would be wrong: an empty answer is also what a rule that says no to everything
        // gives, and the fact above exists precisely because that rule can be broken.
        Assert.True(
            run.OfferedByTheRule.Select(v => v.Window).SequenceEqual([run.Control.Window]),
            $"the shell's rule offers {run.OfferedByTheRule.Count} window(s) of ours where the ONLY one it should "
            + $"offer is the deliberate control {PointerWindowProbe.DescribeWindow(run.Control.Window)}. "
            + $"Offered: {run.Offered}. {run.Trace}");
    }
}
