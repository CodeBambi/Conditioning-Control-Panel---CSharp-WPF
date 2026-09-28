using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

// What a delivery does, as pure rules. No WPF, no App statics: FriendsLanding reads the world
// and hands it in, the tests hand in whatever world they like. The windows only draw.

/// <summary>What the app looks like at the moment a delivery lands.</summary>
/// <param name="Lockdown">A lockdown is running. Nothing interrupts it.</param>
/// <param name="StrictLock">Strict Lock is on for a running session.</param>
/// <param name="ProgramSession">A session started by a program is running.</param>
/// <param name="PanelVisible">The panel window is on screen (not tray-hidden, not minimised).</param>
/// <param name="LauncherVisible">The launcher window is on screen.</param>
/// <param name="GameHostActive">A game host (Back Room, race, goon) is the active window.</param>
public readonly record struct LandingWorld(
    bool Lockdown,
    bool StrictLock,
    bool ProgramSession,
    bool PanelVisible,
    bool LauncherVisible,
    bool GameHostActive)
{
    /// <summary>The three states that hold every delivery until they end.</summary>
    public bool Holding => Lockdown || StrictLock || ProgramSession;
}

/// <summary>Where one delivery goes right now.</summary>
public enum LandingRoute
{
    /// <summary>Expired: never shown.</summary>
    Drop,
    /// <summary>Wait in the queue until the hold ends.</summary>
    Hold,
    /// <summary>Park as an Inbox row: nothing of ours is on screen.</summary>
    Inbox,
    /// <summary>A game host has the screen: poke as a compact toast, knock card over the game.</summary>
    InGame,
    /// <summary>The panel or the launcher is up: Emi, the floating word, the knock card.</summary>
    Present,
}

public static class LandingRules
{
    /// <summary>How long a watch knock card stays up. A watch lives 24 h on the server; the card
    /// is a knock, not a shelf, and folds into the Inbox when it runs out.</summary>
    public const int WatchCardSeconds = 45;

    /// <summary>The queue never grows past this; the oldest goes first.</summary>
    public const int QueueCap = 10;

    public static LandingRoute Decide(InboxItem item, LandingWorld world, DateTimeOffset now)
    {
        if (item == null || item.IsExpired(now)) return LandingRoute.Drop;
        if (IsRetired(item)) return LandingRoute.Drop;
        if (world.Holding) return LandingRoute.Hold;
        if (world.GameHostActive) return LandingRoute.InGame;
        if (world.PanelVisible || world.LauncherVisible) return LandingRoute.Present;
        return LandingRoute.Inbox;
    }

    /// <summary>True when any game host is up. One flag per host, so adding a host is one probe.</summary>
    public static bool AnyGameHost(IReadOnlyList<bool> hostsUp)
    {
        if (hostsUp == null) return false;
        foreach (var up in hostsUp) if (up) return true;
        return false;
    }

    /// <summary>A delivery nobody can answer any more: a Remote invite from an old client (the
    /// PIN never travelled, owner dropped the tile 2026-09-28). Shown as nothing.</summary>
    public static bool IsRetired(InboxItem item)
        => item.Kind == SendKind.Invite && item.Destination == InviteDestination.Remote;

    /// <summary>When a notice says it arrived: the server's send time, so a poke held through a
    /// lockdown reads as old. Clamped to now (a fast server clock) and to a day back (a broken one).</summary>
    public static DateTimeOffset NoticeAt(DateTimeOffset sent, DateTimeOffset now)
    {
        if (sent == DateTimeOffset.MinValue || sent > now) return now;
        var floor = now.AddDays(-1);
        return sent < floor ? floor : sent;
    }

    /// <summary>When the knock card folds: an invite at its own expiry (90 s from At), a watch
    /// after <see cref="WatchCardSeconds"/> from now or its expiry, whichever is first.</summary>
    public static DateTimeOffset KnockEnds(InboxItem item, DateTimeOffset now)
    {
        if (item.Kind == SendKind.Invite)
        {
            var fromAt = item.At.AddSeconds(InviteDestination.LifetimeSeconds);
            return fromAt < item.ExpiresAt ? fromAt : item.ExpiresAt;
        }
        var card = now.AddSeconds(WatchCardSeconds);
        return card < item.ExpiresAt ? card : item.ExpiresAt;
    }

    /// <summary>Fraction of the ring still lit, 1 at the start, 0 when it runs out, clamped.</summary>
    public static double RingFraction(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var total = (end - start).TotalMilliseconds;
        if (total <= 0) return 0;
        var left = (end - now).TotalMilliseconds;
        return Math.Clamp(left / total, 0, 1);
    }

