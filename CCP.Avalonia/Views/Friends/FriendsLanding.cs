// PORTED from ConditioningControlPanel/Services/Friends/FriendsLanding.cs (7.1.5): what a delivery does
// when it arrives. A poke goes through Emi, a corner notice and a floating word (a notice alone when a
// game has the screen); an invite knocks with a card; a watch or a friend request shows a corner notice.
// The rules live in Core (LandingRules, FriendsLandingRouter: SEEN receipts, the five-minute invite
// life, deliveries HELD while a lockdown or any session runs and filed in the Inbox when nothing of ours
// is on screen); this class reads the world, draws, and runs the button the player pressed. Nothing here
// ever plays or joins by itself. No free text anywhere: every word is a text key, a preset id or a name.
//
// Start() is called once at startup. FriendsHead.Service can be null (signed out, a sandbox) and can be
// replaced, so a light timer re-attaches to whichever instance is current and releases the held queue
// once a hold ends. Panic (PanicSurfaces "friends-landing") takes every landing surface down at once;
// what was unanswered waits in the Inbox.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using FloatingWord = ConditioningControlPanel.Avalonia.Views.Overlays.FloatingWord;
using StartupInboxItem = ConditioningControlPanel.Services.Startup.InboxItem;

namespace ConditioningControlPanel.Avalonia.Views.Friends;

internal static class FriendsLanding
{
    private static DispatcherTimer? _timer;
    private static FriendsLandingRouter? _router;
    private static Window? _hookedPanel;
    private static readonly Sink TheSink = new();

    // ---------------------------------------------------------------- seams

    /// <summary>The friends service the landing follows (WPF App.Friends). Swappable for the suite.</summary>
    internal static Func<IFriendsService?> Service { get; set; } = () => FriendsHead.Service;

    /// <summary>The world the router decides on. Null = <see cref="ReadWorld"/>. Swappable for the suite.</summary>
    internal static Func<LandingWorld>? WorldOverride { get; set; }

    /// <summary>The window a card or a word lands on. Null = the panel, else the launcher. Swappable for the suite.</summary>
    internal static Func<Window?>? AnchorOverride { get; set; }

    /// <summary>The chess door: the board opens on the friend's challenge (WPF
    /// PieceByPieceHostService.JoinFriendChallenge, FriendsLanding.cs:539).</summary>
    internal static Action<string> JoinChess { get; set; } = id => Games.GameWindow.PbpJoinFriendChallenge(id);

    // SEAM(g3): the Goon host lane sets GoonJoin to its launch door (WPF GoonGame.GoonHostService.Launch(true,
    // joinCode): the game opens straight on the join screen with the code, null = no code came with it) and
    // GoonIsActive to its IsActive probe. Until then a Goon invite's Join says "the Goon Game did not open".
    internal static Action<string?>? GoonJoin { get; set; }
    internal static Func<bool> GoonIsActive { get; set; } = () => false;

    /// <summary>The Back Room door (WPF BackRoomHostService.Launch). Swappable for the suite.</summary>
    internal static Action OpenBackRoom { get; set; } = () =>
    {
        if (MainShellWindow.Current is { } panel && LauncherWindow.Destinations.ContainsKey("backroom"))
            LauncherWindow.LaunchGame(panel, "backroom");
        else Serilog.Log.Information("[Friends] back room join: no door on this head");
    };

    /// <summary>The Inbox the rows go to (WPF App.StartupLadder FileRow / RemoveRow).</summary>
    internal static Func<Services.Startup.StartupInbox?> Rows { get; set; } = () => StartupLadder.Inbox;

    /// <summary>Test seam: what Emi was asked to say (line, face). Null = the desk Emi when she is out.</summary>
    internal static Action<string, string>? EmiOverride { get; set; }

