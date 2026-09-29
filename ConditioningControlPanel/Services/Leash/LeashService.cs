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
    private readonly Dictionary<string, DateTimeOffset> _unplayableUntil = new(StringComparer.Ordinal);
    private readonly LeashSentLog _sent = new();
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly Queue<string> _shownOrder = new();

    /// <summary>Where a <c>seen</c> goes: the friends poll's shared receipt channel (the app wires
    /// it in <see cref="CreateForApp"/>). Null = nowhere yet; the ids are still remembered, so an
    /// item is never reported twice.</summary>
    internal Action<string>? ReportSeen { get; set; }

    /// <summary>How long a punishment video that will not play (and that the server could not
    /// drop) stays off the gate before it may be tried again.</summary>
    public static readonly TimeSpan UnplayableHold = TimeSpan.FromHours(24);

    /// <summary>Raised when the leash on THIS account ended from the other side or the server
    /// (holder let go, block, remove, report, expiry, the feature switched off): the same local
    /// safety as the sub's own cut has already run. Never raised for this client's own cut.</summary>
    public event Action? LeashLost;

    public LeashService(
        ILeashApi api,
        Func<string?> account,
        Func<DateTimeOffset>? now = null,
        Func<LeashDayInputs?>? dayInputs = null,
        ILeashTab? tab = null,
        Action? kick = null,
        ILeashCutStore? cutStore = null,
        Action? cutSafety = null,
        Action<string>? reportSeen = null)
    {
        _api = api;
        _account = account;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _dayInputs = dayInputs ?? (() => null);
        _tab = tab ?? new NoTab();
        _kick = kick ?? (() => { });
        _cutStore = cutStore ?? new MemoryCutStore();
        _cutSafety = cutSafety ?? (() => LeashCutSafety.Apply());
        ReportSeen = reportSeen;
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

    public Punishment? GateDue => _cutPending ? null : LeashGateRule.Due(Snapshot.Me?.Pending, _now(), GateSkips());

    /// <summary>Pids the gate passes over: completed here and not yet dropped by the server, or
    /// marked "will not play" within the last <see cref="UnplayableHold"/>.</summary>
    private ISet<string> GateSkips()
    {
        var now = _now();
        foreach (var k in _unplayableUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _unplayableUntil.Remove(k);
        if (_unplayableUntil.Count == 0) return _completedLocal;
        var set = new HashSet<string>(_completedLocal, StringComparer.Ordinal);
        set.UnionWith(_unplayableUntil.Keys);
        return set;
    }

    public bool IsUnplayable(string pid) =>
        !string.IsNullOrEmpty(pid) && _unplayableUntil.TryGetValue(pid, out var until) && until > _now();

    /// <summary>True while the poll must run every 20 s: leashed or holding anyone.</summary>
    public bool Active => Snapshot.Active;

    /// <summary>A cut this client made that the server has not confirmed yet.</summary>
    public bool CutPending => _cutPending;

    /// <summary>The last report this client built (null when not leashed).</summary>
    public DayReport? LastReport { get; private set; }

    // ---- receipts (CONTRACT "Receipts") ----

    public event Action? ReceiptsChanged;

    public bool ReceiptsSupported => _sent.Supported;

    public IReadOnlyList<LeashSentItem> SentTo(string leashedId) =>
        string.IsNullOrEmpty(leashedId) ? Array.Empty<LeashSentItem>() : _sent.For(leashedId, _now());

    /// <summary>The newest offer this account sent <paramref name="friendId"/> (the Offer chip's steps).</summary>
    public LeashSentItem? OfferTo(string friendId) => _sent.LatestOffer(friendId);

    /// <summary>Leashed side: an item is on screen. Each id is reported once per session; the
    /// server drops anything that is not an item addressed to this account.</summary>
    public void NoteShown(string? id)
    {
        if (!LeashSteps.IsId(id) || !CheckAccount()) return;
        if (!_shown.Add(id!)) return;
        _shownOrder.Enqueue(id!);
        while (_shownOrder.Count > SeenCap) _shown.Remove(_shownOrder.Dequeue());
        try { ReportSeen?.Invoke(id!); }
        catch (Exception ex) { App.Logger?.Debug("Leash seen report failed: {E}", ex.Message); }
    }

    /// <summary>Holder side: the sender receipts of a friends poll reply, leash kinds only
    /// (anything else is ignored). <paramref name="present"/> = the reply carried a receipts
    /// array at all, which alone proves the server speaks receipts.</summary>
    public void ApplyReceipts(IEnumerable<LeashReceipt>? receipts, bool present = true)
    {
        if (!CheckAccount()) return;
        bool changed = present && _sent.MarkSupported();
        if (receipts != null)
            foreach (var r in receipts) changed |= _sent.Apply(r);
        if (changed) RaiseReceipts();
    }

    /// <summary>The same from the raw <c>receipts</c> JSON array (null = the key was absent).</summary>
    public void ApplyReceiptsJson(JToken? receipts) => ApplyReceipts(LeashParse.Receipts(receipts), receipts is JArray);

    private void RaiseReceipts()
    {
        try { ReceiptsChanged?.Invoke(); }
        catch (Exception ex) { App.Logger?.Debug("Leash receipts handler failed: {E}", ex.Message); }
    }

    private void NoteSent(LeashItemKind kind, string to, LeashSendResult r,
        PunishKind? punish = null, AssignKind? assign = null, RewardKind? reward = null, int? size = null, string? token = null)
    {
        if (_sent.NoteSent(kind, to, r, _now(), punish, assign, reward, size, token)) RaiseReceipts();
    }

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
        // Leashed a moment ago, by a leash this client did not cut: remember it to catch the end.
        var wasLeashed = !_cutPending && _raw.Me != null;
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
        ForgetUnplayable(snap.Me);
        // The leash went away from the other side (holder let go, block, remove, expiry, the
        // feature off): the same local safety as the sub's own cut, before anything redraws.
        if (wasLeashed && !_cutPending && snap.Me == null) LoseLeash();
        // A punishment's pid and an assignment's aid reach the holder here, not in the send reply.
        bool receipts = _sent.Bind(snap.Holding);
        Publish();

        foreach (var e in events)
        {
            if (!_seen.Add(e.Id)) continue;
            _seenOrder.Enqueue(e.Id);
            while (_seenOrder.Count > SeenCap) _seen.Remove(_seenOrder.Dequeue());
            Handle(e);
            receipts |= _sent.ApplyEvent(e);
            try { EventArrived?.Invoke(e); }
            catch (Exception ex) { App.Logger?.Debug("Leash event handler failed: {E}", ex.Message); }
        }
        if (receipts) RaiseReceipts();

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

    public async Task<LeashSendResult> OfferAsync(string friendId)
    {
        var r = await SendAsync("offer", new JObject { ["to"] = friendId });
        NoteSent(LeashItemKind.Offer, friendId, r);
        return r;
    }

    public async Task<bool> ReleaseAsync(string leashedId)
    {
        var o = await CallAsync("release", new JObject { ["who"] = leashedId });
        if (o == null || o.Value<bool?>("ok") != true) return false;
        _kick();
        return true;
    }

    public async Task<LeashSendResult> AssignAsync(string leashedId, AssignKind kind, int size, LeashWatch? watch = null)
    {
        if (!LeashGrammar.ValidAssign(kind, size, watch)) return new LeashSendResult(LeashSendStatus.Refused);
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.AssignToWire(kind), ["size"] = size };
        if (watch != null) body["watch"] = LeashParse.WatchToWire(watch);
        var r = await SendAsync("assign", body);
        NoteSent(LeashItemKind.Assign, leashedId, r, assign: kind, size: size);
        return r;
    }

    public async Task<LeashSendResult> PunishAsync(string leashedId, PunishKind kind, int size, LeashWatch? watch = null)
    {
        if (!LeashGrammar.ValidPunish(kind, size, watch)) return new LeashSendResult(LeashSendStatus.Refused);
        // The holder side mirrors the server: a punishment above their intensity is not sent.
        var held = Snapshot.Holding.FirstOrDefault(h => h.Who.Id == leashedId);
        if (held != null && !LeashGrammar.Allowed(kind, held.Intensity))
            return new LeashSendResult(LeashSendStatus.NotAllowed);
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.PunishToWire(kind), ["size"] = size };
        if (watch != null) body["watch"] = LeashParse.WatchToWire(watch);
        var r = await SendAsync("punish", body);
        NoteSent(LeashItemKind.Punish, leashedId, r, punish: kind, size: size);
        return r;
    }

    public async Task<LeashSendResult> RewardAsync(string leashedId, RewardKind kind, string? stickerOrPoke = null, int? size = null)
    {
        if (!LeashGrammar.ValidReward(kind, stickerOrPoke, size)) return new LeashSendResult(LeashSendStatus.Refused);
        var body = new JObject { ["who"] = leashedId, ["kind"] = LeashParse.RewardToWire(kind) };
        if (kind == RewardKind.Sticker) body["sticker"] = stickerOrPoke;
        if (kind == RewardKind.Praise) body["poke"] = stickerOrPoke;
        if (kind == RewardKind.Credit) body["size"] = size;
        var r = await SendAsync("reward", body);
        NoteSent(LeashItemKind.Reward, leashedId, r, reward: kind, size: size, token: stickerOrPoke);
        return r;
    }

    public async Task<LeashSendResult> TugAsync(string leashedId)
    {
        var r = await SendAsync("tug", new JObject { ["who"] = leashedId });
        NoteSent(LeashItemKind.Tug, leashedId, r);
        return r;
    }

    // ---- leashed side ----

    public async Task<LeashAnswerResult> AnswerAsync(string holderId, bool accept, LeashIntensity intensity)
    {
        var body = new JObject { ["from"] = holderId, ["accept"] = accept };
        if (accept) body["intensity"] = LeashParse.IntensityToWire(intensity);
        var o = await CallAsync("answer", body);
        var result = AnswerFromWire(o, accept);
        if (result == LeashAnswerResult.Done || result == LeashAnswerResult.Gone) _kick();
        return result;
    }

    /// <summary>The answer reply as the ask card needs it. <c>ok:true</c> alone is not a success:
    /// the server answers an expired or withdrawn offer with <c>{ ok:true, status:"gone" }</c>.</summary>
    internal static LeashAnswerResult AnswerFromWire(JObject? o, bool accept)
    {
        if (o == null) return LeashAnswerResult.Failed;
        var ok = o.Value<bool?>("ok") == true;
        var word = LeashParse.Str(ok ? o["status"] : o["reason"]);
        if (!ok) return word == "off" ? LeashAnswerResult.Off : word == "gone" ? LeashAnswerResult.Gone : LeashAnswerResult.Failed;
        return word switch
        {
            "on" when accept => LeashAnswerResult.Done,
            "declined" when !accept => LeashAnswerResult.Done,
            "gone" => LeashAnswerResult.Gone,
            _ => LeashAnswerResult.Failed,
        };
    }

    public async Task<LeashSkipResult> SkipUnplayableAsync(string pid)
    {
        if (string.IsNullOrEmpty(pid)) return LeashSkipResult.Marked;
        var o = await CallAsync("punish_skip", new JObject { ["pid"] = pid, ["reason"] = "unplayable" });
        // Every worded status answers ok:true (cap, refused, not_found too), so read the status: only
        // `skipped` dropped it. `not_found` means it is already gone, which the next poll confirms.
        var skipStatus = o?.Value<bool?>("ok") == true ? LeashParse.Str(o["status"]) : null;
        if (skipStatus is "skipped" or "not_found")
        {
            // The server dropped it and told the holder; hide it until the next poll agrees.
            _unplayableUntil.Remove(pid);
            _completedLocal.Add(pid);
            Publish();
            _kick();
            App.Logger?.Information("[Leash] punishment {Pid} will not play: skipped on the server", pid);
            return LeashSkipResult.Skipped;
        }
        _unplayableUntil[pid] = _now() + UnplayableHold;
        App.Logger?.Information("[Leash] punishment {Pid} will not play: kept pending, off the gate for {H} h ({Reason})",
            pid, UnplayableHold.TotalHours, o == null ? "no reply" : skipStatus ?? LeashParse.Str(o["reason"]) ?? "?");
        return LeashSkipResult.Marked;
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
        _unplayableUntil.Clear();
        // The cut leash is gone here for good; a later block must not read as a second ending.
        _raw = _raw with { Me = null };
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

    private void LoseLeash()
    {
        App.Logger?.Information("[Leash] the leash ended from the other side; running the cut safety");
        _watchedAids.Clear();
        _unplayableUntil.Clear();
        try { _cutSafety(); }
        catch (Exception ex) { App.Logger?.Warning("Leash cut safety failed: {E}", ex.Message); }
        try { LeashLost?.Invoke(); }
        catch (Exception ex) { App.Logger?.Debug("Leash lost handler failed: {E}", ex.Message); }
    }

    /// <summary>A mark lives only while its punishment is still pending.</summary>
    private void ForgetUnplayable(MyLeash? me)
    {
        if (_unplayableUntil.Count == 0) return;
        var pending = new HashSet<string>((me?.Pending ?? Array.Empty<Punishment>()).Select(p => p.Pid), StringComparer.Ordinal);
        foreach (var pid in _unplayableUntil.Keys.Where(k => !pending.Contains(k)).ToList()) _unplayableUntil.Remove(pid);
    }

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
        // A tug or a reward answers item_id, an offer offer_id (only with "sent"); older servers neither.
        var itemId = ok ? LeashParse.ReceiptId(o["item_id"]) ?? LeashParse.ReceiptId(o["offer_id"]) : null;
        return new LeashSendResult(status, LeashParse.Time(o["dnd_until"]), itemId);
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
        _unplayableUntil.Clear();
        _sent.Clear();
        _shown.Clear();
        _shownOrder.Clear();
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
    /// the day numbers off the services that already count them, and the friends poll's shared
    /// receipt channel both ways (a <c>seen</c> rides the next poll out; the holder's sender
    /// receipts come back on it).</summary>
    public static LeashService CreateForApp(Action kick)
    {
        var svc = new LeashService(
            new LeashApi(),
            () => BackRoom.BackRoomApi.AppIdentity()?.UnifiedId,
            dayInputs: AppDayInputs,
            tab: new ChasterLeashTab(),
            kick: kick,
            cutStore: new FileCutStore(Path.Combine(App.UserDataPath, "leash_cut_pending.txt")));
        if (App.Friends is { } friends) svc.Attach(friends);
        return svc;
    }

    /// <summary>Hooks this service to the friends poll's receipt channel.</summary>
    internal void Attach(Friends.IFriendsService friends) =>
        Attach(friends.ReportReceipt, h => friends.ReceiptsArrived += h);

    /// <summary>The same, by its two halves: <paramref name="report"/> queues a report for the
    /// next poll; <paramref name="subscribe"/> takes the handler for the sender receipts.</summary>
    internal void Attach(Action<Friends.ReceiptReport> report, Action<Action<IReadOnlyList<Friends.SenderReceipt>>> subscribe)
    {
        ReportSeen = id => report(Friends.ReceiptReport.Item(id, Friends.ReceiptState.Seen));
        subscribe(list => ApplyReceipts(FromFriends(list)));
    }

    /// <summary>The friends channel's sender receipts, leash kinds only, in the leash's own shape.</summary>
    internal static IReadOnlyList<LeashReceipt> FromFriends(IEnumerable<Friends.SenderReceipt>? list)
    {
        var mine = new List<LeashReceipt>();
        if (list == null) return mine;
        foreach (var r in list)
        {
            if (r == null || !Friends.ReceiptKind.IsLeash(r.Kind)) continue;
            var kind = LeashSteps.KindFromWire(r.Kind);
            var state = LeashSteps.StateFromWire(r.State);
            if (kind == null || state == null || !LeashSteps.IsId(r.Id) || string.IsNullOrEmpty(r.To)) continue;
            var at = new DateTimeOffset(DateTime.SpecifyKind(r.AtUtc, DateTimeKind.Utc));
            mine.Add(new LeashReceipt(r.Id!, kind.Value, r.To, r.ToName, state.Value, at, r.Ref));
        }
        return mine;
    }

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
