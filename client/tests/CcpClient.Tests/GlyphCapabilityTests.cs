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

        // Asserted at statement depth 0 against the machine class rather than behind an early
        // return: the boolean carries BOTH clauses, so a machine with no desktop cannot make the
        // fact pass by never reaching it. GhostReadBackIsEmpty requires that something really was
        // sampled AND that none of it is non-zero.
        Assert.Equal(control.MachineHasInteractiveDesktop, control.GhostReadBackIsEmpty);
        Assert.Equal(0, control.GhostNonZeroPixels);
    }

    [Fact]
    public void ANDTHESAMEWINDOWAfterONEComposite_ReadsBackTheFrameEXACTLY()
    {
        // The other half of the differential, on the SAME handle: one call is the entire difference
        // between the two readings, so nothing else about the window can explain it.
        var control = GlyphSurfaceObservations.Control;

        Assert.True(control.CompositedReadBackCarriesTheFrame == control.MachineHasInteractiveDesktop,
            $"after one UpdateLayeredWindow the surface reads back {control.CompositedNonZeroPixels} non-zero "
            + $"pixels and matches {control.CompositedInkMatches} of {control.CompositedInkPoints} ink points. "
            + "If that is not distinguishable from the ghost above, the instrument cannot tell the two apart "
            + "and no fact below means anything");
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
        var machine = control.MachineHasInteractiveDesktop;

        Assert.True(control.UniformModeRefusesPerPixel == machine,
            "UpdateLayeredWindow SUCCEEDED on a window holding uniform layered attributes. If that is true on "
            + "this machine then an overlay surface could be converted to per-pixel mode by one call, and this "
            + "capability's isolation argument needs re-deriving");
        Assert.Equal(machine ? 87 : 0, control.UniformModeRefusalError);
        Assert.Equal(machine ? 153 : -1, control.UniformAlphaSurvivedTheRefusal);

        Assert.True(control.StyleToggleClearsUniformAlpha == machine,
            "clearing WS_EX_LAYERED did NOT wipe the uniform alpha, so SP-099's first line does not reproduce "
            + "here and the hazard this design avoids is a different one than recorded");
        Assert.Equal(machine, control.ToggleThenPerPixelSucceeds);
        Assert.Equal(-1, control.UniformAlphaAfterToggle);
    }

    [Fact]
    public void THEUNIFORMALPHAREFUSALISSTAGEDOnARealSurface_NotClassedAsUnreachable()
    {
        // THIS PACKET'S CENTRAL HAZARD, driven end to end against the product. A real surface
        // composites, is then given uniform layered attributes from OUTSIDE the capability with the
        // probe's own declaration, and is asked to composite again. The capability must name the
        // mode rather than report the OS's bare "87".
        //
        // A first draft classified this refusal as a branch nothing could reach. It is reachable
        // with instruments this suite already shipped.
        var run = GlyphSurfaceObservations.UniformMode;

        Assert.True(
            run.MachineHasInteractiveDesktop
                ? run.FirstPresent is CapabilityState.Available
                : run.FirstPresent is CapabilityState.Unavailable,
            $"the surface did not composite before it was poisoned, so the refusal below would be about a "
            + $"window that never worked: {GlyphSurfaceObservations.Describe(run.FirstPresent)}");

        Assert.Equal(run.MachineHasInteractiveDesktop, run.PoisonApplied);
        Assert.Equal(run.MachineHasInteractiveDesktop ? 200 : -1, run.UniformAlphaAfterPoison);
    }

    [Fact]
    public void ANDTHECAPABILITYNAMESTHEMODE_OnBothEntryPoints()
    {
        var run = GlyphSurfaceObservations.UniformMode;
        if (!run.MachineHasInteractiveDesktop)
        {
            Assert.IsType<CapabilityState.Unavailable>(run.RePresentAfterPoison);
            Assert.IsType<CapabilityState.Unavailable>(run.PaintAfterPoison);
            return;
        }

        var present = Assert.IsType<CapabilityState.Unavailable>(run.RePresentAfterPoison);
        Assert.Equal(GlyphReasonCodes.GlyphUniformAlphaMode, present.Reason.Code);
        Assert.Contains("SetLayeredWindowAttributes mode", present.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("permanently", present.Reason.Detail, StringComparison.Ordinal);

        // Paint has no mode check of its own - it reaches UpdateLayeredWindow, which the OS now
        // refuses - so it must report the COMPOSITE refusal with the OS's own error rather than a
        // success. Both answers are refusals; they are different refusals and both are true.
        var paint = Assert.IsType<CapabilityState.Unavailable>(run.PaintAfterPoison);
        Assert.Equal(GlyphReasonCodes.GlyphCompositeRefused, paint.Reason.Code);
        Assert.Contains("87", paint.Reason.Detail, StringComparison.Ordinal);
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
        Assert.True(run.RectMatchesRequest == run.MachineHasInteractiveDesktop,
            $"the OS holds {run.RectAfterPresent} where {run.RequestedRect} was asked for");
    }

    [Fact]
    public void THESURFACEHOLDSNOUNIFORMALPHA_WhichIsTheWholeDifferenceFromTheOverlay()
    {
        // -1 means GetLayeredWindowAttributes returned FALSE. For the overlay that reading is the
        // GHOST and its capability refuses on it; here it is the CORRECT state and the refusal is
        // the other way round. Asserting it pins that these two capabilities really are driving
        // different, mutually exclusive mechanisms, and that a future edit cannot quietly make the
        // glyph surface uniform.
        // -1 on EVERY machine class, which is why this one needs no conditioning at all: a machine
        // with no desktop has no window and reads -1 too.
        Assert.Equal(-1, GlyphSurfaceObservations.Lifecycle.UniformAlphaAfterPresent);
    }

    [Fact]
    public void TheExtendedStyleReadBackCarriesEveryBitThatWasWritten()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        // WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST, then WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT.
        Assert.True(run.ExStyleCarriesEveryBit == run.MachineHasInteractiveDesktop,
            $"the extended-style read-back is 0x{run.ExStyleAfterPresent:X}, which is missing a bit that was "
            + "written; the window is not the window that was asked for, whatever the write calls returned");
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

        Assert.True(run.SurfaceCarriesTheFrame == run.MachineHasInteractiveDesktop,
            $"the OS returned {run.SurfaceNonZeroPixels} non-zero of {run.SurfaceSampledPixels} sampled pixels "
            + $"and matched {run.InkMatchesAfterPresent} of {run.InkPoints} ink points for a window that was "
            + "just composited with an opaque magenta quadrant. An entirely black answer is exactly what a "
            + "window compositing nothing returns");
        Assert.Equal(run.InkPoints, run.InkMatchesAfterPresent);
    }

    [Fact]
    public void AndTheTransparentQuadrantReadsBackAsNOTHING_WhichIsTheLIMITOfThisReadBack()
    {
        // Asserted so the limit is pinned rather than confessed in prose: a fully transparent pixel
        // and an opaque BLACK pixel are the SAME value here. Nothing in a window read-back can
        // separate them; that separation is the differential run's, over a known background.
        Assert.Equal(
            GlyphSurfaceObservations.Lifecycle.MachineHasInteractiveDesktop,
            GlyphSurfaceObservations.Lifecycle.TransparentReadsZero);
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

        Assert.True(run.SecondFrameHeldAndChanged == run.MachineHasInteractiveDesktop,
            $"the second frame matched {run.SecondInkMatches} of {run.SecondInkPoints} ink points and the OS's "
            + $"bytes changed = {run.SurfaceChangedBetweenFrames}. Without BOTH, nothing proves the second "
            + "composite arrived");
        Assert.Equal(run.SecondInkPoints, run.SecondInkMatches);
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

        Assert.True(run.MoveRectMatches == run.MachineHasInteractiveDesktop,
            $"the OS holds {run.RectAfterMove} where {run.RequestedRectAfterMove} was asked for");
        Assert.Equal(run.MachineHasInteractiveDesktop, run.ContentSurvivedTheMove);
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
    public void PRESENTALSOREFUSESANINKLESSFRAME_NotOnlyPaint()
    {
        // A round-1 sweep survivor: the refusal existed on both entry points and only ONE of them
        // was asked, so deleting the guard from Present cost nothing. A window that is shown before
        // anything provable was composited into it is the exact ghost this capability exists to
        // make unreachable, and Present is the call that shows it.
        using var surface = new Win32GlyphSurface();
        var state = surface.Present(
            new GlyphSurfaceRequest(new GlyphBounds(0, 0, 16, 16), 1.0, ClickThrough: true),
            GlyphFrame.Solid(16, 16, 255, 255, 255, 0));

        var refusal = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(GlyphReasonCodes.GlyphFrameCarriesNoProvableInk, refusal.Reason.Code);

        // And nothing was created: the refusal happens BEFORE any window exists.
        Assert.Equal(0, surface.NativeHandles.Window);
        Assert.False(surface.IsPresenting);
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
        Assert.True(run.ContentSurvivedTheWithdraw == run.MachineHasInteractiveDesktop,
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
    [InlineData(0.0005, 1)]
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
        Assert.IsType<UnsupportedGlyphSurface>(GlyphSurfaceFactory.CreateFor(GlyphHostPlatform.MacOs));
        Assert.IsType<UnsupportedGlyphSurface>(GlyphSurfaceFactory.CreateFor(GlyphHostPlatform.Unknown));

        // The parameterless factory is the named one applied to this process's platform, and that
        // is asserted WITHOUT a platform predicate of the test's own: an OS check here would be a
        // second, independent opinion about which platform this is, and the two could drift.
        using var byName = GlyphSurfaceFactory.CreateFor(GlyphSurfaceFactory.CurrentPlatform());
        using var byDefault = GlyphSurfaceFactory.Create();
        Assert.Equal(byName.GetType(), byDefault.GetType());
    }

    [Fact]
    public void ADisposedSurfaceRefusesEverything_AndSaysThatItWasDisposed()
    {
        var surface = new Win32GlyphSurface();
        surface.Dispose();

        var frame = GlyphFrame.Solid(8, 8, 10, 20, 30, 255);
        var states = new[]
        {
            surface.Present(new GlyphSurfaceRequest(new GlyphBounds(0, 0, 8, 8), 1.0, true), frame),
            surface.Paint(frame),
            surface.MoveTo(new GlyphBounds(0, 0, 8, 8)),
            surface.Withdraw(),
        };

        // Pinned at statement depth 0 so an emptied array cannot make the loop below assert nothing.
        Assert.Equal(4, states.Length);
        foreach (var state in states)
        {
            var refusal = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(GlyphReasonCodes.GlyphSurfaceDisposed, refusal.Reason.Code);
        }
    }

    [Fact]
    public void AnUnpresentedSurfaceRefusesPaintMoveAndWithdraw()
    {
        using var surface = new Win32GlyphSurface();
        var states = new[]
        {
            surface.Paint(GlyphFrame.Solid(8, 8, 10, 20, 30, 255)),
            surface.MoveTo(new GlyphBounds(0, 0, 8, 8)),
            surface.Withdraw(),
        };

        Assert.Equal(3, states.Length);
        foreach (var state in states)
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

        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            run.PresentDetail.Contains("TRANSPARENT", StringComparison.Ordinal));
        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            run.PresentDetail.Contains("headed claim", StringComparison.Ordinal));
    }

    [Fact]
    public void AndTheMOVEsAvailableSaysExactlyWhatItDidNotReask()
    {
        var run = GlyphSurfaceObservations.Lifecycle;

        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            run.MoveDetail.Contains("NO z-order was walked", StringComparison.Ordinal));
        Assert.Equal(
            run.MachineHasInteractiveDesktop,
            run.MoveDetail.Contains("NO hit test", StringComparison.Ordinal));
    }
}
