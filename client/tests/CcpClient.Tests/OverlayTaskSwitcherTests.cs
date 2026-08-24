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
/// <para><b>WINDOWS EVIDENCE ONLY.</b> <c>WS_EX_TOOLWINDOW</c>, <c>WS_EX_APPWINDOW</c>, window
/// ownership and DWM cloaking are Win32 concepts. The X11 and Wayland halves of this invariant —
/// taskbar hiding via <c>_NET_WM_STATE_SKIP_TASKBAR</c> or a layer-shell surface — are entirely
/// unmeasured, and a green run here says nothing whatever about them.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayTaskSwitcherTests : RealDesktopFacts
{
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
    /// </summary>
    [Fact]
    public void TheShellsRuleOffersAnOrdinaryWindow_AndAllThreeSurfacesReallyReachedTheDesktop()
    {
        var run = Run;
        var expected = run.MachineHasInteractiveDesktop;

        Assert.True(run.Control.Visible == expected,
            $"the control window is not on the desktop, so the rule has nothing ordinary to answer YES about. "
            + $"{run.Trace}");
        Assert.True(run.Control.Reading.IsOrdinaryTaskSwitchingWindow == expected,
            "the shell's own task-window rule refuses an ORDINARY visible unowned non-tool window "
            + $"({run.Control.Reading.Clause}). The rule is answering NO to everything on this machine, so the "
            + $"invariant below would pass without measuring anything. {run.Trace}");

        Assert.True(
            expected
                ? run.OverlayPresentState is CapabilityState.Available
                : run.OverlayPresentState is CapabilityState.Unavailable,
            $"presenting the overlay answered {PointerSurfaceObservations.Describe(run.OverlayPresentState)}");
        Assert.True(
            expected
                ? run.PointerOpenState is CapabilityState.Available
                : run.PointerOpenState is CapabilityState.Unavailable,
            $"opening the pointer target answered {PointerSurfaceObservations.Describe(run.PointerOpenState)}");
        Assert.True(
            expected
                ? run.CardPromptState is CapabilityState.Available
                : run.CardPromptState is CapabilityState.Unavailable,
            $"prompting the lock card answered {PointerSurfaceObservations.Describe(run.CardPromptState)}");

        Assert.True(run.Overlay.Visible == expected, $"the overlay is not visible. {run.Trace}");
        Assert.True(run.PointerTarget.Visible == expected, $"the pointer target is not visible. {run.Trace}");
        Assert.True(run.Card.Visible == expected, $"the lock card is not visible. {run.Trace}");

        // Four windows placed, four expected. A run that gained fewer gained a window that is not
        // separately countable, and the census below would then be reading the wrong set.
        Assert.True(run.NewWindowCount >= (expected ? 4 : 0),
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
    /// fourth surface somebody adds later without touching this file.</para>
    ///
    /// <para><b>Mutation that reds it:</b> drop <c>WS_EX_TOOLWINDOW</c> from any of the three
    /// surfaces' creation styles, or add <c>WS_EX_APPWINDOW</c> to one.</para>
    /// </summary>
    [Fact]
    public void NoNativeSurfaceOfThisPort_IsAnOrdinaryTaskSwitchingWindow_ByTheShellsOwnRule()
    {
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
        var expectedOffered = run.MachineHasInteractiveDesktop ? new[] { run.Control.Window } : [];
        Assert.True(
            run.OfferedByTheRule.Select(v => v.Window).SequenceEqual(expectedOffered),
            $"the shell's rule offers {run.OfferedByTheRule.Count} window(s) of ours where the ONLY one it should "
            + $"offer is the deliberate control {PointerWindowProbe.DescribeWindow(run.Control.Window)}. "
            + $"Offered: {run.Offered}. {run.Trace}");
    }
}
