using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Glyph;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-115. The module per-pixel alpha unblocked: its motion, its raster, its cadence and its dot.
///
/// <para>Nothing here touches the desktop. The motion is a pure state machine with an injected
/// random source, the surface is a probe double, and the clock is driven by hand — which is the
/// whole point of the seams, and the reason a bounce can be pinned frame by frame without a window
/// anywhere near it.</para>
/// </summary>
public class BouncingTextModuleTests
{
    private static BouncingTextPresentation Dials(
        int speed = 5,
        int size = 100,
        int opacity = 100,
        BouncingTextColourMode mode = BouncingTextColourMode.Random,
        uint? fixedColour = null,
        params string[] phrases) =>
        new(true, speed, size, opacity, mode, fixedColour, Outline: false,
            phrases.Length == 0 ? ["OBEY"] : phrases);

    private static BouncingTextField Field(
        BouncingTextPresentation? dials = null,
        (int X, int Y, int Width, int Height)? bounds = null,
        int seed = 7,
        int wordWidth = 100,
        int wordHeight = 40) =>
        new(dials ?? Dials(),
            bounds ?? (0, 0, 800, 600),
            _ => (wordWidth, wordHeight),
            new Random(seed));

    // ------------------------------------------------------------------ the motion

    [Fact]
    public void THEFIRSTFRAMEESTABLISHESTHEBASELINEAndMovesNOTHING_WhichIsWPFsOwnGuard()
    {
        // BouncingTextService.cs:356-360: the first composition callback records the time and
        // returns. Without it the first step would use a garbage delta and teleport the logo.
        var field = Field();
        var before = field.Rectangle;

        Assert.False(field.Advance(1.0 / 60.0));
        Assert.Equal(before, field.Rectangle);

        Assert.True(field.Advance(1.0 / 60.0));
        Assert.NotEqual(before, field.Rectangle);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void ANonPositiveOrNaNStepMovesNothing(double step)
    {
        var field = Field();
        field.Advance(0.016);
        var before = field.Rectangle;

        Assert.False(field.Advance(step));
        Assert.Equal(before, field.Rectangle);
    }

    [Fact]
    public void ASTALLEDFRAMEISCLAMPEDTo100ms_SoTheLogoNeverTeleportsAcrossTheScreen()
    {
        // WPF's clamp (:367). Measured against the same field advanced by exactly the clamp: a ten
        // second stall must move the logo no further than a 0.1 s frame does.
        var stalled = Field();
        var clamped = Field();
        stalled.Advance(0.016);
        clamped.Advance(0.016);

        stalled.Advance(10.0);
        clamped.Advance(BouncingTextField.MaxStepSeconds);

        Assert.Equal(clamped.Rectangle, stalled.Rectangle);
    }

    [Fact]
    public void THELOGOSTAYSINSIDETHEFIELDFOREVER_AndTheEdgeIsSNAPPEDBackBeforeTheVelocityFlips()
    {
        // The snap (:416-431) is what stops overshoot accumulating. Two thousand frames at the
        // fastest speed is the test: without the snap the logo drifts out of the field.
        var bounds = (X: 0, Y: 0, Width: 800, Height: 600);
        var field = Field(Dials(speed: 10), bounds);
        field.Advance(0.016);

        for (var i = 0; i < 2000; i++)
        {
            field.Advance(0.016);
            var (x, y, width, height) = field.Rectangle;
            Assert.True(x >= bounds.X, $"left edge {x} escaped at frame {i}");
            Assert.True(y >= bounds.Y, $"top edge {y} escaped at frame {i}");
            Assert.True(x + width <= bounds.X + bounds.Width, $"right edge {x + width} escaped at frame {i}");
            Assert.True(y + height <= bounds.Y + bounds.Height, $"bottom edge {y + height} escaped at frame {i}");
        }

        Assert.True(field.Bounces > 0, "two thousand frames at the fastest speed produced no bounce at all");
    }

    [Fact]
    public void ABOUNCECHANGESTHECOLOUR_AndTHATIsWhatMakesARERASTERNecessaryRatherThanAMove()
    {
        // The split between MoveTo and Present is the whole reason this module is portable, and this
        // is the fact that decides which path a frame takes.
        var field = Field(Dials(speed: 10));
        field.Advance(0.016);
        field.RasterTaken();

        var bouncesBefore = field.Bounces;
        for (var i = 0; i < 2000 && field.Bounces == bouncesBefore; i++)
        {
            field.Advance(0.016);
            if (field.Bounces == bouncesBefore)
            {
                Assert.False(field.NeedsRaster, "a frame with no bounce asked for a re-raster");
            }
        }

        Assert.True(field.Bounces > bouncesBefore);
        Assert.True(field.NeedsRaster, "a bounce did not ask for a re-raster, so the colour change would never "
            + "reach the screen");
    }

    [Fact]
    public void ACORNERHITIsBOTHAXESAtOnce_OrASingleAxisBounceWithin15PxOfOne()
    {
        // WPF's rule (:459-470, :609-622). A corner is reachable but not from every start, so this
        // sweeps twenty SEEDED starts in a small square field rather than hunting one lucky seed —
        // and it asserts the arithmetic invariant on every one of them, which is what a mutation
        // that broke the rule would fail regardless of which seed found a corner.
        var seedsWithCorners = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var field = new BouncingTextField(
                Dials(speed: 10), (0, 0, 200, 200), _ => (50, 50), new Random(seed));
            field.Advance(0.016);

            for (var i = 0; i < 4000; i++)
            {
                field.Advance(0.016);
            }

            Assert.True(field.CornerHits <= field.Bounces,
                $"seed {seed} reported {field.CornerHits} corners against {field.Bounces} bounces, which is "
                + "arithmetically impossible");
            if (field.CornerHits > 0)
            {
                seedsWithCorners++;
            }
        }

        Assert.True(seedsWithCorners > 0,
            "twenty seeded runs of four thousand frames in a 200x200 field produced no corner hit at all, so "
            + "the corner rule is unreachable and its tolerance means nothing");
    }

