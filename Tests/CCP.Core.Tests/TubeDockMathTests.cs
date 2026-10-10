using ConditioningControlPanel.AvatarTubeLayout;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Golden values from WPF's attached formula (AvatarTubeWindow.Windowing.cs:855-856):
/// x = mainLeft - tubeW + 353*s, y = mainTop + (mainH - tubeH)/2 + 20*s. Stock insets at s = 1.</summary>
public class TubeDockMathTests
{
    private static readonly Box Uhd = new(0, 0, 3840, 2160);

    /// <summary>A mod tube whose art paints past the built-in glass (Infection Control: right edge
    /// at 587/1024 vs the built-in 1129/2048) moves the seam out by that overhang; the built-in
    /// art and narrower art keep the WPF seam.</summary>
    [Fact]
    public void ModTubeArtPastTheBuiltInGlassMovesTheSeamOut()
    {
        double builtIn = 1129 / 2048.0;
        Assert.Equal(16.2, TubeWindowMath.ArtOverhangUnits(587 / 1024.0, 1, builtIn, 1), 1);
        Assert.Equal(0, TubeWindowMath.ArtOverhangUnits(builtIn, 1, builtIn, 1));
        Assert.Equal(0, TubeWindowMath.ArtOverhangUnits(0.5, 1, builtIn, 1));
        Assert.Equal(120, TubeWindowMath.ArtOverhangUnits(1, 1, 0.2, 1));
    }

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
    [InlineData(1113, 1979, 0.4281)]   // portrait DP-1 at 1.79x (1994x3545 px): width-bound
    [InlineData(2560, 900, 0.75)]      // height-bound: 85% of 900 / 1020 (WPF DesignHeight)
    [InlineData(1080, 1920, 0.4154)]   // portrait 1080p
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

    [Fact]
    public void RestoreDetached_PicksTheScreenItsDipsLandOn()
    {
        // Screen A: 1920x1080 at 1.0 at the origin. Screen B: 3456x1944 at 1.79 to its right.
        var a = (new Box(0, 0, 1920, 1080), 1.0, new Box(0, 0, 1920, 1040));
        var b = (new Box(1920, 0, 3456, 1944), 1.7916666666666667, new Box(1920, 0, 3456, 1900));
        var screens = new[] { a, b };
        // 1500 DIP: at 1.0 it is 1500 px (on A); A wins, no 1.79 inflation.
        Assert.Equal((1500, 100), TubeWindowMath.RestoreDetached(1500, 100, 600, 800, screens, 1.79));
        // 2000 DIP: at 1.0 = 2000 px (off A); at 1.79 = 3583 px, inside B.
        Assert.Equal((3583, 179), TubeWindowMath.RestoreDetached(2000, 100, 600, 800, screens, 1.0));
        // Nowhere: fallback scaling, clamped to the first screen.
        Assert.Equal((1620, 100), TubeWindowMath.RestoreDetached(9000, 100, 600, 800, screens, 1.0));
    }
}
