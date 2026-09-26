using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>Remembers, across restarts, that this account cut its leash and the server has not
/// heard yet. Holds the account id, or null.</summary>
public interface ILeashCutStore
{
    string? Read();
    void Write(string? accountId);
}

/// <summary>
/// THE LEASH SERVICE, hung off <c>App.Leash</c>. It owns no timer: it rides the friends poll
/// (CONTRACT "The poll piggyback"). The friends service asks <see cref="BuildReportJson"/> for R
/// before each poll and hands every reply's <c>leash</c> block to <see cref="ApplyBlock"/>;
/// <see cref="Active"/> tells it to poll on the 20 s cadence (<see cref="LeashPollRule"/>).
///
/// <para>Every event is raised on the thread that called in, which in the app is the dispatcher
/// (the friends timer, or the UI awaiting an op). Nothing here uses ConfigureAwait(false).</para>
///
/// <para>The cut: never refused, never priced, never gated. <see cref="CutAsync"/> runs the local
/// safety steps (<see cref="LeashCutSafety"/>), drops the leash from the snapshot at once, and
/// sends <c>cut</c> until the server has it, across restarts. Until then no report is sent and a
/// block that still says leashed is read as not leashed.</para>
/// </summary>
public sealed class LeashService : ILeashService
{
    internal const int SeenCap = 500;

    private readonly ILeashApi _api;
    private readonly Func<string?> _account;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<LeashDayInputs?> _dayInputs;
    private readonly ILeashTab _tab;
    private readonly Action _kick;
    private readonly ILeashCutStore _cutStore;
    private readonly Action _cutSafety;

    private string? _lastAccount;
    private bool _off;
    private bool _cutPending;
    private bool _cutSending;
    private string _signature = "";
    private LeashSnapshot _raw = LeashSnapshot.Empty;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly HashSet<string> _booked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedLocal = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completeUnsent = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watchedAids = new(StringComparer.Ordinal);

    public LeashService(
        ILeashApi api,
        Func<string?> account,
        Func<DateTimeOffset>? now = null,
        Func<LeashDayInputs?>? dayInputs = null,
        ILeashTab? tab = null,
        Action? kick = null,
        ILeashCutStore? cutStore = null,
        Action? cutSafety = null)
    {
        _api = api;
        _account = account;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _dayInputs = dayInputs ?? (() => null);
        _tab = tab ?? new NoTab();
        _kick = kick ?? (() => { });
        _cutStore = cutStore ?? new MemoryCutStore();
        _cutSafety = cutSafety ?? (() => LeashCutSafety.Apply());
        CheckAccount();
    }

    // ---- ILeashService ----

    public bool Available => _account() != null && !_off;

    public LeashSnapshot Snapshot { get; private set; } = LeashSnapshot.Empty;

    public event Action<LeashSnapshot>? SnapshotChanged;

    public event Action<LeashEvent>? EventArrived;

    /// <summary>Raised when this account goes from not leashed to leashed or back (a cut counts
    /// at once). The tray keeps its "Cut leash" reachable off this.</summary>
    public event Action<bool>? LeashedChanged;

    private bool _wasLeashed;

    public Punishment? GateDue => _cutPending ? null : LeashGateRule.Due(Snapshot.Me?.Pending, _now(), _completedLocal);

    /// <summary>True while the poll must run every 20 s: leashed or holding anyone.</summary>
    public bool Active => Snapshot.Active;

    /// <summary>A cut this client made that the server has not confirmed yet.</summary>
    public bool CutPending => _cutPending;

    /// <summary>The last report this client built (null when not leashed).</summary>
    public DayReport? LastReport { get; private set; }

    // ---- the poll piggyback ----

