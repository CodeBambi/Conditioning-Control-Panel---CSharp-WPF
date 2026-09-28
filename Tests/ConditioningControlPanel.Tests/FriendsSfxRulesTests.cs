using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The friends cues' gate (gaps, session level) and the friend-request diff.</summary>
public class FriendsSfxRulesTests
{
    private static readonly DateTime U0 = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private static FriendRequest R(string id) => new(id, "name " + id, null, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AnyTwoCuesStandAtLeastTheMinimumGapApart()
    {
        var g = new FriendsSfxGate();
        Assert.True(g.TryPass(U0, incoming: false));
        Assert.False(g.TryPass(U0.AddMilliseconds(100), incoming: false));
        Assert.True(g.TryPass(U0.AddMilliseconds(140), incoming: false));
    }

    [Fact]
    public void IncomingCuesStandASecondAndAHalfApartButClicksDoNot()
    {
        var g = new FriendsSfxGate();
        Assert.True(g.TryPass(U0, incoming: true));
        Assert.False(g.TryPass(U0.AddMilliseconds(600), incoming: true));
        Assert.True(g.TryPass(U0.AddMilliseconds(700), incoming: false));
        Assert.False(g.TryPass(U0.AddMilliseconds(1400), incoming: true));
        Assert.True(g.TryPass(U0.AddMilliseconds(1600), incoming: true));
    }

    [Fact]
    public void ARefusedCueDoesNotResetTheClock()
    {
        var g = new FriendsSfxGate();
        Assert.True(g.TryPass(U0, incoming: true));
        for (int ms = 200; ms < 1500; ms += 200) Assert.False(g.TryPass(U0.AddMilliseconds(ms), incoming: true));
        Assert.True(g.TryPass(U0.AddMilliseconds(1500), incoming: true));
    }

    [Theory]
    [InlineData(true, true, 0.5f)]
    [InlineData(true, false, 1f)]
    [InlineData(false, true, 1f)]
    [InlineData(false, false, 1f)]
    public void OnlyIncomingCuesDuckUnderASession(bool incoming, bool session, float expected)
        => Assert.Equal(expected, FriendsSfxGate.Level(incoming, session));

    [Fact]
    public void TheFirstListIsASilentBaseline()
    {
        var w = new FriendRequestWatch();
        var (arrived, gone) = w.Update(new[] { R("a"), R("b") });
        Assert.Empty(arrived);
        Assert.Empty(gone);
        Assert.True(w.Seeded);
    }

    [Fact]
    public void NewRequestsArriveOnceAndLeftOnesGoOnce()
    {
        var w = new FriendRequestWatch();
        w.Update(new[] { R("a") });
        var (arrived, gone) = w.Update(new[] { R("a"), R("b"), R("c") });
        Assert.Equal(new[] { "b", "c" }, arrived.Select(r => r.Id));
        Assert.Empty(gone);

        (arrived, gone) = w.Update(new[] { R("c"), R("b") });
        Assert.Empty(arrived);
        Assert.Equal(new[] { "a" }, gone);

        (arrived, gone) = w.Update(new[] { R("c"), R("b") });
        Assert.Empty(arrived);
        Assert.Empty(gone);
    }

    [Fact]
    public void AResetMakesTheNextListABaselineAgain()
    {
        var w = new FriendRequestWatch();
        w.Update(new[] { R("a") });
        w.Reset();
        Assert.False(w.Seeded);
        var (arrived, gone) = w.Update(new[] { R("x") });
        Assert.Empty(arrived);
        Assert.Empty(gone);
    }

    [Fact]
    public void NullOrEmptyListsAreSafe()
    {
        var w = new FriendRequestWatch();
        w.Update(null);
        var (arrived, _) = w.Update(new[] { R("a"), R("a") });
        Assert.Single(arrived);
    }
}
