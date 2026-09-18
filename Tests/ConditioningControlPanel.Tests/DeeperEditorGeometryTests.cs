using ConditioningControlPanel.Views.Deeper;
using Xunit;
using static ConditioningControlPanel.Views.Deeper.DeeperEditorGeometry;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper editor timeline math that used to live inside the Window and drifted:
/// the rubber-band hit-test kept the pre-redesign lane layout (regions top half,
/// haptics below, effects in a 22 px strip) while rendering had moved to three
/// equal thirds, so a band drawn over the Effects lane selected haptics. The lane
/// bands and hit-test now share one helper; these pin the contract.
/// </summary>
public class DeeperEditorGeometryTests
{
    [Fact]
    public void LaneBands_AreThreeEqualThirds_TopToBottom()
    {
        var (rTop, rH) = LaneBand(TimelineLane.Regions, 150);
        var (eTop, eH) = LaneBand(TimelineLane.Effects, 150);
        var (hTop, hH) = LaneBand(TimelineLane.Haptics, 150);

        Assert.Equal(0, rTop); Assert.Equal(50, rH);
        Assert.Equal(50, eTop); Assert.Equal(50, eH);
        Assert.Equal(100, hTop); Assert.Equal(50, hH);
    }

    [Fact]
    public void LaneBand_ZeroHeightCanvas_IsEmpty()
    {
        Assert.Equal((0, 0), LaneBand(TimelineLane.Effects, 0));
        Assert.False(BandHitsLane(TimelineLane.Effects, 0, 10, 0));
    }

    [Fact]
    public void RubberBand_OverMiddleThird_HitsEffectsOnly()
    {
        // The regression: a band drawn inside the middle third must pick the
        // Effects lane and nothing else.
        Assert.True(BandHitsLane(TimelineLane.Effects, 60, 90, 150));
        Assert.False(BandHitsLane(TimelineLane.Regions, 60, 90, 150));
        Assert.False(BandHitsLane(TimelineLane.Haptics, 60, 90, 150));
    }

    [Fact]
    public void RubberBand_OverBottomThird_HitsHapticsOnly()
    {
        Assert.True(BandHitsLane(TimelineLane.Haptics, 110, 140, 150));
        Assert.False(BandHitsLane(TimelineLane.Effects, 110, 140, 150));
        Assert.False(BandHitsLane(TimelineLane.Regions, 110, 140, 150));
    }

    [Fact]
    public void RubberBand_SpanningAllLanes_HitsAll()
    {
        Assert.True(BandHitsLane(TimelineLane.Regions, 5, 145, 150));
        Assert.True(BandHitsLane(TimelineLane.Effects, 5, 145, 150));
        Assert.True(BandHitsLane(TimelineLane.Haptics, 5, 145, 150));
    }

    [Fact]
    public void RubberBand_TouchingLaneBoundaryOnly_DoesNotHit()
    {
        // [0, 50) is Regions; a band ending exactly at y=50 never reaches Effects.
        Assert.False(BandHitsLane(TimelineLane.Effects, 10, 50, 150));
        Assert.True(BandHitsLane(TimelineLane.Regions, 10, 50, 150));
    }

    private static readonly string[] Palette = { "#A", "#B", "#C" };

    [Fact]
    public void LeastUsedColor_EmptyProject_PicksFirst()
    {
        Assert.Equal("#A", LeastUsedPaletteColor(Palette, System.Array.Empty<string?>()));
    }

    [Fact]
    public void LeastUsedColor_AfterDelete_DoesNotRepeatNeighbour()
    {
        // Old behaviour: Regions.Count % palette = index 2 => "#C", repeating the
        // survivor's colour. Least-used picks "#B".
        var used = new[] { "#A", "#C" };
        Assert.Equal("#B", LeastUsedPaletteColor(Palette, used));
    }

    [Fact]
    public void LeastUsedColor_TieBreaksOnPaletteOrder_AndIgnoresUnknown()
    {
        var used = new[] { "#B", "#a", "#C", "#zz", null };
        Assert.Equal("#A", LeastUsedPaletteColor(Palette, used));
    }
}
