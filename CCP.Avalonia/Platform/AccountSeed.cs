using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Seeds <see cref="CoreAccount"/> from Core's <see cref="ProviderSubscription"/>s and <see cref="DiscordAccount"/>,
/// tokens via CoreSecrets. FAIL CLOSED: a provider that cannot be built seeds nothing; CoreAccount turns a throw into "no".
/// Cloud load (unit 6), then the known-fields-only push and heartbeat (unit 7c, Core <see cref="SyncPush"/>); no Rich Presence.</summary>
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
        // WPF App.xaml.cs:487-488: TierGate's seam reads the same gates (Patreon.HasPremiumAccess there).
        CoreEntitlement.HasPremiumProvider = () => CoreAccount.HasPremiumAccess;
        CoreEntitlement.HasLabProvider = () => CoreAccount.HasLabAccess;
        return true;
    }

    /// <summary>WPF App.xaml.cs:2267 restore: the unified id persisted from the previous session. Unlike WPF,
    /// only when something can still prove it (an auth token, or a signed-in provider): with no Secret Service
    /// the token is gone after a restart, and showing "signed in" with nothing behind it is worse than signed out.</summary>
    internal static void RestoreSession()
    {
        var id = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(id)) return;
        if (string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
            && Patreon?.IsAuthenticated != true && Discord?.IsAuthenticated != true && SubscribeStar?.IsAuthenticated != true)
        {
            Log.Information("Stored UnifiedUserId not restored: no auth token and no signed-in provider");
            return;
        }
        CoreAccount.UnifiedUserId = id;
        Log.Information("Restored UnifiedUserId from settings: {Id}", id);
    }

    /// <summary>WPF App.ValidateRestoredSessionAsync (Core <see cref="V2AuthService.ValidateRestoredSessionAsync"/>), run after
    /// the providers validated, then the cloud profile load that follows it. WPF's provider init paths load the profile when
    /// a provider authenticated, and this path loads it when the session validated; either way it loads here.</summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task ValidateRestoredSessionAsync(V2AuthService? v2 = null)
    {
        var id = CoreAccount.UnifiedUserId;
        if (string.IsNullOrEmpty(id) || CoreSettings.Current.OfflineMode) return;
        v2 ??= NewV2();
        // If a provider already authenticated, it validated the session (and owns the load in WPF).
        if (Patreon?.IsAuthenticated != true && Discord?.IsAuthenticated != true)
        {
            Log.Information("Validating restored session for {Id}...", id);
            var outcome = await v2.ValidateRestoredSessionAsync(id);
            if (outcome == V2AuthService.RestoreOutcome.Cleared) CoreAccount.UnifiedUserId = null;
            if (outcome != V2AuthService.RestoreOutcome.Validated) return;
        }
        await LoadProfileAsync(v2);
    }

    /// <summary>
    /// WPF ProfileSyncService.LoadProfileAsync's V2 read-before-write (ReadServerProfileBeforePushAsync), READ-ONLY:
    /// GET /v2/user/profile, follow the curve epoch, offer a tier rise, adopt take-higher (Core <see cref="ProfileAdopt"/>).
    /// Then, as WPF LoadProfileAsync does after the read, the heartbeat and the push (<see cref="Sync"/>; unseeded = none).
    /// ponytail: no season-recap nudge - the next layer.
    /// </summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task<bool> LoadProfileAsync(V2AuthService? v2 = null)
    {
        try { return await LoadProfileCoreAsync(v2 ?? NewV2()); }
        catch (Exception ex)
        {
            // Fire-and-forget from the login dialog and inside an async void Post at startup: never throw.
            Log.Warning(ex, "Profile load failed");
            return false;
        }
    }

    /// <summary>The push, seeded by the app at startup only: unseeded (every test that does not set it) nothing is pushed.</summary>
    internal static SyncPush? Sync;

    /// <summary>Tests only: the client every default-path load and restore uses.</summary>
    internal static Func<V2AuthService> NewV2 = () => new V2AuthService();

    private static async Task<bool> LoadProfileCoreAsync(V2AuthService v2)
    {
        var s = CoreSettings.Current;
        var id = s.UnifiedId;
        if (s.OfflineMode || string.IsNullOrEmpty(id)) return false;
        var user = await v2.GetUserProfileAsync(id);
        if (user == null)
        {
            Log.Warning("Profile load: server profile could not be read for {Id}", id);
            return false;
        }
        ProfileAdopt.ApplyCurveEpoch(s, user.CurveEpoch);
        // EntitlementTierSync.SignedInWithV2: a rise only for a V2 account with a token.
        if (!string.IsNullOrEmpty(s.AuthToken))
            EntitlementTierRule.ApplyRise(s, EntitlementTierRule.ParseTier(user.EffectiveTierRaw), DateTime.UtcNow);
        ProfileAdopt.AdoptReadBeforeWrite(s, user);
        CoreSettings.Save();
        Log.Information("Profile load: Level {Level} ({Xp} XP into level) after adopt", s.PlayerLevel, (int)s.PlayerXP);
        if (Sync is { } sync)
        {
            sync.MarkLoaded(user.Achievements);
            sync.StartHeartbeat();
            await sync.PushAsync("after load");
        }
        return true;
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
    /// Order (unit 7c): pre-logout push if loaded -> stop heartbeat -> providers and identity -> progression clear
    /// (Core <see cref="ProgressionClear"/> + achievements, WPF ClearProgressionData) -> the loaded flag reset.
    /// </summary>
    internal static async Task Logout()
    {
        if (Sync is { Loaded: true } sync) await sync.PushAsync("pre-logout", waitForGate: true);
        Sync?.StopHeartbeat();
        SecretStore.ClearFailed = false;
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
        ProgressionClear.Apply(s);
        CoreSettings.Save();
        App.Achievements?.Reset();
        Sync?.Reset();
    }

    /// <summary>All three validate at once, as WPF starts them (App.xaml.cs). Never throws.</summary>
    internal static Task InitializeAsync() => Task.WhenAll(
        Patreon?.InitializeAsync() ?? Task.CompletedTask,
        SubscribeStar?.InitializeAsync() ?? Task.CompletedTask,
        Discord?.InitializeAsync() ?? Task.CompletedTask);
}
