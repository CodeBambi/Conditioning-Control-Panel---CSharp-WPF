using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#705: a 5-minute lockdown paid the "Locked Away" daily. A lockdown counts for the
/// Lockdown quests and program tasks only once it ran for 20 minutes.
/// </summary>
public class LockdownQuestMinimumTests
{
    [Fact]
    public void Minimum_is_twenty_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(20), QuestService.LockdownQuestMinimum);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(19.99)]
    public void Short_lockdowns_do_not_count(double minutes) =>
        Assert.False(QuestService.LockdownCountsForQuests(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(20)]
    [InlineData(45)]
    public void Twenty_minutes_and_up_count(double minutes) =>
        Assert.True(QuestService.LockdownCountsForQuests(TimeSpan.FromMinutes(minutes)));

    /// <summary>
    /// ccp-bugs #1360/#1361: the rule shipped but the cards still said "Complete 1 lockdown", so two
    /// players sat through 5-minute lockdowns and got nothing. The server catalogue sends that old
    /// text; a local quest_{id}_desc key wins over it, so every Lockdown quest needs one that names
    /// the minimum, in every language.
    /// </summary>
    [Fact]
    public void Every_lockdown_quest_card_names_the_minimum_in_every_language()
    {
        var ids = QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests)
            .Where(q => q.Category == QuestCategory.Lockdown)
            .Select(q => q.Id)
            .ToList();
        Assert.NotEmpty(ids);

        var minutes = ((int)QuestService.LockdownQuestMinimum.TotalMinutes).ToString();
        var langs = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var file in Directory.GetFiles(langs, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var id in ids)
            {
                Assert.True(doc.RootElement.TryGetProperty($"quest_{id}_desc", out var desc),
                    $"{Path.GetFileName(file)} has no quest_{id}_desc");
                Assert.Contains(minutes, desc.GetString());
            }
            Assert.True(doc.RootElement.TryGetProperty("lockdown_quest_min_hint", out var hint),
                $"{Path.GetFileName(file)} has no lockdown_quest_min_hint");
            Assert.Contains(minutes, hint.GetString());
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
