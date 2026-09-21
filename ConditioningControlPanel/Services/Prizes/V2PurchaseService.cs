using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// Buying a v2 prize without the Back Room (owner decision, 2026-09-19).
///
/// <para>It is the SAME purchase the Prize Parlour makes: <c>GET counter/state</c> for the price and
/// the sale, then <c>POST counter/buy</c> with the row's id and the version the counter just named,
/// both through <see cref="BackRoomApi"/>. The server asks for nothing a seat would give it - the
/// route's gate is the account's token plus the Back Room door - so the panel can reach it from
/// where the options live. The relay already adopts the reply's <c>sp</c> and hands its
/// <c>prizes</c> block to <see cref="OwnershipService"/>, so a buy debits the wallet and unlocks
/// the dials before any sync.</para>
///
/// <para><b>One request per prize, ever.</b> The idem key is made once per prize and kept: a prize
/// can only be bought once, so replaying the same key is exactly right, and the server's receipt
/// turns a second press (or the relay's own network retry) into a byte-identical replay rather than
/// a second debit. The in-flight set is the first guard; the receipt is the one that holds.</para>
///
/// <para><b>Lazily, never on a clock.</b> The counter is read the first time a box asks and cached
/// for the session; it is re-read after a buy and when grants move under us. Nothing polls.</para>
///
/// <para>WPF-free. <see cref="Changed"/> and <see cref="Bought"/> fire on whatever thread finished
/// the work; the controls marshal.</para>
/// </summary>
public sealed class V2PurchaseService
{
    private sealed record Row(int PriceSp, bool OnSale);

    private readonly IBackRoomRelay _relay;
    private readonly Func<string?> _account;
    private readonly Func<string, bool> _owned;
    private readonly Func<int> _sp;

    private readonly object _gate = new();
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idem = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private bool _known;
    private bool _open;
    private int _catalogVersion;
    private bool _fetching;
    /// <summary>Which account the cached counter was read for. The door is per account, so a
    /// signed-in cache says nothing about whoever signs in next.</summary>
    private string? _fetchedFor;

    /// <summary>A row's appearance changed: repaint.</summary>
    public event Action? Changed;
    /// <summary>A prize just landed (the id). The panel says so and points at the options.</summary>
    public event Action<string>? Bought;

