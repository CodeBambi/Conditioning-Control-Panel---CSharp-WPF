using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Stakes;

/// <summary>The stakes wire as the rest of the app sees it. <see cref="StakeApi"/> is the real
/// one; tests hand in a fake. Nothing here throws: a fault is a null reply.</summary>
public interface IStakeApi
{
    /// <summary>The account this call would be sent for, or null when nobody is signed in.
    /// Read BEFORE the await, so a reply can be checked against it afterwards.</summary>
    string? Account();

    /// <summary>One op (<c>limits</c>, <c>offer</c>, <c>state</c>). The parsed JSON reply (worded
    /// refusals included), or null for a network fault, a timeout, an HTML page or no account.</summary>
    Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default);
}

/// <summary>
/// THE STAKES WIRE: <c>POST /v2/stakes/&lt;op&gt;</c> with the account token, the same door,
/// base url and reply reading as the friends and leash wires. All replies are HTTP 200 JSON and
/// refusals are <c>{ok:false, reason}</c>. Nothing is retried here; the settlement watcher
/// decides what is asked again.
/// </summary>
public sealed class StakeApi : IStakeApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>The only ops this client sends.</summary>
    public static bool IsOp(string? op) => op is "limits" or "offer" or "state";

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public StakeApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public string? Account()
    {
        try { return _identity()?.UnifiedId; } catch { return null; }
    }

    public async Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
    {
        if (!IsOp(op)) return null;
        (string UnifiedId, string Token)? id;
        try { id = _identity(); } catch { id = null; }
        if (id == null) return null;
        var o = (JObject)body.DeepClone();
        o["unified_id"] = id.Value.UnifiedId;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/stakes/{op}")
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
            App.Logger?.Debug("Stakes {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }
}
