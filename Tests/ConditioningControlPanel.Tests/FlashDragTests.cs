using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Flashes v2 wave 2, drag and fling: the pure physics behind picking a flash up and throwing it.
/// The mouse hook only feeds this pointer samples and the compositor tick only feeds it delta
/// time, so everything deciding how the gesture FEELS is in here - the tap window that keeps an
/// ordinary click working, the staleness rule that stops a parked pointer throwing, the 25% a
/// bounce takes, and how friction brings it to rest.
///
/// Geometry is a 1920x1080 work area at the origin and a 200x100 flash unless a test says so.
/// </summary>
public class FlashDragTests
{
    private const double BW = 1920, BH = 1080;

    private static FlashMotionState Still(double x = 800, double y = 500, double w = 200, double h = 100)
        => new()
        {
            Style = FlashMotionStyle.Still,
            X = x, Y = y, W = w, H = h, MediaW = w, MediaH = h,
            BoundsX = 0, BoundsY = 0, BoundsW = BW, BoundsH = BH,
        };

    private static FlashMotionState Flying(double vx, double vy, double x = 800, double y = 500)
    {
        var s = Still(x, y);
        s.Drag = new FlashDragState { Held = false, Flinging = true };
        s.Vx = vx;
        s.Vy = vy;
        return s;
    }

