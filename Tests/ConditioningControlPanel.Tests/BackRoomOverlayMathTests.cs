using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.BackRoom.Overlays;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM Hypno v3 overlay maths (CONTRACT 10.13.B) against the mockup's own numbers: the page
/// rect onto the right screen, the wash envelope, the gif growing out of a rect, the spiral fades, and
/// the tunnel's easing, auto-release and shape.
/// </summary>
public class BackRoomOverlayMathTests
{
    // Two 1080p screens side by side, the right one primary; a 1280x720 room at 150% on the left one.
    private static readonly FxScreenInfo[] Screens =
    {
        new(new PxRect(-1920, 0, 1920, 1080), false),
        new(new PxRect(0, 0, 1920, 1080), true),
    };
    private static readonly RoomViewport Room = new(false, new PxRect(-1800, 100, 1500, 900), 1.0, 1.5);

    // ---- rect mapping ----------------------------------------------------------------------------

    [Fact]
    public void Rect_MapsThroughZoomTimesRasterization_OntoTheRoomsScreen()
    {
        var t = BackRoomOverlayMath.MapFrom(new FxCssRect(612, 188, 60, 44), Room, Screens);
        Assert.Equal((0, false), (t.ScreenIndex, t.Centred));
        Assert.Equal(new PxRect(-1800 + 612 * 1.5, 100 + 188 * 1.5, 90, 66), t.RectPx);
        Assert.Equal(Screens[0].BoundsPx, t.ScreenPx);

        // Page zoom counts too.
        var zoomed = BackRoomOverlayMath.MapFrom(new FxCssRect(10, 10, 20, 20), Room with { ZoomFactor = 2 }, Screens);
        Assert.Equal(new PxRect(-1800 + 30, 130, 60, 60), zoomed.RectPx);
    }

    [Fact]
    public void Rect_MissingOrBad_GrowsFromTheRoomsCentre()
    {
        foreach (var bad in new FxCssRect?[] { null, new FxCssRect(0, 0, 4000, 44), new FxCssRect(double.NaN, 0, 60, 44), new FxCssRect(0, 0, 60, 2),
                     new FxCssRect(-5000, 10, 60, 44), new FxCssRect(10, 4000, 60, 44) })
        {
            var t = BackRoomOverlayMath.MapFrom(bad, Room, Screens);
            Assert.True(t.Centred);
            Assert.Equal(0, t.ScreenIndex);
            Assert.Equal((Room.ControlPx.Cx, Room.ControlPx.Cy), (t.RectPx.Cx, t.RectPx.Cy));
            Assert.Equal((90.0, 66.0), (t.RectPx.W, t.RectPx.H));
        }
    }

    [Fact]
    public void MinimisedOrOffScreenRoom_GrowsFromThePrimaryScreensCentre()
    {
        foreach (var vp in new RoomViewport?[] { Room with { Minimised = true }, Room with { ControlPx = new PxRect(-32000, -32000, 160, 28) }, null })
        {
            var t = BackRoomOverlayMath.MapFrom(new FxCssRect(1, 1, 60, 44), vp, Screens);
            Assert.Equal((1, true), (t.ScreenIndex, t.Centred));
            Assert.Equal((960.0, 540.0), (t.RectPx.Cx, t.RectPx.Cy));
        }
    }

    [Fact]
    public void RoomStraddlingScreens_UsesTheScreenHoldingItsCentre()
    {
        var straddle = Room with { ControlPx = new PxRect(-400, 100, 1500, 900) };   // centre at x = 350
        Assert.Equal(1, BackRoomOverlayMath.MapFrom(null, straddle, Screens).ScreenIndex);
        Assert.Equal(new PxRect(-1200, 50, 100, 25),
            BackRoomOverlayMath.ToLocalDip(new PxRect(-3720, 75, 150, 37.5), Screens[0].BoundsPx, 1.5));
    }

