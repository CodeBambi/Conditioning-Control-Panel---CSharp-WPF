using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav rework (2026-10-06): <see cref="NavSections"/> is the one table the rail, the tab strip,
/// the palette and the redirects read. These pin its order (frozen for six months after ship),
/// that every tab key it names is a live ShowTab key (or a declared window, launcher or Settings
/// zone, or one of the keys the lanes still owe), and that every label key exists in every
/// language file.
/// </summary>
public class NavSectionsTests
{
    /// <summary>Tab keys the lanes add a ShowTab case for. When a lane wires one, delete it
    /// here; when this list is empty the rework is fully wired. A key left here after its case
    /// lands fails <see cref="PendingKeysAreStillPending"/>.</summary>
    private static readonly string[] PendingShowTabKeys =
    {
        "friends", "leash",                               // SOCIAL pages (REHOME)
        "folders",                                        // LIBRARY page (REHOME)
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string TabNavigationSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.TabNavigation.cs"));

    /// <summary>Keys ShowTab answers: its own case labels plus every page a lane registers
    /// through the tab registry (<c>new NavTabHost("key", ...)</c> in any MainWindow partial).</summary>
    private static HashSet<string> ShowTabCases()
    {
        var keys = new HashSet<string>(
            Regex.Matches(TabNavigationSource(), @"(?m)^\s*case ""(\w+)"":").Select(m => m.Groups[1].Value),
            StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow"), "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"new NavTabHost\(\s*""(\w+)"""))
                keys.Add(m.Groups[1].Value);
        return keys;
    }

    private static string[] LanguageFiles() => Directory
        .GetFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages"), "*.json")
        .OrderBy(f => f, StringComparer.Ordinal).ToArray();

