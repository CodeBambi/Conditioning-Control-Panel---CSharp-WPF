using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Unified login: OAuth/login flow wiring for the panel.
    public partial class MainWindow
    {
        #region Unified Login

        /// <summary>
        /// Opens the unified login dialog
        /// </summary>
        internal void BtnUnifiedLogin_Click(object sender, RoutedEventArgs e)
        {
            OpenUnifiedLoginDialog();
        }

        /// <summary>
        /// Opens the unified login dialog and handles the result
        /// </summary>
        private void OpenUnifiedLoginDialog()
        {
            // Fall back to the startup snapshot: an expired session can null both
            // App.UnifiedUserId and _lastKnownUnifiedId (which is only set on an explicit
            // logout, not on token expiry). Without the snapshot, re-logging into the SAME
            // account after "session expired" reads previousUnifiedId as empty and the wipe
            // below fires, resetting progression to level 1. (data-loss bug)
            var previousUnifiedId = App.UnifiedUserId ?? _lastKnownUnifiedId ?? App.StartupUnifiedId;

            var loginDialog = new LoginDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (loginDialog.ShowDialog() == true && loginDialog.Result != null)
            {
                var result = loginDialog.Result;

                // Only wipe local progression when we can POSITIVELY confirm this is a
                // different account (both ids known and they differ). If the previous id is
                // unknown — e.g. an expired session cleared it — treat it as the same account
                // and DO NOT wipe; the post-login cloud sync is authoritative and reconciles
                // via take-higher, so no progress is lost. Flipping the default from
                // "wipe unless proven same" to "wipe only when proven different" closes the
                // data-loss hole where a same-account re-login reset everyone to level 1.
                var isDifferentAccount = !string.IsNullOrEmpty(previousUnifiedId)
                    && !string.IsNullOrEmpty(App.UnifiedUserId)
                    && previousUnifiedId != App.UnifiedUserId;
                var isSameAccount = !isDifferentAccount;

                // Update all UI
                UpdateQuickLoginUI();
                UpdateQuickPatreonUI();
                UpdateQuickDiscordUI();
                UpdatePatreonUI();
                UpdateSubscribeStarUI();
                UpdateDiscordUI();
                UpdateDiscordTabUI();
                UpdateBannerWelcomeMessage();
                UpdateAccountLinkingUI();

                if (!isSameAccount)
                {
                    // Save new account's identity/lifetime data set by ApplyUserDataToSettings inside LoginDialog.
                    // ClearProgressionData will zero these, so we restore them after clearing.
                    var savedHighestLevelEver = App.Settings?.Current?.HighestLevelEver ?? 0;
                    var savedIsSeason0Og = App.Settings?.Current?.IsSeason0Og ?? false;
                    var savedCurrentSeason = App.Settings?.Current?.CurrentSeason;
                    var savedPatreonTier = App.Settings?.Current?.PatreonTier ?? 0;

                    // Clear stale progression data from previous account before syncing.
                    // Defer quest generation — cloud data will be restored first.
                    ClearProgressionData(generateQuests: false);

                    // Restore the new account's lifetime data that ClearProgressionData just zeroed
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.HighestLevelEver = savedHighestLevelEver;
                        App.Settings.Current.IsSeason0Og = savedIsSeason0Og;
                        App.Settings.Current.CurrentSeason = savedCurrentSeason;
                        App.Settings.Current.PatreonTier = savedPatreonTier;
                        App.Settings.Save();
                    }
                }

                // Start profile sync
                App.ProfileSync?.StartHeartbeat();

                // Fetch the new account's data from the server
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500); // Let auth tokens settle
                    if (App.ProfileSync != null)
                    {
                        // Suppress achievement popups during post-login sync
                        // (achievements were just cleared and will be restored from cloud)
                        if (App.Achievements != null) App.Achievements.SuppressPopups = true;
                        try
                        {
                            // Load profile from cloud, then sync. Server is authoritative for
                            // progression data after login (local was just cleared).
                            var loaded = await App.ProfileSync.LoadProfileAsync();
                            if (loaded)
                            {
                                await App.ProfileSync.SyncProfileAsync();
                            }
                            else
                            {
                                // LoadProfileAsync fails for invite-code users (no OAuth token).
                                // For V2 users with UnifiedId, LoadProfileAsync calls SyncProfileAsync
                                // internally and returns its result. If it still returns false here,
                                // force a sync anyway — server will return authoritative data.
                                await App.ProfileSync.SyncProfileAsync();
                            }
                        }
                        finally
                        {
                            if (App.Achievements != null) App.Achievements.SuppressPopups = false;
                        }

                        // Refresh UI on the dispatcher thread after sync completes
                        DispatcherHelper.RunOnUISync(() =>
                        {
                            // Generate quests AFTER cloud data has been restored
                            if (!isSameAccount)
                            {
                                App.Quests?.CheckAndGenerateQuests();
                            }

                            UpdateLevelDisplay();
                            RefreshQuestUI();
                            DrawSkillTree();
                            UpdateQuickLoginUI();
                            UpdateUnlockablesVisibility(App.Settings?.Current?.PlayerLevel ?? 1);
                        });
                    }
                });

                // First-time login is the OTHER way a Discord account gets attached: the
                // link-an-existing-account flows offer achievement sharing, this one never did,
                // so Discord-first users never saw the prompt and their unlocks silently never
                // posted (#749). The method self-guards on DiscordShareAchievements, so calling
                // it after every successful login only prompts the users who still need it.
                if (App.Discord?.IsAuthenticated == true)
                {
                    OfferAchievementSharingAfterDiscordLink();
                }
            }
        }

        /// <summary>
        /// Clears all progression, quest, achievement, and streak data from local state.
        /// Does NOT clear identity fields (UnifiedId, UserDisplayName, link flags).
        /// Called on login (before sync), logout, and account deletion.
        /// </summary>
        /// <param name="generateQuests">If false, skip quest generation (caller will generate after cloud sync)</param>
        private void ClearProgressionData(bool generateQuests = true)
        {
            if (App.Settings?.Current != null)
            {
                var s = App.Settings.Current;

                // Progression
                s.PlayerXP = 0;
                s.PlayerLevel = 1;
                s.SkillPoints = 0;
                s.UnlockedSkills = new List<string>();
                s.SeasonalStreakRecoveryUsed = false;
                s.StreakFixCharges = 0; // per-account, server-authoritative balance
                s.HighestLevelEver = 0;

                // Quest streak
                s.DailyQuestStreak = 0;
                s.LastDailyQuestDate = null;

                // Streak shields
                s.StreakShieldsRemaining = 0;
                s.LastStreakShieldResetDate = null;
                s.StreakShieldUsedDates = new List<DateTime>();

                // Usage streaks
                s.CurrentStreak = 0;
                s.LastStreakDate = null;
                s.HighestStreak = 0;

                // Usage stats
                s.NightTimeUsageCount = 0;
                s.EarlyMorningUsageCount = 0;

                // Rerolls
                s.FreeRerollsUsedToday = 0;
                s.LastRerollResetDate = null;

                // Season / OG
                s.IsSeason0Og = false;
                s.CurrentSeason = null;
                s.PatreonTier = 0;

                App.Settings.Save();
            }

            // Reset quest progress (active quests + stats in quests.json)
            App.Quests?.ResetProgress(generateQuests);

            // Reset achievement progress (unlocked achievements + stats in achievements.json)
            App.Achievements?.ResetProgress();

            // Redraw skill tree to reflect cleared state
            DrawSkillTree();
        }

        /// <summary>
        /// Clears all account-specific data from settings, quests, and achievements.
        /// Called on logout and account deletion to prevent stale data bleeding into the next session.
        /// </summary>
        private void ClearAccountData()
        {
            // Tear down SubscribeStar too. It's a third login provider (alongside
            // Patreon/Discord) but the rest of the logout plumbing predates it, so
            // every full-logout path used to clear only Patreon+Discord. Its cached
            // whitelist/tokens would otherwise keep granting premium and re-inject
            // UnifiedUserId on the next launch — premium that survives "Log Out".
            App.SubscribeStar?.Logout();

            // Clear identity
            App.UnifiedUserId = null;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.UnifiedId = null;
                // The server rotates this token on each auth event; keeping it past logout
                // makes every re-login call present a rotated-out token and fail with
                // "Invalid or missing auth token" (#455).
                App.Settings.Current.AuthToken = null;
                App.Settings.Current.UserDisplayName = null;
                App.Settings.Current.HasLinkedDiscord = false;
                App.Settings.Current.HasLinkedPatreon = false;
            }

            // Clear all progression data
            ClearProgressionData();

            // ClearProgressionData just zeroed streaks/XP locally. Re-arm the sync service's
            // defaults guard so a re-login in this same app session READS the server profile
            // before it can PUSH those zeros — otherwise the quest/login streak flashes 0
            // until a later sync repaints it. Every logout path funnels through here.
            App.ProfileSync?.ResetLoadedProfileState();

            // Update all UI
            UpdateQuickLoginUI();
            UpdateQuickPatreonUI();
            UpdateQuickDiscordUI();
            UpdatePatreonUI();
            UpdateDiscordUI();
            UpdateBannerWelcomeMessage();
            UpdateAccountLinkingUI();
            UpdateUnlockablesVisibility(1);

            // Clear profile viewer so stale profile card doesn't linger
            ClearProfileViewer();
        }

        /// <summary>
        /// Logs out from all providers
        /// </summary>
        internal async void BtnQuickLogout_Click(object sender, RoutedEventArgs e)
        {
            // Push latest state to server before clearing local data
            // (prevents streak/progression loss when heartbeat hasn't synced yet)
            try
            {
                if (App.ProfileSync != null && !string.IsNullOrEmpty(App.UnifiedUserId))
                    await App.ProfileSync.SyncProfileAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to sync before logout");
            }

            // Remember which account was logged in (for same-account detection on re-login)
            _lastKnownUnifiedId = App.UnifiedUserId;

            // Stop heartbeat
            App.ProfileSync?.StopHeartbeat();

            // Logout from both providers
            App.Patreon?.Logout();
            App.Discord?.Logout();

            ClearAccountData();
        }

        /// <summary>
        /// Updates the quick login panel UI based on login state
        /// </summary>
        private void UpdateQuickLoginUI()
        {
            var isLoggedIn = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId) || App.IsLoggedIn;
            var displayName = App.Settings?.Current?.UserDisplayName
                           ?? App.Patreon?.DisplayName
                           ?? App.Discord?.DisplayName
                           ?? App.SubscribeStar?.DisplayName
                           ?? "User";

            SettingsTab.BtnUnifiedLogin.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            SettingsTab.LoggedInStatusPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

            // Show/hide login overlays on gated tabs
            QuestsTab.QuestsLoginOverlay.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            EnhancementsTab.EnhancementsLoginOverlay.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;

            if (isLoggedIn)
            {
                SettingsTab.TxtLoggedInName.Text = displayName;

                // Show OG flair
                if (App.Settings?.Current?.IsSeason0Og == true)
                {
                    SettingsTab.TxtLoggedInName.Text = $"⭐ {displayName}";
                }
            }
        }

        #endregion
    }
}
