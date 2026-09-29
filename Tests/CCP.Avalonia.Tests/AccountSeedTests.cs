using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The head's CoreAccount seed (Platform/AccountSeed.cs): entitled only on a real answer,
/// FAIL CLOSED on any exception.</summary>
public sealed partial class AccountSeedTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();

    public AccountSeedTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        Unseed();
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        Unseed();
    }

    private static void Unseed()
    {
        CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
        CoreAccount.DisplayNameProvider = null;
        CoreAccount.UnifiedUserId = null;
        CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
    }

    private static ProviderSubscription Make(string prefix) => new(prefix, () => new AppSettings());

    private static void AssertNothing()
    {
        Assert.False(CoreAccount.IsLoggedIn);
        Assert.False(CoreAccount.IsWhitelisted);
        Assert.False(CoreAccount.HasPremiumAccess);
        Assert.False(CoreAccount.HasLabAccess);
        Assert.False(CoreEntitlement.HasPremium); // TierGate's seam
        Assert.False(CoreEntitlement.HasLab);
    }

    [Fact]
    public void AStoredTier2Patron_IsEntitled()
    {
        // Positive control: the same seed DOES answer yes from a real cached validate.
        _secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        _secrets["patreon_cache"] = JsonConvert.SerializeObject(new PatreonCachedState
        { Tier = PatreonTier.Level2, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1), DisplayName = "Bambi" });
        Assert.True(AccountSeed.Seed(Make));
        Assert.True(CoreAccount.IsLoggedIn);
        Assert.True(CoreAccount.HasPremiumAccess);
        Assert.True(CoreAccount.HasLabAccess);
        // TierGate reads CoreEntitlement, not CoreAccount: the seed must feed both (WPF App.xaml.cs:487).
        Assert.True(CoreEntitlement.HasPremium);
        Assert.True(CoreEntitlement.HasLab);
        Assert.Equal("Bambi", CoreAccount.DisplayName);
    }

    [Fact]
    public void AStoredWhitelistedDiscordUser_IsLoggedInAndEntitled()
    {
        // Parallel to Patreon, as WPF: Discord's cached whitelist promotes the Patreon gate (DiscordService.ApplyWhitelistAccess).
        _secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(5) });
        _secrets["discord_cache"] = JsonConvert.SerializeObject(new DiscordCachedState
        { UserId = "4242", Username = "bambi", IsWhitelisted = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1) });
        Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
        Assert.True(CoreAccount.IsLoggedIn);
        Assert.True(CoreAccount.IsWhitelisted);
        Assert.True(CoreAccount.HasLabAccess);
        Assert.Equal("bambi", CoreAccount.DisplayName);
    }

    [Fact]
    public void AProviderThatCannotBeBuilt_SeedsNothing()
    {
        Assert.False(AccountSeed.Seed(_ => throw new InvalidOperationException("boom")));
        AssertNothing();
        Assert.False(AccountSeed.Seed(Make, _ => throw new InvalidOperationException("boom")));
        AssertNothing();
    }

    [Fact]
    public void NoTokens_OrAStoreThatThrows_IsNotEntitled()
    {
        Assert.True(AccountSeed.Seed(Make));
        AssertNothing();
        CoreSecrets.RetrieveProvider = _ => throw new InvalidOperationException("keyring gone");
        AssertNothing();
    }

    [Fact]
    public async Task Logout_ClearsTokensIdentityAndProgression_AndRestoreBringsTheIdBack()
    {
        var s = CoreSettings.Current;
        var (oldId, oldName, oldLevel, oldXp) = (s.UnifiedId, s.UserDisplayName, s.PlayerLevel, s.PlayerXP);
        try
        {
            s.UnifiedId = "u-1"; s.UserDisplayName = "Bambi"; s.HasLinkedDiscord = s.HasLinkedPatreon = true;
            s.AuthToken = "tok"; s.PlayerLevel = 12; s.PlayerXP = 345;
            foreach (var n in new[] { "patreon_auth", "discord_auth", "substar_auth" })
                _secrets[n] = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));

            AccountSeed.RestoreSession(); // WPF App.xaml.cs:2267
            Assert.Equal("u-1", CoreAccount.UnifiedUserId);

            await AccountSeed.Logout();

            Assert.False(CoreAccount.IsLoggedIn);
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.Null(s.UnifiedId);
            Assert.Null(s.UserDisplayName);
            Assert.Null(s.AuthToken);
            Assert.False(s.HasLinkedDiscord || s.HasLinkedPatreon);
            Assert.All(new[] { "patreon_auth", "discord_auth", "substar_auth" }, n => Assert.Null(_secrets.GetValueOrDefault(n)));
            // Unit 7c: the progression clear ships with the push (WPF ClearProgressionData).
            Assert.Equal(1, s.PlayerLevel);
            Assert.Equal(0, s.PlayerXP);
        }
        finally { (s.UnifiedId, s.UserDisplayName, s.PlayerLevel, s.PlayerXP) = (oldId, oldName, oldLevel, oldXp); }
    }

    [Fact]
    public async Task Logout_WhoseKeyringClearFails_IsReported_AndASuccessfulOneResetsIt()
    {
        var (sandboxed, notRemembered) = (SecretStore.Sandboxed, SecretStore.NotRemembered);
        var clearsWork = false;
        try
        {
            SecretStore.Sandboxed = SecretStore.NotRemembered = false;
            SecretStore.OsWriteOverride = (_, v) => v != null || clearsWork;   // a locked keyring: writes land, removals do not
            CoreSecrets.RetrieveProvider = SecretStore.Retrieve;
            CoreSecrets.StoreProvider = SecretStore.Store;
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            SecretStore.Store("patreon_auth", "{}");

            await AccountSeed.Logout();
            Assert.True(SecretStore.ClearFailed);

            clearsWork = true;
            await AccountSeed.Logout();
            Assert.False(SecretStore.ClearFailed);
        }
        finally
        {
            SecretStore.OsWriteOverride = null;
            (SecretStore.Sandboxed, SecretStore.NotRemembered) = (sandboxed, notRemembered);
            SecretStore.ClearFailed = false;
        }
    }

    [Fact]
    public async Task RestoreSession_NeedsSomethingToProveIt_AndA404SignsOut()
    {
        var s = CoreSettings.Current;
        var oldId = s.UnifiedId;
        try
        {
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            s.UnifiedId = "u-1";
            s.AuthToken = null;
            AccountSeed.RestoreSession();            // no token, no provider: stays signed out
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.False(CoreAccount.IsLoggedIn);

            s.AuthToken = "tok";
            AccountSeed.RestoreSession();
            Assert.Equal("u-1", CoreAccount.UnifiedUserId);

            await AccountSeed.ValidateRestoredSessionAsync(new V2AuthService(() => s, new Answer(HttpStatusCode.NotFound)));
            Assert.Null(CoreAccount.UnifiedUserId);  // server does not know the id: signed out
            Assert.Null(s.UnifiedId);
        }
        finally { s.UnifiedId = oldId; s.AuthToken = null; }
    }

    private sealed class Answer(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
}
