using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The nav rework's search promises (2026-10-06): old names still find their new home with a
/// "(was X)" hint, the new pills and every game and Studio module have a row, and a query that
/// finds nothing gets a nearest guess instead of a dead end.
/// </summary>
public class PaletteSynonymTests
{
    [Fact]
    public void Vault_finds_Account_and_Plans_with_the_was_hint()
    {
        var hits = SettingsPaletteIndex.Search("vault");
        var account = hits.FirstOrDefault(e => e.Id == "section.account");
        Assert.NotNull(account);
        Assert.Equal("appsettings", account!.TabKey);
        Assert.Equal("account", account.SectionKey);
        Assert.False(string.IsNullOrEmpty(SettingsPaletteIndex.WasHint(account, "vault")));
    }

    [Theory]
    [InlineData("premium", "section.account")]
    [InlineData("exclusives", "section.account")]
    [InlineData("velvet vault", "section.account")]
    [InlineData("lab", "tab.play")]
    [InlineData("effects rack", "tab.studio")]
    [InlineData("available subjects", "tab.availablesubjects")]
    [InlineData("together", "tab.availablesubjects")]
    public void Old_names_reach_their_new_home(string query, string expectedId)
    {
        var hits = SettingsPaletteIndex.Search(query);
        Assert.True(hits.Any(e => e.Id == expectedId),
            $"\"{query}\" does not offer {expectedId} (got: " + string.Join(", ", hits.Take(6).Select(e => e.Id)) + ")");
    }

    [Fact]
    public void A_caption_hit_carries_no_was_hint()
    {
        var studio = SettingsPaletteIndex.All.First(e => e.Id == "tab.studio");
        Assert.Null(SettingsPaletteIndex.WasHint(studio, "studio"));
        Assert.Null(SettingsPaletteIndex.WasHint(studio, ""));
    }

    [Fact]
    public void The_retired_exclusives_row_stays_registered_but_leaves_search()
    {
        Assert.Contains(SettingsPaletteIndex.All, e => e.Id == "tab.exclusives");
        Assert.DoesNotContain(SettingsPaletteIndex.Search("premium"), e => e.Id == "tab.exclusives");
        Assert.DoesNotContain(SettingsPaletteIndex.Search(""), e => e.Id == "tab.exclusives");
    }

    [Theory]
    [InlineData("chess", "game.piecebypiece")]
    [InlineData("casino", "game.backroom")]
    [InlineData("slots", "game.backroom")]
    [InlineData("brick", "game.breakout")]
    [InlineData("racing", "game.race")]
    [InlineData("rabbit hole", "game.dtrh")]
    [InlineData("monitor", "section.monitors")]
    [InlineData("second monitor", "section.monitors")]
    [InlineData("assets path", "tab.folders")]
    [InlineData("lock cards", "tab.permissions")]
    [InlineData("hypnotube", "tab.companionlinks")]
    [InlineData("leash", "tab.leash")]
    [InlineData("friends", "tab.friends")]
    [InlineData("lobby", "tab.availablesubjects")]
    [InlineData("open tables", "tab.availablesubjects")]
    [InlineData("leaderboard", "tab.leaderboard")]
    [InlineData("bubble pop", "rack.bubbles")]
    [InlineData("brain drain", "rack.braindrain")]
    [InlineData("scheduler", "tab.ramp")]
    public void New_rows_are_findable_by_the_words_people_type(string query, string expectedId)
    {
        var hits = SettingsPaletteIndex.Search(query);
        Assert.True(hits.Any(e => e.Id == expectedId),
            $"\"{query}\" does not offer {expectedId} (got: " + string.Join(", ", hits.Take(6).Select(e => e.Id)) + ")");
    }

