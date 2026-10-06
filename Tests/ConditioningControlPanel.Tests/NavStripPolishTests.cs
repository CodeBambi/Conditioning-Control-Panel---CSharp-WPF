using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
}
