using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>What the broker hands back. <see cref="RefreshExpiresIn"/> 0 means an offline token:
/// it does not idle out, which is what lets the tab settle once a day without a new consent.</summary>
public sealed class ChasterTokens
{
    [JsonProperty("access_token")] public string AccessToken { get; set; } = "";
    [JsonProperty("refresh_token")] public string? RefreshToken { get; set; }
    [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
    [JsonProperty("refresh_expires_in")] public int RefreshExpiresIn { get; set; }
    [JsonProperty("scope")] public string Scope { get; set; } = "";
}

/// <summary>The fields of Chaster's LockForWearer that CCP reads. Everything else is ignored.</summary>
public sealed class ChasterLock
{
    [JsonProperty("_id")] public string Id { get; set; } = "";
    [JsonProperty("title")] public string? Title { get; set; }
    [JsonProperty("status")] public string? Status { get; set; }
    [JsonProperty("role")] public string? Role { get; set; }
    [JsonProperty("startDate")] public DateTime? StartDate { get; set; }
    [JsonProperty("endDate")] public DateTime? EndDate { get; set; }
    [JsonProperty("isFrozen")] public bool IsFrozen { get; set; }
    [JsonProperty("displayRemainingTime")] public bool DisplayRemainingTime { get; set; } = true;
    [JsonProperty("isAllowedToViewTime")] public bool IsAllowedToViewTime { get; set; } = true;
    [JsonProperty("isTestLock")] public bool IsTestLock { get; set; }

    /// <summary>The keyholder hid the timer. CCP then never shows an end date either, not even
    /// one it could work out from its own pushes.</summary>
    [JsonIgnore] public bool TimerHidden => !DisplayRemainingTime || !IsAllowedToViewTime || EndDate == null;
}

/// <summary>Who the linked account is, from GET /auth/profile. Only the two fields the account
/// strip shows are kept; the profile carries email, birth date and more, and none of it is read.</summary>
public sealed record ChasterProfile(string Username, Uri? Avatar);

public enum ChasterStatus
{
    Ok,
    /// <summary>The grant is dead (revoked on Chaster, or idled out). Ask the player to link again.</summary>
    LinkExpired,
    RateLimited,
    /// <summary>The lock is gone, unlocked, or not this wearer's.</summary>
    NotFound,
    /// <summary>Chaster said no to this call on this lock (403). The link itself is fine.</summary>
    Refused,
    /// <summary>Network, 5xx, a body we cannot read. Try later; never drop the link over it.</summary>
    Unavailable,
    /// <summary>The call went out and no answer came back. For a read that is an outage like
    /// any other; for a write nobody knows whether it landed, so it must not simply go again.</summary>
    TimedOut,
    /// <summary>api.chaster.app refused this access token (401). Not a dead link on its own: the
    /// caller refreshes once and tries again, and only the broker's own "link_expired" on
    /// /chaster/refresh ever drops the link.</summary>
    Unauthorized,
}

public readonly record struct ChasterResult<T>(ChasterStatus Status, T? Value)
{
    public bool Ok => Status == ChasterStatus.Ok;
}

/// <summary>
/// The wire. Two hosts on purpose: tokens come from OUR proxy (it holds the client secret), and
/// every lock call goes STRAIGHT to api.chaster.app with the player's own token. Chaster rate
/// limits 100 a minute per IP, so routing lock calls through one proxy IP would trip it for
/// everybody at once.
///
/// <para>Stateless and handler-injected so the mapping from HTTP to <see cref="ChasterStatus"/>
/// is tested without a network. Never logs a token, a lock title or a username.</para>
/// </summary>
public sealed class ChasterClient : IDisposable
{
    public const string ProxyBase = "https://codebambi-proxy.vercel.app";
    public const string ApiBase = "https://api.chaster.app";

    /// <summary>One add is never more than the day's cap. A caller with a bigger number has a bug.</summary>
    public const int MaxAddSeconds = TabLimits.MaxDailySeconds;

    private readonly HttpClient _http;

    public ChasterClient(HttpMessageHandler? handler = null, string? userAgent = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrEmpty(userAgent)) _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public static string AuthorizeUrl(string state, string challenge) =>
        $"{ProxyBase}/chaster/authorize?state={state}&code_challenge={challenge}";

