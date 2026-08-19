using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-112's real-desktop facts. They ask the operating system ONE thing the module's own facts
/// cannot: whether the video capability holds a picture this process COMPOSED as readily as one its
/// decoder produced — and whether the overlay and the input capability are unharmed by a module
/// that uses both of the others in one game.
///
/// <para><b>Nothing here is a claim that a human watched or counted anything.</b>
/// <c>watched-verified</c> and <c>answered-verified</c> are named manual gates; every number below
/// is an operating-system read-back.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class BubbleCountCapabilityTests
{
    private static BubbleCountObservations.PaintedRun Run => BubbleCountObservations.Painted;

    [Fact]
    public void TheOSsOwnMediaStackOpenedTheClip_AndThePainterReallyMarkedItsPicture()
    {
        var run = Run;

        // WITHOUT THIS LEG EVERY FACT BELOW IS A TEST OF NOTHING HAPPENING. The clip has to have
        // opened, a bubble has to have been spawned, and the painter has to have changed pixels the
        // decoder produced — measured on the FRAME, before the operating system was involved at all.
        Assert.Equal(run.MachineHasInteractiveDesktop, run.ClipOpened);
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(run.BubblesPainted > 0, "the run spawned no bubble at all");
        Assert.True(
            run.PaintedPixelsInFrame == run.BubblePointsSampled && run.BubblePointsSampled > 0,
            $"the painter changed {run.PaintedPixelsInFrame} of {run.BubblePointsSampled} sampled points "
            + "inside its own bubble; a painter that changed none would make every read-back below "
            + "vacuous");
    }

    [Fact]
    public void THEOPERATINGSYSTEMHoldsAPictureTHISPROCESSPainted_WhichIsWhatMakesTheSeamASeam()
    {
        var run = Run;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        // The capability's own differential, unchanged and unweakened: the bar it was told to paint
        // is really there AND every sampled picture point matches the frame it was HANDED. That the
        // frame was composed rather than decoded is invisible to it — which is precisely the
        // property that makes IVideoPresence a capability rather than a wrapper around one caller.
        Assert.IsType<CapabilityState.Available>(run.ShowState);
        Assert.True(run.FrameHeld, $"the OS's copy did not carry the composed picture: {run.ShowState}");
        Assert.True(run.PictureSampled > 0);
        Assert.Equal(run.PictureSampled, run.PictureMatched);

        // AND THE INDEPENDENT READ, through a call the product never makes (PrintWindow): the
        // operating system's own rendering of the window carries the BUBBLE's own pixels, exactly as
        // this process composed them.
        Assert.True(run.RenderedPixels > 0, "PrintWindow returned nothing at all");
        Assert.Equal(run.BubblePointsSampled, run.BubblePointsMatchingComposed);

        // AND THE POINTS ARE REALLY INSIDE THE BUBBLE. Without this the match above is satisfied by
        // background compared against background — the decoder's fixture is one flat colour, so
        // "not that colour" is exactly "something this process painted".
        Assert.Equal(run.BubblePointsSampled, run.BubblePointsUnlikeTheDecodersColour);
    }

    [Fact]
    public void AFTERTheClipComesDownTheQuestionTakesTheKeyboard_TheOrderSP111NeverMeasured()
    {
        var run = Run;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        // SP-111's coexistence run measured a card that ALREADY held the foreground while a video
        // surface came up (its §5 leg 5). This is the opposite order and it is this module's own:
        // a picture goes up, comes down, and only then is the keyboard asked for. Nothing in either
        // capability promised that would work, so it is measured rather than assumed.
        Assert.IsType<CapabilityState.Available>(run.CardPromptState);
        Assert.True(
            run.CardTookTheInputAfterTheVideo,
            "after the video surface came down, the question card did not hold both the foreground and "
            + "the system keyboard focus");
    }

    [Fact]
    public void THEOVERLAYIsUnharmedThroughTheWholeGame_MeasuredAtFourMoments()
    {
        var run = Run;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(run.OverlayPresented);

        // Five modules draw through this surface. A game that plays a picture and then takes the
        // keyboard must leave it exactly as it found it — click-through, above every ordinary
        // window, never the foreground, and still holding its own alpha.
        foreach (var (moment, reading) in new[]
        {
            ("before", run.OverlayBefore),
            ("during the clip", run.OverlayDuringClip),
            ("during the question", run.OverlayDuringCard),
            ("after", run.OverlayAfter),
        })
        {
            Assert.True(reading.PointPassesThrough, $"the overlay stopped being click-through {moment}");
            Assert.True(reading.TransparentStyleHeld, $"WS_EX_TRANSPARENT was lost {moment}");
            Assert.True(
                reading.AboveEveryOrdinaryWindow,
                $"the overlay left the topmost band {moment}");
            Assert.False(reading.IsForeground, $"the overlay became the foreground {moment}");
            Assert.Equal(153, reading.Alpha);
        }
    }

    [Fact]
    public void THEOVERLAYSOwnDifferentialAndItsOwnORACLEBothStillAnswer()
    {
        var run = Run;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        // Without the differential, "the point went past the overlay" is also true of an overlay
        // that stopped existing — SP-099's own lesson, and the reason this leg is re-run HERE on
        // the overlay itself rather than inherited from that packet.
        Assert.True(
            run.OverlayCatchesItsOwnPointWhenMadeOpaque,
            "with WS_EX_TRANSPARENT cleared the overlay did not catch its own centre, so the "
            + "pass-through readings above prove nothing");

        // And the capability's own oracle, re-asked after the whole game: eight OS round trips
        // including its own click-through differential.
        Assert.True(run.OverlayStillEarnsAvailable, run.OverlayRePresentState.ToString());
    }

    [Fact]
    public void TheGamesOwnGeometryIsDerivedFromUpstreamsNumbers_NotFromWhatHappenedToLookRight()
    {
        // Not a desktop fact — a pin on the constants the run above samples against, so a change to
        // the placement arithmetic moves the sample points with it rather than quietly sampling
        // background and still passing.
        Assert.Equal(120.0 / 1920.0, BubbleCountRun.MinDiameterFraction, precision: 10);
        Assert.Equal(225.0 / 1920.0, BubbleCountRun.MaxDiameterFraction, precision: 10);
        Assert.Equal(0.7, BubbleCountRun.PlacementXSpan);
        Assert.Equal(0.15, BubbleCountRun.PlacementXOffset);
        Assert.Equal(0.5, BubbleCountRun.PlacementYSpan);
        Assert.Equal(0.25, BubbleCountRun.PlacementYOffset);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), BubbleCountArithmetic.SpawnLeadIn);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), BubbleCountRun.MinLifetime);
        Assert.Equal(TimeSpan.FromMilliseconds(500), BubbleCountRun.LifetimeSpan);
    }
}
