using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Banner;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The marquee's Barnum interludes: the two-beat split, the single-bucket draw, the act picker and
/// the cadence. All four are the pure halves of the feature, deliberately kept out of the WPF
/// partial so they can be pinned without a window, a settings service or a dispatcher.
/// </summary>
public class MarqueeReadTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static List<BannerPoolLine> ShippedReads()
    {
        var path = Path.Combine(RepoRoot(), "Assets", "banner", "lines.en.json");
        Assert.True(File.Exists(path), "the shipped banner pool is gone: " + path);
        var pool = JsonConvert.DeserializeObject<BannerPoolFile>(File.ReadAllText(path));
        Assert.NotNull(pool);
        return pool!.Reads;
    }

    // =====================================================================================
    //  1. the two-beat split
    // =====================================================================================

    [Theory]
    // The five tag shapes the pool is authored in, one line each.
    [InlineData("7 days unbroken. You are steadier than anyone credits you for. hm?",
                "7 days unbroken. You are steadier than anyone credits you for.", "hm?")]
    [InlineData("Two weeks without a missed day. You always wanted to be the reliable one. didn't you.",
                "Two weeks without a missed day. You always wanted to be the reliable one.", "didn't you.")]
    [InlineData("Ten sessions in. The first one felt like a test. It was not. hihi",
                "Ten sessions in. The first one felt like a test. It was not.", "hihi")]
    // A "? " break wins when it is the last one: the tag is what lands after the question.
    [InlineData("A full month without a missed day. You do not do things halfway. am I wrong? nah.",
                "A full month without a missed day. You do not do things halfway. am I wrong?", "nah.")]
    [InlineData("41 sessions. Somewhere in there it stopped being new and you kept going. no? sure.",
                "41 sessions. Somewhere in there it stopped being new and you kept going. no?", "sure.")]
    public void The_split_puts_the_closer_on_its_own_beat(string line, string body, string tag)
    {
        var beats = MarqueeReadSplit.Split(line);
        Assert.Equal(body, beats.Body);
        Assert.Equal(tag, beats.Tag);
        Assert.True(beats.HasTag);
    }

    [Fact]
    public void Every_shipped_read_splits_into_a_body_and_a_short_tag()
    {
        var offenders = new List<string>();
        foreach (var line in ShippedReads())
        {
            var beats = MarqueeReadSplit.Split(line.Text);
            if (string.IsNullOrWhiteSpace(beats.Body) || !beats.HasTag
                || MarqueeReadSplit.WordCount(beats.Tag) > 3
                || beats.Tag.Length > 20)
            {
                offenders.Add(line.Id + " -> body='" + beats.Body + "' tag='" + beats.Tag + "'");
            }
        }

        // Every rd_ line is authored to end on a short closer. One that does not would play its
        // second beat as half a sentence, which is the one thing the act cannot carry.
        Assert.True(offenders.Count == 0,
            "these reads do not end on a tag the splitter can land: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void A_line_with_no_break_still_gives_the_second_beat_something()
    {
        var beats = MarqueeReadSplit.Split("you never really log off");
        Assert.Equal("you never really", beats.Body);
        Assert.Equal("log off", beats.Tag);
    }

    [Fact]
    public void A_long_tail_after_the_last_break_falls_back_to_two_words()
    {
        // Five words after the last ". " is a sentence, not a tag: the rule refuses it and takes
        // the last two words instead rather than reading half a clause as the closer.
        var beats = MarqueeReadSplit.Split("Level 12. and then you went and did it again anyway");
        Assert.Equal("Level 12. and then you went and did it", beats.Body);
        Assert.Equal("again anyway", beats.Tag);
    }

    [Fact]
    public void A_line_too_short_to_split_is_all_body()
    {
        Assert.Equal("hm?", MarqueeReadSplit.Split("hm?").Body);
        Assert.False(MarqueeReadSplit.Split("hm?").HasTag);
        Assert.Equal("", MarqueeReadSplit.Split("   ").Body);
    }

    // =====================================================================================
    //  2. the single-bucket draw
    // =====================================================================================

    [Fact]
    public void A_named_bucket_is_the_whole_draw_order()
    {
        var order = BannerPoolService.BuildDrawOrder("reads", "", _ => 0);
        Assert.Equal(new[] { "reads" }, order);

        // No fallback either: an interlude with nothing eligible shows nothing, it never quietly
        // reads out a tagline instead.
        Assert.Single(order);
    }

    [Fact]
    public void The_header_draw_never_reaches_the_reads_bucket()
    {
        // Every roll, including the ones that would have landed on reads when it still carried a
        // weight, and with every possible "last bucket" state behind it.
        foreach (var last in new[] { "", "taglines", "trivia", "reads" })
        {
            for (int roll = 0; roll < 80; roll++)
            {
                var order = BannerPoolService.BuildDrawOrder(null, last, _ => roll);
                Assert.DoesNotContain("reads", order);
                Assert.NotEmpty(order);
                Assert.Equal(order.Distinct().Count(), order.Count);
            }
        }
    }

    [Fact]
    public void The_header_draw_still_offers_both_of_its_own_buckets()
    {
        var seen = new HashSet<string>();
        for (int roll = 0; roll < 80; roll++)
            foreach (var bucket in BannerPoolService.BuildDrawOrder(null, "", _ => roll))
                seen.Add(bucket);

        Assert.Equal(new[] { "taglines", "trivia" }, seen.OrderBy(s => s).ToArray());
    }

    // =====================================================================================
    //  3. the act picker
    // =====================================================================================

    private static readonly List<string> Acts = new()
    {
        MarqueeReadEffects.GlitchHeal,
        MarqueeReadEffects.NumberFirst,
        MarqueeReadEffects.Teletype,
        MarqueeReadEffects.NeonWarmUp,
        MarqueeReadEffects.Redaction,
    };

    [Fact]
    public void The_table_and_the_ids_are_the_same_five_acts()
    {
        var built = MarqueeReadEffects.Build().Select(a => a.Id).ToArray();
        Assert.Equal(Acts.OrderBy(s => s).ToArray(), built.OrderBy(s => s).ToArray());
        Assert.Equal(5, built.Length);
    }

    [Fact]
    public void The_same_act_never_plays_twice_in_a_row()
    {
        var rng = new Random(7);
        string? last = null;
        for (int i = 0; i < 400; i++)
        {
            var pick = MarqueeReadEffects.PickId(Acts, last, _ => true, rng.Next);
            Assert.NotNull(pick);
            Assert.NotEqual(last, pick);
            last = pick;
        }
    }

    [Fact]
    public void An_act_that_cannot_carry_the_line_is_skipped()
    {
        // A line with no number in it: the odometer act declines and the picker moves on.
        for (int roll = 0; roll < 40; roll++)
        {
            var pick = MarqueeReadEffects.PickId(Acts, null,
                                                 id => id != MarqueeReadEffects.NumberFirst, _ => roll);
            Assert.NotEqual(MarqueeReadEffects.NumberFirst, pick);
            Assert.NotNull(pick);
        }
    }

    [Fact]
    public void One_usable_act_plays_even_when_it_just_played()
    {
        // The no-repeat rule yields rather than returning nothing: a blank interlude is worse than
        // the same act twice.
        var pick = MarqueeReadEffects.PickId(Acts, MarqueeReadEffects.Teletype,
                                             id => id == MarqueeReadEffects.Teletype, _ => 0);
        Assert.Equal(MarqueeReadEffects.Teletype, pick);
    }

    [Fact]
    public void Nothing_usable_is_a_skipped_interlude_not_a_crash()
    {
        Assert.Null(MarqueeReadEffects.PickId(Acts, null, _ => false, _ => 0));
        Assert.Null(MarqueeReadEffects.PickId(new List<string>(), null, _ => true, _ => 0));
    }

    // =====================================================================================
    //  4. the cadence
    // =====================================================================================

    private sealed class FakeClock
    {
        public DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        public Func<DateTime> Read => () => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void Nothing_plays_in_the_first_two_minutes_after_launch()
    {
        var clock = new FakeClock();
        var cadence = new MarqueeReadCadence(clock.Read);

        // Four loops are counted, but the launch delay has not run out yet.
        for (int i = 0; i < 4; i++) Assert.False(cadence.NoteLoop());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(cadence.NoteLoop());

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(cadence.NoteLoop());
    }

    [Fact]
    public void The_fourth_loop_is_the_earliest_an_act_can_want_the_stage()
    {
        var clock = new FakeClock();
        var cadence = new MarqueeReadCadence(clock.Read);
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(cadence.NoteLoop());
        Assert.False(cadence.NoteLoop());
        Assert.False(cadence.NoteLoop());
        Assert.True(cadence.NoteLoop());
        Assert.Equal(4, cadence.LoopsSinceRead);
    }

    [Fact]
    public void Three_minutes_have_to_pass_between_acts_however_fast_the_ticker_loops()
    {
        var clock = new FakeClock();
        var cadence = new MarqueeReadCadence(clock.Read);
        clock.Advance(TimeSpan.FromMinutes(5));

        for (int i = 0; i < 4; i++) cadence.NoteLoop();
        Assert.True(cadence.IsDue());
        cadence.NotePlayed();
        Assert.Equal(0, cadence.LoopsSinceRead);

        // A short message loops every few seconds: four more loops arrive long before the floor.
        clock.Advance(TimeSpan.FromSeconds(40));
        for (int i = 0; i < 4; i++) Assert.False(cadence.NoteLoop());

        clock.Advance(TimeSpan.FromSeconds(150));
        Assert.True(cadence.NoteLoop());
    }

    [Fact]
    public void A_skipped_interlude_tries_again_on_the_next_loop()
    {
        var clock = new FakeClock();
        var cadence = new MarqueeReadCadence(clock.Read);
        clock.Advance(TimeSpan.FromMinutes(5));

        for (int i = 0; i < 4; i++) cadence.NoteLoop();
        Assert.True(cadence.IsDue());

        // No eligible read this time. The floor is untouched (nothing was shown), so the next
        // loop asks again rather than waiting out another four.
        cadence.NoteSkipped();
        Assert.True(cadence.NoteLoop());
    }

    [Fact]
    public void Fast_mode_collapses_all_three_rules_to_twenty_seconds()
    {
        var clock = new FakeClock();
        var cadence = new MarqueeReadCadence(clock.Read) { Fast = true };

        Assert.False(cadence.NoteLoop());        // one loop, but not 20s after launch yet
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(cadence.NoteLoop());
        cadence.NotePlayed();

        Assert.False(cadence.NoteLoop());        // floor
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(cadence.NoteLoop());
    }
}
