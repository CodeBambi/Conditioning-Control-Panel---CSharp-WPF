using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Trap 1: <b>FIVE surfaces now share one desktop</b>, and the four that landed first have to
/// survive the fifth.
///
/// <para>The click-through overlay (five modules draw on it), the Lock Card
/// (which TAKES the foreground and the keyboard), the video surface, the pointer targets
/// (which CATCH clicks), and now a layered window composited with per-pixel alpha.
/// <c>Overlay/**</c>, <c>Input/**</c>, <c>Audio/**</c>, <c>Video/**</c> and <c>Pointer/**</c> are
/// byte-identical to base after this packet: they are CONSUMED here, and every reading of them is
/// taken through their OWN instruments — <see cref="OverlayWindowProbe"/>,
/// <see cref="InputWindowProbe"/> and <see cref="PointerWindowProbe"/>, unmodified — rather than
/// through the capability under test.</para>
///
/// <para><b>THE COEXISTENCE POSITION, stated rather than implied.</b> An earlier final review recorded
/// that the coexistence evidence does not scale past four disjoint rectangles: its strength was that
/// no surface could occlude another's hit-test point, and a fifth breaks that. This file does not
/// pretend otherwise and does not extend that file by copying a pair.</para>
///
/// <list type="number">
/// <item><b>What is still proven by disjointness.</b> The four landed rectangles keep their own
/// positions and the glyph surface takes a fifth that intersects none of them — asserted, not
/// assumed (<see cref="TheFifthRectangleIsDisjointFromAllFour_AssertedRatherThanAssumed"/>). So this
/// run is a strict extension of the arrangement already proved.</item>
/// <item><b>What disjointness could NOT have proven, and where it moved to.</b> This capability's
/// own evidence REQUIRES an overlap — proving a transparent pixel shows the background behind it
/// means putting the surface over a known background — so it cannot live here. It lives in
/// <see cref="GlyphAlphaDifferentialTests"/>, where ownership of every sampled point is DECIDED from
/// the OS's own z-order and the intervening windows' rectangles rather than assumed from geometry.
/// That is the occlusion-aware arbitration that review named, and it is what scales past
/// four.</item>
/// <item><b>What neither proves, plainly.</b> Five surfaces under CONTENTION. Every reading here is
/// taken at a quiet moment of this run's own making, and a foreign topmost window can still own any
/// point on the one machine-global screen — the residue named in
/// <c>client/docs/verification-harness.md</c> and measured again by this packet's probe. Nothing
/// here proves an ORDERING the OS did not already report, and nothing here is a headed
/// capture.</item>
/// </list>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class GlyphCoexistenceTests : RealDesktopFacts
{
    [Fact]
    public void ALLFIVESurfacesReallyReachedTheDesktop_OrEveryReadingBelowIsATestOfNothingHappening()
    {
        var run = GlyphSurfaceObservations.Coexistence;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayPresented);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardTookTheInput);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.VideoShowedAPicture);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.PointerOpened);
        Assert.True(run.GlyphEarnedAvailableBesideThem == run.MachineHasInteractiveDesktop,
            "the glyph surface did not earn Available beside the other four: "
            + $"{GlyphSurfaceObservations.Describe(run.GlyphState)}. Coexistence would then be the new surface "
            + "failing politely rather than five surfaces sharing a desktop");
    }

    [Fact]
    public void TheFifthRectangleIsDisjointFromAllFour_AssertedRatherThanAssumed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "this reading is the Win32 window manager arbitrating between five REAL top-level windows - a "
            + "hit test, a foreground read, a system keyboard-focus read, or a rectangle derived from "
            + "GetSystemMetrics. Off Windows none of the five windows exists, every handle is 0, and 0 == 0 "
            + "would make several of these readings answer YES about nothing at all; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        // The disjointness argument, kept where it still holds and MEASURED rather than asserted in a
        // comment. Where it stops holding is stated on the class.
        Assert.True(GlyphSurfaceObservations.Coexistence.GlyphSurfaceSharesNoRectangle);
    }

    [Fact]
    public void TheOverlayStaysCLICKTHROUGH_AtAllFourMoments_IncludingDuringAMove()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "this reading is the Win32 window manager arbitrating between five REAL top-level windows - a "
            + "hit test, a foreground read, a system keyboard-focus read, or a rectangle derived from "
            + "GetSystemMetrics. Off Windows none of the five windows exists, every handle is 0, and 0 == 0 "
            + "would make several of these readings answer YES about nothing at all; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        var run = GlyphSurfaceObservations.Coexistence;

        Assert.True(run.OverlayBefore.PointPassesThrough);
        Assert.True(run.OverlayDuringPresent.PointPassesThrough);
        Assert.True(run.OverlayDuringMove.PointPassesThrough);
        Assert.True(run.OverlayAfter.PointPassesThrough);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayBefore.TransparentStyleHeld);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringPresent.TransparentStyleHeld);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringMove.TransparentStyleHeld);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayAfter.TransparentStyleHeld);

        // The differential, taken on the OVERLAY itself, so "the point went elsewhere" cannot be
        // satisfied by an overlay that quietly stopped existing.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayCatchesItsOwnPointWhenMadeOpaque);
    }

    [Fact]
    public void THEOVERLAYKEEPSITSUNIFORMALPHA_WhichIsTheONEThingThisCapabilityCouldHaveDestroYED()
    {
        // THE TRAP-1 FACT FOR THIS PACKET SPECIFICALLY. The overlay measured that toggling
        // WS_EX_LAYERED and then calling UpdateLayeredWindow converts a window to per-pixel mode
        // and DELETES its uniform alpha permanently - which is the reading the overlay's own ghost
        // check depends on. If anything in Glyph/** ever reached the overlay's handle, this number
        // would become -1 and the overlay capability would refuse itself forever after.
        var run = GlyphSurfaceObservations.Coexistence;
        var expected = run.MachineHasInteractiveDesktop ? 153 : -1;

        Assert.Equal(expected, run.OverlayBefore.Alpha);
        Assert.Equal(expected, run.OverlayDuringPresent.Alpha);
        Assert.Equal(expected, run.OverlayDuringMove.Alpha);
        Assert.Equal(expected, run.OverlayAfter.Alpha);
    }

    [Fact]
    public void TheOverlayKeepsItsBand_NeverBecomesTheForeground_AndStillEarnsItsOwnAvailable()
    {
        var run = GlyphSurfaceObservations.Coexistence;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayBefore.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringPresent.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringMove.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayAfter.AboveEveryOrdinaryWindow);

        Assert.False(run.OverlayBefore.IsForeground);
        Assert.False(run.OverlayDuringPresent.IsForeground);
        Assert.False(run.OverlayDuringMove.IsForeground);
        Assert.False(run.OverlayAfter.IsForeground);

        Assert.True(run.OverlayStillEarnsAvailable == run.MachineHasInteractiveDesktop,
            "the overlay's own Present no longer earns Available after a glyph surface came and went: "
            + $"{GlyphSurfaceObservations.Describe(run.OverlayRePresentState)}");
    }

    [Fact]
    public void THECARDKEEPSTHEFOREGROUNDANDTHEKEYBOARD_ThroughAPresentAMoveAndAWithdraw()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "this reading is the Win32 window manager arbitrating between five REAL top-level windows - a "
            + "hit test, a foreground read, a system keyboard-focus read, or a rectangle derived from "
            + "GetSystemMetrics. Off Windows none of the five windows exists, every handle is 0, and 0 == 0 "
            + "would make several of these readings answer YES about nothing at all; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        // The two things the operating system lends to exactly one window at a time. A layered
        // WS_EX_NOACTIVATE surface must never take either, and this is where that is checked
        // against the one capability whose whole job is holding them.
        var run = GlyphSurfaceObservations.Coexistence;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringPresent.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringPresent.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringPresent.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.HoldsSystemKeyboardFocus);
    }

    [Fact]
    public void TheVideoSurfaceStillHoldsItsPicture_WithAGlyphSurfaceOnTheDesktopBesideIt()
    {
        var run = GlyphSurfaceObservations.Coexistence;

        Assert.True(run.VideoHeldPictureDuring == run.MachineHasInteractiveDesktop,
            "the video capability's own read-back stopped confirming its picture while a glyph surface was up: "
            + $"{GlyphSurfaceObservations.Describe(run.VideoShowStateDuring)}");
    }

    [Fact]
    public void ThePointerTargetStillOwnsItsOwnPoint()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "this reading is the Win32 window manager arbitrating between five REAL top-level windows - a "
            + "hit test, a foreground read, a system keyboard-focus read, or a rectangle derived from "
            + "GetSystemMetrics. Off Windows none of the five windows exists, every handle is 0, and 0 == 0 "
            + "would make several of these readings answer YES about nothing at all; the machine-keyed facts "
            + "in this class carry what CAN be said off Windows");

        var run = GlyphSurfaceObservations.Coexistence;
        Assert.Equal(run.MachineHasInteractiveDesktop, run.PointerStillOwnsItsPoint);
    }

    [Fact]
    public void TheMOVEEarnsAvailableBesideAllFour()
    {
        var run = GlyphSurfaceObservations.Coexistence;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.GlyphMoveState is CapabilityState.Available
                : run.GlyphMoveState is CapabilityState.Unavailable,
            "the move did not earn Available with an overlay, a card, a video surface and a pointer target on "
            + $"the same desktop: {GlyphSurfaceObservations.Describe(run.GlyphMoveState)}");
    }
}
