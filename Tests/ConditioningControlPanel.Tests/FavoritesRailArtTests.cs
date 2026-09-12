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
    public void Every_destination_ships_with_a_picture()
    {
        // The desk pass on 2026-09-12 asked for a second look at the twelve rooms the first pass
        // left on the emoji, and the repo did ship art for all of them. A row that goes back to
        // null should be a decision somebody argues for, not a drift.
        var glyphOnly = FavoritesRailArt.Map.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
        Assert.True(glyphOnly.Count == 0,
            "destinations still on the emoji fallback: " + string.Join(", ", glyphOnly));
    }

    [Fact]
    public void The_glyph_fallback_still_answers_for_a_row_nobody_listed()
        => Assert.Null(FavoritesRailArt.For("no.such.row"));

    [Fact]
    public void Most_chips_are_cover_art()
    {
        // Plate is for square icon art only. If it ever outgrows the fourteen door rows, the
        // rail has quietly gone back to icons-beside-captions, which is the thing the desk
        // pass rejected.
        var plates = FavoritesRailArt.Map.Values.Count(a => a is { Fit: RailArtFit.Plate });
        var covers = FavoritesRailArt.Map.Values.Count(a => a is { Fit: RailArtFit.Cover });
        Assert.Equal(14, plates);
        Assert.True(covers > plates, "cover art should be the rule and the plate the exception");
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
    public void The_plate_icon_fits_the_chip_it_sits_on()
    {
        // 26 DIP of icon in a 36 DIP chip, with the caption band under it. A plate icon that
        // grew past the chip would crop itself against the rounded border.
        Assert.True(FavoritesRailArt.PlateIconSize < 36, "the plate icon is taller than the chip");
        Assert.True(FavoritesRailArt.PlateIconSize >= 22, "under 22 DIP the plate is an icon beside a caption again");
        // The backdrop is a stretched tiny decode rather than a BlurEffect: it has to be tiny
        // enough to actually blur when it is thrown across 69 DIP.
        Assert.True(FavoritesRailArt.PlateBackdropDecodeWidth <= 16,
            "a backdrop decoded this wide will not read as a blur when stretched over the chip");
    }

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
    public void Plate_art_is_never_given_a_crop_window()
    {
        // A plate shows its icon whole at its own aspect, so a railChip rect for one would be a
        // number nothing reads and a Frame button offered to authors for nothing.
        foreach (var art in FavoritesRailArt.Map.Values.Where(a => a is { Fit: RailArtFit.Plate }))
            Assert.Null(ModArtFramingRegistry.ShippedViewbox(art!.ResourcePath, ModArtFramingRegistry.SurfaceRailChip));
    }

    [Fact]
    public void Every_borrowed_badge_is_cropped_to_its_illustration()
    {
        // The achievement, skill and quest badges carry their own name burned in under the
        // drawing. Without a rect the chip shows a smear of that wordmark behind our caption,
        // so a badge with no railChip row is a bug, not a default.
        var needsARect = FavoritesRailArt.Map.Values
            .Where(a => a is { Fit: RailArtFit.Cover })
            .Select(a => a!.ResourcePath)
            .Where(p => p.StartsWith("achievements/", StringComparison.Ordinal)
                        || p.StartsWith("skills/", StringComparison.Ordinal)
                        || p.StartsWith("quests/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(needsARect);
        foreach (var path in needsARect)
        {
            var rect = ModArtFramingRegistry.ShippedViewbox(path, ModArtFramingRegistry.SurfaceRailChip);
            Assert.True(rect != null, path + " is a badge with no railChip crop - its wordmark will show");
            Assert.True(rect!.Value.Height < 0.95,
                path + " is cropped to the whole image, so the name under the drawing is still in frame");
        }
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

    // ---------------------------------------------------------------- the gesture line

    /// <summary>The rail markup, read from the source tree rather than the copy beside the binary.</summary>
    private static string RailXaml() =>
        File.ReadAllText(Path.Combine(AppDir(), "Views", "Tabs", "SettingsTabView.xaml"));

    [Fact]
    public void The_gesture_line_is_docked_below_the_lists_not_inside_them()
    {
        var xaml = RailXaml();
        var scrollerEnds = xaml.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        var hint = xaml.IndexOf("rail_gesture_hint", StringComparison.Ordinal);

        Assert.True(hint > 0, "the rail markup no longer shows rail_gesture_hint");
        Assert.True(scrollerEnds > 0 && hint > scrollerEnds,
            "rail_gesture_hint sits inside the rail's ScrollViewer, so a full column scrolls it away");
    }

    [Fact]
    public void The_lists_take_the_star_row_and_the_line_the_auto_row()
    {
        // Auto above Auto would let a full column push the line off the bottom; star above Auto
        // pays the line first and gives the lists what is left, which is what the scroller is for.
        var xaml = RailXaml();
        var rail = xaml.Substring(xaml.IndexOf("x:Name=\"FavoritesRail\"", StringComparison.Ordinal));
        rail = rail.Substring(0, rail.IndexOf("VELVET MOSAIC", StringComparison.Ordinal));

        var rows = Regex.Matches(rail, "<RowDefinition Height=\"(\\*|Auto)\"/>")
                        .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "*", "Auto" }, rows);
    }

    /// <summary>A numeric Setter out of one of the rail's TextBlock styles.</summary>
    private static double StyleNumber(string styleKey, string property)
    {
        var xaml = RailXaml();
        var start = xaml.IndexOf("x:Key=\"" + styleKey + "\"", StringComparison.Ordinal);
        Assert.True(start > 0, styleKey + " is gone from SettingsTabView.xaml");
        var block = xaml.Substring(start, xaml.IndexOf("</Style>", start, StringComparison.Ordinal) - start);

        var hit = Regex.Match(block, "Property=\"" + property + "\"\\s+Value=\"([\\d.]+)\"");
        Assert.True(hit.Success, styleKey + " no longer sets " + property);
        return double.Parse(hit.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_gesture_line_is_not_a_footnote()
    {
        // It shipped at 7.5 and the owner sent it back ("bigger text here pls"), so this is the
        // floor rather than a style preference: the line that explains the column may not read
        // smaller than the captions that head it.
        var hint = StyleNumber("RailGestureHint", "FontSize");
        var caption = StyleNumber("RailSectionCaption", "FontSize");

        Assert.True(hint >= caption,
            $"the gesture line is {hint} against a {caption} section caption - it is a footnote again");
        Assert.True(StyleNumber("RailGestureHint", "LineHeight") > hint,
            "the pinned LineHeight is under the font size, which sets the lines on top of each other");
    }

    [Fact]
    public void The_gesture_line_never_hides_itself()
    {
        // It explains the column, so it is not a first-run nudge that gets retired and not a
        // thing to collapse when the lists are empty (that is when it is most wanted).
        var xaml = RailXaml();
        var hint = xaml.IndexOf("rail_gesture_hint", StringComparison.Ordinal);
        var block = xaml.Substring(hint, Math.Min(400, xaml.Length - hint));
        Assert.DoesNotContain("Visibility", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_gesture_line_is_translated_everywhere()
    {
        foreach (var language in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(language);
            Assert.True(file.TryGetValue("rail_gesture_hint", out var line),
                        "rail_gesture_hint is missing from " + language + ".json");
            Assert.False(string.IsNullOrWhiteSpace(line), "rail_gesture_hint is empty in " + language + ".json");
            Assert.DoesNotContain("!", line, StringComparison.Ordinal);
            Assert.DoesNotContain("\u2014", line, StringComparison.Ordinal);  // em-dash
            Assert.DoesNotContain("\u2013", line, StringComparison.Ordinal);  // en-dash
        }
    }

    [Fact]
    public void The_gesture_line_says_what_the_code_does()
    {
        // The owner asked for this with the two buttons the other way round. BuildRailChip wires
        // Click to OpenDestination and AttachPinMenu to the ContextMenu, so left opens and right
        // pins - and the EN line has to agree with that, not with the ask.
        var rail = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.FavoritesRail.cs"));
        Assert.Contains("chip.Click += (_, _) => OpenDestination(entry);", rail, StringComparison.Ordinal);
        Assert.Contains("AttachPinMenu(chip, entry.Id", rail, StringComparison.Ordinal);

        var line = CompanionLocMasters.For("en")["rail_gesture_hint"];
        var left = line.IndexOf("Left-click", StringComparison.OrdinalIgnoreCase);
        var right = line.IndexOf("Right-click", StringComparison.OrdinalIgnoreCase);
        Assert.True(left >= 0 && right > left, "the EN gesture line no longer names both buttons in order");
        Assert.Contains("open", line.Substring(left, right - left), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pin", line.Substring(right), StringComparison.OrdinalIgnoreCase);
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
