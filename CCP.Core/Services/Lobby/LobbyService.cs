// PORTED from WPF 7.1.5 ConditioningControlPanel/Services/Lobby/LobbyService.cs. Deviations, all
// to stay off the UI toolkit: the poll clock is Core's IUiTimer (the head passes FriendsHead.Timer,
// a DispatcherTimer on the UI thread) instead of a WPF DispatcherTimer; the Remote directory read is
// RemoteDirectoryApi (WPF went through AvailableSubjectsService, which stays head-side for its claim
// flow); Goon goes straight to GoonOpenTablesApi (the WPF GoonOpenTables coalescer is head-side);
// friends and the account door are seams the head seeds. Logging through Serilog's static Log.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Serilog;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>When the next poll runs. Pure: a good answer polls at <see cref="Base"/>, a failed
/// one doubles the wait up to <see cref="Max"/>.</summary>
public static class LobbyPollPlan
{
    public static readonly TimeSpan Base = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    public static TimeSpan Next(TimeSpan current, bool ok)
    {
        if (ok) return Base;
        var doubled = TimeSpan.FromTicks(Math.Max(Base.Ticks, current.Ticks) * 2);
        return doubled > Max ? Max : doubled;
    }
}

/// <summary>
/// THE LOBBY (WPF App.Lobby). Fans out to the three existing open-table wires (chess
/// <c>GET /v2/pbp/lobby</c>, Goon <c>POST /v2/goon/open</c>, Remote <c>GET /v2/directory/list</c>),
/// folds in the friends list, and hands one <see cref="LobbySnapshot"/> to the page and the rail badge.
///
/// Polls ONLY while somebody watches (<see cref="Watch"/>: the Lobby page, the rail badge), every
/// 10 s, doubling to 60 s after a failed round. Signed out it asks nobody.
/// </summary>
public sealed class LobbyService : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IUiTimer?> _makeTimer;
    private int _watchers;
    private IUiTimer? _timer;
    private TimeSpan _interval = LobbyPollPlan.Base;
    private Task? _inflight;
    private bool _disposed;

    /// <param name="makeTimer">The poll clock; null (tests) means no timer, rounds only on demand.</param>
    public LobbyService(Func<IUiTimer?>? makeTimer = null) => _makeTimer = makeTimer ?? (() => null);

    /// <summary>Seams. The real wires by default.</summary>
    internal Func<CancellationToken, Task<PbpLobbyReply>> FetchChess { get; set; } = ct => new PbpLobbyApi().FetchAsync(ct);
    internal Func<Task<OpenTablesReply>> FetchGoon { get; set; } = () => new GoonOpenTablesApi().FetchAsync();
    internal Func<Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> FetchRemote { get; set; } = () => new RemoteDirectoryApi().FetchAsync();
    public Func<IReadOnlyList<Friend>> ReadFriends { get; set; } = () => Array.Empty<Friend>();
    public Func<bool> SignedIn { get; set; } = () => LobbyWire.DefaultIdentity() != null;
    internal Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;

    /// <summary>Raised on whatever thread the round finished; marshal before touching UI.</summary>
    public event Action<LobbySnapshot>? Changed;

    public TimeSpan CurrentInterval => _interval;

    /// <summary>False after a round where neither chess nor the Remote directory answered.</summary>
    public bool LastRoundOk { get; private set; } = true;

    public bool IsWatched { get { lock (_gate) return _watchers > 0; } }

    /// <summary>Start watching. Dispose the lease to stop; the last lease out stops the poll.
    /// Call on the UI thread (the timer is the head's UI-thread timer).</summary>
    public IDisposable Watch()
    {
        bool first;
        lock (_gate) { first = ++_watchers == 1; }
        if (first)
        {
            _interval = LobbyPollPlan.Base;
            _timer ??= _makeTimer();
            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Tick += OnTick;
                _timer.Interval = _interval;
                _timer.Start();
            }
        }
        _ = RefreshAsync();
        return new Lease(this);
    }

    private void Unwatch()
    {
        bool last;
        lock (_gate) { last = _watchers > 0 && --_watchers == 0; }
        if (last) _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsWatched) { _timer?.Stop(); return; }
        _ = RefreshAsync();
    }

    /// <summary>One round. Joins a round already in flight.</summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_inflight != null) return _inflight;
            var run = RunAsync();
            _inflight = run.IsCompleted ? null : run;
            return run;
        }
    }

    private async Task RunAsync()
    {
        bool ok = true;
        LobbySnapshot snap;
        try
        {
            if (!SignedIn())
            {
                snap = LobbySnapshot.Empty;
            }
            else
            {
                var chessTask = SafeChess();
                var goonTask = SafeGoon();
                var remoteTask = SafeRemote();
                await Task.WhenAll(chessTask, goonTask, remoteTask).ConfigureAwait(false);
                var chess = chessTask.Result;
                var remote = remoteTask.Result;
                // Goon's wire cannot tell "no tables" from "no answer", so it never counts as a fault.
                ok = chess.Ok || remote.Ok;
                IReadOnlyList<Friend> friends;
                try { friends = ReadFriends(); } catch { friends = Array.Empty<Friend>(); }
                snap = LobbyMerge.Build(chess, goonTask.Result, remote.Seats, friends, true, NowMs());
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Lobby: round failed: {E}", ex.Message);
            snap = Snapshot;
            ok = false;
        }
        finally
        {
            lock (_gate) { _inflight = null; }
        }

        LastRoundOk = ok;
        _interval = LobbyPollPlan.Next(_interval, ok);
        try
        {
            var t = _timer;
            if (t != null) t.OnUiThread(() => { if (IsWatched) t.Interval = _interval; });
        }
        catch { }
        Snapshot = snap;
        try { Changed?.Invoke(snap); } catch (Exception ex) { Log.Debug("Lobby.Changed: {E}", ex.Message); }
    }

    private async Task<PbpLobbyReply> SafeChess()
    {
        try { return await FetchChess(CancellationToken.None).ConfigureAwait(false); }
        catch { return PbpLobbyReply.Empty; }
    }

    private async Task<OpenTablesReply> SafeGoon()
    {
        try { return await FetchGoon().ConfigureAwait(false); }
        catch { return OpenTablesReply.Empty; }
    }

    private async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> SafeRemote()
    {
        try { return await FetchRemote().ConfigureAwait(false); }
        catch { return (Array.Empty<RemoteSeat>(), false); }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watchers = 0; }
        try { _timer?.Stop(); } catch { }
    }

    private sealed class Lease : IDisposable
    {
        private LobbyService? _owner;
        public Lease(LobbyService owner) => _owner = owner;
        public void Dispose() { Interlocked.Exchange(ref _owner, null)?.Unwatch(); }
    }
}
