using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Scrawl's scene: the walls fill up to the cap, stamps fade away on their own, and the
/// stop path (panic, the emergency exit and every other BouncingTextService.Stop) leaves nothing
/// behind to draw or shake.
/// </summary>
public class ScrawlSceneTests
{
    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);

    private static ScrawlScene NewScene() => new(new FontFamily("Segoe UI"), 72, 1.0);

    private static void HitTop(ScrawlScene s, double x) =>
        s.WallHit("SINK", ScrawlWall.Top, x, 0, x + 200, 80, Screen, new Point(960, 540));

    [Fact]
    public void A_wall_hit_leaves_a_stamp_that_fades_out_after_fourteen_seconds()
    {
        var s = NewScene();
        HitTop(s, 800);
        var rects = new List<Rect>();
        s.AppendRects(rects);
        Assert.Single(rects);
        Assert.True(s.IsLive);

        for (int i = 0; i < 15 * 10; i++) s.Tick(0.1, MotionLevel.Full);
        rects.Clear();
        s.AppendRects(rects);
        Assert.Empty(rects);
        Assert.False(s.IsLive);
    }

    [Fact]
    public void The_walls_hold_at_most_forty_four_stamps()
    {
        var s = NewScene();
        for (int i = 0; i < 60; i++) HitTop(s, 300 + i * 20);
        var rects = new List<Rect>();
        s.AppendRects(rects);
        Assert.Equal(ScrawlRules.StampCap, rects.Count);
    }

    [Fact]
    public void Clear_drops_every_stamp_spark_and_shake_at_once()
    {
        var s = NewScene();
        HitTop(s, 800);
        s.SlamLanded("DEEP", 960, 540, 1920);
        s.Tick(0.016, MotionLevel.Full);
        Assert.True(s.IsLive);

        s.Clear();
        var rects = new List<Rect>();
        s.AppendRects(rects);
        Assert.Empty(rects);
        Assert.False(s.IsLive);
        Assert.Equal(0, s.ShakeX);
        Assert.Equal(0, s.ShakeY);
    }

    [Fact]
    public void Motion_off_never_shakes()
    {
        var s = NewScene();
        s.SlamLanded("DEEP", 960, 540, 1920);
        s.Tick(0.016, MotionLevel.Off);
        Assert.Equal(0, s.ShakeX);
        Assert.Equal(0, s.ShakeY);
    }
}
