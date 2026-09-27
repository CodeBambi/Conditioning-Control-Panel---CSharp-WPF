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
        /// <summary>The spoken subliminal word (10.21). The default speaks nothing and says so, which
        /// leaves the page's own speechSynthesis in charge exactly as before.</summary>
        public IBackRoomVoice Voice { get; init; } = NullBackRoomVoice.Instance;
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
        /// <summary>Run blocking work off the UI thread (the media deal reads file headers). Null =
        /// run inline, which is what the suite wants; the host passes <c>Task.Run</c>.</summary>
        public Action<Action>? OffUi { get; init; }
        /// <summary>Write a validated <c>room-option</c> into the settings and save (10.14). Null = ignored.</summary>
        public Action<RoomOption>? SetOption { get; init; }
        /// <summary>One validated <c>haptic</c> pulse, or a stop (10.23). Null = no toy path, the frame is dropped.
        /// The bridge also sends a stop of its own wherever it cancels fx: suspend, close, exit, station-close.</summary>
        public Action<BackRoomHaptic>? Haptic { get; init; }
        public Func<int>? NextSeed { get; init; }
        public Action<string>? Log { get; init; }
        /// <summary>A slot outcome the server really dealt just landed on the page (10.24): its line,
        /// read from the host's own copy of the tape, never from the page. Null = nothing listens.</summary>
        public Action<string>? SlotLanded { get; init; }
        /// <summary>The clock the landing rate limit reads. Null = <see cref="DateTime.UtcNow"/>.</summary>
        public Func<DateTime>? UtcNow { get; init; }
    }

    private readonly Deps _d;
    private readonly object _gate = new();
    private readonly HashSet<string> _seenReq = new(StringComparer.Ordinal);
    private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackRoomMediaDeal> _deals = new(StringComparer.Ordinal);
    /// <summary>Stations with a <c>media-warm</c> waiter in flight: one per station, so a wall that
    /// re-deals while the batch is still landing does not stack a second.</summary>
    private readonly HashSet<string> _warmWaits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string TapeId, int Played)> _cursor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string TapeId, int Played)> _flushed = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _life = new();
    private readonly BackRoomTabLedger _ledger = new();
    private Action? _cancelForce;
    private bool _initPosted, _closing, _closed, _suspended;
    private bool _adopting;   // only touched inside OnUi, so on one thread

    public BackRoomBridge(Deps deps) => _d = deps;

    public bool IsClosing { get { lock (_gate) return _closing; } }
    public bool IsClosed { get { lock (_gate) return _closed; } }
    /// <summary>Suspended (panic, minimise): <c>fx</c> is acked busy and <c>fx-tunnel</c> dropped, so an update
    /// already in flight cannot reopen what the suspend just stopped.</summary>
    private bool Quiet { get { lock (_gate) return _closing || _suspended; } }

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
        lock (_gate) _suspended = on;
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
    public void AdoptSp(int sp, Func<bool>? canAdopt = null)
    {
        void Write()
        {
            if (IsClosed || canAdopt?.Invoke() == false) return;
            _adopting = true;
            try { _d.SetSp?.Invoke(sp); }
            finally { _adopting = false; }
        }
        if (_d.OnUi != null) _d.OnUi(Write); else Write();
    }

    /// <summary>The host wants the window gone (<c>app-exit</c> or <c>panic</c>). Every running fx
    /// primitive stops now, not when the page settles or the watchdog fires (CONTRACT section 4).</summary>
    public void RequestClose(string reason)
    {
        lock (_gate)
        {
            if (_closed || _cancelForce != null) return;
            _closing = true;
        }
        CancelFx();
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
                CancelFx();   // leaving: overlays stop now, not at exit-done or 800 ms (section 4)
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
                    // 10.13.B: that station's holds and tunnel go with it.
                    try { _d.Fx.ReleaseStation(shut); } catch (Exception ex) { _d.Log?.Invoke("fx release threw: " + ex.Message); }
                    StopHaptic(shut);
                }
                break;
            case "haptic":
                // No reply (10.23). A stop always lands; a pulse is dropped while closing or suspended,
                // so a frame already in flight cannot restart what the suspend just stopped.
                if (ReadHaptic(m) is { } pulse && (pulse.IsStop || !Quiet)) Guard(() => _d.Haptic?.Invoke(pulse));
                break;
            case "station-request":
                _ = OnStationRequestAsync(m);
                break;
            case "media-request":
                _ = OnMediaRequestAsync(m);
                break;
            case "fx":
                OnFx(m);
                break;
            case "word.speak":
                OnWordSpeak(m);
                break;
            case "word.stop":
                // No reply (10.21): Law VI on the page's side, the same call cancel() and suspend make.
                StopVoice();
                break;
            case "fx-tunnel":
                // No reply (10.13.B). A NaN or a string level is dropped by the dispatcher's own checks.
                if (!Quiet && Station(m) is { } tunnelAt && m["level"] is JValue { Type: JTokenType.Integer or JTokenType.Float } lv)
                    Guard(() => _d.Fx.Tunnel(tunnelAt, lv.Value<double>()));
                break;
            case "fx-release":
                if ((string?)m["token"] is { Length: > 0 and <= 64 } releaseToken)
                    Guard(() => _d.Fx.Release(releaseToken, Station(m) ?? string.Empty));
                break;
            case "landed":
                OnLanded(m);
                break;
            case "melt":
                _d.Log?.Invoke("melt " + (string?)m["station"] + " left=" + (string?)m["left"]);
                break;
            case "room-option":
                // 10.14: the room's Options. Anything but the three known shapes is dropped.
                if (!IsClosing && ReadRoomOption(m) is { } option) Guard(() => _d.SetOption?.Invoke(option));
                else _d.Log?.Invoke("room-option dropped");
                break;
            default:
                _d.Log?.Invoke("unhandled message '" + type + "'");
                break;
        }
    }

    private void Guard(Action fx)
    {
        try { fx(); } catch (Exception ex) { _d.Log?.Invoke("fx threw: " + ex.Message); }
    }

    public const string OptionTunnel = "tunnel", OptionMelt = "melt", OptionIntensity = "intensity";
    /// <summary>Invert camera (10.14): a drag moves the world instead of the camera. A switch like tunnel and melt.</summary>
    public const string OptionInvertLook = "invertLook";
    /// <summary>The first-visit card was dismissed (CONTRACT section 13). A switch the page only ever sets true;
    /// <c>AppSettings.BackRoomWelcomeSeen</c>, echoed as <c>welcomeSeen</c> on <c>init</c>.</summary>
    public const string OptionWelcomeSeen = "welcomeSeen";
    /// <summary>Where the room's pictures come from (10.13.C). Values as <c>AppSettings.BackRoomMediaSource</c>.</summary>
    public const string OptionMediaSource = "mediaSource";
    /// <summary>The room's own three audio levels, 0-100 (10.14). Not the app's volumes.</summary>
    public const string OptionSubVolume = "subVolume", OptionSfxVolume = "sfxVolume", OptionMusicVolume = "musicVolume";
    /// <summary>One niche at a time (10.13.C). A list on this wire would mean the page owning the
    /// selection and the host taking dictation; one name per press keeps the host the writer.</summary>
    public const string OptionSubAdd = "mediaSubAdd", OptionSubRemove = "mediaSubRemove", OptionSubToggle = "mediaSubToggle";

    /// <summary>A niche name the host will accept: what Reddit and Scrolller allow, and nothing that
    /// could be read as a path. The room validates too, but only so a typo is answered in the room.</summary>
    private static readonly System.Text.RegularExpressions.Regex NicheName =
        new("^[A-Za-z0-9_]{2,40}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly string[] MediaSourceValues = { "auto", "local", "online", "mixed", "bundled" };

    /// <summary>One validated <c>room-option</c>: a switch (<see cref="On"/>), the intensity, a whitelisted
    /// string (<see cref="Text"/>) or a 0-100 level (<see cref="Level"/>). Exactly one is ever set.</summary>
    public sealed record RoomOption(string Key, bool On, BackRoomFxIntensity? Intensity, string? Text = null, int? Level = null);

    /// <summary><c>{type:'room-option', key:'tunnel'|'melt'|'invertLook'|'welcomeSeen', value: bool}</c>, <c>{key:'intensity', value:
    /// 'calm'|'normal'|'full'}</c>, <c>{key:'mediaSource', value:'auto'|'local'|'online'|'mixed'|'bundled'}</c>
    /// or <c>{key:'subVolume'|'sfxVolume'|'musicVolume', value: 0..100}</c>. A string "true", a level outside
    /// the range, a non-integer level and an unknown key are all null: the page does not get to widen this
    /// wire by sending something new.</summary>
    internal static RoomOption? ReadRoomOption(JObject m)
    {
        var key = (string?)m["key"];
        var v = m["value"];
        if ((key == OptionTunnel || key == OptionMelt || key == OptionInvertLook || key == OptionWelcomeSeen) && v is JValue { Type: JTokenType.Boolean } b)
            return new RoomOption(key, b.Value<bool>(), null);
        if (key == OptionIntensity && v is JValue { Type: JTokenType.String } t)
            return (string?)t switch
            {
                "calm" => new RoomOption(key, false, BackRoomFxIntensity.Calm),
                "normal" => new RoomOption(key, false, BackRoomFxIntensity.Normal),
                "full" => new RoomOption(key, false, BackRoomFxIntensity.Full),
                _ => null,
            };
        if (key == OptionMediaSource && v is JValue { Type: JTokenType.String } src
            && Array.IndexOf(MediaSourceValues, (string?)src) >= 0)
            return new RoomOption(key, false, null, (string?)src);
        if ((key == OptionSubVolume || key == OptionSfxVolume || key == OptionMusicVolume)
            && v is JValue { Type: JTokenType.Integer } lv)
        {
            var level = lv.Value<long>();
            if (level is < 0 or > 100) return null;
            return new RoomOption(key, false, null, null, (int)level);
        }
        if ((key == OptionSubAdd || key == OptionSubRemove || key == OptionSubToggle)
            && v is JValue { Type: JTokenType.String } niche
            && (string?)niche is { } name && NicheName.IsMatch(name))
            return new RoomOption(key, false, null, name);
        return null;
    }

    public const int HapticMinMs = 20, HapticMaxMs = 1500;
    private static readonly Regex HapticTag = new("^[A-Za-z0-9_.-]{1,24}$", RegexOptions.CultureInvariant);

    /// <summary><c>{type:'haptic', station, level: 0..1, ms, tag}</c> (10.23). The page is untrusted: a level or
    /// an ms that is not a finite number drops the frame, the level is clamped to 0..1 and the ms to
    /// <see cref="HapticMinMs"/>..<see cref="HapticMaxMs"/>, and a tag that is not a short plain token reads as
    /// empty (it is only ever logged). Level 0 is the stop, whatever its ms says.</summary>
    internal static BackRoomHaptic? ReadHaptic(JObject m)
    {
        if (Station(m) is not { } station) return null;
        if (m["level"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } lv) return null;
        var level = lv.Value<double>();
        if (double.IsNaN(level) || double.IsInfinity(level)) return null;
        level = Math.Clamp(level, 0, 1);
        var tag = m["tag"] is JValue { Type: JTokenType.String } t && (string?)t is { } raw && HapticTag.IsMatch(raw) ? raw : string.Empty;
        if (level <= 0) return new BackRoomHaptic(station, 0, 0, tag);
        if (m["ms"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } dv) return null;
        var ms = dv.Value<double>();
        if (double.IsNaN(ms) || double.IsInfinity(ms)) return null;
        return new BackRoomHaptic(station, level, (int)Math.Clamp(ms, HapticMinMs, HapticMaxMs), tag);
    }

    /// <summary><c>media-request.count</c>: an integer 1..13, anything else reads as 4 (10.13.C).</summary>
    internal static int MediaCount(JToken? t)
        => t is JValue { Type: JTokenType.Integer } v && v.Value<long>() is >= 1 and <= 13 ? (int)v.Value<long>() : 4;

    /// <summary><c>media-request.source</c> (10.13.C): an optional per-request override, one of the same
    /// values <c>room-option: mediaSource</c> takes. Absent, a non-string, or anything off the list reads
    /// as null and the room's own setting decides - shape-strict like the rest of this file, because the
    /// page does not get to invent a source. The feed still applies consent to whatever it is handed:
    /// narrowing is the page's to ask for, widening is not.</summary>
    internal static string? MediaSourceRequest(JToken? t)
        => t is JValue { Type: JTokenType.String } v && Array.IndexOf(MediaSourceValues, (string?)v) >= 0
            ? (string?)v
            : null;

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
        try { _ledger.Relayed(station, result.Body); } catch (Exception ex) { _d.Log?.Invoke("ledger threw: " + ex.Message); }
        if (result.Body?["tape"] is JObject tape && (string?)tape["id"] is { } tapeId)
        {
            var at = (tape["played"]?.Type == JTokenType.Integer) ? (int)tape["played"]! : 0;
            lock (_gate) { _cursor[station] = (tapeId, at); _flushed[station] = (tapeId, at); }
        }
        Reply(reqId, result);
    }

    /// <summary>10.24 <c>landed {station, tapeId?, side?, i}</c>, no reply: a slot outcome the server dealt
    /// has just played. Only the index is the page's; the line is looked up in what the host relayed.</summary>
    private void OnLanded(JObject m)
    {
        if (IsClosed || _d.SlotLanded == null || Station(m) is not { } station) return;
        if (m["i"] is not JValue { Type: JTokenType.Integer } iv || iv.Value<long>() is < 0 or > BackRoomTabLedger.MaxOutcomes) return;
        bool side = m["side"] is JValue { Type: JTokenType.Boolean } sv && sv.Value<bool>();
        var tapeId = m["tapeId"] is JValue { Type: JTokenType.String } tv ? (string?)tv : null;
        var line = _ledger.Land(station, tapeId, side, (int)iv.Value<long>(), _d.UtcNow?.Invoke() ?? DateTime.UtcNow);
        if (line != null) Guard(() => _d.SlotLanded(line));
    }

    private void Reply(string reqId, BackRoomStationResult r)
    {
        lock (_gate) { if (_closed || !_answered.Add(reqId)) return; }
        _d.Post(new { type = "station-result", reqId, ok = r.Ok, status = r.Status, reason = r.Reason, body = r.Body });
    }

    private async Task OnMediaRequestAsync(JObject m)
    {
        var reqId = (string?)m["reqId"];
        var station = Station(m);
        if (string.IsNullOrEmpty(reqId) || station == null) { _d.Log?.Invoke("bad media-request dropped"); return; }
        lock (_gate) { if (!_answered.Add("media:" + reqId)) return; }
        int seed = _d.NextSeed?.Invoke() ?? Random.Shared.Next();
        int count = MediaCount(m["count"]);
        var wanted = MediaSourceRequest(m["source"]);

        // The deal reads file headers, so it still starts off the UI thread (OffUi); DealAsync's own
        // await - a bounded top-up of the warm remote pool - then continues on the pool. Post marshals
        // the reply back. The reply shape is additive: the same gifs and words, plus the source the
        // deal actually resolved to, so the page can show what it GOT.
        async Task DealAndPostAsync()
        {
            BackRoomMediaDeal deal;
            try { deal = await _d.Media.DealAsync(station, seed, count, wanted, _life.Token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _d.Log?.Invoke("media deal threw, using fallback: " + ex.Message);
                deal = new NullBackRoomMedia(null).Deal(station, seed);
            }
            lock (_gate) { if (_closed) return; _deals[station] = deal; }
            _d.Post(new
            {
                type = "media", reqId, seed = deal.Seed, source = deal.Source,
                gifs = deal.Gifs.Select(g => new { key = g.Key, url = g.Url, w = g.W, h = g.H, src = g.Src }),
                words = deal.Words.Select(w => new { key = w.Key, text = w.Text, src = w.Src }),
            });
            await PushWarmWhenLandedAsync(station, deal, count).ConfigureAwait(false);
        }

        // Guarded here rather than inside: this runs detached (no reply guard on media-request), so
        // nothing it throws may reach the task scheduler.
        async Task RunAsync()
        {
            try { await DealAndPostAsync().ConfigureAwait(false); }
            catch (Exception ex) { _d.Log?.Invoke("media post threw: " + ex.Message); }
        }

        if (_d.OffUi != null) _d.OffUi(() => _ = RunAsync());
        else await RunAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// THE COLD-OPEN SWAP (2026-09-18). A deal that wanted remote pictures and got fewer than it asked
    /// for went out on the player's folders or the bundled loops, and nothing told the page when the
    /// batch behind it landed: <c>room\screens.js</c> re-deals on its own only every 72 s, which is the
    /// "preset gifs for a good while" the owner saw. The web shim answers this with a
    /// <c>br-media-changed</c> event once its warm ends; this is that event on the wire. Waits on the
    /// batch (cancelled with the bridge), then posts one <c>media-warm</c> for the station.
    /// </summary>
    private async Task PushWarmWhenLandedAsync(string station, BackRoomMediaDeal deal, int count)
    {
        if (deal.Source is not ("online" or "mixed")) return;
        if (deal.Gifs.Count(g => g.Src == "online") >= count) return;
        lock (_gate) { if (_closed || !_warmWaits.Add(station)) return; }
        bool warmed = false;
        try { warmed = await _d.Media.WaitForWarmAsync(_life.Token).ConfigureAwait(false); }
        catch (Exception ex) { _d.Log?.Invoke("media warm wait threw: " + ex.Message); }
        finally { lock (_gate) _warmWaits.Remove(station); }
        if (!warmed) return;
        lock (_gate) { if (_closed) return; }
        _d.Post(new { type = "media-warm", station });
    }

    private void OnFx(JObject m)
    {
        var token = (string?)m["token"];
        var fxId = (string?)m["fxId"] ?? string.Empty;
        var station = Station(m) ?? string.Empty;
        var symbols = (m["symbols"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string)t! : null)
            .Where(s => s != null).Cast<string>().ToList() ?? new List<string>();
        BackRoomFxAck ack;
        if (Quiet)
        {
            ack = new BackRoomFxAck(Array.Empty<string>(), new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Busy) });
        }
        else
        {
            BackRoomMediaDeal deal;
            lock (_gate) deal = _deals.TryGetValue(station, out var d) ? d : new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), Array.Empty<BackRoomWord>());
            try { ack = _d.Fx.Fire(fxId, station, symbols, deal, BackRoomFxArgs.Parse(m["args"]),
                token is { Length: <= 64 } ? token : null); }
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

    /// <summary>The longest phrase the host will speak. A dealt word can be a whole trigger phrase;
    /// anything past this is not a word, so it is acked <c>none</c> and the page says it itself.</summary>
    public const int MaxWordLength = 200;

    /// <summary>10.21 <c>word.speak</c>: the host says the subliminal word, and the ack tells the page
    /// whether it still has to. Runs off the UI thread - it opens clips and may synthesise speech - and
    /// answers exactly once, <c>none</c> included, so the page never waits on a missing reply.</summary>
    private void OnWordSpeak(JObject m)
    {
        var token = (string?)m["token"];
        var text = (string?)m["text"];
        bool reversed = m["reversed"] is JValue { Type: JTokenType.Boolean } r && r.Value<bool>();
        int seed = m["seed"] is JValue { Type: JTokenType.Integer } sv ? unchecked((int)sv.Value<long>()) : 0;

        void Silent() => _d.Post(new { type = "word-ack", token, source = "none", durationMs = 0 });
        if (Quiet || string.IsNullOrWhiteSpace(text) || text!.Length > MaxWordLength) { Silent(); return; }

        void SpeakAndPost()
        {
            BackRoomVoiceAck ack;
            try { ack = _d.Voice.Speak(text!, reversed, seed); }
            catch (Exception ex) { _d.Log?.Invoke("voice threw: " + ex.Message); ack = new BackRoomVoiceAck("none", 0); }
            lock (_gate) { if (_closed) return; }
            _d.Post(new { type = "word-ack", token, source = ack.Source, durationMs = ack.DurationMs });
        }
        if (_d.OffUi != null) _d.OffUi(SpeakAndPost); else SpeakAndPost();
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
        StopVoice();
        StopHaptic(string.Empty);
        try { _d.Fx.CancelAll(); } catch (Exception ex) { _d.Log?.Invoke("fx cancel threw: " + ex.Message); }
    }

    private void StopHaptic(string station)
    {
        try { _d.Haptic?.Invoke(new BackRoomHaptic(station, 0, 0, "host")); } catch (Exception ex) { _d.Log?.Invoke("haptic stop threw: " + ex.Message); }
    }

    private void StopVoice()
    {
        try { _d.Voice.Stop(); } catch (Exception ex) { _d.Log?.Invoke("voice stop threw: " + ex.Message); }
    }
}
