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
    public void AProviderThatCannotBeBuilt_SeedsNothing()
    {
        Assert.False(AccountSeed.Seed(_ => throw new InvalidOperationException("boom")));
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
}
