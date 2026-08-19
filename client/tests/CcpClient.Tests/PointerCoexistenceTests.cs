using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-113 trap 2: <b>four surfaces now share one desktop</b>, and the three that landed first have
/// to survive the fourth.
///
/// <para>The click-through overlay (SP-099/SP-100, five modules draw on it), the Lock Card
/// (SP-110, which TAKES the foreground and the keyboard), the video surface (SP-111, a layered
/// topmost window this process paints and reads back), and now a set of always-on-top windows that
/// CATCH clicks. <c>Overlay/**</c>, <c>Input/**</c>, <c>Audio/**</c> and <c>Video/**</c> are
/// byte-identical to base after this packet: they are CONSUMED here, and every reading of them is
/// taken through their OWN instruments — <see cref="OverlayWindowProbe"/> and
/// <see cref="InputWindowProbe"/>, unmodified — rather than through the capability under test.</para>
///
/// <para><b>The reading that matters is the card's.</b> Every other surface in the port either takes
/// the foreground once and keeps it, or never asks for it. This one is placed and then MOVED, on a
/// cadence, for the whole session — and a move is an operation <c>Input/**</c> does not have and
/// therefore never had to survive. So the card is read at four moments, and two of them are inside
/// the new operation.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class PointerCoexistenceTests
{
    [Fact]
    public void ALLFOURSurfacesReallyReachedTheDesktop_OrEveryReadingBelowIsATestOfNothingHappening()
    {
        // The positive control, and it is the same role SP-099's middle leg and SP-110's first leg
        // play: without it, "the overlay still passes clicks through" is equally true of an overlay
        // that was never presented.
        var run = PointerSurfaceObservations.Coexistence;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayPresented);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardTookTheInput);
        Assert.True(run.VideoShowedAPicture == run.MachineHasInteractiveDesktop,
            "the video surface did not hold a decoded picture before the pointer target existed, so the reading "
            + "taken while one was up says nothing about coexistence");
        Assert.True(run.PointerEarnedAvailableBesideThem == run.MachineHasInteractiveDesktop,
            $"the pointer surface did not earn Available beside the other three: "
            + $"{PointerSurfaceObservations.Describe(run.PointerOpenState)}. Coexistence would then be the new "
            + "surface failing politely rather than four surfaces sharing a desktop");
    }

    [Fact]
    public void TheOverlayStaysCLICKTHROUGH_AtAllFourMoments_IncludingDuringAMove()
    {
        var run = PointerSurfaceObservations.Coexistence;

        Assert.True(run.OverlayBefore.PointPassesThrough);
        Assert.True(run.OverlayDuringOpen.PointPassesThrough);
        Assert.True(run.OverlayDuringMove.PointPassesThrough);
        Assert.True(run.OverlayAfter.PointPassesThrough);

        Assert.True(run.OverlayBefore.TransparentStyleHeld == run.MachineHasInteractiveDesktop);
        Assert.True(run.OverlayDuringOpen.TransparentStyleHeld == run.MachineHasInteractiveDesktop);
        Assert.True(run.OverlayDuringMove.TransparentStyleHeld == run.MachineHasInteractiveDesktop);
        Assert.True(run.OverlayAfter.TransparentStyleHeld == run.MachineHasInteractiveDesktop);

        // And the differential, so "the point went elsewhere" cannot be satisfied by an overlay that
        // quietly stopped existing.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayCatchesItsOwnPointWhenMadeOpaque);
    }

    [Fact]
    public void TheOverlayKeepsItsBandAndItsAlpha_AndNeverBecomesTheForeground()
    {
        var run = PointerSurfaceObservations.Coexistence;

        // Not "above every window": a pointer target is topmost too and legitimately contests the
        // band. That is SP-099's own wording and SP-099's own reason.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayBefore.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringOpen.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayDuringMove.AboveEveryOrdinaryWindow);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayAfter.AboveEveryOrdinaryWindow);

        var expectedAlpha = run.MachineHasInteractiveDesktop ? 153 : -1;
        Assert.Equal(expectedAlpha, run.OverlayBefore.Alpha);
        Assert.Equal(expectedAlpha, run.OverlayDuringOpen.Alpha);
        Assert.Equal(expectedAlpha, run.OverlayDuringMove.Alpha);
        Assert.Equal(expectedAlpha, run.OverlayAfter.Alpha);

        Assert.False(run.OverlayBefore.IsForeground);
        Assert.False(run.OverlayDuringOpen.IsForeground);
        Assert.False(run.OverlayDuringMove.IsForeground);
        Assert.False(run.OverlayAfter.IsForeground);

        Assert.True(run.OverlayStillEarnsAvailable == run.MachineHasInteractiveDesktop,
            $"the overlay's own Present no longer earns Available after a pointer surface came and went: "
            + $"{PointerSurfaceObservations.Describe(run.OverlayRePresentState)}");
    }

    [Fact]
    public void THECARDKEEPSTHEFOREGROUNDANDTHEKEYBOARD_ThroughAnOpenAMoveAndAClose()
    {
        // THE TRAP-2 FACT, and the one this capability could most plausibly break: the Lock Card
        // holds the two things the operating system lends to exactly one window at a time, and a
        // pointer target is placed, raised to the topmost band, and then MOVED repeatedly beside it.
        var run = PointerSurfaceObservations.Coexistence;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringOpen.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringOpen.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringOpen.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuringMove.HoldsSystemKeyboardFocus);

        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.Visible);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.HoldsSystemKeyboardFocus);
    }

    [Fact]
    public void TheVideoSurfaceStillHoldsItsPicture_WithAPointerTargetOnTheDesktopBesideIt()
    {
        var run = PointerSurfaceObservations.Coexistence;

        Assert.True(run.VideoHeldPictureDuring == run.MachineHasInteractiveDesktop,
            $"the video capability's own read-back stopped confirming its picture while a pointer target was up: "
            + $"{PointerSurfaceObservations.Describe(run.VideoShowStateDuring)}");
    }

    [Fact]
    public void TheMOVEEarnsAvailableBesideAllThree_WhichIsTheOperationTheInputCapabilityDoesNotHave()
    {
        var run = PointerSurfaceObservations.Coexistence;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.PointerMoveState is CapabilityState.Available
                : run.PointerMoveState is CapabilityState.Unavailable,
            $"the move did not earn Available with an overlay, a card and a video surface on the same desktop: "
            + $"{PointerSurfaceObservations.Describe(run.PointerMoveState)}");
    }
}
