using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Stakes;

/// <summary>
/// ONE BRIDGE FOR EVERY GAME PAGE THAT TAKES STAKES (chess, the Goon Game). A host hands it the
/// page's stake frames and a way to post back; it talks to <see cref="IStakeApi"/> and runs
/// settlement in C#.
///
/// <para>page -&gt; host:
/// <c>{type:'stake-limits'}</c>,
/// <c>{type:'stake-offer', match, kind, amount, idem?}</c>,
/// <c>{type:'stake-state', match}</c>,
/// <c>{type:'stake-settle', match}</c> (the match is over: poll until settled, then book).</para>
///
/// <para>host -&gt; page: <c>{type:'stake', op, ...the route reply verbatim}</c>. The host adds
/// <c>time_ok</c> and <c>labels</c> to <c>limits</c>, <c>booked_s</c> to a settled
/// <c>state</c>, and <c>op:'settled'</c> (with <c>gave_up</c> when nothing settled in time) for
/// the watch. A fault the page can show is <c>{ok:false, reason}</c> like any refusal
/// (<c>offline</c> when nothing answered, <c>no_chaster</c> when time is not on offer).</para>
///
/// <para>Picking a TIME stake is the consent to <see cref="StakeRules.LossRowId"/>: the bridge
/// switches that row on before it sends the offer, and offers time at all only while the player's
/// Chaster tab could book it.</para>
/// </summary>
public sealed class StakeBridge
{
    private readonly string _game;
    private readonly IStakeApi _api;
    private readonly StakeSettlement _settlement;
    private readonly Func<bool> _timeOk;
    private readonly Action _enableLossRow;
    private readonly Func<JObject> _labels;
    private readonly Action<JObject> _post;

    public StakeBridge(string game, Action<JObject> post, IStakeApi api, StakeSettlement settlement,
        Func<bool> timeOk, Action enableLossRow, Func<JObject>? labels = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _timeOk = timeOk ?? (() => false);
        _enableLossRow = enableLossRow ?? (() => { });
        _labels = labels ?? StakeText.Labels;
    }

    /// <summary>The app's bridge for one game (<c>pbp</c> or <c>goon</c>).</summary>
    public static StakeBridge ForApp(string game, Action<JObject> post) =>
        new(game, post, new StakeApi(), StakeSettlement.Shared, AppTimeOk, AppEnableLossRow);

    public static bool Handles(string? type) =>
        type is "stake-limits" or "stake-offer" or "stake-state" or "stake-settle";