    /// <summary>16 random bytes as hex: the only state shape the broker accepts.</summary>
    public static string NewState() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>An access token lives 300 s. Refresh when under a minute is left, so a call that
    /// starts now does not die in flight.</summary>
    public static bool NeedsRefresh(DateTime expiresAtUtc, DateTime nowUtc) =>
        expiresAtUtc - nowUtc < TimeSpan.FromSeconds(60);

    /// <summary>PKCE (RFC 7636): 32 random bytes, base64url. Never leaves this machine until the
    /// exchange, so a code read out of browser history or caught by another program is useless.</summary>
    public static string NewVerifier() => Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge the consent page is opened with.</summary>
    public static string Challenge(string verifier) =>
        Base64Url(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Turn the one-time code the loopback caught into tokens. The proxy adds the
    /// secret; Chaster checks the verifier against the challenge the flow started with.</summary>
    public Task<ChasterResult<ChasterTokens>> ExchangeAsync(string code, string verifier, CancellationToken ct = default) =>
        PostProxyAsync("/chaster/token", new { code, code_verifier = verifier }, ct);

    public Task<ChasterResult<ChasterTokens>> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostProxyAsync("/chaster/refresh", new { refresh_token = refreshToken }, ct);

    /// <summary>Best effort. Unlinking forgets the tokens locally whatever this returns.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        try { await PostProxyAsync("/chaster/revoke", new { refresh_token = refreshToken }, ct).ConfigureAwait(false); }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster revoke is best effort"); }
    }

    /// <summary>The wearer's active locks. Keyholder-side locks are filtered out: v1 never acts
    /// on anyone else's lock.</summary>
    public async Task<ChasterResult<IReadOnlyList<ChasterLock>>> GetLocksAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/locks?status=active");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(req, body =>
        {
            var all = JsonConvert.DeserializeObject<List<ChasterLock>>(body) ?? new List<ChasterLock>();
            return (IReadOnlyList<ChasterLock>)all
                .Where(l => !string.IsNullOrEmpty(l.Id) && !string.Equals(l.Role, "keyholder", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }, ct, api: true).ConfigureAwait(false);
    }

    /// <summary>The linked account's name and picture (scope <c>profile</c>). Read-only.</summary>
    public async Task<ChasterResult<ChasterProfile>> GetProfileAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/auth/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var result = await SendAsync(req, ParseProfile, ct, api: true).ConfigureAwait(false);
        // A 200 with no username in it is a body we cannot use: same as an outage, try later.
        return result.Ok && result.Value == null ? new(ChasterStatus.Unavailable, null) : result;
    }

    /// <summary>Username and avatar out of a CurrentUser body. Null when there is no username.
    /// The avatar is kept only when <see cref="SafeAvatarUri"/> accepts it.</summary>
    public static ChasterProfile? ParseProfile(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        Newtonsoft.Json.Linq.JObject obj;
        try { obj = Newtonsoft.Json.Linq.JObject.Parse(json); }
        catch (JsonException) { return null; }
        var name = (obj["username"] as Newtonsoft.Json.Linq.JValue)?.Value as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var avatar = (obj["avatarUrl"] as Newtonsoft.Json.Linq.JValue)?.Value as string;
        return new ChasterProfile(name.Trim(), SafeAvatarUri(avatar));
    }

