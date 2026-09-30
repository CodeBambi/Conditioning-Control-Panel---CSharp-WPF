using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>en.json, for the "is this key real" assertions. A page that composes a key at runtime
/// cannot be caught by the compiler, so it is caught here.</summary>
internal static class ChasterPageLoc
{
    public static JObject En()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        return JObject.Parse(File.ReadAllText(
            Path.Combine(root, "ConditioningControlPanel", "Localization", "Languages", "en.json")));
    }
}

/// <summary>
/// The three ready-made price sets. A preset is the first thing anyone presses on the page, so it
/// has to be a real set of real rows: nothing invented, nothing that cannot be priced, and the
/// three must be distinguishable from each other or the choice is theatre.
/// </summary>
public class TabPresetsTests
{
    [Fact]
    public void There_are_three_presets_and_every_id_in_them_is_a_real_price_row()
    {
        Assert.Equal(3, TabPresets.All.Count);
        Assert.Equal(new[] { TabPresets.Gentle, TabPresets.Strict, TabPresets.Circe },
            TabPresets.All.Select(p => p.Id));
        foreach (var preset in TabPresets.All)
        {
            Assert.NotEmpty(preset.PriceIds);
            Assert.All(preset.PriceIds, id => Assert.True(TabPrices.Find(id) != null || TabPrices.Modifiers.Contains(id)));
            Assert.Equal(preset.PriceIds.Count, preset.PriceIds.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void No_preset_can_put_a_price_on_the_way_out()
    {
        foreach (var preset in TabPresets.All)
            Assert.All(preset.PriceIds, id => Assert.DoesNotContain(id, TabPrices.NeverPriced));
    }

    [Fact]
    public void Gentle_leans_down_and_strict_leans_up_and_both_keep_a_way_the_other_way()
    {
        var gentle = TabPresets.Find(TabPresets.Gentle)!.PriceIds.Where(id => !TabPrices.Modifiers.Contains(id)).Select(id => TabPrices.Find(id)!).ToList();
        var strict = TabPresets.Find(TabPresets.Strict)!.PriceIds.Where(id => !TabPrices.Modifiers.Contains(id)).Select(id => TabPrices.Find(id)!).ToList();

        Assert.True(gentle.Sum(p => p.Seconds) < 0, "Gentle must add up to time coming off");
        Assert.True(strict.Sum(p => p.Seconds) > 0, "Strict must add up to time going on");
        // Neither is a one-way street: a set with no way back is a countdown, not a game.
        Assert.Contains(gentle, p => p.Seconds > 0);
        Assert.Contains(strict, p => p.Seconds < 0);
    }

    [Fact]
    public void Circes_choice_needs_no_tier_and_no_sparkles()
    {
        var circe = TabPresets.Find(TabPresets.Circe)!.PriceIds.Where(id => !TabPrices.Modifiers.Contains(id)).Select(id => TabPrices.Find(id)!).ToList();
        Assert.All(circe, p => Assert.Equal(TabPriceGate.Free, p.Gate));
        // and it is a middle, not a copy of either end
        Assert.Contains(circe, p => p.Seconds > 0);
        Assert.Contains(circe, p => p.Seconds < 0);
    }

    [Fact]
    public void The_three_sets_are_different_sets()
    {
        var sets = TabPresets.All.Select(p => new HashSet<string>(p.PriceIds, StringComparer.Ordinal)).ToList();
        Assert.False(sets[0].SetEquals(sets[1]));
        Assert.False(sets[1].SetEquals(sets[2]));
        Assert.False(sets[0].SetEquals(sets[2]));
    }

    [Fact]
    public void Apply_hands_back_the_set_and_an_unknown_name_clears_nothing()
    {
        Assert.Equal(TabPresets.Find(TabPresets.Strict)!.PriceIds, TabPresets.Apply(TabPresets.Strict));
        Assert.Empty(TabPresets.Apply("nonesuch"));
        Assert.Empty(TabPresets.Apply(null));
    }

    [Fact]
    public void Match_lights_the_preset_it_is_on_whatever_order_it_was_saved_in()
    {
        var strict = TabPresets.Find(TabPresets.Strict)!.PriceIds.Reverse().ToList();
        Assert.Equal(TabPresets.Strict, TabPresets.Match(strict));
        strict.Add("session"); // a repeat is still the same set
        Assert.Equal(TabPresets.Strict, TabPresets.Match(strict));
    }

    [Fact]
    public void Match_says_custom_for_a_hand_made_set_and_nothing_at_all_for_an_empty_one()
    {
        Assert.Null(TabPresets.Match(null));
        Assert.Null(TabPresets.Match(new List<string>()));
        Assert.Equal(TabPresets.Custom, TabPresets.Match(new[] { "typo" }));
        // A set that is only junk is an empty set, not a custom one.
        Assert.Null(TabPresets.Match(new[] { "panic", "not_a_row" }));
    }

    [Fact]
    public void One_switch_off_a_preset_is_custom()
    {
        var gentle = TabPresets.Find(TabPresets.Gentle)!.PriceIds.Skip(1).ToList();
        Assert.Equal(TabPresets.Custom, TabPresets.Match(gentle));
    }

    [Fact]
    public void Every_preset_has_a_name_and_a_hint_in_english()
    {
        var en = ChasterPageLoc.En();
        foreach (var id in TabPresets.All.Select(p => p.Id).Concat(new[] { TabPresets.Custom }))
        {
            Assert.NotNull(en[TabPresets.NameKey(id)]);
            Assert.NotNull(en[TabPresets.HintKey(id)]);
        }
    }
}

/// <summary>
/// The hero's long-form countdown and the day's cap meter. Pure, so the page's one composed string
/// and its one measured bar are tested where no window is needed.
/// </summary>
public class TabPageTextHeroTests
{
    private static string Shape(TimeSpan left) =>
        string.Join(" ", TabPageText.Countdown(left).Select(p => p.Key + "=" + p.Value));

    [Fact]
    public void Over_a_day_it_reads_days_and_hours()
    {
        Assert.Equal("chaster_unit_days=12 chaster_unit_hours=4", Shape(new TimeSpan(12, 4, 30, 0)));
        Assert.Equal("chaster_unit_day=1 chaster_unit_hour=1", Shape(new TimeSpan(1, 1, 0, 0)));
        // a whole number of days drops the empty half rather than printing "0 hours"
        Assert.Equal("chaster_unit_days=3", Shape(TimeSpan.FromDays(3)));
    }

    [Fact]
    public void Under_a_day_it_reads_hours_and_minutes_then_minutes()
    {
        Assert.Equal("chaster_unit_hours=4 chaster_unit_minutes=12", Shape(new TimeSpan(4, 12, 0)));
        Assert.Equal("chaster_unit_hour=1 chaster_unit_minute=1", Shape(new TimeSpan(1, 1, 0)));
        Assert.Equal("chaster_unit_hours=2", Shape(TimeSpan.FromHours(2)));
        Assert.Equal("chaster_unit_minutes=12", Shape(TimeSpan.FromMinutes(12)));
        Assert.Equal("chaster_unit_minute=1", Shape(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Under_a_minute_and_a_finished_lock_have_no_parts_at_all()
    {
        Assert.Empty(TabPageText.Countdown(TimeSpan.FromSeconds(59)));
        Assert.Empty(TabPageText.Countdown(TimeSpan.Zero));
        Assert.Empty(TabPageText.Countdown(TimeSpan.FromHours(-3)));
    }

    [Fact]
    public void Every_countdown_key_exists_in_english()
    {
        var en = ChasterPageLoc.En();
        foreach (var key in new[]
                 {
                     "chaster_unit_day", "chaster_unit_days", "chaster_unit_hour", "chaster_unit_hours",
                     "chaster_unit_minute", "chaster_unit_minutes", "chaster_left_soon", "chaster_hero_caption",
                     "chaster_hero_ends",
                 })
            Assert.NotNull(en[key]);
    }

    [Fact]
    public void The_state_line_names_why_the_number_is_what_it_is()
    {
        var running = new LockSnapshot("l1", "Test", DateTime.UtcNow.AddDays(2), false, false, false, DateTime.UtcNow);
        Assert.Null(TabPageText.HeroState(LockLookup.Chosen, running));
        Assert.Null(TabPageText.HeroState(LockLookup.Unlinked, null));
        Assert.Equal("chaster_state_away", TabPageText.HeroState(LockLookup.Away, running));
        Assert.Equal("chaster_state_no_lock", TabPageText.HeroState(LockLookup.None, null));
        Assert.Equal("chaster_state_pick", TabPageText.HeroState(LockLookup.Ambiguous, null));
        Assert.Equal("chaster_state_frozen", TabPageText.HeroState(LockLookup.Chosen, running with { IsFrozen = true }));
        Assert.Equal("chaster_state_hidden", TabPageText.HeroState(LockLookup.Chosen, running with { TimerHidden = true }));
        Assert.Equal("chaster_state_no_end", TabPageText.HeroState(LockLookup.Chosen, running with { EndsAtUtc = null }));
        Assert.Equal("chaster_state_test", TabPageText.HeroState(LockLookup.Chosen, running with { IsTestLock = true }));
        // Chosen with nothing to show is the same sentence as no lock, never a blank hero.
        Assert.Equal("chaster_state_no_lock", TabPageText.HeroState(LockLookup.Chosen, null));
    }

    [Fact]
    public void Every_state_key_exists_in_english()
    {
        var en = ChasterPageLoc.En();
        foreach (var key in new[]
                 {
                     "chaster_state_away", "chaster_state_no_lock", "chaster_state_pick", "chaster_state_frozen",
                     "chaster_state_hidden", "chaster_state_no_end", "chaster_state_test", "chaster_day_header",
                     "chaster_settle_line", "chaster_hold", "chaster_consent_title", "chaster_consent_1",
                     "chaster_consent_2", "chaster_consent_3", "chaster_consent_4", "chaster_consent_ok",
                     "chaster_preset_header", "chaster_preset_customize", "chaster_bill_added", "chaster_bill_earned",
                     "chaster_bill_pushed_label", "chaster_bill_stamp", "chaster_link_browser_hint",
                     "chaster_link_test_hint",
                 })
            Assert.NotNull(en[key]);
    }

    [Fact]
    public void The_cap_meter_never_runs_off_the_end_of_its_track()
    {
        Assert.Equal(0, TabPageText.CapFraction(0));
        Assert.Equal(0.5, TabPageText.CapFraction(CircesTab.DailyCapSeconds / 2), 6);
        Assert.Equal(1, TabPageText.CapFraction(CircesTab.DailyCapSeconds));
        Assert.Equal(1, TabPageText.CapFraction(CircesTab.DailyCapSeconds * 4));
        Assert.Equal(0, TabPageText.CapFraction(-90));
        Assert.Equal(0, TabPageText.CapFraction(60, 0));
    }
}
