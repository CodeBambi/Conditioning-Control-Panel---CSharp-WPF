using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>The invites wire as the rest of the app sees it. Tests hand in a fake.</summary>
public interface IInviteApi
{
    /// <summary>This month's codes and the lifetime conversion count. Unreachable when offline,
    /// signed out, or on a server that does not have invites yet; reachable with no snapshot when
    /// the account is not a subscriber.</summary>
    Task<InviteMine> MineAsync(CancellationToken ct = default);

    /// <summary>Redeem a friend's code for this account. Never throws.</summary>
    Task<RedeemOutcome> RedeemAsync(string code, CancellationToken ct = default);
}

/// <summary>
/// THE INVITES WIRE: <c>POST /v2/invites/&lt;op&gt;</c> with the account token, the same door,
/// base url and reply reading as the stakes and friends wires. Refusals are
/// <c>{ok:false, reason}</c>. Nothing is retried here.
/// </summary>
public sealed class InviteApi : IInviteApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string? _baseUrl;

    /// <summary>The head's account door and proxy (WPF: BackRoomApi.AppIdentity / BaseUrl). Unseeded:
    /// signed out and no url, so every call reads as unreachable and nothing is sent.</summary>
    public static Func<(string UnifiedId, string Token)?> DefaultIdentity { get; set; } = () => null;
    public static Func<string?> DefaultBaseUrl { get; set; } = () => null;

    public InviteApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? DefaultIdentity;
        _baseUrl = baseUrl ?? DefaultBaseUrl();
    }

    public async Task<InviteMine> MineAsync(CancellationToken ct = default)
    {
        var reply = await CallAsync("mine", new JObject(), ct).ConfigureAwait(false);
        return reply == null
            ? InviteMine.Unreachable
            : new InviteMine(true, InviteRules.ParseSnapshot(reply), InviteRules.ParseConverted(reply));
    }

    public async Task<RedeemOutcome> RedeemAsync(string code, CancellationToken ct = default)
    {
        var normal = InviteRules.NormalizeCode(code);
        if (normal == null) return new RedeemOutcome(false, "bad_code", null);
        return InviteRules.ParseRedeem(await CallAsync("redeem", new JObject { ["code"] = normal }, ct).ConfigureAwait(false));
    }

    private async Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct)
    {
        (string UnifiedId, string Token)? id;
        try { id = _identity(); }
        catch (Exception ex) { Diag.Swallowed(ex, "no identity to send"); id = null; }
        if (id == null || _baseUrl == null) return null;
        body["unified_id"] = id.Value.UnifiedId;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/invites/{op}")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            if ((int)res.StatusCode != 200)
                Log.Debug("[Invites] {Op} answered {Status}", op, (int)res.StatusCode);
            return FriendsApi.Read((int)res.StatusCode, text);
        }
        catch (OperationCanceledException) { return null; } // swallow: the 8s budget ran out or the caller cancelled; null is "offline"
        catch (Exception ex)
        {
            Log.Debug("[Invites] {Op} failed: {E}", op, ex.GetType().Name);
            return null;
        }
    }
}