    [Fact]
    public void THESPEEDDIALREALLYCHANGESTHESPEED_AndTheMappingIsWPFsOwn()
    {
        // (3 + rand*2) * 60 px/s times setting/10 (:165-168). The same seed deals the same base, so
        // the ratio between two settings is the ratio of the settings.
        var slow = Field(Dials(speed: 1), seed: 42);
        var fast = Field(Dials(speed: 10), seed: 42);
        slow.Advance(0.016);
        fast.Advance(0.016);
        var slowStart = slow.Rectangle;
        var fastStart = fast.Rectangle;

        slow.Advance(0.05);
        fast.Advance(0.05);

        var slowMoved = Math.Abs(slow.Rectangle.X - slowStart.X) + Math.Abs(slow.Rectangle.Y - slowStart.Y);
        var fastMoved = Math.Abs(fast.Rectangle.X - fastStart.X) + Math.Abs(fast.Rectangle.Y - fastStart.Y);

        Assert.True(fastMoved > slowMoved * 5,
            $"speed 10 moved {fastMoved} px where speed 1 moved {slowMoved}; the dial is not reaching the motion");
    }

    [Fact]
    public void THESAMESEEDDEALSTHESAMETRAJECTORY_AndADifferentOneDoesNot()
    {
        var a = Field(seed: 99);
        var b = Field(seed: 99);
        var c = Field(seed: 100);
        a.Advance(0.016);
        b.Advance(0.016);
        c.Advance(0.016);

        for (var i = 0; i < 200; i++)
        {
            a.Advance(0.016);
            b.Advance(0.016);
            c.Advance(0.016);
        }

        Assert.Equal(a.Rectangle, b.Rectangle);
        Assert.NotEqual(a.Rectangle, c.Rectangle);
    }

    [Fact]
    public void THEFIXEDCOLOURMODENeverRerolls_AndAnEmptyHexIsWPFsHotPink()
    {
        var field = Field(Dials(speed: 10, mode: BouncingTextColourMode.Fixed, fixedColour: 0x00123456));
        field.Advance(0.016);
        Assert.Equal(0x00123456u, field.Logo.Colour);

        for (var i = 0; i < 500; i++)
        {
            field.Advance(0.016);
            Assert.Equal(0x00123456u, field.Logo.Colour);
        }

        var fallback = Field(Dials(speed: 10, mode: BouncingTextColourMode.Fixed));
        fallback.Advance(0.016);
        Assert.Equal(BouncingTextField.HotPink, fallback.Logo.Colour);
    }

