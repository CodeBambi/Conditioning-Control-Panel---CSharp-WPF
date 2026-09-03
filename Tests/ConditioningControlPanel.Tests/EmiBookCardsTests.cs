using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// HER BOOK, flipbook edition: the card deck, on everything about it that fails in silence.
///
/// <para>The book's whole premise is that a card can be taken in at a glance. Nothing enforces that
/// at runtime - a card with a paragraph in it renders perfectly happily, just badly - so the word
/// ceiling is a test or it is nothing. The same goes for the four string boundaries a card carries:
/// a card id that no painter answers to draws a blank stage, a target id that
/// <see cref="EmiTargets"/> does not know ghosts a button forever, a <c>TutorialType</c> NAME that
/// does not parse logs once and does nothing, and a loc key that is not in <c>en.json</c> silently
/// falls back to the English literal so the card looks right and the other eight languages never
/// get their chance. None of those throw.</para>
///
/// <para><b>What is deliberately not touched here</b>, the same rule <see cref="EmiCodexTests"/>
/// keeps: <see cref="EmiState"/> reads and writes the real user's <c>emi-desk.json</c>, so nothing
/// below goes near the bookmark or the open counter.</para>
/// </summary>
public class EmiBookCardsTests
{
    /// <summary>The ceiling, in words of body text. Gist plus nudges plus catch; the title is a
    /// label rather than prose and does not count against it. Emphasis markup is stripped first, so
    /// a writer never pays a word for a pair of asterisks.</summary>
    private const int WordCeiling = 70;

    /// <summary>The owner's ceiling on bullets, 2026-08-30. The catch is not one of them.</summary>
    private const int NudgeCeiling = 4;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static Dictionary<string, string> English()
    {
        var path = Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString() ?? string.Empty;
        return d;
    }

    private static int Words(string s) =>
        s.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

    // =====================================================================================
    //  the shape of a card
    // =====================================================================================

    [Fact]
    public void The_deck_is_not_empty()
    {
        Assert.NotEmpty(EmiBookCards.All);
        Assert.True(EmiBook.HasContent);
    }

    /// <summary>
    /// THE CEILING. It was forty while a card carried two nudges, and the first draft at that size
    /// drew the owner's verdict: "only 3 lines and are basically useless here. No idea what the
    /// feature does and why people should care" (2026-08-30). Forty words is not enough to say what
    /// a feature IS and what you can do with it, so the budget moved to seventy - which is still
    /// about what somebody reads while a six second loop plays twice, and still nowhere near the
    /// chapter this replaced. The bullet ceiling below is what keeps it a card.
    /// </summary>
    [Fact]
    public void Every_card_stays_under_the_word_ceiling()
    {
        foreach (var c in EmiBookCards.All)
        {
            int n = Words(EmiBookText.Strip(c.GistEn))
                  + c.NudgesEn.Sum(x => Words(EmiBookText.Strip(x)))
                  + Words(EmiBookText.Strip(c.CatchEn));
            Assert.True(n <= WordCeiling, $"card '{c.Id}' carries {n} words of body text, ceiling is {WordCeiling}");
        }
    }

