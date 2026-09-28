using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame;

/// <summary>One listed Goon Game table, as <c>POST /v2/goon/open</c> hands it over. No free text
/// from the host ever rides here: the name is the server's sanitised display name, the song is a
/// bool, never a title.</summary>
public sealed record OpenTable(
    string Code,
    string Name,
    int Level,
    string? Avatar,
    bool Friend,
    string? FriendId,
    bool Song,
    int CardSec,
    bool Pictures,
    int WaitingSec);

/// <summary>What <c>/open</c> answered. <see cref="Empty"/> stands in for "nothing to show":
/// a proxy without the route yet (404), a signed-out app, a network fault.</summary>
public sealed record OpenTablesReply(bool CanHost, bool CanJoin, IReadOnlyList<OpenTable> Tables, int? LastOpenedAgoSec)
{
    public static readonly OpenTablesReply Empty = new(false, false, Array.Empty<OpenTable>(), null);
}

/// <summary>A Goon Game room code as the page's <c>normalizeCode</c> puts it on the wire: upper
/// case, no spaces or hyphens, letters and digits only, 4 to 12 long. Null when it cannot be one.</summary>
public static class GoonJoinCode
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new StringBuilder(input.Length);
        foreach (var raw in input.Trim())
        {
            if (raw == '-' || raw == ' ') continue;
            var c = char.ToUpperInvariant(raw);
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return null;
            sb.Append(c);
        }
        return sb.Length is >= 4 and <= 12 ? sb.ToString() : null;
    }
}

/// <summary>
/// THE OPEN TABLES WIRE. One op, <c>POST /v2/goon/open</c> with <c>{ unified_id }</c> and the
/// account's <c>X-Auth-Token</c> (the same pair <see cref="BackRoomApi.AppIdentity"/> stamps and
/// the goon page's own /v2/goon/* calls carry). Never throws. A 404 (the route is not deployed
/// yet) and every other fault read as <see cref="OpenTablesReply.Empty"/>, so no caller draws an
/// error for a server that simply has not grown the feature.
/// </summary>
public sealed class GoonOpenTablesApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>The page lists at most this many; the desktop never draws more either.</summary>
    public const int MaxTables = 20;

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public GoonOpenTablesApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<OpenTablesReply> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null) return OpenTablesReply.Empty;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            var body = new JObject { ["unified_id"] = id.Value.UnifiedId };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/goon/open")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return OpenTablesReply.Empty;
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GoonOpenTables: fetch failed: {E}", ex.Message);
            return OpenTablesReply.Empty;
        }
    }

    /// <summary>Pure: the reply body to a model. Anything that is not an <c>ok</c> object is
    /// <see cref="OpenTablesReply.Empty"/>; a row without a usable code is dropped.</summary>
    internal static OpenTablesReply Parse(string? text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text ?? "", ReadSettings) as JObject; } catch { }
        if (o == null || o.Value<bool?>("ok") != true) return OpenTablesReply.Empty;

        bool canHost = false, canJoin = false;
        if (o["you"] is JObject you)
        {
            canHost = you["canHost"]?.Type == JTokenType.Boolean && you.Value<bool>("canHost");
            canJoin = you["canJoin"]?.Type == JTokenType.Boolean && you.Value<bool>("canJoin");
        }

        var tables = new List<OpenTable>();
        if (o["tables"] is JArray arr)
            foreach (var t in arr)
            {
                if (tables.Count >= MaxTables) break;
                if (t is not JObject r) continue;
                var code = GoonJoinCode.Normalize(Str(r["code"]));
                if (code == null) continue;
                bool friend = Bool(r["friend"]);
                tables.Add(new OpenTable(
                    code,
                    Str(r["name"]) ?? "",
                    Math.Max(0, Int(r["level"]) ?? 0),
                    Str(r["avatar"]),
                    friend,
                    friend ? Str(r["friendId"]) : null,
                    Bool(r["song"]),
                    Math.Max(0, Int(r["cardSec"]) ?? 0),
                    Bool(r["pictures"]),
                    Math.Max(0, Int(r["waitingSec"]) ?? 0)));
            }

        int? ago = Int(o["lastOpenedAgoSec"]);
        if (ago < 0) ago = null;
        return new OpenTablesReply(canHost, canJoin, tables, ago);
    }

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;
    private static bool Bool(JToken? t) => t?.Type == JTokenType.Boolean && t.Value<bool>();
    private static int? Int(JToken? t) => t?.Type switch
    {
        JTokenType.Integer => (int)Math.Clamp(t.Value<long>(), int.MinValue, int.MaxValue),
        JTokenType.Float => (int)Math.Round(Math.Clamp(t.Value<double>(), int.MinValue, int.MaxValue)),
        _ => null,
    };
}