    [Fact]
    public void THERAINBOWMODEWalksAnOrderedWheelOneStepPerBounce()
    {
        var field = Field(Dials(speed: 10, mode: BouncingTextColourMode.Rainbow));
        field.Advance(0.016);
        var start = field.Logo.Colour;
        Assert.Contains(start, BouncingTextField.RainbowWheel);

        var seen = new List<uint>();
        for (var i = 0; i < 4000 && seen.Count < 4; i++)
        {
            var before = field.Bounces;
            field.Advance(0.016);
            if (field.Bounces != before)
            {
                seen.Add(field.Logo.Colour);
            }
        }

        Assert.Equal(4, seen.Count);
        foreach (var colour in seen)
        {
            Assert.Contains(colour, BouncingTextField.RainbowWheel);
        }

        // Ordered, not random: consecutive bounces are consecutive wheel positions.
        var first = Array.IndexOf(BouncingTextField.RainbowWheel, seen[0]);
        for (var i = 1; i < seen.Count; i++)
        {
            var expected = BouncingTextField.RainbowWheel[(first + i) % BouncingTextField.RainbowWheel.Length];
            Assert.Equal(expected, seen[i]);
        }
    }

    [Fact]
    public void THERANDOMMODEONLYEVEREMITSWPFsTenColours()
    {
        var field = Field(Dials(speed: 10));
        field.Advance(0.016);
        Assert.Contains(field.Logo.Colour, BouncingTextField.RandomColours);

        for (var i = 0; i < 2000; i++)
        {
            field.Advance(0.016);
            Assert.Contains(field.Logo.Colour, BouncingTextField.RandomColours);
        }
    }

    [Fact]
    public void ANEMPTYPOOLFallsBackToWPFsOwnWord_RatherThanShowingNothing()
    {
        var field = new BouncingTextField(
            new BouncingTextPresentation(
                true, 5, 100, 100, BouncingTextColourMode.Random, null, false, []),
            (0, 0, 800, 600), _ => (100, 40), new Random(3));

        Assert.Equal(BouncingTextField.FallbackText, field.Logo.Text);
    }

    [Fact]
    public void THEWORDCHANGESOnAboutOneBounceInTen_WhichIsWPFsOwnChance()
    {
        var field = Field(Dials(speed: 10, phrases: ["OBEY", "SUBMIT", "DROP", "EMPTY"]), seed: 5);
        field.Advance(0.016);

        var changes = 0;
        var last = field.Logo.Text;
        var bounces = 0;
        for (var i = 0; i < 20000; i++)
        {
            var before = field.Bounces;
            field.Advance(0.016);
            if (field.Bounces == before)
            {
                continue;
            }

            bounces++;
            if (field.Logo.Text != last)
            {
                changes++;
                last = field.Logo.Text;
            }
        }

        Assert.True(bounces > 100, $"only {bounces} bounces in 20000 frames; the sample is too small to say "
            + "anything about a one-in-ten chance");

        // A generous band around 10 %, because the roll can also pick the SAME word.
        Assert.InRange(changes / (double)bounces, 0.02, 0.20);
    }

    // ------------------------------------------------------------------ the presentation

    [Theory]
    [InlineData(100, 72)]
    [InlineData(50, 36)]
    [InlineData(300, 216)]
    public void THESIZEDIALIsWPFsPercentageOfIts72PxBase(int percent, int expected)
    {
        Assert.Equal(expected, Dials(size: percent).FontSize);
    }

    [Fact]
    public void OPACITYZEROIsINVISIBLEAndSaysSo_RatherThanBecomingAGhostSurface()
    {
        Assert.True(Dials(opacity: 0).IsInvisible);
        Assert.Null(Dials(opacity: 0).SurfaceOpacity);
        Assert.False(Dials(opacity: 1).IsInvisible);
        Assert.Equal(1.0, Dials(opacity: 100).SurfaceOpacity);
    }

    [Theory]
    [InlineData("#112233", 0x00332211u)]
    [InlineData("112233", 0x00332211u)]
    [InlineData("", null)]
    [InlineData("nonsense", null)]
    [InlineData("#12345", null)]
    public void THEFIXEDCOLOURPARSERIsWPFsOwn_AndABadValueFallsBackRatherThanThrowing(
        string hex, object? expected)
    {
        Assert.Equal(expected is null ? null : (uint?)(uint)expected, BouncingTextEffect.ParseColour(hex));
    }

