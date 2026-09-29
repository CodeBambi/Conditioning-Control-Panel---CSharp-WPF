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

/// <summary>DiscordAccount against a fake proxy. Bodies transcribed from the proxy shapes WPF reads
/// (DiscordModels.cs); outcomes from pre-move WPF DiscordService. No real proxy.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class DiscordAccountTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly AppSettings _settings = new() { UnifiedId = "u_1" };
    private readonly Proxy _proxy = new();
    private readonly ProviderSubscription _patreon;

    public DiscordAccountTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings.AuthToken = "ccp-tok";
        _patreon = new ProviderSubscription("patreon", () => _settings, _proxy);
        DeadRefreshTokens.ResetForTests();   // process-global: a refused "refresh-1" must not leak between tests
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        CoreAccount.UnifiedUserId = null;
        V2AuthService.MergedRecovery = null;
        _patreon.Dispose();
        DeadRefreshTokens.ResetForTests();
    }

    private sealed class Proxy : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode, string)> Routes = new();
        public readonly List<string> Seen = new();
        public Exception? Throw;
        public Action<string>? OnSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Seen.Add($"{r.Method} {r.RequestUri!.AbsolutePath} bearer={r.Headers.Authorization?.Parameter} {body}");
            if (Throw != null) throw Throw;
            var (status, json) = Routes.TryGetValue(r.RequestUri.AbsolutePath, out var hit) ? hit : (HttpStatusCode.NotFound, "{}");
            OnSend?.Invoke(r.RequestUri.AbsolutePath);
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }

    private DiscordAccount Make() => new(() => _patreon, () => _settings, _proxy);

    private void Tokens(DateTime expiresAt) =>
        _secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
        { AccessToken = "access-1", RefreshToken = "refresh-1", ExpiresAt = expiresAt });

    private const string User =
        "{\"id\":\"4242\",\"username\":\"bambi\",\"global_name\":\"Bambi\",\"avatar\":\"a_f00\",\"guild_avatar\":null," +
        "\"is_staff\":true,\"staff_role\":\"support\",\"needs_registration\":false,\"unified_id\":\"u_1\"," +
        "\"is_whitelisted\":true,\"patreon_tier\":0}";
    private const string Refreshed = "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":604800,\"token_type\":\"Bearer\"}";

    private void AssertNothingGranted(DiscordAccount d)
    {
        Assert.Null(d.UserId);
        Assert.Null(_settings.PatreonPremiumValidUntil);
        Assert.False(_patreon.IsWhitelisted);
        Assert.False(ProviderSubscription.HasPremiumAccess(_patreon, null, _settings));
    }

    [Fact]
    public async Task Valid_SetsIdentity_Caches24h_AndGrantsTheWhitelist()
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.OK, User);
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Equal("GET /discord/validate bearer=access-1 ", Assert.Single(_proxy.Seen));
        Assert.Equal(("4242", "Bambi", "support", true), (d.UserId, d.DisplayName, d.StaffRole, d.IsStaff));
        Assert.Equal("https://cdn.discordapp.com/avatars/4242/a_f00.gif?size=64", d.GetAvatarUrl(64));
        var cache = JsonConvert.DeserializeObject<DiscordCachedState>(_secrets["discord_cache"]!)!;
        Assert.True(cache.IsWhitelisted);
        Assert.InRange(cache.CacheExpiresAt, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24.1));
        Assert.InRange(_settings.PatreonPremiumValidUntil!.Value, DateTime.UtcNow.AddHours(24.9), DateTime.UtcNow.AddHours(25.1));
        Assert.True(_patreon.IsWhitelisted);
        Assert.True(ProviderSubscription.HasLabAccess(_patreon, null, new AppSettings()));

        // A later launch: the cached whitelist promotes the peer at construction, before any network.
        using var p2 = new ProviderSubscription("patreon", () => new AppSettings(), _proxy);
        using var d2 = new DiscordAccount(() => p2, () => new AppSettings(), _proxy);
        Assert.Equal("4242", d2.UserId);
        Assert.True(p2.IsWhitelisted);
    }

    [Fact]
    public async Task Expired_Refreshes_ThenValidatesWithTheNewToken()
    {
        Tokens(DateTime.UtcNow.AddHours(-1));
        _proxy.Routes["/discord/refresh"] = (HttpStatusCode.OK, Refreshed);
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.OK, User);
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Equal(new[]
        {
            "POST /discord/refresh bearer= {\"refresh_token\":\"refresh-1\"}",
            "GET /discord/validate bearer=access-2 ",
        }, _proxy.Seen);
        Assert.Equal("refresh-2", JsonConvert.DeserializeObject<DiscordTokenData>(_secrets["discord_auth"]!)!.RefreshToken);
        Assert.Equal("4242", d.UserId);
    }

    [Fact]
    public async Task Expired_RefreshRefused_GrantsNothing_KeepsTokens()
    {
        Tokens(DateTime.UtcNow.AddHours(-1));
        _proxy.Routes["/discord/refresh"] = (HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.DoesNotContain(_proxy.Seen, s => s.Contains("/validate"));
        Assert.NotNull(_secrets["discord_auth"]);
        AssertNothingGranted(d);
    }

    [Fact]
    public async Task ARefusedGrant_IsNotAskedAgainThisSession_ButANewTokenIs()
    {
        // release/6.11.5 (WPF DiscordService.RefreshTokensAsync): invalid_grant is final, so the same
        // refresh token never goes out twice; signing in again stores a new one, which is asked.
        Tokens(DateTime.UtcNow.AddHours(-1));
        _proxy.Routes["/discord/refresh"] = (HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Single(_proxy.Seen, s => s.Contains("/discord/refresh"));

        _secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
        { AccessToken = "access-9", RefreshToken = "refresh-9", ExpiresAt = DateTime.UtcNow.AddHours(-1) });
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Equal(2, _proxy.Seen.Count(s => s.Contains("/discord/refresh")));
    }

    [Fact]
    public async Task Unauthorized_ThenRefreshRefused_ClearsTokensAndCache()
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        _secrets["discord_cache"] = "{}";
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.Unauthorized, "{}");
        _proxy.Routes["/discord/refresh"] = (HttpStatusCode.Unauthorized, "{}");
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Null(_secrets["discord_auth"]);
        Assert.Null(_secrets["discord_cache"]);
        Assert.False(d.IsAuthenticated);
        AssertNothingGranted(d);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("garbage")]
    [InlineData("500")]
    [InlineData("unreadable")]
    public async Task AnyFailure_GrantsNothing(string how)
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        _proxy.Routes["/discord/validate"] = how switch
        {
            "garbage" => (HttpStatusCode.OK, "<html>not json"),
            "500" => (HttpStatusCode.InternalServerError, User),
            _ => (HttpStatusCode.OK, User),
        };
        if (how == "throw") _proxy.Throw = new InvalidOperationException("boom");
        if (how == "unreadable") CoreSecrets.RetrieveProvider = _ => throw new InvalidOperationException("keyring");
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        AssertNothingGranted(d);
    }

    /// <summary>The server-healed auth token is adopted only for this session's own unified record.</summary>
    [Theory]
    [InlineData("u_2", "ccp-tok")]
    [InlineData("u_1", "new")]
    public async Task AuthTokenHeal_OnlyForTheSameUnifiedRecord(string serverId, string expected)
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.OK,
            User.Replace("\"unified_id\":\"u_1\"", $"\"unified_id\":\"{serverId}\",\"auth_token\":\"new\""));
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Equal("4242", d.UserId);
        Assert.Equal(expected, _settings.AuthToken);
    }

    [Fact]
    public async Task Unauthorized_RefreshSucceeds_RevalidatesWithTheNewToken()
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        var calls = 0;
        _proxy.Routes["/discord/refresh"] = (HttpStatusCode.OK, Refreshed);
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.Unauthorized, "{}");
        _proxy.OnSend = path => { if (path == "/discord/validate" && ++calls == 1) _proxy.Routes[path] = (HttpStatusCode.OK, User); };
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.Equal(new[]
        {
            "GET /discord/validate bearer=access-1 ",
            "POST /discord/refresh bearer= {\"refresh_token\":\"refresh-1\"}",
            "GET /discord/validate bearer=access-2 ",
        }, _proxy.Seen);
        Assert.Equal("4242", d.UserId);
    }

    [Fact]
    public async Task AMergedAccountResponse_StopsTheValidate_GrantsNothing()
    {
        Tokens(DateTime.UtcNow.AddDays(5));
        _proxy.Routes["/discord/validate"] = (HttpStatusCode.OK, User);
        V2AuthService.MergedRecovery = (_, _) => Task.FromResult(true);
        using var d = Make();
        await d.ValidateAndRefreshUserAsync(forceRefresh: true);
        Assert.False(_secrets.ContainsKey("discord_cache"));
        AssertNothingGranted(d);
    }

    /// <summary>WPF DiscordTokenStorage.StoreTokens rethrew: a token that was not saved fails the exchange.</summary>
    [Fact]
    public async Task ATokenStoreThatThrows_FailsTheExchange()
    {
        CoreSecrets.StoreProvider = (n, v) => { if (n == "discord_auth" && v != null) throw new System.IO.IOException("disk"); _secrets[n] = v; };
        _proxy.Routes["/discord/token"] = (HttpStatusCode.OK, Refreshed);
        using var d = Make();
        await Assert.ThrowsAsync<System.IO.IOException>(() => d.ExchangeCodeAsync("c1", "http://localhost:47833/callback/"));
        Assert.Equal("POST /discord/token bearer= {\"code\":\"c1\",\"redirect_uri\":\"http://localhost:47833/callback/\"}", Assert.Single(_proxy.Seen));
        Assert.False(d.IsAuthenticated);
    }

    [Fact]
    public async Task Exchange_StoresWpfShapedTokens_AndLogoutClearsThem()
    {
        _proxy.Routes["/discord/token"] = (HttpStatusCode.OK, Refreshed);
        using var d = Make();
        await d.ExchangeCodeAsync("c1", "http://localhost:47833/callback/");
        Assert.StartsWith("{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_at\":\"", _secrets["discord_auth"]);
        Assert.True(d.IsAuthenticated);
        d.Logout();
        Assert.Null(_secrets["discord_auth"]);
        Assert.False(d.IsAuthenticated);
    }
}
