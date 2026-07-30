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
        #region Exclusives Submenu

        private DispatcherTimer? _exclusivesMenuCloseTimer;
        // True when the popup was opened by a click — hover-leave will not
        // dismiss a pinned popup. Outside-click and Alt+Tab close it via the
        // window-level handlers in the constructor.
        private bool _exclusivesPinned;

        private void BtnPatreonExclusives_MouseEnter(object sender, MouseEventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
            if (ExclusivesSubmenuPopup.IsOpen) return;
            RefreshExclusivesSubmenuLocks();
            ExclusivesSubmenuPopup.IsOpen = true;
        }

        private void ExclusivesSubmenuPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
        }

        private void ExclusivesMenu_MouseLeave(object sender, MouseEventArgs e)
        {
            // Click-pinned popups don't dismiss on hover-out — they only close
            // via click-outside or sub-item selection.
            if (_exclusivesPinned) return;

            if (_exclusivesMenuCloseTimer == null)
            {
                _exclusivesMenuCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _exclusivesMenuCloseTimer.Tick += ExclusivesMenuCloseTick;
            }
            _exclusivesMenuCloseTimer.Stop();
            _exclusivesMenuCloseTimer.Start();
        }

        private void ExclusivesMenuCloseTick(object? sender, EventArgs e)
        {
            _exclusivesMenuCloseTimer?.Stop();
            if (_exclusivesPinned) return;
            ExclusivesSubmenuPopup.IsOpen = false;
        }

        private void ExclusivesSubmenuPopup_Closed(object? sender, EventArgs e)
        {
            _exclusivesPinned = false;
        }

        private void CloseExclusivesSubmenu()
        {
            _exclusivesPinned = false;
            ExclusivesSubmenuPopup.IsOpen = false;
        }

        private void BtnSubRemoteControl_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("remotecontrol");
        }

        private void BtnSubBambiTakeover_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("bambitakeover");
        }

        private void BtnSubHaptics_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("haptics");
        }

        private void BtnSubAwareness_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("awareness");
        }

        private void BtnSubLockdown_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("lockdown");
        }

        private void BtnSubBlinkTrainer_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("blinktrainer");
        }

        private void BtnSubSheListening_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("shelistening");
        }

        private void BtnSubGradedIntake_Click(object sender, RoutedEventArgs e)
        {
            CloseExclusivesSubmenu();
            ShowTab("gradedintake");
        }

        /// <summary>
        /// Updates "Premium" badges on the Exclusives submenu items based on the
        /// user's current subscription state. Called whenever the popup opens.
        /// </summary>
        private void RefreshExclusivesSubmenuLocks()
        {
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            var badgeVis = hasPremium ? Visibility.Collapsed : Visibility.Visible;
            if (SubBadgeRemoteControl != null) SubBadgeRemoteControl.Visibility = badgeVis;
            if (SubBadgeBambiTakeover != null) SubBadgeBambiTakeover.Visibility = badgeVis;
            if (SubBadgeHaptics != null) SubBadgeHaptics.Visibility = badgeVis;
            if (SubBadgeAwareness != null) SubBadgeAwareness.Visibility = badgeVis;
            if (SubBadgeLockdown != null) SubBadgeLockdown.Visibility = badgeVis;
            if (SubBadgeBlinkTrainer != null) SubBadgeBlinkTrainer.Visibility = badgeVis;
            if (SubBadgeSheListening != null) SubBadgeSheListening.Visibility = badgeVis;

            // The Graded Intake is the one Exclusive a free account can legitimately open: it
            // hands out one run a week (IntakePassService). Showing the padlock while that pass
            // is sitting unspent would call the door locked at the exact moment it is open, so
            // this badge follows the pass rather than the subscription. Once the pass is spent
            // the lock comes back, which is then true again until Monday.
            if (SubBadgeGradedIntake != null)
                SubBadgeGradedIntake.Visibility = (hasPremium || App.IntakePass?.IsPassAvailable == true)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

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
                PatreonTab.TxtPatreonStatus.Text = string.IsNullOrEmpty(nameToShow) ? "Connected to Patreon" : $"Welcome, {nameToShow}!";
                PatreonTab.TxtPatreonTier.Text = tier switch
                {
                    PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
                    PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
                    _ when isWhitelisted => Loc.Get("label_patreon_tier_whitelisted"),
                    _ => Loc.Get(isActivePatron ? "label_patreon_tier_patron" : "label_patreon_tier_connected")
                };
                PatreonTab.BtnPatreonLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                PatreonTab.TxtPatreonStatus.Text = Loc.Get("label_not_connected");
                PatreonTab.TxtPatreonTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");

                // Show "Link Patreon" if logged in via Discord, otherwise "Login"
                PatreonTab.BtnPatreonLogin.Content = hasUnifiedId ? "Link Patreon" : "Login";
            }

            // AI Features lock overlay - hide when user is logged in (any provider)
            CompanionTab.AiFeaturesLockOverlay.Visibility = App.HasCloudIdentity ? Visibility.Collapsed : Visibility.Visible;

            // Update feature lockboxes
            // All features are now Tier 1 (or whitelisted)
            var hasPremiumAccess = App.Patreon?.HasPremiumAccess == true;
            var level1Unlocked = hasPremiumAccess;
            var level2Unlocked = hasPremiumAccess; // Same as Level 1 now - all features at Tier 1

            // Master overlay for the entire features grid
            PatreonTab.PatreonFeaturesOverlay.Visibility = hasPremiumAccess ? Visibility.Collapsed : Visibility.Visible;

            // Keep the patron-achievements section lock + counts in sync with entitlement.
            UpdateAchievementCount();

            // Haptics - unlock for all Patreon supporters
            var hasHapticsAccess = hasPremiumAccess;
            HapticsTab.HapticsContentGrid.Opacity = hasHapticsAccess ? 1.0 : 0.3;
            HapticsTab.HapticsContentGrid.IsHitTestVisible = hasHapticsAccess;
            HapticsTab.HapticsConnectionLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsTab.HapticsFeatureLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsTab.HapticsConnectionBox.IsEnabled = hasHapticsAccess;
            HapticsTab.HapticsFeatureBox.IsEnabled = hasHapticsAccess;

            // Hide "Coming Soon" overlay for Patreon supporters
            HapticsTab.HapticsComingSoonOverlay.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;

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
            //   RefreshIntakePassTile()        - Dashboard centre tile (MainWindow.UiUpdates.cs)
            //   RefreshExclusivesSubmenuLocks() - the SubBadgeGradedIntake padlock above
            // RefreshGradedIntakeGate() is already covered by the line above.
            try
            {
                RefreshIntakePassTile();
                RefreshExclusivesSubmenuLocks();
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

            // Update AI connection status
            if (CompanionTab.TxtAiStatus != null)
            {
                var provider = App.Settings?.Current?.CompanionPrompt?.AiProvider ?? Models.AiProviderType.Cloud;
                if (App.Ai?.IsAvailable == true)
                {
                    var remaining = App.Ai.DailyRequestsRemaining;
                    if (provider == Models.AiProviderType.Cloud)
                    {
                        CompanionTab.TxtAiStatus.Text = $"AI Ready - {remaining} requests remaining today";
                    }
                    else if (remaining < 0)
                    {
                        // Unlimited (local + custom provider with limit 0) → no remaining-count suffix
                        CompanionTab.TxtAiStatus.Text = "AI Ready";
                    }
                    else
                    {
                        // Custom provider with a finite limit
                        CompanionTab.TxtAiStatus.Text = $"AI Ready - {remaining} requests remaining today";
                    }
                }
                else
                {
                    // For the custom OpenAI-compatible provider, surface a clearer error hint
                    // when the service is not available (likely bad endpoint/API key/model).
                    if (provider == Models.AiProviderType.OpenAiCompatible)
                    {
                        CompanionTab.TxtAiStatus.Text = Loc.Get("label_ai_custom_error");
                    }
                    else
                    {
                        CompanionTab.TxtAiStatus.Text = Loc.Get("label_ai_initializing");
                    }
                }
            }

            // Re-evaluate keyword triggers access (may have been disabled before Patreon validated)
            var hasKeywordAccess = KeywordTriggerService.HasAccess();
            if (PatreonTab.TxtKeywordTriggersLocked != null)
                PatreonTab.TxtKeywordTriggersLocked.Visibility = hasKeywordAccess ? Visibility.Collapsed : Visibility.Visible;
            if (PatreonTab.BtnKeywordTriggersStartStop != null)
                PatreonTab.BtnKeywordTriggersStartStop.IsEnabled = hasKeywordAccess;
            if (PatreonTab.ChkScreenOcrEnabled != null)
                PatreonTab.ChkScreenOcrEnabled.IsEnabled = hasKeywordAccess;

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
        // Account sections reparenting (App Info & Data popup)
        // ========================================================================
        // The Patreon login card, Discord login card, PatreonTab.AccountLinkingSection,
        // PatreonTab.CloudSettingsBackupSection and PatreonTab.DataPrivacySection live physically inside
        // PatreonTab's XAML tree (so their x:Name fields resolve for ~64 handler
        // references across this file). When the dashboard's "App Info & Data"
        // popup opens, we temporarily detach these Borders and attach them to the
        // popup's host StackPanel so the user can manage their account/data from
        // the dashboard. When the popup closes we put them back — the same element
        // instances, so all handler refs remain valid.

        private readonly System.Collections.Generic.List<System.Windows.FrameworkElement> _detachedAccountSections = new();

        /// <summary>
        /// Detaches the account/data sections from PatreonTab's content StackPanel
        /// and attaches them to the provided target host (usually the AppInfoFeatureControl's
        /// ExternalSectionsHost). Called when the App Info &amp; Data popup opens.
        /// </summary>
        internal void DetachAccountSectionsInto(System.Windows.Controls.Panel target)
        {
            if (target == null) return;
            if (_detachedAccountSections.Count > 0) return; // already detached

            // Order matters — this is the vertical order they'll appear in the popup.
            var toMove = new System.Windows.FrameworkElement?[]
            {
                PatreonTab.PatreonLoginCard,
                PatreonTab.SubscribeStarLoginCard,
                PatreonTab.DiscordLoginCard,
                PatreonTab.AccountLinkingSection,
                PatreonTab.CloudSettingsBackupSection,
                PatreonTab.DataPrivacySection,
                PatreonTab.SupportDevelopmentCard,
            };

            foreach (var fe in toMove)
            {
                if (fe == null) continue;

                // Detach from whichever parent it currently has (defensive:
                // could be PatreonTab.PatreonTabContent on first open, or a stale popup
                // host if a previous close didn't clean up).
                if (fe.Parent is System.Windows.Controls.Panel currentParent)
                {
                    currentParent.Children.Remove(fe);
                }
                else if (fe.Parent is System.Windows.Controls.ContentControl cc)
                {
                    cc.Content = null;
                }

                try
                {
                    target.Children.Add(fe);
                    _detachedAccountSections.Add(fe);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "DetachAccountSectionsInto: failed to attach {Name}", fe.Name);
                }
            }
        }

        /// <summary>
        /// Returns the detached account/data sections to PatreonTab so their
        /// x:Name references stay valid and they can be borrowed again next time
        /// the popup opens. Called when the App Info &amp; Data popup closes.
        /// </summary>
        internal void ReattachAccountSections()
        {
            if (_detachedAccountSections.Count == 0 || PatreonTab.PatreonTabContent == null) return;

            // Insert right after the header Grid (index 0), preserving the original order.
            int insertAt = 1;
            foreach (var fe in _detachedAccountSections)
            {
                if (fe.Parent is System.Windows.Controls.Panel currentParent)
                    currentParent.Children.Remove(fe);

                if (insertAt > PatreonTab.PatreonTabContent.Children.Count)
                    insertAt = PatreonTab.PatreonTabContent.Children.Count;
                PatreonTab.PatreonTabContent.Children.Insert(insertAt, fe);
                insertAt++;
            }
            _detachedAccountSections.Clear();
        }

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
                    PatreonTab.BtnPatreonLogin.IsEnabled = false;
                    PatreonTab.BtnPatreonLogin.Content = Loc.Get("login_connecting");

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
                        PatreonTab.BtnPatreonLogin.IsEnabled = true;
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
                    PatreonTab.BtnDiscordLogin.IsEnabled = false;
                    PatreonTab.BtnDiscordLogin.Content = Loc.Get("login_connecting");

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
                        PatreonTab.BtnDiscordLogin.IsEnabled = true;
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
                PatreonTab.TxtDiscordStatus.Text = $"Connected as {discordDisplayName}";
                PatreonTab.TxtDiscordInfo.Text = $"@{App.Discord.Username}";
                PatreonTab.BtnDiscordLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                // Check if user is logged in with another provider (has unified_id)
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                PatreonTab.TxtDiscordStatus.Text = Loc.Get("label_not_connected");
                PatreonTab.TxtDiscordInfo.Text = Loc.Get("label_link_discord_for_community_features");

                // Show "Link Discord" if logged in via Patreon, otherwise "Login"
                PatreonTab.BtnDiscordLogin.Content = hasUnifiedId ? "Link Discord" : "Login";
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
            PatreonTab.AccountLinkingSection.Visibility = showLinkingSection ? Visibility.Visible : Visibility.Collapsed;

            // Show individual buttons for unlinked providers
            PatreonTab.BtnLinkPatreon.Visibility = (hasUnifiedId && !hasLinkedPatreon) ? Visibility.Visible : Visibility.Collapsed;
            PatreonTab.BtnLinkDiscord.Visibility = (hasUnifiedId && !hasLinkedDiscord) ? Visibility.Visible : Visibility.Collapsed;

            // Show cloud settings backup section if user has a cloud identity
            PatreonTab.CloudSettingsBackupSection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
            PatreonTab.DataPrivacySection.Visibility = hasUnifiedId ? Visibility.Visible : Visibility.Collapsed;
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

            PatreonTab.BtnLinkPatreon.IsEnabled = false;
            PatreonTab.BtnLinkPatreon.Content = Loc.Get("login_connecting");

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
                PatreonTab.BtnLinkPatreon.IsEnabled = true;
                PatreonTab.BtnLinkPatreon.Content = Loc.Get("btn_link_patreon");
            }
        }

        /// <summary>
        /// Link Discord account to existing unified account
        /// </summary>
        internal async void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            PatreonTab.BtnLinkDiscord.IsEnabled = false;
            PatreonTab.BtnLinkDiscord.Content = Loc.Get("login_connecting");

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
                PatreonTab.BtnLinkDiscord.IsEnabled = true;
                PatreonTab.BtnLinkDiscord.Content = Loc.Get("btn_link_discord");
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
            }
        }

        internal void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                App.Settings.Current.DiscordShareLevelUps = chk.IsChecked == true;
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
                if (DiscordTab.ChkDiscordTabAllowDm != null && DiscordTab.ChkDiscordTabAllowDm != chk)
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
                if (DiscordTab.ChkDiscordTabSharePfp != null && DiscordTab.ChkDiscordTabSharePfp != chk)
                    DiscordTab.ChkDiscordTabSharePfp.IsChecked = isChecked;

                // Sync immediately so the setting takes effect
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        internal async void ChkShowOnlineStatus_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null && sender is CheckBox chk)
            {
                var isChecked = chk.IsChecked == true;
                App.Settings.Current.ShowOnlineStatus = isChecked;

                // Sync profile tab checkbox
                if (DiscordTab.ChkDiscordTabShowOnline != null && DiscordTab.ChkDiscordTabShowOnline != chk)
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
            });
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

            // Initialize companion settings
            CompanionTab.ChkAvatarEnabledCompanion.IsChecked = settings.AvatarEnabled;
            CompanionTab.ChkMuteAvatarCompanion.IsChecked = settings.AvatarMuted;
            CompanionTab.ChkMuteWhispersCompanion.IsChecked = !settings.SubAudioEnabled;
            CompanionTab.SliderIdleIntervalCompanion.Value = settings.IdleGiggleIntervalSeconds;
            CompanionTab.TxtIdleIntervalCompanion.Text = $"{settings.IdleGiggleIntervalSeconds}s";
            CompanionTab.SliderBubbleDurationCompanion.Value = settings.BubbleDurationSeconds;
            CompanionTab.TxtBubbleDurationCompanion.Text = $"{(int)settings.BubbleDurationSeconds}s";

            // Awareness Mode settings (free for all users)
            var awarenessAvailable = true;
            CompanionTab.ChkAwarenessMode.IsChecked = settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            CompanionTab.SliderAwarenessCooldown.Value = settings.AwarenessReactionCooldownSeconds;
            CompanionTab.TxtAwarenessCooldown.Text = $"{settings.AwarenessReactionCooldownSeconds}s";
            CompanionTab.SliderAwarenessCooldownMax.Value = settings.AwarenessCooldownMaxSeconds;
            CompanionTab.TxtAwarenessCooldownMax.Text = settings.AwarenessCooldownMaxSeconds <= 0
                ? Loc.Get("label_cooldown_off")
                : $"{settings.AwarenessCooldownMaxSeconds}s";

            // Show/hide awareness settings panel based on enabled state
            var awarenessEnabled = awarenessAvailable && settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            CompanionTab.AwarenessSettingsPanel.Visibility = awarenessEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Trigger Mode settings (free for all)
            CompanionTab.ChkTriggerModeCompanion.IsChecked = settings.TriggerModeEnabled;
            CompanionTab.SliderTriggerIntervalCompanion.Value = settings.TriggerIntervalSeconds;
            CompanionTab.TxtTriggerIntervalCompanion.Text = $"{settings.TriggerIntervalSeconds}s";
            CompanionTab.TriggerSettingsPanelCompanion.Visibility = settings.TriggerModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Restore the Companion accordion open/closed state (sections default to collapsed)
            RestoreCompanionSectionStates();

            // Hide avatar if disabled
            if (!settings.AvatarEnabled)
            {
                HideAvatarTube();
            }

            UpdatePatreonUI();
        }

        internal void ChkAvatarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            App.Settings.Current.AvatarEnabled = isEnabled;

            if (isEnabled)
            {
                ShowAvatarTube();
            }
            else
            {
                HideAvatarTube();
            }

            App.Settings.Save();
        }

        internal void BtnDetachCompanion_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("detach_companion"); } catch { }
            if (_avatarTubeWindow == null) return;

            _avatarTubeWindow.ToggleDetached();

            // Update button and status text
            if (_avatarTubeWindow.IsDetached)
            {
                CompanionTab.BtnDetachCompanionTab.Content = Loc.Get("btn_attach");
                CompanionTab.TxtDetachStatusCompanion.Text = Loc.Get("label_floating_freely_drag_to_reposition");
            }
            else
            {
                CompanionTab.BtnDetachCompanionTab.Content = Loc.Get("btn_detach");
                CompanionTab.TxtDetachStatusCompanion.Text = Loc.Get("label_anchored_to_window");
            }
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

        internal void BtnResetCompanionMemory_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reset_memory"); } catch { }
            var confirm = System.Windows.MessageBox.Show(
                this,
                "Wipe the companion's chat memory?\n\nThis clears the AI's conversation history both in memory and on disk, plus the chat log shown in the avatar bubble. " +
                "Useful when she's stuck in an old pattern (e.g. skipping links). She'll start fresh on the next message.\n\nThis can't be undone.",
                "Reset Companion Memory",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // Cloud provider is stateless, so this only does work for local Ollama users.
                // App.Ai is typed as the IAiService interface; ClearLocalHistory lives on
                // the concrete strategy (which is what's always assigned).
                (App.Ai as Services.AIService.AiServiceStrategy)?.ClearLocalHistory();

                // Drop the on-screen history too (the data store the avatar window binds to).
                _avatarTubeWindow?.ChatHistory.Clear();

                App.Logger?.Information("Companion memory reset by user");

                System.Windows.MessageBox.Show(
                    this,
                    "Done — the companion's memory is clear. Send her a new message and she'll respond with no prior context.",
                    "Reset Companion Memory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to reset companion memory");
                System.Windows.MessageBox.Show(
                    this,
                    "Couldn't fully reset the companion's memory: " + ex.Message,
                    "Reset Companion Memory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

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

        // Persist each Companion accordion's open/collapsed state so it survives a restart.
        // The x:Name is "Section<Name>"; we store under just "<Name>".
        internal void CompanionSection_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not Expander exp || string.IsNullOrEmpty(exp.Name)) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var key = exp.Name.StartsWith("Section") ? exp.Name.Substring("Section".Length) : exp.Name;
            s.CompanionSectionOpen[key] = exp.IsExpanded;
            App.Settings.Save();
        }

        // Re-apply the remembered open/collapsed state. Runs while _isLoading is true so the
        // Expanded/Collapsed handlers above no-op and we don't write back what we just read.
        private void RestoreCompanionSectionStates()
        {
            var map = App.Settings?.Current?.CompanionSectionOpen;
            if (map == null) return;
            if (CompanionTab.SectionBehaviour != null && map.TryGetValue("Behaviour", out var b)) CompanionTab.SectionBehaviour.IsExpanded = b;
            if (CompanionTab.SectionPhrases   != null && map.TryGetValue("Phrases",   out var p)) CompanionTab.SectionPhrases.IsExpanded   = p;
            if (CompanionTab.SectionContent   != null && map.TryGetValue("Content",   out var c)) CompanionTab.SectionContent.IsExpanded   = c;
            if (CompanionTab.SectionCommunity != null && map.TryGetValue("Community", out var m)) CompanionTab.SectionCommunity.IsExpanded  = m;
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

        internal void ChkAwarenessMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = CompanionTab.ChkAwarenessMode.IsChecked == true;

            // Show/hide awareness settings panel
            CompanionTab.AwarenessSettingsPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Update settings
            App.Settings.Current.AwarenessModeEnabled = isEnabled;
            App.Settings.Current.AwarenessConsentGiven = isEnabled; // Auto-consent when enabling via UI
            App.Settings.Save();

            // Start or stop the awareness service
            if (isEnabled)
            {
                App.WindowAwareness?.Start();
                App.Logger?.Information("Awareness Mode enabled via UI");
            }
            else
            {
                App.WindowAwareness?.Stop();
                App.Logger?.Information("Awareness Mode disabled via UI");
            }

            UpdateAiBrainPills();
        }

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

        internal void BtnSwitchCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionTab.CompanionRosterTray == null) return;
            CompanionTab.CompanionRosterTray.Visibility = CompanionTab.CompanionRosterTray.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        internal void RadioAiOff_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AiChatEnabled = false;
            App.Settings.Save();
            if (CompanionTab.LocalConfigPanel != null) CompanionTab.LocalConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.OpenAiCompatibleConfigPanel != null) CompanionTab.OpenAiCompatibleConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.DailyLimitPanel != null) CompanionTab.DailyLimitPanel.Visibility = Visibility.Collapsed;
            // Drop any stale Live Actions — with AI off, nothing populates this feed.
            App.AiLiveActions?.Clear();
            UpdateAiBrainPills();
        }

        internal void RadioAiCloud_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            s.AiChatEnabled = true;
            s.CompanionPrompt.AiProvider = Models.AiProviderType.Cloud;
            App.Settings.Save();
            if (CompanionTab.LocalConfigPanel != null) CompanionTab.LocalConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.OpenAiCompatibleConfigPanel != null) CompanionTab.OpenAiCompatibleConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.DailyLimitPanel != null) CompanionTab.DailyLimitPanel.Visibility = Visibility.Collapsed;
            // Cloud can't trigger effects, so prior local-session entries would be misleading.
            App.AiLiveActions?.Clear();
            SyncAiBrainUI();
        }

        internal void RadioAiLocal_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            s.AiChatEnabled = true;
            s.CompanionPrompt.AiProvider = Models.AiProviderType.Local;
            App.Settings.Save();
            if (CompanionTab.LocalConfigPanel != null) CompanionTab.LocalConfigPanel.Visibility = Visibility.Visible;
            if (CompanionTab.OpenAiCompatibleConfigPanel != null) CompanionTab.OpenAiCompatibleConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.DailyLimitPanel != null) CompanionTab.DailyLimitPanel.Visibility = Visibility.Collapsed;
            SyncAiBrainUI();

            // First-time opt-in: if Ollama isn't reachable, offer the setup wizard so
            // the user doesn't have to hunt for the button. Detect runs on a 2s timeout.
            _ = MaybeOfferLocalAiSetupAsync();
        }

        internal void RadioAiOpenAiCompatible_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            s.AiChatEnabled = true;
            s.CompanionPrompt.AiProvider = Models.AiProviderType.OpenAiCompatible;
            App.Settings.Save();
            if (CompanionTab.LocalConfigPanel != null) CompanionTab.LocalConfigPanel.Visibility = Visibility.Collapsed;
            if (CompanionTab.OpenAiCompatibleConfigPanel != null) CompanionTab.OpenAiCompatibleConfigPanel.Visibility = Visibility.Visible;
            if (CompanionTab.DailyLimitPanel != null) CompanionTab.DailyLimitPanel.Visibility = Visibility.Visible;
            SyncAiBrainUI();
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
        /// Lab tab "AI Companion Effects & Memory" notice button — switches to the
        /// Companion tab so the user can see the AI Brain provider controls, then
        /// launches the setup wizard. Effects need a local LLM (cloud is stateless +
        /// has no command-output capability).
        /// </summary>
        internal void BtnLabEffectsSetupLocal_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
            LaunchLocalAiSetupWizard();
        }

        /// <summary>
        /// Slut Mode toggle: swaps the active personality's Personality text with its
        /// SlutModePersonality variant in BambiSprite.GetSystemPrompt. Takes effect on
        /// the next chat — no restart, no provider switch needed.
        /// </summary>
        internal void ChkSlutMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null || CompanionTab.ChkSlutMode == null) return;

            var newValue = CompanionTab.ChkSlutMode.IsChecked == true;

            // CCBill AI Addendum: flipping SlutMode ON activates the explicit variant of
            // any active preset that ships a SlutModePersonality. Gate behind acknowledgement.
            if (newValue && !s.SlutModeEnabled)
            {
                var activePreset = App.Personality?.GetActivePreset();
                if (Services.ExplicitContentGate.RequiresAcknowledgement(activePreset, slutModeOn: true))
                {
                    if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt))
                    {
                        var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                        var ok = dlg.ShowDialog() == true;
                        if (!ok)
                        {
                            // Revert checkbox without re-triggering this handler.
                            _isLoading = true;
                            try { CompanionTab.ChkSlutMode.IsChecked = false; }
                            finally { _isLoading = false; }
                            return;
                        }
                        Services.ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
                    }
                }
            }

            s.SlutModeEnabled = newValue;
            App.Settings?.Save();
        }

        private void LaunchLocalAiSetupWizard()
        {
            var wizard = new LocalAiSetupWizard { Owner = this };
            var ok = wizard.ShowDialog() == true;
            if (ok && wizard.LocalAiReady)
            {
                if (CompanionTab.TxtAiModel != null) CompanionTab.TxtAiModel.Text = wizard.SelectedModel;
                if (CompanionTab.RadioAiLocal != null) CompanionTab.RadioAiLocal.IsChecked = true;
                UpdateAiBrainPills();
            }
        }

        internal void TxtAiModel_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtAiModel == null) return;
            s.AiModel = (CompanionTab.TxtAiModel.Text ?? "").Trim();
            App.Settings.Save();
        }

        internal void TxtAiHost_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtAiHost == null) return;
            s.AiOllamaHost = (CompanionTab.TxtAiHost.Text ?? "").Trim();
            App.Settings.Save();
        }

        internal void TxtOpenAiEndpoint_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtOpenAiEndpoint == null) return;
            s.OpenAiCompatibleEndpoint = (CompanionTab.TxtOpenAiEndpoint.Text ?? string.Empty).Trim();
            App.Settings.Save();
        }

        internal void TxtOpenAiModel_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtOpenAiModel == null) return;
            s.OpenAiCompatibleModel = (CompanionTab.TxtOpenAiModel.Text ?? string.Empty).Trim();
            App.Settings.Save();
        }

        internal void TxtOpenAiApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtOpenAiApiKey == null) return;

            var plain = CompanionTab.TxtOpenAiApiKey.Password ?? string.Empty;
            s.OpenAiCompatibleApiKey = Services.SecureStringHelper.Protect(plain);
            App.Settings.Save();
        }

        internal void TxtDailyLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || CompanionTab.TxtDailyLimit == null) return;

            var text = (CompanionTab.TxtDailyLimit.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                s.DailyRequestLimit = 0;
            }
            else if (int.TryParse(text, out var value) && value >= 0)
            {
                s.DailyRequestLimit = value;
            }

            App.Settings.Save();
        }

        internal async void BtnTestOllamaConnection_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionTab.TxtAiHealthStatus == null || CompanionTab.TxtAiHost == null) return;

            var host = (CompanionTab.TxtAiHost.Text ?? "").Trim();
            if (string.IsNullOrEmpty(host))
            {
                CompanionTab.TxtAiHealthStatus.Text = Loc.Get("label_status_failed");
                CompanionTab.TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            var url = host.TrimEnd('/') + "/api/tags";

            CompanionTab.TxtAiHealthStatus.Text = Loc.Get("label_status_testing");
            CompanionTab.TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Gray);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var http = new HttpClient();
                var resp = await http.GetAsync(url, cts.Token);
                sw.Stop();
                if (resp.IsSuccessStatusCode)
                {
                    CompanionTab.TxtAiHealthStatus.Text = $"{Loc.Get("label_status_connected")} · {sw.ElapsedMilliseconds}ms";
                    CompanionTab.TxtAiHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78));
                }
                else
                {
                    CompanionTab.TxtAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {(int)resp.StatusCode}";
                    CompanionTab.TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                CompanionTab.TxtAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {ex.GetType().Name}";
                CompanionTab.TxtAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
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

        internal async void BtnTestOpenAiConnection_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionTab.TxtOpenAiHealthStatus == null) return;

            // Flush any pending text box edits to settings before testing. The text boxes
            // normally persist on LostFocus, but clicking the test button does not always
            // move focus in time, so the service would otherwise read stale/empty values.
            // The API key is left alone — it is already saved on PasswordChanged, and the
            // PasswordBox is not pre-populated, so re-reading it here could wipe a saved key.
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s != null)
            {
                if (CompanionTab.TxtOpenAiEndpoint != null)
                    s.OpenAiCompatibleEndpoint = (CompanionTab.TxtOpenAiEndpoint.Text ?? string.Empty).Trim();
                if (CompanionTab.TxtOpenAiModel != null)
                    s.OpenAiCompatibleModel = (CompanionTab.TxtOpenAiModel.Text ?? string.Empty).Trim();
                App.Settings.Save();
            }

            CompanionTab.TxtOpenAiHealthStatus.Text = Loc.Get("label_status_testing");
            CompanionTab.TxtOpenAiHealthStatus.Foreground = new SolidColorBrush(Colors.Gray);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var service = new Services.AIService.OpenAiCompatibleService();
                var diag = await service.TestEndpointAsync(cts.Token);

                if (diag.Success)
                {
                    CompanionTab.TxtOpenAiHealthStatus.Text = $"{Loc.Get("label_status_connected")} · {diag.ElapsedMs ?? 0}ms";
                    CompanionTab.TxtOpenAiHealthStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78));
                }
                else
                {
                    var codePart = diag.HttpStatusCode.HasValue ? $" (HTTP {diag.HttpStatusCode.Value})" : string.Empty;
                    CompanionTab.TxtOpenAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {diag.Message}{codePart}";
                    CompanionTab.TxtOpenAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                CompanionTab.TxtOpenAiHealthStatus.Text = $"{Loc.Get("label_status_failed")} · {ex.GetType().Name}";
                CompanionTab.TxtOpenAiHealthStatus.Foreground = new SolidColorBrush(Colors.Red);
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
            if (s == null || LabTab.ChkChatMemoryEnabled == null) return;
            var on = LabTab.ChkChatMemoryEnabled.IsChecked == true;
            if (s.ChatMemoryEnabled == on) return;
            s.ChatMemoryEnabled = on;
            App.Settings?.Save();

            // Turning memory off should wipe what's already saved — not just stop persisting new turns.
            if (!on && App.Ai is Services.AIService.AiServiceStrategy strategy)
            {
                try { strategy.ClearLocalHistory(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "ChkChatMemoryEnabled_Changed: ClearLocalHistory failed"); }
            }
        }

        internal void ChkCapEffects_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null || LabTab.ChkCapEffects == null) return;
            var on = LabTab.ChkCapEffects.IsChecked == true;
            s.AllowAiToControlEffects = on;
            if (LabTab.EffectPermsPanel != null) LabTab.EffectPermsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
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
            if (s == null || LabTab.SliderMaxHapticIntensity == null) return;
            s.MaxAiHapticIntensity = LabTab.SliderMaxHapticIntensity.Value;
            if (LabTab.TxtMaxHapticIntensity != null)
                LabTab.TxtMaxHapticIntensity.Text = $"{(int)(LabTab.SliderMaxHapticIntensity.Value * 100)}%";
            App.Settings.Save();
        }

        private void UpdateAiBrainPills()
        {
            if (CompanionTab.PillAiProvider == null || CompanionTab.PillAwareness == null) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;
            var aiOn = s.AiChatEnabled;
            var provider = s.CompanionPrompt.AiProvider;
            CompanionTab.PillAiProvider.Text = !aiOn ? Loc.Get("label_ai_status_pill_off")
                                : provider == Models.AiProviderType.Local ? Loc.Get("label_ai_status_pill_local")
                                : provider == Models.AiProviderType.OpenAiCompatible ? Loc.Get("label_ai_status_pill_custom")
                                : Loc.Get("label_ai_status_pill_cloud");
            CompanionTab.PillAwareness.Text = s.AwarenessModeEnabled
                                ? Loc.Get("label_awareness_pill_on")
                                : Loc.Get("label_awareness_pill_off");

            // Effects work with any provider that parses + executes command output
            // (Local and OpenAI-compatible); cloud is stateless and has none. Show the
            // Live Actions feed in the AI Brain panel and hide the "needs local" notice
            // in the Lab effects card whenever the user is on an effects-capable provider.
            var effectsActive = aiOn && ProviderSupportsEffects(provider);
            if (CompanionTab.LiveActionsContainer != null)
                CompanionTab.LiveActionsContainer.Visibility = effectsActive ? Visibility.Visible : Visibility.Collapsed;
            if (LabTab.LabEffectsNeedsLocalNotice != null)
                LabTab.LabEffectsNeedsLocalNotice.Visibility = effectsActive ? Visibility.Collapsed : Visibility.Visible;
        }

        // Providers that parse the model's response for command output and run it
        // through App.Commands (populating the Live Actions feed). Cloud is excluded —
        // it is stateless and produces no executable effects.
        private static bool ProviderSupportsEffects(Models.AiProviderType provider)
            => provider == Models.AiProviderType.Local
               || provider == Models.AiProviderType.OpenAiCompatible;

        private void UpdateLiveActionsPlaceholder()
        {
            if (CompanionTab.TxtLiveActionsPlaceholder == null) return;
            CompanionTab.TxtLiveActionsPlaceholder.Visibility = (App.AiLiveActions?.Count ?? 0) == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Populate the Lab tab's AI effect-permission controls from settings. These
        /// checkboxes live on the Lab tab but were only synced when the Companion tab
        /// was visited, so after a restart the Lab tab showed XAML defaults while the
        /// persisted AllowAi* values kept gating effects — videos fired that the UI
        /// said were off (#512). Called from SyncAiBrainUI and on Lab tab open.
        /// </summary>
        internal void SyncLabEffectPermsUI()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                if (LabTab.ChkCapEffects != null)
                    LabTab.ChkCapEffects.IsChecked = s.CompanionPrompt.AllowAiToControlEffects;
                if (LabTab.EffectPermsPanel != null)
                    LabTab.EffectPermsPanel.Visibility = s.CompanionPrompt.AllowAiToControlEffects
                        ? Visibility.Visible : Visibility.Collapsed;

                // Effect permission grid
                if (LabTab.ChkAllowFlash != null)       LabTab.ChkAllowFlash.IsChecked       = s.CompanionPrompt.AllowAiFlash;
                if (LabTab.ChkAllowVideo != null)       LabTab.ChkAllowVideo.IsChecked       = s.CompanionPrompt.AllowAiVideo;
                if (LabTab.ChkAllowAudio != null)       LabTab.ChkAllowAudio.IsChecked       = s.CompanionPrompt.AllowAiAudio;
                if (LabTab.ChkAllowBubbles != null)     LabTab.ChkAllowBubbles.IsChecked     = s.CompanionPrompt.AllowAiBubbles;
                if (LabTab.ChkAllowSubliminal != null)  LabTab.ChkAllowSubliminal.IsChecked  = s.CompanionPrompt.AllowAiSubliminal;
                if (LabTab.ChkAllowOverlay != null)     LabTab.ChkAllowOverlay.IsChecked     = s.CompanionPrompt.AllowAiOverlay;
                if (LabTab.ChkAllowLockCard != null)    LabTab.ChkAllowLockCard.IsChecked    = s.CompanionPrompt.AllowAiLockCard;
                if (LabTab.ChkAllowBounce != null)      LabTab.ChkAllowBounce.IsChecked      = s.CompanionPrompt.AllowAiBounce;
                if (LabTab.ChkAllowHaptic != null)      LabTab.ChkAllowHaptic.IsChecked      = s.CompanionPrompt.AllowAiHaptic;
                if (LabTab.ChkAllowGetBackToMe != null) LabTab.ChkAllowGetBackToMe.IsChecked = s.CompanionPrompt.AllowAiGetBackToMe;

                // Max haptic intensity
                if (LabTab.SliderMaxHapticIntensity != null) LabTab.SliderMaxHapticIntensity.Value = s.CompanionPrompt.MaxAiHapticIntensity;
                if (LabTab.TxtMaxHapticIntensity != null)    LabTab.TxtMaxHapticIntensity.Text    = $"{(int)(s.CompanionPrompt.MaxAiHapticIntensity * 100)}%";

                // Chat memory toggle
                if (LabTab.ChkChatMemoryEnabled != null) LabTab.ChkChatMemoryEnabled.IsChecked = s.CompanionPrompt.ChatMemoryEnabled;
            }
            finally
            {
                _isLoading = wasLoading;
            }
        }

        /// <summary>
        /// Populate AI Brain controls from settings. Called from SyncCompanionTabUI.
        /// </summary>
        private void SyncAiBrainUI()
        {
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            // Provider radios
            var aiOn = s.AiChatEnabled;
            var provider = s.CompanionPrompt.AiProvider;
            if (CompanionTab.RadioAiOff != null)   CompanionTab.RadioAiOff.IsChecked   = !aiOn;
            if (CompanionTab.RadioAiCloud != null) CompanionTab.RadioAiCloud.IsChecked = aiOn && provider == Models.AiProviderType.Cloud;
            if (CompanionTab.RadioAiLocal != null) CompanionTab.RadioAiLocal.IsChecked = aiOn && provider == Models.AiProviderType.Local;
            if (CompanionTab.RadioAiOpenAiCompatible != null)
                CompanionTab.RadioAiOpenAiCompatible.IsChecked = aiOn && provider == Models.AiProviderType.OpenAiCompatible;

            if (CompanionTab.LocalConfigPanel != null)
                CompanionTab.LocalConfigPanel.Visibility = (aiOn && provider == Models.AiProviderType.Local)
                    ? Visibility.Visible : Visibility.Collapsed;
            if (CompanionTab.OpenAiCompatibleConfigPanel != null)
                CompanionTab.OpenAiCompatibleConfigPanel.Visibility = (aiOn && provider == Models.AiProviderType.OpenAiCompatible)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Daily request limit row is only meaningful for the OpenAI-compatible provider
            if (CompanionTab.DailyLimitPanel != null)
            {
                // Only show the daily request limit row when the custom OpenAI-compatible
                // provider is active. Cloud uses built-in free/Patreon limits that are
                // not user-editable; Local has unlimited requests.
                CompanionTab.DailyLimitPanel.Visibility = (aiOn && provider == Models.AiProviderType.OpenAiCompatible)
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            // Local config fields
            if (CompanionTab.TxtAiModel != null) CompanionTab.TxtAiModel.Text = s.CompanionPrompt.AiModel ?? "";
            if (CompanionTab.TxtAiHost != null)  CompanionTab.TxtAiHost.Text  = s.CompanionPrompt.AiOllamaHost ?? "";

            // OpenAI-compatible provider fields
            if (CompanionTab.TxtOpenAiEndpoint != null)
                CompanionTab.TxtOpenAiEndpoint.Text = s.CompanionPrompt.OpenAiCompatibleEndpoint ?? string.Empty;
            if (CompanionTab.TxtOpenAiModel != null)
                CompanionTab.TxtOpenAiModel.Text = s.CompanionPrompt.OpenAiCompatibleModel ?? string.Empty;

            // Daily request limit (0 = unlimited) – only for OpenAI-compatible provider
            if (CompanionTab.TxtDailyLimit != null)
            {
                var limit = s.CompanionPrompt.DailyRequestLimit;
                CompanionTab.TxtDailyLimit.Text = limit > 0 ? limit.ToString() : string.Empty;
            }

            // Capability checkboxes (CompanionTab.ChkAwarenessMode handled by its own sync path; AiChatEnabled is driven solely by the provider radios)
            SyncLabEffectPermsUI();

            // Awareness panel visibility (from previous handler logic)
            if (CompanionTab.AwarenessSettingsPanel != null)
                CompanionTab.AwarenessSettingsPanel.Visibility = s.AwarenessModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Hero pills
            UpdateAiBrainPills();

            // Slut Mode toggle (no Patreon gate — available to all)
            if (CompanionTab.ChkSlutMode != null) CompanionTab.ChkSlutMode.IsChecked = s.SlutModeEnabled;

            // Live actions placeholder + ItemsSource binding
            if (CompanionTab.LiveActionsList != null && CompanionTab.LiveActionsList.ItemsSource == null)
            {
                CompanionTab.LiveActionsList.ItemsSource = App.AiLiveActions;
                // Auto-toggle the placeholder when entries arrive (added by AiCommandService).
                App.AiLiveActions.CollectionChanged += (_, _) =>
                {
                    if (Dispatcher.CheckAccess()) UpdateLiveActionsPlaceholder();
                    else Dispatcher.BeginInvoke(new Action(UpdateLiveActionsPlaceholder));
                };
            }
            UpdateLiveActionsPlaceholder();
        }





        internal void ChkMuteAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            _avatarTubeWindow?.SetMuteAvatar(isEnabled);

            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AvatarMuted = isEnabled;
                App.Settings.Save();
            }
        }

        internal void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isMuted = checkbox?.IsChecked == true;

            // Toggle SubAudioEnabled (muted = disabled)
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !isMuted;
                App.Settings.Save();
            }

            // Sync Settings tab checkbox (inverted - it's "enabled" not "muted")
            _isLoading = true;
            SettingsTab.ChkAudioWhispers.IsChecked = !isMuted;
            _isLoading = false;

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
                // Update Companion tab controls
                if (muteAvatar.HasValue) CompanionTab.ChkMuteAvatarCompanion.IsChecked = muteAvatar.Value;
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
                // Settings tab - SettingsTab.ChkAudioWhispers represents "whispers enabled"
                SettingsTab.ChkAudioWhispers.IsChecked = enabled;

                // Companion tab - CompanionTab.ChkMuteWhispersCompanion represents "whispers muted" (inverted)
                CompanionTab.ChkMuteWhispersCompanion.IsChecked = !enabled;
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
                    SettingsTab.SliderMaster.Value = s.MasterVolume;
                    if (SettingsTab.TxtMaster != null) SettingsTab.TxtMaster.Text = $"{s.MasterVolume}%";
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
                    SettingsTab.SliderMaster.Value = s.MasterVolume;
                    if (SettingsTab.TxtMaster != null) SettingsTab.TxtMaster.Text = $"{s.MasterVolume}%";
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
