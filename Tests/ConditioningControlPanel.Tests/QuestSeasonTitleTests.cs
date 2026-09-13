using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins the Quests header title after The Descent ended monthly seasons: the permanent title
/// unless the server sends a live, non-season event title. The server still says "Airhead August",
/// which is exactly the case that must NOT reach the header.
/// </summary>
public class QuestSeasonTitleTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoServerTitle_IsPermanentTitle()
    {
        Assert.Equal("Deeper Every Month", QuestDefinitionService.ResolveSeasonTitle(null, Now, Now));
        Assert.Equal("Deeper Every Month", QuestDefinitionService.ResolveSeasonTitle("  ", Now, Now));
        Assert.Equal("Deeper Every Month", QuestDefinitionService.ResolveSeasonTitle(null, null, Now));
    }

    [Theory]
    [InlineData("Airhead August")]
    [InlineData("Sissygasm September")]
    [InlineData("Jerk-it January")]
    [InlineData("Mooing May")]
    [InlineData("Mindless March")]
    [InlineData("No-nut November")]
    [InlineData("Obey-tober")]
    [InlineData("Dick-ember")]
    [InlineData("AIRHEAD AUGUST")]
    public void DeadSeasonName_IsPermanentTitle(string server)
    {
        Assert.True(QuestDefinitionService.IsDeadSeasonTitle(server));
        Assert.Equal("Deeper Every Month", QuestDefinitionService.ResolveSeasonTitle(server, Now, Now));
    }

    [Theory]
    [InlineData("Bimbo Halloween Hunt")]
    [InlineData("You May Obey")]
    [InlineData("March of the Bimbos")]
    public void CustomEventTitle_Wins(string server)
    {
        Assert.False(QuestDefinitionService.IsDeadSeasonTitle(server));
        Assert.Equal(server, QuestDefinitionService.ResolveSeasonTitle(server, Now, Now));
    }

    [Fact]
    public void EventTitleFetchedLastMonth_IsPermanentTitle()
    {
        var august = new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal("Deeper Every Month", QuestDefinitionService.ResolveSeasonTitle("Bimbo Halloween Hunt", august, Now));
    }
}
