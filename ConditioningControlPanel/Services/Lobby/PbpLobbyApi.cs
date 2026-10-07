using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// The chess open tables, read the way the chess page's <c>net/lobbyServer.js</c> browses them:
/// <c>GET /v2/pbp/lobby?unified_id=</c> with the account token. BROWSING IS NOT SITTING: this
/// never posts <c>/lobby/enter</c>, so a player looking at the Lobby is not listed. Never throws;
/// any fault reads as <see cref="PbpLobbyReply.Empty"/> (Ok false).
/// </summary>
public sealed class PbpLobbyApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    public const int MaxRows = 30;

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public PbpLobbyApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<PbpLobbyReply> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null) return PbpLobbyReply.Empty;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            var url = $"{_baseUrl}/v2/pbp/lobby?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return PbpLobbyReply.Empty;
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PbpLobby: fetch failed: {E}", ex.Message);
            return PbpLobbyReply.Empty;
        }
    }

    /// <summary>Pure. <c>players</c> must be an array or the answer is not a lobby; a row
    /// marked <c>self</c> or without an id is dropped (the Lobby lists other people).</summary>
    internal static PbpLobbyReply Parse(string? text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text ?? "", ReadSettings) as JObject; } catch { }
        if (o == null || o["players"] is not JArray players) return PbpLobbyReply.Empty;

        var open = new List<PbpOpenSeat>();
        foreach (var t in players)
        {
            if (open.Count >= MaxRows) break;
            if (t is not JObject p) continue;
            if (p["self"]?.Type == JTokenType.Boolean && p.Value<bool>("self")) continue;
            var pid = Str(p["id"]);
            if (string.IsNullOrEmpty(pid)) continue;
            var (init, inc) = Tc(p["time_control"]);
            open.Add(new PbpOpenSeat(pid, Name(p), init, inc, Long(p["since_ms"])));
        }

        var playing = new List<PbpPlayingGame>();
        if (o["playing"] is JArray arr)
            foreach (var t in arr)
            {
                if (playing.Count >= MaxRows) break;
                if (t is not JObject g) continue;
                var (init, inc) = Tc(g["time_control"]);
                playing.Add(new PbpPlayingGame(Seat(g["white"]), Seat(g["black"]), init, inc,
                    Long(g["started_ms"]), (int)Math.Max(0, Long(g["moves"]))));
            }
        return new PbpLobbyReply(open, playing, true);
    }

    private static string Name(JObject p) => Str(p["display_name"]) ?? Str(p["name"]) ?? "someone";

    private static string Seat(JToken? t) => t switch
    {
        JObject o => Name(o),
        _ when Str(t) is { Length: > 0 } s => s,
        _ => "someone",
    };

    private static (int, int) Tc(JToken? t)
    {
        if (t is not JObject tc) return (0, 0);
        return ((int)Math.Clamp(Long(tc["initial_ms"]), 0, int.MaxValue), (int)Math.Clamp(Long(tc["increment_ms"]), 0, int.MaxValue));
    }

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    private static long Long(JToken? t) => t?.Type switch
    {
        JTokenType.Integer => t.Value<long>(),
        JTokenType.Float => (long)t.Value<double>(),
        _ => 0,
    };
}