    private static void Near(PxRect want, PxRect got)
    {
        Assert.Equal(want.X, got.X, 4);
        Assert.Equal(want.Y, got.Y, 4);
        Assert.Equal(want.W, got.W, 4);
        Assert.Equal(want.H, got.H, 4);
    }

    // ---- wash ------------------------------------------------------------------------------------

    [Fact]
    public void Wash_Up80_Decay45_GoneAt900()
    {
        Assert.Equal(0, BackRoomOverlayMath.WashEnvelope(0));
        Assert.Equal(0.5, BackRoomOverlayMath.WashEnvelope(40), 6);
        Assert.Equal(1, BackRoomOverlayMath.WashEnvelope(80), 6);
        Assert.Equal(Math.Exp(-4.5 * 0.5), BackRoomOverlayMath.WashEnvelope(580), 6);
        Assert.Equal(0, BackRoomOverlayMath.WashEnvelope(900));
        // The picture: min(1, env x 1.3) x 0.85 x strength.
        Assert.Equal(0.85 * 0.7, BackRoomOverlayMath.WashPictureAlpha(1, 0.42 * 0.7), 6);
        Assert.Equal(0.85 * 0.5 * 1.3, BackRoomOverlayMath.WashPictureAlpha(0.5, 0.42), 6);
        Near(new PxRect(960 - 302.4, 540 - 226.8, 604.8, 453.6), BackRoomOverlayMath.WashPictureBox(1920, 1080));
    }

    // ---- gif-from --------------------------------------------------------------------------------

    [Fact]
    public void GifFrom_GrowsFromTheRectToCover_ThenFades()
    {
        var from = new PxRect(600, 180, 60, 44);
        var start = BackRoomOverlayMath.GifFrom(0, 3400, from, 1920, 1080, 16.0 / 9, 1, 1, false);
        Assert.Equal(from, start.Image);
        Assert.Equal(1.0, start.ImageAlpha);
        Assert.Equal(0.55 * 0.55, start.DimAlpha, 6);

        var grown = BackRoomOverlayMath.GifFrom(700, 3400, from, 1920, 1080, 16.0 / 9, 1, 1, false);
        Near(new PxRect(0, 0, 1920, 1080), grown.Image);
        Assert.Equal(0.55, grown.DimAlpha, 6);

        var tall = BackRoomOverlayMath.GifFrom(1000, 3400, from, 1920, 1080, 0.5, 1, 1, false);
        Assert.Equal((1920.0, 3840.0, -1380.0), (tall.Image.W, tall.Image.H, tall.Image.Y));   // cover, centred

        var fading = BackRoomOverlayMath.GifFrom(3400 - 450, 3400, from, 1920, 1080, 16.0 / 9, 1, 0.8, false);
        Assert.Equal(0.5, fading.ImageAlpha, 6);
        Assert.Equal(0.55 * 0.5 * 0.8, fading.DimAlpha, 6);
        Assert.Equal(0, BackRoomOverlayMath.GifFrom(3400, 3400, from, 1920, 1080, 1, 1, 1, false).ImageAlpha);
    }

    [Fact]
    public void GifFrom_BelowFullScale_SkipsTheDim_AndSitsHalfSize()
    {
        var f = BackRoomOverlayMath.GifFrom(2000, 4600, new PxRect(0, 0, 60, 44), 1920, 1080, 16.0 / 9, 0.46, 1, false);
        Assert.Equal(0, f.DimAlpha);
        Assert.Equal(1920 * 0.46, f.Image.W, 6);
        Assert.Equal(960, f.Image.Cx, 6);
    }

    [Fact]
    public void GifFrom_Still_HasNoGrowth_And300msFades()
    {
        var from = new PxRect(600, 180, 60, 44);
        var early = BackRoomOverlayMath.GifFrom(150, 2040, from, 1920, 1080, 16.0 / 9, 1, 1, true);
        Near(new PxRect(0, 0, 1920, 1080), early.Image);
        Assert.Equal(0.5, early.ImageAlpha, 6);
        Assert.Equal(1, BackRoomOverlayMath.GifFrom(1000, 2040, from, 1920, 1080, 16.0 / 9, 1, 1, true).ImageAlpha);
        Assert.Equal(0.5, BackRoomOverlayMath.GifFrom(1890, 2040, from, 1920, 1080, 16.0 / 9, 1, 1, true).ImageAlpha, 6);
    }