    /// <summary>R for the next poll, or null: only while leashed, never while a cut is pending.</summary>
    public JObject? BuildReportJson()
    {
        if (!CheckAccount() || _cutPending) { LastReport = null; return null; }
        var me = Snapshot.Me;
        if (me == null) { LastReport = null; return null; }
        LeashDayInputs? inputs;
        try { inputs = _dayInputs(); }
        catch (Exception ex) { App.Logger?.Debug("Leash report inputs failed: {E}", ex.Message); inputs = null; }
        if (inputs is not { } w) return null;
        var watched = me.Assignment is { } a && _watchedAids.Contains(a.Aid);
        var r = LeashReportBuilder.Build(w, me.Assignment, watched, _now());
        LastReport = r;
        return LeashReportBuilder.ToWire(r);
    }

    /// <summary>The poll answered. <paramref name="block"/> is its <c>leash</c> value; null means
    /// the key was absent (not leashed, holding nobody, no offer, or the feature is off).</summary>
    public void ApplyBlock(JObject? block)
    {
        if (!CheckAccount()) return;
        var (snap, events) = LeashParse.Block(block);
        _raw = snap;

        if (_cutPending)
        {
            // The server has not heard the cut yet, or answered before it did: this block's leash
            // is the one that was cut. It never comes back on screen.
            if (snap.Me == null) ClearCutPending();
            else
            {
                _raw = snap with { Me = null };
                SendCut();
            }
        }
        ResendCompletes(snap.Me);
        Publish();

        foreach (var e in events)
        {
            if (!_seen.Add(e.Id)) continue;
            _seenOrder.Enqueue(e.Id);
            while (_seenOrder.Count > SeenCap) _seen.Remove(_seenOrder.Dequeue());
            Handle(e);
            try { EventArrived?.Invoke(e); }
            catch (Exception ex) { App.Logger?.Debug("Leash event handler failed: {E}", ex.Message); }
        }

        // A Chaster punishment should arrive as an event, never in the queue. If one ever sits
        // in the queue, it is booked and completed the same way, once.
        if (!_cutPending && _raw.Me is { } me)
            foreach (var p in me.Pending)
                if (p.Kind == PunishKind.Chaster) BookChaster(p);
    }

    /// <summary>The verified watch of an assignment's video finished (the runner's watch).</summary>
    public void NoteAssignmentWatched(string aid)
    {
        if (string.IsNullOrEmpty(aid) || !_watchedAids.Add(aid)) return;
        _kick();
    }

    // ---- holder side ----

    public Task<LeashSendResult> OfferAsync(string friendId) =>
        SendAsync("offer", new JObject { ["to"] = friendId });

    public async Task ReleaseAsync(string leashedId)
    {
        var o = await CallAsync("release", new JObject { ["who"] = leashedId });
        if (o != null) _kick();
    }

