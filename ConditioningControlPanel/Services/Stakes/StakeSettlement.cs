using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Stakes;

/// <summary>A settled stake as the server wrote it (<c>state.settled</c>).</summary>
/// <param name="Result"><c>won</c>, <c>lost</c> or <c>void</c>.</param>
/// <param name="SpDelta">What the stake moved on the wallet, signed.</param>
/// <param name="TimeSeconds">Above 0 only when THIS player lost a time stake.</param>
/// <param name="Sp">The balance after settlement, or null when the reply did not carry one.</param>
public sealed record StakeResult(string Result, int SpDelta, int TimeSeconds, int? Sp)
{
    public bool Won => Result == "won";
    public bool Lost => Result == "lost";
    public bool Void => !Won && !Lost;
}

/// <summary>What one settle did on this machine.</summary>
/// <param name="Result">The server's word, or null when the reply had not settled yet.</param>
/// <param name="BookedSeconds">Seconds that actually landed on Circe's tab (0 when the tab refused, or nothing was owed).</param>
/// <param name="First">True the first time this match settled here; a repeat books nothing.</param>
public sealed record StakeSettleOutcome(StakeResult? Result, int BookedSeconds, bool First)
{
    public static readonly StakeSettleOutcome Pending = new(null, 0, false);
}

