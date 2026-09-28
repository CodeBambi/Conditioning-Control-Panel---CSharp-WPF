using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Seeds <see cref="CoreAccount"/> from Core's <see cref="ProviderSubscription"/>s and <see cref="DiscordAccount"/>,
/// tokens via CoreSecrets. FAIL CLOSED: a provider that cannot be built seeds nothing; CoreAccount turns a throw into "no".
/// No sync (units 6-7), no Rich Presence.</summary>
internal static class AccountSeed
{
    internal static ProviderSubscription? Patreon { get; private set; }
    internal static ProviderSubscription? SubscribeStar { get; private set; }
    internal static DiscordAccount? Discord { get; private set; }

    /// <param name="make">Tests only: builds the provider for a prefix.</param>
    /// <param name="makeDiscord">Tests only: builds the Discord account.</param>
    internal static bool Seed(Func<string, ProviderSubscription>? make = null, Func<ProviderSubscription, DiscordAccount>? makeDiscord = null)
    {
        make ??= prefix => new ProviderSubscription(prefix);
        makeDiscord ??= peer => new DiscordAccount(() => peer);
        ProviderSubscription patreon, substar;
        DiscordAccount discord;
        try { patreon = make("patreon"); substar = make("substar"); discord = makeDiscord(patreon); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Account providers unavailable: signed out, no entitlement");
            return false;
        }
        (Patreon, SubscribeStar, Discord) = (patreon, substar, discord);
        patreon.PeerDisplayName = () => discord.CustomDisplayName; // WPF PatreonService

        // WPF App.IsLoggedIn / UserDisplayName / the PatreonService gates.
        CoreAccount.IsLoggedInProvider = () => patreon.IsAuthenticated || discord.IsAuthenticated
            || substar.IsAuthenticated || !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);
        CoreAccount.DisplayNameProvider = () =>
        {
            var s = CoreSettings.Current;
            if (s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername)) return s.OfflineUsername;
            return s.UserDisplayName ?? patreon.DisplayName ?? discord.CustomDisplayName ?? discord.DisplayName ?? substar.DisplayName;
        };
        CoreAccount.IsWhitelistedProvider = () => patreon.IsWhitelisted;
        CoreAccount.HasPremiumAccessProvider = () => ProviderSubscription.HasPremiumAccess(patreon, substar, CoreSettings.Current);
        CoreAccount.HasLabAccessProvider = () => ProviderSubscription.HasLabAccess(patreon, substar, CoreSettings.Current);
        return true;
    }

    /// <summary>WPF App.xaml.cs:2267 restore: the unified id persisted from the previous session.</summary>
    internal static void RestoreSession()
    {
        var id = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(id)) return;
        CoreAccount.UnifiedUserId = id;
        Log.Information("Restored UnifiedUserId from settings: {Id}", id);
    }

    /// <summary>Signs a provider out (WPF AccountService.LogoutProvider).</summary>
    internal static void LogoutProvider(string? provider)
    {
        switch (provider)
        {
            case "patreon": Patreon?.Logout(); break;
            case "discord": Discord?.Logout(); break;
            case "substar": SubscribeStar?.Logout(); break;
        }
    }

    /// <summary>
    /// WPF BtnQuickLogout_Click + the identity half of ClearAccountData (MainWindow.Login.cs:320-410):
    /// every provider out, identity and the rotated auth token (#455) cleared. Tokens and identity ONLY.
    /// ponytail: no pre-logout sync, no progression / XP-watermark clear; both ship with the push (unit 7).
    /// </summary>
    internal static void Logout()
    {
        Patreon?.Logout();
        Discord?.Logout();
        SubscribeStar?.Logout();
        CoreAccount.UnifiedUserId = null;
        var s = CoreSettings.Current;
        s.UnifiedId = null;
        s.AuthToken = null;
        s.UserDisplayName = null;
        s.HasLinkedDiscord = false;
        s.HasLinkedPatreon = false;
        CoreSettings.Save();
    }

    /// <summary>All three validate at once, as WPF starts them (App.xaml.cs). Never throws.</summary>
    internal static Task InitializeAsync() => Task.WhenAll(
        Patreon?.InitializeAsync() ?? Task.CompletedTask,
        SubscribeStar?.InitializeAsync() ?? Task.CompletedTask,
        Discord?.InitializeAsync() ?? Task.CompletedTask);
}
