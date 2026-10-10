using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests.Parity;

/// <summary>
/// Parity with WPF 7.1.5 FlavourPresetsTests: the desktop's flavour tiles are a COPY of the web
/// pickers' table (Assets/web/goon/ui/flavours.js). This reads the js file and fails the moment
/// the two drift.
/// </summary>
public class FlavourPresetsParityTests
{
    private static string Js() => File.ReadAllText(Path.Combine(ParityPaths.RepoRoot(), "Assets", "web", "goon", "ui", "flavours.js"));

    private static string[] List(string body) =>
        Regex.Matches(body, "'([^']*)'").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void Every_flavour_matches_the_web_table()
    {
        var rows = Regex.Matches(Js(),
            @"\{\s*id:\s*'(?<id>[^']+)',\s*name:\s*'(?<name>[^']+)',\s*line:\s*'(?<line>[^']+)',\s*tint:\s*'(?<tint>[^']+)',\s*subs:\s*\[(?<subs>[^\]]*)\],\s*extras:\s*\[(?<extras>[^\]]*)\]\s*\}");
        Assert.True(rows.Count >= 6, "expected five flavours plus MINE in flavours.js, found " + rows.Count);

        var web = rows.ToList();
        var mine = web.Single(m => m.Groups["id"].Value == FlavourPresets.MineId);
        var flavours = web.Where(m => m.Groups["id"].Value != FlavourPresets.MineId).ToList();

        Assert.Equal(flavours.Select(m => m.Groups["id"].Value), FlavourPresets.All.Select(f => f.Id));
        for (int i = 0; i < flavours.Count; i++)
        {
            var m = flavours[i];
            var f = FlavourPresets.All[i];
            Assert.Equal(m.Groups["name"].Value, f.Name);
            Assert.Equal(m.Groups["line"].Value, f.Line);
            Assert.Equal(m.Groups["tint"].Value, f.Tint);
            Assert.Equal(List(m.Groups["subs"].Value), f.Subs);
            Assert.Equal(List(m.Groups["extras"].Value), f.Extras);
        }
        Assert.Equal(mine.Groups["name"].Value, FlavourPresets.Mine.Name);
        Assert.Equal(mine.Groups["line"].Value, FlavourPresets.Mine.Line);
        Assert.Equal(mine.Groups["tint"].Value, FlavourPresets.Mine.Tint);
    }

    [Fact]
    public void Resolve_covers_every_core_sub_exactly_once_between_niches_and_custom_subs()
    {
        var catalog = FypOnlineCoordinator.Catalog;
        foreach (var f in FlavourPresets.All)
        {
            var sel = FlavourPresets.Resolve(f, catalog);
            var covered = sel.NicheIds
                .SelectMany(id => catalog.Single(n => n.Id == id).Subs)
                .Concat(sel.CustomSubs)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var s in f.Subs) Assert.Contains(s, covered);
            Assert.DoesNotContain(sel.CustomSubs, s => sel.NicheIds
                .Any(id => catalog.Single(n => n.Id == id).Subs.Contains(s, StringComparer.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Each_flavour_is_recognised_back_from_its_own_selection()
    {
        var catalog = FypOnlineCoordinator.Catalog;
        foreach (var f in FlavourPresets.All)
        {
            var sel = FlavourPresets.Resolve(f, catalog);
            Assert.Equal(f.Id, FlavourPresets.Match(sel.NicheIds, sel.CustomSubs, catalog)?.Id);
        }
        Assert.Null(FlavourPresets.Match(new[] { "hentai" }, Array.Empty<string>(), catalog));
    }

    [Fact]
    public void Frills_takes_the_sissy_niche_not_the_wider_hypno_one()
    {
        var sel = FlavourPresets.Resolve(FlavourPresets.ById("frills")!, FypOnlineCoordinator.Catalog);
        Assert.Contains("sissy", sel.NicheIds);
        Assert.DoesNotContain("hypno", sel.NicheIds);
        Assert.Same(FlavourPresets.Mine, FlavourPresets.ById("MINE"));
    }
}
