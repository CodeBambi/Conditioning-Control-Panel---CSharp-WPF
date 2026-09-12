using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rail chips wear the feature's own picture since 2026-09-12 (owner ask). Two things can rot
/// silently and neither fails a build: a palette row added with no art decision, so a chip quietly
/// keeps an emoji nobody chose for it; and an art path that is right on disk but missing from the
/// csproj, which makes the pack:// URI fail at RUNTIME only - the exact trap the csproj's own
/// comment about the intake cards warns about.
/// </summary>
public class FavoritesRailArtTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static List<string> DestinationIds() =>
        SettingsPaletteIndex.All.Select(e => e.Id).Where(FavoritesRailRule.IsDestination).ToList();

    // ---------------------------------------------------------------- the table

    [Fact]
    public void Every_pinnable_destination_has_an_art_decision()
    {
        var missing = DestinationIds().Where(id => !FavoritesRailArt.Knows(id)).ToList();
        Assert.True(missing.Count == 0,
            "palette destinations with no row in FavoritesRailArt (add a picture, or null for the "
            + "glyph fallback): " + string.Join(", ", missing));
    }

    [Fact]
    public void The_table_names_no_destination_the_palette_dropped()
    {
        var live = new HashSet<string>(DestinationIds(), StringComparer.Ordinal);
        var stale = FavoritesRailArt.Map.Keys.Where(k => !live.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "FavoritesRailArt rows with no palette destination: " + string.Join(", ", stale));
    }

    [Fact]
    public void Some_destinations_keep_the_glyph_on_purpose()
    {
        // The fallback is a decision, not an empty table: if this ever reads zero, someone has
        // given every row a picture and the glyph path below is dead code nobody is testing.
        Assert.Contains(FavoritesRailArt.Map, kv => kv.Value == null);
        Assert.Null(FavoritesRailArt.For("card.arcademy"));
        Assert.Null(FavoritesRailArt.For("no.such.row"));
    }

    // ---------------------------------------------------------------- the files

    [Fact]
    public void Every_art_path_exists_on_disk()
    {
        foreach (var path in FavoritesRailArt.ArtPaths())
        {
            var full = Path.Combine(AppDir(), "Resources", path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"FavoritesRailArt names {path}, which is not under Resources/");
        }
    }

    [Fact]
    public void Every_art_path_ships_as_a_pack_Resource()
    {
        // There is no Resources\**\*.png glob in this project (the csproj says so where the intake
        // cards are listed), so a file present on disk but absent from the csproj resolves to
        // nothing at runtime and the chip silently falls back. Accept either the folder glob the
        // features and nav art use or an explicit per-file entry.
        var csproj = File.ReadAllText(Path.Combine(AppDir(), "ConditioningControlPanel.csproj"));
        foreach (var path in FavoritesRailArt.ArtPaths())
        {
            var win = path.Replace('/', '\\');
            var dir = win.Contains('\\') ? win.Substring(0, win.LastIndexOf('\\')) : null;
            var glob = dir == null ? null : $"Resources\\{dir}\\*.png";
            bool covered = csproj.Contains($"\"Resources\\{win}\"", StringComparison.OrdinalIgnoreCase)
                           || (glob != null && csproj.Contains(glob, StringComparison.OrdinalIgnoreCase));
            Assert.True(covered, $"{path} is on disk but no <Resource> item in the csproj covers it");
        }
    }

    // ---------------------------------------------------------------- framing

    [Fact]
    public void Cover_art_is_framed_by_the_rail_chip_surface()
    {
        var chip = ModArtFramingRegistry.FindSurface(ModArtFramingRegistry.SurfaceRailChip);
        Assert.NotNull(chip);

        // The six chips the premium rail hand-tuned are still framed by their own rects rather
        // than blind-centre-cropped; that is the whole reason those rows survived the rail.
        foreach (var path in new[] { "features/takeover.png", "features/awareness.png", "features/vibe.png",
                                     "features/lab_quiz_hero.png", "features/remote_control.png", "features/fyp.png" })
        {
            Assert.Contains(FavoritesRailArt.Map.Values, a => a != null && a.ResourcePath == path);
            Assert.NotNull(ModArtFramingRegistry.ShippedViewbox(path, ModArtFramingRegistry.SurfaceRailChip));
        }
    }

    [Fact]
    public void Icon_art_is_never_given_a_crop_window()
    {
        // A 64x64 medallion is drawn centred at its own aspect, so a railChip rect for one would
        // be a number nothing reads and a Frame button offered to authors for nothing.
        foreach (var art in FavoritesRailArt.Map.Values.Where(a => a is { Fit: RailArtFit.Icon }))
            Assert.Null(ModArtFramingRegistry.ShippedViewbox(art!.ResourcePath, ModArtFramingRegistry.SurfaceRailChip));
    }

    // ---------------------------------------------------------------- mod art

    [Theory]
    [InlineData("builtin-bambisleep", "_bambi")]
    [InlineData("builtin-sissyhypno", "_sissy")]
    [InlineData("drone-mode", "_drone")]
    [InlineData("builtin-locked", "_locked")]
    [InlineData("some-third-party-mod", null)]
    [InlineData(null, null)]
    public void The_theme_suffix_matches_the_mosaics(string? modId, string? expected)
        => Assert.Equal(expected, FavoritesRailArt.ThemeSuffix(modId));

    [Fact]
    public void A_themed_fork_is_tried_before_the_base_path()
    {
        Assert.Equal(new[] { "features/vault_bambi.png", "features/vault.png" },
                     FavoritesRailArt.Candidates("features/vault.png", "_bambi"));
        Assert.Equal(new[] { "nav/door_home_drone.png", "nav/door_home.png" },
                     FavoritesRailArt.Candidates("nav/door_home.png", "_drone"));
        Assert.Equal(new[] { "lockdown_icon_locked.png", "lockdown_icon.png" },
                     FavoritesRailArt.Candidates("lockdown_icon.png", "_locked"));
    }

    [Fact]
    public void With_no_theme_there_is_exactly_one_candidate()
    {
        Assert.Equal(new[] { "features/vault.png" }, FavoritesRailArt.Candidates("features/vault.png", null));
        Assert.Equal(new[] { "features/vault.png" }, FavoritesRailArt.Candidates("features/vault.png", ""));
        Assert.Empty(FavoritesRailArt.Candidates("", "_bambi"));
    }

    [Fact]
    public void The_shipped_vault_faces_are_reachable_through_the_suffix()
    {
        // The one row that actually has app-shipped themed art today. If these files are ever
        // renamed the chip goes back to the default face with no other tell.
        foreach (var suffix in new[] { "_bambi", "_sissy", "_drone", "_locked" })
        {
            var themed = FavoritesRailArt.Candidates("features/vault.png", suffix)[0];
            Assert.True(File.Exists(Path.Combine(AppDir(), "Resources", themed.Replace('/', Path.DirectorySeparatorChar))),
                        themed + " is missing, so the themed vault chip silently falls back");
        }
    }

    // ---------------------------------------------------------------- decode caps

    [Theory]
    [InlineData(1.0, FavoritesRailArt.BaseDecodeWidth)]
    [InlineData(0.5, 2 * FavoritesRailArt.BaseDecodeWidth)]
    [InlineData(0.25, 4 * FavoritesRailArt.BaseDecodeWidth)]
    public void A_tighter_crop_asks_for_more_pixels(double viewboxWidth, int expected)
        => Assert.Equal(expected, FavoritesRailArt.DecodeWidthFor(viewboxWidth));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2.0)]
    public void A_nonsense_crop_width_falls_back_to_the_base_cap(double viewboxWidth)
        => Assert.Equal(FavoritesRailArt.BaseDecodeWidth, FavoritesRailArt.DecodeWidthFor(viewboxWidth));

    [Fact]
    public void The_decode_cap_never_runs_away()
        => Assert.Equal(FavoritesRailArt.MaxDecodeWidth, FavoritesRailArt.DecodeWidthFor(0.001));

    // ---------------------------------------------------------------- the column

    /// <summary>
    /// The chrome the two lists sit in: 6+6 of stack margin, then a 9pt bold caption plus its 3px
    /// gap for FAVORITES, and the same for RECENT with 4 more above it. Mirrors the sum written
    /// over RailChipStyle in SettingsTabView.xaml.
    /// </summary>
    private const double Chrome = 12 + 15 + 19;

    /// <summary>What eight favorites and FIVE recent cost at the old 69x42 chip on a 4px gap.</summary>
    private const double OldFullColumn = Chrome + 13 * 46;

    private static (double Height, double Gap) ChipBox()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "Tabs", "SettingsTabView.xaml"));
        var start = xaml.IndexOf("x:Key=\"RailChipStyle\"", StringComparison.Ordinal);
        Assert.True(start > 0, "RailChipStyle not found in SettingsTabView.xaml");
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        var block = xaml.Substring(start, end - start);

        var height = Regex.Match(block, @"Property=""Height""\s+Value=""([\d.]+)""");
        var margin = Regex.Match(block, @"Property=""Margin""\s+Value=""0,0,0,([\d.]+)""");
        Assert.True(height.Success, "RailChipStyle no longer sets a fixed Height - the column sum cannot be checked");
        Assert.True(margin.Success, "RailChipStyle no longer sets a 0,0,0,n Margin - the column sum cannot be checked");
        return (double.Parse(height.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(margin.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Both_lists_full_fit_in_what_the_column_already_held()
    {
        var (height, gap) = ChipBox();
        var full = Chrome + (FavoritesRailRule.FavoritesCap + FavoritesRailRule.RecentCap) * (height + gap);

        Assert.True(full <= OldFullColumn,
            $"8 favorites + {FavoritesRailRule.RecentCap} recent measure {full} DIP at a {height}+{gap} chip, "
            + $"past the {OldFullColumn} the column already held at eight and five. Shrink the chip or the caps.");
    }

    [Fact]
    public void The_chip_is_still_tall_enough_to_carry_a_picture_and_a_caption()
    {
        var (height, gap) = ChipBox();
        Assert.True(height >= 32, "a chip under 32 DIP has no room for art above an 8.5pt caption");
        Assert.True(gap >= 2, "chips need a visible gap or the column reads as one striped block");
    }

    [Fact]
    public void The_rail_chip_surface_matches_the_chip_the_xaml_draws()
    {
        var (height, _) = ChipBox();
        var surface = ModArtFramingRegistry.FindSurface(ModArtFramingRegistry.SurfaceRailChip)!;
        Assert.Equal(69.0 / height, surface.AspectRatio, 3);
    }
}