    private static double Speed(FlashMotionState s) => Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);

    /// <summary>Grab the rect by its top-left, drag it along a straight line, let go.</summary>
    private static FlashMotionState Throw(double dxPerMs, double dyPerMs,
        MotionLevel level = MotionLevel.Full, double moves = 5, double stepMs = 10)
    {
        var s = Still();
        var d = FlashDrag.Begin(s, s.X, s.Y, 0);
        double t = 0, px = s.X, py = s.Y;
        for (var i = 0; i < moves; i++)
        {
            t += stepMs;
            px += dxPerMs * stepMs;
            py += dyPerMs * stepMs;
            FlashDrag.Sample(d, px, py, t);
        }
        FlashDrag.Step(s, stepMs * moves / 1000.0);      // let the rect catch up to the pointer
        FlashDrag.Release(s, t, level);
        return s;
    }

    // ---------------------------------------------------------------- grabbing

    [Fact]
    public void Begin_KeepsTheGrabOffsetSoThePictureNeverJumps()
    {
        var s = Still(x: 800, y: 500);
        FlashDrag.Begin(s, 850, 530, 0);             // grabbed 50,30 inside the picture
        FlashDrag.Sample(s.Drag!, 1050, 730, 16);    // pointer moved +200,+200
        Assert.True(FlashDrag.Step(s, 0.016));
        Assert.Equal(1000, s.X, 6);                  // so the rect moved +200,+200 too
        Assert.Equal(700, s.Y, 6);
    }

    [Fact]
    public void Begin_TakesAPendulumOutOfItsRopeMathsForTheDuration()
    {
        // The renderer only rotates a Pendulum item, so a held flash must not still be one.
        var s = Still();
        s.Style = FlashMotionStyle.Pendulum;
        s.Rope = 400;
        s.AngleRad = 0.2;
        FlashDrag.Begin(s, s.X, s.Y, 0);
        Assert.Equal(FlashMotionStyle.DriftBounce, s.Style);
        Assert.Equal(0, s.Vx);
        Assert.Equal(0, s.Vy);
    }

    [Fact]
    public void AHeldFlashThatIsNotMovingReportsClean()
    {
        // The layer dirties on a true return, and a fullscreen present is not free.
        var s = Still();
        FlashDrag.Begin(s, s.X, s.Y, 0);
        Assert.False(FlashDrag.Step(s, 0.016));
        Assert.False(FlashDrag.Step(s, 0.016));
    }

    [Fact]
    public void AFlashDraggedOffTheEdgeIsNotPulledBack()
    {
        // The walls are for the throw. A hand can put the picture anywhere, half off-screen
        // included, and it must not snap out from under the cursor.
        var s = Still(x: 100, y: 100);
        FlashDrag.Sample(FlashDrag.Begin(s, 100, 100, 0), -300, -200, 16);
        FlashDrag.Step(s, 0.016);
        Assert.Equal(-300, s.X, 6);
        Assert.Equal(-200, s.Y, 6);
    }

    // ---------------------------------------------------------------- the sampler

    [Fact]
    public void Velocity_ComesFromTheLastMoveOnly()
    {
        var s = Still();
        var d = FlashDrag.Begin(s, 0, 0, 0);
        FlashDrag.Sample(d, 100, 0, 100);     // a slow 1.0 px/ms opening
        FlashDrag.Sample(d, 400, 0, 150);     // then a 6 px/ms snap
        var (vx, vy) = FlashDrag.SampleVelocity(d, 150);
        Assert.Equal(6.0, vx, 6);
        Assert.Equal(0.0, vy, 6);
    }

    [Fact]
    public void Velocity_IsZeroBeforeTheSecondSample()
    {
        var s = Still();
        Assert.Equal((0.0, 0.0), FlashDrag.SampleVelocity(FlashDrag.Begin(s, 0, 0, 0), 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(120)]       // exactly on the line still counts
    public void Velocity_SurvivesUntilTheSampleGoesStale(double idleMs)
    {
        var s = Still();
        var d = FlashDrag.Begin(s, 0, 0, 0);
        FlashDrag.Sample(d, 0, 0, 100);
        FlashDrag.Sample(d, 200, 0, 150);
        var (vx, _) = FlashDrag.SampleVelocity(d, 150 + idleMs);
        Assert.Equal(4.0, vx, 6);
    }

    [Fact]
    public void Velocity_IsZeroOnceThePointerHasBeenParked()
    {
        // Drag it across, hold still for a beat, let go: that is a placement, not a throw.
        var s = Still();
        var d = FlashDrag.Begin(s, 0, 0, 0);
        FlashDrag.Sample(d, 0, 0, 100);
        FlashDrag.Sample(d, 200, 0, 150);
        Assert.Equal((0.0, 0.0), FlashDrag.SampleVelocity(d, 150 + FlashDrag.StaleMs + 1));
    }

    [Fact]
    public void Velocity_SurvivesTwoSamplesInsideTheSameMillisecond()
    {
        // A 1 kHz mouse can land two moves on one tick; dividing by that zero would be infinite.
        var s = Still();
        var d = FlashDrag.Begin(s, 0, 0, 0);
        FlashDrag.Sample(d, 0, 0, 100);
        FlashDrag.Sample(d, 100, 0, 150);
        FlashDrag.Sample(d, 110, 0, 150);
        var (vx, _) = FlashDrag.SampleVelocity(d, 150);
        Assert.False(double.IsInfinity(vx));
        Assert.Equal(2.2, vx, 6);           // 110 px over the 50 ms since the kept predecessor
    }

    // ---------------------------------------------------------------- tap vs drag

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5.9, 249, true)]
    [InlineData(6.0, 10, false)]        // moved too far
    [InlineData(1.0, 250, false)]       // held too long
    public void IsTap_HoldsTheLineAtSixPixelsAndAQuarterSecond(double travel, double ms, bool tap)
        => Assert.Equal(tap, FlashDrag.IsTap(travel, ms));

    [Fact]
    public void AClickStillPops()
    {
        var s = Still();
        FlashDrag.Begin(s, s.X, s.Y, 0);
        Assert.Equal(FlashDragOutcome.Tap, FlashDrag.Release(s, 80, MotionLevel.Full));
        Assert.Null(s.Drag);
    }

    [Fact]
    public void TheTravelTallyIsTheFurthestPointNotTheFinalOne()
    {
        // A long press that wanders out and comes back is a drag, not a click.
        var s = Still();
        var d = FlashDrag.Begin(s, 500, 500, 0);
        FlashDrag.Sample(d, 900, 500, 50);
        FlashDrag.Sample(d, 500, 500, 100);
        Assert.NotEqual(FlashDragOutcome.Tap, FlashDrag.Release(s, 150, MotionLevel.Full));
    }

    [Fact]
    public void ASlowHaulIsAPlacement()
    {
        var s = Throw(dxPerMs: 0.2, dyPerMs: 0);     // under the throw threshold
        Assert.Null(s.Drag);
        Assert.Equal(0, s.Vx);
        Assert.Equal(0, s.Vy);
    }

    // ---------------------------------------------------------------- the throw

    [Theory]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]         // exactly on the threshold throws
    [InlineData(2.0, true)]
    public void ShouldFling_HoldsTheLineAtHalfAPixelPerMillisecond(double vxPerMs, bool fling)
        => Assert.Equal(fling, FlashDrag.ShouldFling(vxPerMs, 0));

    [Fact]
    public void ShouldFling_MeasuresTheDiagonalNotEitherAxis()
    {
        // 0.4 on each axis is 0.57 across, which clears the bar even though neither axis does.
        Assert.False(FlashDrag.ShouldFling(0.4, 0));
        Assert.True(FlashDrag.ShouldFling(0.4, 0.4));
    }

    [Fact]
    public void AThrowKeepsTheVelocityInPixelsPerSecond()
    {
        var s = Throw(dxPerMs: 2.0, dyPerMs: -1.0);
        Assert.True(s.Drag!.Flinging);
        Assert.Equal(2000, s.Vx, 3);
        Assert.Equal(-1000, s.Vy, 3);

        var before = s.X;
        Assert.True(FlashDrag.Step(s, 0.016));
        Assert.True(s.X > before);
    }

    // ---------------------------------------------------------------- motion level

    [Fact]
    public void ReducedMotionHalvesTheThrow()
    {
        var full = Throw(dxPerMs: 2.0, dyPerMs: 1.0);
        var reduced = Throw(dxPerMs: 2.0, dyPerMs: 1.0, level: MotionLevel.Reduced);
        Assert.Equal(full.Vx / 2, reduced.Vx, 3);
        Assert.Equal(full.Vy / 2, reduced.Vy, 3);
    }

    [Fact]
    public void MotionOffStillDragsButNeverThrows()
    {
        var s = Still();
        var d = FlashDrag.Begin(s, s.X, s.Y, 0);

        // The drag itself works at every level: the picture follows the pointer.
        FlashDrag.Sample(d, s.X + 300, s.Y, 20);
        Assert.True(FlashDrag.Step(s, 0.020));
        var dropped = s.X;

        Assert.Equal(FlashDragOutcome.Place, FlashDrag.Release(s, 20, MotionLevel.Off));
        Assert.Equal(0, s.Vx);
        Assert.Null(s.Drag);

        // ...and it holds that spot, because nothing is left to step it.
        Assert.False(FlashMotion.Step(s, 1.0));
        Assert.Equal(dropped, s.X, 6);
    }

    // ---------------------------------------------------------------- bounce and friction

    [Fact]
    public void ABounceTakesAQuarterOfTheSpeedAndMirrorsTheOvershoot()
    {
        var s = Flying(vx: 1000, vy: 0, x: 1700, y: 500);   // 20 px shy of the wall at x=1720
        FlashDrag.Step(s, 0.050);                            // 50 px of travel into a 20 px gap

        Assert.True(s.Vx < 0);                               // turned around
        Assert.Equal(1690, s.X, 6);                          // 1750 reflected about the wall
        // 25% into the wall, then one frame of friction on top.
        var expected = 1000 * FlashDrag.BounceKeep * Math.Pow(FlashDrag.FrictionKeepPerSec, 0.050);
        Assert.Equal(expected, Math.Abs(s.Vx), 3);
    }

    [Fact]
    public void AFlashFlungAtAnyWallEndsUpInsideTheWorkArea()
    {
        foreach (var (vx, vy) in new[] { (4000.0, 0.0), (-4000.0, 0.0), (0.0, 4000.0), (0.0, -4000.0) })
        {
            var s = Flying(vx, vy);
            for (var i = 0; i < 600 && s.Drag != null; i++) FlashDrag.Step(s, 1.0 / 60.0);
            Assert.InRange(s.X, 0, BW - s.W);
            Assert.InRange(s.Y, 0, BH - s.H);
        }
    }

    [Fact]
    public void AWorkAreaOffsetByTheTaskbarIsRespected()
    {
        // A bottom taskbar: the work area starts at y=0 and stops 60 px short.
        var s = Flying(vx: 0, vy: 4000, x: 800, y: 900);
        s.BoundsH = BH - 60;
        for (var i = 0; i < 600 && s.Drag != null; i++) FlashDrag.Step(s, 1.0 / 60.0);
        Assert.InRange(s.Y, 0, BH - 60 - s.H);
    }

    [Fact]
    public void FrictionBringsItToAStop()
    {
        var s = Flying(vx: 1500, vy: 0, x: 100, y: 500);
        var ticks = 0;
        while (s.Drag != null && ticks < 600) { FlashDrag.Step(s, 1.0 / 60.0); ticks++; }

        Assert.Null(s.Drag);                       // settled
        Assert.Equal(0, s.Vx);
        Assert.Equal(0, s.Vy);
        Assert.True(ticks < 600, "the flight should end in well under ten seconds");
        Assert.True(ticks > 6, "and it should not stop dead on the first few frames");
    }

    [Fact]
    public void FrictionIsFrameRateIndependent()
    {
        // The same second of flight must cost the same speed at 30 fps as at 144.
        double SpeedAfterASecond(int fps)
        {
            var s = Flying(vx: 6000, vy: 0, x: 100, y: 500);
            s.BoundsW = 100_000;                   // a clear runway: this is about friction alone
            for (var i = 0; i < fps && s.Drag != null; i++) FlashDrag.Step(s, 1.0 / fps);
            return Speed(s);
        }

        Assert.Equal(SpeedAfterASecond(144), SpeedAfterASecond(30), 3);
        Assert.Equal(6000 * FlashDrag.FrictionKeepPerSec, SpeedAfterASecond(60), 3);
    }

    [Fact]
    public void AFlashWiderThanTheScreenParksInsteadOfJittering()
    {
        var s = Flying(vx: 2000, vy: 0, x: 0, y: 500);
        s.W = 3000;
        FlashDrag.Step(s, 1.0 / 60.0);
        Assert.Equal(0, s.X, 6);
        Assert.Null(s.Drag);
    }

    // ---------------------------------------------------------------- the seam with FlashMotion

    [Fact]
    public void FlashMotionStepHandsAHeldFlashToTheDrag()
    {
        // FlashLayer.Update only ever calls FlashMotion.Step, so the drag has to be reachable
        // from there or a held flash would sit frozen under the cursor.
        var s = Still();
        FlashDrag.Sample(FlashDrag.Begin(s, s.X, s.Y, 0), s.X + 250, s.Y + 120, 16);
        Assert.True(FlashMotion.Step(s, 0.016));
        Assert.Equal(1050, s.X, 6);
        Assert.Equal(620, s.Y, 6);
    }

    [Fact]
    public void ADragOverridesTheStyleUntilItSettles()
    {
        var s = Throw(dxPerMs: 3.0, dyPerMs: 0);
        var flying = s.X;
        FlashMotion.Step(s, 0.016);
        Assert.True(s.X > flying);

        while (s.Drag != null) FlashMotion.Step(s, 1.0 / 60.0);

        // Settled: DriftBounce with no velocity left, so it holds for the rest of its dwell.
        var parked = s.X;
        Assert.False(FlashMotion.Step(s, 1.0));
        Assert.Equal(parked, s.X, 6);
    }

    [Fact]
    public void AZeroDeltaNeverMovesAnything()
    {
        var s = Throw(dxPerMs: 3.0, dyPerMs: 0);
        var x = s.X;
        Assert.False(FlashDrag.Step(s, 0));
        Assert.Equal(x, s.X, 6);
    }

    // ---------------------------------------------------------------- the setting

    [Fact]
    public void DraggableDefaultsOff()
    {
        Assert.False(new AppSettings().FlashDraggable);
        var loaded = JsonConvert.DeserializeObject<AppSettings>("{}", new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        })!;
        Assert.False(loaded.FlashDraggable);
        Assert.True(JsonConvert.DeserializeObject<AppSettings>(
            JsonConvert.SerializeObject(new AppSettings { FlashDraggable = true }))!.FlashDraggable);
    }
}
