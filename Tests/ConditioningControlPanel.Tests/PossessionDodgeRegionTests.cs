using System.Windows;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// DodgeEffect's invariant is "friction, never lockout": the button always stays visible and
/// clickable where it lands. Clamping it to the WINDOW was not enough for that - a nav door inside
/// the rail's 56 px <c>ClipToBounds</c> strip dodges out of the clip (invisible AND un-hittable for
/// the whole 20 s hold) and the title-bar X dodges down under opaque page content that paints over
/// it. <see cref="DodgeRegion"/> is the arithmetic that answers where a step may land; this pins it.
/// See Services/Possession/POSSESSION.md.
/// </summary>
public class PossessionDodgeRegionTests
{
    // The design canvas the main window scales, in ghost-layer units.
    private const double LayerW = 1563;
    private const double LayerH = 901;

    // ---- lanes ---------------------------------------------------------------------------------

    [Fact]
    public void The_shut_nav_rail_is_a_strip()
        => Assert.True(DodgeRegion.IsStrip(new Rect(0, 36, 56, 865), LayerW, LayerH));

    /// <summary>The flyout is the dangerous state, not the safe one: a door that dodges into the open
    /// 236 px rail is clipped away the moment the pointer leaves and the rail snaps back to 56.</summary>
    [Fact]
    public void The_open_nav_rail_is_still_a_strip()
        => Assert.True(DodgeRegion.IsStrip(new Rect(0, 36, 236, 865), LayerW, LayerH));

    [Fact]
    public void A_short_band_is_a_strip_too()
        => Assert.True(DodgeRegion.IsStrip(new Rect(0, 0, LayerW, 36), LayerW, LayerH));

    [Fact]
    public void A_page_sized_clip_is_not_a_strip()
        => Assert.False(DodgeRegion.IsStrip(new Rect(96, 36, 1400, 820), LayerW, LayerH));

    [Fact]
    public void An_empty_clip_is_not_a_strip()
        => Assert.False(DodgeRegion.IsStrip(Rect.Empty, LayerW, LayerH));

    // ---- how far it may go ----------------------------------------------------------------------

    [Fact]
    public void The_offset_range_is_the_slack_on_every_side()
    {
        var region = new Rect(12, 12, LayerW - 24, LayerH - 24);
        var home = new Rect(100, 200, 120, 40);

        Assert.True(DodgeRegion.TryOffsetRange(home, region,
                                               out double minX, out double maxX,
                                               out double minY, out double maxY));

        Assert.Equal(12 - 100, minX, 3);
        Assert.Equal(region.Right - home.Right, maxX, 3);
        Assert.Equal(12 - 200, minY, 3);
        Assert.Equal(region.Bottom - home.Bottom, maxY, 3);
    }

    /// <summary>A chrome button's band is exactly its own row, so the only legal step is sideways.</summary>
    [Fact]
    public void A_title_bar_band_leaves_no_vertical_room()
    {
        var home = new Rect(1400, 0, 46, 36);
        var band = new Rect(12, home.Y, LayerW - 24, home.Height);

        Assert.True(DodgeRegion.TryOffsetRange(home, band,
                                               out double minX, out double maxX,
                                               out double minY, out double maxY));

        Assert.True(maxX - minX > 100);      // plenty of room to run along the bar
        Assert.Equal(0, minY, 3);
        Assert.Equal(0, maxY, 3);
    }

    [Fact]
    public void A_region_that_cannot_hold_the_control_refuses_the_dodge()
    {
        var home = new Rect(0, 6, 44, 44);
        var region = new Rect(0, 12, 30, 800);   // narrower than the control

        Assert.False(DodgeRegion.TryOffsetRange(home, region, out _, out _, out _, out _));
    }

    [Fact]
    public void An_empty_region_refuses_the_dodge()
        => Assert.False(DodgeRegion.TryOffsetRange(new Rect(0, 0, 10, 10), Rect.Empty,
                                                   out _, out _, out _, out _));

    /// <summary>A control already outside its region (the rail shut under it) is pulled back in
    /// rather than pushed further out: the range never offers an offset that leaves the region.</summary>
    [Fact]
    public void A_stranded_control_is_offered_only_offsets_that_come_home()
    {
        var home = new Rect(120, 100, 44, 44);            // sitting where the flyout used to be
        var region = new Rect(0, 36, 56, 800);            // the rail, shut under it

        Assert.True(DodgeRegion.TryOffsetRange(home, region,
                                               out double minX, out double maxX,
                                               out double minY, out double maxY));

        // Every offset the clamp can produce puts it back inside the lane, both ends of the range.
        foreach (var (dx, dy) in new[] { (minX, minY), (maxX, maxY), (minX, maxY), (maxX, minY) })
        {
            Assert.True(DodgeRegion.Contains(region,
                new Rect(home.X + dx, home.Y + dy, home.Width, home.Height)));
        }
        Assert.True(maxX < 0);   // it can only come back leftward
    }

    // ---- the acceptance test --------------------------------------------------------------------

    [Fact]
    public void Contains_is_whole_rectangles_only()
    {
        var region = new Rect(0, 36, 56, 800);

        Assert.True(DodgeRegion.Contains(region, new Rect(6, 100, 44, 44)));
        Assert.False(DodgeRegion.Contains(region, new Rect(30, 100, 44, 44)));   // hangs out of the lane
        Assert.False(DodgeRegion.Contains(region, new Rect(6, 800, 44, 44)));    // hangs out of the bottom
        Assert.False(DodgeRegion.Contains(Rect.Empty, new Rect(0, 0, 1, 1)));
    }

    /// <summary>Flush against the edge still counts: a clamp lands exactly on the boundary.</summary>
    [Fact]
    public void Contains_accepts_a_flush_fit()
    {
        var region = new Rect(12, 12, 100, 50);
        Assert.True(DodgeRegion.Contains(region, new Rect(12, 12, 100, 50)));
    }
}
