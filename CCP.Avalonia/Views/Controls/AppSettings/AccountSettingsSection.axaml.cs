using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · ACCOUNT, ported from the WPF head.
    ///
    /// <para>Every card is live except cloud backup/restore and GDPR export (hidden until
    /// ProfileSync's calls reach Core). Providers come from <c>AccountSeed</c>; the tier card asks
    /// <c>CoreAccount</c> exactly what WPF asks (<c>HasLabAccess</c> then <c>HasPremiumAccess</c>).</para>
    /// </summary>
    public partial class AccountSettingsSection : UserControl, IAppSettingsSection
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
            RefreshProviderRows();
            var presence = this.FindControl<CheckBox>("ChkFriendsPresence")!;
            presence.IsCheckedChanged += ChkFriendsPresence_Changed;
            RefreshFriendsPresence();
            EventHandler changed = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { RefreshTierBadge(); RefreshProviderRows(); });
            RefreshChaster();
            // Chaster row (Mich, rows-login-phone): repaint on profile/link/lock changes while attached (P41).
            Action chasterChanged = () => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshChaster);
            ChasterService? chaster = null;
            AttachedToVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged += changed;
                if ((chaster = ChasterHead.Service) is { } c) { c.ProfileChanged += chasterChanged; c.LinkChanged += chasterChanged; c.LockChanged += chasterChanged; }
                RefreshTierBadge();
                RefreshChaster();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged -= changed;
                if (chaster is { } c) { c.ProfileChanged -= chasterChanged; c.LinkChanged -= chasterChanged; c.LockChanged -= chasterChanged; }
                chaster = null;
            };
        }

        /// <summary>
        /// Host seam: the Settings door repaints the tier card every time it opens, so a login that
        /// happened behind another door is never shown stale.
        /// </summary>
        public void OnSectionShown()
        {
            RefreshTierBadge();
            RefreshProviderRows();
            RefreshFriendsPresence();
            RefreshChaster();
            _ = UpdateBackupStatusAsync();
            PlansView.RefreshVault();   // the plates can move between visits (sign-in, tier change)
            // Throttled inside (30 s): the invites card lives on this copy (WPF RefreshVaultCore).
            _ = PlansView.FindControl<Controls.Invites.InvitePanel>("InvitesHost")?.RefreshAsync();
        }

        private bool _refreshingPresence;

        /// <summary>WPF FriendsPresenceSetting.Read: the friends service's live answer when it is up,
        /// else the saved setting.</summary>
        internal void RefreshFriendsPresence()
        {
            var chk = this.FindControl<CheckBox>("ChkFriendsPresence");
            if (chk == null) return;
            _refreshingPresence = true;
            try
            {
                var svc = global::ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service;
                chk.IsChecked = svc?.Available == true ? svc.PresenceShared
                    : CoreSettings.Current?.FriendsPresenceShared == true;
            }
            catch (Exception ex) { Log.Debug("friends presence row: {E}", ex.Message); }
            finally { _refreshingPresence = false; }
        }

        /// <summary>WPF FriendsPresenceSetting.Write: through the service when it is up (it saves),
        /// else straight to settings; answering here counts as answering the once-only ask.</summary>
        private void ChkFriendsPresence_Changed(object? sender, RoutedEventArgs e)
        {
            if (_refreshingPresence || sender is not CheckBox chk) return;
            bool on = chk.IsChecked == true;
            try
            {
                var svc = global::ConditioningControlPanel.Avalonia.Platform.FriendsHead.Service;
                if (svc?.Available == true) svc.PresenceShared = on;
                else if (CoreSettings.Current is { } s) { s.FriendsPresenceShared = on; CoreSettings.Save(); }
                PresenceAsk.MarkAsked();
            }
            catch (Exception ex) { Log.Debug("friends presence write: {E}", ex.Message); }
        }

        /// <summary>The Account &amp; Plans copy of the vault (header, plates, invites).</summary>
        internal Tabs.ExclusivesTabView Plans => PlansView;

        /// <summary>Starts or parks the plans room (AppSettingsTabView.SyncPlansMotion).</summary>
        internal void SetPlansMotion(bool on) => PlansView.SetMotion(on);

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

        private void OpenUrl(string url) => _ = Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), url);

        // Live (social#2): the flows are in AccountSettingsSection.Providers.cs.
        private void BtnPatreonLogin_Click(object? sender, RoutedEventArgs e) => _ = ProviderLoginAsync("patreon");
        private void BtnSubscribeStarLogin_Click(object? sender, RoutedEventArgs e) => _ = ProviderLoginAsync("substar");
        private void BtnDiscordLogin_Click(object? sender, RoutedEventArgs e) => _ = ProviderLoginAsync("discord");
        private void BtnLinkPatreon_Click(object? sender, RoutedEventArgs e) => _ = LinkProviderAsync("patreon", "BtnLinkPatreon");
        private void BtnLinkDiscord_Click(object? sender, RoutedEventArgs e) => _ = LinkProviderAsync("discord", "BtnLinkDiscord");

        private Window? Owner => TopLevel.GetTopLevel(this) as Window;

        // ---- Chaster (WPF AccountSettingsSection.xaml.cs RefreshChaster) ----

        /// <summary>The Chaster row: the account's picture and name plus the lock, or Not linked.</summary>
        internal void RefreshChaster()
        {
            try
            {
                var chaster = ChasterHead.Service;
                var linked = chaster?.IsLinked == true;
                var profile = linked ? chaster!.Profile : null;
                Find<Control>("ChasterBadge").IsVisible = linked;
                Find<TextBlock>("TxtChasterStatus").Text = Loc.Get("chaster_account_name") + " · " + (linked
                    ? profile?.Username ?? Loc.Get("chaster_account_linked")
                    : Loc.Get("label_not_connected"));
                var snapshot = chaster?.Lock;
                Find<TextBlock>("TxtChasterInfo").Text = !linked ? Loc.Get("chaster_account_hint")
                    : snapshot == null ? Loc.Get("chaster_account_nolock")
                    : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!;
                Find<TextBlock>("TxtBtnChasterLink").Text = Loc.Get(linked ? "chaster_unlink" : "chaster_link");
                Find<Button>("BtnChasterLink").IsEnabled = chaster != null && !chaster.IsLinking;
            }
            catch (Exception ex) { Log.Debug("chaster settings row: {E}", ex.Message); }
        }

        private void BtnChasterOpen_Click(object? sender, RoutedEventArgs e) => Shell?.ShowTab("chaster");

        private async void BtnChasterLink_Click(object? sender, RoutedEventArgs e)
        {
            var chaster = ChasterHead.Service;
            if (chaster == null || chaster.IsLinking) return;
            try
            {
                if (chaster.IsLinked)
                {
                    if (Owner is { } owner) await Tabs.ChasterTabView.ConfirmAndUnlinkAsync(owner);
                }
                else
                {
                    Find<Button>("BtnChasterLink").IsEnabled = false;
                    var top = TopLevel.GetTopLevel(this);
                    // A sandbox never opens the real consent page: only its loopback stand-in.
                    await chaster.LinkAsync(url =>
                    {
                        if (ChasterHead.BrowserUrl(url) is { } u)
                            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Platform.ExternalOpener.OpenAsync(top, u));
                    });
                }
            }
            catch (Exception ex) { Log.Debug("chaster link from settings: {E}", ex.Message); }
            finally { RefreshChaster(); }
        }


        // Cloud settings backup: AccountSettingsSection.CloudBackup.cs.

        // Live (social#4): AccountSettingsSection.Providers.cs ExportDataAsync.
        private void BtnExportData_Click(object? sender, RoutedEventArgs e) => _ = ExportDataAsync();

        // Live: WPF used Process.Start with UseShellExecute, Avalonia's Launcher is the
        // cross-platform equivalent. Same two URLs MainWindow.CloudBackup.cs and
        // MainWindow.Patreon.cs carry.
        private void BtnPrivacyPolicy_Click(object? sender, RoutedEventArgs e) => OpenUrl(PrivacyPolicyUrl);
        private void BtnVisitPatreon_Click(object? sender, RoutedEventArgs e) => OpenUrl(PatreonUrl);
    }
}