    /// <summary>Whole seconds left, never negative, rounded up so the card never reads 0 while lit.</summary>
    public static int SecondsLeft(DateTimeOffset end, DateTimeOffset now)
    {
        var s = (end - now).TotalSeconds;
        return s <= 0 ? 0 : (int)Math.Ceiling(s);
    }

    /// <summary>The end point of a ring arc drawn clockwise from 12 o'clock over
    /// <paramref name="fraction"/> of the circle, on a circle of <paramref name="radius"/>
    /// centred at (<paramref name="cx"/>, <paramref name="cy"/>). Screen coordinates (y down).</summary>
    public static (double X, double Y) RingPoint(double cx, double cy, double radius, double fraction)
    {
        var a = Math.Clamp(fraction, 0, 1) * 2 * Math.PI;
        return (cx + radius * Math.Sin(a), cy - radius * Math.Cos(a));
    }

    // ---------------------------------------------------------------- pokes

    private static readonly Dictionary<string, string> PokeEnglish = new(StringComparer.Ordinal)
    {
        ["hi"] = "hi", ["wp"] = "well played", ["tut"] = "tut tut", ["gl"] = "good luck",
        ["oops"] = "oops", ["thanks"] = "thanks", ["drop"] = "drop", ["peek"] = "no peeking",
        ["deeper"] = "deeper", ["still"] = "sit still", ["more"] = "one more", ["sleepy"] = "sleepy?",
    };

    /// <summary>The English poke text, used when the drawer's <c>friends_poke_*</c> key is missing.</summary>
    public static string PokeFallback(string? pokeId)
        => pokeId != null && PokeEnglish.TryGetValue(pokeId, out var t) ? t : "hi";

    /// <summary>Pink pokes tease, mint pokes are friendly. The floating word wears this.</summary>
    public static bool PokeIsPink(string? pokeId) => pokeId switch
    {
        "tut" or "oops" or "drop" or "peek" or "deeper" or "still" or "more" => true,
        _ => false,
    };

    /// <summary>The face Emi reacts with. Every value is a face EmiChains knows.</summary>
    public static string PokeFace(string? pokeId) => pokeId switch
    {
        "hi" => "^_^",
        "wp" => "\\o/",
        "gl" => "^_~",
        "thanks" => "<3",
        "tut" => "\u00AC_\u00AC",
        "oops" => ">_<",
        "drop" => "*_*",
        "peek" => ">:)",
        "deeper" => "*_*",
        "still" => "\u00AC_\u00AC",
        "more" => "B)",
        "sleepy" => "o_o",
        _ => "^_^",
    };

    // ---------------------------------------------------------------- watches

    /// <summary>A flavour watch maps onto the app's own Scrolller niche ids (FypOnlineCoordinator.Catalog).</summary>
    public static IReadOnlyList<string> FlavourNiches(string? flavour) => flavour switch
    {
        "trance" => new[] { "hypno", "bambisleep" },
        "pink" => new[] { "bimbo" },
        "frills" => new[] { "sissy" },
        "shiny" => new[] { "cosplay" },
        "censored" => new[] { "censored", "beta" },
        _ => Array.Empty<string>(),
    };

    /// <summary>The one allowlisted host, pattern A of HtUrlHelper. Null for anything but digits.</summary>
    public static string? HtUrl(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 8) return null;
        foreach (var c in id) if (!char.IsAsciiDigit(c)) return null;
        return "https://hypnotube.com/video/" + id;
    }

    /// <summary>The wire grammar for a system join code: 4 to 12 of A-Z, 0-9 and '-'.</summary>
    public static bool JoinCodeOk(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 4 || code.Length > 12) return false;
        foreach (var c in code)
            if (!((c >= 'A' && c <= 'Z') || char.IsAsciiDigit(c) || c == '-')) return false;
        return true;
    }

    /// <summary>The picture a knock card shows, as a path under Resources/.</summary>
    public static string KnockArt(InboxItem item)
    {
        if (item.Kind == SendKind.Invite)
            return item.Destination switch
            {
                InviteDestination.Goon => "features/goon_game_tile.png",
                InviteDestination.Ramp => "features/Phrase_Lock.png",
                _ => "features/backroom.png",
            };
        return item.Watch?.Kind == WatchKind.Flavour ? "features/backroom.png" : "features/deeper.png";
    }
}

/// <summary>The held deliveries. Arrival order, de-duplicated by id, capped at
/// <see cref="LandingRules.QueueCap"/> (the oldest goes), expired ones dropped on drain.</summary>
public sealed class LandingQueue
{
    private readonly List<InboxItem> _items = new();
    private readonly int _cap;

