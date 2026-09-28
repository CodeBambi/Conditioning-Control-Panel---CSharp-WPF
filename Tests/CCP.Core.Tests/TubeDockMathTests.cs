using ConditioningControlPanel.AvatarTubeLayout;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Golden values from WPF's attached formula (AvatarTubeWindow.Windowing.cs:855-856):
/// x = mainLeft - tubeW + 353*s, y = mainTop + (mainH - tubeH)/2 + 20*s. Stock insets at s = 1.</summary>
public class TubeDockMathTests
{
    private static readonly Box Uhd = new(0, 0, 3840, 2160);

    [Fact]
    public void LeftFits_UsesWpfFormula()
    {
        var p = TubeDockPlacement.Place(new Box(1000, 300, 1200, 800), 780, 1080, 239, 353, 20, Uhd);
        Assert.Equal(new DockPlan(1000 - 780 + 353, 300 + (800 - 1080) / 2 + 20, DockSide.Left), p);
    }

    [Fact]
    public void LeftOffScreen_FallsBackRight()
    {
        var p = TubeDockPlacement.Place(new Box(100, 300, 1200, 800), 780, 1080, 239, 353, 20, Uhd);
        Assert.Equal(new DockPlan(1300 - 239, 180, DockSide.Right), p);
    }

    [Fact]
    public void MaximisedMain_FloatsWithArtOnScreen()
    {
        var p = TubeDockPlacement.Place(new Box(0, 0, 1920, 1040), 780, 1080, 239, 353, 20, new Box(0, 0, 1920, 1080));
        Assert.Equal(new DockPlan(-239, 0, DockSide.Float), p);
    }

    [Theory]
    [InlineData(1920, 1040, 0.7385)]   // width-bound: 30% of 1920 / 780
    [InlineData(3840, 2100, 1.0)]      // ceiling
    [InlineData(800, 600, 0.4)]        // floor
    public void FitScale_MatchesWpf(double w, double h, double expected)
        => Assert.Equal(expected, TubeWindowMath.FitScale(w, h), 4);

    [Fact]
    public void ClampDetached_KeepsHalfOnScreen()
    {
        var work = new Box(0, 0, 1920, 1080);
        Assert.Equal((-390, 100), TubeWindowMath.ClampDetached(-1000, 100, 780, 1080, work));
        Assert.Equal((1530, 540), TubeWindowMath.ClampDetached(1800, 900, 780, 1080, work));
        Assert.Equal((500, -5000), TubeWindowMath.ClampDetached(500, -5000, 780, 1080, work));
    }
}
