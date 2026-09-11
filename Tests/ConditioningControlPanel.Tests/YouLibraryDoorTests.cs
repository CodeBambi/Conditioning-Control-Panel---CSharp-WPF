using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 7 — the You and Library doors.
///
/// <para><b>Why these are source-text assertions and not render tests.</b> Phase 6's
/// <see cref="PlayDoorRenderTests"/> could realize <c>PlayTabView</c> because it is a UserControl.
/// Phase 7's surface is the nav rail itself, which lives in <c>MainWindow.xaml</c> — and MainWindow
/// cannot be instantiated in a unit test without the whole service graph. The failures this phase
/// can actually ship are all legible in the source anyway, and every one of them survives a clean
/// compile:</para>
///
/// <list type="number">
/// <item>A <c>{loc:Str}</c> key with no entry in <c>en.json</c>. WPF renders the raw key, so the
/// rail reads "yl7_nav_mods" and nothing throws. Nothing else in the suite reads MainWindow.xaml,
/// so this was previously unguarded for all 125 of its keys.</item>
/// <item>A Ctrl+K palette row whose <c>ElementNames</c> no longer matches an <c>x:Name</c>.
/// <c>SettingsPaletteWindow.Navigate</c> treats a miss as a soft failure by design (Debug log, no
/// throw) — correct at runtime, invisible in review.</item>
/// <item>A rail row missing from <c>MainWindow.ChromeFx.NavButtons</c>. That list is the only
/// thing that subscribes a row to the icon hover nudge; a row left out still gets the style's
/// hover background, so it reads as "that button feels slightly dead". Phase 4's
/// <c>BtnNavStudio</c> was missing from it until this phase.</item>
/// <item>The "Enhancements" → "Skill Tree" rename leaking past user-facing strings into the tab
/// KEY, which is API: 54 bark rules per built-in mod, the tutorial ladder, the ambient-FX registry
/// and the palette are all keyed off <c>"enhancements"</c>.</item>
/// </list>
/// </summary>
public class YouLibraryDoorTests
{
    // =====================================================================================
    //  fixtures
    // =====================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ProductDir => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { ProductDir }.Concat(parts).ToArray()));

