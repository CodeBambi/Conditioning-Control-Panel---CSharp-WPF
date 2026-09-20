using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;
using static ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorGeometry;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The timeline renderer and rubber-band selection must agree on the three equal lane bands.
/// These are the Avalonia counterpart of the WPF geometry regression checks.
/// </summary>
public sealed class DeeperEditorGeometryTests
{
    [Fact]
    public void LaneBands_AreThreeEqualThirds_TopToBottom()
    {
        var (regionTop, regionHeight) = LaneBand(TimelineLane.Regions, 150);
        var (effectTop, effectHeight) = LaneBand(TimelineLane.Effects, 150);
        var (hapticTop, hapticHeight) = LaneBand(TimelineLane.Haptics, 150);

        Assert.Equal((0, 50), (regionTop, regionHeight));
        Assert.Equal((50, 50), (effectTop, effectHeight));
        Assert.Equal((100, 50), (hapticTop, hapticHeight));
    }

    [Fact]
    public void ZeroHeightCanvas_HasNoSelectableLane()
    {
        Assert.Equal((0, 0), LaneBand(TimelineLane.Effects, 0));
        Assert.False(BandHitsLane(TimelineLane.Effects, 0, 10, 0));
    }

    [Fact]
    public void RubberBand_OverMiddleThird_HitsEffectsOnly()
    {
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
    public void RubberBand_TouchingLaneBoundaryOnly_DoesNotHitNextLane()
    {
        Assert.False(BandHitsLane(TimelineLane.Effects, 10, 50, 150));
        Assert.True(BandHitsLane(TimelineLane.Regions, 10, 50, 150));
    }
}