/// <summary>
/// The desktop's one shared view of the open tables, so the friends drawer and the launcher tile
/// read the same answer and never ask twice in a row. Callers poll it on their own visible-only
/// clocks; <see cref="RefreshAsync"/> coalesces calls that land within <see cref="MinGap"/> of the
/// last one and hands back the cached reply instead.
/// </summary>
public static class GoonOpenTables
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static Task<OpenTablesReply>? _inflight;
    private static DateTime _lastUtc = DateTime.MinValue;

    /// <summary>Test seam and the real wire.</summary>
    internal static Func<CancellationToken, Task<OpenTablesReply>> Fetch { get; set; } =
        ct => new GoonOpenTablesApi().FetchAsync(ct);

    internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public static OpenTablesReply Latest { get; private set; } = OpenTablesReply.Empty;

    /// <summary>Raised (on whatever thread the fetch finished) when <see cref="Latest"/> moves.</summary>
    public static event Action<OpenTablesReply>? Changed;

    public static Task<OpenTablesReply> RefreshAsync(CancellationToken ct = default)
    {
        lock (Gate)
        {
            if (_inflight != null) return _inflight;
            if (UtcNow() - _lastUtc < MinGap) return Task.FromResult(Latest);
            _lastUtc = UtcNow();
            // A fetch that finishes synchronously clears _inflight inside RunAsync before this
            // line runs (the lock is re-entrant), so only keep a task that is still going.
            var run = RunAsync(ct);
            _inflight = run.IsCompleted ? null : run;
            return run;
        }
    }

    private static async Task<OpenTablesReply> RunAsync(CancellationToken ct)
    {
        OpenTablesReply reply;
        try { reply = await Fetch(ct).ConfigureAwait(false); }
        catch { reply = OpenTablesReply.Empty; }
        lock (Gate) { _inflight = null; }
        Latest = reply;
        try { Changed?.Invoke(reply); } catch (Exception ex) { App.Logger?.Debug("GoonOpenTables.Changed: {E}", ex.Message); }
        return reply;
    }

    /// <summary>The table a friend is hosting, if any. Pins by the server's <c>friendId</c>; falls
    /// back to an exact name match only when the server sent no id at all.</summary>
    public static OpenTable? ForFriend(OpenTablesReply reply, string friendId, string friendName)
    {
        OpenTable? byName = null;
        int nameHits = 0;
        foreach (var t in reply.Tables)
        {
            if (!t.Friend) continue;
            if (t.FriendId != null)
            {
                if (t.FriendId == friendId) return t;
                continue;
            }
            if (!string.IsNullOrEmpty(friendName) && string.Equals(t.Name, friendName, StringComparison.Ordinal))
            {
                byName = t;
                nameHits++;
            }
        }
        return nameHits == 1 ? byName : null;
    }

    /// <summary>The launcher badge's number: every table the caller could see, 0 when none.</summary>
    public static int OpenCount(OpenTablesReply reply) => reply.Tables.Count;

    internal static void ResetForTests()
    {
        lock (Gate) { _inflight = null; _lastUtc = DateTime.MinValue; }
        Latest = OpenTablesReply.Empty;
        Changed = null;
    }
}
