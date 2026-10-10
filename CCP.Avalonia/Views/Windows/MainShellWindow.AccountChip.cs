// PORTED from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs RefreshAccountChip (WPF 7.1.5
// :444-505) and MainWindow.ProfileBubble.cs RefreshProfileBubble / RefreshProfileMenu (:150-350).
// Parity ledger shell#17 (account chip), shell#18 + social#21 (profile bubble face + menu).
//
// WPF rides one choke point for the whole "who am I" unit: UpdateXPBarLoginState, reached from
// UpdateLevelDisplay and every auth/tier change. Here that is RefreshAccountIdentity, called from
// UpdateLevelDisplay (MainShellWindow.HeroFx.cs, which UpdateQuickLoginUI runs on sign-in, restore
// and logout) and on window activation (MainShellWindow.HudDepth.cs: Core has no tier event).
//
// ponytail: the bubble face paints initials only. The equipped preset bust (CosmeticsCatalog) and
// the shared Discord photo (LoadProfileBubblePhotoAsync) need the cosmetics resolver and an
// avatar url on Core's DiscordAccount; the XP pulse / level-up wobble / unlock shimmer need
// XPChanged / AchievementUnlocked events Core does not raise yet.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Fixed brand values, not mod-owned (WPF AccountChipTier*Brush): gold is the Tier-1 lock
        // everywhere in the app, violet the Tier-2 "Lab" flask.
        internal static readonly IBrush AccountChipTier1Brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        internal static readonly IBrush AccountChipTier2Brush = new SolidColorBrush(Color.FromRgb(0xB4, 0x7B, 0xFF));
        internal static readonly IBrush AccountChipNeutralBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly IBrush ProfileBubbleNeutralBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));

        /// <summary>WPF UpdateXPBarLoginState's identity half: chip, bubble, spark, open menu.</summary>
        internal void RefreshAccountIdentity()
        {
            RefreshAccountChip();
            RefreshProfileBubble();
            RefreshPremiumSpark();
            UpdateXPBarLoginState();   // WPF UpdateXPBarLoginState's bar half (MainShellWindow.XpBar.cs)
        }

        /// <summary>Signed-out CTA, or display name plus a tier badge (gold lock = Tier 1, violet
        /// flask = Tier 2 / whitelist). WPF MainWindow.UiUpdates.cs RefreshAccountChip.</summary>
        internal void RefreshAccountChip()
        {
            if (Named<Button>("BtnAccountChip") is not { } chip
                || Named<TextBlock>("TxtAccountChipName") is not { } name
                || Named<TextBlock>("TxtAccountChipBadge") is not { } badge) return;
            try
            {
                if (!CoreAccount.IsLoggedIn)
                {
                    name.Text = Loc.Get("account_chip_sign_in");
                    badge.IsVisible = false;
                    // The mod accent, as a live resource reference so a mod switch repaints it.
                    chip.Bind(BorderBrushProperty, chip.GetResourceObservable("PinkBrush"));
                    return;
                }

                var display = CoreAccount.DisplayName;
                name.Text = string.IsNullOrWhiteSpace(display) ? Loc.Get("account_chip_signed_in") : display;

                // HasLabAccess / HasPremiumAccess fold in the whitelist, SubscribeStar and the
                // offline grace, so the badge tells the same story the feature gates do.
                if (CoreAccount.HasLabAccess)
                {
                    badge.Text = "🧪";
                    badge.IsVisible = true;
                    chip.BorderBrush = AccountChipTier2Brush;
                }
                else if (CoreAccount.HasPremiumAccess)
                {
                    badge.Text = "🔒";
                    badge.IsVisible = true;
                    chip.BorderBrush = AccountChipTier1Brush;
                }
                else
                {
                    badge.IsVisible = false;
                    chip.BorderBrush = AccountChipNeutralBrush;
                }
            }
            catch (Exception ex) { Log.Debug("RefreshAccountChip failed: {E}", ex.Message); }
        }

        /// <summary>The menu's identity rows: name + tier badge, the reachable achievement count and
        /// the Log out / Sign in caption. WPF RefreshProfileMenu (the Level/XP rail is painted by
        /// UpdateLevelDisplay).</summary>
        private void RefreshProfileMenu()
        {
            try
            {
                var loggedIn = CoreAccount.IsLoggedIn;
                var display = CoreAccount.DisplayName;
                if (Named<TextBlock>("ProfileMenuName") is { } name)
                {
                    name.Text = loggedIn
                        ? (string.IsNullOrWhiteSpace(display) ? Loc.Get("account_chip_signed_in") : display)
                        : Loc.Get("account_chip_sign_in");
                    name.IsVisible = true;
                }

                if (Named<TextBlock>("ProfileMenuBadge") is { } badge)
                {
                    if (CoreAccount.HasLabAccess) { badge.Text = "🧪"; badge.IsVisible = true; }
                    else if (CoreAccount.HasPremiumAccess) { badge.Text = "🔒"; badge.IsVisible = true; }
                    else badge.IsVisible = false;
                }

                if (Named<TextBlock>("ProfileMenuBadges") is { } badges)
                {
                    // The reachable pair, not the raw catalogue: a free user's bubble must agree
                    // with the tab that 100% is a place they can actually get to.
                    if (App.Achievements is { } ach)
                    {
                        var (got, reachable) = ach.GetReachableCounts();
                        badges.Text = string.Format(Loc.Get("profile_bubble_achievements"), got, reachable);
                        badges.IsVisible = true;
                    }
                    else badges.IsVisible = false;
                }

                if (Named<Button>("ProfileMenuAccountBtn") is { } account)
                {
                    account.Content = loggedIn ? Loc.Get("btn_logout") : Loc.Get("account_chip_sign_in");
                    account.IsVisible = true;
                }
            }
            catch (Exception ex) { Log.Debug("RefreshProfileMenu: {E}", ex.Message); }
        }

        /// <summary>Signed in: the full quick-logout flow (pre-logout sync, provider logouts,
        /// repaint). Signed out: the account chip's door, Settings scrolled to Account.
        /// WPF ProfileMenuAccount_Click.</summary>
        private void ProfileMenuAccount_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
            if (CoreAccount.IsLoggedIn) Logout();
            else OpenAppSettingsSection("account");
        }
    }
}