    /// <summary>A picture is only ever fetched over https from Chaster's own hosts
    /// (chaster.app or a subdomain; today avatars live on api.chaster.app) on the default port.
    /// Anything else is dropped and the strip shows the letter instead.</summary>
    public static Uri? SafeAvatarUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        return host == "chaster.app" || host.EndsWith(".chaster.app", StringComparison.Ordinal) ? uri : null;
    }

    /// <summary>Largest avatar file read. A profile picture is tens of KB; past this it is not one.</summary>
    public const int MaxAvatarBytes = 1024 * 1024;

    /// <summary>The picture's bytes, or null. No token is sent (the file is public), the size is
    /// capped while reading, and a redirect that leaves Chaster's hosts is thrown away.</summary>
    public async Task<byte[]?> GetAvatarBytesAsync(Uri avatar, CancellationToken ct = default)
    {
        if (SafeAvatarUri(avatar.AbsoluteUri) == null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, avatar);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            if (res.RequestMessage?.RequestUri is { } final && SafeAvatarUri(final.AbsoluteUri) == null) return null;
            if (res.Content.Headers.ContentLength > MaxAvatarBytes) return null;
            var type = res.Content.Headers.ContentType?.MediaType;
            if (type != null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxAvatarBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.Length == 0 ? null : buffer.ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException)
        {
            Diag.Swallowed(ex, "chaster avatar fetch, the letter stays");
            return null;
        }
    }

    /// <summary>Add time to a lock. Add only: a wearer token cannot remove, and this method will
    /// not try. <paramref name="seconds"/> outside 1..<see cref="MaxAddSeconds"/> is refused
    /// before anything leaves the machine.</summary>
    public async Task<ChasterResult<bool>> AddTimeAsync(string accessToken, string lockId, int seconds, CancellationToken ct = default)
    {
        if (seconds <= 0 || seconds > MaxAddSeconds) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (string.IsNullOrEmpty(lockId) || !lockId.All(char.IsLetterOrDigit)) throw new ArgumentException("lock id", nameof(lockId));

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/locks/{lockId}/update-time");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(JsonConvert.SerializeObject(new { duration = seconds }), Encoding.UTF8, "application/json");
        return await SendAsync(req, _ => true, ct, api: true, write: true).ConfigureAwait(false);
    }

    private async Task<ChasterResult<ChasterTokens>> PostProxyAsync(string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ProxyBase + path)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"),
        };
        var result = await SendAsync(req, json => JsonConvert.DeserializeObject<ChasterTokens>(json), ct).ConfigureAwait(false);
        // /revoke answers { ok } with no token in it; that is still a success for its caller.
        if (result.Ok && path != "/chaster/revoke" && string.IsNullOrEmpty(result.Value?.AccessToken))
            return new(ChasterStatus.Unavailable, null);
        return result;
    }

    private async Task<ChasterResult<T>> SendAsync<T>(HttpRequestMessage req, Func<string, T?> read, CancellationToken ct,
        bool api = false, bool write = false)
    {
        try
        {
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var status = api ? MapApi(res.StatusCode, write) : Map(res.StatusCode);
            if (status != ChasterStatus.Ok)
            {
                App.Logger?.Debug("[Chaster] {Path} answered {Code}", req.RequestUri?.AbsolutePath.Split('/').ElementAtOrDefault(1), (int)res.StatusCode);
                return new(status, default);
            }
            var body = res.Content == null ? "" : await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new(ChasterStatus.Ok, read(body));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException ex)
        {
            // HttpClient's own timeout: the request may have reached Chaster before we gave up.
            Diag.Swallowed(ex, "chaster call timed out");
            return new(ChasterStatus.TimedOut, default);
        }
        catch (HttpRequestException ex) when (write && !NeverSent(ex))
        {
            // The connection was up: the add may have reached Chaster before it broke.
            Diag.Swallowed(ex, "chaster write broke mid-flight, reads as timed out");
            return new(ChasterStatus.TimedOut, default);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Diag.Swallowed(ex, "chaster call failed, reads as unavailable");
            return new(ChasterStatus.Unavailable, default);
        }
    }

    /// <summary>Only a failure to connect at all (or to find the host) proves a request never left.
    /// Anything later (a reset, a half-read answer) may have landed.</summary>
    public static bool NeverSent(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError;

    /// <summary>An api.chaster.app answer. A 401 is only this access token being refused
    /// (<see cref="ChasterStatus.Unauthorized"/>), never a dead link. For a write, a 502 or a 504
    /// came from a gateway that may already have passed the add on, so it is a doubt
    /// (<see cref="ChasterStatus.TimedOut"/>), never a clean "try again".</summary>
    public static ChasterStatus MapApi(HttpStatusCode code, bool write) => (int)code switch
    {
        401 => ChasterStatus.Unauthorized,
        502 or 504 when write => ChasterStatus.TimedOut,
        _ => Map(code),
    };

    /// <summary>410 is the proxy's "that state is spent": for the caller, the same as a dead link.</summary>
    public static ChasterStatus Map(HttpStatusCode code) => (int)code switch
    {
        >= 200 and < 300 => ChasterStatus.Ok,
        401 or 410 => ChasterStatus.LinkExpired,
        403 => ChasterStatus.Refused,
        404 => ChasterStatus.NotFound,
        429 => ChasterStatus.RateLimited,
        _ => ChasterStatus.Unavailable,
    };

    public void Dispose() => _http.Dispose();
}
