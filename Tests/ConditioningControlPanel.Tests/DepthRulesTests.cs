using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The depth pass (nav polish wave 10, 2026-10-06): one lamp, three heights, on is pressed in.
/// These pin the law so a lane cannot drift a number by hand, and prove the theme file holds
/// every brush the lanes compose from.
/// </summary>
public class DepthRulesTests
{
    [Fact]
    public void OnIsPressedInAndHoverLiftsAndPressWins()
    {
        // rest
        Assert.Equal(0, DepthRules.TravelFor(enabled: true, pressed: false, active: false, hovered: false));
        // a lit thing sits in its socket
        Assert.Equal(DepthRules.ActiveSinkPx, DepthRules.TravelFor(true, false, true, false));
        // hover lifts an idle thing (negative = up)
        Assert.Equal(-DepthRules.HoverLiftPx, DepthRules.TravelFor(true, false, false, true));
        // hovering a lit thing does not lift it out of its socket
        Assert.Equal(DepthRules.ActiveSinkPx, DepthRules.TravelFor(true, false, true, true));
        // press wins over everything
        Assert.Equal(DepthRules.PressTravelPx, DepthRules.TravelFor(true, true, true, true));
        // disabled never moves
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
    public void HeightsAreSubtleExceptStart()
    {
        Assert.InRange(DepthRules.RaisedPx, 2, 4);
        Assert.InRange(DepthRules.HoverLiftPx, 1, 3);
        Assert.InRange(DepthRules.PressTravelPx, 1, 3);
        Assert.Equal(1.0, DepthRules.ActiveSinkPx);
        Assert.True(DepthRules.StartPx > DepthRules.RaisedPx, "START is the one chunky plank");
        Assert.True(DepthRules.FloatPx > DepthRules.RaisedPx, "a card floats higher than a chip stands");
        Assert.True(DepthRules.WellTopAlpha > DepthRules.WellLeftAlpha, "the lamp is above, not beside");
        Assert.True(DepthRules.HighlightAlpha < DepthRules.ShadeAlpha, "shade is heavier than light on a dark sheet");
    }

    [Theory]
    [InlineData(MotionLevel.Full, 90)]
    [InlineData(MotionLevel.Reduced, 45)]
    [InlineData(MotionLevel.Off, 0)]
    public void MotionScalesTheTimings(MotionLevel level, int expected)
    {
        Assert.Equal(expected, DepthRules.Ms(DepthRules.PressMs, level));
    }

    [Fact]
    public void TiltOnlyAboveThePerformanceTierAndNeverAtMotionOff()
    {
        Assert.True(DepthRules.TiltAllowed(MotionLevel.Full, PerformanceTier.Balanced));
        Assert.True(DepthRules.TiltAllowed(MotionLevel.Reduced, PerformanceTier.Quality));
        Assert.False(DepthRules.TiltAllowed(MotionLevel.Off, PerformanceTier.Quality));
        Assert.False(DepthRules.TiltAllowed(MotionLevel.Full, PerformanceTier.Performance));
        Assert.Equal(1.2, DepthRules.TiltDegrees);
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
        Assert.Equal((byte)158, sh.A); // 0.62
        // dark: every channel well under the hue's, and under 0x60 (28% of a 0xFF channel over ink)
        Assert.True(sh.R < hue.R && sh.G < hue.G && sh.B < hue.B);
        Assert.True(sh.R < 0x60 && sh.G < 0x60 && sh.B < 0x60);
        // but tinted: the dominant channel of the hue is the dominant channel of the shadow
        int Dominant(Color c) => c.R >= c.G && c.R >= c.B ? 0 : c.G >= c.B ? 1 : 2;
        Assert.Equal(Dominant(hue), Dominant(sh));
        // a card's shadow is the same ink, softer
        Assert.Equal((byte)102, DepthRules.ShadowColor(hue, DepthRules.FloatAlpha).A);
    }

    [Fact]
    public void TheThemeFileHoldsEveryBrushTheLanesComposeFrom()
    {
        var path = FindRepoFile(Path.Combine("ConditioningControlPanel", "Resources", "Theme", "Depth.xaml"));
        var doc = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = doc.Descendants().Select(e => (string?)e.Attribute(x + "Key")).Where(k => k != null).ToHashSet();
        foreach (var key in new[]
                 {
                     "DepthRaisedBevel", "DepthRaisedSheen", "DepthPlankSheen", "DepthPlankBevel",
                     "DepthPressedBevel", "DepthPressedShade", "DepthDropBand", "DepthDropDisc",
                     "DepthWellTop", "DepthWellLeft", "DepthWellFoot", "DepthWellFloorBrush",
                     "DepthFloatRim", "DepthFloatBand", "DepthLedgeUp", "DepthRailShadow",
                     "DepthTubeGloss", "DepthTubeBead", "DepthCoinDish", "DepthCoinRim",
                 })
            Assert.Contains(key, keys);
        // No Effect anywhere in the depth system: shadows are paint.
        Assert.DoesNotContain("Effect", File.ReadAllText(path).Replace("<!--", "").Split("-->").Last());
        Assert.DoesNotContain("DropShadowEffect", File.ReadAllText(path));

        // and App.xaml merges it
        var app = File.ReadAllText(FindRepoFile(Path.Combine("ConditioningControlPanel", "App.xaml")));
        Assert.Contains("Resources/Theme/Depth.xaml", app);
    }

    private static string FindRepoFile(string rel)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, rel);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(rel);
    }
}
