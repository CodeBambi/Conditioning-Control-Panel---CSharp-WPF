using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-115's central evidence: <b>the composited desktop, read back over a KNOWN background</b>.
///
/// <para><b>Why a window read-back is not enough, stated as the reason this file exists.</b>
/// <see cref="GlyphCapabilityTests"/> proves the operating system's own copy of the surface carries
/// the frame, with a ghost control that reads back nothing. That is the strongest thing the
/// capability can prove about ITSELF, and it is blind to the exact distinction the packet names: a
/// fully transparent pixel and an opaque BLACK pixel are both zero there. A surface that composited
/// a black plate over the user's desktop — worse than an absent surface — would pass every fact in
/// that file.</para>
///
/// <para><b>So the distinction is made where it exists: on the screen.</b> A known opaque background
/// goes up (the LANDED overlay capability, consumed unmodified, holding one colour its own
/// read-back confirms), the glyph surface is composited over it, and the COMPOSITED DESKTOP is read
/// through SP-100's <see cref="FlashPixelProbe"/>. Five points, twice — once with the surface up and
/// once with it hidden — and the pair is the fact.</para>
///
/// <para><b>The occlusion arbitration is why any of these pixels can be attributed at all.</b>
/// SP-113's review recorded that the four-disjoint-rectangles argument does not scale past four
/// surfaces; this capability's evidence cannot use disjointness AT ALL, because the overlap IS the
/// evidence. So ownership is measured, not assumed: every visible window strictly between the two in
/// the OS's own z-order is fetched and tested for intersection with the sampled area, and the pair
/// is re-raised a bounded number of times until nobody is between them. Both arms were observed
/// while this packet was written — with the pair raised back to back the list is empty, and with an
/// ordinary interval between the raises the shipping WPF product sat in the gap and the "background"
/// pixels were its own. <b>Every fact below asserts unconditionally</b>: a desktop this run cannot
/// win is a red fact naming the intruder, not a silently skipped one.</para>
///
/// <para><b>What this still is not.</b> A human. A composited desktop read from inside the process
/// is the strongest evidence a process can produce about its own pixels and it is still not a headed
/// capture; it cannot see a Magnifier, a mirror driver, an exclusive-fullscreen swap chain or a
/// physically dark monitor.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class GlyphAlphaDifferentialTests
{
    [Fact]
    public void BOTHSURFACESREALLYREACHEDTHEDESKTOP_OrEveryPixelBelowIsAReadingOfSomethingElse()
    {
        // The positive control, and the same role SP-099's middle leg plays: without it, "the
        // background shows through the transparent quadrant" is equally true of a run in which
        // neither window ever appeared.
        var run = GlyphSurfaceObservations.Differential;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.BackdropPresented);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.BackdropPainted);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.GlyphPresented);
    }

    [Fact]
    public void THEARBITRATION_NoVisibleWindowSitsBetweenTheTwo_OverTheSampledArea()
    {
        // Not "the rectangles are disjoint" - they are deliberately NOT, because the overlap is the
        // evidence. This is the replacement argument, and it is a measurement.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.ArbitrationHeld == run.MachineHasInteractiveDesktop,
            "a foreign window owns part of the sampled area after "
            + $"{GlyphSurfaceObservations.ArbitrationAttempts} bounded re-raises, so the pixels this run reads "
            + $"belong to it rather than to the two surfaces this run put there. {run.Why}. This is NOT a flake "
            + "to be retried away: it is the residue the port has recorded since SP-099, and on this machine "
            + "the intruder is usually the shipping WPF product re-asserting HWND_TOPMOST on a cadence "
            + "(Services/Flash/FlashService.cs:206-243)");
    }

    [Fact]
    public void THECAPTUREISLIVE_TheMarginOutsideTheSurfaceReadsTheBackgroundsOwnColour_BothTimes()
    {
        // A screen read that returned garbage, or that sampled the wrong point because of the DPI
        // mapping, would make every other reading meaningless in a way that looks like success. The
        // margin is inside the same capture and outside the glyph surface, so it must read the
        // background exactly - with the surface up AND with it hidden.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.MarginIsBackgroundBothTimes == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void WITHTHESURFACEHIDDEN_AllFourPointsReadTheBackground_WhichIsTheCONTROL()
    {
        // Every point the surface covers reads the background when the surface is not there. That
        // is what makes the four readings below CHANGES rather than coincidences, and it is the
        // half that a black-plate surface would also satisfy - which is why it is only half.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.ControlReadsBackgroundEverywhere == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void AFULLYTRANSPARENTPIXELSHOWSTHEBACKGROUNDBEHINDIT()
    {
        // THE FIRST HALF OF THE PACKET'S CENTRAL DEMAND. The surface is up, on top, and holding a
        // frame whose top-left quadrant has alpha zero - and the desktop there is the background's
        // own colour, byte for byte.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.TransparentShowsBackground == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void ANDANOPAQUEBLACKPIXELISNOTTRANSPARENT_SameWindow_SameCapture_DifferentValue()
    {
        // THE SECOND HALF, and the one no window read-back can make: in the OS's own copy of this
        // surface both of these points are 0x000000. On the screen they are not the same at all -
        // the transparent one is the background and this one is black. A surface compositing an
        // opaque black plate would fail HERE and nowhere else.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.OpaqueBlackIsNotTransparent == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void AGLYPHPIXELISDISTINGUISHEDFROMTHEBACKGROUND()
    {
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.InkIsDistinguishedFromBackground == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void ANDTHEBLENDISPERPIXEL_ArithmeticallyExactAgainstPremultipliedSourceOver()
    {
        // The clause that makes this PER-PIXEL alpha rather than a uniform one: a half-alpha sample
        // reads neither the frame's colour nor the background's, but exactly their premultiplied
        // source-over combination - which no single uniform LWA_ALPHA over an opaque frame can
        // produce, because that mechanism has one alpha for the whole rectangle and this frame has
        // four different ones.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.BlendIsPerPixel == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void ALLFOURQUADRANTSAREDIFFERENTVALUES_WhichIsWhatAUNIFORMAlphaCouldNotProduce()
    {
        // Stated as its own fact because it is the shape of the claim, not a detail of it: one
        // window, one capture, four sample points, four distinct values. A surface composited at
        // one uniform alpha over an opaque frame - the overlay's mechanism, and the one this port
        // had before - can produce at most one.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.AllFourQuadrantsDiffer == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void THEOPACITYDIALREALLYREACHESTHECOMPOSITOR_SameFrame_SameBytes_DifferentScreen()
    {
        // THE SURVIVOR THIS LEG EXISTS FOR. Pinning the surface's constant alpha at 255 inside the
        // backend was undetectable: the request carried the dial, nothing read it back off a screen,
        // and the user-facing control the product ships at 100 could have stopped working silently.
        // Here the SAME frame is composited twice with only the multiplier changed, and every point
        // the frame is not transparent at reads differently.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.TheDialReachesTheCompositor == run.MachineHasInteractiveDesktop,
            $"full opacity read [{string.Join(", ", run.WithGlyph.Select(v => $"0x{v:X6}"))}] and half opacity "
            + $"read [{string.Join(", ", run.HalfOpacity.Select(v => $"0x{v:X6}"))}]. If those are equal the "
            + $"dial is not reaching the compositor. {run.Why}");
    }

    [Fact]
    public void ATHALFOPACITYANOPAQUEBLACKPIXELIsNoLongerBlack_AndATransparentOneIsUnchanged()
    {
        // The two halves of the same claim, and the second is the control: zero times anything is
        // zero, so a fully transparent pixel must be UNMOVED by the dial while every opaque one
        // moves. A surface that simply drew something different would fail the second half.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.HalfOpacityBlackIsNeitherBlackNorBackground == run.MachineHasInteractiveDesktop, run.Why);
        Assert.True(run.HalfOpacityLeavesTheTransparentPixelAlone == run.MachineHasInteractiveDesktop, run.Why);
    }

    [Fact]
    public void ANDEVERYHALFOPACITYPOINTIsWithinONEUnitOfTheSameArithmetic()
    {
        // WITHIN ONE, and the tolerance is the finding rather than a convenience. At full opacity
        // the prediction is EXACT and is asserted as such two facts above. At half opacity it is
        // exact at three of the four points and one unit out in the red channel of the fourth. The
        // model was NOT tuned to remove that unit: fitting CompositeOver to a single observation
        // would make the oracle a copy of the data. The residue is declared here, in the record and
        // at the constant.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(
            run.EveryHalfOpacityPointIsWithinOneOfThePrediction == run.MachineHasInteractiveDesktop,
            $"predicted [{string.Join(", ", run.ExpectedHalfOpacity.Select(v => $"0x{v:X6}"))}] and the screen "
            + $"holds [{string.Join(", ", run.HalfOpacity.Select(v => $"0x{v:X6}"))}], further apart than "
            + $"{GlyphSurfaceObservations.HalfOpacityTolerance} unit per channel. {run.Why}");
    }

    [Fact]
    public void EVERYPOINTMATCHESTHEPREDICTEDCOMPOSITE_NotJustTheOnesChosenToBeEasy()
    {
        // The whole predicted row at once, so a future change that got one quadrant right by
        // accident cannot pass. The prediction comes from GlyphFrame.CompositeOver, which is pure
        // arithmetic pinned separately against the raw probe measurement.
        var run = GlyphSurfaceObservations.Differential;

        Assert.True(run.EveryPointMatchesThePrediction == run.MachineHasInteractiveDesktop,
            $"predicted [{string.Join(", ", run.ExpectedWithGlyph.Select(v => $"0x{v:X6}"))}] and the screen "
            + $"holds [{string.Join(", ", run.WithGlyph.Select(v => $"0x{v:X6}"))}]. {run.Why}");
    }
}
