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
        JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json")));

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