    [Fact]
    public void Card_ids_are_unique_and_url_safe()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in EmiBookCards.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.True(seen.Add(c.Id), $"duplicate card id '{c.Id}'");
            // The id is the bookmark value, the painter key and a loc stem's neighbour. Keeping it
            // to lowercase and hyphens means none of those three can ever need escaping.
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", c.Id);
        }
    }

    [Fact]
    public void Every_card_sits_on_a_real_tab()
    {
        foreach (var c in EmiBookCards.All)
            Assert.InRange(c.Tab, 0, EmiBookCards.TabKeys.Count - 1);
    }

    /// <summary>
    /// Reading order groups by tab. The pager walks the whole book in a straight line while the
    /// dots only count the current tab, so a deck that interleaved tabs would show a pager step
    /// that changes the tab underneath the reader and resets the dot count mid-chapter.
    /// </summary>
    [Fact]
    public void Cards_are_grouped_by_tab_in_reading_order()
    {
        var order = EmiBookCards.All.Select(c => c.Tab).ToList();
        var grouped = order.Distinct().ToList();
        Assert.Equal(grouped.Count, order.Distinct().Count());
        for (int i = 1; i < order.Count; i++)
            if (order[i] != order[i - 1])
                Assert.DoesNotContain(order[i], order.Take(i - 1));
    }

    /// <summary>
    /// FOUR BULLETS, and the catch is not one of them. The card is a glance, and a fifth bullet is
    /// the point where a reader stops glancing and starts skimming - which on a panel this size
    /// means reading none of it. The catch keeps its own strip at the foot of the card, so raising
    /// this number is not the way to fit one more honest sentence in.
    /// </summary>
    [Fact]
    public void Four_nudges_at_most_and_a_catch_that_is_never_blank()
    {
        foreach (var c in EmiBookCards.All)
        {
            Assert.InRange(c.NudgesEn.Count, 1, NudgeCeiling);
            Assert.All(c.NudgesEn, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            // The catch is the honest bit and the one thing on the card that is never optional.
            Assert.False(string.IsNullOrWhiteSpace(c.CatchEn), $"card '{c.Id}' has no catch");
        }
    }

    // =====================================================================================
    //  the four string boundaries
    // =====================================================================================

    /// <summary>A card with no painter draws a blank stage, which is the one failure the reader
    /// definitely notices and the log definitely does not mention.</summary>
    [Fact]
    public void Every_card_has_a_demo_and_the_demo_agrees_about_its_id()
    {
        foreach (var c in EmiBookCards.All)
        {
            var p = EmiBookDemos.For(c.Id);
            Assert.True(p != null, $"card '{c.Id}' has no demo painter");
            Assert.Equal(c.Id, p!.Id);
        }
    }

    /// <summary>
    /// The side rail draws one icon per card on the tab, so a card the glyph table forgot arrives as
    /// a chip with a coloured dot in it - navigable, but the only page in the rail whose destination
    /// is not named. The renderer falls back on purpose rather than dropping the chip; this is the
    /// test that says the fallback should never actually fire.
    /// </summary>
    [Fact]
    public void Every_card_has_a_rail_glyph()
    {
        var ids = new HashSet<string>(EmiBookGlyphs.Ids, StringComparer.Ordinal);
        foreach (var c in EmiBookCards.All)
            Assert.True(ids.Contains(c.Id), $"card '{c.Id}' has no rail glyph");
    }

    /// <summary>A glyph for an id no card carries is a drawing nobody will ever see, and usually the
    /// wreckage of a renamed card.</summary>
    [Fact]
    public void No_rail_glyph_is_orphaned()
    {
        var cards = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in EmiBookCards.All) cards.Add(c.Id);
        foreach (var id in EmiBookGlyphs.Ids)
            Assert.True(cards.Contains(id), $"glyph '{id}' belongs to no card");
    }

    /// <summary>Four to seven seconds. Under four a loop is a flicker nobody can follow; over seven
    /// the reader has finished the words and is watching an animation instead of using the app.</summary>
    [Fact]
    public void Every_loop_is_a_sentence_long()
    {
        foreach (var c in EmiBookCards.All)
        {
            var p = EmiBookDemos.For(c.Id)!;
            Assert.InRange(p.LoopMs, 4000, 7000);
            // The reduced-motion still has to be a frame the loop actually reaches.
            Assert.InRange(p.StillMs, 0, p.LoopMs - 1);
        }
    }

    /// <summary>A target id EmiTargets does not know ghosts TAKE ME THERE with no explanation.</summary>
    [Fact]
    public void Every_take_me_there_points_at_a_real_door()
    {
        foreach (var c in EmiBookCards.All.Where(c => c.Target != null))
            Assert.True(EmiTargets.Find(c.Target) != null,
                $"card '{c.Id}' points at target '{c.Target}', which is not in the catalogue");
    }

    /// <summary>
    /// A NAME, never an ordinal. The tour ledger persists names, and an ordinal would move the day
    /// somebody inserted a value into the middle of <c>TutorialType</c> - which is the same reason
    /// <c>EmiOffers.TourNameOf</c> maps to names.
    /// </summary>
    [Fact]
    public void Every_walk_me_through_it_names_a_real_tour()
    {
        foreach (var c in EmiBookCards.All.Where(c => c.Tour != null))
            Assert.True(Enum.TryParse<TutorialType>(c.Tour, out _),
                $"card '{c.Id}' names tour '{c.Tour}', which is not a TutorialType");
    }

    /// <summary>One button at most. Two would make the card a menu.</summary>
    [Fact]
    public void No_card_offers_both_a_door_and_a_tour()
    {
        foreach (var c in EmiBookCards.All)
            Assert.False(c.Target != null && c.Tour != null, $"card '{c.Id}' offers two buttons");
    }

    // =====================================================================================
    //  her voice
    // =====================================================================================

    /// <summary>
    /// Her line rules, stated in the primer and broken silently by anything that just reads well in
    /// a source file: lowercase, one thought, sixty characters. The margin quip sits beside a panel
    /// that is already doing the explaining, so a margin line that explains anything is a bug in
    /// the writing.
    /// </summary>
    [Fact]
    public void Margin_lines_keep_her_voice()
    {
        foreach (var c in EmiBookCards.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.MarginEn), $"card '{c.Id}' has no margin line");
            Assert.True(c.MarginEn.Length <= 60,
                $"card '{c.Id}' margin line is {c.MarginEn.Length} characters, ceiling is 60");
            Assert.True(c.MarginEn == c.MarginEn.ToLowerInvariant(),
                $"card '{c.Id}' margin line is not lowercase: '{c.MarginEn}'");
        }
    }

    /// <summary>A face she has never worn renders as literal text in the bubble.</summary>
    [Fact]
    public void Margin_faces_come_from_the_shipped_set()
    {
        var lines = File.ReadAllText(Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json"));
        foreach (var c in EmiBookCards.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.MarginFace));
            Assert.Contains(c.MarginFace, lines, StringComparison.Ordinal);
        }
    }

    // =====================================================================================
    //  the copy
    // =====================================================================================

    /// <summary>
    /// The English literals in the record are FALLBACKS, not the copy. A missing key renders the
    /// literal, so the card looks perfect on this machine and no translator ever sees the string.
    /// </summary>
    [Fact]
    public void Every_card_key_is_in_english_and_matches_its_literal()
    {
        var en = English();
        foreach (var c in EmiBookCards.All)
        {
            void Check(string suffix, string literal)
            {
                var key = c.KeyStem + suffix;
                Assert.True(en.ContainsKey(key), $"en.json is missing '{key}'");
                Assert.Equal(literal, en[key]);
            }

            Check("_title", c.TitleEn);
            Check("_gist", c.GistEn);
            Check("_catch", c.CatchEn);
            for (int i = 0; i < c.NudgesEn.Count; i++) Check($"_nudge{i + 1}", c.NudgesEn[i]);
        }
    }

    /// <summary>The chrome the window paints from loc rather than from a literal.</summary>
    [Fact]
    public void Every_chrome_key_is_in_english()
    {
        var en = English();
        var keys = new List<string>
        {
            "emi_book_catch_label", "emi_book_go", "emi_book_walk", "emi_book_close", "emi_book_stage",
        };
        keys.AddRange(EmiBookCards.TabKeys.Select(k => "emi_book_tab_" + k));

        foreach (var k in keys) Assert.True(en.ContainsKey(k), $"en.json is missing '{k}'");
    }

    /// <summary>
    /// A tab with no cards behind it is drawn dead rather than hidden, which is only honest if the
    /// tab list and the deck agree about which those are. Wave A ships START and TOOLS.
    /// </summary>
    [Fact]
    public void Tab_names_resolve_and_at_least_one_tab_is_populated()
    {
        for (int i = 0; i < EmiBookCards.TabKeys.Count; i++)
        {
            var name = EmiBookCards.TabName(i);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.DoesNotContain("emi_book_tab_", name, StringComparison.Ordinal);
        }
        Assert.True(EmiBookCards.TabHasCards(0));
        Assert.Equal(0, EmiBookCards.FirstOnTab(0));
        Assert.Equal(-1, EmiBookCards.FirstOnTab(EmiBookCards.TabKeys.Count + 3));
    }

    [Fact]
    public void Index_lookup_answers_for_every_card_and_refuses_everything_else()
    {
        for (int i = 0; i < EmiBookCards.All.Count; i++)
            Assert.Equal(i, EmiBookCards.IndexOf(EmiBookCards.All[i].Id));

        Assert.Equal(-1, EmiBookCards.IndexOf(null));
        Assert.Equal(-1, EmiBookCards.IndexOf("   "));
        Assert.Equal(-1, EmiBookCards.IndexOf("no-such-card"));
    }

    // =====================================================================================
    //  the painters
    // =====================================================================================

    /// <summary>
    /// Every painter, walked across its whole loop. A demo throws on the UI thread thirty times a
    /// second, and while the window drops a painter that throws rather than flooding the log, a
    /// dropped painter is a blank stage the reader is never told about.
    /// </summary>
    [Fact]
    public void Every_demo_paints_its_whole_loop_without_throwing()
    {
        var canvas = new EmiPixelCanvas(96, 72);
        foreach (var c in EmiBookCards.All)
        {
            var p = EmiBookDemos.For(c.Id)!;
            for (int step = 0; step <= 120; step++)
            {
                double t = p.LoopMs * (step / 120.0);
                var ex = Record.Exception(() => p.Draw(canvas, Math.Min(t, p.LoopMs - 1)));
                Assert.True(ex == null, $"demo '{p.Id}' threw at t={t:F0}ms: {ex}");
            }
        }
    }

    /// <summary>
    /// The buffer refuses coordinates outside itself rather than tearing into the next row. Every
    /// painter draws in cell space with hand-written offsets, so an off-by-one is a matter of when,
    /// not whether, and the failure mode without this clamp is a pixel appearing on the wrong line.
    /// </summary>
    [Fact]
    public void The_canvas_clips_instead_of_wrapping()
    {
        var canvas = new EmiPixelCanvas(96, 72);
        var ex = Record.Exception(() =>
        {
            canvas.Px(-4, -4, EmiPix.Pink);
            canvas.Px(4000, 4000, EmiPix.Pink);
            canvas.Rect(-40, -40, 400, 400, EmiPix.Navy);
            canvas.Line(-99, -99, 999, 999, EmiPix.Cream);
            canvas.RectA(90, 68, 40, 40, EmiPix.Gold, 0.5);
            canvas.Commit();
        });
        Assert.Null(ex);
    }
}
