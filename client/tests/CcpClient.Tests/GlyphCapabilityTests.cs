using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Glyph;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-115. What the operating system says about a per-pixel-alpha composite on this machine.
///
/// <para>Every fact reads a cached real-desktop run (<see cref="GlyphSurfaceObservations"/>) through
/// <see cref="GlyphWindowProbe"/>, an independent second copy of every P/Invoke the product uses.
/// The instrument carries its own negative control and re-runs it on every suite execution, because
/// this capability's central claim — "the operating system's own copy of the surface carries the
/// frame" — is worthless unless a window that composites NOTHING answers the same question
/// differently.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class GlyphCapabilityTests
{
    // ------------------------------------------------------- the instrument itself

    [Fact]
    public void THEGHOSTCONTROL_ALayeredWindowThatWasNeverComposited_ReadsBackNOTHING()
    {
        // THE FACT THIS WHOLE CAPABILITY RESTS ON. The overlay's ghost check is
        // GetLayeredWindowAttributes; for a per-pixel surface that call answers FALSE by design, so
        // it cannot be reused. The replacement is the surface read-back, and this is the measurement
        // that makes the replacement worth anything: the exact state the first attempt shipped
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45) reads back as nothing at all.
        var control = GlyphSurfaceObservations.Control;

        Assert.Equal(control.MachineHasInteractiveDesktop, control.GhostIsVisible);
        Assert.False(control.GhostHoldsUniformAlpha,
            "the ghost was given uniform layered attributes, which makes it the OVERLAY's shape rather than the "
            + "never-composited window this control exists to build");

        if (!control.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(control.GhostSampledPixels > 0,
            "the read-back returned NOTHING, so 'zero non-zero pixels' would be a fact about an empty array "
            + "rather than about a window");
        Assert.Equal(0, control.GhostNonZeroPixels);
    }

    [Fact]
    public void ANDTHESAMEWINDOWAfterONEComposite_ReadsBackTheFrameEXACTLY()
    {
        // The other half of the differential, on the SAME handle: one call is the entire difference
        // between the two readings, so nothing else about the window can explain it.
        var control = GlyphSurfaceObservations.Control;
        if (!control.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(control.CompositedNonZeroPixels > 0,
            $"after one UpdateLayeredWindow the surface still reads back {control.CompositedNonZeroPixels} "
            + "non-zero pixels, which is what the ghost reads; the instrument cannot tell the two apart and no "
            + "fact below means anything");
        Assert.True(control.CompositedInkPoints > 0);
        Assert.Equal(control.CompositedInkPoints, control.CompositedInkMatches);
    }

    [Fact]
    public void SP099sHAZARDIsREMEASURED_UniformModeRefusesPerPixel_ButTheStyleToggleLetsItThrough()
    {
        // The recorded hazard, rebuilt and measured rather than quoted:
        //   "toggling WS_EX_LAYERED alone is harmless; UpdateLayeredWindow alone fails with error 87;
        //    but toggle then ULW succeeds and kills the ghost check."
        // Both halves matter to this packet. The refusal is why an overlay surface cannot be
        // converted by a stray call; the toggle is why the capability must never touch a window it
        // did not create, which is a STRUCTURAL property of Glyph/** and not a discipline.
        var control = GlyphSurfaceObservations.Control;
        if (!control.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(control.UniformModeRefusesPerPixel,
            "UpdateLayeredWindow SUCCEEDED on a window holding uniform layered attributes. If that is true on "
            + "this machine then an overlay surface could be converted to per-pixel mode by one call, and this "
            + "capability's isolation argument needs re-deriving");
        Assert.Equal(87, control.UniformModeRefusalError);
        Assert.Equal(153, control.UniformAlphaSurvivedTheRefusal);

        Assert.True(control.StyleToggleClearsUniformAlpha,
            "clearing WS_EX_LAYERED did NOT wipe the uniform alpha, so SP-099's first line does not reproduce "
            + "here and the hazard this design avoids is a different one than recorded");
        Assert.True(control.ToggleThenPerPixelSucceeds);
        Assert.Equal(-1, control.UniformAlphaAfterToggle);
    }

    [Fact]
    public void TheInstrumentLeavesNothingBehind()
    {
        Assert.True(GlyphSurfaceObservations.Control.ScratchWindowsGoneAfterTeardown);
    }

    // ------------------------------------------------------- the capability's own claim

    [Fact]
    public void PresentEarnsAvailableONLYWhereTheMachineHasADesktop()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.PresentState is CapabilityState.Available
                : run.PresentState is CapabilityState.Unavailable,
            $"this session has an interactive desktop = {run.MachineHasInteractiveDesktop}, and the capability "
            + $"said {GlyphSurfaceObservations.Describe(run.PresentState)}");
    }

    [Fact]
    public void TheOSHoldsTheSurface_ItExists_IsVisible_AndCarriesExactlyTheRequestedRectangle()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.Equal(run.MachineHasInteractiveDesktop, run.ExistsAfterPresent);
        Assert.Equal(run.MachineHasInteractiveDesktop, run.VisibleAfterPresent);

        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.Equal(run.RequestedRect, run.RectAfterPresent);
    }

    [Fact]
    public void THESURFACEHOLDSNOUNIFORMALPHA_WhichIsTheWholeDifferenceFromTheOverlay()
    {
        // -1 means GetLayeredWindowAttributes returned FALSE. For the overlay that reading is the
        // GHOST and its capability refuses on it; here it is the CORRECT state and the refusal is
        // the other way round. Asserting it pins that these two capabilities really are driving
        // different, mutually exclusive mechanisms, and that a future edit cannot quietly make the
        // glyph surface uniform.
        var run = GlyphSurfaceObservations.Lifecycle;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.Equal(-1, run.UniformAlphaAfterPresent);
    }

    [Fact]
    public void TheExtendedStyleReadBackCarriesEveryBitThatWasWritten()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        const uint layered = 0x00080000;
        const uint transparent = 0x00000020;
        const uint toolWindow = 0x00000080;
        const uint noActivate = 0x08000000;
        const uint topmost = 0x00000008;

        Assert.Equal(layered, run.ExStyleAfterPresent & layered);
        Assert.Equal(transparent, run.ExStyleAfterPresent & transparent);
        Assert.Equal(toolWindow, run.ExStyleAfterPresent & toolWindow);
        Assert.Equal(noActivate, run.ExStyleAfterPresent & noActivate);
        Assert.Equal(topmost, run.ExStyleAfterPresent & topmost);
    }

    [Fact]
    public void TheOSsOwnZOrderPutsTheSurfaceAboveEveryOrdinaryWindow()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        Assert.Equal(run.MachineHasInteractiveDesktop, run.ZOrderAfterPresent.AboveEveryOrdinaryWindow);
    }

    [Fact]
    public void TheSurfaceNeverTakesTheForeground()
    {
        Assert.False(GlyphSurfaceObservations.Lifecycle.ForegroundAfterPresent);
    }

    [Fact]
    public void TheWindowManagerRoutesThePointPASTIt_AndTOItWhenMomentarilyMadeOpaque()
    {
        // Both legs, one run, same point. "The point does not route to this window" is also true of
        // a window that was never created, so the second leg is what makes the first non-vacuous.
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.True(run.PointPassesThroughWhileClickThrough,
            "WS_EX_TRANSPARENT is set and the window manager still routes the surface's own centre to it: "
            + "clicks are being swallowed and the desktop underneath is broken");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.CatchesItsOwnPointWhenOpaque);
    }

    [Fact]
    public void THEOSSOWNCOPYOFTHESURFACECarriesTheFrame_AtEveryOpaqueInkPoint()
    {
        // The capability's Available cannot be reached without this, and the ghost control above is
        // what makes it a fact rather than a formality.
        var run = GlyphSurfaceObservations.Lifecycle;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(run.SurfaceSampledPixels > 0);
        Assert.True(run.SurfaceNonZeroPixels > 0,
            "the OS returned an entirely black surface for a window that was just composited with an opaque "
            + "magenta quadrant, which is exactly what a window compositing nothing returns");
        Assert.True(run.InkPoints > 0);
        Assert.Equal(run.InkPoints, run.InkMatchesAfterPresent);
    }

    [Fact]
    public void AndTheTransparentQuadrantReadsBackAsNOTHING_WhichIsTheLIMITOfThisReadBack()
    {
        // Asserted so the limit is pinned rather than confessed in prose: a fully transparent pixel
        // and an opaque BLACK pixel are the SAME value here. Nothing in a window read-back can
        // separate them; that separation is the differential run's, over a known background.
        var run = GlyphSurfaceObservations.Lifecycle;
        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(run.TransparentReadsZero);
    }

    [Fact]
    public void PaintReplacesTheContent_AndTheOSsCopyREALLYCHANGED()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.PaintState is CapabilityState.Available
                : run.PaintState is CapabilityState.Unavailable,
            GlyphSurfaceObservations.Describe(run.PaintState));

        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.Equal(run.SecondInkPoints, run.SecondInkMatches);
        Assert.True(run.SurfaceChangedBetweenFrames,
            "the OS returns the same bytes for the surface before and after a DIFFERENT frame was composited "
            + "onto it, so nothing proves the second composite arrived");
    }

    [Fact]
    public void AMOVEIsOneCall_ItEarnsAvailableFromGetWindowRect_AndTheContentSurvivesIt()
    {
        // The operation the overlay does not have, and the one D84 named as its closer.
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.MoveState is CapabilityState.Available
                : run.MoveState is CapabilityState.Unavailable,
            GlyphSurfaceObservations.Describe(run.MoveState));

        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.Equal(run.RequestedRectAfterMove, run.RectAfterMove);
        Assert.True(run.ContentSurvivedTheMove,
            "the surface stopped holding its frame after a move, so a moving module would be dragging an empty "
            + "window around the desktop");
    }

    [Fact]
    public void AMoveThatWouldRESIZEIsRefused_BecauseTheLayeredSurfaceISTheFrame()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        var refusal = Assert.IsType<CapabilityState.Unavailable>(run.ResizingMoveState);
        Assert.Equal(GlyphReasonCodes.GlyphGeometryRefused, refusal.Reason.Code);
    }

    [Fact]
    public void ANINKLESSFRAMEIsREFUSED_BecauseNothingCouldTellItFromAGhost()
    {
        // The measurement behind this refusal is in the control above: an entirely transparent
        // composite reads back zero non-zero pixels, byte for byte what a never-composited window
        // reads. Claiming Available there would be claiming exactly the thing this packet forbids.
        var run = GlyphSurfaceObservations.Lifecycle;
        var refusal = Assert.IsType<CapabilityState.Unavailable>(run.InklessPaintState);
        Assert.Equal(GlyphReasonCodes.GlyphFrameCarriesNoProvableInk, refusal.Reason.Code);
    }

    [Fact]
    public void AMismatchedFrameIsRefusedRatherThanStretched()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        var refusal = Assert.IsType<CapabilityState.Unavailable>(run.MismatchedFrameState);
        Assert.Equal(GlyphReasonCodes.GlyphFrameSizeMismatch, refusal.Reason.Code);
    }

    [Fact]
    public void WithdrawTakesItOffTheScreenAndOutOfTheHitTest_AndKeepsTheComposite()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.WithdrawState is CapabilityState.Available
                : run.WithdrawState is CapabilityState.Unavailable,
            GlyphSurfaceObservations.Describe(run.WithdrawState));

        Assert.False(run.VisibleAfterWithdraw);
        Assert.True(run.PointRoutesAwayAfterWithdraw);

        if (!run.MachineHasInteractiveDesktop)
        {
            return;
        }

        Assert.True(run.ContentSurvivedTheWithdraw,
            "the composited surface was lost by a withdraw, so the next Present would have to re-composite "
            + "before anything could be claimed - which is a different contract from the one the interface states");
    }

    [Fact]
    public void PaintingAWithdrawnSurfaceIsRefused()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        var refusal = Assert.IsType<CapabilityState.Unavailable>(run.PaintAfterWithdrawState);
        Assert.Equal(GlyphReasonCodes.GlyphNothingPresented, refusal.Reason.Code);
    }

    [Fact]
    public void DisposeLeavesNoTopLevelWindowBehind_AndNoTeardownDiagnostic()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        Assert.False(run.ExistsAfterDispose);
        Assert.Null(run.TeardownDiagnostic);
    }

    // ------------------------------------------------------- the frame type's own invariants

    [Fact]
    public void ANONPREMULTIPLIEDFRAMEIsRefusedAtCONSTRUCTION_BecauseTheOSAcceptsOneSILENTLY()
    {
        // Measured: UpdateLayeredWindow returns TRUE for a non-premultiplied buffer and composites
        // it wrongly - white at alpha 64 reads back as full white. There is no OS-level error to
        // catch, so the invariant is a property of the type or it is checked nowhere at all.
        var pixels = new byte[4 * 4 * GlyphFrame.BytesPerPixel];
        for (var i = 0; i < pixels.Length; i += GlyphFrame.BytesPerPixel)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 64;
        }

        var error = Assert.Throws<ArgumentException>(() => new GlyphFrame(4, 4, pixels));
        Assert.Contains("premultiplied", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromStraightAlphaDoesTheMultiplicationSoACallerCannotGetItWrong()
    {
        var straight = new byte[GlyphFrame.BytesPerPixel];
        straight[0] = 200;
        straight[1] = 100;
        straight[2] = 50;
        straight[3] = 128;

        var frame = GlyphFrame.FromStraightAlpha(1, 1, straight);

        Assert.Equal(128, frame.AlphaAt(0, 0));
        Assert.Equal((uint)((200 * 128 / 255) << 16 | (100 * 128 / 255) << 8 | (50 * 128 / 255)),
            frame.PremultipliedColourAt(0, 0));
    }

    [Fact]
    public void AFullyTransparentFrameHasNoProvableInk_AndSaysSoBeforeAnySurfaceIsAsked()
    {
        Assert.False(GlyphFrame.Solid(8, 8, 255, 255, 255, 0).HasProvableInk);
    }

    [Fact]
    public void AFullyOpaqueBLACKFrameAlsoHasNoProvableInk_BecauseBlackIsWhatNOTHINGReadsBackAs()
    {
        // Not a quirk: this is the same measured limit as the read-back's, expressed in the type.
        // An opaque black frame is a legitimate picture and it is one this surface cannot PROVE it
        // composited, so it must not be able to earn Available.
        Assert.False(GlyphFrame.Solid(8, 8, 0, 0, 0, 255).HasProvableInk);
    }

    [Fact]
    public void CompositeOverIsPremultipliedSourceOver_WhichIsWhatTheOSWasMeasuredToDo()
    {
        // Pinned against the raw measurement: white at alpha 128 over B=200 G=40 R=10 read back
        // 0xE49485 off the composited desktop, and 128 + 200*127/255 = 227.6 -> 0xE4.
        var frame = GlyphFrame.Solid(1, 1, 255, 255, 255, 128);
        Assert.Equal(0x00E49485u, frame.CompositeOver(0, 0, 0x00C8280A, 255));
    }

    [Fact]
    public void OpacityZeroIsRefusedAtTheRequestBoundary_TheSameWayTheOverlayRefusesIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GlyphSurfaceRequest(new GlyphBounds(0, 0, 10, 10), 0.0, ClickThrough: true));
    }

    [Theory]
    [InlineData(1.0, 255)]
    [InlineData(0.5, 128)]
    [InlineData(0.004, 1)]
    public void TheOpacityDialBecomesTheConstantAlphaByte_FlooredSoItCanNeverRoundToInvisible(
        double opacity, byte expected)
    {
        Assert.Equal(expected, new GlyphSurfaceRequest(
            new GlyphBounds(0, 0, 10, 10), opacity, ClickThrough: true).ConstantAlpha);
    }

    // ------------------------------------------------------- Linux and the other refusals

    [Theory]
    [InlineData(GlyphHostPlatform.Linux)]
    [InlineData(GlyphHostPlatform.MacOs)]
    [InlineData(GlyphHostPlatform.Unknown)]
    public void EveryUnsupportedPlatformRefusesInTYPE_OnEveryOperation(GlyphHostPlatform platform)
    {
        // A PARTIAL refusal is a path a caller can mistake for a surface on screen, which is the
        // first attempt's failure mode in miniature. So all four operations are asked.
        using var surface = GlyphSurfaceFactory.CreateFor(platform);
        var frame = GlyphFrame.Solid(8, 8, 10, 20, 30, 255);
        var request = new GlyphSurfaceRequest(new GlyphBounds(0, 0, 8, 8), 1.0, ClickThrough: true);

        Assert.IsType<CapabilityState.Unavailable>(surface.Present(request, frame));
        Assert.IsType<CapabilityState.Unavailable>(surface.Paint(frame));
        Assert.IsType<CapabilityState.Unavailable>(surface.MoveTo(new GlyphBounds(1, 1, 8, 8)));
        Assert.IsType<CapabilityState.Unavailable>(surface.Withdraw());
        Assert.False(surface.IsPresenting);
    }

    [Fact]
    public void TheLinuxRefusalNamesTheROUTE_TheCOMPOSITOR_AndTheUndischargedGate()
    {
        using var surface = GlyphSurfaceFactory.CreateFor(GlyphHostPlatform.Linux);
        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            surface.Paint(GlyphFrame.Solid(8, 8, 10, 20, 30, 255)));

        Assert.Equal(GlyphReasonCodes.GlyphMechanismAbsent, refusal.Reason.Code);
        Assert.Contains("ARGB visual", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("compositing manager", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("MANUAL GATE", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("WSLg", refusal.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinuxGateAsksForTHEDIFFERENTIAL_NotForACountOfDrawCalls()
    {
        // The gate has to demand the same three-value distinction the Windows harness makes, or a
        // future Linux implementer could discharge it with a surface that composites a black plate.
        Assert.Contains("XGetImage", GlyphSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("BLACK", GlyphSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("alpha is 0", GlyphSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
    }

    [Fact]
    public void WaylandIsARefusalForTWOReasons_AndTheSecondIsThatTheProofCannotBeTaken()
    {
        Assert.Contains("read back", GlyphSurfaceFactory.WaylandNote, StringComparison.Ordinal);
        Assert.Contains("wlr-layer-shell", GlyphSurfaceFactory.WaylandNote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFactorySelectsByPlatformAndNeverGrantsAvailability()
    {
        Assert.IsType<Win32GlyphSurface>(GlyphSurfaceFactory.CreateFor(GlyphHostPlatform.Windows));
        Assert.IsType<UnsupportedGlyphSurface>(GlyphSurfaceFactory.CreateFor(GlyphHostPlatform.Linux));
        Assert.Equal(
            OperatingSystem.IsWindows() ? GlyphHostPlatform.Windows : GlyphHostPlatform.Linux,
            GlyphSurfaceFactory.CurrentPlatform());
    }

    [Fact]
    public void ADisposedSurfaceRefusesEverything_AndSaysThatItWasDisposed()
    {
        var surface = new Win32GlyphSurface();
        surface.Dispose();

        var frame = GlyphFrame.Solid(8, 8, 10, 20, 30, 255);
        foreach (var state in new[]
                 {
                     surface.Present(new GlyphSurfaceRequest(new GlyphBounds(0, 0, 8, 8), 1.0, true), frame),
                     surface.Paint(frame),
                     surface.MoveTo(new GlyphBounds(0, 0, 8, 8)),
                     surface.Withdraw(),
                 })
        {
            var refusal = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(GlyphReasonCodes.GlyphSurfaceDisposed, refusal.Reason.Code);
        }
    }

    [Fact]
    public void AnUnpresentedSurfaceRefusesPaintMoveAndWithdraw()
    {
        using var surface = new Win32GlyphSurface();
        foreach (var state in new[]
                 {
                     surface.Paint(GlyphFrame.Solid(8, 8, 10, 20, 30, 255)),
                     surface.MoveTo(new GlyphBounds(0, 0, 8, 8)),
                     surface.Withdraw(),
                 })
        {
            var refusal = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(GlyphReasonCodes.GlyphNothingPresented, refusal.Reason.Code);
        }
    }

    [Fact]
    public void PresentNAMESWhatItDoesNotClaim_TheTransparentPixelAndTheHumanEye()
    {
        // The Available detail is read by the module panel and by a bug report. A claim that did not
        // carry its own limits would be the thing the packet calls the most easily faked property.
        var run = GlyphSurfaceObservations.Lifecycle;
        if (run.PresentState is not CapabilityState.Available available)
        {
            return;
        }

        Assert.Contains("TRANSPARENT", available.Detail, StringComparison.Ordinal);
        Assert.Contains("headed claim", available.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AndTheMOVEsAvailableSaysExactlyWhatItDidNotReask()
    {
        var run = GlyphSurfaceObservations.Lifecycle;
        if (run.MoveState is not CapabilityState.Available available)
        {
            return;
        }

        Assert.Contains("NO z-order was walked", available.Detail, StringComparison.Ordinal);
        Assert.Contains("NO hit test", available.Detail, StringComparison.Ordinal);
    }
}
