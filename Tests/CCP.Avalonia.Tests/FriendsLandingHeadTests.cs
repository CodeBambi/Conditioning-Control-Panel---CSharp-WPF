using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Avalonia.Views.Friends;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Startup;
using Xunit;
using FriendItem = ConditioningControlPanel.Services.Friends.InboxItem;

namespace CCP.Avalonia.Tests;

/// <summary>The friends landing on this head (WPF Services/Friends/FriendsLanding.cs + Windows/Friends):
/// a delivery from the service ends as the surface WPF shows (corner notice, knock card, Inbox row),
/// with its SEEN / joined / declined receipt, the bell, the holds, the doors and panic. One frame
/// family per test: delivery in, surface + receipt + row out. Swaps process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FriendsLandingHeadTests : IDisposable
{
    private readonly FakeFriends _svc = new();
    private readonly StartupInbox _rows = new();
    private readonly List<(string Line, string Face)> _emi = new();
    private readonly List<string> _sounds = new();
    private LandingWorld _world = new(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false);
    private Window? _anchor;
    private bool _bell = true;
    private Func<bool>? _oldBell;
    private Func<MotionLevel>? _oldMotion;
    private Action<string>? _oldChess;
    private Action? _oldBackRoom;
    private bool _seamed;

    // Every seam is swapped ON the dispatcher thread: touching FriendsDrawer / FriendsLanding statics from the
    // xunit thread builds their brushes there, and Avalonia then reports a cross-thread access at teardown.
    private void Seam()
    {
        _seamed = true;
        (_oldBell, _oldMotion, _oldChess, _oldBackRoom) = (FriendsDrawer.NoticesOn, FriendsDrawer.MotionLevelNow, FriendsLanding.JoinChess, FriendsLanding.OpenBackRoom);
        FriendsSfx.Player = (rel, _, tag) => _sounds.Add(tag);
        FriendsDrawer.NoticesOn = () => _bell;
        FriendsDrawer.MotionLevelNow = () => MotionLevel.Off;   // every OUT is immediate, so a test sees the end state
        FriendsLanding.Service = () => _svc;
        FriendsLanding.WorldOverride = () => _world;
        FriendsLanding.AnchorOverride = () => _anchor;
        FriendsLanding.Rows = () => _rows;
        FriendsLanding.EmiOverride = (line, face) => _emi.Add((line, face));
        FriendsSeen.Shared.Clear();
    }

    public void Dispose()
    {
        if (!_seamed) return;
        AvaloniaTestDispatcher.Run(() =>
        {
            try { FriendsLanding.Stop(); _anchor?.Close(); } catch { }
            FriendsSfx.Player = null;
            FriendsDrawer.NoticesOn = _oldBell!;
            FriendsDrawer.MotionLevelNow = _oldMotion!;
            FriendsLanding.Service = () => ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service;
            FriendsLanding.WorldOverride = null;
            FriendsLanding.AnchorOverride = null;
            FriendsLanding.Rows = () => ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox;
            FriendsLanding.EmiOverride = null;
            FriendsLanding.JoinChess = _oldChess!;
            FriendsLanding.OpenBackRoom = _oldBackRoom!;
            FriendsLanding.GoonJoin = null;
            FriendsLanding.GoonIsActive = () => false;
            FriendsSeen.Shared.Clear();
        });
    }

    private const string Id1 = "0123456789abcdef", Id2 = "fedcba9876543210", Challenge = "c_0123456789abcdef";

    private static FriendItem Poke(string id = Id1, string poke = "hi") =>
        new(id, SendKind.Poke, "u_ann", "Ann", null, poke, null, null, null, ServerClock.UtcNow, ServerClock.UtcNow.AddHours(24));

    private static FriendItem Invite(string dest, string? code, string id = Id1) =>
        new(id, SendKind.Invite, "u_ann", "Ann", null, null, dest, code, null, ServerClock.UtcNow, ServerClock.UtcNow.AddMinutes(5));

    private static FriendItem WatchItem(string id = Id1) =>
        new(id, SendKind.Watch, "u_ann", "Ann", null, null, null, null, new WatchRef(WatchKind.Flavour, "pink", null), ServerClock.UtcNow, ServerClock.UtcNow.AddHours(24));

    private Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Seam();
        _anchor = new Window { Width = 900, Height = 600 };
        _anchor.Show();
        FriendsLanding.Stop();     // a router left by another class's test never sees this one's deliveries
        FriendsLanding.Tick();     // builds the router over the fake
        body();
        return Task.CompletedTask;
    });

    private bool Reported(string id, string state) => _svc.Receipts.Any(r => r.Id == id && r.State == state);

    // ---------------------------------------------------------------- pokes

    [Fact]
    public Task APokeLandsAsACornerNoticeWithASeenReceiptEmiAndTheCue() => Run(() =>
    {
        _svc.Deliver(Poke());
        Assert.True(FriendNotices.AnyUp);
        var n = Assert.Single(FriendNotices.Current!.Up);
        Assert.Equal(NoticeKind.Poke, n.Kind);
        Assert.True(Reported(Id1, ReceiptState.Seen), "a poke on screen reports seen");
        Assert.Single(_emi);
        Assert.False(_rows.Contains("friends:" + Id1));
    });

    [Fact]
    public Task TheBellOffMeansAnInboxRowAndNothingElse() => Run(() =>
    {
        _bell = false;
        _svc.Deliver(Poke());
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge, Id2));
        Assert.False(FriendNotices.AnyUp);
        Assert.False(KnockCard.AnyUp);
        Assert.True(_rows.Contains("friends:" + Id1));
        Assert.True(_rows.Contains("friends:" + Id2));
        Assert.Empty(_sounds);
        Assert.Empty(_emi);
        Assert.Empty(_svc.Receipts);   // nothing was on screen, so nothing is "seen"
    });

    [Fact]
    public Task APokeThatRunsOutUnseenWaitsInTheInbox() => Run(() =>
    {
        _svc.Deliver(Poke());
        FriendNotices.Current!.Advance(FriendNoticeRules.LifetimeMs(NoticeKind.Poke) + 1);
        Assert.False(FriendNotices.AnyUp);
        Assert.True(_rows.Contains("friends:" + Id1));
    });

    [Fact]
    public Task PokeBackSendsTheSamePresetAndNothingTyped() => Run(() =>
    {
        _svc.Deliver(Poke(poke: "tut"));
        var w = FriendNotices.Current!;
        w.PressAction(w.Up[0]);
        Assert.Equal(("u_ann", "tut"), Assert.Single(_svc.Pokes));
        Assert.False(FriendNotices.AnyUp);
        Assert.False(_rows.Contains("friends:" + Id1));   // acted on: no row left behind
    });

    // ---------------------------------------------------------------- invites

    [Fact]
    public Task AChessInviteKnocksAndJoinOpensTheBoardOnTheChallenge() => Run(() =>
    {
        string? joined = null;
        FriendsLanding.JoinChess = id => joined = id;
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        var card = Assert.Single(KnockCard.Up);
        Assert.True(Reported(Id1, ReceiptState.Seen));
        Assert.Null(joined);   // nothing joins by itself
        card.Fold(KnockOutcome.Go);
        Assert.Equal(Challenge, joined);
        Assert.True(Reported(Id1, ReceiptState.Joined));
        Assert.False(KnockCard.AnyUp);
        Assert.False(_rows.Contains("friends:" + Id1));
    });

    [Fact]
    public Task AChessInviteWithoutAChallengeIdOpensNothing() => Run(() =>
    {
        string? joined = null;
        FriendsLanding.JoinChess = id => joined = id;
        FriendsLanding.OnKnockDone(Invite(InviteDestination.Chess, "ABCD12"), KnockOutcome.Go);
        Assert.Null(joined);
    });

    [Fact]
    public Task AGoonInviteGoesThroughTheGoonSeamWithItsCode() => Run(() =>
    {
        var codes = new List<string?>();
        FriendsLanding.GoonJoin = c => codes.Add(c);
        _svc.Deliver(Invite(InviteDestination.Goon, "ABCD12"));
        Assert.Single(KnockCard.Up).Fold(KnockOutcome.Go);
        Assert.Equal(new string?[] { "ABCD12" }, codes);
        Assert.True(Reported(Id1, ReceiptState.Joined));
    });

    [Fact]
    public Task AGoonInviteWithNoHostOnThisHeadIsDeniedNotSilent() => Run(() =>
    {
        FriendsLanding.GoonJoin = null;
        _svc.Deliver(Invite(InviteDestination.Goon, "ABCD12"));
        Assert.Single(KnockCard.Up).Fold(KnockOutcome.Go);
        // the refusal is worded where the player is looking: a floating word over the anchor
        Assert.Contains(_anchor!.OwnedWindows, w => w is ConditioningControlPanel.Avalonia.Views.Overlays.FloatingWord);
    });

    [Fact]
    public Task ABackRoomInviteOpensTheBackRoomDoor() => Run(() =>
    {
        int opened = 0;
        FriendsLanding.OpenBackRoom = () => opened++;
        _svc.Deliver(Invite(InviteDestination.BackRoom, null));
        Assert.Single(KnockCard.Up).Fold(KnockOutcome.Go);
        Assert.Equal(1, opened);
    });

    [Fact]
    public Task ARemoteInviteNeverLandsAndNeverOpensAnything() => Run(() =>
    {
        int opened = 0;
        string? joined = null;
        FriendsLanding.OpenBackRoom = () => opened++;
        FriendsLanding.JoinChess = id => joined = id;
        var remote = Invite("remote", "ABCD12");
        _svc.Deliver(remote);
        Assert.False(KnockCard.AnyUp);
        Assert.False(_rows.Contains("friends:" + Id1));
        FriendsLanding.OnKnockDone(remote, KnockOutcome.Go);   // even a forged press opens no door
        Assert.Equal(0, opened);
        Assert.Null(joined);
    });

    [Fact]
    public Task NotNowTellsTheSenderAndLeavesNoRow() => Run(() =>
    {
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        Assert.Single(KnockCard.Up).Fold(KnockOutcome.NotNow);
        Assert.True(Reported(Id1, ReceiptState.Declined));
        Assert.False(_rows.Contains("friends:" + Id1));
        Assert.False(KnockCard.AnyUp);
    });

    [Fact]
    public Task TheSmallXPutsItInTheInboxAndTheRowReopensTheCard() => Run(() =>
    {
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        Assert.Single(KnockCard.Up).Fold(KnockOutcome.Later);
        Assert.False(Reported(Id1, ReceiptState.Declined));
        var row = Assert.Single(_rows.Items);
        Assert.Equal("friends:" + Id1, row.Key);
        _bell = false;            // opened by hand: the card comes up whatever the bell says
        row.Open();
        Assert.Single(KnockCard.Up);
    });

    [Fact]
    public Task AnInviteLivesFiveMinutesThenTheCardGoesWithoutARowOrAnAnswer() => Run(() =>
    {
        var item = Invite(InviteDestination.Chess, Challenge);
        _svc.Deliver(item);
        var card = Assert.Single(KnockCard.Up);
        card.Advance(item.At.AddSeconds(LandingRules.KnockRingSeconds + 1));
        Assert.True(card.IsTucked, "after the ring it tucks to the small strip");
        Assert.True(KnockCard.AnyUp);
        card.Advance(item.ExpiresAt.AddSeconds(1));
        Assert.False(KnockCard.AnyUp);
        Assert.Empty(_rows.Items);
        Assert.False(Reported(Id1, ReceiptState.Declined));
        Assert.False(Reported(Id1, ReceiptState.Joined));
    });

    [Fact]
    public Task TheSameInviteNeverStacksATwinCard() => Run(() =>
    {
        var item = Invite(InviteDestination.Chess, Challenge);
        _svc.Deliver(item);
        FriendsLanding.Reopen(item);
        Assert.Single(KnockCard.Up);
    });

    // ---------------------------------------------------------------- watches and requests

    [Fact]
    public Task AWatchLandsAsANoticeAndWaitsInTheInboxWhenItLeavesUnanswered() => Run(() =>
    {
        _svc.Deliver(WatchItem());
        var w = FriendNotices.Current!;
        Assert.Equal(NoticeKind.Watch, Assert.Single(w.Up).Kind);
        Assert.True(Reported(Id1, ReceiptState.Seen));
        w.PressClose(w.Up[0]);
        Assert.False(FriendNotices.AnyUp);
        Assert.True(_rows.Contains("friends:" + Id1));
    });

    [Fact]
    public Task AFriendRequestFilesARowAndAnnouncesWithAccept() => Run(() =>
    {
        var req = new FriendRequest("u_bo", "Bo", null, null, ServerClock.UtcNow);
        _svc.Request(req);
        Assert.True(_rows.Contains("friends-request:u_bo"));
        var w = FriendNotices.Current!;
        Assert.Equal(NoticeKind.Request, Assert.Single(w.Up).Kind);
        w.PressAction(w.Up[0]);
        Assert.Equal("u_bo", Assert.Single(_svc.Accepted));
        _svc.Gone("u_bo");
        Assert.False(_rows.Contains("friends-request:u_bo"));
    });

    // ---------------------------------------------------------------- holds, panic, focus

    [Fact]
    public Task WhileASessionRunsNothingLandsAndItArrivesWhenTheHoldEnds() => Run(() =>
    {
        _world = _world with { SessionRunning = true };
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        _svc.Deliver(Poke(Id2));
        Assert.False(KnockCard.AnyUp);
        Assert.False(FriendNotices.AnyUp);
        Assert.Empty(_sounds);
        Assert.Empty(_svc.Receipts);
        _world = _world with { SessionRunning = false };
        FriendsLanding.Tick();
        Assert.Single(KnockCard.Up);
        Assert.True(FriendNotices.AnyUp);
    });

    [Fact]
    public Task ASessionStartingFoldsWhatIsUpIntoTheInbox() => Run(() =>
    {
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        _svc.Deliver(Poke(Id2));
        _world = _world with { Lockdown = true };
        FriendsLanding.Tick();
        Assert.False(KnockCard.AnyUp);
        Assert.False(FriendNotices.AnyUp);
        Assert.True(_rows.Contains("friends:" + Id1));
        Assert.True(_rows.Contains("friends:" + Id2));
        Assert.False(Reported(Id1, ReceiptState.Declined));   // a hold never answers for the player
    });

    [Fact]
    public Task WithNothingOfOursOnScreenADeliveryIsAnInboxRow() => Run(() =>
    {
        _world = _world with { PanelVisible = false };
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        Assert.False(KnockCard.AnyUp);
        Assert.True(_rows.Contains("friends:" + Id1));
    });

    [Fact]
    public Task PanicTakesEveryLandingSurfaceDownAtOnceAndAnswersNothing() => Run(() =>
    {
        Assert.Contains(PanicSurfaces.All, s => s.Id == "friends-landing");
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        _svc.Deliver(Poke(Id2));
        var card = KnockCard.Up[0];
        var notices = FriendNotices.Current!;
        FriendsDrawer.MotionLevelNow = () => MotionLevel.Full;   // even with motion on there is no fade to wait for
        PanicSurfaces.All.Single(s => s.Id == "friends-landing").Stop(null);
        Assert.False(KnockCard.AnyUp);
        Assert.False(FriendNotices.AnyUp);
        Assert.False(card.IsVisible);
        Assert.False(notices.IsVisible);
        Assert.True(_rows.Contains("friends:" + Id1));
        Assert.False(Reported(Id1, ReceiptState.Declined));
        Assert.False(Reported(Id1, ReceiptState.Joined));
    });

    [Fact]
    public Task LandingWindowsAreUnownedAndNeverActivated() => Run(() =>
    {
        _svc.Deliver(Invite(InviteDestination.Chess, Challenge));
        _svc.Deliver(Poke(Id2));
        foreach (Window w in new Window[] { KnockCard.Up[0], FriendNotices.Current! })
        {
            Assert.False(w.ShowActivated);
            Assert.Null(w.Owner);          // unowned: showing it cannot lift the panel's chain over another app
            Assert.False(w.ShowInTaskbar);
            Assert.False(w.Focusable);
            Assert.False(w.IsActive);
        }
        Assert.True(_anchor!.IsVisible);
    });

    [Fact]
    public Task TheWorldReadsASessionAsAHold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var before = CoreSession.IsSessionRunningProvider;
        try
        {
            CoreSession.IsSessionRunningProvider = () => true;
            Assert.True(FriendsLanding.ReadWorld().Holding);
            CoreSession.IsSessionRunningProvider = () => false;
            Assert.False(FriendsLanding.ReadWorld().SessionRunning);
        }
        finally { CoreSession.IsSessionRunningProvider = before; }
        return Task.CompletedTask;
    });

    // ---------------------------------------------------------------- small tables

    [Fact]
    public void EveryPokeFaceIsOneTheDeskKnows()
    {
        foreach (var id in PokeSet.All)
            Assert.True(ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiChains.FaceBodyFrame.ContainsKey(LandingRules.PokeFace(id)), id);
    }

    // WPF FriendsKnockTests "the blocked move": once the server lists blocks, this PC's old copy for that
    // account is dropped and nothing is sent again; another account's list is never touched.
    [Fact]
    public async Task TheOldBlockListIsDroppedOnceTheServerListsBlocks()
    {
        var t0 = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        var store = new List<BlockedEntry> { new("me", "u_a", "Ann", t0), new("me", "u_b", "Bo", t0), new("other", "u_z", "Zed", t0) };
        var list = new FriendsBlockList(() => store.ToList(), l => { store.Clear(); store.AddRange(l); });
        Assert.True(await FriendsBlockList.MigrateAsync(list, "me"));
        Assert.Empty(list.For("me"));
        Assert.Single(list.For("other"));
    }

    // ---------------------------------------------------------------- fake

    private sealed class FakeFriends : IFriendsService
    {
        public readonly List<ReceiptReport> Receipts = new();
        public readonly List<(string To, string Poke)> Pokes = new();
        public readonly List<string> Accepted = new();

        public bool Available => true;
        public FriendsSnapshot Snapshot { get; private set; } = FriendsSnapshot.Empty;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged;
        public event Action<FriendItem>? Delivered;
        public event Action<SendKind, Friend>? Sent;
        public event Action<FriendRequest>? RequestArrived;
        public event Action<string>? RequestGone;
        public void Request(FriendRequest r) => RequestArrived?.Invoke(r);
        public void Gone(string id) => RequestGone?.Invoke(id);
        public void Deliver(FriendItem item) => Delivered?.Invoke(item);
        public void RaiseSent(Friend f) { SnapshotChanged?.Invoke(Snapshot); Sent?.Invoke(SendKind.Poke, f); }

        public void ReportReceipt(ReceiptReport report) => Receipts.Add(report);
        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string friendId, string pokeId) { Pokes.Add((friendId, pokeId)); return Task.FromResult(SendResult.Sent); }
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code) => Task.FromResult(SendResult.Sent);
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) => Task.FromResult(SendResult.Sent);
        public Task<AddResult> AddByCodeAsync(string code) => Task.FromResult(AddResult.Sent);
        public Task<ActResult> AcceptAsync(string requesterId) { Accepted.Add(requesterId); return Task.FromResult(ActResult.Done); }
        public Task<ActResult> DeclineAsync(string requesterId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> CancelRequestAsync(string targetId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> RemoveAsync(string friendId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> BlockAsync(string friendId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> UnblockAsync(string friendId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> SetSquelchAsync(string friendId, bool on) => Task.FromResult(ActResult.Done);
        public Task<ActResult> ReportAsync(string friendId, string reason) => Task.FromResult(ActResult.Done);
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
    }
}
