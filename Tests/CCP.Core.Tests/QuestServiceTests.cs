using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>QuestService, moved from the WPF head, headless: roll, reroll, progress, completion.
/// No head seeded: embedded definitions, no premium, no settings (level 1, streak 0).</summary>
[Collection(SessionStatics.Name)] // CoreProgression.AddXPProvider is process-global
public sealed class QuestServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-questsvc-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FreshProfile_RollsAFullDailyBoardAndAWeekly()
    {
        using var quests = new QuestService(null, _dir);

        var slots = quests.GetDailySlots();
        Assert.Equal(QuestProgress.MaxDailySlots, slots.Count);
        Assert.All(slots, s => Assert.NotNull(s.Definition));
        Assert.Equal(slots.Count, slots.Select(s => s.Definition!.Id).Distinct().Count());
        Assert.All(slots, s => Assert.Contains(QuestDefinition.DailyQuests, d => d.Id == s.Definition!.Id));
        Assert.NotNull(quests.GetCurrentWeeklyDefinition());
    }

    /// <summary>The day's first daily completion advances the streak before it pays, so the card
    /// quotes the streak it will be paid at (StreakPaidOn), not the stored one (3% short before).</summary>
    [Fact]
    public void CardQuote_IsWhatTheDaysFirstCompletionPays()
    {
        var (oldProvider, oldXp) = (CoreSettings.ServiceProvider, CoreProgression.AddXPProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreProgression.AddXPProvider = (_, _) => { };
        try
        {
            var s = service.Current;
            (s.PlayerLevel, s.DailyQuestStreak) = (1, 4);
            using var quests = new QuestService(null, _dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            Assert.Equal(1, quests.StreakPaidOn(QuestType.Daily, s));      // gap: the streak restarts
            quests.Progress.DailyQuestCompletionDates.Add(DateTime.Today.AddDays(-1));
            Assert.Equal(5, quests.StreakPaidOn(QuestType.Daily, s));
            Assert.Equal(4, quests.StreakPaidOn(QuestType.Weekly, s));     // a weekly never advances it
            var pop = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
            var quoted = QuestService.ScaledQuestXp(pop.XPReward, s, quests.StreakPaidOn(QuestType.Daily, s));
            QuestCompletedEventArgs? done = null;
            quests.QuestCompleted += (_, e) => done = e;

            quests.TrackBubblesPopped(pop.TargetValue);

            Assert.Equal(quoted, done!.XPAwarded);
            Assert.Equal(5, s.DailyQuestStreak);
            Assert.Equal(5, quests.StreakPaidOn(QuestType.Daily, s));      // later dailies today
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreProgression.AddXPProvider) = (oldProvider, oldXp);
        }
    }

    [Fact]
    public void Reroll_SwapsTheSlotAndSpendsTheOneFreeReroll()
    {
        using var quests = new QuestService(null, _dir);
        var before = quests.Progress.DailyQuests[0].DefinitionId;
        Assert.Equal(1, quests.GetRemainingDailyRerolls());

        Assert.True(quests.RerollDailyQuest(0));

        Assert.NotEqual(before, quests.Progress.DailyQuests[0].DefinitionId);
        Assert.Equal(0, quests.GetRemainingDailyRerolls());
        Assert.False(quests.RerollDailyQuest(1));
    }

    [Fact]
    public void Progress_ThenCompletion_PaysScaledXpAndPersists()
    {
        var old = CoreProgression.AddXPProvider;
        var awarded = new List<(double, string)>();
        CoreProgression.AddXPProvider = (xp, source) => awarded.Add((xp, source));
        try
        {
            using var quests = new QuestService(null, _dir);
            quests.Progress.DailyQuests = new List<ActiveQuest>
            {
                new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d"),
            };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            var progressed = new List<int>();
            QuestCompletedEventArgs? completed = null;
            quests.QuestProgressChanged += (_, e) => progressed.Add(e.CurrentProgress);
            quests.QuestCompleted += (_, e) => completed = e;

            quests.TrackBubblesPopped(10);
            Assert.Equal(new[] { 10 }, progressed);
            Assert.Null(completed);

            quests.TrackBubblesPopped(30);

            var seat = quests.Progress.DailyQuests[0];
            Assert.True(seat.IsCompleted);
            Assert.False(quests.Progress.DailyQuests[1].IsCompleted);
            Assert.NotNull(completed);
            Assert.Equal("pop_parade_d", completed!.QuestDefinition.Id);
            // 150 base x QuestLevelScale(level 1, legacy curve) = 150 x 1.04.
            Assert.Equal(156, completed.XPAwarded);
            Assert.Equal(new[] { (156.0, "Quest") }, awarded);
            Assert.Equal(1, quests.GetDailyQuestsCompletedToday());

            var saved = File.ReadAllText(Path.Combine(_dir, "quests.json"));
            Assert.Contains("\"TotalXPFromQuests\": 156", saved);
        }
        finally { CoreProgression.AddXPProvider = old; }
    }
}
