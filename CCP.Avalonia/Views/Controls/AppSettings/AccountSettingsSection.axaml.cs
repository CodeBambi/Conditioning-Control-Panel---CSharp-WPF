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
            Refresh();
            // Repaint on whatever moves a card (WPF: Loaded/IsVisibleChanged, TierChanged via
            // UpdatePatreonUI, Chaster ProfileChanged). Subscribed only while attached (P41).
            EventHandler changed = (_, _) => Post();
            EventHandler<PatreonTier> tier = (_, _) => Post();
            Action chasterChanged = () => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshChaster);
            ChasterService? chaster = null;
            AttachedToVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged += changed;
                if (AccountSeed.Patreon is { } p) p.TierChanged += tier;
                if (AccountSeed.SubscribeStar is { } ss) ss.TierChanged += tier;
                if ((chaster = ChasterHead.Service) is { } c) { c.ProfileChanged += chasterChanged; c.LinkChanged += chasterChanged; c.LockChanged += chasterChanged; }
                Refresh();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged -= changed;
                if (AccountSeed.Patreon is { } p) p.TierChanged -= tier;
                if (AccountSeed.SubscribeStar is { } ss) ss.TierChanged -= tier;
                if (chaster is { } c) { c.ProfileChanged -= chasterChanged; c.LinkChanged -= chasterChanged; c.LockChanged -= chasterChanged; }
                chaster = null;
            };
        }

        /// <summary>
        /// Host seam: the Settings door repaints the tier card every time it opens, so a login that
        /// happened behind another door is never shown stale.
        /// </summary>
        public void OnSectionShown() => Refresh();

        private void Post() => global::Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

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

        private Window? Owner => TopLevel.GetTopLevel(this) as Window;
        private MainShellWindow? Shell => TopLevel.GetTopLevel(this) as MainShellWindow;

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

        /// <summary>Every card on the section, as WPF's OnSectionShown and the auth choke points
        /// (UpdatePatreonUI/UpdateDiscordUI/UpdateSubscribeStarUI/UpdateAccountLinkingUI) repaint it.</summary>
        public void Refresh()
        {
            RefreshTierBadge();
            try { RefreshProviders(); RefreshLinking(); }
            catch (Exception ex) { Log.Debug("AccountSettingsSection.RefreshProviders failed: {E}", ex.Message); }
            RefreshChaster();
            RefreshFriendsPresence();
        }

        private static string Tier(PatreonTier tier, bool whitelisted, bool active) => tier switch
        {
            PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
            PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
            _ when whitelisted => Loc.Get("label_patreon_tier_whitelisted"),
            _ => Loc.Get(active ? "label_patreon_tier_patron" : "label_patreon_tier_connected"),
        };

        /// <summary>WPF MainWindow.Patreon.cs:284 UpdatePatreonUI, :578 UpdateDiscordUI and
        /// MainWindow.SubscribeStar.cs:57 UpdateSubscribeStarUI - the AppSettingsTab halves. WPF's
        /// hardcoded English ("Welcome, X!", "Connected as X", "Link Patreon") goes through Loc here.</summary>
        private void RefreshProviders()
        {
            var s = CoreSettings.Current;
            var hasUnifiedId = !string.IsNullOrEmpty(s.UnifiedId);

            void Sub(ProviderSubscription? sub, string card, string connectedKey, bool offerLink)
            {
                var status = Find<TextBlock>($"Txt{card}Status");
                var tier = Find<TextBlock>($"Txt{card}Tier");
                var label = Find<TextBlock>($"TxtBtn{card}Login");
                if (sub?.IsAuthenticated == true)
                {
                    var name = s.UserDisplayName ?? sub.DisplayName;
                    status.Text = string.IsNullOrEmpty(name) ? Loc.Get(connectedKey) : Loc.GetF("label_welcome", name);
                    tier.Text = Tier(sub.CurrentTier, sub.IsWhitelisted, sub.IsActive);
                    label.Text = Loc.Get("btn_logout");
                }
                else
                {
                    status.Text = Loc.Get("label_not_connected");
                    tier.Text = Loc.Get("label_login_to_unlock_exclusive_features");
                    label.Text = Loc.Get(offerLink && hasUnifiedId ? "btn_link_patreon" : "btn_login");
                }
            }
            Sub(AccountSeed.Patreon, "Patreon", "account_connected_to_patreon", offerLink: true);
            Sub(AccountSeed.SubscribeStar, "SubscribeStar", "account_connected_to_substar", offerLink: false);

            var discord = AccountSeed.Discord;
            if (discord?.IsAuthenticated == true)
            {
                Find<TextBlock>("TxtDiscordStatus").Text = Loc.GetF("account_connected_as", s.UserDisplayName ?? discord.DisplayName ?? "");
                Find<TextBlock>("TxtDiscordInfo").Text = $"@{discord.Username}";
                Find<TextBlock>("TxtBtnDiscordLogin").Text = Loc.Get("btn_logout");
            }
            else
            {
                Find<TextBlock>("TxtDiscordStatus").Text = Loc.Get("label_not_connected");
                Find<TextBlock>("TxtDiscordInfo").Text = Loc.Get("label_link_discord_for_community_features");
                Find<TextBlock>("TxtBtnDiscordLogin").Text = Loc.Get(hasUnifiedId ? "btn_link_discord" : "btn_login");
            }
        }

        private static readonly IBrush PatreonRedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x42, 0x4D));

        /// <summary>WPF MainWindow.Patreon.cs UpdateAccountLinkingUI: the linking row, its Reconnect
        /// state (PatreonReconnectRule owns the decision) and the cloud-identity sections.</summary>
        private void RefreshLinking()
        {
            var s = CoreSettings.Current;
            var patreon = AccountSeed.Patreon;
            var hasUnifiedId = !string.IsNullOrEmpty(s.UnifiedId);
            var hasLinkedDiscord = s.HasLinkedDiscord || AccountSeed.Discord?.IsAuthenticated == true;
            var row = PatreonReconnectRule.Decide(
                hasUnifiedId: hasUnifiedId,
                linkedServerSide: s.HasLinkedPatreon,
                desktopAuthenticated: patreon?.IsAuthenticated == true && !patreon.GrantLooksDead,
                hasPremiumNow: CoreAccount.HasPremiumAccess,
                whitelisted: patreon?.IsWhitelisted == true);

            Find<Border>("AccountLinkingSection").IsVisible = hasUnifiedId && (row.ShowsButton || !hasLinkedDiscord);
            var link = Find<Button>("BtnLinkPatreon");
            link.IsVisible = row.ShowsButton;
            if (link.IsEnabled)
                Find<TextBlock>("TxtBtnLinkPatreon").Text = Loc.Get(row.Action == PatreonLinkAction.Reconnect ? "btn_reconnect_patreon" : "btn_link_patreon");
            link.Background = row.Filled ? PatreonRedBrush : Brushes.Transparent;
            link.Foreground = row.Filled ? Brushes.White : PatreonRedBrush;
            link.BorderBrush = PatreonRedBrush;
            link.BorderThickness = new Thickness(row.Filled ? 0 : 1);
            Find<TextBlock>("TxtPatreonReconnectHint").IsVisible = row.ShowsHint;
            var linkDiscord = Find<Button>("BtnLinkDiscord");
            linkDiscord.IsVisible = hasUnifiedId && !hasLinkedDiscord;
            // WPF BtnLinkDiscord_Click finally: the label comes back after a failed/cancelled link.
            if (linkDiscord.IsEnabled) Find<TextBlock>("TxtBtnLinkDiscord").Text = Loc.Get("btn_link_discord");
            // ponytail: CloudSettingsBackupSection stays hidden and BtnExportData collapsed until
            // ProfileSync's backup/restore/export calls reach this head (MainShellWindow.CloudBackup.cs).
            Find<Border>("DataPrivacySection").IsVisible = hasUnifiedId;
        }

        // ---- provider buttons (WPF MainWindow.Patreon.cs:455/526, MainWindow.SubscribeStar.cs:92) ----

        /// <summary>Signed in: sign this provider out (the whole account when none is left). Signed
        /// out with a cloud identity: link it. Otherwise: the unified login dialog.</summary>
        private async Task ProviderClickAsync(string provider, Button button, string labelName)
        {
            bool authed = provider switch
            {
                "discord" => AccountSeed.Discord?.IsAuthenticated == true,
                "substar" => AccountSeed.SubscribeStar?.IsAuthenticated == true,
                _ => AccountSeed.Patreon?.IsAuthenticated == true,
            };
            if (authed)
            {
                if (provider == "patreon") AccountSeed.Sync?.StopHeartbeat(); // WPF MainWindow.Patreon.cs:462
                AccountSeed.LogoutProvider(provider);
                var anyLeft = AccountSeed.Patreon?.IsAuthenticated == true || AccountSeed.Discord?.IsAuthenticated == true
                              || AccountSeed.SubscribeStar?.IsAuthenticated == true;
                if (!anyLeft) { if (Shell is { } mw) await mw.LogoutAsync(); else await AccountSeed.Logout(); }
                else
                {
                    if (provider == "patreon" && AccountSeed.Patreon is { } p) p.UnifiedUserId = null;
                    if (provider == "discord" && AccountSeed.Discord is { } d) d.UnifiedUserId = null;
                    Shell?.UpdateQuickLoginUI(accountChanged: true);
                }
            }
            else if (provider != "substar" && !string.IsNullOrEmpty(CoreSettings.Current.UnifiedId))
                await LinkAsync(provider, button, labelName);
            else if (Shell is { } mw)
                await mw.OpenUnifiedLoginDialog();
            Refresh();
        }

        private async void BtnPatreonLogin_Click(object? sender, RoutedEventArgs e) => await Guard(ProviderClickAsync("patreon", Find<Button>("BtnPatreonLogin"), "TxtBtnPatreonLogin"));
        private async void BtnSubscribeStarLogin_Click(object? sender, RoutedEventArgs e) => await Guard(ProviderClickAsync("substar", Find<Button>("BtnSubscribeStarLogin"), "TxtBtnSubscribeStarLogin"));
        private async void BtnDiscordLogin_Click(object? sender, RoutedEventArgs e) => await Guard(ProviderClickAsync("discord", Find<Button>("BtnDiscordLogin"), "TxtBtnDiscordLogin"));
        private async void BtnLinkPatreon_Click(object? sender, RoutedEventArgs e) => await Guard(LinkAsync("patreon", Find<Button>("BtnLinkPatreon"), "TxtBtnLinkPatreon"));
        private async void BtnLinkDiscord_Click(object? sender, RoutedEventArgs e) => await Guard(LinkAsync("discord", Find<Button>("BtnLinkDiscord"), "TxtBtnLinkDiscord"));

        private static async Task Guard(Task work)
        {
            try { await work; }
            catch (Exception ex) { Log.Warning(ex, "Account settings action failed"); }
        }

        /// <summary>Tests only: the dialog every link outcome is told through (WPF MessageBox.Show).</summary>
        internal static Func<Window?, string, string, Task> Tell = async (owner, title, body) =>
        {
            if (owner != null) await MessageDialog.ShowAsync(owner, title, body);
        };

        /// <summary>WPF BtnLinkPatreon_Click/BtnLinkDiscord_Click + AccountService.LinkProviderV2Async:
        /// the provider's OAuth, then the V2 link call. A sandbox never opens a real provider page.</summary>
        internal async Task LinkAsync(string provider, Button button, string labelName)
        {
            if (string.IsNullOrEmpty(CoreSettings.Current.UnifiedId))
            {
                await Tell(Owner, Loc.Get("account_not_logged_in_title"), Loc.Get("account_login_first"));
                return;
            }
            button.IsEnabled = false;
            Find<TextBlock>(labelName).Text = Loc.Get("login_connecting");
            var top = TopLevel.GetTopLevel(this);
            Action<string> open = url =>
            {
                if (Platform.ExternalOpener.Allowed(url))
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Platform.ExternalOpener.OpenAsync(top, url));
            };
            try
            {
                string? token;
                if (provider == "discord")
                {
                    if (AccountSeed.Discord is not { } d) return;
                    await d.SignInAsync(open);
                    token = d.GetAccessToken();
                }
                else
                {
                    if (AccountSeed.Patreon is not { } p) return;
                    await p.SignInAsync(open);
                    token = p.GetAccessToken();
                }
                if (string.IsNullOrEmpty(token)) return;

                var s = CoreSettings.Current;
                var result = await AccountSeed.NewV2().LinkProviderAsync(s.UnifiedId!, provider, token);
                var alreadyLinked = !result.Success && ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(result.Error);
                if (!result.Success && !alreadyLinked)
                {
                    Log.Warning("Failed to link {Provider}: {Error}", provider, result.Error);
                    await Tell(Owner, Loc.Get("account_link_failed_title"), result.Error ?? Loc.GetF("account_link_failed_generic", provider));
                    return;
                }
                if (provider == "discord") s.HasLinkedDiscord = true; else s.HasLinkedPatreon = true;
                if (!string.IsNullOrEmpty(result.AuthToken)) s.AuthToken = result.AuthToken;
                CoreSettings.Save();
                if (alreadyLinked && provider == "patreon")
                {
                    // A patron repairing a dead grant: its own word, no dialog (WPF AccountService).
                    App.Notifications.Show(Loc.Get("account_patreon_reconnected"), Helpers.NotificationType.Success, TimeSpan.FromSeconds(6));
                    _ = AccountSeed.Patreon?.ValidateSubscriptionAsync(forceRefresh: true);
                }
                else
                    await Tell(Owner, Loc.Get("account_linked_title"), Loc.GetF("account_linked_success", provider));
                Shell?.UpdateQuickLoginUI();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to link {Provider}", provider);
                await Tell(Owner, Loc.Get("account_link_failed_title"), Loc.GetF("account_link_failed_generic", provider) + "\n\n" + ex.Message);
            }
            finally
            {
                button.IsEnabled = true;
                Refresh();
            }
        }

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

        // ---- friends presence (WPF FriendsPresenceSetting.Read/Write) ----

        private bool _refreshingPresence;

        internal void RefreshFriendsPresence()
        {
            _refreshingPresence = true;
            try
            {
                var svc = FriendsHead.Service;
                bool shared;
                // WPF FriendsPresenceSetting.Read: a throwing service falls back to the saved setting.
                try { shared = svc != null ? svc.PresenceShared : CoreSettings.Current.FriendsPresenceShared; }
                catch { shared = CoreSettings.Current.FriendsPresenceShared; }
                Find<CheckBox>("ChkFriendsPresence").IsChecked = shared;
            }
            finally { _refreshingPresence = false; }
        }

        private void ChkFriendsPresence_Changed(object? sender, RoutedEventArgs e)
        {
            if (_refreshingPresence) return;
            var on = Find<CheckBox>("ChkFriendsPresence").IsChecked == true;
            var done = false;
            try { if (FriendsHead.Service is { } svc) { svc.PresenceShared = on; done = true; } }
            catch (Exception ex) { Log.Debug("[Friends] presence write failed: {E}", ex.Message); }
            if (!done)
            {
                CoreSettings.Current.FriendsPresenceShared = on;
                CoreSettings.Save();
            }
            PresenceAsk.MarkAsked();
        }

        // ponytail: cloud settings backup - ProfileSyncService.BackupSettingsAsync /
        // GetSettingsBackupInfoAsync / RestoreSettingsFromCloudAsync, all still in the WPF head.
        // CloudSettingsBackupSection stays hidden until they land (MainShellWindow.CloudBackup.cs).
        private void BtnBackupSettingsNow_Click(object? sender, RoutedEventArgs e) { }
        private void BtnRestoreSettings_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: GDPR export - ProfileSyncService.ExportDataAsync plus a save-file picker; the
        // button stays collapsed until that transport reaches Core.
        private void BtnExportData_Click(object? sender, RoutedEventArgs e) { }

        // Live: WPF used Process.Start with UseShellExecute, Avalonia's Launcher is the
        // cross-platform equivalent. Same two URLs MainWindow.CloudBackup.cs and
        // MainWindow.Patreon.cs carry.
        private void BtnPrivacyPolicy_Click(object? sender, RoutedEventArgs e) => OpenUrl(PrivacyPolicyUrl);
        private void BtnVisitPatreon_Click(object? sender, RoutedEventArgs e) => OpenUrl(PatreonUrl);
    }
}
