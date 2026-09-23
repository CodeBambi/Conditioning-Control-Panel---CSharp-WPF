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
        public void OnSectionShown()
        {
            RefreshTierBadge();
            RefreshChaster();
        }

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
            Loaded += (_, __) => { RefreshTierBadge(); RefreshChaster(); SubscribeChaster(true); };
            Unloaded += (_, __) => SubscribeChaster(false);
            // Settings is a page you arrive at, not one you sit on: repainting when it becomes
            // visible is enough to catch a login that happened on another door, and costs nothing
            // when it does not.
            IsVisibleChanged += (_, __) => { if (IsVisible) { RefreshTierBadge(); RefreshChaster(); } };
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

        // ------------------------------------------------------------------ Chaster

        private bool _chasterSubscribed;

        // The username lands a moment after the link (or after launch): repaint when it does.
        private void SubscribeChaster(bool on)
        {
            var chaster = App.Chaster;
            if (chaster == null || on == _chasterSubscribed) return;
            _chasterSubscribed = on;
            if (on) chaster.ProfileChanged += OnChasterProfileChanged;
            else chaster.ProfileChanged -= OnChasterProfileChanged;
        }

        private void OnChasterProfileChanged() => Dispatcher.BeginInvoke(new Action(RefreshChaster));

        /// <summary>The Chaster row: the account's picture and name plus the lock, or Not linked. The page owns the rest.</summary>
        internal void RefreshChaster()
        {
            try
            {
                var chaster = App.Chaster;
                var linked = chaster?.IsLinked == true;
                var profile = linked ? chaster!.Profile : null;
                ChasterBadge.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
                TxtChasterStatus.Text = linked
                    ? Loc.Get("chaster_account_name") + " · " + (profile?.Username ?? Loc.Get("chaster_account_linked"))
                    : Loc.Get("chaster_account_name") + " · " + Loc.Get("label_not_connected");
                TxtChasterInfo.Text = linked
                    ? Views.Tabs.ChasterTabView.AccountLockLine(chaster)
                    : Loc.Get("chaster_account_hint");
                BtnChasterLink.Content = Loc.Get(linked ? "chaster_unlink" : "chaster_link");
                BtnChasterLink.IsEnabled = chaster != null && !chaster.IsLinking;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster settings row"); }
        }

        private void BtnChasterOpen_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ShowTab("chaster");
        }

        private async void BtnChasterLink_Click(object sender, RoutedEventArgs e)
        {
            var chaster = App.Chaster;
            if (chaster == null || chaster.IsLinking) return;
            try
            {
                if (chaster.IsLinked)
                {
                    await Views.Tabs.ChasterTabView.ConfirmAndUnlinkAsync(Window.GetWindow(this));
                }
                else
                {
                    BtnChasterLink.IsEnabled = false;
                    await chaster.LinkAsync(url => Helpers.BrowserLauncher.OpenUrlOrPrompt(url, "link Chaster"));
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster link from settings"); }
            finally { RefreshChaster(); }
        }

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
