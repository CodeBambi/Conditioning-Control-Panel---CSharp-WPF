using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The head's CoreAccount seed (Platform/AccountSeed.cs): entitled only on a real answer,
/// FAIL CLOSED on any exception.</summary>
public sealed class AccountSeedTests : IDisposable
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
    }

    private static ProviderSubscription Make(string prefix) => new(prefix, () => new AppSettings());

    private static void AssertNothing()
    {
        Assert.False(CoreAccount.IsLoggedIn);
        Assert.False(CoreAccount.IsWhitelisted);
        Assert.False(CoreAccount.HasPremiumAccess);
        Assert.False(CoreAccount.HasLabAccess);
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
    public void Logout_ClearsTokensAndIdentity_ButNotProgression_AndRestoreBringsTheIdBack()
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

            AccountSeed.Logout();

            Assert.False(CoreAccount.IsLoggedIn);
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.Null(s.UnifiedId);
            Assert.Null(s.UserDisplayName);
            Assert.Null(s.AuthToken);
            Assert.False(s.HasLinkedDiscord || s.HasLinkedPatreon);
            Assert.All(new[] { "patreon_auth", "discord_auth", "substar_auth" }, n => Assert.Null(_secrets.GetValueOrDefault(n)));
            // Progression stays until unit 7 ships the clear with the push.
            Assert.Equal(12, s.PlayerLevel);
            Assert.Equal(345, s.PlayerXP);
        }
        finally { (s.UnifiedId, s.UserDisplayName, s.PlayerLevel, s.PlayerXP) = (oldId, oldName, oldLevel, oldXp); }
    }
}
