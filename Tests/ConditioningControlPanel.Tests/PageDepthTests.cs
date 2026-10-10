using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 10 (depth), lane PAGES: the strip's tray is sunken, its pills are raised with
/// a hue-tinted drop band, the lit pill is pressed in; the leaderboard list is a well of floating
/// rows with the player's own row pressed in; the lobby rows carry the card / plank paint.
/// </summary>
public class PageDepthTests
{
    private static SectionTabStrip Laid(string section, string tab, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = width };
        strip.Show(section, tab);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static Color ColorAt(Brush b, int stop) => ((GradientBrush)b).GradientStops[stop].Color;

    [Fact]
    public void TheTrayIsASunkenWell()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Play, "play");
            var floor = Assert.IsType<SolidColorBrush>(strip.TrayFloorBrush);
            Assert.Equal(Color.FromRgb(0x12, 0x0D, 0x20), floor.Color);
            // The top band is the theme's DepthWellTop mapped in pixels: WellPx long, strongest at the top.
            var top = Assert.IsType<LinearGradientBrush>(strip.TrayWellTopBrush);
            Assert.Equal(BrushMappingMode.Absolute, top.MappingMode);
            Assert.Equal(DepthRules.WellPx, top.EndPoint.Y);
            Assert.True(ColorAt(top, 0).A > ColorAt(top, 1).A);
            var left = Assert.IsType<LinearGradientBrush>(strip.TrayWellLeftBrush);
            Assert.Equal(BrushMappingMode.Absolute, left.MappingMode);
            Assert.Equal(6, left.EndPoint.X);
            Assert.NotNull(strip.TrayWellFootBrush);
            // The wave 7 fill still paints the tray over the floor.
            Assert.Equal(NavStripRules.TrackFill(NavStripRules.Accent(NavSections.Play)), ((SolidColorBrush)strip.TrackFill).Color);
        });
    }

    [Fact]
    public void RaisedPillsCastAHueTintedDropAndTheLitPillSitsIn()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                var shadow = DepthRules.ShadowColor(NavStripRules.Accent(s.Key), DepthRules.ShadowAlpha);
                foreach (var key in strip.PillKeys)
                {
                    bool lit = key == strip.ActivePillKey;
                    Assert.Equal(lit ? DepthRules.ActiveSinkPx : 0, strip.PillFaceTravel(key));
                    Assert.Equal(lit ? 0 : DepthRules.RaisedPx, strip.PillDropLength(key));
                    if (!lit) Assert.Equal(shadow, ColorAt(strip.PillDropBrush(key)!, 0));
                    // The drop band never grows the pill: still 38 px.
                    Assert.Equal(NavStripRules.PillHeight, strip.PillFor(key)!.ActualHeight, 1);
                }
                // The lit pill: the fill sits ActiveSinkPx down and wears the pressed shade + bevel.
                Assert.Equal(DepthRules.ActiveSinkPx, strip.ActiveFillSink);
                Assert.NotNull(strip.ActivePressLayer);
                Assert.True(ColorAt(strip.ActivePressLayer!.BorderBrush, 0).A > 0x80, "pressed bevel: dark top");
            }
        });
    }

    [Fact]
    public void HoverLengthensTheDropAndPressTravelsDown()
    {
        Assert.Equal(DepthRules.HoverLiftPx, NavStripFxRules.HoverLiftPx);
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Play, "play");
            var idle = strip.PillKeys.First(k => k != strip.ActivePillKey);
            strip.DepthHoverForTests(idle, true);
            Assert.Equal(DepthRules.RaisedPx + DepthRules.HoverLiftPx, strip.PillDropLength(idle));
            strip.DepthPressForTests(idle, true);
            Assert.Equal(DepthRules.PressTravelPx, strip.PillFaceTravel(idle));
            Assert.Equal(0, strip.PillDropLength(idle));
            strip.DepthPressForTests(idle, false);
            strip.DepthHoverForTests(idle, false);
            Assert.Equal(0, strip.PillFaceTravel(idle));
            Assert.Equal(DepthRules.RaisedPx, strip.PillDropLength(idle));
        });
    }

    [Fact]
    public void TheBarStillFits1469()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab, 1469);
                Assert.True(strip.TrayHostForTests.ActualWidth <= 1469, $"{s.Key}: tray {strip.TrayHostForTests.ActualWidth}");
                Assert.Equal(49, strip.TrackHeightForTests, 1);
            }
        });
    }

    [Fact]
    public void TheLeaderboardIsAWellOfFloatingRowsAndYourRowSitsIn()
    {
        var xaml = File.ReadAllText(Path.Combine(Root(), "Views", "Tabs", "LeaderboardTabView.xaml"));
        Assert.Contains("x:Name=\"LbWellFloor\"", xaml);
        Assert.Contains("{DynamicResource DepthWellFloorBrush}", xaml);
        Assert.Contains("{DynamicResource DepthWellTop}", xaml);
        Assert.Contains("{DynamicResource DepthFloatRim}", xaml);
        Assert.Contains("{DynamicResource DepthFloatBand}", xaml);
        Assert.Contains("{DynamicResource DepthPressedBevel}", xaml);
        // The own row sinks ActiveSinkPx (1 px) and the rim is paid out of the padding (pitch kept).
        Assert.Equal(1.0, DepthRules.ActiveSinkPx);
        Assert.Contains("<TranslateTransform Y=\"1\"/>", xaml);
        Assert.Contains("Padding=\"9,2\" Margin=\"4,0\" BorderThickness=\"1\"", xaml);
        Assert.DoesNotContain("Padding=\"10,3\" Margin=\"4,0\"", xaml);

        WpfRenderHarness.OnStaThread(() =>
        {
            var view = new LeaderboardTabView();
            view.Measure(new Size(1240, 800));
            view.Arrange(new Rect(0, 0, 1240, 800));
            Assert.NotNull(view.LbWellFloor);
            Assert.False(view.LbWellBands.IsHitTestVisible);
        });
    }

    [Fact]
    public void LobbyRowsCarryTheCardAndPlankPaint()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var snap = LobbyRenderTests.Sample();
            var view = LobbyRowView.From(snap.Open, LobbyGates.From(true, true, true), k => k).First();
            Assert.Equal(DepthRules.ShadowColor(NavStripRules.Accent("social"), DepthRules.FloatAlpha), ColorAt(view.CardShadow, 0));
            Assert.Equal(DepthRules.ShadowColor(NavStripRules.Accent("social"), DepthRules.ShadowAlpha), ColorAt(view.ButtonDrop, 0));
            Assert.Equal(DepthRules.FloatPx, view.CardShadowPx);
            Assert.Equal(DepthRules.RaisedPx, view.ButtonDropPx);
            Assert.NotNull(view.CardRim);
            Assert.NotNull(view.ButtonBevel);
        });
    }
}
