using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Seeds <see cref="CoreAccount"/> from Core's <see cref="ProviderSubscription"/> (the lifecycle WPF's
/// services delegate to), tokens via CoreSecrets (<see cref="SecretStore"/>). FAIL CLOSED: a provider
/// that cannot be built seeds nothing; CoreAccount turns a throwing provider into "no".
/// No login UI yet (unit 5), no Discord (unit 4b), no sync (units 6-7).
/// </summary>
internal static class AccountSeed
{
    internal static ProviderSubscription? Patreon { get; private set; }
    internal static ProviderSubscription? SubscribeStar { get; private set; }

    /// <param name="make">Tests only: builds the provider for a prefix.</param>
    internal static bool Seed(Func<string, ProviderSubscription>? make = null)
    {
        make ??= prefix => new ProviderSubscription(prefix);
        ProviderSubscription patreon, substar;
        try { patreon = make("patreon"); substar = make("substar"); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Account providers unavailable: signed out, no entitlement");
            return false;
        }
        (Patreon, SubscribeStar) = (patreon, substar);

        // WPF App.IsLoggedIn / UserDisplayName / the PatreonService gates, minus Discord.
        CoreAccount.IsLoggedInProvider = () =>
            patreon.IsAuthenticated || substar.IsAuthenticated || !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);
        CoreAccount.DisplayNameProvider = () =>
        {
            var s = CoreSettings.Current;
            if (s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername)) return s.OfflineUsername;
            return s.UserDisplayName ?? patreon.DisplayName ?? substar.DisplayName;
        };
        CoreAccount.IsWhitelistedProvider = () => patreon.IsWhitelisted;
        CoreAccount.HasPremiumAccessProvider = () => ProviderSubscription.HasPremiumAccess(patreon, substar, CoreSettings.Current);
        CoreAccount.HasLabAccessProvider = () => ProviderSubscription.HasLabAccess(patreon, substar, CoreSettings.Current);
        return true;
    }

    /// <summary>WPF's startup order: Patreon validates, then SubscribeStar. Never throws.</summary>
    internal static async Task InitializeAsync()
    {
        if (Patreon is { } p) await p.InitializeAsync();
        if (SubscribeStar is { } s) await s.InitializeAsync();
    }
}
