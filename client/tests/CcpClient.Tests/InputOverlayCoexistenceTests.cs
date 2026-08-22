using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Trap 1. <b>Five landed modules draw through a click-through, always-on-top overlay.</b> A
/// window that steals focus or z-order from it, or that leaves it input-opaque, breaks live sessions
/// in the least visible way available: the desktop underneath stops taking clicks and nothing in the
/// app looks wrong.
///
/// <para><b>So <c>Overlay/**</c> is CONSUMED here, never edited.</b> These facts build the real
/// <c>Win32OverlayPresence</c>, present a real click-through surface, and then measure it three
/// times through <see cref="OverlayWindowProbe"/> — the overlay's own instrument, unmodified:
/// before any card exists, while a card holds the foreground, and after the card is dismissed and
/// its presence disposed. The overlay's own <c>OverlayCapabilityTests</c> are untouched and are
/// not weakened by anything here.</para>
///
/// <para>The card is placed at a rectangle DISJOINT from the overlay's, so the overlay's hit-test
/// point is never occluded by the thing under test — otherwise "the point went past the overlay"
/// would be measuring the card.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class InputOverlayCoexistenceTests
{
    [Fact]
    public void TheCardReallyTookTheForeground_WhichIsWhatMakesTheRestOfThisFileATest()
    {
        // Without this leg, every "the overlay is unchanged" fact below would be satisfied by a card
        // that never came up. It is the same role the overlay's middle leg plays for click-through.
        var run = InputCaptureObservations.Coexistence;

        Assert.True(run.OverlayPresented == run.MachineHasInteractiveDesktop,
            "the overlay itself did not reach the screen, so nothing below is a measurement of a card's effect on "
            + "it: " + InputCaptureObservations.Describe(run.OverlayRePresentState));
        Assert.True(run.CardTookTheInput == run.MachineHasInteractiveDesktop,
            "the input card did not take the foreground and the keyboard focus while the overlay was up. Every "
            + "coexistence fact here would then be a test of nothing happening");
    }

    [Fact]
    public void TheOverlayStillPassesInputThrough_BeforeDuringAndAfterACard()
    {
        // The property five landed modules depend on. Measured at three separate moments against the
        // SAME point, through the overlay's own oracle.
        var run = InputCaptureObservations.Coexistence;

        Assert.True(run.Before.PointPassesThrough,
            "the overlay was already swallowing clicks before any card existed — this fixture cannot say anything "
            + "about the card");
        Assert.True(run.During.PointPassesThrough,
            "with the input card holding the foreground, the window manager routes the overlay's own centre TO the "
            + "overlay. Clicks are being swallowed and the desktop underneath is broken while everything looks "
            + "implemented");
        Assert.True(run.After.PointPassesThrough,
            "after the card was dismissed and its presence disposed, the overlay swallows clicks. Opening a card "
            + "left the overlay input-opaque, which is the failure mode this trap names");
        Assert.True(run.Before.TransparentStyleHeld && run.During.TransparentStyleHeld && run.After.TransparentStyleHeld,
            "WS_EX_TRANSPARENT was lost from the overlay across the card's lifecycle");
    }

    [Fact]
    public void TheOverlayIsStillAboveEveryOrdinaryWindow_AfterACardHasComeAndGone()
    {
        // The z-order half. A card is topmost too, so while it is up it legitimately CONTESTS the
        // band with the overlay — which is why the claim is "above every ORDINARY window" and not
        // "above every window" (the same wording, and the same reason, as the overlay's own z-order fact:
        // WPF re-raises its topmost windows on a cadence precisely because the band is shared).
        var run = InputCaptureObservations.Coexistence;

        Assert.True(run.Before.AboveEveryOrdinaryWindow == run.MachineHasInteractiveDesktop,
            "the overlay was not above every ordinary window before the card existed");
        Assert.True(run.During.AboveEveryOrdinaryWindow == run.MachineHasInteractiveDesktop,
            "a card holding the foreground pushed the overlay below an ordinary window — the five modules drawing "
            + "through it are now underneath somebody's browser");
        Assert.True(run.After.AboveEveryOrdinaryWindow == run.MachineHasInteractiveDesktop,
            "the overlay did not get its band back after the card was gone");
    }

    [Fact]
    public void TheOverlayNeverBecomesTheForeground_AndKeepsItsAlpha_ThroughTheWholeCardLifecycle()
    {
        var run = InputCaptureObservations.Coexistence;

        Assert.False(run.Before.IsForeground);
        Assert.False(run.During.IsForeground,
            "the overlay became the foreground window while a card was up — WS_EX_NOACTIVATE was disturbed");
        Assert.False(run.After.IsForeground);
        Assert.Equal(run.Before.Alpha, run.During.Alpha);
        Assert.Equal(run.Before.Alpha, run.After.Alpha);
        Assert.True(run.After.Alpha > 0 == run.MachineHasInteractiveDesktop,
            $"the OS holds alpha {run.After.Alpha} for the overlay after the card's lifecycle; a layered window the "
            + "OS holds no alpha for is present, on top and invisible");
    }

    [Fact]
    public void TheOverlaysOwnOracleStillEarnsAvailable_AfterACardHasHeldTheForeground()
    {
        // The strongest statement available without touching Overlay/**: the overlay capability's
        // OWN Available — which is downstream of eight OS round trips including the click-through
        // differential in both polarities — is re-earned after a card has come and gone. And the
        // differential is re-run here on the overlay itself, so "the point went elsewhere" cannot be
        // satisfied by an overlay that quietly stopped existing.
        var run = InputCaptureObservations.Coexistence;

        Assert.True(run.OverlayCatchesItsOwnPointWhenMadeOpaque == run.MachineHasInteractiveDesktop,
            "with WS_EX_TRANSPARENT cleared after the card's lifecycle, the overlay does NOT catch its own centre "
            + "point. Every pass-through assertion in this file is then also true of a window that is not there");
        Assert.True(run.OverlayStillEarnsAvailable == run.MachineHasInteractiveDesktop,
            "the overlay capability no longer earns Available after an input card held the foreground: "
            + InputCaptureObservations.Describe(run.OverlayRePresentState));
    }
}
