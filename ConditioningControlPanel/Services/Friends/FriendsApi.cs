using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>What one <c>poll</c> came back with: who is online, and the drained inbox.</summary>
public sealed record FriendsPollReply(IReadOnlyList<string> Online, IReadOnlyList<InboxItem> Inbox);

/// <summary>The wire as the service sees it. <see cref="FriendsApi"/> is the only real one; tests hand in a fake.
/// Nothing here throws: a fault is a null reply, <see cref="SendResult.TryLater"/> or false.</summary>
public interface IFriendsApi
{
    Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default);

    /// <summary><paramref name="activity"/> and <paramref name="lockDay"/> are sent only when
    /// <paramref name="shared"/> is true; the caller already decided that.</summary>
    Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default);

    Task<AddResult> RequestAsync(string code, CancellationToken ct = default);

    Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
        CancellationToken ct = default);

    /// <summary>accept, decline, cancel, remove, block, unblock, squelch, report. True only on <c>ok</c>.</summary>
    Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default);
}

/// <summary>
/// THE FRIENDS WIRE (CONTRACT.md beside this file). Every op is <c>POST /v2/friends/&lt;op&gt;</c>
/// with the account's token door, the same pair <see cref="BackRoomApi.AppIdentity"/> stamps.
/// Every reply is worded into the shared enums: a gateway HTML page, a 404 from a proxy that has
/// no friends routes yet, a timeout and a dead socket all read <c>TryLater</c>, never a throw.
/// Nothing is retried: a send is not idempotent.
/// </summary>
public sealed class FriendsApi : IFriendsApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    /// <param name="http">Null = the shared client.</param>
    /// <param name="identity">Null = <see cref="BackRoomApi.AppIdentity"/>.</param>
    /// <param name="baseUrl">Null = <see cref="BackRoomApi.BaseUrl"/> (the same proxy, same DEBUG override).</param>
    public FriendsApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    // ---- wire strings ----

    internal static string? ActivityToWire(PresenceActivity a) => a switch
    {
        PresenceActivity.Panel => "panel",
        PresenceActivity.Session => "session",
        PresenceActivity.BackRoom => "backroom",
        PresenceActivity.Race => "race",
        PresenceActivity.Goon => "goon",
        PresenceActivity.GoonHosting => "goon_hosting",
        PresenceActivity.Arcademy => "arcademy",
        PresenceActivity.Breakout => "breakout",
        PresenceActivity.Deeper => "deeper",
        PresenceActivity.Remote => "remote",
        _ => null,
    };

    /// <summary>"online" (a friend who shares nothing beyond being here) reads as the panel.</summary>
    internal static PresenceActivity ActivityFromWire(string? s) => s switch
    {
        "panel" or "online" => PresenceActivity.Panel,
        "session" => PresenceActivity.Session,
        "backroom" => PresenceActivity.BackRoom,
        "race" => PresenceActivity.Race,
        "goon" => PresenceActivity.Goon,
        "goon_hosting" => PresenceActivity.GoonHosting,
        "arcademy" => PresenceActivity.Arcademy,
        "breakout" => PresenceActivity.Breakout,
        "deeper" => PresenceActivity.Deeper,
        "remote" => PresenceActivity.Remote,
        _ => PresenceActivity.Offline,
    };

    internal static string KindToWire(SendKind k) => k switch
    {
        SendKind.Poke => "poke",
        SendKind.Invite => "invite",
        _ => "watch",
    };

    internal static string WatchKindToWire(WatchKind k) => k switch
    {
        WatchKind.Catalogue => "catalogue",
        WatchKind.Flavour => "flavour",
        _ => "ht",
    };

    internal static SendResult SendFromWire(string? status) => status switch
    {
        "sent" => SendResult.Sent,
        "squelched" => SendResult.Squelched,
        "too_fast" => SendResult.TooFast,
        "not_friends" => SendResult.NotFriends,
        "offline" => SendResult.Offline,
        "refused" or "bad_input" => SendResult.Refused,
        _ => SendResult.TryLater,
    };

    internal static AddResult AddFromWire(string? status) => status switch
    {
        "sent" => AddResult.Sent,
        "accepted" => AddResult.Accepted,
        "already" => AddResult.Already,
        "not_found" or "bad_input" => AddResult.NotFound,
        "blocked" => AddResult.Blocked,
        "self" => AddResult.Self,
        "full" => AddResult.Full,
        _ => AddResult.TryLater,
    };

    /// <summary>A friend code as the server wants it: <c>CCP-</c> plus five from the code alphabet,
    /// upper case. Accepts the bare five, any case, stray spaces. Null when it cannot be one.</summary>
    public static string? NormaliseCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().Replace(" ", "").ToUpperInvariant();
        if (s.StartsWith("CCP-", StringComparison.Ordinal)) s = s.Substring(4);
        else if (s.StartsWith("CCP", StringComparison.Ordinal) && s.Length == 8) s = s.Substring(3);
        if (s.Length != 5) return null;
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        foreach (var c in s) if (alphabet.IndexOf(c) < 0) return null;
        return "CCP-" + s;
    }

    // ---- ops ----

    public async Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default)
    {
        var o = await CallAsync("state", null, ct);
        return o != null && Ok(o) ? ParseState(o) : null;
    }

    public async Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default)
    {
        var body = new JObject { ["shared"] = shared };
        if (shared)
        {
            var wire = activity is { } a ? ActivityToWire(a) : null;
            if (wire != null) body["activity"] = wire;
            if (lockDay is { } d) body["lock_day"] = d;
        }
        var o = await CallAsync("poll", body, ct);
        return o != null && Ok(o) ? ParsePoll(o) : null;
    }

    public async Task<AddResult> RequestAsync(string code, CancellationToken ct = default)
    {
        var o = await CallAsync("request", new JObject { ["code"] = code }, ct);
        if (o == null) return AddResult.TryLater;
        return Ok(o) ? AddFromWire(o.Value<string?>("status")) : AddFromWire(o.Value<string?>("reason"));
    }

    public async Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code,
        WatchRef? watch, CancellationToken ct = default)
    {
        var body = new JObject { ["to"] = to, ["kind"] = KindToWire(kind) };
        if (poke != null) body["poke"] = poke;
        if (destination != null) body["destination"] = destination;
        if (code != null) body["code"] = code;
        if (watch != null)
        {
            var w = new JObject { ["kind"] = WatchKindToWire(watch.Kind), ["id"] = watch.Id };
            if (!string.IsNullOrEmpty(watch.Title)) w["title"] = watch.Title;
            body["watch"] = w;
        }
        var o = await CallAsync("send", body, ct);
        if (o == null) return SendResult.TryLater;
        return Ok(o) ? SendFromWire(o.Value<string?>("status")) : SendFromWire(o.Value<string?>("reason"));
    }

    public async Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default)
    {
        var body = extra != null ? (JObject)extra.DeepClone() : new JObject();
        body["id"] = id;
        var o = await CallAsync(op, body, ct);
        return o != null && Ok(o);
    }

    private static bool Ok(JObject o) => o.Value<bool?>("ok") == true;

    /// <summary>One POST. Returns the parsed JSON object, or null for anything that is not one
    /// (no account, network fault, timeout, an HTML page). A 429 with no body is worded
    /// <c>too_fast</c> so a send reads right.</summary>
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
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/friends/{op}")
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            try { MergedAccountRecovery.TryHandle((int)res.StatusCode, text); } catch { }
            return Read((int)res.StatusCode, text);
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException ex)
        {
            App.Logger?.Debug("Friends {Op} network failure: {E}", op, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Friends {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }

    internal static JObject? Read(int status, string text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text, ReadSettings) as JObject; } catch { }
        if (o == null)
            return status == 429 ? new JObject { ["ok"] = false, ["reason"] = "too_fast" } : null;
        if (status < 200 || status >= 300)
        {
            // A worded refusal keeps its reason; anything else out of a non-2xx is not ours to read.
            if (status == 429) return new JObject { ["ok"] = false, ["reason"] = "too_fast" };
            if (o.Value<bool?>("ok") == false && !string.IsNullOrEmpty(o.Value<string?>("reason")) && status != 401 && status < 500)
                return o;
            return null;
        }
        return o;
    }

    // ---- parse ----

    internal static DateTimeOffset ParseTime(JToken? t, DateTimeOffset fallback)
    {
        var s = t?.Type == JTokenType.String ? (string?)t : null;
        return s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var v)
            ? v : fallback;
    }

    private static int? IntOrNull(JToken? t) => t?.Type == JTokenType.Integer ? t.Value<int>() : null;

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    internal static FriendsSnapshot ParseState(JObject o)
    {
        var friends = new List<Friend>();
        if (o["friends"] is JArray fa)
            foreach (var f in fa)
            {
                if (f is not JObject fo || Str(fo["id"]) is not { Length: > 0 } fid) continue;
                bool online = fo.Value<bool?>("online") == true;
                var presence = new FriendPresence(
                    online ? ActivityFromWire(Str(fo["activity"])) : PresenceActivity.Offline,
                    IntOrNull(fo["lock_day"]),
                    ParseTime(fo["last_seen"], DateTimeOffset.MinValue));
                friends.Add(new Friend(fid, Str(fo["name"]) ?? "", Str(fo["avatar"]),
                    Math.Clamp(IntOrNull(fo["tier"]) ?? 0, 0, 2), online, presence, fo.Value<bool?>("squelched") == true));
            }

        var me = FriendPresence.None;
        if (o["me"] is JObject mo)
            me = new FriendPresence(ActivityFromWire(Str(mo["activity"])), IntOrNull(mo["lock_day"]), DateTimeOffset.MinValue);

        return new FriendsSnapshot(friends, ParseRequests(o["incoming"]), ParseRequests(o["outgoing"]), Str(o["code"]) ?? "", me);
    }

    private static List<FriendRequest> ParseRequests(JToken? t)
    {
        var list = new List<FriendRequest>();
        if (t is not JArray a) return list;
        foreach (var r in a)
        {
            if (r is not JObject ro || Str(ro["id"]) is not { Length: > 0 } rid) continue;
            list.Add(new FriendRequest(rid, Str(ro["name"]) ?? "", Str(ro["avatar"]), Str(ro["via"]),
                ParseTime(ro["at"], DateTimeOffset.MinValue)));
        }
        return list;
    }

    internal static FriendsPollReply ParsePoll(JObject o)
    {
        var online = new List<string>();
        if (o["online"] is JArray oa)
            foreach (var t in oa) if (Str(t) is { Length: > 0 } s) online.Add(s);
        var inbox = new List<InboxItem>();
        if (o["inbox"] is JArray ia)
            foreach (var t in ia)
                if (t is JObject io && ParseItem(io) is { } item) inbox.Add(item);
        return new FriendsPollReply(online, inbox);
    }

    /// <summary>One inbox item, or null when it does not fit the grammar (the server should never
    /// send one; the client refuses it anyway, because the landing side acts on these).</summary>
    internal static InboxItem? ParseItem(JObject o)
    {
        var id = Str(o["id"]);
        var from = Str(o["from"]);
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(from)) return null;
        var at = ParseTime(o["at"], DateTimeOffset.MinValue);
        if (at == DateTimeOffset.MinValue) return null;

        SendKind kind;
        string? poke = null, destination = null, code = null;
        WatchRef? watch = null;
        TimeSpan life;
        switch (Str(o["kind"]))
        {
            case "poke":
                kind = SendKind.Poke;
                poke = Str(o["poke"]);
                if (!PokeSet.IsValid(poke)) return null;
                life = TimeSpan.FromHours(24);
                break;
            case "invite":
                kind = SendKind.Invite;
                destination = Str(o["destination"]);
                if (!InviteDestination.IsValid(destination)) return null;
                code = Str(o["code"]);
                if (code != null && !IsJoinCode(code)) return null;
                life = TimeSpan.FromSeconds(InviteDestination.LifetimeSeconds);
                break;
            case "watch":
                kind = SendKind.Watch;
                if (o["watch"] is not JObject wo) return null;
                WatchKind? wk = Str(wo["kind"]) switch
                {
                    "catalogue" => WatchKind.Catalogue,
                    "flavour" => WatchKind.Flavour,
                    "ht" => WatchKind.Ht,
                    _ => null,
                };
                if (wk == null) return null;
                watch = new WatchRef(wk.Value, Str(wo["id"]) ?? "", Str(wo["title"]));
                if (!watch.IsValid()) return null;
                life = TimeSpan.FromHours(24);
                break;
            default:
                return null;
        }
        var expires = ParseTime(o["expires_at"], at + life);
        return new InboxItem(id, kind, from, Str(o["from_name"]) ?? "", Str(o["from_avatar"]), poke, destination, code, watch,
            at, expires);
    }

    internal static bool IsJoinCode(string s)
    {
        if (s.Length < 4 || s.Length > 12) return false;
        foreach (var c in s)
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')) return false;
        return true;
    }
}
