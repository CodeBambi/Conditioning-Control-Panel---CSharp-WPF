using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner, 2026-10-07: "add a ? button over each chip so people can hover before clicking to know
/// what it does", and "a prompt on Just Drop that asks if they want to open it, since it opens in
/// a new window". Every visible pill carries a "what is this" line in every language, the badge
/// never grows the pill, and a Window pill only opens after "Open it".
/// </summary>
public class NavPillHelpTests
{
    private static string LanguagesDir() => SourceRoots.LanguagesDirectory;

    private static IEnumerable<(string Section, NavTab Tab)> VisiblePills() =>
        NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key))
            .SelectMany(s => s.Tabs.Where(t => !t.Hidden).Select(t => (s.Key, t)));

    private static readonly string[] ConfirmKeys =
        { "nav_open_window_title", "nav_open_window_body", "nav_open_window_yes", "nav_open_window_no" };

    [Fact]
    public void EveryVisiblePillHasAHelpLineInEveryLanguage()
    {
        var files = Directory.GetFiles(LanguagesDir(), "*.json");
        Assert.True(files.Length >= 9);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Path.GetFileName(file) + " has a BOM");
            using var doc = JsonDocument.Parse(bytes);
            var keys = VisiblePills().Select(p => NavStripRules.HelpKey(p.Tab)).Concat(ConfirmKeys);
            foreach (var key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var v), $"{Path.GetFileName(file)} lacks {key}");
                var text = v.GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)} {key} is empty");
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
                Assert.DoesNotContain("!", text);
            }
            Assert.Contains("{0}", doc.RootElement.GetProperty("nav_open_window_title").GetString());
        }
    }

    [Fact]
    public void TheEnglishHelpNeverGendersTheCompanion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(LanguagesDir(), "en.json")));
        foreach (var (_, tab) in VisiblePills())
        {
            var words = (doc.RootElement.GetProperty(NavStripRules.HelpKey(tab)).GetString() ?? "")
                .ToLowerInvariant().Split(' ', '.', ',', ':');
            Assert.DoesNotContain("she", words);
            Assert.DoesNotContain("her", words);
        }
    }

    [Fact]
    public void OnlyWindowPillsAskBeforeOpening()
    {
        var asking = VisiblePills().Where(p => NavStripRules.AsksBeforeOpening(p.Tab)).Select(p => p.Tab.Key).ToArray();
        Assert.Equal(new[] { "justdrop" }, asking);
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
    public void EveryPillWearsABadgeThatDoesNotGrowIt()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(x => NavStripRules.ShowsPills(x.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                foreach (var key in strip.PillKeys)
                {
                    var pill = strip.PillFor(key)!;
                    var badge = strip.HelpBadgeFor(key);
                    Assert.True(badge != null, $"{s.Key}/{key} has no ? badge");
                    var host = (FrameworkElement)pill.Parent;
                    Assert.Equal(pill.ActualWidth, host.ActualWidth, 2);
                    Assert.Equal(pill.ActualHeight, host.ActualHeight, 2);
                    // The badge sits on the pill's top-right corner, outside the button.
                    Assert.False(badge!.IsDescendantOf(pill));
                    var at = badge.TranslatePoint(new Point(0, 0), pill);
                    Assert.InRange(at.Y, -SectionTabStrip.HelpBadgeOverTop - 0.5, 0);
                    Assert.InRange(at.X + badge.ActualWidth - pill.ActualWidth, 0, SectionTabStrip.HelpBadgeOverRight + 0.5);
                }
            }
        });
    }

    [Fact]
    public void JustDropOpensOnlyAfterOpenIt()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Studio, "studio");
            var asked = new List<string>();
            strip.TabRequested += t => asked.Add(t.Key);

            strip.ChooseForTests("justdrop");
            Assert.Empty(asked);
            Assert.Equal("justdrop", strip.ConfirmingKey);
            strip.AnswerConfirmForTests(open: false);
            Assert.Empty(asked);
            Assert.Null(strip.ConfirmingKey);

            strip.ChooseForTests("justdrop");
            strip.AnswerConfirmForTests(open: true);
            Assert.Equal(new[] { "justdrop" }, asked);

            // A plain page still goes at once, and Just Drop never lights as the page on screen.
            strip.ChooseForTests("presets");
            Assert.Equal(new[] { "justdrop", "presets" }, asked);
            Assert.Equal("presets", strip.ActivePillKey);
        });
    }
}
