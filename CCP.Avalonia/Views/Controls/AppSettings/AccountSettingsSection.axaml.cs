using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · ACCOUNT, ported from the WPF head.
    ///
    /// <para><see cref="RefreshTierBadge"/> is live: it reads <c>CoreAccount</c>, which is the
    /// account seam (CCP.Core/CoreAccount.cs). It asks exactly the two properties the WPF original
    /// asks, in the same order - <c>HasLabAccess</c> then <c>HasPremiumAccess</c> - so the badge can
    /// never claim an entitlement the gates would refuse. On this head the seam is unseeded, which
    /// means signed out and not entitled, so the card paints "sign in": that is the honest reading
    /// of a head with no OAuth flow and no token store, not a placeholder.</para>
    ///
    /// <para>The two link buttons are live too - Avalonia's <c>Launcher</c> is the cross-platform
    /// stand-in for WPF's <c>Process.Start(UseShellExecute)</c>, and the URLs are the same two
    /// constants MainWindow carries.</para>
    ///
    /// <para>The login/link/backup/export buttons stay stubs: each needs a provider service (OAuth,
    /// cloud backup) that this head does not have. See the notes on each.</para>
    /// </summary>
    public partial class AccountSettingsSection : UserControl
    {
        // The same fixed brand values as the WPF original and the header chip: gold is the Tier-1
        // lock everywhere in the app, violet the Tier-2 "Lab" flask. Not mod-owned.
        private static readonly IBrush TierBadgeTier1Brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly IBrush TierBadgeTier2Brush = new SolidColorBrush(Color.FromRgb(0xB4, 0x7B, 0xFF));
        private static readonly IBrush TierBadgeNeutralBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));

        private const string PatreonUrl = "https://www.patreon.com/CodeBambi";
        private const string PrivacyPolicyUrl = "https://cclabs.app/privacy-policy.html";

        // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
        // x:Name fields and wires the markup Click handlers (CLAUDE.md). The controls are still
        // resolved by name here so this file never depends on which of the two ran.
        private readonly Border _tierCard;
        private readonly Border _tierBadge;
        private readonly TextBlock _txtBadge;
        private readonly TextBlock _txtName;
        private readonly TextBlock _txtTier;

        public AccountSettingsSection()
        {
            InitializeComponent();

            _tierCard = this.FindControl<Border>("AccountTierCard")!;
            _tierBadge = this.FindControl<Border>("AccountTierBadge")!;
            _txtBadge = this.FindControl<TextBlock>("TxtAccountTierBadge")!;
            _txtName = this.FindControl<TextBlock>("TxtAccountSectionName")!;
            _txtTier = this.FindControl<TextBlock>("TxtAccountSectionTier")!;

            // WPF repaints on Loaded and on becoming visible; this head's sections are seeded from
            // their own constructor (see GeneralSettingsSection), and Settings is a page you arrive
            // at rather than sit on. The language hook is the one addition: the two TextBlocks are
            // driven from code, so nothing else would re-render them after a language change.
            RefreshTierBadge();
            LocalizationManager.Instance.LanguageChanged += (_, _) => RefreshTierBadge();
        }

        /// <summary>
        /// Host seam: the Settings door repaints the tier card every time it opens, so a login that
        /// happened behind another door is never shown stale.
        /// </summary>
        public void OnSectionShown() => RefreshTierBadge();

        /// <summary>Repaints the account/tier card from the account seam. Never throws.</summary>
        internal void RefreshTierBadge()
        {
            try
            {
                if (!CoreAccount.IsLoggedIn)
                {
                    _txtName.Text = Loc.Get("account_chip_sign_in");
                    _txtTier.Text = Loc.Get("label_login_to_unlock_exclusive_features");
                    _tierBadge.IsVisible = false;
                    _tierCard.BorderBrush = TierBadgeNeutralBrush;
                    return;
                }

                var name = CoreAccount.DisplayName;
                _txtName.Text = string.IsNullOrWhiteSpace(name) ? Loc.Get("account_chip_signed_in") : name;

                if (CoreAccount.HasLabAccess)
                {
                    _txtBadge.Text = "🧪";
                    _tierBadge.IsVisible = true;
                    _tierBadge.BorderBrush = TierBadgeTier2Brush;
                    _tierCard.BorderBrush = TierBadgeTier2Brush;
                    _txtTier.Text = CoreAccount.IsWhitelisted
                        ? Loc.Get("label_patreon_tier_whitelisted")
                        : Loc.Get("label_patreon_tier_level2");
                }
                else if (CoreAccount.HasPremiumAccess)
                {
                    _txtBadge.Text = "🔒";
                    _tierBadge.IsVisible = true;
                    _tierBadge.BorderBrush = TierBadgeTier1Brush;
                    _tierCard.BorderBrush = TierBadgeTier1Brush;
                    _txtTier.Text = Loc.Get("label_patreon_tier_level1");
                }
                else
                {
                    _tierBadge.IsVisible = false;
                    _tierCard.BorderBrush = TierBadgeNeutralBrush;
                    _txtTier.Text = Loc.Get("label_patreon_tier_connected");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("AccountSettingsSection.RefreshTierBadge failed: {E}", ex.Message);
            }
        }

        private async void OpenUrl(string url)
        {
            try
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher == null) return;
                await launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AccountSettingsSection: failed to open {Url}", url);
            }
        }

        // ponytail: the OAuth halves. Each needs a provider service this head does not have -
        // PatreonService owns an HttpListener callback, and Discord/SubscribeStar the same shape.
        // CoreAccount answers who is signed in; it deliberately carries no way to SIGN somebody in,
        // because a login button that mints a session with no token store to keep it would leave
        // the user logged in until they blink. CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs is
        // the surface to hang these on once a provider exists.
        private void BtnPatreonLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnSubscribeStarLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDiscordLogin_Click(object? sender, RoutedEventArgs e) { }
        private void BtnLinkPatreon_Click(object? sender, RoutedEventArgs e) { }
        private void BtnLinkDiscord_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: cloud settings backup - ProfileSyncService.BackupSettingsAsync /
        // GetSettingsBackupInfoAsync / RestoreSettingsFromCloudAsync, all still in the WPF head and
        // not in this layer's seam. CCP.Avalonia/Views/Windows/MainShellWindow.CloudBackup.cs holds
        // the restore path's LOCAL-WINS field list and is where these belong.
        private void BtnBackupSettingsNow_Click(object? sender, RoutedEventArgs e) { }
        private void BtnRestoreSettings_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: GDPR export - ProfileSyncService.ExportDataAsync plus a save-file picker and a
        // result message box. CoreAccount deliberately does not carry ExportDataAsync yet: it is
        // one line to add, and adding it without the picker and the failure dialog would give the
        // button a transport and no way to tell the user it failed.
        private void BtnExportData_Click(object? sender, RoutedEventArgs e) { }

        // Live: WPF used Process.Start with UseShellExecute, Avalonia's Launcher is the
        // cross-platform equivalent. Same two URLs MainWindow.CloudBackup.cs and
        // MainWindow.Patreon.cs carry.
        private void BtnPrivacyPolicy_Click(object? sender, RoutedEventArgs e) => OpenUrl(PrivacyPolicyUrl);
        private void BtnVisitPatreon_Click(object? sender, RoutedEventArgs e) => OpenUrl(PatreonUrl);
    }
}
