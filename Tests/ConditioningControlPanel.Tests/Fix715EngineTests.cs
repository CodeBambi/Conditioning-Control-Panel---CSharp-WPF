using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>7.1.5 lane B: engine + quests fixes (#1388, #1377, settings churn, #1387).</summary>
public class Fix715EngineTests
{
    // ---- #1377 flashes switched on mid-run --------------------------------------------------

    [Fact]
    public void Switched_off_scheduler_keeps_a_slow_recheck_instead_of_dying()
    {
        Assert.Equal(FlashScheduleRule.DisabledRecheck.TotalSeconds,
            FlashScheduleRule.IntervalSeconds(enabled: false, flashesPerHour: 60, unit01: 0.5));
        Assert.True(FlashScheduleRule.DisabledRecheck <= TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(60, 0.0, 42.0)]
    [InlineData(60, 0.5, 60.0)]
    [InlineData(60, 1.0, 78.0)]
    [InlineData(10000, 0.5, 3.0)]
    [InlineData(0, 0.5, 3600.0)]
    public void Enabled_interval_is_base_plus_minus_thirty_percent_with_a_floor(int perHour, double unit, double expected) =>
        Assert.Equal(expected, FlashScheduleRule.IntervalSeconds(true, perHour, unit), 6);

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void A_tick_fires_only_when_running_idle_and_switched_on(bool running, bool busy, bool enabled, bool fires) =>
        Assert.Equal(fires, FlashScheduleRule.ShouldFire(running, busy, enabled));

    // ---- settings saved every second --------------------------------------------------------

    [Fact]
    public void Achievement_tick_does_not_credit_conditioning_time_every_second()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Progression", "AchievementService.cs"));
        var code = string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("AddConditioningTime(", code);
    }

    // ---- #1388 lockdowns and quests ---------------------------------------------------------

    [Fact]
    public void A_throwing_deactivation_handler_does_not_starve_the_quest_credit()
    {
        var credited = 0;
        Action handlers = () => throw new InvalidOperationException("teardown");
        handlers += () => credited++;
        LockdownService.RaiseEach(handlers);
        Assert.Equal(1, credited);
        LockdownService.RaiseEach(null);
    }

    [Fact]
    public void Lockdown_page_defaults_to_a_duration_that_counts_and_offers_twenty_minutes()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "LockdownTabView.xaml"));
        var combo = Regex.Match(xaml, "<ComboBox[^>]*x:Name=\"CmbLockdownDuration\"[\\s\\S]*?</ComboBox>").Value;
        Assert.NotEmpty(combo);
        var index = int.Parse(Regex.Match(combo, "SelectedIndex=\"(\\d+)\"").Groups[1].Value);
        var tags = Regex.Matches(combo, "Tag=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Contains((int)QuestService.LockdownQuestMinimum.TotalMinutes, tags);
        Assert.True(QuestService.LockdownCountsForQuests(TimeSpan.FromMinutes(tags[index])),
            $"default lockdown of {tags[index]} min never counts for the Lockdown quests");

        var langs = SourceRoots.LanguagesDirectory;
        foreach (var file in Directory.GetFiles(langs, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(doc.RootElement.TryGetProperty("btn_20_minutes", out var v), $"{Path.GetFileName(file)} has no btn_20_minutes");
            Assert.Contains("20", v.GetString());
        }
    }

    // ---- #1387 companion offers the quests page with nothing left ---------------------------

    [Fact]
    public void A_finished_board_has_no_open_quest()
    {
        var done = new ActiveQuest("a") { IsCompleted = true };
        var open = new ActiveQuest("b");
        Assert.False(QuestService.AnyUnfinished(new ActiveQuest?[] { done, done, null }, done));
        Assert.False(QuestService.AnyUnfinished(null, null));
        Assert.True(QuestService.AnyUnfinished(new ActiveQuest?[] { done, open }, done));
        Assert.True(QuestService.AnyUnfinished(new ActiveQuest?[] { done }, open));
    }

    [Fact]
    public void Ask_card_skips_quests_once_the_board_is_done()
    {
        AskSources Sources(bool open) => new()
        {
            Quests = new AskOption("page.quests", "Quests", () => true),
            QuestsOpen = open,
        };
        Assert.Null(AskCatalog.Build(AskKind.Quests, Sources(false), new Random(1), DateTime.UtcNow));
        Assert.NotNull(AskCatalog.Build(AskKind.Quests, Sources(true), new Random(1), DateTime.UtcNow));
    }

    [Fact]
    public void Chat_does_not_offer_the_quests_page_when_every_quest_is_done_unless_asked_by_name()
    {
        var activities = new[] { Activity("page.quests"), Activity("game.test") };
        var none = Array.Empty<CompanionTurn>();
        Assert.DoesNotContain(ConversationDelivery.Select(activities, "suggest something", none, questsOpen: false), a => a.Id == "page.quests");
        Assert.Contains(ConversationDelivery.Select(activities, "suggest something", none, questsOpen: true), a => a.Id == "page.quests");
        Assert.Contains(ConversationDelivery.Select(activities, "show my quests", none, questsOpen: false), a => a.Id == "page.quests");
    }

    private static CompanionActivity Activity(string id) => new(id, id, "test", () => true,
        () => throw new Exception("a reply must never open an activity"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
