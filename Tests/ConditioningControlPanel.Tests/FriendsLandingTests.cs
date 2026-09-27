using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// What a delivery does when it lands: the route by kind, the hold and the queue, the ring.
/// Everything here runs against a fake IFriendsService and the pure rules; no window is built.
/// </summary>
public class FriendsLandingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly LandingWorld PanelUp = new(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false);

    private static InboxItem Poke(string id, string poke = "hi", DateTimeOffset? at = null)
    {
        var a = at ?? T0;
        return new InboxItem(id, SendKind.Poke, "u_1", "Sam", null, poke, null, null, null, a, a.AddHours(24));
    }

    private static InboxItem Invite(string id, string dest = InviteDestination.BackRoom, DateTimeOffset? at = null, string? code = null)
    {
        var a = at ?? T0;
        return new InboxItem(id, SendKind.Invite, "u_1", "Sam", null, null, dest, code, null, a, a.AddSeconds(InviteDestination.LifetimeSeconds));
    }

    private static InboxItem Watch(string id, WatchKind kind = WatchKind.Flavour, string wid = "pink")
        => new(id, SendKind.Watch, "u_1", "Sam", null, null, null, null, new WatchRef(kind, wid, null), T0, T0.AddHours(24));

    // ---------------------------------------------------------------- routing by kind

    [Fact]
    public void PokeWithThePanelUpGoesToPokeNotKnock()
    {
        var (svc, sink, _) = Rig(() => PanelUp);
        svc.Deliver(Poke("a"));
        Assert.Equal(new[] { "poke:a:False" }, sink.Calls);
    }

    [Fact]
    public void InviteAndWatchKnock()
    {
        var (svc, sink, _) = Rig(() => PanelUp);
        svc.Deliver(Invite("i"));
        svc.Deliver(Watch("w"));
        Assert.Equal(new[] { "knock:i:False", "knock:w:False" }, sink.Calls);
    }

    [Fact]
    public void AGameOnScreenRoutesInGame()
    {
        var game = PanelUp with { GameHostActive = true };
        var (svc, sink, _) = Rig(() => game);
        svc.Deliver(Poke("a"));
        svc.Deliver(Invite("i"));
        Assert.Equal(new[] { "poke:a:True", "knock:i:True" }, sink.Calls);
    }

    [Fact]
    public void TrayHiddenWithNoGameGoesToTheInbox()
    {
        var hidden = PanelUp with { PanelVisible = false };
        var (svc, sink, _) = Rig(() => hidden);
        svc.Deliver(Poke("a"));
        svc.Deliver(Invite("i"));
        Assert.Equal(new[] { "inbox:a", "inbox:i" }, sink.Calls);
    }

    [Fact]
    public void TheLauncherAloneStillPresents()
    {
        var launcher = PanelUp with { PanelVisible = false, LauncherVisible = true };
        Assert.Equal(LandingRoute.Present, LandingRules.Decide(Poke("a"), launcher, T0));
    }

    [Fact]
    public void AnExpiredDeliveryIsDropped()
    {
        var (svc, sink, _) = Rig(() => PanelUp, now: () => T0.AddSeconds(91));
        svc.Deliver(Invite("late"));
        Assert.Empty(sink.Calls);
    }

    [Fact]
    public void SentRaisesTheSendersBeatUnlessHeld()
    {
        var world = PanelUp;
        var (svc, sink, _) = Rig(() => world);
        var friend = new Friend("u_2", "Ari", null, 0, true, FriendPresence.None, false);
        svc.RaiseSent(SendKind.Poke, friend);
        world = PanelUp with { Lockdown = true };
        svc.RaiseSent(SendKind.Invite, friend);
        Assert.Equal(new[] { "sent:Poke:Ari" }, sink.Calls);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        var (svc, sink, router) = Rig(() => PanelUp);
        router.Dispose();
        svc.Deliver(Poke("a"));
        Assert.Empty(sink.Calls);
    }

    // ---------------------------------------------------------------- hold and queue

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void LockdownStrictLockAndAProgramHoldThenRelease(bool lockdown, bool strict, bool program)
    {
        var world = new LandingWorld(lockdown, strict, program, true, false, false);
        var (svc, sink, router) = Rig(() => world);
        svc.Deliver(Poke("a"));
        svc.Deliver(Invite("i"));
        Assert.Empty(sink.Calls);
        Assert.Equal(2, router.Held);

        router.Release();
        Assert.Empty(sink.Calls);

        world = PanelUp;
        router.Release();
        Assert.Equal(new[] { "poke:a:False", "knock:i:False" }, sink.Calls);
        Assert.Equal(0, router.Held);
    }

    [Fact]
    public void HeldItemsThatExpireAreDroppedOnRelease()
    {
        var world = PanelUp with { Lockdown = true };
        var now = T0;
        var (svc, sink, router) = Rig(() => world, () => now);
        svc.Deliver(Invite("i"));
        svc.Deliver(Poke("p"));
        now = T0.AddMinutes(5);
        world = PanelUp;
        router.Release();
        Assert.Equal(new[] { "poke:p:False" }, sink.Calls);
    }

    [Fact]
    public void AHoldThatEndsWhileTrayHiddenInboxesTheQueue()
    {
        var world = PanelUp with { ProgramSession = true };
        var (svc, sink, router) = Rig(() => world);
        svc.Deliver(Poke("p"));
        world = PanelUp with { PanelVisible = false };
        router.Release();
        Assert.Equal(new[] { "inbox:p" }, sink.Calls);
    }

    [Fact]
    public void TheQueueCapsAtTenAndDropsTheOldest()
    {
        var q = new LandingQueue();
        for (var i = 0; i < 13; i++) q.Add(Poke("p" + i));
        Assert.Equal(LandingRules.QueueCap, q.Count);
        var drained = q.Drain(T0);
        Assert.Equal("p3", drained[0].Id);
        Assert.Equal("p12", drained[^1].Id);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void TheQueueKeepsOneCopyOfAnId()
    {
        var q = new LandingQueue();
        q.Add(Poke("a"));
        q.Add(Poke("a"));
        Assert.Equal(1, q.Count);
    }

    // ---------------------------------------------------------------- the ring

    [Fact]
    public void AnInviteRingRunsNinetySecondsFromAt()
    {
        var item = Invite("i", at: T0);
        var end = LandingRules.KnockEnds(item, T0.AddSeconds(10));
        Assert.Equal(T0.AddSeconds(90), end);
        Assert.Equal(1.0, LandingRules.RingFraction(T0, end, T0), 6);
        Assert.Equal(0.5, LandingRules.RingFraction(T0, end, T0.AddSeconds(45)), 6);
        Assert.Equal(0.0, LandingRules.RingFraction(T0, end, T0.AddSeconds(200)), 6);
        Assert.Equal(1.0, LandingRules.RingFraction(T0, end, T0.AddSeconds(-5)), 6);
    }

    [Fact]
    public void AWatchCardFoldsAfterItsOwnWindow()
    {
        var end = LandingRules.KnockEnds(Watch("w"), T0);
        Assert.Equal(T0.AddSeconds(LandingRules.WatchCardSeconds), end);
    }

    [Fact]
    public void ADegenerateRingIsEmpty()
        => Assert.Equal(0, LandingRules.RingFraction(T0, T0, T0));

    [Fact]
    public void SecondsLeftRoundsUpAndNeverGoesNegative()
    {
        Assert.Equal(1, LandingRules.SecondsLeft(T0.AddMilliseconds(200), T0));
        Assert.Equal(0, LandingRules.SecondsLeft(T0, T0.AddSeconds(3)));
    }

    [Fact]
    public void RingPointWalksClockwiseFromTwelve()
    {
        var (x0, y0) = LandingRules.RingPoint(10, 10, 5, 0);
        Assert.Equal(10, x0, 6); Assert.Equal(5, y0, 6);
        var (x1, y1) = LandingRules.RingPoint(10, 10, 5, 0.25);
        Assert.Equal(15, x1, 6); Assert.Equal(10, y1, 6);
        var (x2, y2) = LandingRules.RingPoint(10, 10, 5, 0.5);
        Assert.Equal(10, x2, 6); Assert.Equal(15, y2, 6);
    }

    // ---------------------------------------------------------------- the small tables

    [Fact]
    public void EveryPokeHasAWordAFaceTheDeskKnowsAndAColour()
    {
        foreach (var id in PokeSet.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(LandingRules.PokeFallback(id)));
            Assert.True(ConditioningControlPanel.Services.EmiDesk.EmiChains.FaceBodyFrame.ContainsKey(LandingRules.PokeFace(id)), id);
        }
        Assert.True(LandingRules.PokeIsPink("tut"));
        Assert.False(LandingRules.PokeIsPink("hi"));
    }

    [Fact]
    public void EveryFlavourMapsToRealNiches()
    {
        var known = new HashSet<string>();
        foreach (var n in ConditioningControlPanel.Services.Fyp.Online.FypOnlineCoordinator.Catalog) known.Add(n.Id);
        foreach (var f in WatchRef.Flavours)
        {
            var niches = LandingRules.FlavourNiches(f);
            Assert.NotEmpty(niches);
            foreach (var n in niches) Assert.Contains(n, known);
        }
    }

    [Fact]
    public void HtUrlTakesDigitsOnlyAndPassesTheHtCheck()
    {
        var url = LandingRules.HtUrl("109749");
        Assert.NotNull(url);
        Assert.True(ConditioningControlPanel.Helpers.HtUrlHelper.IsEligibleHtUrl(url));
        Assert.Null(LandingRules.HtUrl("12a"));
        Assert.Null(LandingRules.HtUrl("123456789"));
        Assert.Null(LandingRules.HtUrl(""));
    }

    [Theory]
    [InlineData("AB12", true)]
    [InlineData("CCP-ABCDE", true)]
    [InlineData("ab12", false)]
    [InlineData("A1", false)]
    [InlineData("ABCDEFGHIJKLM", false)]
    [InlineData("AB 12", false)]
    [InlineData(null, false)]
    public void JoinCodesFollowTheWireGrammar(string? code, bool ok)
        => Assert.Equal(ok, LandingRules.JoinCodeOk(code));

    [Fact]
    public void KnockArtNamesAPictureForEveryDestination()
    {
        foreach (var d in InviteDestination.All)
            Assert.StartsWith("features/", LandingRules.KnockArt(Invite("i", d)));
        Assert.StartsWith("features/", LandingRules.KnockArt(Watch("w", WatchKind.Ht, "12")));
    }

    // ---------------------------------------------------------------- rig

    private static (FakeFriends, RecordingSink, FriendsLandingRouter) Rig(Func<LandingWorld> world, Func<DateTimeOffset>? now = null)
    {
        var svc = new FakeFriends();
        var sink = new RecordingSink();
        var router = new FriendsLandingRouter(svc, world, now ?? (() => T0), sink);
        return (svc, sink, router);
    }

    private sealed class RecordingSink : ILandingSink
    {
        public List<string> Calls { get; } = new();
        public void Poke(InboxItem item, bool inGame) => Calls.Add($"poke:{item.Id}:{inGame}");
        public void Knock(InboxItem item, bool inGame) => Calls.Add($"knock:{item.Id}:{inGame}");
        public void Inbox(InboxItem item) => Calls.Add($"inbox:{item.Id}");
        public void SentBeat(SendKind kind, Friend to) => Calls.Add($"sent:{kind}:{to.Name}");
    }

    private sealed class FakeFriends : IFriendsService
    {
        public bool Available => true;
        public FriendsSnapshot Snapshot => FriendsSnapshot.Empty;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged;
        public event Action<InboxItem>? Delivered;
        public event Action<SendKind, Friend>? Sent;

        public void Deliver(InboxItem item) => Delivered?.Invoke(item);
        public void RaiseSent(SendKind k, Friend f) => Sent?.Invoke(k, f);
        public void Touch() => SnapshotChanged?.Invoke(Snapshot);

        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string friendId, string pokeId) => Task.FromResult(SendResult.Sent);
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code) => Task.FromResult(SendResult.Sent);
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) => Task.FromResult(SendResult.Sent);
        public Task<AddResult> AddByCodeAsync(string code) => Task.FromResult(AddResult.Sent);
        public Task AcceptAsync(string requesterId) => Task.CompletedTask;
        public Task DeclineAsync(string requesterId) => Task.CompletedTask;
        public Task CancelRequestAsync(string targetId) => Task.CompletedTask;
        public Task RemoveAsync(string friendId) => Task.CompletedTask;
        public Task BlockAsync(string friendId) => Task.CompletedTask;
        public Task UnblockAsync(string friendId) => Task.CompletedTask;
        public Task SetSquelchAsync(string friendId, bool on) => Task.CompletedTask;
        public Task ReportAsync(string friendId, string reason) => Task.CompletedTask;
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
    }
}
