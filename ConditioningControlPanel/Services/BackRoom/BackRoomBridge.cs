using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// THE BACK ROOM PROTOCOL (CONTRACT section 2), with no WPF in it. <see cref="BackRoomHostService"/>
/// owns the window and feeds every page message through <see cref="Handle"/>; everything the host
/// side of protocol 1 promises lives here so the suite can hold it to the letter:
/// <list type="bullet">
/// <item>every <c>station-request</c> gets EXACTLY ONE <c>station-result</c>, refusals and timeouts
/// included, and a repeated reqId never gets a second one;</item>
/// <item>a request after the room started closing is refused <c>closed</c> without touching the network;</item>
/// <item><c>close</c> gives the page <see cref="PageSettleMs"/> to answer <c>exit-done</c> and the
/// window goes at <see cref="ForceCloseMs"/> whatever the page did (Law VI);</item>
/// <item>a balance the page's own result carried is adopted silently; only changes from elsewhere
/// are pushed as <c>balance</c>.</item>
/// </list>
/// </summary>
public sealed class BackRoomBridge
{
    public const int Protocol = 1;
    public const int PageSettleMs = 300;
    public const int ForceCloseMs = 800;
    public const int MinReqIdLength = 16;

    /// <summary>A little past the relay's own 5 s budget and under the page's 6 s one, so the host
    /// always answers before the page gives up, even if a relay implementation hangs.</summary>
    public static readonly TimeSpan ReplyGuard = TimeSpan.FromMilliseconds(5500);

    private static readonly Regex StationId = new("^[a-z0-9_]{1,24}$", RegexOptions.CultureInvariant);

    public sealed class Deps
    {
        public required Action<object> Post { get; init; }
        public required IBackRoomRelay Relay { get; init; }
        public IBackRoomFx Fx { get; init; } = new NullBackRoomFx();
        public IBackRoomMedia Media { get; init; } = new NullBackRoomMedia(null);
        /// <summary>The whole <c>init</c> message (the host's settings projection).</summary>
        public required Func<object> BuildInit { get; init; }
        /// <summary>Tear the window down. Called at most once.</summary>
        public required Action CloseWindow { get; init; }
        /// <summary>Run <c>fn</c> after a delay; the returned action cancels it.</summary>
        public required Func<TimeSpan, Action, Action> Schedule { get; init; }
        /// <summary>Day-log event sink (<c>e_backroom_&lt;station&gt;</c>).</summary>
        public Action<string>? NoteEvent { get; init; }
        /// <summary>Write the adopted SP into the settings.</summary>
        public Action<int>? SetSp { get; init; }
        /// <summary>Marshal onto the thread settings listeners run on (the host's dispatcher).
        /// Null = run inline, which is what the suite wants.</summary>
        public Action<Action>? OnUi { get; init; }
        public Func<int>? NextSeed { get; init; }
        public Action<string>? Log { get; init; }
    }