    [Fact]
    public void Every_launcher_game_has_a_row_that_launches_it()
    {
        foreach (var id in new[] { "backroom", "race", "dtrh", "arcademy", "goon", "piecebypiece",
                                   "breakout", "breakoutdemo", "intake" })
        {
            var row = SettingsPaletteIndex.All.FirstOrDefault(e => e.Id == "game." + id);
            Assert.NotNull(row);
            Assert.Equal(id, row!.GameId);
            Assert.True(string.IsNullOrEmpty(row.TabKey), $"game.{id} would navigate instead of launching");
            Assert.NotNull(Services.Launcher.LauncherCatalogue.Find(id));
        }
    }

    [Fact]
    public void Every_rack_module_is_reachable_from_search()
    {
        var keys = new[] { "flash", "video", "subliminal", "spiral", "pinkfilter", "visuals", "bubbles",
                           "bubblecount", "lockcard", "bouncingtext", "mindwipe", "braindrain" };
        foreach (var key in keys)
        {
            var row = SettingsPaletteIndex.All.FirstOrDefault(e => e.RackKey == key);
            Assert.True(row != null, "no palette row opens the rack module " + key);
            Assert.Equal("studio", row!.TabKey);
            // The form labels carry their own emoji; the palette draws the glyph, so the caption drops it.
            Assert.True(row.Label.Length == 0 || char.IsLetterOrDigit(row.Label[0]) || row.Label == row.LabelKey,
                $"rack.{key} caption still starts with a glyph: {row.Label}");
        }
        // Haptics and Scheduler & Ramp are pills with rows of their own.
        Assert.Contains(SettingsPaletteIndex.All, e => e.Id == "tab.haptics");
        Assert.Contains(SettingsPaletteIndex.All, e => e.Id == "tab.ramp" && e.TabKey == "ramp");
    }

    [Theory]
    [InlineData("monitr")]
    [InlineData("leaderbord")]
    [InlineData("chesss")]
    public void Zero_hits_give_a_nearest_suggestion(string typo)
    {
        Assert.Empty(SettingsPaletteIndex.Search(typo));
        var near = SettingsPaletteIndex.Nearest(typo);
        Assert.NotNull(near);
        Assert.False(string.IsNullOrWhiteSpace(near!.Value.Term));
    }

    [Fact]
    public void Nonsense_gets_no_suggestion()
    {
        Assert.Null(SettingsPaletteIndex.Nearest("qzxwv"));
        Assert.Null(SettingsPaletteIndex.Nearest("ab"));
    }

    [Fact]
    public void Show_all_pages_lists_pages_not_settings()
    {
        var pages = SettingsPaletteIndex.AllPages();
        Assert.Contains(pages, e => e.Id == "section.monitors");
        Assert.Contains(pages, e => e.Id == "tab.friends");
        Assert.DoesNotContain(pages, e => e.Id.StartsWith("set.", StringComparison.Ordinal));
    }

    [Fact]
    public void Recents_keep_five_newest_first_without_duplicates()
    {
        string json = "";
        foreach (var id in new[] { "tab.quests", "tab.friends", "game.backroom", "tab.quests",
                                   "section.monitors", "rack.spiral", "tab.leash" })
            json = SettingsPaletteIndex.PushRecent(json, id);

        var ids = SettingsPaletteIndex.ReadRecents(json);
        Assert.Equal(new[] { "tab.leash", "rack.spiral", "section.monitors", "tab.quests", "game.backroom" }, ids);
        Assert.Empty(SettingsPaletteIndex.ReadRecents("not json"));
        Assert.DoesNotContain(SettingsPaletteIndex.Recents("[\"no.such.row\"]"), e => e == null);
    }

    [Fact]
    public void Distance_is_plain_levenshtein()
    {
        Assert.Equal(0, SettingsPaletteIndex.Distance("lobby", "lobby"));
        Assert.Equal(1, SettingsPaletteIndex.Distance("monitr", "monitor"));
        Assert.Equal(3, SettingsPaletteIndex.Distance("kitten", "sitting"));
    }
}
