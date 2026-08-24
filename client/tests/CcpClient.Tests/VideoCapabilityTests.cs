using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// What the operating system says about a video frame reaching a surface, and where the
/// chain stops.
///
/// <para><b>The distinction every fact in this file exists to hold open:</b> "frames decoded" is not
/// "frames displayed". The decoded count is measured and REPORTED; the displayed claim is a
/// read-back of the operating system's own copy of the surface, compared pixel for pixel against
/// the picture handed over, above a bar that must come back exactly the colour it was filled
/// with — and, where the machine allows it, the same picture read out of the COMPOSITED
/// DESKTOP.</para>
///
/// <para><b>What none of it proves.</b> That a human watched anything. <c>watched-verified</c> is a
/// named manual gate and no automated step on any platform discharges it, Windows included.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class VideoCapabilityTests : RealDesktopFacts
{
    private static VideoSurfaceObservations.Measurements Observed => VideoSurfaceObservations.Observed;

    // ---------------------------------------------------------------------------------------
    //  THE DECODE HALF — and it is named as the trap level, not as the claim
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheOperatingSystemOpensASynthesisedClipAndReportsItsOwnPictureSize()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.True(
            m.OpenState is CapabilityState.Available,
            $"the operating system's own media stack must open the fixture clip, and it answered {m.OpenState}. "
            + "The fixture is an uncompressed 32bpp AVI written by TestAvi in pure managed code — no encoder and "
            + "no codec — so a refusal here is Media Foundation's own, not the writer's");

        Assert.True(m.ClipInfo.Asked, "a clip that opened must report an ASKED observation, never a shaped zero");
        Assert.True(m.ClipInfo.HasPicture, $"the clip must report a real picture size; it reported {m.ClipInfo}");
        Assert.Equal(VideoSurfaceObservations.ClipWidth, m.ClipInfo.Width);
        Assert.Equal(VideoSurfaceObservations.ClipHeight, m.ClipInfo.Height);

        // The container says 100 000 microseconds per frame and three frames, and BOTH come back
        // from the OS rather than from the writer's own arithmetic.
        Assert.Equal(TimeSpan.FromMilliseconds(100), m.ClipInfo.FrameInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(300), m.ClipInfo.Duration);
    }

    [Fact]
    public void TheDecodedPictureComesBackTheRIGHTWAYUP_BecauseTheOperatingSystemHandsItBackBOTTOMUP()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;

        // Measured, not assumed: Media Foundation reports MF_MT_DEFAULT_STRIDE = -1280 for this
        // fixture. If that ever stops being true the fact still holds — what must never change is
        // the picture's orientation on the far side of the decoder.
        Assert.True(
            m.ClipInfo.BottomUp,
            "this fixture is stored bottom-up (AVI's positive biHeight) and the operating system reports a "
            + "negative default stride for it. If THAT is false the rest of this fact is testing nothing, "
            + $"because a top-down source cannot catch a missing flip. Clip: {m.ClipInfo}");

        Assert.Equal(VideoSurfaceObservations.ExpectedTop(0), m.DecodedTopColour);
        Assert.Equal(VideoSurfaceObservations.ExpectedBottom(0), m.DecodedBottomColour);
        Assert.True(
            m.DecodedTopColour != m.DecodedBottomColour,
            "the fixture's halves must differ, or a vertical flip would be invisible to this fact");
    }

    [Fact]
    public void AFileTheOperatingSystemCannotDecode_RefusesTYPED_AndTheRefusalIsAboutTheFILE_NotTheSurface()
    {
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        var path = Path.Combine(VideoSurfaceObservations.MediaFolder, "not-really-a-clip.avi");
        Directory.CreateDirectory(VideoSurfaceObservations.MediaFolder);
        File.WriteAllText(path, "this is not a RIFF file and no decoder on earth will open it");

        var state = source.Open(path, out var clip);
        Assert.Null(clip);
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);

        // NO SKIP: asking for the WINDOWS decoder from a process that is not on Windows is a thing
        // this suite does on purpose (VideoPresenceFactory.CreateClipSourceFor says so), and the
        // answer there is the absent-mechanism refusal rather than a verdict about the file — the
        // factory never hands out Media Foundation off Windows, so no file is ever read. Both codes
        // are typed refusals and both are the truth about their platform.
        Assert.Equal(
            OperatingSystem.IsWindows()
                ? VideoReasonCodes.VideoClipUnreadable
                : VideoReasonCodes.VideoMechanismAbsent,
            unavailable.Reason.Code);

        // The whole point of two families of code: a bad file must never be reported as a dead
        // surface, because the next clip in the pool may play perfectly.
        Assert.DoesNotContain("surface", unavailable.Reason.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingFileRefusesTyped_AndNothingIsOpened()
    {
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        var state = source.Open(
            Path.Combine(VideoSurfaceObservations.MediaFolder, "no-such-clip-at-all.avi"), out var clip);

        Assert.Null(clip);
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);

        // Same platform pair as the fact above, and the same reason: off Windows nothing is opened
        // because there is no decoder to open it with, which the refusal says in type.
        Assert.Equal(
            OperatingSystem.IsWindows()
                ? VideoReasonCodes.VideoClipUnreadable
                : VideoReasonCodes.VideoMechanismAbsent,
            unavailable.Reason.Code);
    }

    [Fact]
    public void DecodedFramesAndFramesTheOperatingSystemHOLDS_AreTwoDifferentNumbers_AndBothAreReported()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;

        // Three decoded pictures. On its own this proves a FILE is readable and nothing more — the
        // same grade of evidence as a tray method returning or Play() returning, and this port has
        // rejected it four times under other names.
        Assert.Equal(3, m.DecodedFrames);

        // Four presents were confirmed HELD by the operating system: the three pictures plus the
        // still-frame control. THIS is the displayed number, and it comes from a read-back.
        Assert.Equal(4, m.FramesHeldCount);

        // Of those, three CHANGED the OS's copy — the still frame did not, by construction.
        Assert.Equal(3, m.FramesAdvancedCount);
    }

    // ---------------------------------------------------------------------------------------
    //  THE SURFACE HALF
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheOperatingSystemHoldsTheSurfaceAboveEveryOrdinaryWindow_AndNEVERMakesItTheForeground()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.True(
            m.PresentState is CapabilityState.Available,
            $"placing the surface must be earned from the OS, and it answered {m.PresentState}");

        Assert.Equal(
            m.MachineHasInteractiveDesktop, m.ZOrderAfterPresent.AboveEveryOrdinaryWindow);
        Assert.Equal(
            (VideoSurfaceObservations.SubjectBounds.X, VideoSurfaceObservations.SubjectBounds.Y,
                VideoSurfaceObservations.SurfaceWidth, VideoSurfaceObservations.SurfaceHeight),
            m.HeldBounds);

        // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST.
        Assert.Equal(0x00080000u, m.ExStyleAfterPresent & 0x00080000u);
        Assert.Equal(0x08000000u, m.ExStyleAfterPresent & 0x08000000u);
        Assert.Equal(0x00000008u, m.ExStyleAfterPresent & 0x00000008u);

        // WS_EX_TRANSPARENT must NOT be set: this surface is a deliberate click sink, which is
        // upstream's own behaviour (VideoService.cs:2894-2907 swallows every press, :2862-2874 exists
        // to catch them) and is declared at the creation site in Win32VideoPresence.EnsureWindow.
        //
        // THIS LINE IS THE STYLE AND NOT THE POLICY. It is a read-back of what the OS holds, so it
        // catches a write that was never made — but the overlay measured a run where every style
        // write SUCCEEDED and the ex-style read back wrong anyway (Win32OverlayPresence.cs:504-511),
        // so it cannot carry the input claim on its own. VideoInputRoutingTests pins the OUTCOME:
        // where the window manager routes a point, and where a real injected click actually lands.
        Assert.Equal(0u, m.ExStyleAfterPresent & 0x00000020u);

        // THE TRAP-1 FACT. Five modules draw through a click-through overlay and Lock Card takes the
        // foreground; a video surface that took it would take it from a question the user is being
        // asked. Upstream's own video window DOES activate (VideoService.cs:2624) and this port's
        // deliberately does not — divergence D122.
        Assert.False(
            m.SurfaceIsForeground,
            "the video surface must never become the foreground window: WS_EX_NOACTIVATE is set for exactly "
            + "that reason, and Lock Card's whole capability is the foreground it would be taking");
    }

    [Fact]
    public void TheOperatingSystemsOwnCopyOfTheSurfaceCarriesTheDECODEDPicture_OverABarItReadsBackEXACTLY()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.NotEmpty(m.Frames);

        for (var i = 0; i < m.Frames.Count; i++)
        {
            var frame = m.Frames[i];
            Assert.True(
                frame.ShowState is CapabilityState.Available,
                $"frame {i}: showing a picture must be earned from the OS, and it answered {frame.ShowState}");

            Assert.True(
                frame.LetterboxHeld,
                $"frame {i}: the bar control point must come back EXACTLY the colour the painter filled it with. "
                + "Without this clause the picture count means nothing — the surface's window class registers no "
                + "background brush, so an UNPAINTED window's device context holds whatever the OS left in it and "
                + "reads as 'not the background' just as reliably as a painted one (M-t)");

            Assert.Equal(9, frame.PictureSampled);
            Assert.Equal(frame.PictureSampled, frame.PictureMatched);
            Assert.True(frame.FrameHeld, $"frame {i}: bar={frame.LetterboxHeld} matched={frame.PictureMatched}/{frame.PictureSampled}");
            Assert.True(frame.Confirmed, $"frame {i}: {frame.ShowState}");
            Assert.Equal(m.Window, frame.HitTestWinner);
        }
    }

    [Fact]
    public void TheSurfacesOwnPixelsCHANGEBetweenTwoDifferentFrames_WhichNoCountOfDecodedBuffersCanShow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.True(m.Frames.Count >= 3, $"the fixture must present at least three frames; it presented {m.Frames.Count}");

        // FRAME 0 CARRIES NO EVIDENCE HERE, and saying so is the whole point of this comment.
        // Win32VideoPresence.Present resets BOTH sample slots to zero, so the first Show compares its
        // read-back against a SENTINEL rather than against a fold taken after the letterbox fill: any
        // non-zero fold "advances". The LOAD-BEARING advances are frames 1 and 2, each measured
        // against the fold of the frame really before it, plus the still-frame control below, which is
        // the negative half of the same differential. Frame 0 is asserted for uniformity, not for proof.
        for (var i = 0; i < m.Frames.Count; i++)
        {
            Assert.True(
                m.Frames[i].SurfaceAdvanced,
                $"frame {i}: the operating system's own copy of the surface must CHANGE when a different picture "
                + "is handed over. This is the differential the whole packet turns on: a decoder that returns "
                + "bytes into a dead surface satisfies every other check in this file");
            Assert.True(m.Frames[i].PictureIsMoving, $"frame {i}: the dot's seventh meaning must be true while frames advance");
        }
    }

    [Fact]
    public void AFrameHandedOverTWICE_LeavesTheSurfaceUnchanged_AndIsStillALivePicture()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;

        Assert.True(
            m.StillFrameShowState is CapabilityState.Available,
            $"a repeated picture is still held by the OS, and Show answered {m.StillFrameShowState}");

        Assert.False(
            m.StillFrameSurfaceAdvanced,
            "handing over the picture that is already up must NOT advance the surface — if it did, the "
            + "differential would be measuring the blit rather than the content and a frozen video would pass");

        Assert.True(
            m.StillFramePictureIsMoving,
            "and it must still count as ALIVE. A video may legitimately hold a still frame, so the seventh "
            + "meaning is 'the OS's copy changed WHERE A DIFFERENT PICTURE WAS HANDED OVER', not a bare "
            + "requirement that pixels differ");
    }

    [Fact]
    public void TheOperatingSystemsOwnRENDERINGOfTheWindow_MatchesThePortsCompositionPixelForPixel()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        var area = VideoSurfaceObservations.SurfaceWidth * VideoSurfaceObservations.SurfaceHeight;
        Assert.NotEmpty(m.Frames);

        foreach (var frame in m.Frames)
        {
            // PrintWindow: the OS renders the window into a bitmap of the caller's. The product
            // never makes this call, so a read-back that agreed with the product's own device-context
            // read only because both used the same declarations cannot happen here.
            Assert.Equal(area, frame.RenderedPixels);
            Assert.Equal(area, frame.RenderedMatchingComposed);
        }
    }

    [Fact]
    public void TheCOMPOSITEDDESKTOPCarriesTheDecodedPicture_AndItChangesFrameByFrame()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;

        if (!m.DesktopCaptureIsLive)
        {
            // NOT a skip: the expectation flips with the machine and the numbers travel either way.
            // A foreign topmost window can own the point a surface is at — measured, the shipping WPF
            // product's own window did exactly that while this packet was written — and no in-process
            // mechanism can exclude one (RealDesktopCollection's own doc says so).
            Assert.True(
                m.Frames.All(f => f.FrameHeld),
                "this machine's desktop capture could not see the CONTROL surface (a landed overlay capability "
                + $"painted 0x7B2C4D at a disjoint rectangle and the capture read 0x{m.ControlDesktopColour:X6}), "
                + "so the composited leg is not evidence here. The window-level read-back must still hold, and "
                + "that is what is asserted instead — never a skip");
            return;
        }

        for (var i = 0; i < m.Frames.Count; i++)
        {
            Assert.Equal(VideoSurfaceObservations.ExpectedTop(i), m.Frames[i].DesktopTop);
            Assert.Equal(VideoSurfaceObservations.ExpectedBottom(i), m.Frames[i].DesktopBottom);
        }

        Assert.True(
            m.Frames.Select(f => f.DesktopTop).Distinct().Count() == m.Frames.Count,
            "every frame must put a DIFFERENT colour on the composited desktop; identical readings would mean "
            + "the screen was holding one still picture while the decoder ran");
    }

    [Fact]
    public void WithdrawTakesTheSurfaceDown_AndTheOperatingSystemAgrees()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.True(
            m.WithdrawState is CapabilityState.Available,
            $"withdrawing must be earned from the OS, and it answered {m.WithdrawState}");
        Assert.False(m.VisibleAfterWithdraw);
        Assert.False(m.HitTestAfterWithdraw);
        Assert.False(
            m.MovingAfterWithdraw,
            "a withdrawn surface is not a moving picture, whatever the decoder is still doing");
    }

    [Fact]
    public void EveryRefusalIsTyped_AndNamesWHICHLinkOfTheChainFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.Equal(VideoReasonCodes.VideoNothingPresented, m.ShowBeforePresentCode);
        Assert.Equal(VideoReasonCodes.VideoNothingPresented, m.ShowAfterWithdrawCode);
        Assert.Equal(VideoReasonCodes.VideoPresenceDisposed, m.ShowAfterDisposeCode);
        Assert.True(m.WindowGoneAfterDispose, "a disposed presence must leave no window behind");
    }

    [Fact]
    public void TheDisplayReadBackIsTheNECESSARYCondition_AndItIsAskedOfTheOperatingSystem()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var m = Observed;
        Assert.True(m.DisplayObservation.Asked);
        Assert.Equal(m.MachineHasInteractiveDesktop, m.DisplayObservation.WindowStationVisible);
        Assert.True(m.DisplayObservation.DisplayCount >= 1);
        Assert.True(
            m.DisplayObservation.CompositionEnabled,
            "DwmIsCompositionEnabled is the one DWM read this capability takes. Its sibling "
            + "DwmGetCompositionTimingInfo is deliberately NOT read: the shipping dwmapi.dll on the machine "
            + "this was written on accepts a 292-byte struct where the SDK header declares 320 "
            + "(MILERR_MISMATCHED_SIZE, winerror.h:60848), and its counters are system-wide anyway");
        Assert.True(m.DisplayObservation.MediaStackUsable);
        Assert.True(m.DisplayObservation.Confirmed);
    }

    // ---------------------------------------------------------------------------------------
    //  PREDICATE ISOLATION — one clause at a time, so a mutation has somewhere to bite
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Asked")]
    [InlineData("WindowExists")]
    [InlineData("WindowVisible")]
    [InlineData("AboveEveryOrdinaryWindow")]
    [InlineData("HitTest")]
    [InlineData("LetterboxHeld")]
    [InlineData("PictureMatched")]
    public void EveryClauseOfConfirmed_IsLoadBearing(string dropped)
    {
        var whole = Confirmable();
        Assert.True(whole.Confirmed, "the control must be confirmed, or dropping a clause proves nothing");

        var broken = dropped switch
        {
            "Asked" => whole with { Asked = false },
            "WindowExists" => whole with { WindowExists = false },
            "WindowVisible" => whole with { WindowVisible = false },
            "AboveEveryOrdinaryWindow" => whole with { AboveEveryOrdinaryWindow = false },
            "HitTest" => whole with { HitTestWinner = 0x999 },
            "LetterboxHeld" => whole with { LetterboxHeld = false },
            "PictureMatched" => whole with { PictureMatched = whole.PictureSampled - 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(dropped), dropped, "unknown clause"),
        };

        Assert.False(broken.Confirmed, $"dropping '{dropped}' must break Confirmed");
    }

    [Fact]
    public void AnObservationThatWasNeverASKED_IsNotConfirmed_WhateverItsOtherFieldsSay()
    {
        Assert.False(VideoSurfaceObservation.NotAsked.Confirmed);
        Assert.False(VideoSurfaceObservation.NotAsked.FrameHeld);
        Assert.False(VideoSurfaceObservation.NotAsked.SurfaceAdvanced);
        Assert.False(VideoSurfaceObservation.NotAsked.PictureIsMoving);

        // Every other field can be perfect and it is still not asked.
        var lying = Confirmable() with { Asked = false };
        Assert.False(lying.Confirmed);
        Assert.False(lying.FrameHeld);

        // And the ADVANCE clause carries its own Asked, which is not redundant with the others: two
        // remembered sample values that happen to differ must not be reported as an advance nobody
        // measured.
        Assert.False((Confirmable() with { Asked = false, SurfaceSample = 9, PreviousSurfaceSample = 4 }).SurfaceAdvanced);
        Assert.True((Confirmable() with { SurfaceSample = 9, PreviousSurfaceSample = 4 }).SurfaceAdvanced);
    }

    [Fact]
    public void ASurfaceTAKESItsOwnPointBackFromAWindowPlacedOverIt_AndThatIsWhyOcclusionCannotBeStaged()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var edges = VideoSurfaceObservations.Edges;

        // MEASURED, and it is the reason this packet's occlusion refusal has no positive fact behind
        // it. An OPAQUE topmost window was placed over the surface's own centre and raised after it;
        // the surface's next Present re-asserted the topmost band (MaxBandAttempts) and WON THE POINT
        // BACK. That is the product behaving as designed — upstream re-asserts HWND_TOPMOST on a
        // cadence for exactly this reason (Services/Flash/FlashService.cs:206-243) — and it means a
        // STATIC thief cannot stage an occlusion, while a thief that kept re-asserting would be a
        // race by construction rather than a fact. So `video-surface-occluded` is reachable in the
        // wild (it happened unaided on this machine while the packet was written, plan.md §0 Q4) and
        // is NOT covered by an assertion. Recorded, not papered over.
        Assert.Equal(
            edges.MachineHasInteractiveDesktop ? "(not a refusal)" : VideoReasonCodes.VideoNoDisplay,
            edges.OccludedPresentCode);
        Assert.False(
            edges.OccludedIsTheThief,
            "after the band was re-asserted the window manager must route the surface's centre back to "
            + $"the SURFACE; it answered {OverlayWindowProbe.DescribeWindow(edges.OccludedHitTestWinner)}");
    }

    [Fact]
    public void AWindowREADBACKIsAboutTheOperatingSystemsCopy_NOTAboutAnyMonitor_AndThatLimitIsMeasured()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var edges = VideoSurfaceObservations.Edges;

        // A surface at (-8000,-8000) — a rectangle NO MONITOR COVERS — still passes every window
        // check AND still reads back the decoded picture, because `GetDC(hwnd)` answers about the
        // copy the operating system holds FOR THE WINDOW rather than about anything on a screen.
        //
        // THIS IS THE CEILING OF THE `frame-on-surface` CLASS AND IT IS ASSERTED RATHER THAN
        // CONFESSED IN PROSE: a fact that claimed a window read-back proves a user could see
        // something would be false, and the class that does reach a screen is
        // `desktop-composited`, which is machine-conditional and covered separately.
        Assert.Equal("(not a refusal)", edges.OffScreenShowCode);
        Assert.True(edges.OffScreenFrameHeld);
        Assert.True(edges.OffScreenLetterboxHeld);
    }

    [Fact]
    public void AFileWithNoVIDEOSTREAMSaysSO_RatherThanBlamingThePixelFormat()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "the measured run decodes through Media Foundation and reads a REAL Win32 surface back out of "
            + "the operating system; both are Windows mechanisms. Off Windows the factory hands back "
            + "UnsupportedVideoClipSource and UnsupportedVideoPresence and nothing is attempted, which "
            + "Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass and "
            + "Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows assert on "
            + "every platform");

        var edges = VideoSurfaceObservations.Edges;
        Assert.Equal(VideoReasonCodes.VideoClipUnreadable, edges.AudioOnlyOpenCode);

        // ONE code is reached by several causes, so the DETAIL has to separate them: an audio-only
        // file and a container whose video track this machine cannot convert are different problems
        // and a bug report that cannot tell them apart sends the reader after the wrong one.
        Assert.Contains("reports NO video stream in it", edges.AudioOnlyOpenDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("RGB32", edges.AudioOnlyOpenDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void HitTestRoutesHere_IsFalseForNoWindowAtAll_Because0EqualsZero()
    {
        var nothing = Confirmable() with { Window = 0, HitTestWinner = 0 };
        Assert.False(
            nothing.HitTestRoutesHere,
            "an observation about NO window must not report that a hit test routes to it; without the "
            + "non-zero guard, 0 == 0 says it does");
    }

    [Fact]
    public void FrameHeld_NeedsTheBarANDTheMatch_AndSamplingNOTHINGIsNotAPerfectMatch()
    {
        Assert.True(Confirmable().FrameHeld);
        Assert.False(Confirmable() with { LetterboxHeld = false } is { FrameHeld: true });
        Assert.False((Confirmable() with { PictureMatched = 0 }).FrameHeld);
        Assert.False(
            (Confirmable() with { PictureSampled = 0, PictureMatched = 0 }).FrameHeld,
            "0 of 0 points matching is a read-back that sampled nothing, not a perfect match. Without the "
            + "PictureSampled > 0 clause a surface nobody could read would report a held frame");
    }

    [Fact]
    public void PictureIsMoving_IsALIVEForARepeatedStill_AndDEADForAFrozenChange()
    {
        var advancing = Confirmable() with { FrameDiffersFromPrevious = true, SurfaceSample = 7, PreviousSurfaceSample = 6 };
        Assert.True(advancing.PictureIsMoving);

        var still = Confirmable() with { FrameDiffersFromPrevious = false, SurfaceSample = 7, PreviousSurfaceSample = 7 };
        Assert.True(still.PictureIsMoving, "a repeated still is a legitimate video, not a dead one");

        var frozen = Confirmable() with { FrameDiffersFromPrevious = true, SurfaceSample = 7, PreviousSurfaceSample = 7 };
        Assert.False(
            frozen.PictureIsMoving,
            "THE SEVENTH MEANING: a DIFFERENT picture was handed over and the operating system's copy did not "
            + "change. Every call succeeded, the decoder is running, and the screen is frozen — which is "
            + "upstream's own worst failure (VideoService.cs:2677-2678) and the exact state a decoded-frame "
            + "count reports as healthy");

        var blank = Confirmable() with { LetterboxHeld = false, FrameDiffersFromPrevious = true, SurfaceSample = 7, PreviousSurfaceSample = 6 };
        Assert.False(blank.PictureIsMoving, "a surface with no frame held is not moving, however much it changes");
    }

    [Theory]
    [InlineData("Asked")]
    [InlineData("WindowStationVisible")]
    [InlineData("DisplayCount")]
    [InlineData("CompositionEnabled")]
    [InlineData("MediaStackUsable")]
    public void EveryClauseOfTheDisplayObservation_IsLoadBearing(string dropped)
    {
        var whole = new VideoDisplayObservation(true, true, 1, true, true);
        Assert.True(whole.Confirmed);

        var broken = dropped switch
        {
            "Asked" => whole with { Asked = false },
            "WindowStationVisible" => whole with { WindowStationVisible = false },
            "DisplayCount" => whole with { DisplayCount = 0 },
            "CompositionEnabled" => whole with { CompositionEnabled = false },
            "MediaStackUsable" => whole with { MediaStackUsable = false },
            _ => throw new ArgumentOutOfRangeException(nameof(dropped), dropped, "unknown clause"),
        };

        Assert.False(broken.Confirmed, $"dropping '{dropped}' must break the display observation");
        Assert.False(VideoDisplayObservation.NotAsked.Confirmed);
    }

    [Fact]
    public void AClipInfoThatWasNeverAsked_HasNoPicture_AndNeitherDoesADegenerateOne()
    {
        Assert.False(VideoClipInfo.NotAsked.HasPicture);
        Assert.False(new VideoClipInfo(true, 0, 240, TimeSpan.Zero, TimeSpan.Zero, false).HasPicture);
        Assert.False(new VideoClipInfo(true, 320, 0, TimeSpan.Zero, TimeSpan.Zero, false).HasPicture);
        Assert.False(new VideoClipInfo(false, 320, 240, TimeSpan.Zero, TimeSpan.Zero, false).HasPicture);
        Assert.True(new VideoClipInfo(true, 320, 240, TimeSpan.Zero, TimeSpan.Zero, false).HasPicture);
    }

    // ---------------------------------------------------------------------------------------
    //  LINUX REFUSES IN TYPE
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Linux_RefusesTyped_AndNamesTheSixStepGateIncludingTheOneWaylandCannotPass()
    {
        var presence = VideoPresenceFactory.CreateFor(VideoHostPlatform.Linux);
        var state = presence.Present(new VideoSurfaceRequest(new VideoBounds(0, 0, 320, 240)));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(VideoReasonCodes.VideoMechanismAbsent, unavailable.Reason.Code);

        // The gate travels WITH the refusal, not only in a record file.
        Assert.Contains("MANUAL GATE (Linux, undischarged)", unavailable.Reason.Detail, StringComparison.Ordinal);
        foreach (var step in new[] { "(1)", "(2)", "(3)", "(4)", "(5)", "(6)" })
        {
            Assert.Contains(step, VideoPresenceFactory.LinuxManualGate, StringComparison.Ordinal);
        }

        // The honest part: under Wayland a client cannot read back its own composited window, so the
        // expected outcome there is that the PROOF is unavailable even where the picture works.
        Assert.Contains("Wayland", VideoPresenceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("read its own composited", VideoPresenceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("no automated step on ANY platform", VideoPresenceFactory.LinuxManualGate, StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_TheDECODERRefusesToo_BecauseBothHalvesAreAbsentAndOnlyOneOfThemIsAboutWindows()
    {
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Linux);
        Assert.False(source.MediaStackUsable);

        var state = source.Open("/tmp/anything.mp4", out var clip);
        Assert.Null(clip);
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(VideoReasonCodes.VideoMechanismAbsent, unavailable.Reason.Code);
        Assert.Contains("GStreamer", unavailable.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxReadBacksAreNOTASKED_NotShapedZeroes()
    {
        var presence = VideoPresenceFactory.CreateFor(VideoHostPlatform.Linux);
        Assert.False(presence.Observe().Asked);
        Assert.False(presence.ObserveDisplay().Asked);
        Assert.False(presence.PictureIsMoving);
        Assert.False(presence.CanReachADisplay);
        Assert.False(presence.IsPresenting);
        Assert.Equal(0, presence.FramesHeld);
        Assert.Equal(0, presence.FramesAdvanced);
    }

    [Theory]
    [InlineData(VideoHostPlatform.Linux)]
    [InlineData(VideoHostPlatform.MacOs)]
    [InlineData(VideoHostPlatform.Unknown)]
    public void SelectionIsNotAvailability_NoUnsupportedBranchEverProducesAvailable(VideoHostPlatform platform)
    {
        var presence = VideoPresenceFactory.CreateFor(platform);
        Assert.IsType<CapabilityState.Unavailable>(presence.Present(new VideoSurfaceRequest(new VideoBounds(0, 0, 4, 4))));
        Assert.IsType<CapabilityState.Unavailable>(presence.Show(VideoFrame.Solid(4, 4, 1, 2, 3)));
        Assert.IsType<CapabilityState.Unavailable>(presence.Withdraw());
        Assert.IsType<CapabilityState.Unavailable>(presence.LastShow);
        Assert.IsType<CapabilityState.Unavailable>(
            VideoPresenceFactory.CreateClipSourceFor(platform).Open("anything", out _));
    }

    [Fact]
    public void TheFactoryNeverReturnsNullAndNeverThrows_OnEitherSideOfItsOwnPlatformGuard()
    {
        // The Windows arm of both factories is an if rather than a switch arm, because the backend
        // is annotated [SupportedOSPlatform("windows")]. The property asserted here is the one that
        // must hold on BOTH sides of that guard and needs no platform predicate to state: asking for
        // the Windows backend always produces a usable object, never null and never an exception —
        // on the far side it is a typed refusal instead.
        using var presence = VideoPresenceFactory.CreateFor(VideoHostPlatform.Windows);
        Assert.NotNull(presence);
        Assert.NotNull(VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows));
        using var current = VideoPresenceFactory.Create();
        Assert.NotNull(current);
        Assert.NotNull(VideoPresenceFactory.CreateClipSource());

        // And a source asked for a file that is not there answers rather than throwing, which is the
        // same property one layer down.
        var state = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows)
            .Open(Path.Combine(VideoSurfaceObservations.MediaFolder, "definitely-absent.avi"), out var clip);
        Assert.Null(clip);
        Assert.IsType<CapabilityState.Unavailable>(state);
    }

    /// <summary>An observation with every clause satisfied, so a theory can break exactly one.</summary>
    private static VideoSurfaceObservation Confirmable() =>
        new(
            Asked: true,
            Window: 0x1234,
            WindowExists: true,
            WindowVisible: true,
            HeldBounds: new VideoBounds(10, 20, 400, 240),
            AboveEveryOrdinaryWindow: true,
            HitTestWinner: 0x1234,
            LetterboxHeld: true,
            PictureSampled: 9,
            PictureMatched: 9,
            SurfaceSample: 7,
            PreviousSurfaceSample: 6,
            FrameDiffersFromPrevious: true,
            FramesPresented: 1);
}
