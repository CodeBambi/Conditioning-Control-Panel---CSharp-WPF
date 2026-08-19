using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-111 trap 1, discharged by MEASUREMENT rather than by assumption: a video surface opening and
/// closing must not break the click-through overlay five modules draw through, and must not take the
/// foreground away from the Lock Card.
///
/// <para><b><c>Overlay/**</c> and <c>Input/**</c> were not edited.</b> They are CONSUMED: the run
/// builds a real <see cref="CcpClient.Desktop.Overlay.Win32OverlayPresence"/> and a real
/// <see cref="CcpClient.Desktop.Input.Win32InputPresence"/> and measures both through SP-099's and
/// SP-110's own instruments — <see cref="OverlayWindowProbe"/> and <see cref="InputWindowProbe"/>,
/// unmodified — before a video surface exists, while one is up holding a picture, and after it has
/// come down and been disposed.</para>
///
/// <para><b>Why the video surface is NOT the foreground.</b> It carries
/// <c>WS_EX_NOACTIVATE</c>, which is a deliberate divergence from upstream's own video window
/// (<c>Services/Video/VideoService.cs:2621</c>, <c>ShowActivated = withAudio</c>): the foreground is
/// LENT by the OS and Lock Card's entire capability is holding it. A video that took it would end a
/// question the user was being asked.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class VideoOverlayCoexistenceTests
{
    private static VideoSurfaceObservations.CoexistenceRun Run => VideoSurfaceObservations.Coexistence;

    [Fact]
    public void BothOtherCapabilitiesReallyWereUP_WithoutWhichEveryFactBelowTestsNothingHappening()
    {
        var run = Run;
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayPresented);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardTookTheInput);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardBefore.HoldsSystemKeyboardFocus);

        // And the SUBJECT really was up too, earning Available from the operating system while both
        // of the others held their own resources. Without this leg the coexistence facts would be
        // satisfied by a video capability that politely did nothing.
        Assert.True(
            run.VideoEarnedAvailableBesideThem,
            $"a video surface must be able to hold a picture while a click-through overlay is on screen and a "
            + $"lock card holds the foreground; the capability answered {run.VideoShowState}");
    }

    [Fact]
    public void TheOverlayStillLetsThePointerTHROUGHAtAllThreeMoments_AndKeepsItsTransparentStyle()
    {
        var run = Run;
        foreach (var (moment, reading) in Moments(run))
        {
            Assert.True(
                reading.PointPassesThrough,
                $"{moment}: the window manager must still route the overlay's own centre PAST it. This is "
                + "SP-099's whole capability and a video surface must not cost it");
            Assert.True(
                reading.TransparentStyleHeld,
                $"{moment}: WS_EX_TRANSPARENT must survive the video surface's whole lifecycle");
        }

        // The differential, taken on the overlay itself: with the flag cleared the same point routes
        // TO it, so "the point went elsewhere" cannot be satisfied by an overlay that stopped
        // existing.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.OverlayCatchesItsOwnPointWhenMadeOpaque);
    }

    [Fact]
    public void TheOverlayIsStillAboveEveryOrdinaryWindow_AndStillNotTheForeground_AtAllThreeMoments()
    {
        var run = Run;
        foreach (var (moment, reading) in Moments(run))
        {
            Assert.Equal(run.MachineHasInteractiveDesktop, reading.AboveEveryOrdinaryWindow);
            Assert.False(
                reading.IsForeground,
                $"{moment}: the overlay must never become the foreground window — it is WS_EX_NOACTIVATE and "
                + "always has been");

            // The OS still holds its uniform alpha: 0.6 of 255 is 153, and a layered window whose
            // attributes were lost reads -1 (SP-099's ghost detector).
            Assert.Equal(run.MachineHasInteractiveDesktop ? 153 : -1, reading.Alpha);
        }
    }

    [Fact]
    public void TheOverlayCapabilityStillEARNSAvailableAfterAVideoSurfaceHasComeAndGone()
    {
        var run = Run;
        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            run.OverlayStillEarnsAvailable);

        if (!run.OverlayStillEarnsAvailable)
        {
            Assert.IsNotType<CapabilityState.Available>(run.OverlayRePresentState);
            return;
        }

        // Not merely "the readings still look right": the overlay capability's OWN oracle is re-asked
        // and it re-runs eight OS round trips including its own click-through differential.
        Assert.IsType<CapabilityState.Available>(run.OverlayRePresentState);
    }

    [Fact]
    public void TheLOCKCARDKeepsTheForegroundAndTheKeyboard_WhileAVideoSurfaceIsUpAndAfterItGoes()
    {
        var run = Run;

        Assert.True(
            run.CardDuring.Visible == run.MachineHasInteractiveDesktop,
            "the card must still be on screen while a video surface is up");

        // THE TRAP-1 FACT. A video surface that stole the foreground would end a question the user
        // was being asked, and the module would keep reporting the card as live while every keystroke
        // went somewhere else — which is exactly the state SP-110's own probe measured and refused.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuring.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardDuring.HoldsSystemKeyboardFocus);

        // And it survives the surface coming DOWN as well: a withdraw that handed the foreground to
        // the desktop would be the same harm one moment later.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.IsForeground);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CardAfter.HoldsSystemKeyboardFocus);
    }

    private static IEnumerable<(string Moment, VideoSurfaceObservations.OverlayReading Reading)> Moments(
        VideoSurfaceObservations.CoexistenceRun run) =>
        [
            ("before any video surface existed", run.OverlayBefore),
            ("while a video surface was up holding a picture", run.OverlayDuring),
            ("after the video surface was withdrawn and disposed", run.OverlayAfter),
        ];
}