    /// <param name="relay">The station relay. The app hands in a <see cref="BackRoomApi"/>.</param>
    /// <param name="account">The signed-in unified id, or null for no account and offline mode:
    /// the row then offers the way in instead. It also pins the cache to one account.</param>
    /// <param name="owned">prizeId to ownership. The app reads <see cref="PrizeGrants"/>.</param>
    /// <param name="sp">The balance the wallet shows.</param>
    public V2PurchaseService(IBackRoomRelay relay, Func<string?> account, Func<string, bool> owned, Func<int> sp)
    {
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _owned = owned ?? throw new ArgumentNullException(nameof(owned));
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    /// <summary>What the row for <paramref name="prizeId"/> shows right now.</summary>
    public V2PurchaseRow RowFor(string prizeId)
    {
        bool owned;
        try { owned = _owned(prizeId); } catch { owned = false; }
        var account = AccountSafe();
        int sp;
        try { sp = _sp(); } catch { sp = 0; }

        lock (_gate)
        {
            _rows.TryGetValue(prizeId, out var row);
            _failures.TryGetValue(prizeId, out var failure);
            // A cache read for someone else is not knowledge about this account.
            var known = _known && string.Equals(_fetchedFor, account, StringComparison.Ordinal);
            return V2PurchaseRule.Decide(owned, account != null, known, _open,
                row?.OnSale ?? false, row?.PriceSp ?? 0, sp, _busy.Contains(prizeId), failure);
        }
    }

    /// <summary>
    /// Read the counter once, the first time a box is shown. Cheap and idempotent: a second call
    /// while the first is in flight joins it, and a call after a good read does nothing.
    /// </summary>
    public void EnsureState() => Start(onlyIfUnknown: true);

    /// <summary>Read the counter again (after a buy, or when grants moved). No-op while one is in flight.</summary>
    public void Refresh() => Start(onlyIfUnknown: false);

    private void Start(bool onlyIfUnknown)
    {
        var account = AccountSafe();
        if (account == null) return;
        lock (_gate)
        {
            // _fetching is the in-flight flag rather than the Task itself: FetchAsync can finish
            // before Task.Run's result is assigned, and then the assignment would strand it.
            var known = _known && string.Equals(_fetchedFor, account, StringComparison.Ordinal);
            if (_fetching || (onlyIfUnknown && known)) return;
            _fetching = true;
        }
        _ = Task.Run(FetchAsync);
    }

    /// <summary>
    /// Drop the cached counter (sign out, account switch). The next box to appear reads it fresh.
    /// Failures and the in-flight set go with it; the idem keys do NOT, because a key that already
    /// bought something must keep replaying if the same account comes back.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _known = false;
            _open = false;
            _catalogVersion = 0;
            _fetchedFor = null;
            _rows.Clear();
            _failures.Clear();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Buy one row. The caller has already confirmed the spend. Answers true when the account holds
    /// the prize afterwards, whether this call bought it or a receipt replayed. A second call while
    /// the first is in flight answers false and sends nothing.
    /// </summary>
    public async Task<bool> BuyAsync(string prizeId)
    {
        if (string.IsNullOrEmpty(prizeId)) return false;

        int version;
        string idem;
        lock (_gate)
        {
            if (!_busy.Add(prizeId)) return false;   // a press already in flight owns this row
            _failures.Remove(prizeId);
            version = _catalogVersion;
            if (!_idem.TryGetValue(prizeId, out idem!)) _idem[prizeId] = idem = V2PurchaseRule.NewIdem();
        }
        Changed?.Invoke();

        bool bought = false;
        try
        {
            if (version <= 0)
            {
                // The row cannot offer without a version, so this is a torn cache rather than a
                // first press. Read the counter and take whatever it says.
                await FetchNowAsync().ConfigureAwait(false);
                lock (_gate) version = _catalogVersion;
            }
            if (version <= 0)
            {
                lock (_gate) _failures[prizeId] = "v2_get_error_offline";
                return false;
            }

            var body = new JObject { ["prizeId"] = prizeId, ["catalogVersion"] = version };
            var res = await _relay.RelayAsync("counter", "buy", idem, body).ConfigureAwait(false);
            // The relay has already adopted the receipt's sp and applied its prizes block, so by
            // here ownership and the wallet are settled; all that is left is what to say.
            var failure = V2PurchaseRule.FailureKeyFor(res.Ok, res.Reason);
            bought = res.Ok || res.Reason == "owned";
            lock (_gate)
            {
                if (failure != null) _failures[prizeId] = failure;
                if (res.Reason == "catalog_changed") _known = false;   // the next read re-learns it
            }
            App.Logger?.Information("[Prizes] standalone buy {Prize}: ok={Ok} status={Status} reason={Reason}",
                prizeId, res.Ok, res.Status, res.Reason ?? "-");
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Prizes] standalone buy {Prize} threw: {Error}", prizeId, ex.Message);
            lock (_gate) _failures[prizeId] = "v2_get_error_generic";
        }
        finally
        {
            lock (_gate) _busy.Remove(prizeId);
            Changed?.Invoke();
        }

        if (bought)
        {
            Bought?.Invoke(prizeId);
            // The receipt settled the balance and the grants; this is for `owned` (nothing came
            // back in that reply) and for the sale flags.
            Refresh();
        }
        return bought;
    }

    /// <summary>The signed-in unified id, or null. Blank counts as null.</summary>
    private string? AccountSafe()
    {
        try
        {
            var id = _account();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch { return null; }
    }

    /// <summary>A read the caller waits for. Skipped (false) when one is already in flight.</summary>
    private async Task<bool> FetchNowAsync()
    {
        lock (_gate)
        {
            if (_fetching) return false;
            _fetching = true;
        }
        await FetchAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>The read itself. The caller has already taken <c>_fetching</c>; this clears it.</summary>
    private async Task FetchAsync()
    {
        var account = AccountSafe();
        try
        {
            var res = await _relay.RelayAsync("counter", "state", null, null).ConfigureAwait(false);
            lock (_gate)
            {
                if (res.Ok && res.Body is JObject o)
                {
                    _fetchedFor = account;
                    _open = o.Value<bool?>("open") == true;
                    _catalogVersion = o.Value<int?>("catalogVersion") ?? 0;
                    _rows.Clear();
                    foreach (var entry in (o["catalog"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var id = entry.Value<string>("id");
                        if (string.IsNullOrEmpty(id)) continue;
                        _rows[id] = new Row(entry.Value<int?>("priceSp") ?? 0,
                            string.Equals(entry.Value<string>("sale"), "on", StringComparison.Ordinal));
                    }
                    _known = _catalogVersion > 0;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Prizes] counter state read failed: {Error}", ex.Message);
        }
        finally
        {
            lock (_gate) _fetching = false;
            Changed?.Invoke();
        }
    }
}
