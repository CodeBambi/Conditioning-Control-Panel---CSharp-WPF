using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // SubscribeStar login card: mirrors the Patreon login flow (MainWindow.Patreon.cs).
    // SubscribeStar shares the canonical premium gate (PatreonService.HasPremiumAccess
    // OR's App.SubscribeStar in), so when the tier changes we refresh the same Patreon-
    // driven premium UI to light up / lock down gated features.
    public partial class MainWindow
    {
        /// <summary>
        /// Wire up SubscribeStar tier-change handling + initial UI. Called from
        /// InitializePatreonTab() alongside the Patreon tab init.
        /// </summary>
        private void InitializeSubscribeStarTab()
        {
            if (App.SubscribeStar != null)
            {
                App.SubscribeStar.TierChanged += OnSubscribeStarTierChanged;
            }
            UpdateSubscribeStarUI();
        }

        private void OnSubscribeStarTierChanged(object? sender, PatreonTier tier)
        {
            // This handler sat unreachable until InitializeSubscribeStarTab() was finally called,
            // so its shutdown behaviour has never actually been exercised. It fires from a
            // background validation continuation, and a *blocking* Invoke onto a dispatcher that
            // has begun shutting down either throws or waits on a pump that will never run again.
            // Bail first, then post rather than block - nothing here needs a return value.
            if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    UpdateSubscribeStarUI();
                    // Shared gate — refresh the Patreon-driven premium UI so locked
                    // features unlock/relock based on the combined entitlement.
                    UpdatePatreonUI();
                    UpdateUnlockablesVisibility(App.Settings?.Current?.PlayerLevel ?? 1);
                    MaybeShowPremiumCelebration();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "SubscribeStar tier refresh failed");
                }
            });
        }

        private void UpdateSubscribeStarUI()
        {
            if (PatreonTab.TxtSubscribeStarStatus == null || PatreonTab.TxtSubscribeStarTier == null || PatreonTab.BtnSubscribeStarLogin == null)
                return;

            var svc = App.SubscribeStar;
            var isAuthenticated = svc?.IsAuthenticated ?? false;

            if (isAuthenticated)
            {
                var tier = svc?.CurrentTier ?? PatreonTier.None;
                var isWhitelisted = svc?.IsWhitelisted == true;
                var nameToShow = App.Settings?.Current?.UserDisplayName ?? svc?.DisplayName;

                PatreonTab.TxtSubscribeStarStatus.Text = string.IsNullOrEmpty(nameToShow)
                    ? "Connected to SubscribeStar"
                    : $"Welcome, {nameToShow}!";

                PatreonTab.TxtSubscribeStarTier.Text = tier switch
                {
                    PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
                    PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
                    _ when isWhitelisted => Loc.Get("label_patreon_tier_whitelisted"),
                    _ => Loc.Get(svc?.IsActiveSubscriber == true ? "label_patreon_tier_patron" : "label_patreon_tier_connected")
                };
                PatreonTab.BtnSubscribeStarLogin.Content = Loc.Get("btn_logout");
            }
            else
            {
                PatreonTab.TxtSubscribeStarStatus.Text = Loc.Get("label_not_connected");
                PatreonTab.TxtSubscribeStarTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");
                PatreonTab.BtnSubscribeStarLogin.Content = Loc.Get("btn_login");
            }
        }

        internal void BtnSubscribeStarLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.SubscribeStar == null) return;

            if (App.SubscribeStar.IsAuthenticated)
            {
                // Logout. If no other provider remains, tear down the whole account
                // (mirrors BtnPatreonLogin_Click); otherwise just refresh this card.
                App.SubscribeStar.Logout();
                if (App.Patreon?.IsAuthenticated != true && App.Discord?.IsAuthenticated != true)
                {
                    ClearAccountData();
                }
                else
                {
                    UpdateSubscribeStarUI();
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
                return;
            }

            // Route through the unified login dialog so SubscribeStar establishes a real
            // account (username pick on first login, ProfileSync, XP/AI/feature unlock) —
            // identical to Patreon/Discord. The dialog handles the substar provider path.
            OpenUnifiedLoginDialog();
        }
    }
}
