// The Lobby's account door and proxy, plus the Remote directory read it merges. WPF 7.1.5 reads
// the door from the head-only BackRoomApi and the directory through AvailableSubjectsService (which
// also owns the claim flow and an ObservableCollection on the UI thread). Core keeps only what the
// Lobby needs: one GET of /v2/directory/list, parsed to RemoteSeat rows, never throwing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// Seeded by the head at startup (WPF: BackRoomApi.AppIdentity / BaseUrl). Unseeded: signed out
/// and no url, so every Lobby wire reads as empty and nothing is sent.
/// </summary>
public static class LobbyWire
{
    public static Func<(string UnifiedId, string Token)?> DefaultIdentity { get; set; } = () => null;
    public static Func<string?> DefaultBaseUrl { get; set; } = () => null;
}

/// <summary>
/// <c>GET /v2/directory/list</c> with <c>X-Auth-Token</c> + <c>X-Caller-Unified-Id</c> (the WPF
/// AvailableSubjectsService.RefreshAsync request), reduced to <see cref="RemoteSeat"/> rows.
/// Never throws; Ok is false on any fault so the Lobby can say it could not reach the server.
/// </summary>
public sealed class RemoteDirectoryApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public RemoteDirectoryApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? LobbyWire.DefaultIdentity;
        _baseUrl = baseUrl ?? LobbyWire.DefaultBaseUrl() ?? "";
    }

    public async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0) return (Array.Empty<RemoteSeat>(), false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v2/directory/list");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            req.Headers.Add("X-Caller-Unified-Id", id.Value.UnifiedId);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Log.Debug("[Lobby] directory list answered {Status}", (int)res.StatusCode);
                return (Array.Empty<RemoteSeat>(), false);
            }
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception ex)
        {
            Log.Debug("[Lobby] directory list failed: {E}", ex.GetType().Name);
            return (Array.Empty<RemoteSeat>(), false);
        }
    }

    /// <summary>WPF AvailableSubjectsService.TryClaimAsync: <c>POST /v2/directory/claim</c> for one listed
    /// subject. The session url on 200 (it carries the PIN in its fragment: the caller opens it once and
    /// never logs or stores it); null on a 409 (someone claimed first, <paramref name="lostRace"/> set so the
    /// caller re-reads the list), on any other refusal (status only is logged, never the body) and on a fault.
    /// Only an absolute http(s) url without quotes or control characters is handed back.</summary>
    public async Task<(string? Url, bool LostRace)> ClaimAsync(string subjectUnifiedId, CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0)
        {
            Log.Warning("[AvailableSubjects] claim called without auth state");
            return (null, false);
        }
        if (string.IsNullOrEmpty(subjectUnifiedId)) return (null, false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/directory/claim")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { unified_id = subjectUnifiedId }), System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            req.Headers.Add("X-Caller-Unified-Id", id.Value.UnifiedId);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if ((int)res.StatusCode == 409) return (null, true);
            if (!res.IsSuccessStatusCode)
            {
                Log.Warning("[AvailableSubjects] claim failed: {Status}", (int)res.StatusCode);
                return (null, false);
            }
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            var url = SafeSessionUrl(JsonConvert.DeserializeObject<JObject>(text, ReadSettings)?["session_url"]?.ToString());
            if (url == null) Log.Warning("[AvailableSubjects] claim 200 without a usable session_url");
            return (url, false);
        }
        catch (Exception ex)
        {
            Log.Warning("[AvailableSubjects] claim error: {E}", ex.GetType().Name);
            return (null, false);
        }
    }

    /// <summary>Pure. The url as the browser gets it, or null when it is not a plain web address.</summary>
    internal static string? SafeSessionUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        foreach (var c in url) if (c == '"' || c == '\'' || char.IsControl(c)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ? uri.AbsoluteUri : null;
    }

    /// <summary>Pure. <c>entries</c> must be an array; an entry without a unified id is dropped.
    /// Defaults match WPF ParseEntry (Anonymous, level 1, tier light).</summary>
    internal static (IReadOnlyList<RemoteSeat> Seats, bool Ok) Parse(string? text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text ?? "", ReadSettings) as JObject; } catch { }
        if (o == null || o["entries"] is not JArray arr) return (Array.Empty<RemoteSeat>(), false);
        var seats = new List<RemoteSeat>();
        foreach (var t in arr)
        {
            if (t is not JObject e) continue;
            var uid = e["unified_id"]?.Type == JTokenType.String ? (string?)e["unified_id"] : null;
            if (string.IsNullOrEmpty(uid)) continue;
            var tags = e["tags"] is JArray tagArr
                ? tagArr.Where(x => x.Type == JTokenType.String).Select(x => (string)x!).Where(s => s.Length > 0).ToList()
                : new List<string>();
            int level = e["level"]?.Type == JTokenType.Integer ? (int)Math.Clamp(e.Value<long>("level"), 0, int.MaxValue) : 1;
            seats.Add(new RemoteSeat(
                uid!,
                e["display_name"]?.Type == JTokenType.String ? (string)e["display_name"]! : "Anonymous",
                level,
                e["tier"]?.Type == JTokenType.String ? (string)e["tier"]! : "light",
                tags,
                e["claimed"]?.Type == JTokenType.Boolean && e.Value<bool>("claimed")));
        }
        return (seats, true);
    }
}