    /// <summary>Build output and nested agent worktrees, matched on the path RELATIVE to the
    /// product directory.</summary>
    ///
    /// <para>Matching the ABSOLUTE path is the bug this replaces: an agent worktree checks the whole
    /// repo out under <c>&lt;repo&gt;/.claude/worktrees/&lt;name&gt;/</c>, so every file's absolute
    /// path carries a <c>.claude</c> segment and the walk excluded all of them — the source scan
    /// below then "passed" against an empty set in a worktree and only really ran on a normal
    /// checkout. The exclusion is about directories INSIDE the tree under test, so only the
    /// relative path may decide it.</para>
    private static bool IsExcludedSource(string file)
    {
        var skip = new[] { "obj", "bin", ".claude" };
        return Path.GetRelativePath(ProductDir, file)
                   .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(segment => skip.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string MainWindowXaml() => ReadSource("MainWindow", "MainWindow.xaml");

    private static Dictionary<string, string> Language(string file)
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", file);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        // Indexer, not ToDictionary: several language files carry duplicate keys (en.json has 14,
        // e.g. btn_cancel and deeper_editor_section_selected_empty — all pre-dating this phase).
        // Newtonsoft, which is what LocalizationManager actually parses with, takes the LAST
        // occurrence, so mirroring that is both crash-free and honest about what ships.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String)
                map[p.Name] = p.Value.GetString()!;
        return map;
    }

    private static readonly string[] AllLanguages =
        { "en.json", "de.json", "es.json", "fr.json", "ja.json", "ko.json", "pt-BR.json", "ru.json", "zh-CN.json" };

    /// <summary>The four Library rows added in Phase 7, and the handler each one binds.</summary>
    public static IEnumerable<object[]> LibraryLaunchers => new[]
    {
        new object[] { "BtnNavMods",     "BtnManageMods_Click",   "yl7_nav_mods",      "yl7_nav_mods_tip" },
        new object[] { "BtnNavCatalogue", "BtnCatalogue_Click",   "yl7_nav_catalogue", "yl7_nav_catalogue_tip" },
        new object[] { "BtnNavPhrases",  "BtnManagePhrases_Click", "yl7_nav_phrases",  "yl7_nav_phrases_tip" },
        new object[] { "BtnNavMediaLog", "BtnNavMediaLog_Click",  "yl7_nav_medialog",  "yl7_nav_medialog_tip" },
    };

    /// <summary>The Library door's rail panel, Assets row included.</summary>
    private static string LibraryDoorPanel()
    {
        var xaml = MainWindowXaml();
        // Anchored on </StackPanel></Border>, not the first </StackPanel>: every row's content is
        // itself a StackPanel, so a lazy match to the first close tag stops inside row one.
        var m = Regex.Match(xaml, "<StackPanel x:Name=\"DoorEntriesLibrary\".*?</StackPanel>\\s*</Border>",
                            RegexOptions.Singleline);
        Assert.True(m.Success, "DoorEntriesLibrary is gone from MainWindow.xaml — the Library door has no rail panel");
        return m.Value;
    }

    // =====================================================================================
    //  1. the Library door's four launcher rows
    // =====================================================================================

    [Theory]
    [MemberData(nameof(LibraryLaunchers))]
    public void EachLibraryLauncherIsOneRowBoundToOneExistingHandler(
        string buttonName, string handler, string labelKey, string tipKey)
    {
        var panel = LibraryDoorPanel();

        var row = Regex.Match(panel, "<Button x:Name=\"" + buttonName + "\".*?</Button>", RegexOptions.Singleline);
        Assert.True(row.Success, buttonName + " is not in the Library door's rail panel");

        Assert.Contains("Click=\"" + handler + "\"", row.Value);
        Assert.Contains("{loc:Str " + labelKey + "}", row.Value);
        Assert.Contains("{loc:Str " + tipKey + "}", row.Value);

        // Exactly one row per window. A second surface for the same launcher is how the Media Log
        // "unseen" badge ends up armed forever: only the Assets tab's own button banks the count.
        Assert.Single(Regex.Matches(MainWindowXaml(), "x:Name=\"" + buttonName + "\""));

        // The handler has to exist somewhere in the product. XAML Click= is compile-checked, so
        // this is belt-and-braces — but it is also the assertion that fails loudly if someone
        // "simplifies" a row by pointing it at a new duplicate launcher instead of the real one.
        var sources = Directory
            .EnumerateFiles(ProductDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedSource(f))
            .ToList();
        Assert.True(sources.Count > 0, "the product source walk found no .cs files under " + ProductDir);

        var found = sources.Any(f => Regex.IsMatch(File.ReadAllText(f), @"\bvoid\s+" + handler + @"\s*\("));
        Assert.True(found, handler + " is bound in MainWindow.xaml but defined nowhere");
    }

    [Fact]
    public void AssetsStaysTheLibraryDoorsFirstRow()
    {
        // NavDoorMap routes the door header to "assets"; ShowTab then moves the active indicator
        // onto whichever row owns that key. If a launcher were first, the door would open with the
        // indicator on the second row — the rail would look like it navigated somewhere it didn't.
        var names = Regex.Matches(LibraryDoorPanel(), "<Button x:Name=\"(\\w+)\"")
                         .Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal("BtnOpenAssetsTop", names.First());
        Assert.Equal(5, names.Length);
    }

    [Fact]
    public void TheLibraryLaunchersClaimNoTabKey()
    {
        // A rail row that claims a key the app has no view for leaves the active indicator
        // pointing at nothing. These four open a dialog, a website, a dialog and a window.
        var nav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var row = Regex.Match(nav, @"\(""library"",\s*""assets"",\s*new\[\]\s*\{([^}]*)\}");
        Assert.True(row.Success, "the NavDoorMap row for the Library door has moved or changed shape");
        Assert.Equal("\"assets\"", row.Groups[1].Value.Trim());
    }

    [Fact]
    public void TheMediaLogRowRefiresTheAssetsButtonRatherThanOpeningItsOwnWindow()
    {
        // The Assets tab's BtnMediaLog has a SECOND Click subscriber — MediaLogButton_Clicked in
        // MainWindow.AssetsFx.cs — which banks _mediaLogSeenCount so the three-beat "new media"
        // pulse goes quiet once the log has been read. A parallel `new MediaHistoryWindow()` here
        // would open the same window and leave that badge armed from the one entry point that
        // never touches the Assets tab.
        var nav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var body = Regex.Match(nav, @"void BtnNavMediaLog_Click\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(body.Success, "BtnNavMediaLog_Click is gone");

        Assert.Contains("InitializeAssetsFx()", body.Value);        // subscription is wired lazily
        Assert.Contains("BtnMediaLog", body.Value);                 // ...then the real button fires
        Assert.DoesNotContain("new MediaHistoryWindow", body.Value);

        // And the seen-count subscriber is still the thing that would be bypassed.
        var fx = ReadSource("MainWindow", "MainWindow.AssetsFx.cs");
        Assert.Contains("BtnMediaLog.Click += MediaLogButton_Clicked", fx);
        Assert.Contains("_mediaLogSeenCount =", fx);
    }

    // =====================================================================================
    //  2. localization — the failure that renders as raw key text
    // =====================================================================================

    [Fact]
    public void EveryLocKeyInMainWindowXamlExistsInEnglish()
    {
        var en = Language("en.json");
        var missing = Regex.Matches(MainWindowXaml(), @"\{loc:Str\s+([A-Za-z0-9_]+)\s*\}")
                           .Select(m => m.Groups[1].Value)
                           .Distinct(StringComparer.Ordinal)
                           .Where(k => !en.ContainsKey(k))
                           .OrderBy(k => k, StringComparer.Ordinal)
                           .ToArray();

        Assert.True(missing.Length == 0,
            "MainWindow.xaml binds loc keys with no en.json entry — the rail renders the raw key: "
            + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(LibraryLaunchers))]
    public void EveryLibraryLauncherKeyIsTranslatedInAllNineLanguages(
        string buttonName, string handler, string labelKey, string tipKey)
    {
        _ = buttonName; _ = handler;
        foreach (var file in AllLanguages)
        {
            var lang = Language(file);
            foreach (var key in new[] { labelKey, tipKey })
            {
                Assert.True(lang.ContainsKey(key), $"{file} is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(lang[key]), $"{file}: {key} is blank");
            }
        }
    }

    // =====================================================================================
    //  3. the rename — user-facing strings only
    // =====================================================================================

    [Fact]
    public void TheEnhancementsTabKeyIsUntouchedByTheSkillTreeRename()
    {
        // The rename is USER-FACING STRINGS ONLY. This key is API: bark rules (tab_eq), tutorial
        // RequiresTab steps, RegisterTabFx and the Ctrl+K palette all spell it "enhancements".
        var nav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("\"enhancements\"", nav);
        Assert.Contains("x:Name=\"BtnEnhancements\"", MainWindowXaml());

        var tutorial = ReadSource("Services", "TutorialService.cs");
        Assert.Contains("[\"BtnEnhancements\"] = \"enhancements\"", tutorial);

        var palette = ReadSource("Services", "SettingsPaletteIndex.cs");
        Assert.Contains("Tab(\"enhancements\", \"tab_enhancements\"", palette);
    }

    [Fact]
    public void TheSkillTreeStringsNoLongerCallThemselvesEnhancements()
    {
        // The English copy is the reference register; the other eight follow their own term
        // (Talentbaum, 스킬 트리, 技能树, …), which is why only en is asserted by wording.
        //
        // msg_skill_seasonal_note used to be listed here and is not any more: the Descent folded
        // every skill into permanent, so the seasonal note was retired from all nine files rather
        // than left telling users about a rollover that cannot happen. The permanent note carries
        // the whole job now, and it is still checked for the old register right below.
        var en = Language("en.json");
        foreach (var key in new[] { "tab_enhancements", "label_enhancement_tree_title",
                                    "tooltip_enhancement_tree", "dialog_purchase_enhancement",
                                    "msg_skill_permanent_note",
                                    "skill_err_login_required", "tooltip_prestige",
                                    "label_log_in_with_discord_or_patreon_to_access_enha" })
        {
            Assert.True(en.ContainsKey(key), "en.json lost " + key);
            Assert.DoesNotContain("nhancement", en[key]);
        }
    }

    [Fact]
    public void TheSkillTreeFallbackTitlesMatchTheRenamedLocKeys()
    {
        // ModService's fallbacks SHADOW label_enhancement_tree_title / tooltip_enhancement_tree:
        // App.Mods is never null in practice, so the literal is what actually renders. A rename
        // that lands only in the JSON never reaches the tree header or the nav tooltip.
        var mods = ReadSource("Services", "ModService.cs");
        Assert.DoesNotContain("Bimbo Enhancement Tree", mods);
        Assert.Contains("MakeModAware(\"Bimbo Skill Tree\")", mods);
    }

    // =====================================================================================
    //  4. rail parity — the hover nudge
    // =====================================================================================

    [Fact]
    public void EveryRailRowIsSubscribedToTheIconHoverNudge()
    {
        // MainWindow.ChromeFx.NavButtons is the ONLY thing that wires a rail row to the nudge.
        // The list is null-tolerant, so an omission is silent: the row keeps the style's hover
        // background and simply never moves. Phase 4's BtnNavStudio sat outside it for two phases.
        var xaml = MainWindowXaml();
        var rows = Regex.Matches(xaml, "<StackPanel x:Name=\"DoorEntries\\w+\".*?</StackPanel>\\s*</Border>",
                                 RegexOptions.Singleline)
                        .SelectMany(p => Regex.Matches(p.Value, "<Button x:Name=\"(\\w+)\"")
                                              .Select(b => b.Groups[1].Value))
                        .ToArray();
        Assert.True(rows.Length >= 25, "the door panels stopped parsing — found only " + rows.Length + " rows");

        var chromeFx = ReadSource("MainWindow", "MainWindow.ChromeFx.cs");
        var list = Regex.Match(chromeFx, @"IEnumerable<Button> NavButtons.*?return all", RegexOptions.Singleline);
        Assert.True(list.Success, "the NavButtons list has changed shape");

        var missing = rows.Where(r => !Regex.IsMatch(list.Value, @"\b" + r + @"\b")).ToArray();
        Assert.True(missing.Length == 0,
            "rail rows missing from ChromeFx.NavButtons (no hover nudge): " + string.Join(", ", missing));
    }

    // =====================================================================================
    //  5. the Ctrl+K palette rows
    // =====================================================================================

    [Fact]
    public void TheLibraryPaletteRowsPointAtElementsThatExist()
    {
        // Navigate() logs a miss at Debug and returns — correct behaviour, invisible in review.
        var palette = ReadSource("Services", "SettingsPaletteIndex.cs");
        var xaml = MainWindowXaml();
        var en = Language("en.json");

        // Explicitly Match, not var: MatchCollection's pattern-based GetEnumerator is the
        // non-generic one, so `var` here binds to object.
        foreach (Match launcher in Regex.Matches(palette, @"Launcher\(""(\w+)"",\s*""(\w+)"",\s*""[^""]+"",\s*""(\w+)"""))
        {
            var id = launcher.Groups[1].Value;
            var labelKey = launcher.Groups[2].Value;
            var element = launcher.Groups[3].Value;

            Assert.True(en.ContainsKey(labelKey), $"palette row launch.{id} labels itself with the unknown key {labelKey}");
            Assert.Contains("x:Name=\"" + element + "\"", xaml);
        }

        // All four, not three: a dropped row is a feature nobody can search for.
        Assert.Equal(4, Regex.Matches(palette, @"\n\s*Launcher\(""").Count);
    }
}
