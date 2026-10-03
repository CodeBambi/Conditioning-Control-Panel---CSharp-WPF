using System;
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
}
