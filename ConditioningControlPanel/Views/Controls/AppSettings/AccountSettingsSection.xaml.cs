using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · ACCOUNT. The account cards' real home as of Phase 2 - they used to live in
    /// PatreonTabView and be borrowed at runtime by <c>DetachAccountSectionsInto</c>.
    ///
    /// <para>Pure forwarding, like every re-parented cell: each Click shim hands straight to the
    /// MainWindow handler that already owned it, so no login/link/backup logic moved.</para>
    ///
    /// <para>The one exception is <see cref="RefreshTierBadge"/>, which paints the net-new
    /// "who you are / which bar you clear" card. It reads exactly the two properties the Phase-1
    /// header chip and <c>Services.TierGate</c> read - <c>HasLabAccess</c> then
    /// <c>HasPremiumAccess</c>, in that order - so the badge can never claim an entitlement the
    /// gates would refuse. Null-safe throughout: during startup <c>App.Patreon</c> is null and the
    /// card simply reads as signed-out.</para>
    /// </summary>
    public partial class AccountSettingsSection : UserControl, IAppSettingsSection
    {
        /// <summary>
        /// Host seam: the Settings door repaints the tier card every time it opens, so a login that
        /// happened behind another door is never shown stale.
        /// </summary>
        public void OnSectionShown() => RefreshTierBadge();

        // Same fixed brand values as the header chip (MainWindow.UiUpdates.cs): gold is the Tier-1
        // lock everywhere in the app, violet is the Tier-2 "Lab" flask. Not mod-owned.
        private static readonly SolidColorBrush TierBadgeTier1Brush = Frozen(0xFF, 0xD7, 0x00);
        private static readonly SolidColorBrush TierBadgeTier2Brush = Frozen(0xB4, 0x7B, 0xFF);
        private static readonly SolidColorBrush TierBadgeNeutralBrush = Frozen(0x3D, 0x3D, 0x60);

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public AccountSettingsSection()
        {
            InitializeComponent();
            Loaded += (_, __) => RefreshTierBadge();
            // Settings is a page you arrive at, not one you sit on: repainting when it becomes
            // visible is enough to catch a login that happened on another door, and costs nothing
            // when it does not.
            IsVisibleChanged += (_, __) => { if (IsVisible) RefreshTierBadge(); };
        }

        /// <summary>
        /// Repaints the account/tier card. Safe to call at any time from any thread that owns the
        /// dispatcher; never throws.
        /// </summary>
        internal void RefreshTierBadge()
        {
            if (TxtAccountSectionName == null || TxtAccountSectionTier == null ||
                AccountTierBadge == null || TxtAccountTierBadge == null) return;

            try
            {
                if (!App.IsLoggedIn)
                {
                    TxtAccountSectionName.Text = Loc.Get("account_chip_sign_in");
                    TxtAccountSectionTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");
                    AccountTierBadge.Visibility = Visibility.Collapsed;
                    AccountTierCard.BorderBrush = TierBadgeNeutralBrush;
                    return;
                }

                var name = App.UserDisplayName;
                TxtAccountSectionName.Text = string.IsNullOrWhiteSpace(name)
                    ? Loc.Get("account_chip_signed_in")
                    : name;

                if (App.Patreon?.HasLabAccess == true)
                {
                    TxtAccountTierBadge.Text = "🧪";
                    AccountTierBadge.Visibility = Visibility.Visible;
                    AccountTierBadge.BorderBrush = TierBadgeTier2Brush;
                    AccountTierCard.BorderBrush = TierBadgeTier2Brush;
                    TxtAccountSectionTier.Text = App.Patreon?.IsWhitelisted == true
                        ? Loc.Get("label_patreon_tier_whitelisted")
                        : Loc.Get("label_patreon_tier_level2");
                }
                else if (App.Patreon?.HasPremiumAccess == true)
                {
                    TxtAccountTierBadge.Text = "🔒";
                    AccountTierBadge.Visibility = Visibility.Visible;
                    AccountTierBadge.BorderBrush = TierBadgeTier1Brush;
                    AccountTierCard.BorderBrush = TierBadgeTier1Brush;
                    TxtAccountSectionTier.Text = Loc.Get("label_patreon_tier_level1");
                }
                else
                {
                    AccountTierBadge.Visibility = Visibility.Collapsed;
                    AccountTierCard.BorderBrush = TierBadgeNeutralBrush;
                    TxtAccountSectionTier.Text = Loc.Get("label_patreon_tier_connected");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AccountSettingsSection.RefreshTierBadge failed: {E}", ex.Message);
            }
        }

        // ---- forwarding shims (identical bodies to the ones PatreonTabView carried) ----

        private void BtnPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnPatreonLogin_Click(sender, e);
        }

        private void BtnSubscribeStarLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnSubscribeStarLogin_Click(sender, e);
        }

        private void BtnDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnDiscordLogin_Click(sender, e);
        }

        private void BtnLinkPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnLinkPatreon_Click(sender, e);
        }

        private void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnLinkDiscord_Click(sender, e);
        }

        private void BtnBackupSettingsNow_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnBackupSettingsNow_Click(sender, e);
        }

        private void BtnRestoreSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnRestoreSettings_Click(sender, e);
        }

        private void BtnExportData_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnExportData_Click(sender, e);
        }

        private void BtnPrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnPrivacyPolicy_Click(sender, e);
        }

        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnVisitPatreon_Click(sender, e);
        }
    }
}
