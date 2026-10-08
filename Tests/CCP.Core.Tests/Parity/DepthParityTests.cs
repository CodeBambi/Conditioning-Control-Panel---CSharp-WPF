using System;
using System.Linq;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5: the depth law (DepthRulesTests), the HUD plank press (HudDepthTests,
/// pure half), the rail coin (NavRailDepthTests, pure half) and Depth.xaml as data. Numbers are
/// pinned by value so a drive-by edit to either side shows up.
/// </summary>
public class DepthParityTests
{
    [Fact]
    public void OnIsPressedInAndHoverLiftsAndPressWins()
    {
        Assert.Equal(0, DepthRules.TravelFor(enabled: true, pressed: false, active: false, hovered: false));
        Assert.Equal(DepthRules.ActiveSinkPx, DepthRules.TravelFor(true, false, true, false));
        Assert.Equal(-DepthRules.HoverLiftPx, DepthRules.TravelFor(true, false, false, true));
        Assert.Equal(DepthRules.ActiveSinkPx, DepthRules.TravelFor(true, false, true, true));
        Assert.Equal(DepthRules.PressTravelPx, DepthRules.TravelFor(true, true, true, true));
        Assert.Equal(0, DepthRules.TravelFor(false, true, true, true));
    }

    [Fact]
    public void TheShadowTellsTheHeight()
    {
        Assert.Equal(DepthRules.RaisedPx, DepthRules.ShadowFor(true, false, false, false));
        Assert.Equal(DepthRules.RaisedPx + DepthRules.HoverLiftPx, DepthRules.ShadowFor(true, false, false, true));
        Assert.Equal(0, DepthRules.ShadowFor(true, true, false, false));
        Assert.Equal(0, DepthRules.ShadowFor(true, false, true, false));
        Assert.Equal(0, DepthRules.ShadowFor(false, false, false, false));
    }

    [Fact]
    public void TheNumbersAreTheWpfNumbers()
    {
        Assert.Equal(3.0, DepthRules.RaisedPx);
        Assert.Equal(2.0, DepthRules.HoverLiftPx);
        Assert.Equal(2.0, DepthRules.PressTravelPx);
        Assert.Equal(1.0, DepthRules.ActiveSinkPx);
        Assert.Equal(10.0, DepthRules.FloatPx);
        Assert.Equal(5.0, DepthRules.StartPx);
        Assert.Equal(9.0, DepthRules.WellPx);
        Assert.Equal(6.0, DepthRules.RailShadowPx);
        Assert.Equal(14.0, DepthRules.LedgeUpPx);
        Assert.Equal(0.26, DepthRules.HighlightAlpha);
        Assert.Equal(0.50, DepthRules.ShadeAlpha);
        Assert.Equal(0.09, DepthRules.SheenAlpha);
        Assert.Equal(0.62, DepthRules.ShadowAlpha);
        Assert.Equal(0.65, DepthRules.WellTopAlpha);
        Assert.Equal(0.35, DepthRules.WellLeftAlpha);
        Assert.Equal(0.07, DepthRules.WellFootAlpha);
        Assert.Equal(0.40, DepthRules.FloatAlpha);
        Assert.Equal(0.70, DepthRules.LedgeUpAlpha);
        Assert.Equal(0.60, DepthRules.PressedShadeAlpha);
        Assert.Equal((90, 140, 120), (DepthRules.PressMs, DepthRules.ReleaseMs, DepthRules.HoverMs));
        Assert.Equal(1.0, DepthRules.ReleaseOvershootPx);
        Assert.Equal(1.2, DepthRules.TiltDegrees);
        Assert.Equal(0.28, DepthRules.ShadowHuePull);
        Assert.Equal(0xFF060210u, DepthRules.NeutralShadowInk);
    }

    [Fact]
    public void HeightsAreSubtleExceptStart()
    {
        Assert.InRange(DepthRules.RaisedPx, 2, 4);
        Assert.InRange(DepthRules.HoverLiftPx, 1, 3);
        Assert.InRange(DepthRules.PressTravelPx, 1, 3);
        Assert.True(DepthRules.StartPx > DepthRules.RaisedPx);
        Assert.True(DepthRules.FloatPx > DepthRules.RaisedPx);
        Assert.True(DepthRules.WellTopAlpha > DepthRules.WellLeftAlpha);
        Assert.True(DepthRules.HighlightAlpha < DepthRules.ShadeAlpha);
    }

    [Theory]
    [InlineData(MotionLevel.Full, 90)]
    [InlineData(MotionLevel.Reduced, 45)]
    [InlineData(MotionLevel.Off, 0)]
    public void MotionScalesTheTimings(MotionLevel level, int expected)
        => Assert.Equal(expected, DepthRules.Ms(DepthRules.PressMs, level));

    [Fact]
    public void TiltOnlyAboveThePerformanceTierAndNeverAtMotionOff()
    {
        Assert.True(DepthRules.TiltAllowed(MotionLevel.Full, PerformanceTier.Balanced));
        Assert.True(DepthRules.TiltAllowed(MotionLevel.Reduced, PerformanceTier.Quality));
        Assert.False(DepthRules.TiltAllowed(MotionLevel.Off, PerformanceTier.Quality));
        Assert.False(DepthRules.TiltAllowed(MotionLevel.Full, PerformanceTier.Performance));
    }

    [Theory]
    [InlineData(NavSections.Home)]
    [InlineData(NavSections.Studio)]
    [InlineData(NavSections.Social)]
    [InlineData(NavSections.Library)]
    public void ShadowsAreInkPulledTowardTheSectionNeverTheHueItself(string section)
    {
        var hue = NavStripRules.Accent(section);
        var sh = DepthRules.ShadowColor(hue);
        Assert.Equal((byte)158, Argb.A(sh));
        Assert.True(Argb.R(sh) < Argb.R(hue) && Argb.G(sh) < Argb.G(hue) && Argb.B(sh) < Argb.B(hue));
        Assert.True(Argb.R(sh) < 0x60 && Argb.G(sh) < 0x60 && Argb.B(sh) < 0x60);
        static int Dominant(uint c) => Argb.R(c) >= Argb.G(c) && Argb.R(c) >= Argb.B(c) ? 0 : Argb.G(c) >= Argb.B(c) ? 1 : 2;
        Assert.Equal(Dominant(hue), Dominant(sh));
        Assert.Equal((byte)102, Argb.A(DepthRules.ShadowColor(hue, DepthRules.FloatAlpha)));
    }

    [Fact]
    public void TheShadowInkIsTheExactWpfByteMix()
    {
        // Mix(#060210, Lilac #B79CFF, 0.28) at 0.62: R = round(6 + (183-6)*.28) = 56 (0x38),
        // G = round(2 + (156-2)*.28) = 45 (0x2D), B = round(16 + (255-16)*.28) = 83 (0x53).
        Assert.Equal(0x9E382D53u, DepthRules.ShadowColor(NavStripRules.Lilac));
    }

    // ---- HUD plank (HudDepthTests, pure half) ----------------------------------------------

    [Fact]
    public void APlankLiftsOnHoverDropsOnPressAndNeverSitsLit()
    {
        Assert.Equal(0, HudPlankRules.Travel(enabled: true, pressed: false, hovered: false));
        Assert.Equal(-DepthRules.HoverLiftPx, HudPlankRules.Travel(true, false, true));
        Assert.Equal(DepthRules.PressTravelPx, HudPlankRules.Travel(true, true, true));
        Assert.Equal(0, HudPlankRules.Travel(false, true, true));
    }

    [Fact]
    public void TheDropIsRaisedPxAndStartPxForTheChunkyPlankAndGoneWhilePressed()
    {
        Assert.Equal(DepthRules.RaisedPx, HudPlankRules.DropLength(HudPlankKind.Plank, true, false, false));
        Assert.Equal(DepthRules.StartPx, HudPlankRules.DropLength(HudPlankKind.Chunky, true, false, false));
        Assert.Equal(DepthRules.RaisedPx + DepthRules.HoverLiftPx, HudPlankRules.DropLength(HudPlankKind.Plank, true, false, true));
        Assert.Equal(DepthRules.StartPx + DepthRules.HoverLiftPx, HudPlankRules.DropLength(HudPlankKind.Chunky, true, false, true));
        Assert.Equal(0, HudPlankRules.DropLength(HudPlankKind.Chunky, true, true, true));
        Assert.Equal(0, HudPlankRules.DropLength(HudPlankKind.Plank, false, false, false));
        Assert.Equal(0, HudPlankRules.DropLength(HudPlankKind.None, true, false, false));
        foreach (var kind in new[] { HudPlankKind.Plank, HudPlankKind.Chunky })
            Assert.Equal(HudPlankRules.DropLength(kind, true, false, true), HudPlankRules.DropBaseHeight(kind));
        // The rendered WPF START plank at rest: ScaleY = StartPx / base.
        Assert.Equal(DepthRules.StartPx / HudPlankRules.DropBaseHeight(HudPlankKind.Chunky),
            HudPlankRules.DropScale(HudPlankKind.Chunky, true, false, false), 6);
        Assert.Equal(0, (int)HudPlankKind.None);
        Assert.Equal(2, (int)HudPlankKind.Chunky);
    }

    [Fact]
    public void PressIsFastReleaseSpringsAndMotionOffIsStatic()
    {
        Assert.Equal(DepthRules.PressMs, HudPlankRules.DurationFor(pressed: true, releasing: false, MotionLevel.Full));
        Assert.Equal(DepthRules.ReleaseMs, HudPlankRules.DurationFor(false, true, MotionLevel.Full));
        Assert.Equal(DepthRules.HoverMs, HudPlankRules.DurationFor(false, false, MotionLevel.Full));
        Assert.Equal(DepthRules.ReleaseMs / 2, HudPlankRules.DurationFor(false, true, MotionLevel.Reduced));
        Assert.Equal(0, HudPlankRules.DurationFor(true, false, MotionLevel.Off));
        Assert.Equal(0, HudPlankRules.DurationFor(false, true, MotionLevel.Off));
    }

    [Fact]
    public void TheReleaseSpringOvershootsUpThenSettles()
    {
        var track = HudPlankRules.FaceTrack(0, releasing: true, DepthRules.ReleaseMs);
        Assert.Equal(2, track.Length);
        Assert.Equal(DepthRules.ReleaseMs * 0.6, track[0].TimeMs, 9);
        Assert.Equal(-DepthRules.ReleaseOvershootPx, track[0].Value);
        Assert.Equal(EaseKind.QuadOut, track[0].Ease);
        Assert.Equal(0, track[1].Value);
        Assert.Equal(EaseKind.QuadInOut, track[1].Ease);
        // Sampled from the pressed face (+2): passes rest at 60%, back to rest at the end.
        Assert.Equal(-1, Keyframes.Sample(track, DepthRules.PressTravelPx, 84), 9);
        Assert.Equal(0, Keyframes.Sample(track, DepthRules.PressTravelPx, 140), 9);
        Assert.Equal(DepthRules.PressTravelPx, Keyframes.Sample(track, DepthRules.PressTravelPx, 0), 9);
    }

    // ---- the rail coin (NavRailDepthTests, pure half) -------------------------------------

    [Theory]
    [InlineData(false, false, false, 0.0)]
    [InlineData(false, false, true, -DepthRules.HoverLiftPx)]
    [InlineData(false, true, false, DepthRules.ActiveSinkPx)]
    [InlineData(false, true, true, DepthRules.ActiveSinkPx)]
    [InlineData(true, false, true, DepthRules.PressTravelPx)]
    [InlineData(true, true, false, DepthRules.PressTravelPx)]
    public void ACoinTravelsByTheSharedLaw(bool pressed, bool active, bool hovered, double expected)
    {
        Assert.Equal(expected, NavRailRules.CoinTravel(pressed, active, hovered));
        Assert.Equal(DepthRules.TravelFor(true, pressed, active, hovered), NavRailRules.CoinTravel(pressed, active, hovered));
    }

    [Theory]
    [InlineData(false, false, false, DepthRules.RaisedPx)]
    [InlineData(false, false, true, DepthRules.RaisedPx + DepthRules.HoverLiftPx)]
    [InlineData(false, true, false, 0.0)]
    [InlineData(true, false, false, 0.0)]
    public void OnIsPressedInSoALitOrPressedCoinThrowsNoShadow(bool pressed, bool active, bool hovered, double expected)
        => Assert.Equal(expected, NavRailRules.CoinShadow(pressed, active, hovered));

    [Fact]
    public void TheReleaseSpringPassesItsTargetByTheOvershootThenSettles()
    {
        Assert.Equal(DepthRules.ActiveSinkPx - DepthRules.ReleaseOvershootPx,
            NavRailRules.CoinOvershoot(DepthRules.PressTravelPx, DepthRules.ActiveSinkPx));
        Assert.Equal(-DepthRules.HoverLiftPx - DepthRules.ReleaseOvershootPx,
            NavRailRules.CoinOvershoot(DepthRules.PressTravelPx, -DepthRules.HoverLiftPx));
        Assert.Equal(1.0, NavRailRules.CoinOvershoot(1.0, 1.0));
    }

    [Fact]
    public void TheTiltNeverPassesTheLauncherNumberAndOnlyIdleCoinsLean()
    {
        foreach (var nx in new[] { -3.0, -1, -0.4, 0, 0.5, 1, 4 })
            foreach (var ny in new[] { -2.0, -1, 0, 0.3, 1, 5 })
                Assert.InRange(Math.Abs(NavRailRules.CoinTilt(nx, ny)), 0, DepthRules.TiltDegrees + 1e-9);
        Assert.Equal(-DepthRules.TiltDegrees, NavRailRules.CoinTilt(1, 1), 6);
        Assert.Equal(0, NavRailRules.CoinTilt(0, 0));
        Assert.True(NavRailRules.CoinTilts(false, MotionLevel.Full, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(true, MotionLevel.Full, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(false, MotionLevel.Off, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(false, MotionLevel.Full, PerformanceTier.Performance));
        Assert.Equal(90, NavRailRules.CoinTiltMs);
    }

    [Fact]
    public void TheCoinShadowAndTheRailShadowAreTintedByTheHue()
    {
        foreach (var section in NavRailRules.RailSections.Select(s => s.Key).Append(NavSections.Settings))
        {
            var hue = NavStripRules.Accent(section);
            var disc = NavRailRules.CoinDiscStops(hue);
            Assert.Equal(DepthRules.ShadowColor(hue), disc[0].Color);
            Assert.Equal((byte)Math.Round(DepthRules.ShadowAlpha * 255), Argb.A(disc[0].Color));
            Assert.Equal((byte)0x5C, Argb.A(disc[1].Color));
            Assert.Equal((byte)0, Argb.A(disc[^1].Color));
            Assert.Equal(1.0, disc[^1].Offset);
            var rail = NavRailRules.RailShadowStops(hue);
            Assert.Equal((byte)0xBF, Argb.A(rail[0].Color));
            Assert.Equal((byte)0, Argb.A(rail[^1].Color));
            Assert.Equal(Argb.R(disc[0].Color), Argb.R(rail[0].Color));
            Assert.Equal(Argb.B(disc[0].Color), Argb.B(rail[0].Color));
        }
    }

    // ---- Depth.xaml as data ---------------------------------------------------------------

    [Fact]
    public void ThePaletteHoldsEveryBrushTheLanesComposeFrom()
    {
        foreach (var key in new[]
                 {
                     "DepthRaisedBevel", "DepthRaisedSheen", "DepthPlankSheen", "DepthPlankBevel",
                     "DepthPressedBevel", "DepthPressedShade", "DepthDropBand", "DepthDropDisc",
                     "DepthWellTop", "DepthWellLeft", "DepthWellFoot", "DepthWellFloorBrush",
                     "DepthFloatRim", "DepthFloatBand", "DepthLedgeUp", "DepthRailShadow",
                     "DepthTubeGloss", "DepthTubeBead", "DepthCoinDish", "DepthCoinRim",
                     "DepthCoinSocket", "DepthCoinInnerShadow", "DepthCoinOuterRim", "DepthCoinSpecular",
                 })
            Assert.NotNull(DepthPalette.Find(key));
        Assert.Equal(28, DepthPalette.All.Count);
        Assert.Equal(DepthPalette.All.Count, DepthPalette.All.Select(b => b.Key).Distinct().Count());
    }

    [Fact]
    public void ThePaletteStopsAreTheXamlStops()
    {
        var bevel = DepthPalette.Find("DepthRaisedBevel")!;
        Assert.Equal(DepthBrushKind.Linear, bevel.Kind);
        Assert.Equal(new[] { (0x42FFFFFFu, 0.0), (0x10FFFFFFu, 0.5), (0x80000000u, 1.0) }, bevel.Stops.ToArray());

        var disc = DepthPalette.Find("DepthDropDisc")!;
        Assert.Equal(DepthBrushKind.Radial, disc.Kind);
        Assert.Equal((0.5, 0.5), (disc.RadiusX, disc.RadiusY));
        // The neutral drop: NeutralShadowInk at ShadowAlpha (0x9E), 0x5C at 60%, clear at the edge.
        Assert.Equal(Argb.WithAlpha(DepthRules.NeutralShadowInk, DepthRules.ShadowAlpha) & 0xFF000000u, disc.Stops[0].Color & 0xFF000000u);
        Assert.Equal(new[] { (0x9E06020Fu, 0.0), (0x5C06020Fu, 0.6), (0x0006020Fu, 1.0) }, disc.Stops.ToArray());

        var ledge = DepthPalette.Find("DepthLedgeUp")!;
        Assert.Equal(new Vec2(0, 1), ledge.Start);   // the ledge throws its shadow UP
        Assert.Equal(new Vec2(0, 0), ledge.End);

        Assert.Equal(0xFF120D20u, DepthPalette.Find("DepthWellFloorBrush")!.Color);
        Assert.Equal(0x12FFFFFFu, DepthPalette.Find("DepthWellFoot")!.Color);

        var bead = DepthPalette.Find("DepthTubeBead")!;
        Assert.Equal(new Vec2(0.44, 0.38), bead.End);   // the nudged GradientOrigin (lamp top-left)
        var socket = DepthPalette.Find("DepthCoinSocket")!;
        Assert.Equal(NavRailRules.SocketTopAlpha, Argb.A(socket.Stops[0].Color));
        var spec = DepthPalette.Find("DepthCoinSpecular")!;
        Assert.Equal(NavRailRules.SpecularAlpha, Argb.A(spec.Stops[0].Color));
        // The rail shadow's top alpha is the one NavRailRules.RailShadowStops tints.
        Assert.Equal((byte)0xBF, Argb.A(DepthPalette.Find("DepthRailShadow")!.Stops[0].Color));
    }
}