    public LandingQueue(int cap = LandingRules.QueueCap) { _cap = Math.Max(1, cap); }

    public int Count => _items.Count;

    public void Add(InboxItem item)
    {
        if (item == null) return;
        foreach (var i in _items) if (i.Id == item.Id) return;
        _items.Add(item);
        while (_items.Count > _cap) _items.RemoveAt(0);
    }

    /// <summary>Takes everything still alive, in arrival order, and empties the queue.</summary>
    public List<InboxItem> Drain(DateTimeOffset now)
    {
        var alive = new List<InboxItem>(_items.Count);
        foreach (var i in _items) if (!i.IsExpired(now)) alive.Add(i);
        _items.Clear();
        return alive;
    }
}

/// <summary>What the landing side can do on screen. FriendsLanding implements it with windows;
/// the tests record the calls.</summary>
public interface ILandingSink
{
    void Poke(InboxItem item, bool inGame);
    void Knock(InboxItem item, bool inGame);
    void Inbox(InboxItem item);
    void SentBeat(SendKind kind, Friend to);

    /// <summary>Files an incoming friend request as an Inbox row. Always, whatever the world.</summary>
    void RequestRow(FriendRequest request);

    /// <summary>The request's cue and toast, while something of ours (or a game) has the screen.</summary>
    void RequestAnnounce(FriendRequest request, bool inGame);

    /// <summary>One request cue after a hold ends, for the requests that arrived during it.</summary>
    void RequestCue();

    /// <summary>The request left the list (accepted, declined, withdrawn): its row goes.</summary>
    void RequestGone(string requestId);

    /// <summary>Requests that were already waiting when the list first arrived (launch, sign in):
    /// one Inbox row for all of them, and one notice when <paramref name="announce"/>. An empty
    /// list takes that row back; a shorter one re-words it quietly.</summary>
    void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame);
}

/// <summary>
/// The requests that were already there on the first list after a start or a sign in. The
/// service takes that list as a silent baseline (<see cref="FriendRequestWatch"/>), so without
/// this nobody would ever hear about them. Fed every snapshot and every request event; the
/// router asks <see cref="Take"/> on its tick, after the service has raised its own arrivals
/// for the same list, so a request is never announced twice.
/// </summary>
public sealed class WaitingRequests
{
    private bool _seeded;
    private List<FriendRequest> _pending = new();
    private HashSet<string> _filed = new(StringComparer.Ordinal);

    /// <summary>Ids the one waiting row currently speaks for.</summary>
    public IReadOnlyCollection<string> Filed => _filed;

    /// <summary>A snapshot arrived. The first one with anything in it is the baseline. An empty
    /// one (signed out, account changed) starts over. Returns true when the filed row changed
    /// (a filed request was answered) and should be re-worded or taken back.</summary>
    public bool OnSnapshot(FriendsSnapshot snap)
    {
        if (snap == null) return false;
        bool empty = snap.Friends.Count == 0 && snap.Incoming.Count == 0 && snap.Outgoing.Count == 0
            && string.IsNullOrEmpty(snap.MyCode);
        if (empty)
        {
            _seeded = false;
            _pending.Clear();
            if (_filed.Count == 0) return false;
            _filed.Clear();
            return true;
        }
        if (!_seeded)
        {
            _seeded = true;
            _pending = new List<FriendRequest>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in snap.Incoming)
                if (r != null && !string.IsNullOrEmpty(r.Id) && seen.Add(r.Id)) _pending.Add(r);
        }
        if (_filed.Count == 0) return false;
        var still = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in snap.Incoming) if (r != null && _filed.Contains(r.Id)) still.Add(r.Id);
        if (still.Count == _filed.Count) return false;
        _filed = still;
        return true;
    }

    /// <summary>The service raised this one on its own (it is new, or gone): not ours to announce.</summary>
    public void Forget(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _pending.RemoveAll(r => r.Id == id);
    }

    public bool HasPending => _pending.Count > 0;

    /// <summary>Takes the waiting ones to announce, once. They become the filed row.</summary>
    public IReadOnlyList<FriendRequest> Take()
    {
        var list = _pending;
        _pending = new List<FriendRequest>();
        foreach (var r in list) _filed.Add(r.Id);
        return list;
    }
}

/// <summary>The routing, as a plain class over the service: subscribe, decide, hold, release.
/// Knows nothing about windows; the sink does the drawing.</summary>
public sealed class FriendsLandingRouter : IDisposable
{
    private readonly IFriendsService _service;
    private readonly Func<LandingWorld> _world;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILandingSink _sink;
    private readonly LandingQueue _queue = new();

