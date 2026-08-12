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
    // Patreon tab: exclusives submenu, Patreon exclusives content, and community prompts.
    public partial class MainWindow
    {
        #region Exclusives Gates

        // The Exclusives launcher popup submenu was retired for the Exclusives tab
        // ("the Velvet Vault", MainWindow.Exclusives.cs). Entitlement chips/veils on
        // that tab are repainted by RefreshExclusivesTab(), which took over the old
        // RefreshExclusivesSubmenuLocks contract — including the Graded Intake rule
        // (weekly pass counts as an open door for free accounts).

        /// <summary>
        /// Routes the gating overlay's CTA button to the App Info &amp; Data popup,
        /// where users can sign in with Patreon/Discord to unlock premium features.
        /// </summary>
        internal void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            ShowAppInfoPopup();
        }

        /// <summary>
        /// Toggles a translucent gating overlay's visibility based on the user's
        /// premium subscription state. Used by the new visible-but-locked tabs.
        /// </summary>
        private void RefreshPremiumGate(Border? gate)
        {
            if (gate == null) return;
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            gate.Visibility = hasPremium ? Visibility.Collapsed : Visibility.Visible;

            // FX (PR-4a): one shared animated treatment for every gate in the app - scrim fog
            // drift, a breathing glow behind the padlock, a sheen across the CTA. Attach is
            // idempotent, decorates only (it never touches Visibility or entitlement), and parks
            // all three clocks whenever the gate is collapsed or motion is reduced. This is the
            // choke point six of the eight gates already share; the other two (Blink Trainer,
            // Graded Intake) attach from their own refresh methods.
            Controls.PremiumGateFx.Attach(gate);
        }

        #endregion

        #region Patreon Exclusives Tab

        private void UpdatePatreonUI()
        {
            var tier = App.Patreon?.CurrentTier ?? PatreonTier.None;
            var isAuthenticated = App.Patreon?.IsAuthenticated ?? false;
            var isActivePatron = App.Patreon?.IsActivePatron ?? false;

            // Update login status
            if (isAuthenticated)
            {
                var isWhitelisted = App.Patreon?.IsWhitelisted == true;

                // Use unified display name first (what user chose), then fall back to Patreon-specific
                var unifiedDisplayName = App.Settings?.Current?.UserDisplayName;
                var patreonDisplayName = App.Patreon?.DisplayName;

                // Show unified DisplayName if available, otherwise Patreon display name
                var nameToShow = unifiedDisplayName ?? patreonDisplayName;
                AppSettingsTab.TxtPatreonStatus.Text = string.IsNullOrEmpty(nameToShow) ? "Connected to Patreon" : $"Welcome, {nameToShow}!";
                AppSettingsTab.TxtPatreonTier.Text = tier switch
                {
                    PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
                    PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
                    _ when isWhitelisted => Loc.Get("label_patreon_tier_whitelisted"),
                    _ => Loc.Get(isActivePatron ? "label_patreon_tier_patron" : "label_patreon_tier_connected")
                };
                AppSettingsTab.BtnPatreonLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                AppSettingsTab.TxtPatreonStatus.Text = Loc.Get("label_not_connected");
                AppSettingsTab.TxtPatreonTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");

                // Show "Link Patreon" if logged in via Discord, otherwise "Login"
                AppSettingsTab.BtnPatreonLogin.Content = hasUnifiedId ? "Link Patreon" : "Login";
            }

            // Phase 2: the Settings/Account tier card rides this method for the same reason the
            // header chip rides UpdateLevelDisplay - it is the choke point every auth change
            // already runs through (TierChanged for both providers, ClearAccountData, every
            // login/link flow). The section also repaints itself when it becomes visible, so a
            // missed call here degrades to "stale until you leave and come back", never to wrong.
            AppSettingsTab?.RefreshAccountTierBadge();

            // The page-wide AI lock veil is gone (design §6: superseded). Logged-out is an inline
            // row in the Engine Room and a teaser on the chat card, so the Companion page never
            // fully veils; both read App.HasCloudIdentity through the room's own sync.
            CompanionRoom?.SyncBrain();

            // Update feature lockboxes
            // All features are now Tier 1 (or whitelisted)
            var hasPremiumAccess = App.Patreon?.HasPremiumAccess == true;
            var level1Unlocked = hasPremiumAccess;
            var level2Unlocked = hasPremiumAccess; // Same as Level 1 now - all features at Tier 1

            // PHASE 8: PatreonFeaturesOverlay - the one big "become a patron" veil over the old
            // Exclusives grid - went with PatreonTabView. The Play door's per-card lockbands
            // (RefreshPlayCards, below) are the wall now, and they repaint on this same path.

            // Keep the patron-achievements section lock + counts in sync with entitlement.
            UpdateAchievementCount();

            // Haptics - unlock for all Patreon supporters.
            // Phase E deleted the three dead lock overlays (HapticsConnectionLock,
            // HapticsFeatureLock, HapticsComingSoonOverlay) — they were all Collapsed-forever
            // leftovers stacked behind the one live gate, HapticsGate, refreshed below.
            var hasHapticsAccess = hasPremiumAccess;
            HapticsTab.HapticsContentGrid.Opacity = hasHapticsAccess ? 1.0 : 0.3;
            HapticsTab.HapticsContentGrid.IsHitTestVisible = hasHapticsAccess;
            HapticsTab.HapticsConnectionBox.IsEnabled = hasHapticsAccess;
            HapticsTab.HapticsFeatureBox.IsEnabled = hasHapticsAccess;

            // Bambi Takeover (Autonomy) — visible-but-locked: keep BambiTakeoverTab.AutonomyUnlocked
            // always visible, BambiTakeoverTab.AutonomyLocked stays collapsed (legacy element), and the
            // new BambiTakeoverTab.BambiTakeoverGate translucent overlay handles gating.
            if (BambiTakeoverTab.AutonomyLocked != null) BambiTakeoverTab.AutonomyLocked.Visibility = Visibility.Collapsed;
            if (BambiTakeoverTab.AutonomyUnlocked != null) BambiTakeoverTab.AutonomyUnlocked.Visibility = Visibility.Visible;
            RefreshPremiumGate(BambiTakeoverTab.BambiTakeoverGate);
            RefreshPremiumGate(HapticsTab.HapticsGate);
            RefreshPremiumGate(RemoteControlTab.RemoteControlGate);
            RefreshPremiumGate(AwarenessTab.AwarenessGate);
            RefreshPremiumGate(LockdownTab.LockdownGate);
            if (SheListeningTab != null) RefreshPremiumGate(SheListeningTab.SheListeningGate);
            if (GradedIntakeTab != null) RefreshGradedIntakeGate();
            // PHASE 6: the Play door's wall is per-card lockbands, not one overlay, so entitlement
            // arriving (or lapsing) has to repaint it the same way it repaints every gate above.
            // This is also the path that covers the free-user logout, where no TierChanged fires.
            RefreshPlayCards();

            // Weekly intake pass. It is a FREE-TIER amenity - patrons have the feature outright and
            // must never see pass UI - so every surface that paints off IntakePassService has to be
            // re-evaluated on this path, not just at startup. This method is the single choke point
            // that both TierChanged handlers (Patreon and SubscribeStar) and every login/logout path
            // (ClearAccountData, the link flows, OpenUnifiedLoginDialog's completion) already run
            // through, so hanging the pass refresh here covers the arrival of premium AND its
            // removal - including the free-user logout, where the tier never changes and no
            // TierChanged event is raised at all.
            //
            // These are the existing entry points, called rather than reimplemented:
            //   RefreshIntakePassTile() - Dashboard centre tile (MainWindow.UiUpdates.cs)
            //   RefreshExclusivesTab()  - Exclusives tab chips/veils (incl. the pass-aware
            //                             Graded Intake card)
            // RefreshGradedIntakeGate() is already covered by the line above.
            try
            {
                RefreshIntakePassTile();
                RefreshExclusivesTab();
                // Anything else listening to the door (and the two lazily-attached handlers, if a
                // refresh has not run yet to install them) gets its own repaint. Idempotent, and
                // both current listeners marshal + no-op when nothing actually changed.
                App.IntakePass?.RaiseChanged();
            }
            catch (Exception ex) { App.Logger?.Debug("UpdatePatreonUI: intake pass refresh failed: {E}", ex.Message); }

            RefreshBecomeASubjectCta();
            // Blink Trainer uses its own gate refresh (also re-resolves stage
            // mode + status state since premium loss/gain flips the resolver
            // short-circuit and may swap demo↔live).
            RefreshBlinkTrainerGate();
            if (BlinkTrainerTab != null)
            {
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }

            // AI connection status. The old TxtAiStatus line lived on the AI Brain card; the
            // Engine Room's status line replaced it (design §6: "demoted — Z7, verbatim"), and the
            // attention gauge draws the same remaining-requests number as a meter. One difference
            // worth stating: the custom-provider error hint is kept, because "AI initializing"
            // forever is exactly the wrong thing to tell someone with a typo in their endpoint.
            var engine = CompanionRoom?.EngineVm;
            if (engine != null)
            {
                var provider = App.Settings?.Current?.CompanionPrompt?.AiProvider ?? Models.AiProviderType.Cloud;
                if (App.Ai?.IsAvailable == true)
                {
                    var remaining = App.Ai.DailyRequestsRemaining;
                    engine.SetStatus(remaining < 0
                        ? Loc.Get("companion_engine_status_ready")
                        : Loc.GetF("companion_engine_status_ready_fmt", remaining),
                        healthy: true);
                }
                else if (provider == Models.AiProviderType.OpenAiCompatible)
                {
                    engine.SetStatus(Loc.Get("label_ai_custom_error"), healthy: false);
                }
                else
                {
                    engine.SetStatus(Loc.Get("label_ai_initializing"), healthy: false);
                }
            }

            // Re-evaluate keyword triggers access (may have been disabled before Patreon validated)
            var hasKeywordAccess = KeywordTriggerService.HasAccess();
            // PHASE 5 (G3) + PHASE 8: the live editors are on the Awareness tab, and the OCR detail
            // rows are hidden until access is confirmed - re-seed them with the fresh verdict.
            // The three PatreonTab twins that used to be gated here (TxtKeywordTriggersLocked,
            // BtnKeywordTriggersStartStop, ChkScreenOcrEnabled) died with PatreonTabView;
            // SyncKeywordRescuePanelUi re-derives every one of those states from
            // KeywordTriggerService.HasAccess() itself.
            SyncKeywordRescuePanelUi();

            // If triggers were enabled in settings but couldn't start earlier (Patreon not validated yet),
            // start them now that access is confirmed
            if (hasKeywordAccess && App.Settings?.Current?.KeywordTriggersEnabled == true)
            {
                App.KeywordTriggers?.Start();
                _keyboardHook?.Start();
                if (App.Settings.Current.ScreenOcrEnabled)
                    App.ScreenOcr?.Start();
            }

            // Update XP bar login state when Patreon auth changes
            UpdateXPBarLoginState();

            // Dashboard premium quick-toggle rail: re-gate (lock overlay + greying) on
            // every auth change. Without this, logging out mid-session left the rail
            // chips live because the rail only refreshed on startup / TierChanged.
            RefreshPremiumRail();
        }

        // ========================================================================
        // Account sections reparenting — RETIRED in Phase 2 (gap-report R-2)
        // ========================================================================
        // The seven account/data cards used to live in PatreonTab's XAML tree and were
        // borrowed at runtime (DetachAccountSectionsInto / ReattachAccountSections) so the
        // dashboard's "App Info & Data" popup could show them. They now live in
        // Views/Controls/AppSettings/AccountSettingsSection.xaml, mounted permanently on
        // AppSettingsTab — same element names, same handlers, no reparenting. The writes
        // below read AppSettingsTab.X. The popup keeps only its About/version content.

        internal async void BtnPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                if (App.Discord?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Discord still active — just update Patreon UI
                    App.Patreon.UnifiedUserId = null;
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Patreon to existing account
                    AppSettingsTab.BtnPatreonLogin.IsEnabled = false;
                    AppSettingsTab.BtnPatreonLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Patreon.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "patreon");

                        if (success)
                        {
                            UpdateQuickPatreonUI();
                            UpdatePatreonUI();
                            UpdateDiscordUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Patreon");
                        MessageBox.Show($"Failed to link Patreon account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        AppSettingsTab.BtnPatreonLogin.IsEnabled = true;
                        UpdatePatreonUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        internal async void BtnDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    App.Discord.UnifiedUserId = null;
                    UpdateDiscordUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Discord to existing account
                    AppSettingsTab.BtnDiscordLogin.IsEnabled = false;
                    AppSettingsTab.BtnDiscordLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Discord.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "discord");

                        if (success)
                        {
                            UpdateQuickDiscordUI();
                            UpdateDiscordUI();
                            UpdatePatreonUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Discord");
                        MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        AppSettingsTab.BtnDiscordLogin.IsEnabled = true;
                        UpdateDiscordUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        private void UpdateDiscordUI()
        {
            if (App.Discord?.IsAuthenticated == true)
            {
                // Use unified display name first, then fall back to Discord-specific
                var discordDisplayName = App.Settings?.Current?.UserDisplayName ?? App.Discord.DisplayName;
                AppSettingsTab.TxtDiscordStatus.Text = $"Connected as {discordDisplayName}";
                AppSettingsTab.TxtDiscordInfo.Text = $"@{App.Discord.Username}";
                AppSettingsTab.BtnDiscordLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                AppSettingsTab.TxtDiscordStatus.Text = Loc.Get("label_not_connected");
                AppSettingsTab.TxtDiscordInfo.Text = Loc.Get("label_link_discord_for_community_features");

                // Show "Link Discord" if logged in via Patreon, otherwise "Login"
                AppSettingsTab.BtnDiscordLogin.Content = hasUnifiedId ? "Link Discord" : "Login";
            }

            // Update XP bar login state when Discord auth changes
            UpdateXPBarLoginState();
        }

        /// <summary>
        /// Updates the visibility of account linking buttons based on current login state
        /// </summary>
        private void UpdateAccountLinkingUI()
        {
            // Only show linking section if user is logged in with a unified account
            var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);
            var hasLinkedPatreon = App.Settings?.Current?.HasLinkedPatreon == true || App.Patreon?.IsAuthenticated == true;
            var hasLinkedDiscord = App.Settings?.Current?.HasLinkedDiscord == true || App.Discord?.IsAuthenticated == true;

            // Show section only if logged in and missing at least one provider
            bool showLinkingSection = hasUnifiedId && (!hasLinkedPatreon || !hasLinkedDiscord);
            AppSettingsTab.AccountLinkingSection.Visibility = showLinkingSection ? Visibility.Visible : Visibility.Collapsed;

            // Show individual buttons for unlinked providers
            AppSettingsTab.BtnLinkPatreon.Visibility = (hasUnifiedId && !hasLinkedPatreon) ? Visibility.Visible : Visibility.Collapsed;
            AppSettingsTab.BtnLinkDiscord.Visibility = (hasUnifiedId && !hasLinkedDiscord) ? Visibility.Visible : Visibility.Collapsed;

            // Show cloud settings backup section if user has a cloud identity
            AppSettingsTab.CloudSettingsBackupSection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
            AppSettingsTab.DataPrivacySection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
            if (hasUnifiedId)
            {
                _ = UpdateBackupStatus();
            }
        }

        /// <summary>
        /// Link Patreon account to existing unified account
        /// </summary>
        internal async void BtnLinkPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon == null) return;

            AppSettingsTab.BtnLinkPatreon.IsEnabled = false;
            AppSettingsTab.BtnLinkPatreon.Content = Loc.Get("login_connecting");

            try
            {
                // Start Patreon OAuth flow
                await App.Patreon.StartOAuthFlowAsync();

                // Link to existing unified account
                var success = await AccountService.LinkProviderV2Async(this, "patreon");

                if (success)
                {
                    UpdateQuickPatreonUI();
                    UpdatePatreonUI();
                    UpdateAccountLinkingUI();
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to link Patreon");
                MessageBox.Show($"Failed to link Patreon account.\n\n{ex.Message}",
                    "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                AppSettingsTab.BtnLinkPatreon.IsEnabled = true;
                AppSettingsTab.BtnLinkPatreon.Content = Loc.Get("btn_link_patreon");
            }
        }

        /// <summary>
        /// Link Discord account to existing unified account
        /// </summary>
        internal async void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            AppSettingsTab.BtnLinkDiscord.IsEnabled = false;
            AppSettingsTab.BtnLinkDiscord.Content = Loc.Get("login_connecting");

            try
            {
                // Start Discord OAuth flow
                await App.Discord.StartOAuthFlowAsync();

                // Link to existing unified account
                var success = await AccountService.LinkProviderV2Async(this, "discord");

                if (success)
                {
                    UpdateQuickDiscordUI();
                    UpdateDiscordUI();
                    UpdateAccountLinkingUI();
                    OfferAchievementSharingAfterDiscordLink();
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to link Discord");
                MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                    "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                AppSettingsTab.BtnLinkDiscord.IsEnabled = true;
                AppSettingsTab.BtnLinkDiscord.Content = Loc.Get("btn_link_discord");
            }
        }


        /// <summary>
        /// Achievement sharing is a separate opt-in that defaults OFF — users routinely
        /// link Discord and then wonder why nothing posts (support, 2026-07-10). Offer it
        /// once right after a successful link instead of leaving them to find the toggle.
        /// </summary>
        internal void OfferAchievementSharingAfterDiscordLink()
        {
            var s = App.Settings?.Current;
            if (s == null || s.DiscordShareAchievements) return;

            var share = MessageBox.Show(
                Loc.Get("msg_discord_share_achievements_prompt"),
                Loc.Get("title_discord_linked"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (share == MessageBoxResult.Yes)
            {
                s.DiscordShareAchievements = true;
                App.Settings?.Save();
                UpdateDiscordTabUI(); // sync the Share Achievements checkbox
            }
        }

        internal void ChkShareAchievements_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShareAchievements = chk.IsChecked == true;
                App.Settings.Save();
            }
        }

        internal void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShareLevelUps = chk.IsChecked == true;
                App.Settings.Save();
            }
        }

        internal void ChkShowLevelInPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShowLevelInPresence = chk.IsChecked == true;
                // Update presence immediately to reflect change
                App.DiscordRpc?.UpdateLevel(App.Settings.Current.PlayerLevel);
            }
        }

        internal async void ChkAllowDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.AllowDiscordDm = isChecked;

                // Sync profile tab checkbox
                if (DiscordTab?.ChkDiscordTabAllowDm != null && DiscordTab.ChkDiscordTabAllowDm != chk)
                    DiscordTab.ChkDiscordTabAllowDm.IsChecked = isChecked;

                // Sync immediately so the setting takes effect on the leaderboard
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }

                // Refresh profile viewer to show/hide DM button
                if (DiscordTab.ProfileCardWrapper?.Visibility == Visibility.Visible)
                {
                    // Update the Discord button visibility based on new setting
                    if (DiscordTab.BtnProfileDiscord != null)
                    {
                        if (isChecked && !string.IsNullOrEmpty(App.Discord?.UserId))
                        {
                            DiscordTab.BtnProfileDiscord.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            DiscordTab.BtnProfileDiscord.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
        }

        internal async void ChkShareProfilePicture_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.ShareProfilePicture = isChecked;

                // Sync profile tab checkbox
                if (DiscordTab?.ChkDiscordTabSharePfp != null && DiscordTab.ChkDiscordTabSharePfp != chk)
                    DiscordTab.ChkDiscordTabSharePfp.IsChecked = isChecked;

                // Sync immediately so the setting takes effect
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        #region Goon Game sharing toggles

        // Goon Game consent flags (docs/GOON_DISCORD_CONTRACT.md §1/§2). Sharer-only:
        // each flag governs what THIS user exposes to the current opponent. All default off.
        //
        // The two SHARE flags push to the server ON CHANGE (RemoteControl precedent,
        // MainWindow.RemoteControl.cs:275-284) so a revoke lands before the next duel
        // instead of waiting for the next scheduled sync. Each handler no-ops when the
        // value is unchanged, because LoadDiscordTabState() assigns IsChecked
        // programmatically and that re-fires Checked/Unchecked.
        //
        // GoonRichPresence is LOCAL-ONLY — never synced.

        internal async void ChkGoonShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null || sender is not CheckBox chk) return;
            var isChecked = chk.IsChecked == true;
            if (App.Settings.Current.GoonShareAvatar == isChecked) return; // programmatic load echo

            App.Settings.Current.GoonShareAvatar = isChecked;
            App.Settings.Save();
            App.Logger?.Information("[GoonShare] avatar sharing changed: {Enabled}", isChecked);

            // Push-on-change: revoking must reach the server (it drops the cached avatar
            // bytes at sync time) without waiting for the next scheduled push.
            if (App.ProfileSync != null)
            {
                try { await App.ProfileSync.SyncProfileAsync(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GoonShare] immediate avatar-flag sync push failed"); }
            }
        }

        internal async void ChkGoonShareDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null || sender is not CheckBox chk) return;
            var isChecked = chk.IsChecked == true;
            if (App.Settings.Current.GoonShareDiscordDm == isChecked) return; // programmatic load echo

            App.Settings.Current.GoonShareDiscordDm = isChecked;
            App.Settings.Save();
            App.Logger?.Information("[GoonShare] opponent DMs changed: {Enabled}", isChecked);

            if (App.ProfileSync != null)
            {
                try { await App.ProfileSync.SyncProfileAsync(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GoonShare] immediate dm-flag sync push failed"); }
            }
        }

        internal void ChkGoonRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null || sender is not CheckBox chk) return;
            var isChecked = chk.IsChecked == true;
            if (App.Settings.Current.GoonRichPresence == isChecked) return; // programmatic load echo

            App.Settings.Current.GoonRichPresence = isChecked;
            App.Settings.Save();
            App.Logger?.Information("[GoonShare] Goon Game rich presence changed: {Enabled}", isChecked);
            // Deliberately NO sync push — this flag never leaves the machine.
        }

        #endregion

        internal async void ChkShowOnlineStatus_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.ShowOnlineStatus = isChecked;

                // Sync profile tab checkbox
                if (DiscordTab?.ChkDiscordTabShowOnline != null && DiscordTab.ChkDiscordTabShowOnline != chk)
                    DiscordTab.ChkDiscordTabShowOnline.IsChecked = isChecked;

                App.Logger?.Information("Online status visibility changed: {Visible}", isChecked);

                // Sync immediately so the setting takes effect
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        internal void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Patreon page");
            }
        }

        private void OnPatreonTierChanged(object? sender, PatreonTier tier)
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePatreonUI();
                UpdateUnlockablesVisibility(App.Settings?.Current?.PlayerLevel ?? 1);
                MaybeShowPremiumCelebration();
            });
        }

        /// <summary>
        /// One-time "premium unlocked" celebration card. Re-reads the combined entitlement
        /// (Patreon tier + whitelist + cached grace + SubscribeStar) rather than trusting any
        /// single event's tier argument, because several grant paths - cached state restored in
        /// the ctor, the 14-day grace window, V2-linked accounts - never raise TierChanged at
        /// all. Suppressed (unspent) while a session, the guided tour, or the update dialog is
        /// on screen; MainWindow_Loaded re-checks on every launch, so suppression only delays
        /// the card, never burns it.
        /// </summary>
        private void MaybeShowPremiumCelebration()
        {
            try
            {
                if (App.Patreon?.HasPremiumAccess != true) return;
                if (_sessionEngine?.IsRunning == true) return;
                if (App.IsUpdateDialogActive || IsStartupDialogShowing) return;
                FeatureIntroPopup.ShowCelebrationIfFirstTime(this);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Premium celebration hook failed");
            }
        }

        private void InitializePatreonTab()
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Subscribe to Patreon tier changes
            if (App.Patreon != null)
            {
                App.Patreon.TierChanged += OnPatreonTierChanged;
            }

            // SubscribeStar is the third login provider and it OR's into the canonical premium gate
            // (PatreonService.HasPremiumAccess), but its own init was never actually called from
            // here despite MainWindow.SubscribeStar.cs saying it should be. Consequence:
            // App.SubscribeStar.TierChanged had no subscriber at all, so a SubscribeStar patron's
            // subscription resolving mid-session refreshed nothing - the premium UI (and the intake
            // pass with it) stayed on whatever it painted at startup. One line, and it is the hook
            // that file was written to receive.
            InitializeSubscribeStarTab();

            // Initialize companion settings. Show/mute are the hero's own chips now and read
            // AvatarEnabled / AvatarMuted straight out of settings (design §6: "kept — Z1 quick
            // actions"), so there is nothing to seed for them here.
            // "Muted" is now its own flag, not the inverse of the enable (AppSettings.SubAudioMuted).
            CompanionTab.ChkMuteWhispersCompanion.IsChecked = settings.SubAudioMuted;
            CompanionTab.ChkVoiceLinesCompanion.IsChecked = settings.CompanionVoiceLinesMuted;
            CompanionTab.SliderIdleIntervalCompanion.Value = settings.IdleGiggleIntervalSeconds;
            CompanionTab.TxtIdleIntervalCompanion.Text = $"{settings.IdleGiggleIntervalSeconds}s";
            CompanionTab.SliderBubbleDurationCompanion.Value = settings.BubbleDurationSeconds;
            CompanionTab.TxtBubbleDurationCompanion.Text = $"{(int)settings.BubbleDurationSeconds}s";

            // Awareness Mode settings (free for all users). The on/off checkbox became Z5's dial
            // (design §6: "toggle superseded by Z5 dial"), which reads the same two settings; the
            // cooldowns below are the "kept" half and still live in the Workshop.
            var awarenessAvailable = true;
            CompanionTab.SliderAwarenessCooldown.Value = settings.AwarenessReactionCooldownSeconds;
            CompanionTab.TxtAwarenessCooldown.Text = $"{settings.AwarenessReactionCooldownSeconds}s";
            CompanionTab.SliderAwarenessCooldownMax.Value = settings.AwarenessCooldownMaxSeconds;
            CompanionTab.TxtAwarenessCooldownMax.Text = settings.AwarenessCooldownMaxSeconds <= 0
                ? Loc.Get("label_cooldown_off")
                : $"{settings.AwarenessCooldownMaxSeconds}s";

            // Show/hide awareness settings panel based on enabled state. Under Awareness v2 the two
            // cooldown sliders are superseded by the intensity dial in the same cell and are no longer
            // surfaced (doc 02 §8) — the SETTING is kept, because the v2 kill switch falls back to the
            // legacy pipeline that reads it, but a control that no longer drives anything is a lie.
            var awarenessEnabled = awarenessAvailable && settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            CompanionTab.AwarenessSettingsPanel.Visibility =
                awarenessEnabled && !settings.UseAwarenessV2 ? Visibility.Visible : Visibility.Collapsed;
            CompanionTab.AwarenessCell.SyncIntensity();

            // Trigger Mode settings (free for all)
            CompanionTab.ChkTriggerModeCompanion.IsChecked = settings.TriggerModeEnabled;
            CompanionTab.SliderTriggerIntervalCompanion.Value = settings.TriggerIntervalSeconds;
            CompanionTab.TxtTriggerIntervalCompanion.Text = $"{settings.TriggerIntervalSeconds}s";
            CompanionTab.TriggerSettingsPanelCompanion.Visibility = settings.TriggerModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Restore the Companion accordion open/closed state (sections default to collapsed)
            RestoreCompanionSectionStates();

            // (The old "hide avatar if disabled" guard is gone: it ran in the ctor, before
            // MainWindow_Loaded builds the tube, so it was always a no-op. #888 moved the decision
            // to the only place it can work — the tube is not created at all when AvatarEnabled
            // is false.)

            UpdatePatreonUI();
        }

        // ChkAvatarEnabled_Changed / ChkMuteAvatar_Changed are gone with the checkboxes they
        // read (design §6: "kept — Z1 quick-action chips"). Their bodies live on as
        // SetAvatarEnabled / SetAvatarMuted in MainWindow.CompanionRoom.cs, which take the value
        // instead of fishing it back out of a control.

        internal void BtnDetachCompanion_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("detach_companion"); } catch { }
            if (_avatarTubeWindow == null) return;

            _avatarTubeWindow.ToggleDetached();

            // The hero's Detach chip carries a fixed label and the tooltip reads the status text,
            // so only the (hidden, compat) status line is written now.
            CompanionTab.TxtDetachStatusCompanion.Text = _avatarTubeWindow.IsDetached
                ? Loc.Get("label_floating_freely_drag_to_reposition")
                : Loc.Get("label_anchored_to_window");
        }

        internal void BtnCustomizeCompanion_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("customize_companion"); } catch { }
            var dialog = new CompanionPromptEditorDialog
            {
                Owner = this
            };
            dialog.ShowDialog();

            // Refresh UI to reflect any prompt changes
            UpdateCommunityPromptsUI();
        }

        // BtnResetCompanionMemory is gone (design §6: "superseded — absorbed by Z3's Forget
        // everything"). Its two scopes were split rather than merged, per doc 01 §2.4:
        //   · the DIARY's "Forget everything…" is THE wipe — facts, profile and conversation,
        //     through CompanionBrain.Forget, behind an in-voice two-step confirm;
        //   · this button's narrower job — drop the thread, keep everything she knows — survives as
        //     the Engine Room's "clear conversation" (ClearCompanionConversation, in
        //     MainWindow.CompanionRoom.cs), which is the same ForgetConversation call this made.
        // Neither is orphaned and neither is hardcoded English any more.

        internal void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CompanionPhraseEditorDialog { Owner = this };
            dialog.ShowDialog();
            UpdatePhraseCountDisplay();
        }

        private void UpdatePhraseCountDisplay()
        {
            var count = App.CompanionPhrases?.GetActivePhraseCount() ?? 0;
            CompanionTab.TxtPhraseCount.Text = $"{count} active";
        }

        /// <summary>
        /// Re-applies the remembered drawer state.
        ///
        /// <para>The five accordions became two drawers (Engine Room and Workshop), so the five
        /// remembered keys collapse to two. The old per-section keys stay in
        /// <c>CompanionSectionOpen</c> untouched — the setting is a plain dictionary and nothing
        /// reads the stale entries, so a user who downgrades gets their accordions back.</para>
        ///
        /// <para>Both drawers default CLOSED, which is the whole point of Z7/Z8: the plumbing
        /// stopped being the front door.</para>
        /// </summary>
        private void RestoreCompanionSectionStates()
        {
            var map = App.Settings?.Current?.CompanionSectionOpen;
            var room = CompanionRoom;
            if (map == null || room == null) return;
            if (map.TryGetValue(CompanionEngineDrawerKey, out var engineOpen)) room.EngineVm.IsExpanded = engineOpen;
            if (map.TryGetValue(CompanionWorkshopDrawerKey, out var shopOpen)) room.WorkshopVm.IsExpanded = shopOpen;
        }

        /// <summary>Settings key for the Engine Room drawer's remembered state.</summary>
        internal const string CompanionEngineDrawerKey = "EngineRoom";

        /// <summary>Settings key for the Workshop drawer's remembered state.</summary>
        internal const string CompanionWorkshopDrawerKey = "Workshop";

        /// <summary>
        /// Persists the two drawers' open/closed state. Called on tab hide and on shutdown rather
        /// than per-toggle: the drawers are viewmodel state, not Expander events, and one write when
        /// the user leaves is cheaper than one per click.
        /// </summary>
        internal void PersistCompanionDrawerStates()
        {
            var map = App.Settings?.Current?.CompanionSectionOpen;
            var room = CompanionRoom;
            if (map == null || room == null) return;
            map[CompanionEngineDrawerKey] = room.EngineVm.IsExpanded;
            map[CompanionWorkshopDrawerKey] = room.WorkshopVm.IsExpanded;
            App.Settings?.Save();
        }

        internal void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || CompanionTab.TxtIdleIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 120);
            CompanionTab.TxtIdleIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.IdleGiggleIntervalSeconds = value;
            App.Settings.Save();
            _avatarTubeWindow?.RestartIdleTimer();
        }

        internal void SliderBubbleDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || CompanionTab.TxtBubbleDurationCompanion == null) return;

            var slider = sender as Slider;
            var value = slider?.Value ?? 2.0;
            CompanionTab.TxtBubbleDurationCompanion.Text = $"{(int)value}s";
            App.Settings.Current.BubbleDurationSeconds = value;
            App.Settings.Save();
        }

        // ============================================================
        // TRIGGER MODE (Free for all)
        // ============================================================

        internal void ChkTriggerMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            CompanionTab.TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            App.Settings.Current.TriggerModeEnabled = isEnabled;
            App.Settings.Save();

            // Restart trigger timer on avatar window
            _avatarTubeWindow?.RestartTriggerTimer();

            App.Logger?.Information("Trigger Mode {State}", isEnabled ? "enabled" : "disabled");
        }

        /// <summary>
        /// Sync the Trigger Mode UI when changed from avatar context menu
        /// </summary>
        public void SyncTriggerModeUI(bool isEnabled)
        {
            CompanionTab.ChkTriggerModeCompanion.IsChecked = isEnabled;
            CompanionTab.TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void SliderTriggerInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || CompanionTab.TxtTriggerIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 60);
            CompanionTab.TxtTriggerIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.TriggerIntervalSeconds = value;

            // Restart trigger timer with new interval
            _avatarTubeWindow?.RestartTriggerTimer();
        }

        internal void BtnEditTriggers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Convert List<string> to Dictionary<string, bool> for the editor
                // Use Distinct() to handle any duplicate triggers that could crash ToDictionary
                var triggers = App.Settings.Current.CustomTriggers ?? new List<string>();
                var triggerDict = triggers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(t => t, _ => true);

                // Note: We no longer auto-populate defaults when empty.
                // Users can add triggers manually via the editor if they want them.
                // This fixes the bug where removed triggers would reappear.

                var dialog = new TextEditorDialog("Trigger Phrases", triggerDict);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.ResultData != null)
                {
                    // Get only enabled triggers
                    var newTriggers = dialog.ResultData
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    App.Settings.Current.CustomTriggers = newTriggers;
                    App.Settings.Save();
                    App.Logger?.Information("Updated {Count} custom triggers", newTriggers.Count);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open trigger editor");
                MessageBox.Show($"Error opening trigger editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ChkAwarenessMode_Changed went with its checkbox: Z5's three-stop dial calls
        // SetAwarenessEnabled (MainWindow.CompanionRoom.cs), which owns the service start/stop and the
        // Workshop panel's visibility. The auto-consent that used to live there is gone — Awareness v2
        // raises AwarenessConsentDialog the first time her eyes are opened (doc 02 §6.3), and a decline
        // leaves the setting untouched.

        internal void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionTab.TxtPrivacyDetails.Visibility == Visibility.Collapsed)
            {
                CompanionTab.TxtPrivacyDetails.Visibility = Visibility.Visible;
                CompanionTab.BtnPrivacySpoiler.Content = Loc.Get("btn_hide");
            }
            else
            {
                CompanionTab.TxtPrivacyDetails.Visibility = Visibility.Collapsed;
                CompanionTab.BtnPrivacySpoiler.Content = Loc.Get("btn_click_to_reveal");
            }
        }

        internal void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || CompanionTab.TxtAwarenessCooldown == null) return;

            var value = (int)CompanionTab.SliderAwarenessCooldown.Value;
            CompanionTab.TxtAwarenessCooldown.Text = $"{value}s";
            App.Settings.Current.AwarenessReactionCooldownSeconds = value;
            App.Settings.Save();
        }

        internal void SliderAwarenessCooldownMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || CompanionTab.TxtAwarenessCooldownMax == null) return;

            // 0 (or below the base cooldown) = randomization off; the fixed base cooldown is used.
            var value = (int)CompanionTab.SliderAwarenessCooldownMax.Value;
            CompanionTab.TxtAwarenessCooldownMax.Text = value <= 0 ? Loc.Get("label_cooldown_off") : $"{value}s";
            App.Settings.Current.AwarenessCooldownMaxSeconds = value;
            App.Settings.Save();
        }

        // ============================================================
        // COMPANION TAB — Hero + AI Brain redesign (v5.9)
        // ============================================================

        /// <summary>
        /// The hero's Switch chip. The roster tray it used to toggle is a Workshop pigeonhole now
        /// (design §6: "kept — Z8 Roster, opened by the hero's Switch chip"), so this opens the
        /// drawer and scrolls the roster into view instead of flipping a Visibility.
        ///
        /// <para>Deliberately not a toggle any more: the old one hid the tray on a second click,
        /// which is why the tutorial had to reach past it to reveal the roster.</para>
        /// </summary>
        internal void BtnSwitchCompanion_Click(object sender, RoutedEventArgs e)
            => RevealCompanionWorkshopCell(Views.Controls.Companion.CompanionRoomAnchors.WorkshopRosterCell);

        /// <summary>Opens the Workshop drawer on a named pigeonhole. Safe before the tab exists.</summary>
        internal void RevealCompanionWorkshopCell(string? cellKey)
        {
            try { CompanionTab?.Room?.RevealWorkshop(cellKey); }
            catch (Exception ex) { App.Logger?.Debug("RevealCompanionWorkshopCell: {E}", ex.Message); }
        }

        // The four provider radios became Z7's segmented row (design §6: "demoted — Z7 Engine
        // Room, verbatim"). SetAiProviderMode (MainWindow.CompanionRoom.cs) writes the same two
        // settings these four handlers wrote; the per-provider config PANELS they used to show and
        // hide are gone too — the Engine Room shows the panel that belongs to the selected segment,
        // declaratively, which is what those twelve Visibility writes were doing by hand.
        //
        // The one behaviour that was NOT just visibility is kept here: picking Local offers the
        // Ollama setup wizard when nothing answers on the host.

        /// <summary>Called by <see cref="SetAiProviderMode"/> after the settings write.</summary>
        private void OnCompanionProviderSelected(Views.Controls.Companion.CompanionProviderMode mode)
        {
            if (mode != Views.Controls.Companion.CompanionProviderMode.LocalOllama) return;

            // First-time opt-in: if Ollama isn't reachable, offer the setup wizard so the user
            // doesn't have to hunt for the button. Detect runs on a 2s timeout.
            _ = MaybeOfferLocalAiSetupAsync();
        }

        private async Task MaybeOfferLocalAiSetupAsync()
        {
            try
            {
                var model = App.Settings?.Current?.CompanionPrompt?.AiModel;
                var snap = await Services.AIService.OllamaSetupService.DetectAsync(targetModel: model);
                if (snap.Status == Services.AIService.OllamaSetupService.InstallStatus.Ready) return;

                var result = MessageBox.Show(
                    this,
                    Loc.Get("dialog_local_ai_setup_offer_body"),
                    Loc.Get("dialog_local_ai_setup_offer_title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) LaunchLocalAiSetupWizard();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: detect-on-local-toggle failed");
            }
        }

        internal void BtnSetupLocalAi_Click(object sender, RoutedEventArgs e)
        {
            LaunchLocalAiSetupWizard();
        }

        /// <summary>
        /// Lab tab "AI Companion Effects and Memory" notice button — switches to the
        /// Companion tab so the user can see the provider controls, then launches the setup
        /// wizard. Effects need a local LLM (cloud is stateless + has no command-output
        /// capability).
        ///
        /// <para>Those controls are inside a collapsed drawer now, so this opens it. Sending
        /// someone to a tab where the thing they were just promised sits behind a shut Expander
        /// is the deep link failing quietly.</para>
        /// </summary>
        internal void BtnLabEffectsSetupLocal_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
            try { CompanionTab?.Room?.RevealEngineRoom(); }
            catch (Exception ex) { App.Logger?.Debug("BtnLabEffectsSetupLocal_Click: {E}", ex.Message); }
            LaunchLocalAiSetupWizard();
        }

        // ChkSlutMode_Changed went with its checkbox: Z4's flame toggle sets SetSlutMode
        // (MainWindow.CompanionRoom.cs), which still carries the CCBill explicit-content
        // acknowledgement gate and still reports back what actually took effect when the
        // acknowledgement is cancelled.

        private void LaunchLocalAiSetupWizard()
        {
            var wizard = new LocalAiSetupWizard { Owner = this };
            var ok = wizard.ShowDialog() == true;
            if (ok && wizard.LocalAiReady)
            {
                var prompt = App.Settings?.Current?.CompanionPrompt;
                if (prompt != null)
                {
                    prompt.AiModel = wizard.SelectedModel;
                    App.Settings?.Save();
                }
                SetAiProviderMode(Views.Controls.Companion.CompanionProviderMode.LocalOllama);
                UpdateAiBrainPills();
            }
        }

        // The five provider text fields (Ollama model/host, BYO endpoint/model/key, daily limit)
        // moved into the Engine Room, where they are two-way bound to EngineRoomRuntimeVm and write
        // the same CompanionPromptSettings properties these LostFocus handlers wrote. The API key
        // is the one that changed shape rather than address: it is a PasswordBox now, pushed one-way
        // through SetCustomApiKey (MainWindow.CompanionRoom.cs), so the stored secret is never read
        // back into a control. The daily limit is a prompt dialog for the same reason it was a
        // TextBox: it is a number, and it is the only one on that drawer.

        /// <summary>
        /// Commits whatever the user is currently typing.
        ///
        /// <para>The Engine Room's provider fields bind with the default LostFocus trigger, exactly
        /// as the old TextBoxes did — a settings write per keystroke on an endpoint URL is a JSON
        /// save per keystroke. Clicking Test normally moves focus and commits on the way, but the
        /// old code carried a hand-written flush for the cases where it does not (keyboard
        /// activation, a click that lands while the box is mid-IME), and dropping it here would
        /// have made "Test" quietly probe the previous endpoint.</para>
        /// </summary>
        private static void CommitFocusedEdit()
        {
            try
            {
                if (Keyboard.FocusedElement is TextBox box)
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
            catch (Exception ex) { App.Logger?.Debug("CommitFocusedEdit: {E}", ex.Message); }
        }

        /// <summary>
        /// Probes the configured Ollama host. Same request, same 3s timeout; the result lands in
        /// the Engine Room's status line rather than the AI Brain card's TxtAiHealthStatus, which
        /// the drawer replaced. The host comes from settings rather than from a TextBox, which is
        /// why the pending edit is committed first.
        /// </summary>
        internal async void BtnTestOllamaConnection_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedEdit();

            var engine = CompanionRoom?.EngineVm;
            if (engine == null) return;

            var host = (App.Settings?.Current?.CompanionPrompt?.AiOllamaHost ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host))
            {
                engine.SetStatus(Loc.Get("label_status_failed"), healthy: false);
                return;
            }
            var url = host.TrimEnd('/') + "/api/tags";

            engine.SetStatus(Loc.Get("label_status_testing"), healthy: false);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var http = new HttpClient();
                var resp = await http.GetAsync(url, cts.Token);
                sw.Stop();
                engine.SetStatus(resp.IsSuccessStatusCode
                    ? $"{Loc.Get("label_status_connected")} · {sw.ElapsedMilliseconds}ms"
                    : $"{Loc.Get("label_status_failed")} · {(int)resp.StatusCode}",
                    healthy: resp.IsSuccessStatusCode);
            }
            catch (Exception ex)
            {
                engine.SetStatus($"{Loc.Get("label_status_failed")} · {ex.GetType().Name}", healthy: false);
            }
        }

        internal void BtnOpenAiSamplerSettings_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null) return;

            var dialog = new OpenAiCompatibleSamplerSettingsDialog(s)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Save();
            }
        }

        /// <summary>
        /// Probes the BYO endpoint. The old body flushed the two text boxes into settings by hand,
        /// because a click did not always move focus in time; <see cref="CommitFocusedEdit"/> does
        /// the same job generically now, so the service still reads what the drawer shows. The API
        /// key is left alone for the same reason it always was — it is never read back into a
        /// control.
        /// </summary>
        internal async void BtnTestOpenAiConnection_Click(object sender, RoutedEventArgs e)
        {
            CommitFocusedEdit();

            var engine = CompanionRoom?.EngineVm;
            if (engine == null) return;

            engine.SetStatus(Loc.Get("label_status_testing"), healthy: false);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var service = new Services.AIService.OpenAiCompatibleService();
                var diag = await service.TestEndpointAsync(cts.Token);

                if (diag.Success)
                {
                    engine.SetStatus($"{Loc.Get("label_status_connected")} · {diag.ElapsedMs ?? 0}ms", healthy: true);
                }
                else
                {
                    var codePart = diag.HttpStatusCode.HasValue ? $" (HTTP {diag.HttpStatusCode.Value})" : string.Empty;
                    engine.SetStatus($"{Loc.Get("label_status_failed")} · {diag.Message}{codePart}", healthy: false);
                }
            }
            catch (Exception ex)
            {
                engine.SetStatus($"{Loc.Get("label_status_failed")} · {ex.GetType().Name}", healthy: false);
                App.Logger?.Warning(ex, "MainWindow: OpenAI-compatible test connection failed");
            }
        }

        /// <summary>
        /// Wipes the local AI's persisted chat history (in-memory + on-disk).
        /// Cloud provider has no memory, so this is a local-only action.
        /// </summary>
        internal void BtnClearChatMemory_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                Loc.Get("dialog_forget_everything_prompt"),
                Loc.Get("btn_forget_everything"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // The brain owns the transcript for every provider since Train 1; clearing only the
                // legacy local file would leave companion/session.json (and the live log) intact.
                App.Brain?.ForgetConversation();

                if (App.Ai is Services.AIService.AiServiceStrategy strategy)
                {
                    strategy.ClearLocalHistory();
                }

                // Also clear the live actions feed so the visual state matches "fresh slate".
                App.AiLiveActions.Clear();
                UpdateLiveActionsPlaceholder();

                MessageBox.Show(
                    Loc.Get("dialog_forget_everything_done"),
                    Loc.Get("btn_forget_everything"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnClearChatMemory_Click failed");
            }
        }

        internal void ChkChatMemoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.ChkChatMemoryEnabled == null) return;
            var on = CompanionTab.ChkChatMemoryEnabled.IsChecked == true;
            if (s.ChatMemoryEnabled == on) return;
            s.ChatMemoryEnabled = on;
            App.Settings?.Save();

            // Turning memory off should wipe what's already saved — not just stop persisting new
            // turns. That promise now spans companion/session.json (every provider, since Train 1)
            // AND the live turn log the brain holds, not just the legacy local-Ollama file: a cloud
            // user unticking this box has a transcript on disk that never existed before Train 1,
            // and it is exactly what they are asking to remove.
            if (!on)
            {
                try { App.Brain?.ForgetConversation(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "ChkChatMemoryEnabled_Changed: brain wipe failed"); }

                if (App.Ai is Services.AIService.AiServiceStrategy strategy)
                {
                    try { strategy.ClearLocalHistory(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "ChkChatMemoryEnabled_Changed: ClearLocalHistory failed"); }
                }
            }
        }

        /// <summary>
        /// The master "she may drive effects" switch, on the Companion door's permissions card
        /// since Phase 5 of the UX restructure.
        ///
        /// <para>It carries the tier check the move made necessary. On the Lab tab the switch was
        /// gated by geography — the whole page sat under LabSmokescreen — and the only thing left
        /// after it was the force-clear in UpdateUnlockablesVisibility, which is a REPAIR (it undoes
        /// a setting that outlived its entitlement) and not a gate (a Free account could still tick
        /// the box and have it stick until the next refresh). The Companion door is Free/Tier 1, so
        /// the bar has to be here.</para>
        ///
        /// <para>Only turning it ON is gated: unticking must always work, whatever the account.</para>
        /// </summary>
        internal void ChkCapEffects_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.ChkCapEffects == null) return;
            var on = CompanionTab.ChkCapEffects.IsChecked == true;

            if (on && !TierGate.DemandLab(Loc.Get("lab_ai_effects_memory_title")))
            {
                // Put the switch back without re-entering this handler, and leave the setting
                // untouched — a refusal must not write.
                var wasLoading = _isLoading;
                _isLoading = true;
                try { CompanionTab.ChkCapEffects.IsChecked = false; }
                finally { _isLoading = wasLoading; }
                return;
            }

            s.AllowAiToControlEffects = on;
            if (CompanionTab.EffectPermsPanel != null) CompanionTab.EffectPermsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            App.Settings.Save();
        }

        internal void ChkAllowEffect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null) return;
            var on = cb.IsChecked == true;
            switch (cb.Tag as string)
            {
                case "Flash":       s.AllowAiFlash = on; break;
                case "Video":       s.AllowAiVideo = on; break;
                case "Audio":       s.AllowAiAudio = on; break;
                case "Bubbles":     s.AllowAiBubbles = on; break;
                case "Subliminal":  s.AllowAiSubliminal = on; break;
                case "Overlay":     s.AllowAiOverlay = on; break;
                case "LockCard":    s.AllowAiLockCard = on; break;
                case "Bounce":      s.AllowAiBounce = on; break;
                case "Haptic":      s.AllowAiHaptic = on; break;
                case "GetBackToMe": s.AllowAiGetBackToMe = on; break;
            }
            App.Settings.Save();
        }

        internal void SliderMaxHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.SliderMaxHapticIntensity == null) return;
            s.MaxAiHapticIntensity = CompanionTab.SliderMaxHapticIntensity.Value;
            if (CompanionTab.TxtMaxHapticIntensity != null)
                CompanionTab.TxtMaxHapticIntensity.Text = $"{(int)(CompanionTab.SliderMaxHapticIntensity.Value * 100)}%";
            App.Settings.Save();
        }

        /// <summary>
        /// The two hero pills, plus the permissions card's "needs a local model" notice.
        ///
        /// <para>The pills are Z1's now and derive their own text from the same settings this used
        /// to format (design §6: "kept — Z1, become deep-link buttons"), and the live-actions feed's
        /// visibility is the Engine Room's. Both come from one re-read. The notice is Z7b's — same
        /// Companion page since Phase 5, one card below the drawer it points at, which is why the
        /// deep link behind its button can simply open that drawer.</para>
        /// </summary>
        private void UpdateAiBrainPills()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            CompanionRoom?.SyncBrain();

            var effectsActive = s.AiChatEnabled && ProviderSupportsEffects(s.CompanionPrompt.AiProvider);
            if (CompanionTab.LabEffectsNeedsLocalNotice != null)
                CompanionTab.LabEffectsNeedsLocalNotice.Visibility = effectsActive ? Visibility.Collapsed : Visibility.Visible;
        }

        // Providers that parse the model's response for command output and run it
        // through App.Commands (populating the Live Actions feed). Cloud is excluded —
        // it is stateless and produces no executable effects.
        private static bool ProviderSupportsEffects(Models.AiProviderType provider)
            => provider == Models.AiProviderType.Local
               || provider == Models.AiProviderType.OpenAiCompatible;

        /// <summary>
        /// The live-actions placeholder. The Engine Room binds its own list and swaps the
        /// placeholder declaratively, and it subscribes to the collection itself, so this is now
        /// only the nudge for callers that clear the feed by hand.
        /// </summary>
        private void UpdateLiveActionsPlaceholder() => CompanionRoom?.EngineVm.Sync();

        /// <summary>
        /// Populate the AI effect-permission controls from settings.
        ///
        /// <para>The name is a fossil: since Phase 5 of the UX restructure these controls are Z7b of
        /// the Companion room, not the Lab tab. The method survives verbatim because the bug it was
        /// written for survives the move — the grid was only ever synced when the Companion tab was
        /// visited, so after a restart it showed XAML defaults while the persisted AllowAi* values
        /// kept gating effects, and videos fired that the UI said were off (#512). Now that the grid
        /// IS on the Companion tab there is exactly one caller (SyncAiBrainUI, from
        /// SyncCompanionTabUI, from ShowTab("companion")), which is the point: the sync and the
        /// surface can no longer be on different pages.</para>
        ///
        /// <para>Ends on the tier gate so the lockband and the values it covers are always painted
        /// in the same pass — a grid showing a legit T2 user's ticked boxes under a lock, or a
        /// lapsed one's under none, would be worse than either state alone.</para>
        /// </summary>
        internal void SyncLabEffectPermsUI()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                if (CompanionTab.ChkCapEffects != null)
                    CompanionTab.ChkCapEffects.IsChecked = s.CompanionPrompt.AllowAiToControlEffects;
                if (CompanionTab.EffectPermsPanel != null)
                    CompanionTab.EffectPermsPanel.Visibility = s.CompanionPrompt.AllowAiToControlEffects
                        ? Visibility.Visible : Visibility.Collapsed;

                // Effect permission grid
                if (CompanionTab.ChkAllowFlash != null)       CompanionTab.ChkAllowFlash.IsChecked       = s.CompanionPrompt.AllowAiFlash;
                if (CompanionTab.ChkAllowVideo != null)       CompanionTab.ChkAllowVideo.IsChecked       = s.CompanionPrompt.AllowAiVideo;
                if (CompanionTab.ChkAllowAudio != null)       CompanionTab.ChkAllowAudio.IsChecked       = s.CompanionPrompt.AllowAiAudio;
                if (CompanionTab.ChkAllowBubbles != null)     CompanionTab.ChkAllowBubbles.IsChecked     = s.CompanionPrompt.AllowAiBubbles;
                if (CompanionTab.ChkAllowSubliminal != null)  CompanionTab.ChkAllowSubliminal.IsChecked  = s.CompanionPrompt.AllowAiSubliminal;
                if (CompanionTab.ChkAllowOverlay != null)     CompanionTab.ChkAllowOverlay.IsChecked     = s.CompanionPrompt.AllowAiOverlay;
                if (CompanionTab.ChkAllowLockCard != null)    CompanionTab.ChkAllowLockCard.IsChecked    = s.CompanionPrompt.AllowAiLockCard;
                if (CompanionTab.ChkAllowBounce != null)      CompanionTab.ChkAllowBounce.IsChecked      = s.CompanionPrompt.AllowAiBounce;
                if (CompanionTab.ChkAllowHaptic != null)      CompanionTab.ChkAllowHaptic.IsChecked      = s.CompanionPrompt.AllowAiHaptic;
                if (CompanionTab.ChkAllowGetBackToMe != null) CompanionTab.ChkAllowGetBackToMe.IsChecked = s.CompanionPrompt.AllowAiGetBackToMe;

                // Max haptic intensity
                if (CompanionTab.SliderMaxHapticIntensity != null) CompanionTab.SliderMaxHapticIntensity.Value = s.CompanionPrompt.MaxAiHapticIntensity;
                if (CompanionTab.TxtMaxHapticIntensity != null)    CompanionTab.TxtMaxHapticIntensity.Text    = $"{(int)(s.CompanionPrompt.MaxAiHapticIntensity * 100)}%";

                // Chat memory toggle
                if (CompanionTab.ChkChatMemoryEnabled != null) CompanionTab.ChkChatMemoryEnabled.IsChecked = s.CompanionPrompt.ChatMemoryEnabled;
            }
            finally
            {
                _isLoading = wasLoading;
            }

            CompanionTab.AiPermissions?.ApplyTierGate();
        }

        /// <summary>
        /// Populate the AI-provider surface from settings. Called from SyncCompanionTabUI.
        ///
        /// <para>Was ~50 lines of writes into the AI Brain card's radios, panels and text boxes.
        /// Every one of those controls moved into the Engine Room, which binds the same settings —
        /// so this is a re-read plus the two things that are NOT the Engine Room's: the Lab tab's
        /// effect-permission checkboxes, and the Workshop's awareness panel visibility (the panel is
        /// "kept" while the toggle above it is superseded, so nothing else would hide it).</para>
        /// </summary>
        private void SyncAiBrainUI()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            CompanionRoom?.SyncBrain();

            SyncLabEffectPermsUI();

            if (CompanionTab.AwarenessSettingsPanel != null)
                CompanionTab.AwarenessSettingsPanel.Visibility =
                    s.AwarenessModeEnabled && !s.UseAwarenessV2 ? Visibility.Visible : Visibility.Collapsed;

            UpdateAiBrainPills();
        }





        internal void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isMuted = checkbox?.IsChecked == true;

            // Flips the dedicated MUTE, not SubAudioEnabled - see AppSettings.SubAudioMuted. Mute
            // is a comfort/safety reflex and stays available during a session; the whispers ENABLE
            // is part of the prescribed dose and is locked while one runs.
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioMuted = isMuted;
                App.Settings.Save();
            }

            // Deliberately NO LONGER syncing SettingsTab.ChkAudioWhispers: that checkbox is the
            // feature's enable and this one is a mute. They were the same flag before, so muting
            // silently turned the feature off; keeping them in step now would re-create exactly
            // the bypass this split exists to remove.

            // Sync avatar menu
            _avatarTubeWindow?.UpdateQuickMenuState();
        }

        internal async void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isPaused = checkbox?.IsChecked == true;
            await SetBrowserPaused(isPaused);
            _avatarTubeWindow?.SetBrowserPaused(isPaused);
        }

        // #846: mute only the spoken voicelines - the bubble, its text and the giggle cues stay.
        // Read at the single playback choke point (AvatarTubeWindow.Speech ShowGiggle), so it
        // covers barks, autonomy lines and voice-command responses alike.
        internal void ChkVoiceLines_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.CompanionVoiceLinesMuted = checkbox?.IsChecked == true;
                App.Settings.Save();
            }
        }

        private async Task SetBrowserPaused(bool isPaused)
        {
            try
            {
                var webView = GetBrowserWebView();
                if (webView?.CoreWebView2 != null)
                {
                    if (isPaused)
                    {
                        webView.CoreWebView2.IsMuted = true;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.pause());
                        ");
                    }
                    else
                    {
                        webView.CoreWebView2.IsMuted = false;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.play());
                        ");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to toggle browser audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Sync Quick Controls UI from avatar context menu
        /// </summary>
        public void SyncQuickControlsUI(bool? muteAvatar = null, bool? muteWhispers = null, bool? pauseBrowser = null)
        {
            _isLoading = true;
            try
            {
                // Update Companion tab controls. Mute-avatar is the hero's own chip and reads
                // AvatarMuted from settings, which the caller has already written — so it needs a
                // re-read rather than a checkbox poke.
                if (muteAvatar.HasValue) CompanionRoom?.SyncHero();
                if (muteWhispers.HasValue) CompanionTab.ChkMuteWhispersCompanion.IsChecked = muteWhispers.Value;
                if (pauseBrowser.HasValue) CompanionTab.ChkPauseBrowserCompanion.IsChecked = pauseBrowser.Value;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Sync whispers enabled state across all UI controls (Settings tab + Companion tab)
        /// </summary>
        public void SyncWhispersUI(bool enabled)
        {
            _isLoading = true;
            try
            {
                // PHASE 8: the SettingsTab.ChkAudioWhispers mirror is gone with
                // LegacyDashboardHost. The whispers ENABLE is SubAudioEnabled, whose only live
                // editor is Features/SubliminalFeatureControl - which re-reads it on
                // AppSettings.PropertyChanged, so the caller's write to s.SubAudioEnabled
                // (ApplyVoiceMute) already repaints it. Nothing to push by hand.

                // The Companion tab's box is a MUTE and no longer mirrors the enable, so it is
                // driven from SubAudioMuted rather than !enabled (AppSettings.SubAudioMuted).
                CompanionTab.ChkMuteWhispersCompanion.IsChecked =
                    App.Settings?.Current?.SubAudioMuted == true;
            }
            finally
            {
                _isLoading = false;
            }
        }

        // Remembers master volume from just before a voice "mute" so "unmute" can restore it.
        private int _preMuteMasterVolume = 70;

        /// <summary>
        /// Applies a full mute / unmute exactly as if the user flipped the master-volume, mute-whispers,
        /// and mute-avatar toggles by hand — used by the "mute" / "unmute" voice commands. Writes the
        /// settings AND refreshes every control that mirrors them (Settings tab master slider, Settings +
        /// Companion whisper toggles, Companion mute-avatar, and the avatar right-click menu), since those
        /// are synced manually (not data-bound) and otherwise wouldn't move. Remembers the pre-mute master
        /// volume so unmute restores it rather than guessing.
        /// </summary>
        public void ApplyVoiceMute(bool muted)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ApplyVoiceMute(muted))); return; }
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                if (muted)
                {
                    if (s.MasterVolume > 0) _preMuteMasterVolume = s.MasterVolume; // remember for unmute
                    s.MasterVolume = 0;
                    s.SubAudioEnabled = false; // "mute whispers" (bark / voiceline audio)
                    s.AvatarMuted = true;
                }
                else
                {
                    s.MasterVolume = _preMuteMasterVolume > 0 ? _preMuteMasterVolume : 70;
                    s.SubAudioEnabled = true;
                    s.AvatarMuted = false;
                }

                // Master-volume slider + readout — guarded so its ValueChanged handler doesn't fight us.
                _isLoading = true;
                try
                {
                    AppSettingsTab.SliderMaster.Value = s.MasterVolume;
                    if (AppSettingsTab.TxtMaster != null) AppSettingsTab.TxtMaster.Text = $"{s.MasterVolume}%";
                }
                finally { _isLoading = false; }

                // Checkboxes that mirror these settings (both tabs).
                SyncQuickControlsUI(muteAvatar: muted);
                SyncWhispersUI(enabled: !muted);

                // Avatar right-click menu (mute / mute-whispers labels) + its own _isMuted flag.
                App.AvatarWindow?.ApplyMuteState(muted);

                if (muted) { try { App.AvatarWindow?.StopVoiceLineAudio(); } catch { } }

                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ApplyVoiceMute failed");
            }
        }

        /// <summary>
        /// Nudges master volume by <paramref name="delta"/> (the "louder"/"quieter" voice commands) and
        /// moves the Settings-tab slider to match — the slider is synced manually, not bound, so a bare
        /// settings write would leave it stale just like the mute bug.
        /// </summary>
        public void AdjustMasterVolume(int delta)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => AdjustMasterVolume(delta))); return; }
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.MasterVolume = Math.Clamp(s.MasterVolume + delta, 0, 100);
                _isLoading = true;
                try
                {
                    AppSettingsTab.SliderMaster.Value = s.MasterVolume;
                    if (AppSettingsTab.TxtMaster != null) AppSettingsTab.TxtMaster.Text = $"{s.MasterVolume}%";
                }
                finally { _isLoading = false; }
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AdjustMasterVolume failed");
            }
        }

        #endregion

        #region Community Prompts

        internal async void BtnRefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CompanionTab.BtnRefreshPrompts.IsEnabled = false;
                CompanionTab.BtnRefreshPrompts.Content = "...";
                await App.CommunityPrompts?.GetAvailablePromptsAsync(forceRefresh: true);
                UpdateCommunityPromptsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh prompts: {Error}", ex.Message);
            }
            finally
            {
                CompanionTab.BtnRefreshPrompts.IsEnabled = true;
                CompanionTab.BtnRefreshPrompts.Content = Loc.Get("btn_refresh");
            }
        }

        internal void BtnDeactivatePrompt_Click(object sender, RoutedEventArgs e)
        {
            App.CommunityPrompts?.DeactivatePrompt();
            UpdateCommunityPromptsUI();
        }

        internal async void BtnBrowsePrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fetch available prompts
                var available = await App.CommunityPrompts?.GetAvailablePromptsAsync();
                if (available == null || available.Count == 0)
                {
                    ShowStyledDialog(Loc.Get("title_community_prompts"), Loc.Get("msg_no_community_prompts"), Loc.Get("btn_ok"), "");
                    return;
                }

                // Build selection list
                var installed = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();
                var notInstalled = available.Where(p => !installed.Contains(p.Id)).ToList();

                if (notInstalled.Count == 0)
                {
                    ShowStyledDialog(Loc.Get("title_community_prompts"), Loc.Get("msg_all_prompts_installed"), Loc.Get("btn_ok"), "");
                    return;
                }

                // Show simple selection (first 5)
                var message = Loc.Get("label_available_prompts");
                for (int i = 0; i < Math.Min(5, notInstalled.Count); i++)
                {
                    var p = notInstalled[i];
                    message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
                }

                if (notInstalled.Count > 5)
                    message += Loc.GetF("label_and_more_prompts", notInstalled.Count - 5);

                message += Loc.Get("label_install_first_one");

                var result = ShowStyledDialog(Loc.Get("title_browse_community_prompts"), message, Loc.Get("btn_install"), Loc.Get("btn_cancel"));
                if (result && notInstalled.Count > 0)
                {
                    var prompt = await App.CommunityPrompts?.InstallPromptAsync(notInstalled[0].Id);
                    if (prompt != null)
                    {
                        ShowStyledDialog(Loc.Get("title_installed"), Loc.GetF("msg_prompt_installed", prompt.Name), Loc.Get("btn_ok"), "");
                        UpdateCommunityPromptsUI();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error browsing prompts");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_browse_prompts", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        internal void BtnImportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = Loc.Get("title_import_community_prompt")
                };

                if (dialog.ShowDialog() == true)
                {
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        ShowStyledDialog(Loc.Get("title_imported"), Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author), Loc.Get("btn_ok"), "");
                        UpdateCommunityPromptsUI();
                    }
                    else
                    {
                        ShowStyledDialog(Loc.Get("title_error"), Loc.Get("msg_failed_to_import_prompt_invalid"), Loc.Get("btn_ok"), "");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error importing prompt");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt_error", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        internal async void BtnExportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create export dialog with name/author input
                var name = "My Custom Personality";
                var author = App.Patreon?.DisplayName ?? "Anonymous";

                var prompt = App.CommunityPrompts?.ExportCurrentSettings(name, author, "A custom AI personality.");
                if (prompt == null)
                {
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("msg_failed_to_export_settings"), Loc.Get("btn_ok"), "");
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = Loc.Get("title_export_community_prompt"),
                    FileName = $"{name.Replace(" ", "_")}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    await App.CommunityPrompts?.SavePromptToFileAsync(prompt, dialog.FileName);
                    ShowStyledDialog(Loc.Get("title_exported"), Loc.GetF("msg_prompt_exported", dialog.FileName), Loc.Get("btn_ok"), "");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error exporting prompt");
                ShowStyledDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_export_prompt", ex.Message), Loc.Get("btn_ok"), "");
            }
        }

        #endregion
    }
}
