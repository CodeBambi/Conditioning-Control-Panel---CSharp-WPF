using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard catalogue is hand-maintained and every column on it can drift without the build
/// noticing: a loc key renamed elsewhere, art moved, a rack key that no longer names a Studio
/// module, a ring or tier the owner reassigned in one place only. Each of those fails silently at
/// runtime (a raw key on the tile, a flat hue where art should be, a click that opens nothing).
/// </summary>
public class FeatureCatalogTests
{
    private static string ClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static Dictionary<string, string> English()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(ClientDir(), "Localization", "Languages", "en.json")));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? "";
        return map;
    }

    private static string[] Sorted(IEnumerable<string> keys) => keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Catalog_holds_27_rows_with_unique_keys_and_only_fx_can_split()
    {
        Assert.Equal(27, FeatureCatalog.All.Count);
        var keys = FeatureCatalog.All.Select(f => f.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
        foreach (var f in FeatureCatalog.All)
            Assert.Equal(f.Kind == DashboardKind.Fx, FeatureCatalog.CanSplit(f.Key));
        Assert.False(FeatureCatalog.CanSplit("nonsense"));
    }

    [Fact]
    public void Find_answers_by_key_and_refuses_anything_else()
    {
        Assert.Equal("flash", FeatureCatalog.Find("flash")?.Key);
        Assert.Equal("flash", FeatureCatalog.Find("FLASH")?.Key);
        Assert.Null(FeatureCatalog.Find("vault"));      // fixed cells are not catalog rows
        Assert.Null(FeatureCatalog.Find("mystery"));
        Assert.Null(FeatureCatalog.Find(""));
        Assert.Null(FeatureCatalog.Find(null));
    }

    [Fact]
    public void Every_row_names_its_title_exactly_once()
    {
        foreach (var f in FeatureCatalog.All)
            Assert.True(string.IsNullOrEmpty(f.TitleLocKey) ^ string.IsNullOrEmpty(f.TitleLiteral),
                $"'{f.Key}' needs a TitleLocKey or a TitleLiteral, not both and not neither.");
        // The brand names that stay unlocalized (MainWindow.PlayTab.cs:79-80).
        Assert.Equal(new[] { "arcademy", "dtrh", "goon", "piecebypiece" },
            Sorted(FeatureCatalog.All.Where(f => f.TitleLiteral != null).Select(f => f.Key)));
    }

    [Fact]
    public void Every_title_and_blurb_key_exists_in_english()
    {
        var en = English();
        foreach (var f in FeatureCatalog.All)
        {
            if (f.TitleLocKey != null)
            {
                Assert.True(en.ContainsKey(f.TitleLocKey), $"'{f.Key}' title key missing: {f.TitleLocKey}");
                Assert.False(string.IsNullOrWhiteSpace(en[f.TitleLocKey]), f.TitleLocKey);
            }
            Assert.True(en.ContainsKey(f.BlurbLocKey), $"'{f.Key}' blurb key missing: {f.BlurbLocKey}");
            Assert.False(string.IsNullOrWhiteSpace(en[f.BlurbLocKey]), f.BlurbLocKey);
            Assert.DoesNotContain("\n", en[f.BlurbLocKey]);   // one line, under a tile title
        }
    }

    [Fact]
    public void The_vault_ten_reuse_their_shelf_tagline_and_nobody_else_does()
    {
        bool Reused(DashboardFeature f) => f.BlurbLocKey.StartsWith("exclusives_tag_", StringComparison.Ordinal);
        Assert.Equal(Sorted(ExclusiveFeature.All.Select(e => e.Key)),
                     Sorted(FeatureCatalog.All.Where(Reused).Select(f => f.Key)));
        // Everything else gets a blurb of its own, by convention, so a new row cannot ship blank.
        foreach (var f in FeatureCatalog.All.Where(f => !Reused(f)))
            Assert.Equal("dash_blurb_" + f.Key, f.BlurbLocKey);
    }

    [Fact]
    public void Every_art_path_ships_and_resolves()
    {
        var res = Path.Combine(ClientDir(), "Resources");
        foreach (var f in FeatureCatalog.All)
        {
            Assert.True(File.Exists(Path.Combine(res, f.ArtPath.Replace('/', Path.DirectorySeparatorChar))),
                $"'{f.Key}' points at missing art: {f.ArtPath}");
            // The renderer goes through the resolver, and that is the path a mod override rides
            // on: a miss there is a silent flat tile rather than an error.
            Assert.NotNull(ModResourceResolver.ResolvePackPath(f.ArtPath));
        }
    }

    [Fact]
    public void Ring_membership_is_the_owner_lists()
    {
        void Ring(int n, params string[] expected) =>
            Assert.Equal(expected, FeatureCatalog.All.Where(f => f.Ring == n).Select(f => f.Key).ToArray());
        Ring(1, "flash", "video", "subliminal", "bouncingtext", "bubblecount", "bubbles");
        Ring(2, "spiral", "pinkfilter", "mindwipe", "braindrain", "lockcard", "goon", "gradedintake");
        Ring(3, "fyp", "blinktrainer", "remotecontrol", "bambitakeover", "shelistening", "haptics",
                "awareness", "lockdown");
        Ring(4, "dtrh", "justdrop", "arcademy", "piecebypiece", "gaze", "focusgaze");
    }

    [Fact]
    public void Tier_two_is_exactly_the_lab_doors_and_every_tier_follows_its_ring_band()
    {
        Assert.Equal(new[] { "arcademy", "dtrh", "focusgaze", "gaze", "justdrop", "piecebypiece" },
            Sorted(FeatureCatalog.All.Where(f => f.Tier == 2).Select(f => f.Key)));
        // Rings 1 and 2 are free, ring 3 is the Tier 1 shelf, ring 4 is the Lab. A row that
        // wandered off its band would advertise a price its handler does not charge.
        foreach (var f in FeatureCatalog.All)
            Assert.Equal(f.Ring <= 2 ? 0 : f.Ring == 3 ? 1 : 2, f.Tier);
    }

    [Fact]
    public void Every_fx_rack_key_is_a_studio_module_and_focusgaze_is_the_only_rackless_one()
    {
        var src = File.ReadAllText(Path.Combine(ClientDir(), "Views", "Tabs", "StudioTabView.xaml.cs"));
        var rack = Regex.Matches(src, @"^\s*Add\(""([a-z0-9]+)"",", RegexOptions.Multiline)
                        .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(rack.Count >= 15, $"only {rack.Count} rack keys parsed out of StudioTabView");

        foreach (var f in FeatureCatalog.All.Where(f => f.Kind == DashboardKind.Fx && f.RackKey != null))
            Assert.True(rack.Contains(f.RackKey!), $"'{f.Key}' names rack key '{f.RackKey}', which BuildRack does not register.");

        Assert.Equal(new[] { "focusgaze" },
            FeatureCatalog.All.Where(f => f.Kind == DashboardKind.Fx && f.RackKey == null).Select(f => f.Key).ToArray());
        foreach (var f in FeatureCatalog.All)
            if (f.Kind == DashboardKind.Fx) Assert.Null(f.TabKey); else Assert.Null(f.RackKey);
    }

    [Fact]
    public void Daily_free_keys_stay_inside_the_services_keyspace()
    {
        // DailyFreeService's overridable set. Deliberately NOT the ShowTab keys - "remote" vs
        // "remotecontrol", "takeover" vs "bambitakeover", "voice" vs "shelistening".
        var pool = new[] { "takeover", "awareness", "fyp", "remote", "haptics", "voice", "dtrh" }
            .ToHashSet(StringComparer.Ordinal);
        foreach (var f in FeatureCatalog.All.Where(f => f.DailyFreeKey != null))
            Assert.True(pool.Contains(f.DailyFreeKey!), $"'{f.Key}' names unknown daily-free key '{f.DailyFreeKey}'.");
        Assert.Equal(new[] { "awareness", "dtrh", "fyp", "haptics", "remote", "takeover", "voice" },
            Sorted(FeatureCatalog.All.Where(f => f.DailyFreeKey != null).Select(f => f.DailyFreeKey!)));
    }
}
