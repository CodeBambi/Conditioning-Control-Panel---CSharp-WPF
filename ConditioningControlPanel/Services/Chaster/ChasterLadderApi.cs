using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The raffle wire as the service sees it. Nothing throws: a fault is null or false.</summary>
public interface IChasterLadderApi
{
    /// <summary>Ask the server to read this month's adds and days off the lock's own Chaster
    /// history. The access token goes to our proxy for those reads and nothing else.</summary>
    Task<LadderVerify?> VerifyAsync(string lockId, string accessToken, CancellationToken ct = default);

    /// <summary>"Post my days in Discord". True when the server took it.</summary>
    Task<bool> OptInAsync(bool postDays, CancellationToken ct = default);

    /// <summary>The player's own raffle card.</summary>
    Task<RaffleCard?> MeAsync(CancellationToken ct = default);

    /// <summary>The month's top ten, brag rights only (the pinned scrap beside the raffle).</summary>
    Task<LadderBoard?> TopAsync(CancellationToken ct = default);

    /// <summary>"Show my name on the ladder". True when the server took it.</summary>
    Task<bool> ShowNameAsync(bool showName, CancellationToken ct = default);
}

/// <summary>
/// POST /chaster/raffle/{verify,optin,me,top,name} on the proxy, with the account's token door (the pair
/// <see cref="BackRoomApi.AppIdentity"/> stamps). A proxy without the routes answers 404, which
/// reads as null here: the page then simply shows no raffle card. Never retried.
/// </summary>
public sealed class ChasterLadderApi : IChasterLadderApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public ChasterLadderApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<LadderVerify?> VerifyAsync(string lockId, string accessToken, CancellationToken ct = default) =>
        ParseVerify(await CallAsync("verify", new JObject { ["lock_id"] = lockId, ["access_token"] = accessToken }, ct).ConfigureAwait(false));

    internal static LadderVerify? ParseVerify(JObject? o)
    {
        if (o == null) return null;
        if (o.Value<bool?>("ok") == true)
            return new LadderVerify(true, Math.Max(0, o.Value<int?>("added_seconds") ?? 0), null, Math.Clamp(o.Value<int?>("days_counted") ?? 0, 0, 31));
        return new LadderVerify(false, 0, o.Value<string?>("reason") ?? "refused");
    }

    public async Task<bool> OptInAsync(bool postDays, CancellationToken ct = default)
    {
        var o = await CallAsync("optin", new JObject { ["post_days"] = postDays }, ct).ConfigureAwait(false);
        return o?.Value<bool?>("ok") == true;
    }

    public async Task<RaffleCard?> MeAsync(CancellationToken ct = default) =>
        ParseCard(await CallAsync("me", null, ct).ConfigureAwait(false));

    public async Task<bool> ShowNameAsync(bool showName, CancellationToken ct = default)
    {
        var o = await CallAsync("name", new JObject { ["show_name"] = showName }, ct).ConfigureAwait(false);
        return o?.Value<bool?>("ok") == true;
    }

    public async Task<LadderBoard?> TopAsync(CancellationToken ct = default) =>
        ParseBoard(await CallAsync("top", null, ct).ConfigureAwait(false));

    /// <summary>The board, or null for a bad reply. Ten rows at most; a junk row is dropped.</summary>
    internal static LadderBoard? ParseBoard(JObject? o)
    {
        if (o == null || o.Value<bool?>("ok") != true) return null;
        var rows = new List<LadderRow>();
        if (o["rows"] is JArray arr)
            foreach (var t in arr)
                if (t is JObject r && ParseRow(r) is { } row && rows.Count < ChasterLadder.TopCount) rows.Add(row);
        var you = o["you"] is JObject y ? ParseRow(y, you: true) : null;
        return new LadderBoard(o.Value<string?>("month") ?? "", rows, you, o.Value<bool?>("show_name") == true);
    }

    private static LadderRow? ParseRow(JObject r, bool you = false)
    {
        var rank = r.Value<int?>("rank") ?? 0;
        var name = r.Value<string?>("name");
        if (rank <= 0 || string.IsNullOrWhiteSpace(name)) return null;
        if (name.Length > 32) name = name[..32];
        return new LadderRow(rank, name, r.Value<bool?>("named") == true,
            Math.Max(0, r.Value<int?>("added_seconds") ?? 0), you || r.Value<bool?>("you") == true);
    }

    /// <summary>The card, or null for a bad reply. Anything out of range is clamped, never trusted.</summary>
    internal static RaffleCard? ParseCard(JObject? o)
    {
        if (o == null || o.Value<bool?>("ok") != true) return null;
        var month = o.Value<string?>("month") ?? "";
        var dim = o.Value<int?>("days_in_month") ?? 0;
        if (ChasterRaffle.MonthStart(month) == null || dim < 28 || dim > 31) return null;
        var days = new List<int>();
        if (o["days"] is JArray arr)
            foreach (var t in arr)
                if (t.Type == JTokenType.Integer && t.Value<int>() is var d && d >= 1 && d <= dim && !days.Contains(d)) days.Add(d);
        days.Sort();
        var ticket = o.Value<int?>("ticket");
        return new RaffleCard(
            month,
            dim,
            Math.Clamp(o.Value<int?>("today") ?? 0, 0, dim + 1),
            days,
            Math.Max(0, o.Value<long?>("total_seconds") ?? 0),
            Math.Clamp(o.Value<int?>("need_days") ?? ChasterRaffle.DefaultNeedDays, 1, dim),
            Math.Max(1, o.Value<long?>("need_seconds") ?? ChasterRaffle.DefaultNeedSeconds),
            o.Value<bool?>("post_days") == true,
            o.Value<bool?>("tickets_frozen") == true,
            ticket is > 0 ? ticket : null);
    }

    private async Task<JObject?> CallAsync(string op, JObject? body, CancellationToken ct)
    {
        var id = _identity();
        if (id == null) return null;
        var o = body ?? new JObject();
        o["unified_id"] = id.Value.UnifiedId;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chaster/raffle/{op}")
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<JToken>(text, ReadSettings) as JObject;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Chaster raffle {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }
}
