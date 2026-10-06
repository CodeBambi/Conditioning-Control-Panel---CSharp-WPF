using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 1 (2026-10-06, lane STRIP): pills never move when the page changes, every
/// header row has one height, pills are named UIA buttons, the Play pills follow the page.
/// </summary>
public class NavStripPolishTests
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

    private static double[] PillXs(SectionTabStrip strip) =>
        strip.PillKeys.Select(k => strip.PillFor(k)!.TranslatePoint(new Point(0, 0), strip).X).ToArray();

    [Fact]
    public void PillsStayPutWhenThePageChanges()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                var before = PillXs(strip);
                foreach (var t in s.Tabs)
                {
                    strip.Show(s.Key, t.Key);
                    strip.UpdateLayout();
                    Assert.Equal(before, PillXs(strip));
                }
            }
        });
    }

    [Fact]
    public void SettingsHeaderIsAsTallAsEveryOtherSection()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var settings = Laid(NavSections.Settings, "appsettings").DesiredSize.Height;
            var play = Laid(NavSections.Play, "play").DesiredSize.Height;
            Assert.Equal(play, settings, 1);
        });
    }

    [Fact]
    public void PillsAreNamedButtonsWithAHelpTextAndAToolTipThatSaysMore()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Play, "play");
            foreach (var key in strip.PillKeys)
            {
                var pill = strip.PillFor(key);
                Assert.IsType<Button>(pill);
                var name = AutomationProperties.GetName(pill!);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.Contains(name, AutomationProperties.GetHelpText(pill!));
                Assert.NotEqual(name, pill!.ToolTip as string);
            }
        });
    }

    [Fact]
    public void PlayPillsFollowThePageOrder()
    {
        Assert.Equal(new[] { "play", "playeyes", "playsessions" },
            NavStripRules.Pills(NavSections.Play).Select(t => t.Key).Take(3).ToArray());
    }

    [Fact]
    public void TheGlowLastsTwoSecondsAndIsOffWithMotionOff()
    {
        Assert.Equal(0, NavGlow.TotalMs(MotionLevel.Off));
        Assert.Equal(2000, NavGlow.TotalMs(MotionLevel.Reduced));
        Assert.True(NavGlow.TotalMs(MotionLevel.Full) >= 2000);
    }

    // Polish wave 9 (owner: "make the border of the pills for the subtabs more noticeable, right
    // now they seem flat"): a heavier face border, a bevel that reads, a solid shaded foot.

    [Fact]
    public void PillFacesAreOneAndAHalfAtRestAndTwoWhenLit()
    {
        Assert.Equal(1.5, NavStripRules.RestFaceThickness);
        Assert.Equal(2.0, NavStripRules.ActiveFaceThickness);
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                foreach (var key in strip.PillKeys)
                {
                    double want = key == strip.ActivePillKey ? NavStripRules.ActiveFaceThickness : NavStripRules.RestFaceThickness;
                    Assert.Equal(want, strip.PillFaceThickness(key));
                    // The heavier ring stays inside the face: every pill is still 38 px tall.
                    Assert.Equal(NavStripRules.PillHeight, strip.PillFor(key)!.ActualHeight, 1);
                }
            }
        });
    }

    [Fact]
    public void TheOutlineFootIsASolidShadedLine()
    {
        Assert.Equal(0.85, NavStripRules.OutlineFootOffset);
        var bevel = NavStripRules.OutlineBrush(NavStripRules.Lilac);
        Assert.Equal(new[] { 0.0, 0.35, 0.65, 0.85 }, bevel.GradientStops.Select(g => g.Offset).ToArray());
    }

    [Fact]
    public void TheBevelIsVisibleOnEveryTint()
    {
        static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
        foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
        {
            var keys = s.Tabs.Select(t => t.Key).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                var tint = NavStripRules.TabTint(s.Key, keys[i], i, keys.Count);
                var stops = NavStripRules.OutlineBrush(tint).GradientStops;
                double delta = NavStripRules.Luminance(Opaque(stops[0].Color)) - NavStripRules.Luminance(Opaque(stops[^1].Color));
                Assert.True(delta >= 0.20, $"{s.Key}/{keys[i]}: bevel luminance delta {delta:F3} under 0.20");
            }
        }
    }
}