    /// <summary>One page frame. Returns the work (tests await it; hosts may ignore it). Never throws.</summary>
    public Task Handle(JObject msg)
    {
        try
        {
            return (string?)msg["type"] switch
            {
                "stake-limits" => LimitsAsync(),
                "stake-offer" => OfferAsync(msg),
                "stake-state" => StateAsync(msg),
                "stake-settle" => SettleAsync(msg),
                _ => Task.CompletedTask,
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Stakes: bridge threw on {Type}: {E}", (string?)msg["type"], ex.Message);
            return Task.CompletedTask;
        }
    }

    private async Task LimitsAsync()
    {
        var sentFor = _api.Account();
        var reply = await _api.CallAsync("limits", new JObject()).ConfigureAwait(false);
        _settlement.AdoptBalance("limits", sentFor, reply);
        var o = reply != null ? (JObject)reply.DeepClone() : Refusal(sentFor == null ? "signin" : "offline");
        o["time_ok"] = SafeTimeOk();
        try { o["labels"] = _labels(); } catch { /* the page has English of its own */ }
        Post("limits", o);
    }

    private async Task OfferAsync(JObject msg)
    {
        var match = Match(msg);
        if (match == null) { Post("offer", Refusal("no_match")); return; }
        var stake = StakeRules.Normalise((string?)msg["kind"], IntOr(msg["amount"]));
        if (stake is not { } s) { Post("offer", Refusal("bad_amount")); return; }
        if (s.Kind == StakeKind.Time)
        {
            if (!SafeTimeOk()) { Post("offer", Refusal("no_chaster")); return; }
            // The pick IS the consent: the row a loss books on goes on now, before anything is sent.
            try { _enableLossRow(); } catch (Exception ex) { App.Logger?.Debug("Stakes: loss row switch failed: {E}", ex.Message); }
        }
        var idem = (string?)msg["idem"];
        if (string.IsNullOrWhiteSpace(idem) || idem!.Length > 64) idem = Guid.NewGuid().ToString("N");
        var sentFor = _api.Account();
        var reply = await _api.CallAsync("offer", new JObject
        {
            ["game"] = _game,
            ["match"] = match,
            ["kind"] = StakeRules.KindWire(s.Kind),
            ["amount"] = s.Amount,
            ["idem"] = idem,
        }).ConfigureAwait(false);
        _settlement.AdoptBalance("offer", sentFor, reply);
        Post("offer", reply != null ? (JObject)reply.DeepClone() : Refusal(sentFor == null ? "signin" : "offline"));
    }

    private async Task StateAsync(JObject msg)
    {
        var match = Match(msg);
        if (match == null) { Post("state", Refusal("no_match")); return; }
        var sentFor = _api.Account();
        var reply = await _api.CallAsync("state", new JObject { ["game"] = _game, ["match"] = match }).ConfigureAwait(false);
        if (reply == null) { Post("state", Refusal(sentFor == null ? "signin" : "offline")); return; }
        var o = (JObject)reply.DeepClone();
        // Whichever sees the settled reply first books it; the settlement books a match once.
        var outcome = _settlement.Settle(_game, match, sentFor, reply);
        if (outcome.Result == null) _settlement.AdoptBalance("state", sentFor, reply);
        else o["booked_s"] = outcome.BookedSeconds;
        Post("state", o);
    }

    private async Task SettleAsync(JObject msg)
    {
        var match = Match(msg);
        if (match == null) return;
        var (reply, outcome) = await _settlement.WatchAsync(_api, _game, match).ConfigureAwait(false);
        var o = outcome.Result != null && reply != null ? (JObject)reply.DeepClone() : new JObject { ["ok"] = true, ["gave_up"] = true };
        o["match"] = match;
        if (outcome.Result != null) o["booked_s"] = outcome.BookedSeconds;
        Post("settled", o);
    }

    private void Post(string op, JObject o)
    {
        o["type"] = "stake";
        o["op"] = op;
        o["game"] = _game;
        try { _post(o); }
        catch (Exception ex) { App.Logger?.Debug("Stakes: post failed: {E}", ex.Message); }
    }

    private bool SafeTimeOk()
    {
        try { return _timeOk(); } catch { return false; }
    }

    private static JObject Refusal(string reason) => new() { ["ok"] = false, ["reason"] = reason };

    /// <summary>A match id the page may name: short, printable, no spaces.</summary>
    internal static string? Match(JObject msg)
    {
        var m = ((string?)msg["match"])?.Trim();
        if (string.IsNullOrEmpty(m) || m.Length > 80) return null;
        foreach (var c in m) if (c < 0x21 || c > 0x7e) return null;
        return m;
    }

    private static int IntOr(JToken? t) =>
        t is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? (int)Math.Round(v.Value<double>())
        : t is JValue { Type: JTokenType.String } sv && int.TryParse((string?)sv, out var n) ? n : 0;

    // ------------------------------------------------------------------ the app's gates

    /// <summary>Time is on offer while the Chaster tab could book a loss right now.</summary>
    public static bool AppTimeOk()
    {
        var chaster = App.Chaster;
        var s = App.Settings?.Current;
        if (chaster == null || s == null) return false;
        return StakeRules.TimeAllowed(chaster.IsLinked, s.ChasterTabEnabled, s.ChasterPaused, chaster.SafetyHoldRemaining);
    }

    /// <summary>Switch <see cref="StakeRules.LossRowId"/> on, on the UI thread, the way the tab
    /// page does: a new list every time, never one being edited.</summary>
    public static void AppEnableLossRow()
    {
        void Apply()
        {
            if (App.Settings?.Current is not { } s) return;
            var now = s.ChasterPrices ?? new List<string>();
            if (now.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(now) { StakeRules.LossRowId };
            App.Settings.Save();
        }
        var d = App.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Apply(); else d.Invoke(Apply);
    }
}

/// <summary>The words a stake row and a result line use, localised by the host so every page
/// says the same thing. Keys are <c>stake_*</c> in all nine language files; each falls back to
/// the English here.</summary>
public static class StakeText
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["stake_title"] = "Stake",
        ["stake_off"] = "Off",
        ["stake_minutes"] = "{0} min",
        ["stake_sparkles"] = "{0} ✦",
        ["stake_you"] = "You",
        ["stake_them"] = "Them",
        ["stake_locked"] = "Locked",
        ["stake_none"] = "No stake",
        ["stake_won_sp"] = "+{0} ✦",
        ["stake_lost_time"] = "+{0} min on your lock",
        ["stake_lost_sp"] = "Stake lost",
        ["stake_returned"] = "Stake returned",
        ["stake_pending"] = "Settling the stake",
        ["stake_refused_daily_cap"] = "No more stakes today",
        ["stake_refused_pair_cap"] = "Already staked with them today",
        ["stake_refused_insufficient_sp"] = "Not enough ✦",
        ["stake_refused_started"] = "Too late, the game started",
        ["stake_refused_signin"] = "Sign in to stake",
        ["stake_refused_other"] = "Stake not taken",
    };

    /// <summary>Every label, keyed without the <c>stake_</c> prefix.</summary>
    public static JObject Labels()
    {
        var o = new JObject();
        foreach (var (key, english) in English)
        {
            string text;
            try
            {
                var v = Loc.Get(key);
                text = string.IsNullOrWhiteSpace(v) || v == key ? english : v;
            }
            catch { text = english; }
            o[key.Substring("stake_".Length)] = text;
        }
        return o;
    }
}