    // ------------------------------------------------------------------ the surface's cadence

    [Fact]
    public void THEMODULESCONSTRUCTORCARRIESNOCLOCK_AndThePresentersDoes()
    {
        // SP-106's rule, pinned at the line a future author would edit: an INTERVAL that decides
        // when a MODULE is due is the module's; a CADENCE that keeps a SURFACE correct is the
        // surface's. Moving the clock into the module is the change this fact exists to catch.
        var moduleParameters = typeof(BouncingTextEffect).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(moduleParameters, p => p.ParameterType == typeof(ISessionClock));

        var presenterParameters = typeof(BouncingTextSurfacePresenter).GetConstructors().Single().GetParameters();
        Assert.Contains(presenterParameters, p => p.ParameterType == typeof(ISessionClock));

        // And it derives from the OWNED base, not the paced one: a bouncing logo is never "due".
        Assert.Equal(typeof(OwnedSessionEffect), typeof(BouncingTextEffect).BaseType);
    }

    [Fact]
    public void ENGAGEPUTSTHELOGOUPAndTheCadenceThenMOVESItWithoutRePRESENTING()
    {
        // THE FACT D84 TURNS ON. Moving an overlay means re-Presenting it, which walks the whole
        // z-order and flips click-through; a glyph surface's move is one call. This asserts the
        // module really takes that path: one Present at engage, and every frame after it a MOVE.
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource(),
            () => (0, 0, 800, 600), () => new Random(11));

        Assert.IsType<CapabilityState.Available>(presenter.Engage(Dials(speed: 10)));
        Assert.Equal(1, surface.Presents);
        Assert.True(presenter.Showing);
        Assert.True(presenter.Running);

        // The first advance moves nothing (WPF's baseline frame) but still costs one operation.
        clock.Fire();
        clock.Fire();
        clock.Fire();