    public FriendsLandingRouter(IFriendsService service, Func<LandingWorld> world, Func<DateTimeOffset> now, ILandingSink sink)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _world = world;
        _now = now;
        _sink = sink;
        _service.Delivered += OnDelivered;
        _service.Sent += OnSent;
        _service.RequestArrived += OnRequestArrived;
        _service.RequestGone += OnRequestGone;
        _service.SnapshotChanged += OnSnapshot;
        OnSnapshot(_service.Snapshot);
    }

    private bool _requestCuePending;
    private readonly WaitingRequests _waiting = new();

    /// <summary>True while requests from the first list wait for the next tick to be announced.</summary>
    public bool WaitingPending => _waiting.HasPending;

    private void OnSnapshot(FriendsSnapshot snap)
    {
        if (!_waiting.OnSnapshot(snap)) return;
        // A filed request was answered (or the account went): re-word the one row quietly.
        var left = new List<FriendRequest>();
        foreach (var r in snap.Incoming) if (r != null && _waiting.Filed.Contains(r.Id)) left.Add(r);
        _sink.RequestsWaiting(left, announce: false, inGame: false);
    }

    /// <summary>True while a request arrived during a hold and its one cue waits for the release.</summary>
    public bool RequestCuePending => _requestCuePending;

    public IFriendsService Service => _service;
    public int Held => _queue.Count;

    public void OnDelivered(InboxItem item)
    {
        var route = LandingRules.Decide(item, _world(), _now());
        Route(item, route);
    }

    private void Route(InboxItem item, LandingRoute route)
    {
        switch (route)
        {
            case LandingRoute.Drop:
                if (item != null && LandingRules.IsRetired(item))
                    App.Logger?.Information("[Friends] dropped a retired {Dest} invite from {From}", item.Destination, item.FromId);
                return;
            case LandingRoute.Hold: _queue.Add(item); return;
            case LandingRoute.Inbox: _sink.Inbox(item); return;
            case LandingRoute.InGame:
            case LandingRoute.Present:
                var inGame = route == LandingRoute.InGame;
                if (item.Kind == SendKind.Poke) _sink.Poke(item, inGame);
                else _sink.Knock(item, inGame);
                return;
        }
    }

    /// <summary>A new incoming friend request: always a row; a cue and a toast only while
    /// something of ours or a game is on screen; held states save one cue for the release.</summary>
    public void OnRequestArrived(FriendRequest request)
    {
        if (request == null) return;
        _waiting.Forget(request.Id);
        _sink.RequestRow(request);
        var world = _world();
        if (world.Holding) { _requestCuePending = true; return; }
        if (world.GameHostActive) _sink.RequestAnnounce(request, inGame: true);
        else if (world.PanelVisible || world.LauncherVisible) _sink.RequestAnnounce(request, inGame: false);
    }

    public void OnRequestGone(string requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return;
        _waiting.Forget(requestId);
        _sink.RequestGone(requestId);
    }

    /// <summary>Called on a timer: once the hold ends, everything still alive lands in order.
    /// Requests that were waiting at launch are told here too, once, as one row and one notice.</summary>
    public void Release()
    {
        if (_queue.Count == 0 && !_requestCuePending && !_waiting.HasPending) return;
        var world = _world();
        if (world.Holding) return;
        if (_waiting.HasPending)
        {
            var waiting = _waiting.Take();
            bool onScreen = world.GameHostActive || world.PanelVisible || world.LauncherVisible;
            if (waiting.Count > 0) _sink.RequestsWaiting(waiting, announce: onScreen, inGame: world.GameHostActive);
        }
        if (_requestCuePending)
        {
            _requestCuePending = false;
            if (world.GameHostActive || world.PanelVisible || world.LauncherVisible) _sink.RequestCue();
        }
        if (_queue.Count == 0) return;
        var now = _now();
        foreach (var item in _queue.Drain(now))
            Route(item, LandingRules.Decide(item, world, now));
    }

    private void OnSent(SendKind kind, Friend to)
    {
        if (to == null) return;
        if (_world().Holding) return;
        _sink.SentBeat(kind, to);
    }

    public void Dispose()
    {
        _service.Delivered -= OnDelivered;
        _service.Sent -= OnSent;
        _service.RequestArrived -= OnRequestArrived;
        _service.RequestGone -= OnRequestGone;
        _service.SnapshotChanged -= OnSnapshot;
    }
}
