using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The leash wire as the service sees it. <see cref="LeashApi"/> is the only real one;
/// tests hand in a fake. Nothing here throws: a fault is a null reply.</summary>
public interface ILeashApi
{
    /// <summary>One op. The parsed JSON reply (worded refusals included), or null for a network
    /// fault, a timeout, an HTML page or no account.</summary>
    Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default);
}

/// <summary>
/// THE LEASH WIRE (CONTRACT.md beside this file). <c>POST /v2/leash/&lt;op&gt;</c> through the same
/// token door, base url and reply reading as the friends wire. Nothing is retried here: the
/// service decides what may be retried (only <c>cut</c>).
/// </summary>
public sealed class LeashApi : ILeashApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public LeashApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null) return null;
        var o = (JObject)body.DeepClone();
        o["unified_id"] = id.Value.UnifiedId;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/leash/{op}")
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return FriendsApi.Read((int)res.StatusCode, text);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.Logger?.Debug("Leash {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }
}
