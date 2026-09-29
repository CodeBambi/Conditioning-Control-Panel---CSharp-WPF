using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Tester feedback 2026-09-29: "a lot of them had the description state one time and then the
/// icon itself show another time". The words now carry a "{0}" the page fills from the price the
/// row books, so they cannot drift from the stamp again.
/// </summary>
public class ChasterTabCopyTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static JObject Lang(string lang) =>
        JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));

    [Fact]
    public void Every_fixed_figure_row_says_its_figure_through_the_mark_in_every_language()
    {
        var clock = new Regex(@"\d{1,2}:\d\d");
        var rows = TabPrices.All.Select(p => p.Id).Where(TabMenuCopy.HasFixedFigure).ToList();
        Assert.Contains("typo", rows);
        Assert.DoesNotContain("leash", rows);
        foreach (var lang in Languages)
        {
            var json = Lang(lang);
            foreach (var id in rows)
            {
                var text = (string?)json[TabMenuCopy.WhyKey(id)];
                Assert.False(string.IsNullOrEmpty(text), $"{lang} {id}: no copy");
                Assert.True(text!.Contains("{0}"), $"{lang} {id}: no figure mark: {text}");
                Assert.False(clock.IsMatch(text), $"{lang} {id}: a hard figure is left: {text}");
            }
        }
    }

    /// <summary>Bug hunt 2026-09-29 (TAB-3): the trailer prints a row's flavour line right over its
    /// why line, and flavour lines still named their own time ("Fat fingers. Fifteen seconds a
    /// typo." over "adds 0:30"), three more going stale after a price edit. The figure lives in the
    /// why line and on the stamp; a flavour line never names a time.</summary>
    [Fact]
    public void No_flavour_line_names_a_time()
    {
        var clock = new Regex(@"\d+:\d\d");
        var amount = new Regex(
            @"\b(\d+|a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|" +
            @"seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|ninety|half|quarter)" +
            @"([\s-]+(a|an|of|and|one|two|three|four|five|six|seven|eight|nine))*[\s-]+(seconds?|secs?|minutes?|mins?|hours?|hrs?)\b",
            RegexOptions.IgnoreCase);
        var lines = Lang("en").Properties()
            .Where(p => p.Name.StartsWith("chaster_flavour_", StringComparison.Ordinal))
            .Select(p => (Key: p.Name, Text: (string?)p.Value ?? ""))
            .ToList();
        Assert.Contains(lines, l => l.Key == TabMenuCopy.FlavourKey("typo"));
        var bad = lines.Where(l => clock.IsMatch(l.Text) || amount.IsMatch(l.Text)).Select(l => $"{l.Key}: {l.Text}").ToList();
        Assert.True(bad.Count == 0, "a flavour line names a time:\n" + string.Join("\n", bad));
        // the check itself bites
        Assert.Matches(amount, "Fat fingers. Fifteen seconds a typo.");
        Assert.Matches(amount, "Typed it clean. Half a minute back.");
        Assert.Matches(clock, "Melted. +5:00.");
        Assert.DoesNotMatch(amount, "Every second of it, to the end.");
    }

    /// <summary>Bug hunt 2026-09-29 (DESK-4): the "Day skipped" / "Day done" scene flew the live
    /// figures and then totalled them with written times (a "-5:00" pill under a "+30:00" flyer),
    /// and the Lockdown scene's timer jumped to a written 15:00 whatever the row's figure. A scene
    /// totals the figures the host mounts (CT.figs), never a time written into the scene.</summary>
    [Theory]
    [InlineData("program")]
    [InlineData("escape")]
    public void A_scene_totals_the_mounted_figures_never_a_written_time(string scene)
    {
        var html = File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, "Assets", "web", "chaster", "trailers.html"));
        var start = html.IndexOf("V." + scene + "=async function(s){", StringComparison.Ordinal);
        Assert.True(start >= 0, $"scene {scene} is missing");
        var end = html.IndexOf("\n};", start, StringComparison.Ordinal);
        Assert.True(end > start, $"scene {scene} has no end");
        var body = html[start..end];
        var written = new Regex(@"textContent\s*=\s*[^;]*(\d:\d\d|':00')");
        Assert.False(written.IsMatch(body), $"{scene}: a total is written as a time: {written.Match(body).Value}");
        Assert.Contains("CT.figs", body);
    }

    [Fact]
    public void Why_fills_the_mark_with_the_unsigned_figure()
    {
        var copy = new Dictionary<string, string>
        {
            ["chaster_why_typo"] = "Every wrong character adds {0}.",
            ["chaster_why_lockcard"] = "Finish clean and {0} comes off.",
        };
        string? Loc(string k) => copy.TryGetValue(k, out var v) ? v : k;
        Assert.Equal("Every wrong character adds 0:30.", TabMenuCopy.Why("typo", Loc, 30));
        Assert.Equal("Finish clean and 1:00 comes off.", TabMenuCopy.Why("lockcard", Loc, -60));
        Assert.Equal("", TabMenuCopy.Why("attention", Loc, 300));
    }

    [Fact]
    public void Scene_figures_follow_the_row_and_the_row_sharing_its_scene()
    {
        int Seconds(string id) => TabPrices.Find(id)!.Seconds;
        // lockcard plays the typo scene: its own earn-back and typo's cost
        Assert.Equal((30, 60), TabMenuCopy.SceneFigures("lockcard", Seconds));
        Assert.Equal((30, 60), TabMenuCopy.SceneFigures("typo", Seconds));
        // the hovered row wins its own side over a sibling on the same side
        Assert.Equal(1200, TabMenuCopy.SceneFigures("quest_weekly", Seconds).Sub);
        Assert.Equal(300, TabMenuCopy.SceneFigures("quest", Seconds).Sub);
        // a size picked elsewhere keeps the scene's own figures
        Assert.Equal(((int?)null, (int?)null), TabMenuCopy.SceneFigures("leash", Seconds));
        Assert.Equal(((int?)null, (int?)null), TabMenuCopy.SceneFigures(CircesMisses.EventId, Seconds));
    }
}
