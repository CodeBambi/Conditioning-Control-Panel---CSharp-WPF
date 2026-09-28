using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// THE FRIENDS SERVICE, hung off <c>App.Friends</c>. Owns the poll (cadence from
/// <see cref="FriendsPollRule"/>), the last snapshot, the inbox de-dup and the presence gate.
/// Every event is raised on the thread that ran the poll or the call, which in the app is the
/// dispatcher: the timer is a DispatcherTimer and nothing here uses ConfigureAwait(false).
///
/// <para>Sign in and sign out: there is no sign-out event to hang off, so the service reads the
/// account on every tick. Signed out, the rule says 0 and the timer only re-reads the account
/// every <see cref="IdleCheckSeconds"/> without touching the network. A different account (or
/// none) clears the snapshot and the de-dup memory. <see cref="Kick"/> polls now; App calls it
/// when a profile loads.</para>
/// </summary>
public sealed partial class FriendsService : IFriendsService, IDisposable
{
    /// <summary>How often a signed-out service looks for an account. No request is made.</summary>
    public const int IdleCheckSeconds = 15;

    /// <summary>How many delivered ids are remembered for the de-dup. The inbox holds 50.</summary>
    internal const int SeenCap = 500;

    private readonly IFriendsApi _api;
    private readonly Func<string?> _account;
    private readonly Func<bool> _foreground;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<bool> _readShared;
    private readonly Action<bool> _writeShared;
    private readonly Func<int?> _lockDay;

    private DispatcherTimer? _timer;
    private string? _lastAccount;
    private int _pollIndex;
    private bool _drawerOpen;
    private bool _drawerJustOpened;
    private bool _busy;
    private bool _kickPending;
    private bool _disposed;
    private PresenceActivity _activity = PresenceActivity.Panel;
    private HashSet<string> _online = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _lastPoke = new(StringComparer.Ordinal);

