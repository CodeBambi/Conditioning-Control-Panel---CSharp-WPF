using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The boot decision and the game catalogue, the two halves of the launcher that can fail silently.
///
/// <para><see cref="LauncherBoot.Decide"/> is the whole reason a fresh install never sees the
/// launcher before the 18+ gate, and the reason a game shortcut lands in the game and not in a
/// picker. Both are string matching on argv, so every rule is a row here.</para>
///
/// <para>The catalogue only captures lambdas, so walking it opens nothing and evaluates no tier
/// gate. What it CAN get wrong is an id typo or a missing loc key, and neither fails a compile.</para>
/// </summary>
public class LauncherBootTests
{
    private static bool Known(string id) => id is "backroom" or "race" or "dtrh" or "arcademy" or "goon";

    private static BootDecision Decide(string[] args, bool welcomed = true, bool age = true, bool skip = false)
        => LauncherBoot.Decide(args, welcomed, age, skip, Known);

    [Fact]
    public void Bare_launch_opens_the_launcher()
    {
        Assert.Equal(BootSurface.Launcher, Decide(Array.Empty<string>()).Surface);
    }

    [Fact]
    public void Bare_launch_opens_the_panel_when_the_user_asked_for_that()
    {
        Assert.Equal(BootSurface.Panel, Decide(Array.Empty<string>(), skip: true).Surface);
    }