    private static HashSet<string> LanguageKeys(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new HashSet<string>(doc.RootElement.EnumerateObject().Select(p => p.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void TheRailOrderIsFrozen()
    {
        Assert.Equal(
            new[] { "home", "studio", "companion", "play", "social", "you", "library", "settings" },
            NavSections.Order.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void EverySectionOpensOnATabItOwns()
    {
        foreach (var s in NavSections.Order)
        {
            Assert.Equal(s.DefaultTab, NavSections.DefaultTab(s.Key));
            Assert.Equal(s.Key, NavSections.SectionForTab(s.DefaultTab));
            Assert.InRange(s.Tabs.Count(t => !t.Hidden), 1, 10);
        }
        Assert.Equal("home", NavSections.SectionForTab("settings"));       // the dashboard
        Assert.Equal("settings", NavSections.SectionForTab("appsettings"));
        Assert.Null(NavSections.SectionForTab("no-such-tab"));
    }

    [Fact]
    public void NoTabKeyLivesInTwoSections()
    {
        var dupes = NavSections.AllTabs.GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "tab keys in two sections: " + string.Join(", ", dupes));
    }

    [Fact]
    public void EveryTabKeyIsALiveShowTabKeyOrDeclared()
    {
        var cases = ShowTabCases();
        var settingsZones = new HashSet<string>(AppSettingsTabView.SectionKeys, StringComparer.OrdinalIgnoreCase);
        var src = TabNavigationSource();
        var missing = new List<string>();

        foreach (var s in NavSections.Order)
            foreach (var t in s.Tabs)
            {
                if (s.Key == NavSections.Settings)
                {
                    if (!settingsZones.Contains(t.Key)) missing.Add($"settings/{t.Key} (no AppSettings section)");
                    continue;
                }
                switch (t.Kind)
                {
                    case NavTabKind.Launcher:
                        continue;   // dialogs, sites and windows: no tab key by design
                    case NavTabKind.Window:
                        if (!src.Contains($"\"{t.Key}\"")) missing.Add($"{s.Key}/{t.Key} (window not intercepted)");
                        continue;
                    default:
                        if (!cases.Contains(t.Key) && !PendingShowTabKeys.Contains(t.Key))
                            missing.Add($"{s.Key}/{t.Key}");
                        continue;
                }
            }

        Assert.True(missing.Count == 0, "tab keys with no ShowTab case and not declared pending: " + string.Join(", ", missing));
    }

    [Fact]
    public void PendingKeysAreStillPending()
    {
        var cases = ShowTabCases();
        var wired = PendingShowTabKeys.Where(cases.Contains).ToList();
        Assert.True(wired.Count == 0, "these keys now have a ShowTab case; drop them from PendingShowTabKeys: " + string.Join(", ", wired));
        foreach (var k in PendingShowTabKeys)
            Assert.Contains(NavSections.AllTabs, t => t.Key == k);
    }

    [Fact]
    public void EveryRedirectLandsOnARealTab()
    {
        foreach (var (old, to) in NavSections.Redirects)
        {
            var section = NavSections.Find(to.Section);
            Assert.True(section != null, $"redirect {old} -> unknown section {to.Section}");
            Assert.True(section!.Tabs.Any(t => t.Key == to.Tab) || section.DefaultTab == to.Tab,
                $"redirect {old} -> {to.Section}/{to.Tab}, which that section does not own");
        }
        Assert.Equal(("settings", "account"), NavSections.Redirects["exclusives"]);
        Assert.Equal(("settings", "account"), NavSections.Redirects["patreon"]);
        Assert.Equal(("play", "play"), NavSections.Redirects["lab"]);
        Assert.Equal(("home", "settings"), NavSections.Redirects["progression"]);
        Assert.Equal(("social", "availablesubjects"), NavSections.Redirects["together"]);
    }

    [Fact]
    public void TheOldNamesAreSearchable()
    {
        var all = NavSections.Aliases.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Exclusives", "Premium", "Velvet Vault", "The Vault", "Lab", "Effects Rack", "Available Subjects", "Together" })
            Assert.Contains(name, all);
    }

    [Fact]
    public void TheLegacyDoorBridgeKeepsTheOldRail()
    {
        // Pinned to the hand-written NavDoorMap this replaced (origin/main 4629b1f4a), so the
        // BASE commit changes no behaviour. The RAIL lane deletes this test with the bridge.
        var built = NavLegacyDoors.Build();
        Assert.Equal(new[] { "home", "studio", "companion", "play", "you", "library", "appsettings" },
            built.Select(d => d.Door).ToArray());
        var map = built.ToDictionary(d => d.Door);

        void Is(string door, string def, params string[] tabs)
        {
            Assert.Equal(def, map[door].DefaultTab);
            Assert.Equal(tabs.OrderBy(t => t), map[door].Tabs.OrderBy(t => t));
        }
        Is("home", "settings", "settings", "progression");
        Is("studio", "studio", "studio", "presets", "haptics", "justdrop");
        Is("companion", "companion", "companion", "bambitakeover", "shelistening", "awareness");
        Is("play", "play", "play", "deeper", "exclusives", "gradedintake", "lockdown", "blinktrainer", "remotecontrol", "availablesubjects");
        Is("you", "discord", "discord", "spiral", "quests", "achievements", "enhancements", "programs", "leaderboard");
        Is("library", "assets", "assets");
        Is("appsettings", "appsettings", "appsettings");
    }

    [Fact]
    public void EveryLabelKeyExistsInEveryLanguage()
    {
        var files = LanguageFiles();
        Assert.True(files.Length >= 9, "expected at least nine language files");
        var labels = NavSections.Order.Select(s => s.LabelKey)
            .Concat(NavSections.AllTabs.Select(t => t.LabelKey))
            .Concat(NewKeys).Distinct().ToList();

        foreach (var file in files)
        {
            var keys = LanguageKeys(file);
            var missing = labels.Where(k => !keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{Path.GetFileName(file)} lacks: {string.Join(", ", missing)}");
        }
    }

    /// <summary>The keys the BASE commit added for the lanes.</summary>
    private static readonly string[] NewKeys =
    {
        "nav_section_social", "nav_tab_games", "nav_tab_sessions", "nav_tab_eyes", "nav_tab_ramp",
        "nav_tab_chat", "nav_tab_personality", "nav_tab_permissions", "nav_tab_companionlinks",
        "nav_tab_friends", "nav_tab_leash", "nav_tab_folders", "nav_tab_lobby", "nav_badge_open",
        "nav_crumb_sep", "settings_section_monitors", "settings_section_plans", "nav_search_placeholder",
        "nav_search_none", "nav_search_try", "nav_search_all", "nav_moved_toast", "nav_was_hint",
        "whatmoved_title", "whatmoved_intro", "whatmoved_show", "whatmoved_close", "whatmoved_row_premium",
        "whatmoved_row_lobby", "whatmoved_row_monitors", "whatmoved_row_tabs", "whatmoved_row_games",
        "help_whatmoved",
    };
}