/// <summary>
/// WHAT A STAKE REPLY MAY DO ON THIS MACHINE. Two things, both after an await, both guarded:
///
/// <para>(1) THE WALLET RULE (<see cref="V2WalletAdoption"/>). A reply's <c>sp</c> is adopted only
/// for the account the request was sent for, checked again inside the write. A RECEIPT (an offer
/// the server took, which debited or refunded an SP ante, or a settled state) is the server's word
/// and may lower the wallet. Anything else (limits, an unsettled state, a refusal) is a snapshot
/// and may only raise it: the client credits level-up and bubble SP locally before a sync pushes
/// them, and a snapshot must never undo that.</para>
///
/// <para>(2) A LOST TIME STAKE books its seconds on the loser's own Circe's tab through
/// <c>ChasterService.NoteSeconds</c> (row <see cref="StakeRules.LossRowId"/>), the path the leash
/// uses: the player's switches, day and backlog limits, safety hold and Remote cap all still
/// decide, and whatever the tab refuses is simply not owed. Once per match, never for a void,
/// never more than <see cref="StakeRules.MaxTimeSeconds"/>.</para>
/// </summary>
public sealed class StakeSettlement
{
    private readonly Func<string, int, int> _bookTime;
    private readonly Action<string, bool, int>? _adoptSp;
    private readonly Func<string?> _account;
    private readonly object _gate = new();
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);

    /// <param name="bookTime">(row, seconds) to seconds applied. The app's books on App.Chaster.</param>
    /// <param name="adoptSp">(account, receipt, sp): hand a balance to the wallet. The app's marshals
    /// onto the UI thread and re-checks the account there.</param>
    /// <param name="account">Who is signed in now.</param>
    public StakeSettlement(Func<string, int, int> bookTime, Action<string, bool, int>? adoptSp, Func<string?> account)
    {
        _bookTime = bookTime ?? throw new ArgumentNullException(nameof(bookTime));
        _adoptSp = adoptSp;
        _account = account ?? throw new ArgumentNullException(nameof(account));
    }

    // ------------------------------------------------------------------ pure reading

    /// <summary>The settled block of a state reply, or null when it has not settled.</summary>
    public static StakeResult? ReadSettled(JObject? reply)
    {
        if (reply == null || reply.Value<bool?>("ok") != true) return null;
        if (reply["settled"] is not JObject s) return null;
        var result = (s.Value<string?>("result") ?? "").Trim().ToLowerInvariant();
        if (result != "won" && result != "lost") result = "void";
        var time = IntOr(s["time_s"], 0);
        // A time charge only ever rides a loss, and never past the table's ceiling.
        time = result == "lost" ? Math.Clamp(time, 0, StakeRules.MaxTimeSeconds) : 0;
        return new StakeResult(result, IntOr(s["sp_delta"], 0), time, ReadSp(s["sp"]));
    }

    /// <summary>Is this reply a receipt (may lower the wallet) rather than a snapshot.</summary>
    public static bool IsReceipt(string op, JObject? reply)
    {
        if (reply == null || reply.Value<bool?>("ok") != true) return false;
        return op switch
        {
            "offer" => true,
            "state" => reply["settled"] is JObject,
            _ => false,
        };
    }

    /// <summary>The balance a reply carries: the settled one when settled, else the top-level one.</summary>
    public static int? BalanceOf(JObject? reply)
    {
        if (reply == null) return null;
        if (reply["settled"] is JObject s && ReadSp(s["sp"]) is int settled) return settled;
        return ReadSp(reply["sp"]);
    }

    public static string Key(string game, string match) => (game ?? "") + ":" + (match ?? "");

    // ------------------------------------------------------------------ what a reply does

    /// <summary>Hand a reply's balance to the wallet under the wallet rule. For every op.</summary>
    public void AdoptBalance(string op, string? sentFor, JObject? reply)
    {
        if (_adoptSp == null || string.IsNullOrEmpty(sentFor)) return;
        if (BalanceOf(reply) is not int sp || sp < 0) return;
        try { _adoptSp(sentFor!, IsReceipt(op, reply), sp); }
        catch (Exception ex) { App.Logger?.Debug("Stakes: adopt sp failed: {E}", ex.Message); }
    }

    /// <summary>
    /// A state reply for a finished match. Adopts the balance, and books a lost time stake once.
    /// Books nothing when the account that asked is not the one signed in now.
    /// </summary>
    public StakeSettleOutcome Settle(string game, string match, string? sentFor, JObject? stateReply)
    {
        var result = ReadSettled(stateReply);
        if (result == null) return StakeSettleOutcome.Pending;
        AdoptBalance("state", sentFor, stateReply);

        bool first;
        lock (_gate) first = _settled.Add(Key(game, match) + ":" + (sentFor ?? ""));
        if (!first) return new StakeSettleOutcome(result, 0, false);

        var booked = 0;
        if (result.Lost && result.TimeSeconds > 0 && !string.IsNullOrEmpty(sentFor)
            && string.Equals(SafeAccount(), sentFor, StringComparison.Ordinal))
        {
            try { booked = Math.Max(0, _bookTime(StakeRules.LossRowId, result.TimeSeconds)); }
            catch (Exception ex) { App.Logger?.Debug("Stakes: booking a lost time stake failed: {E}", ex.Message); }
        }
        return new StakeSettleOutcome(result, booked, true);
    }

    /// <summary>
    /// Ask whether a finished match has settled, on the backoff in
    /// <see cref="StakeRules.SettlePollDelays"/>, and settle it the moment it has. Gives up after
    /// the last step: that is a void here, nothing booked. Returns the last reply (settled or
    /// not, null when nothing ever answered) and what the settle did.
    /// </summary>
    public async Task<(JObject? Reply, StakeSettleOutcome Outcome)> WatchAsync(
        IStakeApi api, string game, string match,
        Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken ct = default)
    {
        delay ??= (t, c) => Task.Delay(t, c);
        var sentFor = api.Account();
        if (string.IsNullOrEmpty(sentFor)) return (null, StakeSettleOutcome.Pending);
        var body = new JObject { ["game"] = game, ["match"] = match };
        JObject? last = null;
        foreach (var wait in StakeRules.SettlePollDelays)
        {
            try { await delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            if (ct.IsCancellationRequested) break;
            var reply = await api.CallAsync("state", body, ct).ConfigureAwait(false);
            if (reply != null) last = reply;
            // A worded refusal will not change its mind by being asked again.
            if (reply != null && reply.Value<bool?>("ok") == false) break;
            var outcome = Settle(game, match, sentFor, reply);
            if (outcome.Result != null) return (reply, outcome);
        }
        return (last, StakeSettleOutcome.Pending);
    }

    private string? SafeAccount()
    {
        try { return _account(); } catch { return null; }
    }

    private static int? ReadSp(JToken? t) =>
        t is JValue { Type: JTokenType.Integer } v ? v.Value<int>() : null;

    private static int IntOr(JToken? t, int fallback) =>
        t is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? (int)Math.Round(v.Value<double>()) : fallback;

    // ------------------------------------------------------------------ the app's one

    private static StakeSettlement? _shared;

    /// <summary>The app's settlement: books on App.Chaster, adopts on the UI thread.</summary>
    public static StakeSettlement Shared => _shared ??= CreateForApp();

    private static StakeSettlement CreateForApp() => new(
        // The row id is literal on purpose: the price-row wiring test reads it.
        (_, seconds) => App.Chaster?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0,
        // The account is checked AGAIN inside the marshalled write (the canAdopt shape
        // BackRoomBridge.AdoptSp uses): the player may be somebody else by then.
        (account, receipt, sp) => App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (App.Settings?.Current is not { } s) return;
            var same = string.Equals(BackRoom.BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
            if (!V2WalletAdoption.Decide(same, receipt, sp, s.SkillPoints, out var next)) return;
            s.SkillPoints = next;
            App.Settings.Save();
        })),
        () => BackRoom.BackRoomApi.AppIdentity()?.UnifiedId);
}