        Assert.True(surface.Moves > 0, "the cadence never moved the surface, so the logo is frozen");
        Assert.True(surface.Presents <= 1 + surface.Moves,
            $"{surface.Presents} presents against {surface.Moves} moves - the module is re-presenting per frame, "
            + "which is exactly the cost class D84 blocked this module for");
    }

    [Fact]
    public void ASURFACETHATSTOPSHOLDINGITSCOMPOSITE_IsRETIREDRatherThanFedForever()
    {
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource(),
            () => (0, 0, 800, 600), () => new Random(11));

        presenter.Engage(Dials(speed: 10));
        surface.FailEverything = true;
        clock.Fire();
        clock.Fire();

        Assert.False(presenter.Running);
        Assert.False(presenter.Showing);
        Assert.True(surface.Disposed, "the dead surface was left open");
    }

    [Fact]
    public void WITHDRAWKILLSTHECADENCEWITHTHESURFACE()
    {
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource(),
            () => (0, 0, 800, 600), () => new Random(11));

        presenter.Engage(Dials());
        presenter.Withdraw();

        Assert.False(presenter.Engaged);
        Assert.True(surface.Disposed);

        var movesAtWithdraw = surface.Moves;
        clock.Fire();
        Assert.Equal(movesAtWithdraw, surface.Moves);
    }

    [Fact]
    public void ABUILDTHATCANNOTRASTERSaysSoAndPlacesNOTHING()
    {
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource { Render = false },
            () => (0, 0, 800, 600), () => new Random(11));

        var state = Assert.IsType<CapabilityState.Degraded>(presenter.Engage(Dials()));
        Assert.Equal(EffectReasonCodes.BouncingTextNoRaster, state.Reason.Code);
        Assert.Equal(0, surface.Presents);
        Assert.False(presenter.Running);
    }

    [Fact]
    public void NODISPLAYIsUNAVAILABLE_TheWholeChannelBeingGone()
    {
        var clock = new HandClock();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => new ProbeGlyphSurface(), new ProbeTextSource(),
            () => null, () => new Random(11));

        var state = Assert.IsType<CapabilityState.Unavailable>(presenter.Engage(Dials()));
        Assert.Equal(EffectReasonCodes.BouncingTextNoDisplay, state.Reason.Code);
    }

    [Fact]
    public void OPACITYZEROENGAGESDEGRADEDAndPlacesNoSurfaceAtAll()
    {
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource(),
            () => (0, 0, 800, 600), () => new Random(11));

        var state = Assert.IsType<CapabilityState.Degraded>(presenter.Engage(Dials(opacity: 0)));
        Assert.Equal(EffectReasonCodes.BouncingTextTransparent, state.Reason.Code);
        Assert.Equal(0, surface.Presents);
    }

    [Fact]
    public void THEOPACITYDIALREACHESTHESURFACEAsTheUNIFORMMultiplier_NotAsBakedInAlpha()
    {
        // WPF sets the same value as the text element's Opacity and leaves the glyph's own
        // antialiased alpha alone (:975). Keeping that structure is also what keeps the surface's
        // read-back anchored at alpha 255 whatever the dial says.
        var clock = new HandClock();
        var surface = new ProbeGlyphSurface();
        using var presenter = new BouncingTextSurfacePresenter(
            clock, action => action(), () => surface, new ProbeTextSource(),
            () => (0, 0, 800, 600), () => new Random(11));

        presenter.Engage(Dials(opacity: 40));

        Assert.NotNull(surface.LastRequest);
        Assert.Equal(102, surface.LastRequest!.ConstantAlpha);
        Assert.NotNull(surface.LastFrame);
        Assert.True(surface.LastFrame!.HasProvableInk,
            "the frame handed to the surface has no fully-opaque ink, so the dial was baked into the alpha and "
            + "the read-back has lost its anchor");
    }

    // ------------------------------------------------------------------ doubles

    /// <summary>A clock whose timer only fires when a test says so. No wall-clock wait anywhere.</summary>
    private sealed class HandClock : ISessionClock
    {
        private Action? _due;

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            _due = fire;
            return new Handle(this, fire);
        }

        /// <summary>Fire whatever is scheduled, once.</summary>
        public void Fire()
        {
            var due = _due;
            _due = null;
            UtcNow = UtcNow.AddMilliseconds(20);
            due?.Invoke();
        }

        private sealed class Handle(HandClock clock, Action fire) : IDisposable
        {
            public void Dispose()
            {
                if (ReferenceEquals(clock._due, fire))
                {
                    clock._due = null;
                }
            }
        }
    }

    /// <summary>A rasteriser that needs no GDI+ and no font.</summary>
    private sealed class ProbeTextSource : IGlyphTextSource
    {
        public bool Render { get; init; } = true;

        public (int Width, int Height) Measure(string text, BouncingTextPresentation presentation) => (120, 48);

        GlyphFrame? IGlyphTextSource.Render(
            string text, uint colour, int width, int height, BouncingTextPresentation presentation) =>
            Render ? GlyphFrame.Solid(width, height, 0x40, 0x80, 0xC0, 0xFF) : null;
    }

    /// <summary>A surface that counts what it was asked to do. It never touches a window.</summary>
    private sealed class ProbeGlyphSurface : IGlyphSurface
    {
        public int Presents { get; private set; }

        public int Moves { get; private set; }

        public int Paints { get; private set; }

        public bool Disposed { get; private set; }

        public bool FailEverything { get; set; }

        public GlyphSurfaceRequest? LastRequest { get; private set; }

        public GlyphFrame? LastFrame { get; private set; }

        public bool IsPresenting { get; private set; }

        public CapabilityState Present(GlyphSurfaceRequest request, GlyphFrame frame)
        {
            if (FailEverything)
            {
                return Refuse();
            }

            Presents++;
            LastRequest = request;
            LastFrame = frame;
            IsPresenting = true;
            return new CapabilityState.Available("probe surface composited");
        }

        public CapabilityState Paint(GlyphFrame frame)
        {
            if (FailEverything)
            {
                return Refuse();
            }

            Paints++;
            LastFrame = frame;
            return new CapabilityState.Available("probe surface repainted");
        }

        public CapabilityState MoveTo(GlyphBounds bounds)
        {
            if (FailEverything)
            {
                return Refuse();
            }

            Moves++;
            return new CapabilityState.Available("probe surface moved");
        }

        public void Reassert()
        {
        }

        public CapabilityState Withdraw()
        {
            IsPresenting = false;
            return new CapabilityState.Available("probe surface withdrawn");
        }

        public void Dispose() => Disposed = true;

        private static CapabilityState Refuse() =>
            new CapabilityState.Unavailable(
                new CapabilityReason(GlyphReasonCodes.GlyphNotComposited, "the probe was told to fail"));
    }
}