    // ---- spiral ----------------------------------------------------------------------------------

    [Fact]
    public void Spiral_FadesIn800_HoldsThenOut1200_OrOutFromARelease()
    {
        Assert.Equal(0.5, BackRoomOverlayMath.SpiralEnvelope(400, 4200, null), 6);
        Assert.Equal(1, BackRoomOverlayMath.SpiralEnvelope(4200, 4200, null));
        Assert.Equal(0.5, BackRoomOverlayMath.SpiralEnvelope(4800, 4200, null), 6);
        Assert.False(BackRoomOverlayMath.SpiralDone(5399, 4200, null));
        Assert.True(BackRoomOverlayMath.SpiralDone(5400, 4200, null));

        // A hold released at 2 s, while still at full: out from there, whatever the 20 s cap says.
        Assert.Equal(0.5, BackRoomOverlayMath.SpiralEnvelope(2600, 20000, 2000), 6);
        Assert.True(BackRoomOverlayMath.SpiralDone(3200, 20000, 2000));
        // Released during the fade in: both curves multiply, as the mockup draws it.
        Assert.Equal(0.625 * 0.75, BackRoomOverlayMath.SpiralEnvelope(500, 20000, 200), 6);
    }

    // ---- tunnel ----------------------------------------------------------------------------------

    [Fact]
    public void Tunnel_ClosesAt14_OpensAt22()
    {
        var t = new TunnelModel();
        t.Set(0.8, 0);
        Assert.Equal(0.8 * 0.14, t.Step(100, 100, false), 6);        // (want - level) x dt x 1.4
        t.Set(0, 100);
        double before = t.Level;
        Assert.Equal(before - before * 0.22, t.Step(200, 100, false), 6);
    }

    [Fact]
    public void Tunnel_LetsGoBySelf_After1500msWithoutARefresh()
    {
        var t = new TunnelModel();
        long now = 0;
        t.Set(0.62, now);
        for (; now < 1500; now += 50) t.Step(now, 50, false);
        Assert.Equal(0.62, t.Want);
        Assert.True(t.Level > 0.5);
        now += 50;
        t.Step(now, 50, false);
        Assert.Equal(0, t.Want);
        for (int i = 0; i < 80; i++) { now += 50; t.Step(now, 50, false); }
        Assert.True(t.Idle);
    }

    [Fact]
    public void Tunnel_RefreshKeepsIt_StillSteps_CancelIsInstant()
    {
        var t = new TunnelModel();
        for (long now = 0; now < 6000; now += 100) { if (now % 1000 == 0) t.Set(0.5, now); t.Step(now, 100, false); }
        Assert.Equal(0.5, t.Want);

        var still = new TunnelModel();
        still.Set(0.7, 0);
        Assert.Equal(0.7, still.Step(16, 16, true));
        still.Cancel();
        Assert.True(still.Idle);
    }

    [Fact]
    public void TunnelShape_NeverClosesTheCentre()
    {
        var open = BackRoomOverlayMath.Tunnel(1920, 1080, 0);
        double r = Math.Sqrt(1920.0 * 1920 + 1080 * 1080) / 2;
        Assert.Equal((r, r * 1.12, 0.0), (open.Inner, open.Outer, open.Alpha));

        var shut = BackRoomOverlayMath.Tunnel(1920, 1080, 1);
        Assert.Equal(r * 0.28, shut.Inner, 6);
        Assert.Equal(r * 0.62, shut.Outer, 6);
        Assert.Equal(0.94, shut.Alpha, 6);
        Assert.True(shut.Inner > 0 && shut.Inner < shut.Outer);
        Assert.Equal(0.94 * 0.65, BackRoomOverlayMath.Tunnel(1920, 1080, 0.5).Alpha, 6);
    }
}
