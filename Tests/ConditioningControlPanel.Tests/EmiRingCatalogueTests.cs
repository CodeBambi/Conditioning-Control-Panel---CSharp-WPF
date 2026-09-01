using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The EMI Desk ring catalogue, the half of it that can fail silently.
///
/// <para>A ring card is a string id in four places at once: the catalogue, the nine localisation
/// files, the tab map and the rack map. Three of those four are dictionaries of bare strings, so a
/// typo does not fail a compile and does not throw at runtime - the door simply never scores,
/// forever, and the suggester quietly ranks it last. That is the whole reason this file exists.</para>
///
/// <para>Nothing here touches <see cref="EmiState"/> or composes a ring: the state singleton reads
/// and writes the real user's <c>emi-desk.json</c>, and a test has no business editing that. The
/// catalogue itself is pure - <c>Build()</c> only captures lambdas, it never runs them - so every
/// invariant below is checked without opening a door or evaluating a tier gate.</para>
/// </summary>
public class EmiRingCatalogueTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string LangDir() =>
        SourceRoots.LanguagesDirectory;

    private static Dictionary<string, string> English()
    {
        var path = Path.Combine(LangDir(), "en.json");
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString() ?? string.Empty;
        return d;
    }

    private static IReadOnlyDictionary<string, string> Map(string field)
    {
        var f = typeof(EmiTargets).GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        return (IReadOnlyDictionary<string, string>)f!.GetValue(null)!;
    }

    private static IEnumerable<string> NewKeys() =>
        EmiTargets.All.Select(t => t.LabelKey)
            .Concat(new[] { "emi_desk_ring_tip_pinned", "emi_desk_ring_tip_suggested", "emi_desk_ring_tip_locked" });

    [Fact]
    public void Catalogue_has_no_duplicate_ids()
    {
        var ids = EmiTargets.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogue_fills_a_ring_several_times_over()
    {
        // Six slots, and the ring has to survive whole families of doors going dark at once
        // (no Lab tier, Arcademy off, JustDrop withheld) without running out of cards.
        Assert.True(EmiTargets.All.Count >= EmiSuggester.Slots * 3, $"only {EmiTargets.All.Count} targets");
    }

    [Fact]
    public void Every_card_that_declares_art_has_a_file_to_show()
    {
        // A ThumbPath is resolved at runtime by ModResourceResolver, which answers a miss with a
        // logged null and a flat hue tile - the same silent failure this file exists to catch. The
        // arcademy card shipped as that tile for a whole build before anyone noticed (QA
        // 2026-08-29), so the paths are checked against the tree instead of against a play-test.
        var res = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        foreach (var t in EmiTargets.All)
        {
            if (string.IsNullOrWhiteSpace(t.ThumbPath)) continue;
            var file = Path.Combine(res, t.ThumbPath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"\"{t.Id}\" points at missing art: {t.ThumbPath}");
        }
    }

    [Fact]
    public void Every_target_declares_the_label_key_the_convention_promises()
    {
        foreach (var t in EmiTargets.All)
            Assert.Equal("emi_desk_target_" + t.Id, t.LabelKey);
    }

    [Fact]
    public void Every_target_label_exists_in_english_and_fits_a_card()
    {
        var en = English();
        foreach (var t in EmiTargets.All)
        {
            Assert.True(en.ContainsKey(t.LabelKey), $"missing loc key {t.LabelKey}");
            var s = en[t.LabelKey];
            Assert.False(string.IsNullOrWhiteSpace(s), t.LabelKey);
            // The card is 76 DIPs wide with a 7px pixel font wrapping to two lines.
            Assert.True(s.Length <= 14, $"{t.LabelKey} is {s.Length} chars: \"{s}\"");
            Assert.DoesNotContain("\n", s);
        }
    }

    [Fact]
    public void Ring_tooltips_exist_in_english()
    {
        var en = English();
        foreach (var key in new[] { "emi_desk_ring_tip_pinned", "emi_desk_ring_tip_suggested", "emi_desk_ring_tip_locked" })
        {
            Assert.True(en.ContainsKey(key), $"missing loc key {key}");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), key);
        }
    }

    [Fact]
    public void All_nine_language_files_parse_strictly_and_carry_every_new_key()
    {
        var files = Directory.GetFiles(LangDir(), "*.json");
        Assert.Equal(9, files.Length);

        var needed = NewKeys().ToList();
        foreach (var f in files)
        {
            // Strict parse on purpose: a literal newline inside a value parses in Newtonsoft and
            // nowhere else, and this repo has been bitten by exactly that before.
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var k in needed)
                Assert.True(keys.Contains(k), $"{Path.GetFileName(f)} is missing {k}");
        }
    }

    [Fact]
    public void Tab_and_rack_maps_only_point_at_real_targets()
    {
        var ids = EmiTargets.All.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in Map("_tabTargets"))
            Assert.True(ids.Contains(kv.Value), $"tab \"{kv.Key}\" scores unknown target \"{kv.Value}\"");
        foreach (var kv in Map("_rackTargets"))
            Assert.True(ids.Contains(kv.Value), $"rack \"{kv.Key}\" scores unknown target \"{kv.Value}\"");
    }

    [Fact]
    public void No_door_is_counted_twice()
    {
        // A target is reachable by a tab OR a rack module OR a host launcher, never two of them,
        // or one sitting scores two opens and outranks an honest habit.
        var tabs = Map("_tabTargets").Values.ToList();
        var racks = Map("_rackTargets").Values.ToList();
        Assert.Empty(tabs.Intersect(racks, StringComparer.Ordinal));
        Assert.Equal(tabs.Count, tabs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(racks.Count, racks.Distinct(StringComparer.Ordinal).Count());

        // The three ShowTab keys intercepted above the counter, plus the quiz landing page, are
        // counted at their launchers instead, so they must NOT also sit in the tab map.
        foreach (var intercepted in new[] { "fyp", "justdrop", "patreon", "gradedintake" })
            Assert.False(Map("_tabTargets").ContainsKey(intercepted),
                $"\"{intercepted}\" is counted at its launcher, not at the bottom of ShowTab");
    }

    [Fact]
    public void Every_host_launcher_scores_its_own_door()
    {
        // Source tripwire: these launchers are the only way in for their features, so the counter
        // line inside each of them is the whole of "an open is an open, wherever it came from".
        var expected = new (string File, string Id)[]
        {
            (SourceRoots.FindProductFile("Services", "Arcademy", "ArcademyHostService.cs"), "arcademy"),
            (SourceRoots.FindProductFile("Services", "Chaos", "DtrhHostService.cs"), "dtrh"),
            (SourceRoots.FindProductFile("Services", "Chaos", "LoomHostService.cs"), "loom"),
            (SourceRoots.FindProductFile("Services", "Fyp", "FypHostService.cs"), "fyp"),
            (SourceRoots.FindProductFile("Services", "Quiz", "IntakeHostService.cs"), "intake"),
            (SourceRoots.FindProductFile("Services", "GoonGame", "GoonHostService.cs"), "goon"),
            (SourceRoots.FindProductFile("Services", "JustDrop", "JustDropHostService.cs"), "justdrop"),
        };

        foreach (var (file, id) in expected)
        {
            Assert.True(File.Exists(file), file);
            Assert.Contains($"App.EmiDesk?.NoteOpen(\"{id}\")", File.ReadAllText(file));
        }
    }

    [Fact]
    public void The_two_navigation_chokepoints_still_call_the_counter()
    {
        Assert.Contains("EmiTargets.NoteTabOpened(",
                        SourceRoots.ReadProductFile("MainWindow", "MainWindow.TabNavigation.cs"));
        Assert.Contains("EmiTargets.NoteRackOpened(",
                        SourceRoots.ReadProductFile("MainWindow", "MainWindow.Presets.cs"));
    }
}
