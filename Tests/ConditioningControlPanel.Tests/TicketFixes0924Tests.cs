using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Ticket 2026-09-24 (Pika): the "post your achievements to Discord?" box landed on the Rabbit
/// Hole's two doors, and neither the box nor the door under it could be used. The offer already
/// went to the Inbox while a game WINDOW was up, but the descent runs inside the app and is not a
/// game window, so only the panic key's surface registry can see it.
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class AchievementShareOverDescentTests : IDisposable
{
    private readonly IReadOnlyList<GameSurfaces.Surface> _real = GameSurfaces.All;

    public void Dispose() => GameSurfaces.All = _real;

    private static GameSurfaces.Surface Fake(string id, bool active)
        => new(id, () => active, () => { });

    [Fact]
    public void TheDescentAloneCountsAsAGameOnScreen()
    {
        GameSurfaces.All = new[] { Fake("chaos", true), Fake("race", false) };
        Assert.True(AchievementSharePromptRule.GameOnScreen(webGameHostUp: false));
    }

    [Fact]
    public void TheOfferGoesToTheInboxDuringTheDescent()
    {
        GameSurfaces.All = new[] { Fake("chaos", true) };
        var routing = AchievementSharePromptRule.Decide(false,
            AchievementSharePromptRule.GameOnScreen(webGameHostUp: false),
            launcherHasTheScreen: false, panelOnScreen: true);
        Assert.Equal(AchievementSharePromptRouting.Inbox, routing);
    }

    [Fact]
    public void NothingUpMeansNoGame()
    {
        GameSurfaces.All = new[] { Fake("chaos", false), Fake("dtrh", false) };
        Assert.False(AchievementSharePromptRule.GameOnScreen(webGameHostUp: false));
        Assert.True(AchievementSharePromptRule.GameOnScreen(webGameHostUp: true));
    }
}

/// <summary>Ticket 2026-09-24: the side rail kept stock names under the Circe mod.</summary>
public class ModAwareLocTextTests
{
    private static string Circe(string s) => s.Replace("Takeover", "Circe's Hour");

    [Fact]
    public void ModWordingWinsWhenTheModRenamesTheEnglish()
        => Assert.Equal("Circe's Hour", ModAwareLocText.Resolve("Übernahme", "Takeover", Circe));

    [Fact]
    public void LocalizedTextStaysWhenTheModLeavesItAlone()
        => Assert.Equal("Begleiter", ModAwareLocText.Resolve("Begleiter", "Companion", Circe));

    [Fact]
    public void NoModOrNoEnglishKeepsTheLocalizedText()
    {
        Assert.Equal("Übernahme", ModAwareLocText.Resolve("Übernahme", "Takeover", null));
        Assert.Equal("Übernahme", ModAwareLocText.Resolve("Übernahme", null, Circe));
    }
}
