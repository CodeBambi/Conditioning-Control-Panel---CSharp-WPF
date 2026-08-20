using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-100 — the draw path: pixels reaching a real surface, and the operating system asked what it
/// holds afterwards.
///
/// <para><b>The shape of every effect fact here.</b> Compare a MACHINE property, measured by
/// <see cref="FlashPixelProbe"/> with its own P/Invokes — does this session have an interactive
/// desktop, and can a desktop capture see a painted layered window at all — against what the OS
/// reports about the product's surface. A build that draws nothing fails; a build whose instrument
/// has degenerated into "yes" fails the control; neither branch is a skip and no assertion sits
/// behind a conditional.</para>
///
/// <para><b>What these facts do NOT prove.</b> That a human saw a flash. A composited desktop read
/// from inside the process is the strongest thing a process can say about its own pixels, and it is
/// still not the headed capture <c>verification-harness.md</c> requires: it cannot see a Magnifier,
/// a mirror driver, an exclusive-fullscreen swap chain, colour management, or a monitor that is
/// physically off. <c>presentation-verified</c> is the orchestrator's capture and is not claimed
/// here. Nor do they prove anything at all on Linux, where the overlay refuses by design (SP-099
/// D56) and the facts below assert that the refusal is complete.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class FlashDrawTests
{
    // ---------------------------------------------------------------------------------
    //  the instruments, and their controls
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheDesktopCaptureInstrument_CanSeeAPaintedLayeredWindowOfItsOwn_OrSaysItCannot()
    {
        // Every composited-pixel expectation in this file is compared against this measurement.
        // The control is a layered, topmost, GDI-painted window built with the PROBE's own calls,
        // so a machine where a screen read is not a screen (locked, no compositor, some remote
        // sessions) reports "not live" HERE rather than turning the flash's fact red for a reason
        // that has nothing to do with the flash.
        var run = FlashDrawObservations.Run;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.ControlWindowRendersItsColour);
        Assert.True(run.ControlWindowGoneAfterTeardown,
            "the probe's own control window survived its teardown, so the instrument cannot say 'gone' and "
            + "nothing it reports about windows can be trusted");
        Assert.False(run.DesktopCaptureIsLive && !run.MachineHasInteractiveDesktop,
            "a desktop capture reported the control window's colour on a session with no interactive desktop — "
            + "the capture is reading something that is not the screen, and every composited fact that trusts it "
            + "is worthless");
    }

    [Fact]
    public void EveryCompositedReadIsOrderedBehindTheCompositor_OrEveryNumberBelowIsACoinFlip()
    {
        // SP-116. THE RESIDUE SP-107 §4 LEFT OPEN, AND ITS PIN.
        //
        // Between "this process showed and painted a layered top-most window" and "this process
        // read the screen" there was no happens-before edge. Measured with a rig that replicates
        // the control window below, on a loaded pool: 34 misses in 1200 unfenced reads and 0 in 900
        // fenced ones, and on EVERY miss the window owned its own centre point by WindowFromPoint
        // and rendered its own colour through PrintWindow — so the window was up, on top, and
        // painted, and the compositor simply had not published it yet. Occlusion by a foreign
        // window is refuted by that control, not assumed away. Decomposed: GdiFlush alone changes
        // nothing (8 in 300), DwmFlush alone is the whole effect (0 in 300).
        //
        // This fact exists so that deleting FlashPixelProbe's fence reds the suite ON EVERY RUN
        // instead of restoring a 5 % intermittent that took three packets to name. It is one
        // implication and it is not vacuous: where the capture is live, the fence must have been
        // taken and DWM must have granted it. Where composition is off, DesktopCaptureIsLive is
        // false and every composited expectation in this file is already false == false.
        var run = FlashDrawObservations.Run;

        Assert.True(
            !run.DesktopCaptureIsLive || run.CompositorFenceHeld,
            "a desktop capture is live on this machine and the compositor fence was NOT held "
            + $"(FlashPixelProbe.LastCompositorFenceResult = 0x{FlashPixelProbe.LastCompositorFenceResult:X8}; "
            + $"{FlashPixelProbe.FenceNotTaken:X8} means no fence was taken at all, "
            + $"{FlashPixelProbe.FenceUnavailable:X8} means dwmapi was not there to ask, anything else is DWM "
            + "refusing). Every composited number in this file was then read with no ordering against the "
            + "thing that publishes pixels, which SP-116 measured as 34 misses in 1200 reads. This is not a "
            + "flake to be re-run: either the fence was removed from FlashPixelProbe.CaptureDesktop, or this "
            + "session has no compositor and cannot make composited claims at all");
    }

    [Fact]
    public void TheDesktopReadIsMappedThroughTheOperatingSystemsOwnDpiRatio_NotAssumedToBeOne()
    {
        // The trap that cost SP-100 four measurement rounds: this test host is DPI-unaware, so
        // USER32 virtualises window coordinates while the screen device context stays physical.
        // Reading the desktop at a window's own coordinates samples the wrong point and reports
        // whatever is behind the surface — a perfectly visible flash looks invisible.
        var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;
        var run = FlashDrawObservations.Run;

        Assert.Equal(run.MachineHasInteractiveDesktop, virtualWidth > 0);
        Assert.Equal(run.MachineHasInteractiveDesktop, physicalWidth > 0);
        Assert.Equal(
            run.MachineHasInteractiveDesktop ? physicalWidth / (double)Math.Max(1, virtualWidth) : 0.0,
            run.VirtualToPhysicalRatio,
            3);
    }

    // ---------------------------------------------------------------------------------
    //  THE PACKET: the surface holds the frame, and the desktop shows it
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheOperatingSystemRendersTheSurfaceAsExactlyTheFrameThatWasPainted()
    {
        // Not "BitBlt returned TRUE". The OS is asked to render the window into a bitmap this test
        // owns, through a call the product never makes, and EVERY pixel must be the frame's — which
        // also pins the row order: the frame is two-tone, so a vertically flipped DIB (the silent
        // classic of this mechanism) fails here rather than shipping upside down.
        var run = FlashDrawObservations.Run;
        var pixels = FlashDrawObservations.SurfaceWidth * FlashDrawObservations.SurfaceHeight;

        Assert.Equal(run.MachineHasInteractiveDesktop ? pixels : 0, run.RenderedPixelCount);
        Assert.True(run.RenderedPixelsMatchingFrame == (run.MachineHasInteractiveDesktop ? pixels : 0),
            $"the OS rendered {run.RenderedPixelsMatchingFrame} of {run.RenderedPixelCount} pixels as the frame "
            + $"(interactive desktop = {run.MachineHasInteractiveDesktop}). Backend said: "
            + $"{OverlayObservations.Describe(run.PaintState)}");
        Assert.True(run.PaintClaimedAvailable == run.MachineHasInteractiveDesktop,
            $"the backend claimed the paint Available = {run.PaintClaimedAvailable} on a machine whose "
            + $"interactive-desktop state is {run.MachineHasInteractiveDesktop}. Backend said: "
            + $"{OverlayObservations.Describe(run.PaintState)}");
    }

    [Fact]
    public void TheCompositedDesktopHoldsTheFlashWhereTheSurfaceIs_BothHalvesTheRightWayUp()
    {
        // The closest a process can get to "a human sees it": the desktop, captured with CAPTUREBLT
        // (the only screen read that includes layered windows), at the surface's own rectangle,
        // with no wall-clock wait anywhere: the read is ordered behind DWM's own next present
        // (SP-116, FlashPixelProbe.CaptureDesktop), which is an edge on the producer rather than a
        // deadline this suite chose, and nothing is re-read. Both halves are checked, so a flash
        // that composited upside down or in the wrong place fails.
        var run = FlashDrawObservations.Run;

        Assert.True(
            (run.DesktopTopColour == FlashDrawObservations.TopColour) == run.DesktopCaptureIsLive,
            $"a desktop capture can see a painted layered window on this machine = {run.DesktopCaptureIsLive}, "
            + $"and the composited desktop at the top half of the flash reads 0x{run.DesktopTopColour:X6} against "
            + $"the frame's 0x{FlashDrawObservations.TopColour:X6}. Those two must agree: where the screen can be "
            + "read the flash must be ON it, and where it cannot, nothing may be claimed. Backend said: "
            + OverlayObservations.Describe(run.PaintState));
        Assert.True(
            (run.DesktopBottomColour == FlashDrawObservations.BottomColour) == run.DesktopCaptureIsLive,
            $"the bottom half of the flash reads 0x{run.DesktopBottomColour:X6} against the frame's "
            + $"0x{FlashDrawObservations.BottomColour:X6} (capture is live = {run.DesktopCaptureIsLive}) — the "
            + "frame reached the screen the wrong way up, or only half of it did");
    }

    [Fact]
    public void WithdrawingTheSurface_TakesTheFlashOffTheCompositedDesktopToo()
    {
        // The half of stop the user can see. A withdraw that only stops the CODE, leaving pixels on
        // the screen, is the residue a panic button exists to prevent.
        var run = FlashDrawObservations.Run;

        Assert.True(
            run.DesktopCaptureIsLive == (run.DesktopTopColour == FlashDrawObservations.TopColour
                && run.DesktopColourAfterWithdraw != FlashDrawObservations.TopColour),
            $"the differential did not close: capture live = {run.DesktopCaptureIsLive}, the desktop showed the "
            + $"flash = {run.DesktopTopColour == FlashDrawObservations.TopColour}, and after Withdraw it reads "
            + $"0x{run.DesktopColourAfterWithdraw:X6}. Where the screen can be read, the flash must be on it "
            + "BEFORE and gone AFTER; a withdraw that only clears the code's books leaves pixels on the user's "
            + "screen");
    }

    [Fact]
    public void PaintingCostsNothingTheGhostCheckNeeds_TheOsStillHoldsTheAlpha_AndAPlacementStillEarnsAvailable()
    {
        // THE DEFENCE OF THE CONTENT ROUTE, PINNED. Divergence D57 rejects UpdateLayeredWindow for
        // one reason: it is mutually exclusive with SetLayeredWindowAttributes, so taking it would
        // stop GetLayeredWindowAttributes answering for the window — and that answer is the port's
        // only measured discriminator against the exact defect the first attempt shipped
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45: WS_EX_LAYERED set, attributes
        // never given, the OS calls it visible and composites nothing).
        //
        // SP-099 asks the alpha immediately after Present, BEFORE any content exists. Nothing asked
        // it again on the far side of a draw. That is the hole the next packet walks into: D60 names
        // an alpha ramp as the natural next job, UpdateLayeredWindow is the obvious tool for one,
        // and without this fact the whole suite would stay green while the discriminator went quiet.
        var run = FlashDrawObservations.Run;
        var pixels = FlashDrawObservations.SurfaceWidth * FlashDrawObservations.SurfaceHeight;

        Assert.Equal(run.MachineHasInteractiveDesktop ? 255 : -1, run.AlphaHeldAfterPaint);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.RePresentAfterPaintClaimedAvailable);
        Assert.Equal(
            run.MachineHasInteractiveDesktop ? pixels : 0,
            run.RenderedPixelsMatchingFrameAfterRePresent);
    }

    // ---------------------------------------------------------------------------------
    //  a paint that did not happen never reads as one
    // ---------------------------------------------------------------------------------

    [Fact]
    public void PaintingWithNothingPresented_IsATypedRefusal_NotASilentNoOp()
    {
        var run = FlashDrawObservations.Run;

        Assert.Equal(OverlayReasonCodes.OverlayNothingPresented, run.PaintBeforePresentCode);
        Assert.Equal(OverlayReasonCodes.OverlayPresenceDisposed, run.PaintAfterDisposeCode);
        Assert.Equal(OverlayReasonCodes.OverlayNothingPresented, run.PaintAfterWithdrawCode);
        Assert.False(run.PaintAfterWithdrawClaimedAvailable);
    }

    [Fact]
    public void AFrameThatIsNotTheSurfacesSize_IsRefusedRatherThanStretched()
    {
        // This capability scales nothing: a stretched frame would silently decide the port's DPI
        // policy on a surface whose coordinates are physical pixels (SP-099 D55), and a stale frame
        // of the wrong size would appear as a plausible picture rather than as a bug.
        var run = FlashDrawObservations.Run;

        Assert.Equal(
            run.MachineHasInteractiveDesktop
                ? OverlayReasonCodes.OverlayFrameSizeMismatch
                : OverlayReasonCodes.OverlayNothingPresented,
            run.WrongSizeFrameCode);
        Assert.False(run.WrongSizeFrameClaimedAvailable);
    }

    [Fact]
    public void TheSurfaceLeavesNoWindowBehind_AfterTheDrawLifecycle()
    {
        Assert.True(FlashDrawObservations.Run.WindowGoneAfterDispose,
            "the painted surface's top-level window survived Dispose");
    }

    // ---------------------------------------------------------------------------------
    //  THE WHOLE CHAIN: a real image file, on the real desktop
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheProductsOwnDecoder_SizesARealImageFileByWpfsGeometry()
    {
        // A real .png on disk, decoded by the product's own frame source at the size WPF's
        // CalculateGeometry asks for (FlashService.cs:2292-2301) — decode-at-display-size, which is
        // what upstream does too.
        var run = FlashEndToEndObservations.Measured;

        Assert.Equal(run.ExpectedFrameWidth, run.DecodedFrameWidth);
        Assert.Equal(run.ExpectedFrameHeight, run.DecodedFrameHeight);
    }

    [Fact]
    public void AFileThatIsNotAnImage_ProducesNoFrameAndNoException()
    {
        // WPF's own normal case: "a file is missing, corrupted, or uses an unsupported codec"
        // (FlashService.cs, LoadImagesUntilAsync). It is an outcome, not a fault, because it is
        // what a real user's folder contains.
        var run = FlashEndToEndObservations.Measured;

        Assert.False(run.MissingFileDecodes);
        Assert.False(run.CorruptFileDecodes);
    }

    [Fact]
    public void ATransparentImage_IsComposedOverBlack_AsWpfsFlashWindowDoes()
    {
        // WPF's flash window background is Brushes.Black (FlashService.cs:1245) and the image sits
        // on it. This surface has ONE uniform alpha and no per-pixel alpha to give a transparent
        // PNG, so the black backing is what stops the desktop showing through in a shape nobody
        // asked for.
        var run = FlashEndToEndObservations.Measured;

        Assert.True(run.TransparentHalfIsBlack, "the transparent half of the image did not compose over black");
        Assert.True(run.OpaqueHalfIsTheImage, "the opaque half of the image did not survive the decode");
    }

    [Fact]
    public void ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden()
    {
        // The packet, end to end and with nothing stubbed but the clock: a file on disk, the
        // product's decoder, the product's presenter, a real overlay surface, and the desktop
        // counted before / during / after. The image's colour is one nothing else on a desktop is,
        // and the count needs no knowledge of WHERE the flash landed — which is right, because the
        // placement is a random roll (FlashService.cs:2360).
        var run = FlashEndToEndObservations.Measured;
        var expectedArea = run.ExpectedFrameWidth * run.ExpectedFrameHeight;

        Assert.Equal(run.DesktopCaptureIsLive ? 1 : 0, run.SurfacesShown);
        Assert.Equal(0, run.DesktopPixelsBefore);
        Assert.True(
            (run.DesktopPixelsDuring > expectedArea / 2) == run.DesktopCaptureIsLive,
            $"a desktop capture can see a painted layered window on this machine = {run.DesktopCaptureIsLive}, "
            + $"and while the flash was up the composited desktop carried {run.DesktopPixelsDuring} pixels of the "
            + $"image's colour against an expected area of at least {expectedArea / 2}. Those two must agree: "
            + "where the screen can be read, the user's own image must really be on it. "
            + $"The screen read RETURNED {run.DesktopPixelsSampledDuring} pixels (SP-107), of which "
            + $"{run.DesktopUniformPixelsDuring} were the same colour as the first one "
            + $"(0x{run.DesktopFirstPixelDuring:X6}). The display metrics were {run.PlacementScreen.Width}x"
            + $"{run.PlacementScreen.Height} virtual / H{run.PlacementHorizontal} V{run.PlacementVertical} when "
            + $"the flash was PLACED and H{run.CaptureHorizontalDuring} V{run.CaptureVerticalDuring} when the "
            + $"screen was READ — they agree = {run.DisplayMetricsHeldStill}. FOUR different verdicts hide "
            + "behind a count of zero and those numbers separate them: returned=0 is a failed CAPTUREBLT "
            + "allocation and this fact measured nothing; returned=all-uniform is a blank or asleep display; "
            + "metrics that MOVED mean the desktop was rescaled between the placement and the read, so the "
            + "capture mapped the rectangle through the wrong ratio and sampled somewhere the flash never was "
            + "(the pixel count cannot show this, being physical and so scale-invariant); and only a real "
            + "desktop read at unchanged metrics means the flash was genuinely not composited where a user "
            + $"would see it. SP-116 adds the fifth and it is the one that used to fire: the compositor fence "
            + $"was held = {run.CompositorFenceHeldDuring} — a read taken WITHOUT that edge saw a layered "
            + "top-most window this process had just shown and painted 34 times in 1200, with the window "
            + "owning its own centre point every time");
        Assert.True(run.DesktopPixelsAfterHide == 0,
            $"after the flash was hidden the desktop still carries {run.DesktopPixelsAfterHide} pixels of the "
            + "image's colour — the picture outlived the effect that put it there");
    }

    // ---------------------------------------------------------------------------------
    //  the refusal is complete: a backend that cannot draw says so about DRAWING too
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(OverlayHostPlatform.Linux)]
    [InlineData(OverlayHostPlatform.MacOs)]
    [InlineData(OverlayHostPlatform.Unknown)]
    public void ABackendThatRefusesASurface_RefusesToPaintOneToo(OverlayHostPlatform platform)
    {
        // A refusal that covers Present but quietly accepts Paint is the first attempt's Linux
        // surface exactly: "overlay operations degrade to logged no-ops"
        // (CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs:9-78). Then a caller cannot
        // tell "the frame went to a screen" from "the frame went nowhere".
        using var presence = OverlayPresenceFactory.CreateFor(platform);
        var paint = presence.Paint(OverlayFrame.Solid(64, 64, 0x10, 0x20, 0x30));

        var refusal = Assert.IsType<CapabilityState.Unavailable>(paint);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, refusal.Reason.Code);
        Assert.False(presence.IsPresenting);

        // And re-asserting topmost on a backend with no window is a silent no-op that claims
        // nothing — it returns no state on ANY backend, so silence here cannot be read as success.
        presence.Reassert();
        Assert.False(presence.IsPresenting);
    }
}