    [Theory]
    [InlineData("--panel")]
    [InlineData("--PANEL")]
    [InlineData("--startup")]
    public void Panel_and_startup_flags_always_win(string flag)
    {
        Assert.Equal(BootSurface.Panel, Decide(new[] { flag }).Surface);
        Assert.Equal(BootSurface.Panel, Decide(new[] { flag, "--game", "backroom" }).Surface);
        Assert.Equal(BootSurface.Panel, Decide(new[] { "--launcher", flag }).Surface);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void A_fresh_install_boots_the_panel_whatever_the_args_say(bool welcomed, bool age)
    {
        Assert.Equal(BootSurface.Panel, Decide(Array.Empty<string>(), welcomed, age).Surface);
        Assert.Equal(BootSurface.Panel, Decide(new[] { "--launcher" }, welcomed, age).Surface);
        Assert.Equal(BootSurface.Panel, Decide(new[] { "--game", "backroom" }, welcomed, age).Surface);
    }

    [Theory]
    [InlineData(new[] { "--game", "backroom" }, "backroom")]
    [InlineData(new[] { "--game=backroom" }, "backroom")]
    [InlineData(new[] { "--game", "BackRoom" }, "backroom")]
    [InlineData(new[] { "--game", "\"race\"" }, "race")]
    [InlineData(new[] { "--verbose", "--game", "goon" }, "goon")]
    public void Game_flag_boots_that_game(string[] args, string expected)
    {
        var d = Decide(args);
        Assert.Equal(BootSurface.Game, d.Surface);
        Assert.Equal(expected, d.GameId);
    }

    [Fact]
    public void Game_flag_beats_the_skip_setting()
    {
        var d = Decide(new[] { "--game", "backroom" }, skip: true);
        Assert.Equal(BootSurface.Game, d.Surface);
    }

    [Theory]
    [InlineData(new object[] { new[] { "--game", "nope" } })]
    [InlineData(new object[] { new[] { "--game" } })]
    [InlineData(new object[] { new[] { "--game", "--verbose" } })]
    [InlineData(new object[] { new[] { "--game=" } })]
    public void Unknown_or_missing_game_id_falls_back_to_the_launcher(string[] args)
    {
        var d = Decide(args);
        Assert.Equal(BootSurface.Launcher, d.Surface);
        Assert.Null(d.GameId);
    }

    [Theory]
    [InlineData("--launcher")]
    [InlineData("--client")]
    public void Launcher_flags_open_the_launcher_even_with_skip_on(string flag)
    {
        Assert.Equal(BootSurface.Launcher, Decide(new[] { flag }, skip: true).Surface);
    }

    [Fact]
    public void NamesASurface_reports_the_three_surface_flags_and_nothing_else()
    {
        Assert.True(LauncherBoot.NamesASurface(new[] { "--panel" }));
        Assert.True(LauncherBoot.NamesASurface(new[] { "--client" }));
        Assert.True(LauncherBoot.NamesASurface(new[] { "--game=dtrh" }));
        Assert.False(LauncherBoot.NamesASurface(new[] { "--verbose", "--play", "x.mp4" }));
        Assert.False(LauncherBoot.NamesASurface(null));
    }

    // ---- catalogue ------------------------------------------------------------------------

    [Fact]
    public void Catalogue_ids_are_unique_lowercase_and_shortcut_safe()
    {
        var ids = LauncherCatalogue.Games.Select(g => g.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var id in ids)
        {
            Assert.Equal(id.ToLowerInvariant(), id);
            Assert.Matches("^[a-z0-9]+$", id);
        }
        Assert.DoesNotContain(LauncherCatalogue.PanelId, ids);
    }

    [Fact]
    public void Catalogue_has_the_games_the_owner_listed_and_none_of_the_panel_features()
    {
        var ids = LauncherCatalogue.Games.Select(g => g.Id).ToHashSet();
        foreach (var must in new[] { "backroom", "race", "dtrh", "arcademy", "goon", "piecebypiece", "intake" })
            Assert.Contains(must, ids);
        foreach (var never in new[] { "remote", "companion", "sessions", "loom", "fyp" })
            Assert.DoesNotContain(never, ids);
    }

    [Fact]
    public void Intake_tile_is_last_available_always_revealed_and_never_a_window()
    {
        var intake = LauncherCatalogue.Find("intake");
        Assert.NotNull(intake);
        Assert.Equal("intake", LauncherCatalogue.Games[^1].Id);
        Assert.True(intake!.Available);
        Assert.Null(intake.IsRevealed);
        Assert.True(intake.Revealed);
        Assert.False(intake.Active);
        Assert.Equal("features/lab_quiz_hero.png", intake.ArtPath);
    }

    [Fact]
    public void Race_tile_has_a_reveal_probe_and_stays_available()
    {
        var race = LauncherCatalogue.Find("race");
        Assert.NotNull(race);
        Assert.NotNull(race!.IsRevealed);
        Assert.True(race.Available);
    }

    [Fact]
    public void Mystery_decision_is_pure_null_reveals_false_hides_throw_hides()
    {
        static LauncherEntry Entry(Func<bool>? revealed) => new("x", "t", "b", null, "?", default,
            () => true, () => false, () => { }, () => false, revealed);
        Assert.True(Entry(null).Revealed);
        Assert.True(Entry(() => true).Revealed);
        Assert.False(Entry(() => false).Revealed);
        Assert.False(Entry(() => throw new InvalidOperationException("probe")).Revealed);
    }

    [Fact]
    public void Find_is_case_insensitive_and_null_safe()
    {
        Assert.NotNull(LauncherCatalogue.Find("BackRoom"));
        Assert.Null(LauncherCatalogue.Find(null));
        Assert.Null(LauncherCatalogue.Find("  "));
        Assert.Null(LauncherCatalogue.Find("nope"));
    }

    [Fact]
    public void Every_tile_has_its_title_and_blurb_in_english()
    {
        var en = English();
        foreach (var g in LauncherCatalogue.Games)
        {
            Assert.True(en.ContainsKey(g.TitleKey), $"en.json lacks {g.TitleKey}");
            Assert.True(en.ContainsKey(g.BlurbKey), $"en.json lacks {g.BlurbKey}");
        }
    }

    [Fact]
    public void Art_paths_point_at_files_that_ship()
    {
        // The art itself lives once at <repo>/Assets and every head LINKS what it needs, so the
        // WPF head's pack URI (Resources/features/x.png) is an ALIAS of Assets/features/x.png --
        // see the <Resource Include="..\Assets\features\*.png" Link="Resources\..."> item. Checking
        // the shared source is what proves the byte ships; checking the head's aliased folder
        // checked a directory that no longer exists on disk.
        var assets = Path.Combine(SourceRoots.RepoRoot, "Assets");
        foreach (var g in LauncherCatalogue.Games.Where(g => g.ArtPath != null))
        {
            var full = Path.Combine(assets, g.ArtPath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"{g.Id} art missing: {full}");
        }
    }

    private static Dictionary<string, string> English()
    {
        var path = Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
    }
}
