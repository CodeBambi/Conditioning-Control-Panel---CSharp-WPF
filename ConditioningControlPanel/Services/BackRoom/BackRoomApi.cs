using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>One answer to one <c>station-request</c> (CONTRACT section 2.2, <c>station-result</c>).
/// A host refusal has <see cref="Status"/> 0 and a reason of <c>offline</c>, <c>closed</c> or
/// <c>bad_op</c>; a request that ran out of time is <c>timeout</c>.</summary>
public sealed record BackRoomStationResult(bool Ok, int Status, string? Reason, JToken? Body)
{
    public static BackRoomStationResult Refuse(string reason) => new(false, 0, reason, null);
}

/// <summary>What the host needs from the relay. The service owns one; tests hand in a fake.</summary>
public interface IBackRoomRelay
{
    Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct = default);
}

/// <summary>
/// THE STATION RELAY (CONTRACT section 3). The only way a Back Room page reaches the server: the
/// page names a station and an op, this class looks the pair up in <see cref="Ops"/>, attaches the
/// account's token door (<c>X-Auth-Token</c> plus <c>unified_id</c>, the same pair
/// ProfileSyncService and the Arcademy sync services send) and hands back one result. The page
/// never sees the token, the base url or a fetch, so a station cannot call a route this table
/// does not name.
///
/// <para>Budget: <see cref="Timeout"/> across every attempt, because the page gives up at 6 s and
/// an answer after that helps nobody. A request that failed on the NETWORK (never reached the
/// server) is sent once more with the same <c>idem</c>, which the server's receipt table turns
/// into a byte-identical replay if the first one did land after all. Nothing else is retried.</para>
///
/// <para>Any <c>sp</c> in a reply is adopted through <c>adoptSp</c> the moment it arrives: the
/// server settles the whole tape up front (Law I), so the true balance is always the reply's.</para>
/// </summary>
public sealed class BackRoomApi : IBackRoomRelay
{
    public const string BaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>Total time for one relay, retries included.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The whitelist. One row per station, and the ONLY C# that changes when a station is added
    /// (CONTRACT section 7). op maps to <c>{METHOD} /v2/backroom/{station}/{op}</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Method, string Path)[]> Ops =
        new Dictionary<string, (string Method, string Path)[]>(StringComparer.Ordinal)
        {
            ["slot"] = new[] { ("GET", "state"), ("POST", "tape"), ("POST", "cursor") },
            // ["wheel"] = new[] { ("GET", "state"), ("POST", "spin") },   // later stations append a row
        };

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Action<int>? _adoptSp;

    /// <param name="http">Null = the shared client. Tests pass one over a fake handler.</param>
    /// <param name="identity">Null result = no account (or offline): every request refuses <c>offline</c>.</param>
    /// <param name="adoptSp">Receives every <c>body.sp</c> the server answers with.</param>
    public BackRoomApi(HttpClient? http, Func<(string UnifiedId, string Token)?> identity, Action<int>? adoptSp)
    {
        _http = http ?? SharedHttp;
        _identity = identity;
        _adoptSp = adoptSp;
    }

    /// <summary>The account's token door off AppSettings, or null when there is none to use.</summary>
    public static (string UnifiedId, string Token)? AppIdentity()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null || s.OfflineMode) return null;
            if (string.IsNullOrWhiteSpace(s.UnifiedId) || string.IsNullOrWhiteSpace(s.AuthToken)) return null;
            return (s.UnifiedId, s.AuthToken);
        }
        catch { return null; }
    }

    /// <summary>Whitelist lookup. Exact, case-sensitive match on both station and op.</summary>
    public static bool TryResolve(string? station, string? op, out string method, out string path)
    {
        method = path = string.Empty;
        if (string.IsNullOrEmpty(station) || string.IsNullOrEmpty(op)) return false;
        if (!Ops.TryGetValue(station, out var rows)) return false;
        foreach (var (m, p) in rows)
        {
            if (!string.Equals(p, op, StringComparison.Ordinal)) continue;
            method = m;
            path = $"/v2/backroom/{station}/{op}";
            return true;
        }
        return false;
    }

    public async Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct = default)
    {
        if (!TryResolve(station, op, out var method, out var path)) return BackRoomStationResult.Refuse("bad_op");
        var id = _identity();
        if (id == null) return BackRoomStationResult.Refuse("offline");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var req = BuildRequest(method, path, id.Value, idem, body);
                using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
                return Read((int)res.StatusCode, res.IsSuccessStatusCode, text);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return BackRoomStationResult.Refuse("timeout");
            }
            catch (OperationCanceledException)
            {
                return BackRoomStationResult.Refuse("closed");
            }
            catch (HttpRequestException ex)
            {
                // Never reached the server (or the socket died under the reply). Once more, same idem.
                App.Logger?.Debug("BackRoom relay {Station}/{Op} network failure (attempt {N}): {E}",
                    station, op, attempt + 1, ex.Message);
            }
        }
        return BackRoomStationResult.Refuse("offline");
    }

    private static HttpRequestMessage BuildRequest(string method, string path, (string UnifiedId, string Token) id,
        string? idem, JObject? body)
    {
        HttpRequestMessage req;
        if (method == "GET")
        {
            req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}?unified_id={Uri.EscapeDataString(id.UnifiedId)}");
        }
        else
        {
            // The page's body, then the fields only the host may write. A page cannot name another
            // account: unified_id is stamped last and overwrites whatever the body carried.
            var o = body != null ? (JObject)body.DeepClone() : new JObject();
            if (!string.IsNullOrEmpty(idem)) o["idem"] = idem;
            o["unified_id"] = id.UnifiedId;
            req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
        }
        req.Headers.Add("X-Auth-Token", id.Token);
        return req;
    }

    private BackRoomStationResult Read(int status, bool success, string text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject(text) as JObject; } catch { }
        if (o == null) return new BackRoomStationResult(false, status, "bad_reply", null);

        if (o["sp"] is JValue { Type: JTokenType.Integer } sp)
        {
            try { _adoptSp?.Invoke(sp.Value<int>()); }
            catch (Exception ex) { App.Logger?.Debug("BackRoom adopt sp failed: {E}", ex.Message); }
        }
        bool ok = success && o.Value<bool?>("ok") == true;
        var reason = o.Value<string?>("reason");
        if (!ok && string.IsNullOrEmpty(reason)) reason = success ? "refused" : "http_" + status;
        return new BackRoomStationResult(ok, status, ok ? null : reason, o);
    }
}