    public FriendsService(
        IFriendsApi api,
        Func<string?> account,
        Func<bool>? foreground = null,
        Func<DateTimeOffset>? now = null,
        Func<bool>? readShared = null,
        Action<bool>? writeShared = null,
        Func<int?>? lockDay = null)
    {
        _api = api;
        _account = account;
        _foreground = foreground ?? AnyAppWindowActive;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _readShared = readShared ?? (() => App.Settings?.Current?.FriendsPresenceShared == true);
        _writeShared = writeShared ?? (v =>
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FriendsPresenceShared = v;
            try { App.Settings?.Save(); } catch { }
        });
        _lockDay = lockDay ?? (() => null);
    }

    /// <summary>The app's own wiring: the real wire, the account off AppSettings.</summary>
    public static FriendsService CreateForApp() => new(
        new FriendsApi(),
        () => Services.BackRoom.BackRoomApi.AppIdentity()?.UnifiedId);

    // ---- IFriendsService ----

    public bool Available => _account() != null;

    public FriendsSnapshot Snapshot { get; private set; } = FriendsSnapshot.Empty;

    public bool PresenceShared
    {
        get => _readShared();
        set
        {
            if (_readShared() == value) return;
            _writeShared(value);
            // The server learns at once: on writes the presence key, off deletes it.
            Kick();
        }
    }

    public event Action<FriendsSnapshot>? SnapshotChanged;
    public event Action<InboxItem>? Delivered;
    public event Action<SendKind, Friend>? Sent;
    public event Action<FriendRequest>? RequestArrived;
    public event Action<string>? RequestGone;

    private readonly FriendRequestWatch _requests = new();

    // ---- the leash piggyback (Leash CONTRACT "The poll piggyback") ----

    /// <summary>R for the next poll, or null. Set by the app to the leash service.</summary>
    public Func<JObject?>? LeashReportProvider { get; set; }

    /// <summary>After every answered poll: its <c>leash</c> block, null when the key was absent.</summary>
    public event Action<JObject?>? LeashBlockArrived;

    /// <summary>True while the leash wants the 20 s cadence (leashed or holding anyone).</summary>
    public Func<bool>? LeashActive { get; set; }

    /// <summary>The activity this app would publish. Read by tests and the drawer's own row.</summary>
    public PresenceActivity Activity => _activity;

    public void SetActivity(PresenceActivity activity)
    {
        if (_activity == activity) return;
        _activity = activity;
        // Presence is only worth a request when someone can see it.
        if (_readShared() && Available) Kick();
    }

    public void SetDrawerOpen(bool open)
    {
        if (_drawerOpen == open) return;
        _drawerOpen = open;
        if (open)
        {
            _drawerJustOpened = true;
            Kick();
        }
        else Reschedule();
    }

    public async Task RefreshAsync()
    {
        if (!CheckAccount()) return;
        var sentFor = _lastAccount;
        var state = await _api.StateAsync();
        if (state == null || _account() != sentFor) return;
        _online = new HashSet<string>(state.Friends.Where(f => f.Online).Select(f => f.Id), StringComparer.Ordinal);
        Publish(state);
        NoteRequests(state);
    }

    // ---- the poll ----

    /// <summary>Start the timer. The app calls this once; tests drive <see cref="TickAsync"/> by hand.</summary>
    public void Start()
    {
        if (_disposed || _timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) =>
        {
            _timer?.Stop();
            try { await TickAsync(); }
            catch (Exception ex) { App.Logger?.Debug("Friends poll failed: {E}", ex.Message); }
            finally { Reschedule(); }
        };
        _timer.Start();
    }

    /// <summary>Poll now (sign-in, the drawer opening, presence switched). Safe from any thread.</summary>
    public void Kick()
    {
        var t = _timer;
        if (t == null || _disposed) return;
        void Go()
        {
            if (_disposed || _timer == null) return;
            if (_busy) { _kickPending = true; return; }
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Start();
        }
        if (t.Dispatcher.CheckAccess()) Go();
        else t.Dispatcher.BeginInvoke((Action)Go);
    }

    private void Reschedule()
    {
        var t = _timer;
        if (t == null || _disposed) return;
        void Go()
        {
            if (_disposed || _timer == null || _busy) return;
            var s = NextIntervalSeconds();
            _timer.Stop();
            _timer.Interval = _kickPending
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromSeconds(s == 0 ? IdleCheckSeconds : s);
            _kickPending = false;
            _timer.Start();
        }
        if (t.Dispatcher.CheckAccess()) Go();
        else t.Dispatcher.BeginInvoke((Action)Go);
    }

    internal int NextIntervalSeconds()
    {
        bool fg;
        try { fg = _foreground(); } catch { fg = false; }
        var friends = FriendsPollRule.NextIntervalSeconds(Available, _drawerOpen, _online.Count > 0, fg);
        bool leash;
        try { leash = LeashActive?.Invoke() == true; } catch { leash = false; }
        return Leash.LeashPollRule.NextIntervalSeconds(friends, leash);
    }

    /// <summary>One poll cycle: presence out, inbox and online in, the full list every fifth time.</summary>
    public async Task TickAsync()
    {
        if (_busy || _disposed) return;
        if (!CheckAccount()) return;
        _busy = true;
        try
        {
            var sentFor = _lastAccount;
            bool wantState = FriendsPollRule.FetchState(_pollIndex, _drawerJustOpened);
            _drawerJustOpened = false;
            _pollIndex++;

            bool shared = _readShared();
            JObject? report = null;
            try { report = LeashReportProvider?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("Leash report failed: {E}", ex.Message); }
            var reply = await _api.PollAsync(shared ? _activity : null, shared ? _lockDay() : null, shared, report);
            if (_account() != sentFor) return;
            if (reply != null)
            {
                ApplyOnline(reply.Online);
                Deliver(reply.Inbox);
                try { LeashBlockArrived?.Invoke(reply.Leash); }
                catch (Exception ex) { App.Logger?.Debug("Leash block handler failed: {E}", ex.Message); }
            }

            if (wantState)
            {
                var state = await _api.StateAsync();
                if (state != null && _account() == sentFor)
                {
                    // The poll's online list is newer than nothing but older than this; keep the state's.
                    _online = new HashSet<string>(state.Friends.Where(f => f.Online).Select(f => f.Id), StringComparer.Ordinal);
                    Publish(state);
                    NoteRequests(state);
                }
            }
        }
        finally { _busy = false; }
    }

    /// <summary>Returns true when an account is signed in. A change of account (or none) wipes
    /// everything the last account knew.</summary>
    private bool CheckAccount()
    {
        var now = _account();
        if (now != _lastAccount)
        {
            _lastAccount = now;
            _pollIndex = 0;
            _online = new HashSet<string>(StringComparer.Ordinal);
            _seen.Clear();
            _seenOrder.Clear();
            _lastPoke.Clear();
            _requests.Reset();
            Publish(FriendsSnapshot.Empty);
        }
        return now != null;
    }

    private void ApplyOnline(IReadOnlyList<string> online)
    {
        var set = new HashSet<string>(online, StringComparer.Ordinal);
        _online = set;
        var snap = Snapshot;
        if (snap.Friends.Count == 0) return;
        var friends = snap.Friends.Select(f =>
        {
            bool on = set.Contains(f.Id);
            if (on == f.Online) return f;
            // Gone offline: last seen is now. Come online: the activity waits for the next state.
            var p = on
                ? f.Presence with { Activity = f.Presence.Activity == PresenceActivity.Offline ? PresenceActivity.Panel : f.Presence.Activity }
                : f.Presence with { Activity = PresenceActivity.Offline, LastSeen = _now() };
            return f with { Online = on, Presence = p };
        }).ToList();
        Publish(snap with { Friends = friends });
    }

    /// <summary>Oldest first, never twice, never expired.</summary>
    internal void Deliver(IReadOnlyList<InboxItem> inbox)
    {
        if (inbox.Count == 0) return;
        var now = _now();
        foreach (var item in inbox.OrderBy(i => i.At))
        {
            if (item.IsExpired(now)) continue;
            if (!_seen.Add(item.Id)) continue;
            _seenOrder.Enqueue(item.Id);
            while (_seenOrder.Count > SeenCap) _seen.Remove(_seenOrder.Dequeue());
            try { Delivered?.Invoke(item); }
            catch (Exception ex) { App.Logger?.Debug("Friends delivery handler failed: {E}", ex.Message); }
        }
    }

    /// <summary>Diffs a fresh server list against the last one: a new incoming request is raised
    /// once, a vanished one once. The first list after a start or an account change is the baseline.</summary>
    internal void NoteRequests(FriendsSnapshot state)
    {
        var (arrived, gone) = _requests.Update(state.Incoming);
        foreach (var r in arrived)
        {
            try { RequestArrived?.Invoke(r); }
            catch (Exception ex) { App.Logger?.Debug("Friends request handler failed: {E}", ex.Message); }
        }
        foreach (var id in gone)
        {
            try { RequestGone?.Invoke(id); }
            catch (Exception ex) { App.Logger?.Debug("Friends request-gone handler failed: {E}", ex.Message); }
        }
    }

    private void Publish(FriendsSnapshot next)
    {
        if (FriendsSnapshotEquality.Same(Snapshot, next)) return;
        Snapshot = next;
        try { SnapshotChanged?.Invoke(next); }
        catch (Exception ex) { App.Logger?.Debug("Friends snapshot handler failed: {E}", ex.Message); }
    }

    private static bool AnyAppWindowActive()
    {
        try
        {
            var app = Application.Current;
            if (app == null) return false;
            if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(AnyAppWindowActive);
            foreach (Window w in app.Windows) if (w.IsActive) return true;
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _disposed = true;
        var t = _timer;
        _timer = null;
        if (t == null) return;
        try
        {
            if (t.Dispatcher.CheckAccess()) t.Stop();
            else t.Dispatcher.BeginInvoke((Action)t.Stop);
        }
        catch { }
    }
}

/// <summary>Snapshot records hold lists, and list equality is by reference; this compares contents.</summary>
public static class FriendsSnapshotEquality
{
    public static bool Same(FriendsSnapshot a, FriendsSnapshot b)
    {
        if (ReferenceEquals(a, b)) return true;
        return a.MyCode == b.MyCode
            && Equals(a.Me, b.Me)
            && a.Friends.SequenceEqual(b.Friends)
            && a.Incoming.SequenceEqual(b.Incoming)
            && a.Outgoing.SequenceEqual(b.Outgoing);
    }
}
