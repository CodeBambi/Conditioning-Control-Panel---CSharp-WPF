using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Tests that swap the process-wide CoreSecrets / MergedRecovery / CoreAccount statics.</summary>
[CollectionDefinition(Name)]
public sealed class CoreSecretsStatics { public const string Name = "CoreSecretsStatics"; }

/// <summary>
/// The Patreon / SubscribeStar lifecycle (ProviderSubscription) against a fake proxy. Response
/// bodies are transcribed from the proxy shapes the WPF services read (PatreonModels.cs:
/// PatreonSubscriptionResponse / PatreonTokenResponse); expected outcomes from the pre-move WPF
/// PatreonService.cs / SubscribeStarService.cs. Nothing here reached the real proxy.
/// </summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class ProviderSubscriptionTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly AppSettings _settings = new() { UnifiedId = "u_1" };
    private readonly Proxy _proxy = new();

    public ProviderSubscriptionTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings.AuthToken = "ccp-tok";   // CoreSecrets-backed, so after the seed
        CoreAccount.UnifiedUserId = null;
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        CoreAccount.UnifiedUserId = null;
    }

    /// <summary>A scripted proxy: path -> (status, body), or a throw.</summary>
    private sealed class Proxy : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode, string)> Routes = new();
        public readonly List<string> Seen = new();
        public Exception? Throw;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Seen.Add($"{r.Method} {r.RequestUri!.AbsolutePath} bearer={r.Headers.Authorization?.Parameter} " +
                     $"x-auth={string.Join(",", r.Headers.TryGetValues("X-Auth-Token", out var v) ? v : Array.Empty<string>())} {body}");
            if (Throw != null) throw Throw;
            var (status, json) = Routes.TryGetValue(r.RequestUri.AbsolutePath, out var hit) ? hit : (HttpStatusCode.NotFound, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }

    private ProviderSubscription Make(string prefix) => new(prefix, () => _settings, _proxy);

    private void Tokens(string prefix, string access, DateTime expiresAt) =>
        _secrets[prefix + "_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = access, RefreshToken = "refresh-1", ExpiresAt = expiresAt });

    private static string Validate(bool active, int tier, bool whitelisted = false, string? name = "Bambi") =>
        $"{{\"is_active\":{(active ? "true" : "false")},\"tier\":{tier},\"patreon_user_id\":\"p9\"," +
        $"\"display_name\":{(name == null ? "null" : $"\"{name}\"")},\"is_whitelisted\":{(whitelisted ? "true" : "false")}," +
        "\"unified_id\":\"u_1\",\"needs_registration\":false}";

    private const string Refreshed = "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":2678400,\"token_type\":\"Bearer\"}";

    private static void AssertAbout(DateTime? stamp, TimeSpan fromNow) =>
        Assert.InRange(stamp!.Value, DateTime.UtcNow + fromNow - TimeSpan.FromMinutes(1), DateTime.UtcNow + fromNow + TimeSpan.FromMinutes(1));

    [Fact]
    public async Task PatreonTier2_Validates_StampsBothGraces_AndCaches24h()
    {
        Tokens("patreon", "access-1", DateTime.UtcNow.AddDays(10));
        _proxy.Routes["/patreon/validate"] = (HttpStatusCode.OK, Validate(true, 2));
        using var p = Make("patreon");
        Assert.Equal(PatreonTier.Level2, await p.ValidateSubscriptionAsync(forceRefresh: true));
        Assert.Equal("GET /patreon/validate bearer=access-1 x-auth=ccp-tok ", Assert.Single(_proxy.Seen));
        Assert.True(p.IsActive);
        Assert.Equal("Bambi", p.DisplayName);
        Assert.Equal("u_1", CoreAccount.UnifiedUserId);
        AssertAbout(_settings.PatreonPremiumValidUntil, TimeSpan.FromDays(14));
        AssertAbout(_settings.PatreonLabValidUntil, TimeSpan.FromDays(14));
        var cache = JsonConvert.DeserializeObject<PatreonCachedState>(_secrets["patreon_cache"]!)!;
        Assert.Equal(PatreonTier.Level2, cache.Tier);
        AssertAbout(cache.CacheExpiresAt, TimeSpan.FromHours(24));
        Assert.True(ProviderSubscription.HasLabAccess(p, null, _settings));
    }

    [Fact]
    public async Task Tier1_Patreon_KillsAStaleLabWindow_SubscribeStar_LeavesIt()
    {
        foreach (var prefix in new[] { "patreon", "substar" })
        {
            Tokens(prefix, "access-1", DateTime.UtcNow.AddDays(10));
            _proxy.Routes[$"/{prefix}/validate"] = (HttpStatusCode.OK, Validate(true, 1));
            _settings.PatreonLabValidUntil = DateTime.UtcNow.AddDays(3);
            using var p = Make(prefix);
            Assert.Equal(PatreonTier.Level1, await p.ValidateSubscriptionAsync(forceRefresh: true));
            AssertAbout(_settings.PatreonPremiumValidUntil, TimeSpan.FromDays(14));
            if (prefix == "patreon") Assert.Null(_settings.PatreonLabValidUntil);
            else AssertAbout(_settings.PatreonLabValidUntil, TimeSpan.FromDays(3));
        }
        Assert.EndsWith("POST /substar/validate bearer=access-1 x-auth=ccp-tok ", _proxy.Seen.Last());
    }

    [Theory]
    [InlineData("patreon")]
    [InlineData("substar")]
    public async Task Whitelisted_Inactive_TierZero_IsLevel2(string prefix)
    {
        Tokens(prefix, "access-1", DateTime.UtcNow.AddDays(10));
        _proxy.Routes[$"/{prefix}/validate"] = (HttpStatusCode.OK, Validate(false, 0, whitelisted: true));
        using var p = Make(prefix);
        Assert.Equal(PatreonTier.Level2, await p.ValidateSubscriptionAsync(forceRefresh: true));
        Assert.True(p.IsWhitelisted);
        AssertAbout(_settings.PatreonLabValidUntil, TimeSpan.FromDays(14));
    }

    [Fact]
    public async Task ExpiredToken_Refreshes_ThenValidatesWithTheNewToken()
    {
        Tokens("patreon", "access-1", DateTime.UtcNow.AddHours(-1));
        _proxy.Routes["/patreon/refresh"] = (HttpStatusCode.OK, Refreshed);
        _proxy.Routes["/patreon/validate"] = (HttpStatusCode.OK, Validate(true, 1));
        using var p = Make("patreon");
        Assert.Equal(PatreonTier.Level1, await p.ValidateSubscriptionAsync(forceRefresh: true));
        Assert.Equal(new[]
        {
            "POST /patreon/refresh bearer= x-auth= {\"refresh_token\":\"refresh-1\"}",
            "GET /patreon/validate bearer=access-2 x-auth=ccp-tok ",
        }, _proxy.Seen);
        var stored = JsonConvert.DeserializeObject<PatreonTokenData>(_secrets["patreon_auth"]!)!;
        Assert.Equal("refresh-2", stored.RefreshToken);
        AssertAbout(stored.ExpiresAt, TimeSpan.FromSeconds(2678400));
        Assert.False(p.GrantLooksDead);
    }

    /// <summary>
    /// WPF #585 + PatreonGrantHealth: an expired token whose refresh fails keeps the cached tier and
    /// the tokens. Only a refusal (4xx, or a 5xx once the expiry is 3+ days stale) marks the grant
    /// dead; a young 5xx or a throw says nothing. SubscribeStar drops to None and has no flag.
    /// </summary>
    [Theory]
    [InlineData("patreon", 400, 1, true)]
    [InlineData("patreon", 500, 4, true)]
    [InlineData("patreon", 500, 1, false)]
    [InlineData("patreon", 429, 30, false)]
    [InlineData("patreon", 0, 30, false)]   // 0 = the request throws
    [InlineData("substar", 400, 1, false)]
    public async Task RefreshFailure_GrantLooksDead_ExactlyAsWpf(string prefix, int status, int expiredDaysAgo, bool dead)
    {
        // A still-valid cache from an earlier validate (Level1), as WPF's LoadCachedState would find it.
        _secrets[prefix + "_cache"] = JsonConvert.SerializeObject(new PatreonCachedState
        { Tier = PatreonTier.Level1, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1) });
        Tokens(prefix, "access-1", DateTime.UtcNow.AddDays(-expiredDaysAgo));
        _proxy.Routes[$"/{prefix}/refresh"] = ((HttpStatusCode)status, "{\"error\":\"Internal server error\"}");
        if (status == 0) _proxy.Throw = new HttpRequestException("offline");
        using var p = Make(prefix);
        Assert.Equal(PatreonTier.Level1, p.CurrentTier);
        var tier = await p.ValidateSubscriptionAsync(forceRefresh: true);
        Assert.Equal(dead, p.GrantLooksDead);
        Assert.Equal(prefix == "patreon" ? PatreonTier.Level1 : PatreonTier.None, tier);
        Assert.NotNull(_secrets[prefix + "_auth"]);   // #585: tokens survive a failed refresh
        Assert.DoesNotContain(_proxy.Seen, s => s.Contains("/validate"));
    }

    [Fact]
    public async Task Unauthorized_ThenRefreshRefused_ClearsTokens_AndDropsToNone()
    {
        Tokens("patreon", "access-1", DateTime.UtcNow.AddDays(10));
        _proxy.Routes["/patreon/validate"] = (HttpStatusCode.Unauthorized, "{}");
        _proxy.Routes["/patreon/refresh"] = (HttpStatusCode.BadRequest, "{}");
        using var p = Make("patreon");
        Assert.Equal(PatreonTier.None, await p.ValidateSubscriptionAsync(forceRefresh: true));
        Assert.False(p.IsAuthenticated);
        Assert.True(p.GrantLooksDead);
    }

    [Fact]
    public async Task AGoodValidate_ClearsGrantLooksDead()
    {
        Tokens("patreon", "access-1", DateTime.UtcNow.AddDays(-5));
        _proxy.Routes["/patreon/refresh"] = (HttpStatusCode.InternalServerError, "{}");
        using var p = Make("patreon");
        await p.ValidateSubscriptionAsync(forceRefresh: true);
        Assert.True(p.GrantLooksDead);
        _proxy.Routes["/patreon/refresh"] = (HttpStatusCode.OK, Refreshed);
        _proxy.Routes["/patreon/validate"] = (HttpStatusCode.OK, Validate(true, 1));
        await p.ValidateSubscriptionAsync(forceRefresh: true);
        Assert.False(p.GrantLooksDead);
    }

    /// <summary>Any exception, a garbage body, an HTTP error, no token, or a lapsed patron: no entitlement.</summary>
    [Theory]
    [InlineData("inactive")]
    [InlineData("throw")]
    [InlineData("garbage")]
    [InlineData("500")]
    [InlineData("notokens")]
    [InlineData("storethrows")]
    public async Task AnyFailure_FromNothing_GrantsNothing(string how)
    {
        if (how != "notokens") Tokens("patreon", "access-1", DateTime.UtcNow.AddDays(10));
        _proxy.Routes["/patreon/validate"] = how == "500"
            ? (HttpStatusCode.InternalServerError, Validate(true, 2))
            : (HttpStatusCode.OK, how == "garbage" ? "<html>not json" : Validate(how != "inactive", 2));
        if (how == "throw") _proxy.Throw = new InvalidOperationException("boom");
        if (how == "storethrows") CoreSecrets.RetrieveProvider = _ => throw new InvalidOperationException("keyring");
        using var p = Make("patreon");
        Assert.Equal(PatreonTier.None, await p.ValidateSubscriptionAsync(forceRefresh: true));
        Assert.False(ProviderSubscription.HasPremiumAccess(p, p, _settings));
        Assert.False(ProviderSubscription.HasLabAccess(p, p, _settings));
        Assert.Null(_settings.PatreonPremiumValidUntil);
    }

    [Fact]
    public async Task Exchange_StoresWpfShapedTokens()
    {
        _proxy.Routes["/patreon/token"] = (HttpStatusCode.OK, Refreshed);
        using var p = Make("patreon");
        await p.ExchangeCodeAsync(new { code = "c1", redirect_uri = "http://localhost:47832/callback/" });
        Assert.Equal("POST /patreon/token bearer= x-auth= {\"code\":\"c1\",\"redirect_uri\":\"http://localhost:47832/callback/\"}",
            Assert.Single(_proxy.Seen));
        var json = _secrets["patreon_auth"]!;
        Assert.StartsWith("{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_at\":\"", json);
        Assert.True(p.IsAuthenticated);
    }

    [Fact]
    public async Task Logout_ClearsTokensCacheAndGrace()
    {
        Tokens("patreon", "access-1", DateTime.UtcNow.AddDays(10));
        _proxy.Routes["/patreon/validate"] = (HttpStatusCode.OK, Validate(true, 2));
        using var p = Make("patreon");
        await p.ValidateSubscriptionAsync(forceRefresh: true);
        p.Logout();
        Assert.All(new object?[] { _secrets["patreon_auth"], _secrets["patreon_cache"], _settings.PatreonPremiumValidUntil, _settings.PatreonLabValidUntil }, Assert.Null);
        Assert.Equal(PatreonTier.None, p.CurrentTier);
        Assert.False(ProviderSubscription.HasLabAccess(p, null, _settings));
    }
}