    // ---------------------------------------------------------------- life

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    /// <summary>Exit path. Closes every landing window so none can hold the process open.</summary>
    public static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        _router?.Dispose();
        _router = null;
        try { KnockCard.CloseAll(); } catch { /* shutting down */ }
        try { FriendNotices.CloseAll(); } catch { /* shutting down */ }
    }

    /// <summary>Panic: every landing surface goes at once, no fade. Nothing is answered for the player:
    /// a knock that was up waits in the Inbox while the invite still lives (as a hold does), a poke
    /// files its row. The router keeps running, and it holds while the session or lockdown lasts.</summary>
    public static void CloseAllForPanic()
    {
        try { KnockCard.FoldAll(instant: true); } catch (Exception ex) { Serilog.Log.Debug("[Friends] panic knock: {E}", ex.Message); }
        try { FriendNotices.CloseAll(fold: true); } catch (Exception ex) { Serilog.Log.Debug("[Friends] panic notice: {E}", ex.Message); }
        try
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                foreach (var w in d.Windows.OfType<FloatingWord>().ToArray()) w.Close();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] panic word: {E}", ex.Message); }
    }

    /// <summary>One pass of the landing timer (the suite steps it).</summary>
    internal static void Tick()
    {
        try
        {
            var current = Service();
            if (!ReferenceEquals(_router?.Service, current))
            {
                _router?.Dispose();
                _router = current == null
                    ? null
                    // The server's clock: an invite's at / expires_at are server times, and a
                    // PC running fast must not drop a fresh invite or shorten its countdown.
                    : new FriendsLandingRouter(current, World, () => ServerClock.UtcNow, TheSink);
            }
            _router?.Release();

            // A notice already up when a lockdown or a session starts goes on the next tick.
            if (FriendNotices.AnyUp && World().Holding) FriendNotices.CloseAll(fold: true);
            // So does a knock card, unanswered: it waits in the Inbox while the invite still lives.
            if (KnockCard.AnyUp && World().Holding) KnockCard.FoldAll();

            // The server lists blocks now: carry this PC's old list over once (FriendsBlockList).
            if (current != null) FriendsBlockList.MigrateIfDue(current);

            // The landing windows are unowned: none may outlive the panel.
            var mw = MainShellWindow.Current;
            if (mw != null && !ReferenceEquals(mw, _hookedPanel))
            {
                _hookedPanel = mw;
                mw.Closed += (_, _) => Stop();
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] landing tick: {E}", ex.Message); }
    }

    /// <summary>Test seam: the live router (null signed out).</summary>
    internal static FriendsLandingRouter? Router => _router;
    /// <summary>Test seam: the landing's sink, as the router drives it.</summary>
    internal static ILandingSink SinkForTests => TheSink;

    // ---------------------------------------------------------------- the world

    private static LandingWorld World() => WorldOverride?.Invoke() ?? ReadWorld();

    internal static LandingWorld ReadWorld()
    {
        var session = false;
        try { session = CoreSession.IsSessionRunning; } catch { /* not built yet */ }
        bool lockdown = false, program = false, strict = false;
        try { lockdown = LockdownService.Current?.IsActive == true; } catch { /* not built yet */ }
        try { program = session && App.Programs?.ActiveEnrollment != null; } catch { /* not built yet */ }
        try { strict = session && CoreSettings.Current?.StrictLockEnabled == true; } catch { /* not built yet */ }

        return new LandingWorld(
            Lockdown: lockdown,
            StrictLock: strict,
            ProgramSession: program,
            PanelVisible: Up(Panel()),
            LauncherVisible: Up(Launcher()),
            GameHostActive: ActiveGameWindow() != null,
            SessionRunning: session);
    }

    private static bool Up(Window? w) => w is { IsVisible: true } && w.WindowState != WindowState.Minimized;

    private static MainShellWindow? Panel()
    {
        try { return MainShellWindow.Current; } catch { return null; }
    }

    private static Window? Launcher()
    {
        try { return LauncherWindow.Instance; } catch { return null; }
    }

    /// <summary>The active window when it belongs to a game host (Back Room, race, goon, the Arcademy,
    /// Piece by Piece: every web game is a GameWindow on this head), else null. While a game is up it is
    /// the active window that is neither the panel, the launcher nor one of ours.</summary>
    private static Window? ActiveGameWindow()
    {
        try
        {
            if (!LandingRules.AnyGameHost(new[] { Games.GameWindow.IsAnyOpen() })) return null;
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d) return null;
            foreach (var w in d.Windows)
            {
                if (!w.IsActive) continue;
                if (w is MainShellWindow or LauncherWindow) return null;
                if (w is KnockCard or FriendNotices or FloatingWord) return null;
                return w;
            }
        }
        catch { /* windows collection changed mid-walk */ }
        return null;
    }

    /// <summary>Where a card or a word lands when there is no game: the panel, else the launcher.</summary>
    private static Window? Anchor()
    {
        if (AnchorOverride != null) return AnchorOverride();
        var mw = Panel();
        if (Up(mw)) return mw;
        var lw = Launcher();
        return Up(lw) ? lw : null;
    }

    // ---------------------------------------------------------------- words

    internal static string Str(string key, string english)
    {
        try
        {
            var v = Loc.Get(key);
            return string.IsNullOrWhiteSpace(v) || v == key ? english : v;
        }
        catch { return english; }
    }

    internal static string PokeText(string? pokeId)
        => Str("friends_poke_" + pokeId, LandingRules.PokeFallback(pokeId));

    private static string DestinationName(string? dest) => dest switch
    {
        InviteDestination.Goon => Str("friends_land_dest_goon", "the Goon Game"),
        InviteDestination.Ramp => Str("friends_land_dest_ramp", "a Ramp link"),
        InviteDestination.Chess => Str("friends_land_dest_chess", "a game of chess"),
        _ => Str("friends_land_dest_backroom", "the Back Room"),
    };

    internal static string KnockLine(InboxItem item)
    {
        if (item.Kind == SendKind.Invite)
            return string.Format(Str("friends_land_invite_line", "invites you to {0}"), DestinationName(item.Destination));
        return item.Watch?.Kind == WatchKind.Flavour
            ? Str("friends_land_flavour_line", "sends you a flavour")
            : Str("friends_land_watch_line", "sends you a watch");
    }

    private static string GoLabel(InboxItem item)
    {
        if (item.Kind == SendKind.Invite) return Str("friends_land_join", "Join");
        return item.Watch?.Kind == WatchKind.Flavour
            ? Str("friends_land_try", "Try it")
            : Str("friends_land_watch", "Watch");
    }

    // ---------------------------------------------------------------- the sink

    private sealed class Sink : ILandingSink
    {
        public void Poke(InboxItem item, bool inGame)
        {
            var word = PokeText(item.PokeId);
            var pink = LandingRules.PokeIsPink(item.PokeId);
            // Bell off means silent: the Inbox row and nothing else, not even the cue.
            if (!NoticesOn) { Inbox(item); return; }
            FriendsSfx.PokeIn(inGame);
            var game = inGame ? ActiveGameWindow() : null;
            FriendNotices.Show(game ?? Anchor(), Notice(NoticeKind.Poke, item.FromId, item.FromName, item, item.At), new NoticeLook
            {
                Line = Str("friends_notice_poked", "poked you:"),
                Word = word,
                Pink = pink,
                AvatarUrl = item.FromAvatarUrl,
                ActionLabel = Str("friends_notice_poke_back", "Poke back"),
                Act = p => { if (p is InboxItem i) PokeBack(i); },
                Open = p => OpenDrawer((p as InboxItem)?.FromId),
                // Ran out unseen (or pushed off the stack): it waits in the Inbox instead of vanishing.
                Missed = p => { if (p is InboxItem i) Inbox(i); },
            });
            // On screen now: the sender's trail may say seen.
            FriendsSeen.Shared.Seen(Service(), item.Id);
            if (game != null) return;
            EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word),
                LandingRules.PokeFace(item.PokeId));
            var anchor = Anchor();
            if (anchor != null)
                FloatingWord.Throw(anchor, word, pink);
        }

        public void Knock(InboxItem item, bool inGame)
        {
            var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (anchor == null) { Inbox(item); return; }
            if (!NoticesOn) { Inbox(item); return; }
            FriendsSfx.Knock();
            if (!inGame)
                EmiSays(item.Kind == SendKind.Invite
                        ? Str("friends_land_emi_knock", "someone wants you")
                        : Str("friends_land_emi_present", "a present"),
                    item.Kind == SendKind.Invite ? "o_o" : "^_~");
            if (item.Kind == SendKind.Invite)
            {
                // An invite knocks with a card that counts down to the invite's own expiry (five
                // minutes): Join, Not now (the sender hears it), or the x to answer later.
                ShowCard(anchor, item);
                return;
            }
            FriendNotices.Show(anchor, Notice(NoticeKind.Watch, item.FromId, item.FromName, item, item.At), new NoticeLook
            {
                Line = KnockLine(item),
                AvatarUrl = item.FromAvatarUrl,
                ActionLabel = GoLabel(item),
                Act = p => { if (p is InboxItem i && !i.IsExpired(ServerClock.UtcNow)) OnKnockDone(i, KnockOutcome.Go); },
                Open = p => OpenDrawer((p as InboxItem)?.FromId),
                // Still answerable after the toast goes: it waits in the Inbox, where it reopens as a card.
                Left = p => { if (p is InboxItem i && !i.IsExpired(ServerClock.UtcNow)) Inbox(i); },
            });
            FriendsSeen.Shared.Seen(Service(), item.Id);
        }

        public void Inbox(InboxItem item)
        {
            var title = item.Kind == SendKind.Poke
                ? string.Format(Str("friends_land_inbox_poke", "{0}: {1}"), item.FromName, PokeText(item.PokeId))
                : item.FromName;
            FileRow(new StartupInboxItem
            {
                Key = "friends:" + item.Id,
                Title = title,
                Summary = item.Kind == SendKind.Poke ? "" : KnockLine(item),
                Glyph = item.Kind == SendKind.Poke ? "✨" : item.Kind == SendKind.Invite ? "🚪" : "🎁",
                Open = () => Reopen(item),
            });
        }

        public void RequestRow(FriendRequest request) => FileRow(new StartupInboxItem
        {
            Key = RequestKey(request.Id),
            Title = request.Name,
            Summary = Str("friends_land_request_line", "wants to be friends"),
            Glyph = "💌",
            Open = () => OpenDrawer(null),
        });

        public void RequestAnnounce(FriendRequest request, bool inGame)
        {
            if (!NoticesOn) return;
            var owner = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (owner == null) return;
            FriendsSfx.Request();
            FriendNotices.Show(owner, Notice(NoticeKind.Request, request.Id, request.Name, request, request.At), new NoticeLook
            {
                Line = Str("friends_land_request_line", "wants to be friends"),
                AvatarUrl = request.AvatarUrl,
                ActionLabel = Str("friends_notice_accept", "Accept"),
                Act = p => { if (p is FriendRequest r) Accept(r); },
                Open = _ => OpenDrawer(null),
            });
            FriendsSeen.Shared.RequestSeen(Service(), request);
        }

        public void RequestCue()
        {
            if (NoticesOn) FriendsSfx.Request();
        }

        public void RequestGone(string requestId) => RemoveRow(RequestKey(requestId));

        public void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame)
        {
            if (waiting.Count == 0) { RemoveRow(WaitingKey); return; }
            var newest = waiting[0];
            foreach (var r in waiting) if (r.At > newest.At) newest = r;
            var title = waiting.Count == 1
                ? newest.Name
                : string.Format(Str("friends_land_waiting_many", "{0} friend requests waiting"), waiting.Count);
            var summary = waiting.Count == 1 ? Str("friends_land_request_line", "wants to be friends") : "";
            // A shorter list re-words the row quietly: the old wording goes, the new one is filed.
            RemoveRow(WaitingKey);
            FileRow(new StartupInboxItem
            {
                Key = WaitingKey,
                Title = title,
                Summary = summary,
                Glyph = "💌",
                Open = () => OpenDrawer(null),
            });
            if (!announce || !NoticesOn) return;
            var owner = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (owner == null) return;
            FriendsSfx.Request();
            FriendNotices.Show(owner, Notice(NoticeKind.Request, WaitingKey, waiting.Count == 1 ? newest.Name : "", newest, newest.At),
                new NoticeLook
                {
                    Line = waiting.Count == 1 ? summary : title,
                    AvatarUrl = waiting.Count == 1 ? newest.AvatarUrl : null,
                    ActionLabel = Str("friends_land_waiting_open", "Open"),
                    Act = _ => OpenDrawer(null),
                    Open = _ => OpenDrawer(null),
                });
            // One request by name on screen is seen; "3 friend requests waiting" names nobody, so
            // those wait for their rows in the drawer.
            if (waiting.Count == 1) FriendsSeen.Shared.RequestSeen(Service(), newest);
        }

        public void SentBeat(SendKind kind, Friend to)
        {
            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor == null) return;
            var word = kind switch
            {
                SendKind.Invite => Str("friends_land_knock_sent", "knock sent"),
                SendKind.Watch => Str("friends_land_watch_sent", "watch sent"),
                _ => Str("friends_land_poke_sent", "sent"),
            };
            FloatingWord.Throw(anchor, word, pink: false, small: true);
        }
    }

    private static string RequestKey(string id) => "friends-request:" + id;

    private const string WaitingKey = "friends-requests-waiting";

    // ---------------------------------------------------------------- Inbox rows

    private static void FileRow(StartupInboxItem row)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => FileRow(row)); return; }
        try { Rows()?.File(row); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] inbox row: {E}", ex.Message); }
    }

    private static void RemoveRow(string key)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => RemoveRow(key)); return; }
        try { Rows()?.Remove(key); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] inbox row gone: {E}", ex.Message); }
    }

    // ---------------------------------------------------------------- corner notices

    /// <summary>The drawer's bell (AppSettings.FriendNotificationsEnabled, FriendsDrawer.NoticesOn).
    /// Off: Inbox rows only, nothing on screen and no cue.</summary>
    private static bool NoticesOn
    {
        get { try { return FriendsDrawer.NoticesOn(); } catch { return true; } }
    }

    /// <summary>A notice dated by when it was SENT (the server's <c>at</c>), not when it landed here.</summary>
    private static FriendNotice Notice(NoticeKind kind, string friendId, string name, object payload, DateTimeOffset sent)
        => new(kind, friendId, name, LandingRules.NoticeAt(sent, DateTimeOffset.UtcNow), FriendNoticeRules.LifetimeMs(kind), payload);

    private static void ShowCard(Window anchor, InboxItem item) =>
        KnockCard.Show(anchor, item, KnockLine(item), GoLabel(item),
            Str("friends_land_not_now", "Not now"), Str("friends_land_answer_later", "Answer later"),
            OnKnockDone, shown: i => FriendsSeen.Shared.Seen(Service(), i.Id));

    /// <summary>The drawer's way to word a result it can no longer show.</summary>
    internal static void Tell(string text, bool good) => Say(text, good);

    /// <summary>A short word where the player is looking (the game, the panel, the launcher).</summary>
    private static void Say(string text, bool good)
    {
        var anchor = ActiveGameWindow() ?? Anchor();
        if (anchor != null) FloatingWord.Throw(anchor, text, pink: !good, small: true);
    }

    /// <summary>The same preset straight back, through the drawer's own send path. A preset id only.</summary>
    private static async void PokeBack(InboxItem item)
    {
        try
        {
            var svc = Service();
            if (svc == null || string.IsNullOrEmpty(item.FromId)) return;
            FriendsSfx.Click();
            var r = await svc.PokeAsync(item.FromId, string.IsNullOrEmpty(item.PokeId) ? "hi" : item.PokeId);
            // A good send has its own beat (the router's SentBeat); only a refusal needs words here.
            if (FriendsDrawerRules.IsGood(r)) return;
            Serilog.Log.Debug("[Friends] poke back: {R}", r);
            FriendsSfx.Denied();
            Say(Loc.Get(FriendsDrawerRules.SendResultKey(r)), good: false);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] poke back: {E}", ex.Message); }
    }

    private static async void Accept(FriendRequest request)
    {
        try
        {
            var svc = Service();
            if (svc == null) return;
            FriendsSfx.Click();
            var r = await svc.AcceptAsync(request.Id);
            if (r == ActResult.Done)
            {
                FriendsSfx.Accepted();
                Say(Loc.Get("friends_add_accepted"), good: true);
                return;
            }
            FriendsSfx.Denied();
            Say(Loc.Get(FriendsDrawerRules.ActResultKey(r)), good: false);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] accept: {E}", ex.Message); }
    }

    /// <summary>A request row or a notice body opened: the panel up, the friends drawer open on it.</summary>
    private static void OpenDrawer(string? friendId)
    {
        try
        {
            var mw = Panel();
            if (mw == null) return;
            if (!Up(mw)) LauncherWindow.OpenPanel(mw);
            // After the panel has had a layout pass: a popup placed on a hidden chip lands at 0,0.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var chip = mw.FindControl<FriendsRailChip>("FriendsChip");
                    if (chip == null) return;
                    if (!chip.IsOpen) chip.Toggle();
                    if (!string.IsNullOrEmpty(friendId)) chip.Drawer.OpenOn(friendId);
                }
                catch (Exception ex) { Serilog.Log.Debug("[Friends] open drawer: {E}", ex.Message); }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] open drawer: {E}", ex.Message); }
    }

    /// <summary>An Inbox row opened later: the card again while it is still answerable.</summary>
    internal static void Reopen(InboxItem item)
    {
        if (item.IsExpired(ServerClock.UtcNow))
        {
            EmiSays(Str("friends_land_too_late", "that one already left"), ";_;");
            return;
        }
        if (item.Kind == SendKind.Poke) TheSink.Poke(item, inGame: false);
        // Opened by hand: the card comes up whatever the bell says (the bell only silences arrivals).
        else if (item.Kind == SendKind.Invite && Anchor() is { } anchor) ShowCard(anchor, item);
        else TheSink.Knock(item, inGame: false);
    }

    private static void EmiSays(string line, string face)
    {
        try
        {
            if (EmiOverride != null) { EmiOverride(line, face); return; }
            var desk = Windows.EmiDesk.EmiDeskService.Instance;
            if (desk.IsOut && desk.Window != null) desk.Window.Say(line, face);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] emi say: {E}", ex.Message); }
    }

    internal static void OnKnockDone(InboxItem item, KnockOutcome outcome)
    {
        var now = ServerClock.UtcNow;
        switch (outcome)
        {
            case KnockOutcome.Later:
                // Put away unanswered: it waits in the Inbox while it can still be answered.
                if (!item.IsExpired(now)) TheSink.Inbox(item);
                return;
            case KnockOutcome.RanOut:
                // A watch keeps for a day on the server: it folds into the Inbox rather than vanishing.
                // An invite that ran out says nothing here; the server tells the sender "no answer".
                if (item.Kind == SendKind.Watch && !item.IsExpired(now)) TheSink.Inbox(item);
                return;
            case KnockOutcome.NotNow:
                // The sender hears "not now" instead of waiting out the five minutes.
                FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Declined));
                RemoveRow("friends:" + item.Id);
                FriendsSfx.Dismiss();
                return;
        }
        RemoveRow("friends:" + item.Id);
        if (item.Kind == SendKind.Invite)
        {
            FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Joined));
            FriendsSfx.Join();
            Join(item);
        }
        else if (item.Watch != null) Watch(item.Watch);
    }

    // ---------------------------------------------------------------- join

    private static void Join(InboxItem item)
    {
        Serilog.Log.Information("[Friends] joining {Dest} from a knock", item.Destination);
        switch (item.Destination)
        {
            case InviteDestination.Goon:
                JoinGoon(item.Code);
                return;

            case InviteDestination.BackRoom:
                try { OpenBackRoom(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] back room launch failed"); }
                return;

            case InviteDestination.Chess:
                // The code is the friend's challenge: the board opens and takes it up at once.
                if (!InviteDestination.IsChallengeId(item.Code)) return;
                try { JoinChess(item.Code!); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] chess launch failed"); }
                return;

            case InviteDestination.Ramp:
                // Link to Ramp lives on the Ramp card in the Studio rack; one tab away, not one call.
                OpenTab("studio");
                return;

            default:
                // "remote" is refused over friends (CONTRACT): nothing opens, and the log says so.
                Serilog.Log.Information("[Friends] join refused: destination {Dest} is not a friends door", item.Destination);
                return;
        }
    }

    /// <summary>The Goon invite: the game opens straight on the join screen with the code. A window
    /// already up gets the code as a frame, which the page only takes between matches, so the player is
    /// told, and the code waits on the clipboard for after the match.</summary>
    private static void JoinGoon(string? code)
    {
        var codeOk = LandingRules.JoinCodeOk(code);
        bool alreadyUp;
        try { alreadyUp = GoonIsActive(); } catch { alreadyUp = false; }
        if (codeOk && alreadyUp)
        {
            try { _ = (Anchor() ?? Panel())?.Clipboard?.SetTextAsync(code!); }
            catch (Exception ex) { Serilog.Log.Debug("[Friends] clipboard: {E}", ex.Message); }
        }
        try
        {
            // SEAM(g3): no Goon host on this head yet reads as "did not open", never as a silent nothing.
            if (GoonJoin == null) throw new InvalidOperationException("no Goon host on this head (SEAM g3)");
            GoonJoin(codeOk ? code : null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("[Friends] goon launch failed: {E}", ex.Message);
            FriendsSfx.Denied();
            Say(Str("friends_land_goon_failed", "the Goon Game did not open"), good: false);
            return;
        }
        if (!codeOk)
        {
            Say(Str("friends_land_goon_no_code", "no code came with it, ask again"), good: false);
            return;
        }
        if (alreadyUp) EmiOrSay(Str("friends_land_goon_busy", "in a match? finish it, the code is copied"));
    }

    /// <summary>A line through Emi when she is out, else as a word where the player is looking.</summary>
    private static void EmiOrSay(string line)
    {
        try
        {
            if (EmiOverride != null) { EmiOverride(line, "o_o"); return; }
            var desk = Windows.EmiDesk.EmiDeskService.Instance;
            if (desk.IsOut && desk.Window != null) { desk.Window.Say(line, "o_o"); return; }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] emi say: {E}", ex.Message); }
        Say(line, good: true);
    }

    // ---------------------------------------------------------------- watch

    private static void Watch(WatchRef watch)
    {
        if (!watch.IsValid()) return;
        Serilog.Log.Information("[Friends] watch {Kind} from a knock", watch.Kind);
        switch (watch.Kind)
        {
            case WatchKind.Catalogue: WatchCatalogue(watch.Id); return;
            case WatchKind.Flavour: WatchFlavour(watch.Id); return;
            case WatchKind.Ht: WatchHt(watch.Id); return;
        }
    }

    private static void WatchCatalogue(string catalogueId)
    {
        string? path = null;
        try
        {
            var subs = CoreSettings.Current?.DeeperSubmissions;
            if (subs != null)
                foreach (var kv in subs)
                    if (string.Equals(kv.Value?.CatalogueId, catalogueId, StringComparison.Ordinal) && File.Exists(kv.Key))
                    { path = kv.Key; break; }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] catalogue lookup: {E}", ex.Message); }

        var mw = Panel();
        if (path == null || mw == null) { OpenTab("deeper"); return; }
        try { mw.PlayDeeperLibraryEntry(path); }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Friends] deeper player failed");
            OpenTab("deeper");
        }
    }

    /// <summary>WPF WatchFlavour + LauncherMediaSettings.Apply. A local-only player keeps their source:
    /// pulling from the net is a consent the panel asks for, never one a friend's watch grants.</summary>
    internal static void WatchFlavour(string flavour)
    {
        var s = CoreSettings.Current;
        if (s == null) return;
        var niches = LandingRules.FlavourNiches(flavour);
        if (niches.Count == 0) return;
        try
        {
            var source = s.MediaSource;
            if (source is not ("local" or "online" or "mixed")) source = "local";
            if (source == "local" && s.HasRemoteMediaConsent) source = "mixed";
            s.MediaSource = source;
            s.FypOnlineNiches = new List<string>(niches);
            // The room follows the app again (LauncherMediaSettings.Apply): a stale room override is the bug.
            s.BackRoomMediaSource = "auto";
            s.BackRoomMediaSubs = new List<string>();
            s.BackRoomMediaSubsOff = new List<string>();
            Services.Fyp.Online.FypOnlineCoordinator.ResetAllChannels();
            CoreSettings.Save();
            AssetSelection.NotifyChanged();

            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor != null)
                FloatingWord.Throw(anchor, Str("friends_land_flavour_on", "flavour on"), pink: true, small: true);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] flavour apply failed"); }
    }

    private static void WatchHt(string id)
    {
        var url = LandingRules.HtUrl(id);
        if (url == null) return;
        var mw = Panel();
        if (mw == null) return;
        try
        {
            LauncherWindow.OpenPanel(mw);
            mw.NavigateToUrlInBrowser(url, autoPlayFullscreen: false, userInitiated: true);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] ht watch failed"); }
    }

    private static void OpenTab(string tab)
    {
        try
        {
            var mw = Panel();
            if (mw == null) return;
            LauncherWindow.OpenPanel(mw);
            mw.ShowTab(tab);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] open tab {Tab}: {E}", tab, ex.Message); }
    }
}
