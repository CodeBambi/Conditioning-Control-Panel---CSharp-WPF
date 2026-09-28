using System;
using Serilog;
using static ConditioningControlPanel.Services.V2AuthService;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF-only half of <see cref="V2AuthService"/> (which lives in Core): applying a profile
    /// to the live settings reads App.Progression and saves through App.Settings. An extension so
    /// every <c>v2Auth.ApplyUserDataToSettings(...)</c> call site is unchanged.
    /// ponytail: head-only until unit 6 (read-only cloud adopt) moves the take-higher rule to Core.
    /// </summary>
    public static class V2AuthServiceHead
    {
        /// <summary>
        /// Apply v2 user data to local settings, optionally storing an auth token.
        /// </summary>
        public static void ApplyUserDataToSettings(this V2AuthService _, V2User user, string? authToken = null)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.UnifiedId = user.UnifiedId;
            settings.UserDisplayName = user.DisplayName;
            settings.IsSeason0Og = user.IsSeason0Og;
            settings.CurrentSeason = user.CurrentSeason;
            settings.HighestLevelEver = user.HighestLevelEver;
            settings.HasLinkedDiscord = !string.IsNullOrEmpty(user.DiscordId);
            settings.HasLinkedPatreon = !string.IsNullOrEmpty(user.PatreonId);
            settings.PatreonTier = user.PatreonTier;

            // Discord/unified-login users with a LINKED Patreon sub have no local Patreon
            // tokens, so PatreonService.CurrentTier stays None and nothing else refreshes the
            // cached-premium window for them. The canonical HasPremiumAccess gate (now used by
            // the Takeover paths, #465) relies on that window — extend it here so a
            // server-confirmed linked tier keeps premium alive, mirroring the 2-week grace
            // direct Patreon validation writes. Never shorten an existing longer window.
            EntitlementTierRule.ExtendGrace(settings, user.PatreonTier, DateTime.UtcNow);

            // Store auth token if provided
            if (!string.IsNullOrEmpty(authToken))
            {
                settings.AuthToken = authToken;
            }

            // Sync level/XP using "take higher" logic to prevent progress loss
            // Server returns TOTAL accumulated XP, but PlayerXP stores current-level XP
            if (user.Level > 0)
            {
                var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
                var serverTotalXp = (double)user.Xp;

                if (serverTotalXp >= localTotalXp)
                {
                    settings.PlayerLevel = user.Level;
                    settings.PlayerXP = App.Progression?.GetCurrentLevelXP(user.Level, user.Xp) ?? 0;
                }
                else
                {
                    Log.Information("[V2Auth] Keeping local progress (higher): Local Level {LocalLevel} ({LocalXP} total) > Server Level {ServerLevel} ({ServerXP} total)",
                        settings.PlayerLevel, (int)localTotalXp, user.Level, user.Xp);
                }
            }

            App.Settings?.Save();
        }
    }
}
