using System;
using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Every game needs an account (Sep 18 2026 owner decision). The rule lives in one hook,
/// <see cref="LauncherCatalogue.SignedIn"/>, and every way into a game (a tile, a shortcut, the
/// <c>--game</c> argument) funnels through <see cref="LauncherHost.LaunchGame"/> and
/// <see cref="LauncherCatalogue.TryLaunch(LauncherEntry)"/>, so these rows are the whole fence.
/// </summary>
[Collection("LauncherSignIn")]
public class LauncherSignInTests : IDisposable
{
    private readonly Func<bool> _previous = LauncherCatalogue.SignedIn;

    public void Dispose() => LauncherCatalogue.SignedIn = _previous;

    private static LauncherEntry Fake(Action launch, bool locked = false) => new(
        "fake", "launcher_game_backroom_title", "launcher_game_backroom_blurb", null, "?", Colors.White,
        () => true, () => locked, launch, () => false);

    [Fact]
    public void Signed_out_every_catalogue_entry_needs_an_account()
    {
        LauncherCatalogue.SignedIn = () => false;
        Assert.True(LauncherCatalogue.NeedsAccount);
        Assert.All(LauncherCatalogue.Games, g => Assert.True(g.NeedsAccount));
    }

    [Fact]
    public void Signed_in_no_entry_needs_an_account()
    {
        LauncherCatalogue.SignedIn = () => true;
        Assert.False(LauncherCatalogue.NeedsAccount);
        Assert.All(LauncherCatalogue.Games, g => Assert.False(g.NeedsAccount));
    }

    [Fact]
    public void A_probe_that_throws_reads_as_signed_out()
    {
        LauncherCatalogue.SignedIn = () => throw new InvalidOperationException("no app");
        Assert.True(LauncherCatalogue.NeedsAccount);
    }

    [Fact]
    public void TryLaunch_refuses_signed_out_and_never_calls_Launch()
    {
        LauncherCatalogue.SignedIn = () => false;
        int launched = 0;
        Assert.False(LauncherCatalogue.TryLaunch(Fake(() => launched++)));
        Assert.False(LauncherCatalogue.TryLaunch(Fake(() => launched++, locked: true)));
        Assert.Equal(0, launched);
    }

    [Fact]
    public void TryLaunch_calls_Launch_once_signed_in()
    {
        LauncherCatalogue.SignedIn = () => true;
        int launched = 0;
        Assert.True(LauncherCatalogue.TryLaunch(Fake(() => launched++)));
        Assert.Equal(1, launched);
    }

    [Fact]
    public void LaunchGame_refuses_every_game_signed_out_and_waits_on_nothing()
    {
        LauncherCatalogue.SignedIn = () => false;
        int asked = 0;
        var previousHook = LauncherHost.RequestSignIn;
        LauncherHost.RequestSignIn = () => asked++;
        try
        {
            foreach (var id in LauncherCatalogue.Games.Select(g => g.Id))
                Assert.False(LauncherHost.LaunchGame(id));
            Assert.Null(LauncherHost.AwaitingGame);
            // No launcher window in a test, so the refusal is silent: the caller shows the
            // launcher, whose tiles wear the same ask.
            Assert.Equal(0, asked);
        }
        finally { LauncherHost.RequestSignIn = previousHook; }
    }

    [Fact]
    public void LaunchGame_still_refuses_an_unknown_id_whatever_the_account()
    {
        LauncherCatalogue.SignedIn = () => true;
        Assert.False(LauncherHost.LaunchGame("nope"));
    }
}