    public Task<LeashSendResult> AssignAsync(string leashedId, AssignKind kind, int size, LeashWatch? watch = null)
    {
        if (!LeashGrammar.ValidAssign(kind, size, watch)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Refused));
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.AssignToWire(kind), ["size"] = size };
        if (watch != null) body["watch"] = LeashParse.WatchToWire(watch);
        return SendAsync("assign", body);
    }

    public Task<LeashSendResult> PunishAsync(string leashedId, PunishKind kind, int size, LeashWatch? watch = null)
    {
        if (!LeashGrammar.ValidPunish(kind, size, watch)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Refused));
        // The holder side mirrors the server: a punishment above their intensity is not sent.
        var held = Snapshot.Holding.FirstOrDefault(h => h.Who.Id == leashedId);
        if (held != null && !LeashGrammar.Allowed(kind, held.Intensity))
            return Task.FromResult(new LeashSendResult(LeashSendStatus.NotAllowed));
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.PunishToWire(kind), ["size"] = size };
        if (watch != null) body["watch"] = LeashParse.WatchToWire(watch);
        return SendAsync("punish", body);
    }

    public Task<LeashSendResult> RewardAsync(string leashedId, RewardKind kind, string? stickerOrPoke = null, int? size = null)
    {
        if (!LeashGrammar.ValidReward(kind, stickerOrPoke, size)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Refused));
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.RewardToWire(kind) };
        if (kind == RewardKind.Sticker) body["sticker"] = stickerOrPoke;
        if (kind == RewardKind.Praise) body["poke"] = stickerOrPoke;
        if (kind == RewardKind.Credit) body["size"] = size;
        return SendAsync("reward", body);
    }

    public Task<LeashSendResult> TugAsync(string leashedId) =>
        SendAsync("tug", new JObject { ["who"] = leashedId });

    // ---- leashed side ----

    public async Task<bool> AnswerAsync(string holderId, bool accept, LeashIntensity intensity)
    {
        var body = new JObject { ["from"] = holderId, ["accept"] = accept };
        if (accept) body["intensity"] = LeashParse.IntensityToWire(intensity);
        var o = await CallAsync("answer", body);
        if (o == null || o.Value<bool?>("ok") != true) return false;
        _kick();
        return true;
    }

    public async Task CutAsync()
    {
        // The local safety steps run first and whatever the network does. Never gated.
        try { _cutSafety(); }
        catch (Exception ex) { App.Logger?.Warning("Leash cut safety failed: {E}", ex.Message); }

        var account = _account();
        if (account == null) return;
        _cutPending = true;
        try { _cutStore.Write(account); } catch (Exception ex) { App.Logger?.Debug("Leash cut store failed: {E}", ex.Message); }
        _watchedAids.Clear();
        Publish();
        await SendCutAsync();
    }

    public Task SetIntensityAsync(LeashIntensity intensity) =>
        SettingsAsync(new JObject { ["intensity"] = LeashParse.IntensityToWire(intensity) });

    public Task SetDndAsync(LeashDnd dnd)
    {
        var body = new JObject { ["dnd"] = LeashDndRule.ToWire(dnd) };
        if (dnd == LeashDnd.Today && LeashDndRule.Until(dnd, DateTimeOffset.Now) is { } until)
            body["dnd_until"] = until.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        return SettingsAsync(body);
    }

    public Task SetRemoteModeAsync(LeashRemoteMode mode) =>
        SettingsAsync(new JObject { ["remote_mode"] = LeashParse.RemoteModeToWire(mode) });

    public Task SetVideoMaxAsync(int minutes) =>
        SettingsAsync(new JObject { ["video_max"] = LeashVideoCap.Clamp(minutes) });

    public async Task CompleteAsync(string pid)
    {
        if (string.IsNullOrEmpty(pid)) return;
        _completedLocal.Add(pid);
        _completeUnsent.Add(pid);
        Publish();
        await SendCompleteAsync(pid);
    }

    public async Task<bool> PardonAsync(string pid)
    {
        if (string.IsNullOrEmpty(pid)) return false;
        var o = await CallAsync("pardon", new JObject { ["pid"] = pid });
        if (o == null || o.Value<bool?>("ok") != true || LeashParse.Str(o["status"]) != "pardoned") return false;
        _completedLocal.Add(pid);
        Publish();
        _kick();
        return true;
    }

    // ---- internals ----

    private void Handle(LeashEvent e)
    {
        if (_cutPending) return;
        switch (e.Kind)
        {
            case LeashEventKind.Punish when e.Punishment is { Kind: PunishKind.Chaster } p
                                             && Snapshot.Me is { } holderOf && holderOf.Holder.Id == e.From.Id:
                BookChaster(p);
                break;
            case LeashEventKind.Reward when e.Reward == RewardKind.Credit:
                // A credit books on the leashed side; the event only ever reaches that side.
                if (Snapshot.Me is { } me && me.Holder.Id == e.From.Id && _booked.Add("credit:" + e.Id))
                {
                    var s = LeashChasterRule.CreditSeconds(e.Size);
                    if (s > 0) _tab.BookCredit(s);
                }
                break;
            case LeashEventKind.Ended:
                _watchedAids.Clear();
                _kick();
                break;
        }
    }

    private void BookChaster(Punishment p)
    {
        if (!_booked.Add("pun:" + p.Pid)) return;
        var seconds = LeashChasterRule.PunishSeconds(p.Size, Snapshot.Me?.Intensity);
        if (seconds > 0)
        {
            var applied = _tab.BookPunish(seconds);
            App.Logger?.Information("[Leash] chaster punishment {Pid}: asked {S}s, booked {A}s", p.Pid, seconds, applied);
        }
        // It completes itself whether or not the player's own limits let any of it book.
        _completedLocal.Add(p.Pid);
        _completeUnsent.Add(p.Pid);
        _ = SendCompleteAsync(p.Pid);
    }

    private async Task SendCompleteAsync(string pid)
    {
        var o = await CallAsync("complete", new JObject { ["pid"] = pid });
        if (o != null && o.Value<bool?>("ok") == true)
        {
            _completeUnsent.Remove(pid);
            _kick();
        }
    }

    private void ResendCompletes(MyLeash? me)
    {
        var pending = new HashSet<string>((me?.Pending ?? Array.Empty<Punishment>()).Select(p => p.Pid), StringComparer.Ordinal);
        // The server dropped it: nothing left to hide or resend.
        _completedLocal.RemoveWhere(pid => !pending.Contains(pid));
        _completeUnsent.RemoveWhere(pid => !pending.Contains(pid));
        foreach (var pid in _completeUnsent.ToList()) _ = SendCompleteAsync(pid);
    }

    private async Task SettingsAsync(JObject body)
    {
        var o = await CallAsync("settings", body);
        if (o == null || o.Value<bool?>("ok") != true) return;
        if (LeashParse.Me(o["me"]) is { } me && !_cutPending)
        {
            _raw = _raw with { Me = me };
            Publish();
        }
        _kick();
    }

    private void SendCut() => _ = SendCutAsync();

    private async Task SendCutAsync()
    {
        if (_cutSending) return;
        _cutSending = true;
        try
        {
            var o = await CallAsync("cut", new JObject());
            // ok, or the feature is off (then there is no leash to cut).
            if (o != null && (o.Value<bool?>("ok") == true || LeashParse.Str(o["reason"]) == "off"))
            {
                ClearCutPending();
                _kick();
            }
        }
        finally { _cutSending = false; }
    }

    private void ClearCutPending()
    {
        if (!_cutPending) return;
        _cutPending = false;
        try { _cutStore.Write(null); } catch (Exception ex) { App.Logger?.Debug("Leash cut store failed: {E}", ex.Message); }
    }

    private async Task<LeashSendResult> SendAsync(string op, JObject body)
    {
        var o = await CallAsync(op, body);
        if (o == null) return new LeashSendResult(LeashSendStatus.Failed);
        var ok = o.Value<bool?>("ok") == true;
        var status = LeashParse.SendFromWire(LeashParse.Str(ok ? o["status"] : o["reason"]));
        if (ok) _kick();
        return new LeashSendResult(status, LeashParse.Time(o["dnd_until"]));
    }

    private async Task<JObject?> CallAsync(string op, JObject body)
    {
        if (!CheckAccount()) return null;
        var sentFor = _lastAccount;
        JObject? o;
        try { o = await _api.CallAsync(op, body); }
        catch (Exception ex) { App.Logger?.Debug("Leash {Op} threw: {E}", op, ex.Message); return null; }
        if (_account() != sentFor) return null;
        if (o != null && o.Value<bool?>("ok") == false && LeashParse.Str(o["reason"]) == "off") _off = true;
        else if (o != null && o.Value<bool?>("ok") == true) _off = false;
        return o;
    }

    /// <summary>True while signed in. A different account (or none) forgets everything.</summary>
    private bool CheckAccount()
    {
        var now = _account();
        if (now == _lastAccount) return now != null;
        _lastAccount = now;
        _off = false;
        _raw = LeashSnapshot.Empty;
        _seen.Clear();
        _seenOrder.Clear();
        _booked.Clear();
        _completedLocal.Clear();
        _completeUnsent.Clear();
        _watchedAids.Clear();
        LastReport = null;
        string? stored = null;
        try { stored = _cutStore.Read(); } catch (Exception ex) { App.Logger?.Debug("Leash cut store failed: {E}", ex.Message); }
        _cutPending = now != null && string.Equals(stored, now, StringComparison.Ordinal);
        Publish();
        return now != null;
    }

    private void Publish()
    {
        var me = _cutPending ? null : _raw.Me;
        if (me != null && _completedLocal.Count > 0)
            me = me with { Pending = me.Pending.Where(p => !_completedLocal.Contains(p.Pid)).ToList() };
        var next = _raw with { Me = me };
        string sig;
        try { sig = JsonConvert.SerializeObject(next); } catch { sig = Guid.NewGuid().ToString(); }
        if (sig == _signature) return;
        _signature = sig;
        Snapshot = next;
        try { SnapshotChanged?.Invoke(next); }
        catch (Exception ex) { App.Logger?.Debug("Leash snapshot handler failed: {E}", ex.Message); }
        var leashed = next.Me != null;
        if (leashed == _wasLeashed) return;
        _wasLeashed = leashed;
        try { LeashedChanged?.Invoke(leashed); }
        catch (Exception ex) { App.Logger?.Debug("Leash leashed handler failed: {E}", ex.Message); }
    }

    // ---- app wiring ----

    /// <summary>The app's own wiring: the real wire, the account off AppSettings, the real tab,
    /// the day numbers off the services that already count them.</summary>
    public static LeashService CreateForApp(Action kick) => new(
        new LeashApi(),
        () => BackRoom.BackRoomApi.AppIdentity()?.UnifiedId,
        dayInputs: AppDayInputs,
        tab: new ChasterLeashTab(),
        kick: kick,
        cutStore: new FileCutStore(Path.Combine(App.UserDataPath, "leash_cut_pending.txt")));

    /// <summary>R's numbers, read fresh. Null when the services are not up yet.</summary>
    internal static LeashDayInputs? AppDayInputs()
    {
        var local = DateTime.Now;
        var minutes = 0;
        try
        {
            var key = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var day = App.FeatureDayLog?.Log?.Days?.FirstOrDefault(d => d.D == key);
            minutes = day?.Cm ?? 0;
        }
        catch (Exception ex) { App.Logger?.Debug("Leash minutes read failed: {E}", ex.Message); }

        int done = 0, total = 0;
        try
        {
            var slots = App.Quests?.GetDailySlots();
            if (slots != null)
                foreach (var (q, _) in slots)
                {
                    if (q == null) continue;
                    total++;
                    if (q.IsCompleted) done++;
                }
        }
        catch (Exception ex) { App.Logger?.Debug("Leash quests read failed: {E}", ex.Message); }

        var streak = 0;
        try { streak = App.Achievements?.Progress?.ConsecutiveDays ?? 0; } catch { }

        bool linked = false;
        DateTime? ends = null;
        bool hidden = false;
        int? tab = null;
        try
        {
            var c = App.Chaster;
            if (c != null && c.IsLinked)
            {
                linked = true;
                var l = c.Lock;
                ends = l?.EndsAtUtc;
                hidden = l?.TimerHidden == true;
                tab = c.BalanceSeconds;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Leash chaster read failed: {E}", ex.Message); }

        return new LeashDayInputs(local, minutes, done, total, streak, linked, ends, hidden, tab);
    }

    private sealed class NoTab : ILeashTab
    {
        public int BookPunish(int seconds) => 0;
        public int BookCredit(int seconds) => 0;
    }

    private sealed class MemoryCutStore : ILeashCutStore
    {
        private string? _v;
        public string? Read() => _v;
        public void Write(string? accountId) => _v = accountId;
    }

    /// <summary>One line: the account id whose cut the server has not heard yet.</summary>
    public sealed class FileCutStore : ILeashCutStore
    {
        private readonly string _path;
        public FileCutStore(string path) { _path = path; }

        public string? Read()
        {
            try { return File.Exists(_path) ? File.ReadAllText(_path).Trim() is { Length: > 0 } s ? s : null : null; }
            catch { return null; }
        }

        public void Write(string? accountId)
        {
            try
            {
                if (accountId == null) { if (File.Exists(_path)) File.Delete(_path); }
                else File.WriteAllText(_path, accountId);
            }
            catch (Exception ex) { App.Logger?.Debug("Leash cut file failed: {E}", ex.Message); }
        }
    }
}
