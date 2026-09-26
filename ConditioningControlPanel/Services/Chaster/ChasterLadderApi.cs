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

/// <summary>The ladder wire as the service sees it. Nothing throws: a fault is null or false.</summary>
public interface IChasterLadderApi
{
    /// <summary>Ask the server to read this month's adds off the lock's own Chaster history.
    /// The access token goes to our proxy for those two reads and nothing else.</summary>
    Task<LadderVerify?> VerifyAsync(string lockId, string accessToken, CancellationToken ct = default);
    Task<bool> OptInAsync(bool showName, CancellationToken ct = default);
    Task<LadderBoard?> TopAsync(CancellationToken ct = default);
}

/// <summary>
/// POST /chaster/ladder/{verify,optin,top} on the proxy, with the account's token door (the pair
/// <see cref="BackRoomApi.AppIdentity"/> stamps). A proxy without the routes answers 404, which
/// reads as null here: the page then simply shows no ladder. Never retried.
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
        if (o.Value<bool?>("ok") == true) return new LadderVerify(true, Math.Max(0, o.Value<int?>("added_seconds") ?? 0), null);
        return new LadderVerify(false, 0, o.Value<string?>("reason") ?? "refused");
    }

    public async Task<bool> OptInAsync(bool showName, CancellationToken ct = default)
    {
        var o = await CallAsync("optin", new JObject { ["show_name"] = showName }, ct).ConfigureAwait(false);
        return o?.Value<bool?>("ok") == true;
    }

    public async Task<LadderBoard?> TopAsync(CancellationToken ct = default) =>
        ParseBoard(await CallAsync("top", null, ct).ConfigureAwait(false));

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
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chaster/ladder/{op}")
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
            App.Logger?.Debug("Chaster ladder {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }
}