    private readonly Deps _d;
    private readonly object _gate = new();
    private readonly HashSet<string> _seenReq = new(StringComparer.Ordinal);
    private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackRoomMediaDeal> _deals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string TapeId, int Played)> _cursor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string TapeId, int Played)> _flushed = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _life = new();
    private Action? _cancelForce;
    private bool _initPosted, _closing, _closed;
    private bool _adopting;   // only touched inside OnUi, so on one thread

    public BackRoomBridge(Deps deps) => _d = deps;

    public bool IsClosing { get { lock (_gate) return _closing; } }
    public bool IsClosed { get { lock (_gate) return _closed; } }

    // ============================ host -> page ============================

    /// <summary>The page posted <c>ready</c>: answer <c>init</c>, once per window.</summary>
    public void OnReady()
    {
        lock (_gate) { if (_initPosted || _closed) return; _initPosted = true; }
        _d.Post(_d.BuildInit());
    }

    public void PushSettings(object settings) { if (!IsClosed) _d.Post(settings); }

    /// <summary>Stop audio and fx now (panic, minimise); <c>on:false</c> resumes.</summary>
    public void Suspend(bool on, string reason)
    {
        if (IsClosed) return;
        if (on) CancelFx();
        _d.Post(new { type = "suspend", on, reason });
    }

    /// <summary>Settings.SkillPoints moved. Silent while we are the ones writing it.</summary>
    public void OnSpChanged(int sp, string why)
    {
        if (_adopting || IsClosed) return;
        _d.Post(new { type = "balance", sp, why });
    }

    /// <summary>A server reply carried <c>sp</c>: that is the true balance, adopted now and never
    /// echoed back to the page that asked for it.</summary>
    public void AdoptSp(int sp)
    {
        void Write()
        {
            _adopting = true;
            try { _d.SetSp?.Invoke(sp); }
            finally { _adopting = false; }
        }
        if (_d.OnUi != null) _d.OnUi(Write); else Write();
    }

    /// <summary>The host wants the window gone (<c>app-exit</c> or <c>panic</c>).</summary>
    public void RequestClose(string reason)
    {
        lock (_gate)
        {
            if (_closed || _cancelForce != null) return;
            _closing = true;
        }
        _d.Post(new { type = "close", reason });
        FlushCursors(null);
        ArmForceClose();
    }

    /// <summary>Close now, no round trip: app shutdown and the title-bar X.</summary>
    public void CloseNow()
    {
        FlushCursors(null);
        Finish();
    }

    // ============================ page -> host ============================

    public void Handle(JObject m)
    {
        var type = (string?)m["type"];
        switch (type)
        {
            case "exit":
                _d.Log?.Invoke("page exit (" + ((string?)m["reason"] ?? "?") + ")");
                lock (_gate) _closing = true;
                NoteReportedCursor(m);
                FlushCursors(null);
                ArmForceClose();
                break;
            case "exit-done":
                Finish();
                break;
            case "station-open":
                if (Station(m) is { } open)
                {
                    _d.Log?.Invoke("station open: " + open);
                    try { _d.NoteEvent?.Invoke("e_backroom_" + open); } catch { }
                }
                break;
            case "station-close":
                if (Station(m) is { } shut)
                {
                    _d.Log?.Invoke("station close: " + shut);
                    NoteReportedCursor(m);
                    FlushCursors(shut);
                }
                break;
            case "station-request":
                _ = OnStationRequestAsync(m);
                break;
            case "media-request":
                OnMediaRequest(m);
                break;
            case "fx":
                OnFx(m);
                break;
            case "melt":
                _d.Log?.Invoke("melt " + (string?)m["station"] + " left=" + (string?)m["left"]);
                break;
            default:
                _d.Log?.Invoke("unhandled message '" + type + "'");
                break;
        }
    }

    private static string? Station(JObject m)
    {
        var s = (string?)m["station"];
        return s != null && StationId.IsMatch(s) ? s : null;
    }

    private async Task OnStationRequestAsync(JObject m)
    {
        var reqId = (string?)m["reqId"];
        if (string.IsNullOrEmpty(reqId)) { _d.Log?.Invoke("station-request without reqId dropped"); return; }
        lock (_gate)
        {
            if (!_seenReq.Add(reqId)) { _d.Log?.Invoke("duplicate reqId ignored"); return; }
        }
        if (reqId.Length < MinReqIdLength) { Reply(reqId, BackRoomStationResult.Refuse("bad_op")); return; }
        if (IsClosing) { Reply(reqId, BackRoomStationResult.Refuse("closed")); return; }

        var station = Station(m);
        var op = (string?)m["op"];
        if (station == null || !BackRoomApi.TryResolve(station, op, out _, out _))
        {
            Reply(reqId, BackRoomStationResult.Refuse("bad_op"));
            return;
        }
        var body = m["body"] as JObject;
        NoteCursor(station, op!, body);

        var cancelGuard = _d.Schedule(ReplyGuard, () => Reply(reqId, BackRoomStationResult.Refuse("timeout")));
        BackRoomStationResult result;
        try { result = await _d.Relay.RelayAsync(station, op!, (string?)m["idem"], body, _life.Token); }
        catch (Exception ex)
        {
            _d.Log?.Invoke("relay threw: " + ex.Message);
            result = BackRoomStationResult.Refuse("offline");
        }
        cancelGuard();
        if (result.Body?["tape"] is JObject tape && (string?)tape["id"] is { } tapeId)
        {
            var at = (tape["played"]?.Type == JTokenType.Integer) ? (int)tape["played"]! : 0;
            lock (_gate) { _cursor[station] = (tapeId, at); _flushed[station] = (tapeId, at); }
        }
        Reply(reqId, result);
    }

    private void Reply(string reqId, BackRoomStationResult r)
    {
        lock (_gate) { if (_closed || !_answered.Add(reqId)) return; }
        _d.Post(new { type = "station-result", reqId, ok = r.Ok, status = r.Status, reason = r.Reason, body = r.Body });
    }

    private void OnMediaRequest(JObject m)
    {
        var reqId = (string?)m["reqId"];
        var station = Station(m);
        if (string.IsNullOrEmpty(reqId) || station == null) { _d.Log?.Invoke("bad media-request dropped"); return; }
        lock (_gate) { if (!_answered.Add("media:" + reqId)) return; }
        int seed = _d.NextSeed?.Invoke() ?? Random.Shared.Next();
        BackRoomMediaDeal deal;
        try { deal = _d.Media.Deal(station, seed); }
        catch (Exception ex)
        {
            _d.Log?.Invoke("media deal threw, using fallback: " + ex.Message);
            deal = new NullBackRoomMedia(null).Deal(station, seed);
        }
        lock (_gate) _deals[station] = deal;
        _d.Post(new
        {
            type = "media", reqId, seed = deal.Seed,
            gifs = deal.Gifs.Select(g => new { key = g.Key, url = g.Url, w = g.W, h = g.H, src = g.Src }),
            words = deal.Words.Select(w => new { key = w.Key, text = w.Text, src = w.Src }),
        });
    }

    private void OnFx(JObject m)
    {
        var token = (string?)m["token"];
        var fxId = (string?)m["fxId"] ?? string.Empty;
        var station = Station(m) ?? string.Empty;
        var symbols = (m["symbols"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string)t! : null)
            .Where(s => s != null).Cast<string>().ToList() ?? new List<string>();
        BackRoomFxAck ack;
        if (IsClosing)
        {
            ack = new BackRoomFxAck(Array.Empty<string>(), new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Busy) });
        }
        else
        {
            BackRoomMediaDeal deal;
            lock (_gate) deal = _deals.TryGetValue(station, out var d) ? d : new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), Array.Empty<BackRoomWord>());
            try { ack = _d.Fx.Fire(fxId, station, symbols, deal); }
            catch (Exception ex)
            {
                _d.Log?.Invoke("fx threw: " + ex.Message);
                ack = new BackRoomFxAck(Array.Empty<string>(), new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Unknown) });
            }
        }
        _d.Post(new
        {
            type = "fx-ack", token,
            fired = ack.Fired,
            skipped = ack.Skipped.Select(s => new { prim = s.Prim, why = s.Why.ToString().ToLowerInvariant() }),
        });
    }

    // ============================ cursor + teardown ============================

    private void NoteCursor(string station, string op, JObject? body)
    {
        var c = op == "cursor" ? body : body?["cursor"] as JObject;
        if (c == null || (string?)c["tapeId"] is not { } tapeId || c["played"]?.Type != JTokenType.Integer) return;
        var played = (int)c["played"]!;
        lock (_gate)
        {
            _cursor[station] = (tapeId, played);
            _flushed[station] = (tapeId, played);   // it rides this very request
        }
    }

    /// <summary>Send the last cursor the page reported for any station whose server copy is behind.
    /// Fire and forget: a lost cursor only means re-watching a few settled spins (3.3).</summary>
    public void FlushCursors(string? only)
    {
        List<(string Station, string TapeId, int Played)> due = new();
        lock (_gate)
        {
            foreach (var (station, c) in _cursor)
            {
                if (only != null && station != only) continue;
                if (_flushed.TryGetValue(station, out var f) && f == c) continue;
                _flushed[station] = c;
                due.Add((station, c.TapeId, c.Played));
            }
        }
        foreach (var (station, tapeId, played) in due)
        {
            if (!BackRoomApi.TryResolve(station, "cursor", out _, out _)) continue;
            var body = new JObject { ["tapeId"] = tapeId, ["played"] = played };
            _ = _d.Relay.RelayAsync(station, "cursor", null, body, CancellationToken.None);
        }
    }

    /// <summary>The optional <c>cursor {tapeId, played}</c> a page may put on <c>station-close</c> or
    /// <c>exit</c> (with <c>station</c>): where playback actually stopped, which no request carried.</summary>
    private void NoteReportedCursor(JObject m)
    {
        if (Station(m) is not { } station || m["cursor"] is not JObject c) return;
        if ((string?)c["tapeId"] is not { } tapeId || c["played"]?.Type != JTokenType.Integer) return;
        lock (_gate) _cursor[station] = (tapeId, (int)c["played"]!);
    }

    private void ArmForceClose()
    {
        lock (_gate)
        {
            if (_closed || _cancelForce != null) return;
            _cancelForce = _d.Schedule(TimeSpan.FromMilliseconds(ForceCloseMs), Finish);
        }
    }

    private void Finish()
    {
        Action? cancel;
        lock (_gate)
        {
            if (_closed) return;
            _closed = _closing = true;
            cancel = _cancelForce;
        }
        try { cancel?.Invoke(); } catch { }
        try { _life.Cancel(); } catch { }
        CancelFx();
        _d.CloseWindow();
    }

    private void CancelFx()
    {
        try { _d.Fx.CancelAll(); } catch (Exception ex) { _d.Log?.Invoke("fx cancel threw: " + ex.Message); }
    }
}
