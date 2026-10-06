using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav rework 2026-10-06, TABSTRIP lane: the section page header. The pills follow
/// <see cref="NavSections"/> (hidden tabs skipped, tier badges in place), the keyboard wraps,
/// the section hues stay clear of the reserved tier and danger colours, the last-tab memory
/// round-trips, and ShowTab's door (registry, redirects, built-in keys) agrees with its switch.
/// Set NAV_STRIP_SHOTS to a folder to also save offscreen renders of every section's strip.
/// </summary>
public class SectionTabStripTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string TabNavigationSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.TabNavigation.cs"));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private static SectionTabStrip Laid(string section, string tab, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = width };
        strip.Show(section, tab);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    [Fact]
    public void ThePillsFollowTheTableAndSkipHiddenTabs()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order)
            {
                var strip = Laid(s.Key, s.DefaultTab);
                if (s.Key == NavSections.Home)
                {
                    Assert.Equal(Visibility.Collapsed, strip.Visibility);
                    continue;
                }
                Assert.Equal(Visibility.Visible, strip.Visibility);
                var expected = s.Key == NavSections.Settings
                    ? Array.Empty<string>()
                    : s.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray();
                Assert.Equal(expected, strip.PillKeys.ToArray());
                Assert.DoesNotContain(strip.PillKeys, k => s.Tabs.Any(t => t.Hidden && t.Key == k));
            }
        });
    }

    [Fact]
    public void LockedPillsWearTheirTierSignAndFreePillsDoNot()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                foreach (var t in NavStripRules.Pills(s.Key))
                {
                    var pill = strip.PillFor(t.Key);
                    Assert.NotNull(pill);
                    var badges = Descendants<TierBadge>(pill!).ToList();
                    if (t.Tier > 0)
                    {
                        Assert.True(badges.Count == 1, $"{s.Key}/{t.Key} is tier {t.Tier} and shows no tier sign");
                        Assert.Equal(t.Tier, badges[0].Tier);
                        Assert.False(badges[0].IsAnimating, "chrome never runs idle loops");
                    }
                    else Assert.Empty(badges);
                }
            }
        });
    }

    [Fact]
    public void TheActivePillAndTheBreadcrumbFollowTheTab()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Social, "availablesubjects");
            Assert.Equal("availablesubjects", strip.ActivePillKey);

            strip.Show(NavSections.Social, "leaderboard");
            Assert.Equal("leaderboard", strip.ActivePillKey);

            strip.Show(NavSections.Play, "gradedintake");
            Assert.Equal("playsessions", strip.ActivePillKey);
            Assert.Equal(NavSections.Play, strip.Section);
        });
    }

    [Theory]
    [InlineData("lab", "play")]
    [InlineData("play", "play")]
    [InlineData("playeyes", "playeyes")]
    [InlineData("gradedintake", "playsessions")]
    [InlineData("lockdown", "playsessions")]
    [InlineData("blinktrainer", "playeyes")]
    [InlineData("haptics", "haptics")]
    [InlineData("ramp", "ramp")]
    [InlineData("spiral", null)]
    [InlineData("settings", null)]
    [InlineData("appsettings", null)]
    public void EveryPageLightsTheRightPill(string tab, string? pill)
        => Assert.Equal(pill, NavStripRules.ActivePill(tab));

    [Theory]
    [InlineData(0, 5, Key.Left, 4)]
    [InlineData(4, 5, Key.Right, 0)]
    [InlineData(2, 5, Key.Right, 3)]
    [InlineData(2, 5, Key.Left, 1)]
    [InlineData(3, 5, Key.Home, 0)]
    [InlineData(1, 5, Key.End, 4)]
    [InlineData(-1, 5, Key.Right, 0)]
    [InlineData(2, 5, Key.Up, -1)]
    [InlineData(0, 0, Key.Right, -1)]
    public void TheKeyboardWrapsAndJumps(int current, int count, Key key, int expected)
        => Assert.Equal(expected, NavStripRules.MoveIndex(current, count, key));

    private static double Hue(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return 0;
        double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static double HueGap(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    [Fact]
    public void SectionHuesStayClearOfTheReservedColours()
    {
        // gold = T1, cyan = T2, red = danger / Circe's tab, mint = credit.
        var reserved = new Dictionary<string, double> { ["gold"] = 45, ["cyan"] = 190, ["red"] = 0, ["mint"] = 155 };
        var hues = new[] { NavStripRules.Lilac, NavStripRules.Pink, NavStripRules.VioletBlue, NavStripRules.Coral };
        foreach (var c in hues)
            foreach (var (name, h) in reserved)
                Assert.True(HueGap(Hue(c), h) >= 15, $"{c} sits {HueGap(Hue(c), h):0} degrees from {name}");

        // Four families, never a hue per section.
        Assert.Equal(4, NavSections.Order.Select(s => NavStripRules.Accent(s.Key)).Distinct().Count());
        Assert.Equal(NavStripRules.Accent(NavSections.Studio), NavStripRules.Accent(NavSections.Companion));
        Assert.Equal(NavStripRules.Accent(NavSections.Play), NavStripRules.Accent(NavSections.Social));
        Assert.Equal(NavStripRules.Accent(NavSections.Home), NavStripRules.Accent(NavSections.Library));
    }

    [Fact]
    public void TheLastTabPerSectionRoundTrips()
    {
        var json = NavStripRules.WithLastTab("", NavSections.Social, "leaderboard");
        json = NavStripRules.WithLastTab(json, NavSections.Play, "playeyes");
        Assert.Equal("leaderboard", NavStripRules.LastTabFor(json, NavSections.Social));
        Assert.Equal("playeyes", NavStripRules.LastTabFor(json, NavSections.Play));
        Assert.Equal("discord", NavStripRules.LastTabFor(json, NavSections.You));      // default
        Assert.Same(json, NavStripRules.WithLastTab(json, NavSections.Social, "leaderboard"));

        // A remembered tab the section no longer owns, or junk, falls back to the default.
        Assert.Equal("availablesubjects", NavStripRules.LastTabFor("{\"social\":\"quests\"}", NavSections.Social));
        Assert.Equal("assets", NavStripRules.LastTabFor("not json", NavSections.Library));
        Assert.Equal("play", NavStripRules.LastTabFor("{\"play\":\"lab\"}", NavSections.Play));
    }

    [Fact]
    public void TheSlideFollowsTheMotionLevel()
    {
        Assert.InRange(NavStripRules.SlideMs(MotionLevel.Full), 150, 200);
        Assert.Equal(NavStripRules.SlideMs(MotionLevel.Full) / 2, NavStripRules.SlideMs(MotionLevel.Reduced));
        Assert.Equal(0, NavStripRules.SlideMs(MotionLevel.Off));
    }

    [Fact]
    public void BuiltInTabKeysMatchTheShowTabSwitchExactly()
    {
        var cases = Regex.Matches(TabNavigationSource(), @"(?m)^\s*case ""(\w+)"":")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = MainWindow.BuiltInTabKeys;
        Assert.True(cases.SetEquals(declared),
            "BuiltInTabKeys drifted from the ShowTab switch. Only in the switch: "
            + string.Join(", ", cases.Except(declared)) + "; only in the set: " + string.Join(", ", declared.Except(cases)));
    }

    [Fact]
    public void OldKeysLandOnTheirNewHomeBeforeTheSwitch()
    {
        var src = TabNavigationSource();
        var door = src.IndexOf("if (TryRedirectMovedTab(tab)) return;", StringComparison.Ordinal);
        var collapse = src.IndexOf("SettingsTab.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        Assert.True(door > 0 && door < collapse, "the moved-key redirect must run before ShowTab collapses anything");
        Assert.DoesNotMatch(new Regex(@"(?m)^\s*case ""exclusives"":"), src);

        foreach (var key in MainWindow.MovedRedirectKeys)
            Assert.True(NavSections.Redirects.ContainsKey(key), $"{key} is toasted as moved but has no redirect");
        Assert.Equal(("settings", "account"), NavSections.Redirects["exclusives"]);
        Assert.Equal(3, MainWindow.NavMovedNoteLimit);
    }

    [Fact]
    public void RenderEverySectionsStrip()
    {
        var dir = Environment.GetEnvironmentVariable("NAV_STRIP_SHOTS");
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(s => s.Key != NavSections.Home))
            {
                // Light the second pill where there is one, so the shot shows rest and active.
                var tab = s.Tabs.Where(t => !t.Hidden && t.Kind is NavTabKind.Tab or NavTabKind.Zone)
                                .Skip(1).FirstOrDefault()?.Key ?? s.DefaultTab;
                var strip = Laid(s.Key, s.Key == NavSections.Settings ? "appsettings" : tab, 1180);
                Assert.True(strip.ActualHeight > 20 && strip.ActualHeight < 80, $"{s.Key} strip is {strip.ActualHeight} px tall");
                if (!string.IsNullOrEmpty(dir)) Shot(strip, dir!, $"strip-{s.Key}.png");
            }
        });
    }

    private static void Shot(FrameworkElement element, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth) + 40;
        var h = (int)Math.Ceiling(element.ActualHeight) + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(20, 20, element.ActualWidth, element.ActualHeight));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }
}
