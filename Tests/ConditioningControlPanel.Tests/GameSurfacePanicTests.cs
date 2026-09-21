using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Support, Sep 19 2026: "why does pressing Esc 2-3 times exit the app? Whilst am ingame?".
///
/// <para>The panic ladder kept its list of game surfaces twice - the probe that decides whether a
/// press may advance the double-press exit counter, and the stop pass that closes things - and
/// Racing Thoughts, the Goon Game, Piece by Piece and the Graded Intake window were in neither. So
/// a press inside one of them closed nothing and DID advance the counter, and in Racing Thoughts
/// Escape is the brake. One registry now feeds both, and these tests hold it there.</para>
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class GameSurfacePanicTests : IDisposable
{
    private readonly IReadOnlyList<GameSurfaces.Surface> _real = GameSurfaces.All;

    public void Dispose() => GameSurfaces.All = _real;

    private static GameSurfaces.Surface Fake(string id, bool active, Action? close = null)
        => new(id, () => active, close ?? (() => { }));

    [Fact]
    public void EveryLauncherGameIsOnTheList()
    {
        // The drift guard. A game the launcher can start but the panic ladder has never heard of is
        // a game you cannot panic out of without quitting the app.
        var known = GameSurfaces.Ids();

        foreach (var game in LauncherCatalogue.Games)
        {
            Assert.True(known.Contains(game.Id, StringComparer.OrdinalIgnoreCase),
                $"launcher game '{game.Id}' is not registered in GameSurfaces");
        }
    }

    [Fact]
    public void RacingThoughtsIsRegistered()
    {
        // The reported one, named outright so a refactor that drops it fails loudly.
        Assert.Contains("race", GameSurfaces.Ids());
        Assert.Contains("goon", GameSurfaces.Ids());
        Assert.Contains("intake", GameSurfaces.Ids());
    }

    [Fact]
    public void NothingUp_MeansNoGameOwnsTheScreen()
    {
        GameSurfaces.All = new[] { Fake("race", false), Fake("goon", false) };

        Assert.False(GameSurfaces.AnyOwnsTheScreen());
        Assert.Empty(GameSurfaces.ActiveIds());
    }

    [Fact]
    public void AGameThatIsUp_OwnsTheScreen()
    {
        GameSurfaces.All = new[] { Fake("race", true), Fake("goon", false) };

        Assert.True(GameSurfaces.AnyOwnsTheScreen());
        Assert.Equal(new[] { "race" }, GameSurfaces.ActiveIds());
    }

    [Fact]
    public void AProbeThatThrows_ReadsAsClosed_AndNeverEatsThePress()
    {
        GameSurfaces.All = new[]
        {
            new GameSurfaces.Surface("broken", () => throw new InvalidOperationException("dead host"), () => { }),
            Fake("goon", false),
        };

        Assert.False(GameSurfaces.AnyOwnsTheScreen());
    }

    [Fact]
    public void CloseAll_TakesDownEverySurface()
    {
        var closed = new List<string>();
        GameSurfaces.All = new[]
        {
            Fake("race", true, () => closed.Add("race")),
            Fake("goon", false, () => closed.Add("goon")),
        };

        GameSurfaces.CloseAll((_, close) => close());

        // Closing is idempotent on every host, so the pass does not ask who is up first.
        Assert.Equal(new[] { "race", "goon" }, closed);
    }

    [Fact]
    public void CloseAll_RunsThroughTheCallersGuard_SoOneBadHostCannotStarveTheRest()
    {
        var closed = new List<string>();
        GameSurfaces.All = new[]
        {
            Fake("race", true, () => throw new InvalidOperationException("close threw")),
            Fake("goon", true, () => closed.Add("goon")),
        };

        GameSurfaces.CloseAll((_, close) => { try { close(); } catch { /* the stop pass's own Step */ } });

        Assert.Equal(new[] { "goon" }, closed);
    }

    [Fact]
    public void CloseAll_ByName_TouchesOnlyTheOnesAsked()
    {
        // The legacy rung has already probed, and it must not reach past the rungs above it into a
        // Rabbit Hole descent or a feed that those rungs deliberately declined to touch.
        var closed = new List<string>();
        GameSurfaces.All = new[]
        {
            Fake("chaos", false, () => closed.Add("chaos")),
            Fake("race", true, () => closed.Add("race")),
            Fake("goon", true, () => closed.Add("goon")),
        };

        GameSurfaces.CloseAll(new[] { "race", "goon" }, (_, close) => close());

        Assert.Equal(new[] { "race", "goon" }, closed);
    }

    [Fact]
    public void CloseAll_ByName_IgnoresAnIdThatIsNotRegistered()
    {
        var closed = new List<string>();
        GameSurfaces.All = new[] { Fake("race", true, () => closed.Add("race")) };

        GameSurfaces.CloseAll(new[] { "race", "somethingelse" }, (_, close) => close());

        Assert.Equal(new[] { "race" }, closed);
    }

    [Fact]
    public void APressThatClosesAGame_DoesNotArmTheExitLadder()
    {
        // The rule the registry exists to serve: with a game on screen the press stops the world
        // and nothing more, so a reflexive double tap cannot quit the app. With the game already
        // gone the counter moves normally and double-press-to-exit is still reachable.
        Assert.False(PanicPolicy.AdvancesExitLadder(PanicPolicy.Rung.StopEverything, aGameSurfaceOwnedTheScreen: true));
        Assert.True(PanicPolicy.AdvancesExitLadder(PanicPolicy.Rung.StopEverything, aGameSurfaceOwnedTheScreen: false));
    }
}
