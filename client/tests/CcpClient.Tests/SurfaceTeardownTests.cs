using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The overlay safety invariant that had no instrument:</b> <i>"No failure leaves an invisible
/// input-blocking or permanently topmost surface"</i> and <i>"Teardown and display/window
/// transitions restore normal desktop input"</i>
/// (<c>.claude/skills/overlay-clickthrough/SKILL.md:30-31</c>).
///
/// <para><b>Why it is the one worth having.</b> <see cref="TeardownTests"/> proves the SHAPE of
/// teardown — once per process, reverse start order, never throws — over counting participants that
/// own nothing. Nothing in it, and nothing anywhere else in the suite, asked what the DESKTOP looks
/// like afterwards. Four of the port's six native surfaces are click-through and merely occupy the
/// topmost band; the other two are the dangerous ones. <c>Pointer/Win32PointerSurface.cs:850-852</c>
/// is <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c> with <c>WS_EX_TRANSPARENT</c> deliberately absent,
/// because a poppable bubble must receive clicks; <c>Input/Win32InputPresence.cs:1097-1099</c> is
/// <c>WS_EX_TOOLWINDOW</c> alone and takes the foreground AND the keyboard. Strand either and the
/// user's desktop eats their clicks, or their keyboard, with no visible window to close.</para>
///
/// <para><b>Every reading here is the operating system's, not the product's.</b> A source guard over
/// the extended styles would be the wrong instrument twice: the skill forbids treating those styles
/// as approved (<c>SKILL.md:48</c>), and <c>Overlay/Win32OverlayPresence.cs:504-511</c> records a
/// measured run in which every style write SUCCEEDED and the ex-style read back wrong anyway,
/// because another process had stripped topmost. So the surfaces are placed by the product, torn
/// down through the product's own <c>ApplicationHost.ShutdownAsync</c>, and then the window manager
/// is asked two questions: is any visible top-level window of ours left in the z-order, and does a
/// point where one of them used to be route somewhere that is not us.</para>
///
/// <para><b>The vacuous case is closed twice.</b> Fact 1 proves all five surfaces really reached the
/// desktop and that the two dangerous ones really were dangerous — the pointer target really won its
/// own centre, the card really held the foreground and the system keyboard focus. Fact 2 tears down
/// with one surface deliberately withheld and proves the instrument can say NO. Only then does
/// fact 3 assert the invariant.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class SurfaceTeardownTests : RealDesktopFacts
{
    [Fact]
    public void ALLFIVESurfacesReallyReachedTheDesktop_OrEveryTeardownReadingBelowIsATestOfNothingHappening()
    {
        var run = SurfaceTeardownObservations.Teardown;

        Assert.True(run.OverlayPresentState is CcpClient.Desktop.Capabilities.CapabilityState.Available
            == run.MachineHasInteractiveDesktop,
            $"the overlay never reached the desktop: {SurfaceTeardownObservations.Describe(run.OverlayPresentState)}");
        Assert.True(run.GlyphPresentState is CcpClient.Desktop.Capabilities.CapabilityState.Available
            == run.MachineHasInteractiveDesktop,
            $"the glyph surface never reached the desktop: {SurfaceTeardownObservations.Describe(run.GlyphPresentState)}");
        Assert.True(run.VideoShowState is CcpClient.Desktop.Capabilities.CapabilityState.Available
            == run.MachineHasInteractiveDesktop,
            $"the video surface never held a picture: {SurfaceTeardownObservations.Describe(run.VideoShowState)}");
        Assert.True(run.PointerOpenState is CcpClient.Desktop.Capabilities.CapabilityState.Available
            == run.MachineHasInteractiveDesktop,
            $"the pointer target never opened: {SurfaceTeardownObservations.Describe(run.PointerOpenState)}");

        // The OS's own answer for each of the four handles, and the fifth by difference: the video
        // surface exposes no handle accessor, so the only way to know its window existed is that a
        // visible top-level window of ours appeared that is none of the other four.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayVisibleBefore);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.GlyphVisibleBefore);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.PointerVisibleBefore);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardVisibleBefore);
        Assert.True(run.UnnamedNewWindowsBefore >= (run.MachineHasInteractiveDesktop ? 1 : 0),
            "no visible top-level window of ours appeared beyond the four whose handles this run holds, so the "
            + "video surface — the one that exposes no handle — was never on the desktop and the teardown "
            + "readings below say nothing about it");
        Assert.True(run.OurVisibleWindowsBefore >= (run.MachineHasInteractiveDesktop ? 5 : 0),
            $"only {run.OurVisibleWindowsBefore} visible top-level window(s) of ours reached the desktop where "
            + "five surfaces were placed");

        // And the two that make this invariant worth measuring at all.
        Assert.True(run.PointerOwnsItsPointBefore == run.MachineHasInteractiveDesktop,
            "the pointer target did not win its own centre even while it was up, so it was never the "
            + "input-blocking surface this fact is about and 'it stopped blocking input' would be true of a "
            + "window that never blocked any");
        Assert.True(run.CardHeldForegroundAndKeyboardBefore == run.MachineHasInteractiveDesktop,
            "the lock card never took the foreground and the system keyboard focus, so 'teardown gave the "
            + "keyboard back' would be true of a card that never took it");
        Assert.True(run.TopmostOfOursBefore >= (run.MachineHasInteractiveDesktop ? 1 : 0),
            "none of our new windows was in the topmost band, so the 'permanently topmost surface' half of the "
            + "invariant is being asserted over surfaces that were never topmost");
    }

    /// <summary>
    /// <b>The broken-detector control, and the mutation the packet asks for, run every time rather
    /// than performed once by hand.</b>
    ///
    /// <para>A teardown that forgets one surface must be VISIBLE to the readings fact 3 makes. So
    /// this run tears down through the same <c>ApplicationHost.ShutdownAsync</c> over a participant
    /// that was handed four of the five surfaces — the pointer target, the one that is deliberately
    /// not click-through, never reaches an owner. Nothing in the product is modified to construct
    /// it; that is exactly how a real surface gets stranded.</para>
    /// </summary>
    [Fact]
    public void ATeardownThatForgetsOneSurface_LeavesAStrandedTopmostWindowEatingItsOwnPoint()
    {
        var run = SurfaceTeardownObservations.Teardown;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.LeakedPointerStillAWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.LeakedPointerStillVisible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.LeakedPointerStillTopmost);

        // The reading fact 3 depends on. Unraised, with nothing else of ours on the screen: the
        // stranded window wins the point on its own, which is what a user's click would do.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.LeakedPointerStillEatsItsPoint);

        Assert.Equal(run.MachineHasInteractiveDesktop ? 1 : 0, run.OurVisibleWindowsAfterLeak);
    }

    /// <summary>
    /// <b>The refusal THE INVARIANT takes rather than running vacuously, and the one place in this
    /// file where keying is not available.</b>
    ///
    /// <para>The three facts around it are KEYED to <c>MachineHasInteractiveDesktop</c> because
    /// "these five Win32 surfaces never reached the desktop" is a true and useful statement about a
    /// machine with no Win32 window manager. This one is different: <c>Os.RoutesAwayFromUs</c>
    /// requires the point to resolve to a REAL window belonging to another process - an answer of 0
    /// on a live desktop means the reading was taken off-screen, not that the desktop got its input
    /// back - so off Windows it answers NO, and keying it would mean asserting the INVERSE of the
    /// invariant as this platform's expected outcome. The other clauses ("nothing of ours survives",
    /// "the handle is gone", "the foreground came back", "no teardown diagnostic") are each
    /// trivially true of a run that created no window at all.</para>
    ///
    /// <para>So it refuses by name, and the name is pinned in <c>client/tests/floor/floor.json</c>'s
    /// <c>allowedSkips</c> under its machine class. The X11 and Wayland halves of
    /// <c>.claude/skills/overlay-clickthrough/SKILL.md:30-31</c> are unmeasured and no green run
    /// here says anything about them.</para>
    /// </summary>
    private const string ReclamationRefusalReason =
        "this fact asks the WINDOW MANAGER what it still holds after the application tore itself down: whether a "
        + "visible top-level window of ours survives in the z-order, and whether each of five points where a surface "
        + "used to be now routes to a real window belonging to another process. Neither question exists off Windows, "
        + "and neither can be asked in a Windows session with no interactive desktop: the five Win32 surfaces refuse "
        + "to present, every handle is 0, the hit test resolves to no window at all, and 'nothing of ours survived' "
        + "would be true of a run that put nothing on the screen. Its siblings above are KEYED to the machine "
        + "instead, because 'the surfaces never reached the desktop' IS a true statement there; this one has no such "
        + "reading and refuses instead";

    /// <summary>
    /// <b>THE INVARIANT.</b> With the leak repaired, the application is fully down and the operating
    /// system holds nothing of ours: no visible top-level window in the z-order, no live handle, and
    /// every point where a surface used to be routes to another process.
    ///
    /// <para><b>Why both halves.</b> A handle-existence check alone would pass over a window that
    /// was destroyed and immediately re-created, and it can say nothing at all about the video
    /// surface, whose handle this run never learns. The routing question is the one the user's mouse
    /// asks, and it is asked at all five rectangles.</para>
    /// </summary>
    [Fact]
    public void AfterTeardown_NoWindowOfOursSurvives_AndEverySurfacePointRoutesBackToTheDesktop()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, ReclamationRefusalReason);

        var run = SurfaceTeardownObservations.Teardown;

        Assert.True(run.OurVisibleWindowsAfterRestore == 0,
            $"{run.OurVisibleWindowsAfterRestore} visible top-level window(s) of this process survived the "
            + $"application's own teardown: {run.SurvivorsAfterRestore}. That is a surface the user cannot see "
            + $"and cannot close. The surfaces' own teardown diagnostics: {run.TeardownDiagnostics}");

        Assert.True(run.OverlayHandleGone, $"the overlay's window outlived teardown: {run.TeardownDiagnostics}");
        Assert.True(run.GlyphHandleGone, $"the glyph surface's window outlived teardown: {run.TeardownDiagnostics}");
        Assert.True(run.PointerHandleGone,
            $"the pointer target's window outlived teardown, and it is NOT click-through by design "
            + $"(Pointer/Win32PointerSurface.cs:850-852), so it is still eating clicks: {run.TeardownDiagnostics}");
        Assert.True(run.CardHandleGone,
            $"the lock card's window outlived teardown, and it takes the foreground AND the keyboard by design "
            + $"(Input/Win32InputPresence.cs:1097-1099): {run.TeardownDiagnostics}");

        Assert.True(run.PointerPointRoutesAwayFromUs,
            $"the pointer target's old centre still routes to this process: {run.PointerPointOwnerAfter}");
        Assert.True(run.CardPointRoutesAwayFromUs, "the card's old centre still routes to this process");
        Assert.True(run.OverlayPointRoutesAwayFromUs, "the overlay's old centre still routes to this process");
        Assert.True(run.GlyphPointRoutesAwayFromUs, "the glyph surface's old centre still routes to this process");
        Assert.True(run.VideoPointRoutesAwayFromUs,
            "the video surface's old centre still routes to this process — and it is the surface whose handle "
            + "this run never learned, so this reading is the only one that can see it");

        Assert.True(run.ForegroundReturnedToTheDesktop,
            $"this process still holds the foreground after its own teardown: {run.ForegroundOwnerAfter}");

        Assert.Equal("(none)", run.TeardownDiagnostics);
    }

    /// <summary>
    /// <b>The harness's own honesty leg.</b> A native window belongs to the thread that created it
    /// and only that thread may destroy it — which is the entire reason
    /// <c>Session/SessionParticipant.cs:884-940</c> routes its disposals through the UI dispatch
    /// boundary and why <c>Win32OverlayPresence.Dispose</c> writes a wrong-thread diagnostic instead
    /// of pretending. If this run's teardown ever resumed on a pool thread, every reading above
    /// would report a stranded surface that the PRODUCT did not strand. So the run records where the
    /// disposals actually ran, and this fact refuses the whole measurement rather than let it lie.
    /// </summary>
    [Fact]
    public void TheTeardownRanWhereTheWindowsWereMade_OrTheReadingsAboveAreTheHarnessesFaultAndNotTheProducts()
    {
        var run = SurfaceTeardownObservations.Teardown;

        Assert.True(run.LeakShutdownCompletedSynchronously,
            "ApplicationHost.ShutdownAsync did not complete on the calling thread, so the OS was asked about the "
            + "desktop while teardown was still in flight (OperationRegistry.CancelAndDrainAsync only awaits when "
            + "the registry holds outstanding completions, and this host's is empty)");
        Assert.True(run.RestoreShutdownCompletedSynchronously,
            "the repairing host's ShutdownAsync did not complete on the calling thread");
        Assert.Equal(run.CreatingThread, run.LeakStopThread);
        Assert.Equal(run.CreatingThread, run.RestoreStopThread);

        // The other way this fact could quietly become a reading of nothing: a process that already
        // owned a visible window when the run started would have its own survivor subtracted out of
        // every count. Zero here is also a small fact in its own right — no earlier real-desktop
        // fixture in this assembly stranded a window either.
        Assert.Equal(0, run.BaselineOurVisibleWindows);
    }
}
