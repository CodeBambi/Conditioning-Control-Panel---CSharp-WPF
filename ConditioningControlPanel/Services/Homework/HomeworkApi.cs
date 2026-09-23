using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Homework;

/// <summary>
/// One reply. <see cref="Ok"/> false with reason <c>offline</c> means the server was not reached
/// (network, non-2xx, unreadable body; a 409 <c>merged</c> is in here too). Any other reason is
/// the proxy refusing on purpose (<c>busy</c>, <c>not_watched</c>, <c>wrong_video</c> and so on),
/// and a refusal still carries the today payload when the proxy sent one.
/// </summary>
public sealed record HomeworkReply(bool Ok, string Reason, HomeworkToday? Today)
{
    public const string Offline = "offline";
    public static readonly HomeworkReply Unreachable = new(false, Offline, null);

    /// <summary>Worth asking again: nobody answered, or the proxy was busy with this account.</summary>
    public bool Transient => !Ok && (Reason == Offline || Reason == "busy");
}

/// <summary>
/// The three homework routes on the proxy. Same token door as the Back Room relay
/// (<see cref="BackRoomApi.AppIdentity"/>: <c>unified_id</c> plus <c>X-Auth-Token</c>), same base url.
/// Fails quiet: nothing here throws, and a reply that is not <c>ok:true</c> leaves the feature
/// idle. Homework is opt-in flavour, so a proxy outage must never put a card in front of anyone.
/// </summary>
public sealed class HomeworkApi
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;

    /// <param name="http">Null = the shared client. Tests pass one over a fake handler.</param>
    /// <param name="identity">Null result = no account or offline mode: nothing is sent.</param>
    public HomeworkApi(HttpClient? http, Func<(string UnifiedId, string Token)?> identity)
    {
        _http = http ?? SharedHttp;
        _identity = identity;
    }

    public Task<HomeworkReply> TodayAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, "today", null, ct);

    public Task<HomeworkReply> SetOptInAsync(bool on, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "optin", new JObject { ["on"] = on }, ct);

    public Task<HomeworkReply> WatchedAsync(string day, string url, double watchedSeconds, double durationSeconds,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "watched", new JObject
        {
            ["day"] = day,
            ["url"] = url,
            ["watchedSeconds"] = Math.Round(watchedSeconds, 1),
            ["durationSeconds"] = Math.Round(durationSeconds, 1),
        }, ct);

    private async Task<HomeworkReply> SendAsync(HttpMethod method, string op, JObject? body, CancellationToken ct)
    {
        var id = _identity();
        if (id == null) return HomeworkReply.Unreachable;
        try
        {
            var path = $"{BackRoomApi.BaseUrl}/v2/homework/{op}";
            using var req = method == HttpMethod.Get
                ? new HttpRequestMessage(method, $"{path}?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}")
                : new HttpRequestMessage(method, path);
            if (body != null)
            {
                body["unified_id"] = id.Value.UnifiedId;   // stamped last: the caller cannot name another account
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            }
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return HomeworkReply.Unreachable;
            return Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Homework {Op} failed quietly: {E}", op, ex.Message);
            return HomeworkReply.Unreachable;
        }
    }

    /// <summary>
    /// Reads a reply. Anything that is not a JSON object with an <c>ok</c> flag is
    /// <see cref="HomeworkReply.Unreachable"/>. The today payload is read whenever it is there
    /// (<c>enabled</c> present): <c>enabled:false</c> is <see cref="HomeworkToday.Idle"/> whatever
    /// else it carries, and a <c>current</c> without a day, an https url and a title is no homework.
    /// </summary>
    internal static HomeworkReply Parse(string? text)
    {
        JObject? o;
        try { o = JsonConvert.DeserializeObject(text ?? "") as JObject; }
        catch { return HomeworkReply.Unreachable; }
        if (o?["ok"] is not JValue { Type: JTokenType.Boolean } okFlag) return HomeworkReply.Unreachable;
        var ok = okFlag.Value<bool>();
        var reason = ok ? "" : o.Value<string?>("reason") is { Length: > 0 } r ? r : HomeworkReply.Offline;
        return new HomeworkReply(ok, reason, ReadToday(o));
    }

    private static HomeworkToday? ReadToday(JObject o)
    {
        if (o["enabled"] is not JValue { Type: JTokenType.Boolean } enabled) return null;
        if (!enabled.Value<bool>()) return HomeworkToday.Idle;

        HomeworkCurrent? current = null;
        if (o["current"] is JObject c)
        {
            var day = c.Value<string?>("day");
            var url = c.Value<string?>("url");
            var title = c.Value<string?>("title");
            if (!string.IsNullOrWhiteSpace(day) && !string.IsNullOrWhiteSpace(title)
                && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                current = new HomeworkCurrent(day!, url!, title!);
        }
        return new HomeworkToday(true, o.Value<bool?>("optedIn") == true, o.Value<bool?>("discordLinked") == true,
            current, current != null && o.Value<bool?>("done") == true);
    }
}
