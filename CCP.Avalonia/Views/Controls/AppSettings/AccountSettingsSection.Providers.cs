using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// Settings &gt; Account provider rows (social#2) and the GDPR export (social#4), ported from WPF
    /// MainWindow.Patreon.cs (BtnPatreonLogin/BtnDiscordLogin/BtnLink*, UpdatePatreonUI, UpdateDiscordUI,
    /// UpdateAccountLinkingUI), MainWindow.SubscribeStar.cs and MainWindow.CloudBackup.cs (BtnExportData).
    /// The OAuth half is Core's (ProviderSubscription / DiscordAccount.SignInAsync), the link call
    /// <see cref="AccountLink"/>. Cloud settings backup stays hidden: no backup service on this head yet.
    /// </summary>
    public partial class AccountSettingsSection
    {
        private static readonly IBrush PatreonRedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x42, 0x4D));

        private MainShellWindow? Shell => TopLevel.GetTopLevel(this) as MainShellWindow;
        private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

        private static bool HasUnifiedId => !string.IsNullOrEmpty(CoreSettings.Current?.UnifiedId);

        /// <summary>Linking needs the id AND its token. An id whose token is gone (cleared secret store, a
        /// copied profile) cannot link (the server answers "Invalid or missing auth token"), so the provider
        /// button runs the full sign-in instead, which mints a fresh token.</summary>
        private static bool CanLink => HasUnifiedId && !string.IsNullOrEmpty(CoreSettings.Current?.AuthToken);

        private T? Find<T>(string name) where T : Control => this.FindControl<T>(name);

        /// <summary>WPF UpdatePatreonUI + UpdateDiscordUI + UpdateSubscribeStarUI + UpdateAccountLinkingUI. Never throws.</summary>
        internal void RefreshProviderRows()
        {
            try
            {
                var s = CoreSettings.Current;
                var hasUnifiedId = HasUnifiedId;

                // Patreon
                var patreon = AccountSeed.Patreon;
                if (patreon?.IsAuthenticated == true)
                {
                    var name = s?.UserDisplayName ?? patreon.DisplayName;
                    SetText("TxtPatreonStatus", string.IsNullOrEmpty(name) ? "Connected to Patreon" : $"Welcome, {name}!");
                    SetText("TxtPatreonTier", TierLine(patreon));
                    SetContent("BtnPatreonLogin", Loc.Get("btn_logout"));
                }
                else
                {
                    SetText("TxtPatreonStatus", Loc.Get("label_not_connected"));
                    SetText("TxtPatreonTier", Loc.Get("label_login_to_unlock_exclusive_features"));
                    SetContent("BtnPatreonLogin", hasUnifiedId ? "Link Patreon" : "Login");
                }

                // SubscribeStar
                var sub = AccountSeed.SubscribeStar;
                if (sub?.IsAuthenticated == true)
                {
                    var name = s?.UserDisplayName ?? sub.DisplayName;
                    SetText("TxtSubscribeStarStatus", string.IsNullOrEmpty(name) ? "Connected to SubscribeStar" : $"Welcome, {name}!");
                    SetText("TxtSubscribeStarTier", TierLine(sub));
                    SetContent("BtnSubscribeStarLogin", Loc.Get("btn_logout"));
                }
                else
                {
                    SetText("TxtSubscribeStarStatus", Loc.Get("label_not_connected"));
                    SetText("TxtSubscribeStarTier", Loc.Get("label_login_to_unlock_exclusive_features"));
                    SetContent("BtnSubscribeStarLogin", Loc.Get("btn_login"));
                }

                // Discord
                var discord = AccountSeed.Discord;
                if (discord?.IsAuthenticated == true)
                {
                    SetText("TxtDiscordStatus", $"Connected as {s?.UserDisplayName ?? discord.DisplayName}");
                    SetText("TxtDiscordInfo", $"@{discord.Username}");
                    SetContent("BtnDiscordLogin", Loc.Get("btn_logout"));
                }
                else
                {
                    SetText("TxtDiscordStatus", Loc.Get("label_not_connected"));
                    SetText("TxtDiscordInfo", Loc.Get("label_link_discord_for_community_features"));
                    SetContent("BtnDiscordLogin", hasUnifiedId ? "Link Discord" : "Login");
                }

                // Link Accounts (WPF UpdateAccountLinkingUI, PatreonReconnectRule owns the Patreon row).
                var hasLinkedDiscord = s?.HasLinkedDiscord == true || discord?.IsAuthenticated == true;
                var row = PatreonReconnectRule.Decide(
                    hasUnifiedId: hasUnifiedId,
                    linkedServerSide: s?.HasLinkedPatreon == true,
                    desktopAuthenticated: patreon?.IsAuthenticated == true && patreon.GrantLooksDead != true,
                    hasPremiumNow: CoreAccount.HasPremiumAccess,
                    whitelisted: patreon?.IsWhitelisted == true);
                SetVisible("AccountLinkingSection", hasUnifiedId && (row.ShowsButton || !hasLinkedDiscord));
                if (Find<Button>("BtnLinkPatreon") is { } link)
                {
                    link.IsVisible = row.ShowsButton;
                    link.Content = Loc.Get(row.Action == PatreonLinkAction.Reconnect ? "btn_reconnect_patreon" : "btn_link_patreon");
                    link.Background = row.Filled ? PatreonRedBrush : Brushes.Transparent;
                    link.Foreground = row.Filled ? Brushes.White : PatreonRedBrush;
                    link.BorderBrush = PatreonRedBrush;
                    link.BorderThickness = new global::Avalonia.Thickness(row.Filled ? 0 : 1);
                }
                SetVisible("TxtPatreonReconnectHint", row.ShowsHint);
                SetVisible("BtnLinkDiscord", hasUnifiedId && !hasLinkedDiscord);

                // WPF shows backup + data privacy with a cloud identity. Backup has no service here
                // (social#3): its buttons would be dead, so only the data privacy card opens.
                SetVisible("DataPrivacySection", hasUnifiedId);
            }
            catch (Exception ex)
            {
                Log.Debug("AccountSettingsSection.RefreshProviderRows failed: {E}", ex.Message);
            }
        }

        private static string TierLine(ProviderSubscription p) => p.CurrentTier switch
        {
            PatreonTier.Level2 => Loc.Get("label_patreon_tier_level2"),
            PatreonTier.Level1 => Loc.Get("label_patreon_tier_level1"),
            _ when p.IsWhitelisted => Loc.Get("label_patreon_tier_whitelisted"),
            _ => Loc.Get(p.IsActive ? "label_patreon_tier_patron" : "label_patreon_tier_connected"),
        };

        private void SetText(string name, string text) { if (Find<TextBlock>(name) is { } t) t.Text = text; }
        private void SetContent(string name, string text) { if (Find<Button>(name) is { } b) b.Content = text; }
        private void SetVisible(string name, bool on) { if (Find<Control>(name) is { } c) c.IsVisible = on; }

        /// <summary>Every surface a sign-in, link or sign-out moves (WPF ran the same set on each path).</summary>
        private void AfterAccountChange(bool accountChanged)
        {
            Shell?.UpdateQuickLoginUI(accountChanged);
            RefreshTierBadge();
            RefreshProviderRows();
        }

        // ---- login buttons (WPF BtnPatreonLogin_Click / BtnDiscordLogin_Click / BtnSubscribeStarLogin_Click) ----

        /// <summary>Signed in with this provider: sign it out (the whole account when none is left). Signed in with
        /// another: link this one. Signed out: the unified login dialog.</summary>
        internal async Task ProviderLoginAsync(string provider)
        {
            var btn = Find<Button>(provider switch { "patreon" => "BtnPatreonLogin", "discord" => "BtnDiscordLogin", _ => "BtnSubscribeStarLogin" });
            await ProviderButtonAsync(provider, OwnerWindow, btn);
            AfterAccountChange(accountChanged: false);
        }

        /// <summary>The shared flow behind every provider login button (Settings &gt; Account, the Profile tab's
        /// Discord button). The caller repaints its own surface after; the shell's login UI is repainted here.</summary>
        internal static async Task ProviderButtonAsync(string provider, Window? owner, Button? btn)
        {
            var shell = owner as MainShellWindow;
            bool authenticated = provider switch
            {
                "patreon" => AccountSeed.Patreon?.IsAuthenticated == true,
                "discord" => AccountSeed.Discord?.IsAuthenticated == true,
                _ => AccountSeed.SubscribeStar?.IsAuthenticated == true,
            };
            if (authenticated)
            {
                AccountSeed.LogoutProvider(provider);
                bool anyLeft = AccountSeed.Patreon?.IsAuthenticated == true || AccountSeed.Discord?.IsAuthenticated == true
                               || AccountSeed.SubscribeStar?.IsAuthenticated == true;
                if (!anyLeft)
                {
                    // WPF ClearAccountData = the shell's Logout, awaited here so the caller repaints after it.
                    await AccountSeed.Logout();
                    shell?.UpdateQuickLoginUI(accountChanged: true);
                    if (SecretStore.ClearFailed && owner != null)
                        await MessageDialog.ShowAsync(owner, Loc.Get("title_error"), Loc.Get("logout_not_complete_body"));
                }
                else
                {
                    if (provider == "patreon" && AccountSeed.Patreon is { } p) p.UnifiedUserId = null;
                    shell?.UpdateQuickLoginUI(accountChanged: false);
                }
                return;
            }

            // SubscribeStar always goes through the dialog (WPF: it establishes the real account there).
            if (provider != "substar" && CanLink)
            {
                await LinkFlowAsync(provider, owner, btn);
                return;
            }
            if (shell != null) await shell.OpenUnifiedLoginDialog();
        }

        /// <summary>WPF BtnLinkPatreon_Click / BtnLinkDiscord_Click: OAuth in the browser, then link to the
        /// signed-in unified account.</summary>
        internal async Task LinkProviderAsync(string provider, string buttonName)
        {
            await LinkFlowAsync(provider, OwnerWindow, Find<Button>(buttonName));
            AfterAccountChange(accountChanged: false);
        }

        internal static async Task LinkFlowAsync(string provider, Window? owner, Button? btn)
        {
            if (btn is { IsEnabled: false }) return;   // a link is already in flight
            if (btn != null) { btn.IsEnabled = false; btn.Content = Loc.Get("login_connecting"); }
            try
            {
                if (!HasUnifiedId)
                {
                    if (owner != null) await MessageDialog.ShowAsync(owner, Loc.Get("account_not_logged_in_title"), Loc.Get("account_login_first"));
                    return;
                }
                if (!CanLink)
                {
                    // The id survived but its token did not: sign in again (mints the token) rather than a
                    // link the server can only refuse.
                    Log.Warning("Account: {Provider} link asked with no auth token; opening sign-in", provider);
                    if (owner is MainShellWindow shellOwner) await shellOwner.OpenUnifiedLoginDialog();
                    return;
                }
                Action<string> open = url => { if (ExternalOpener.Allowed(url)) _ = ExternalOpener.OpenAsync(owner, url); };
                if (provider == "patreon") { if (AccountSeed.Patreon is { } p) await p.SignInAsync(open); }
                else if (AccountSeed.Discord is { } d) await d.SignInAsync(open);

                var (outcome, error) = await AccountLink.LinkAsync(provider);
                switch (outcome)
                {
                    case AccountLink.Outcome.AlreadyLinked when provider == "patreon":
                        // A patron repairing a dead grant: its own word, no dialog (WPF account_patreon_reconnected).
                        OsNotifications.Show(Loc.Get("app_title"), Loc.Get("account_patreon_reconnected"));
                        _ = AccountSeed.Patreon?.ValidateSubscriptionAsync(forceRefresh: true);
                        break;
                    case AccountLink.Outcome.Linked or AccountLink.Outcome.AlreadyLinked:
                        if (owner != null) await MessageDialog.ShowAsync(owner, Loc.Get("account_linked_title"), Loc.GetF("account_linked_success", provider));
                        break;
                    case AccountLink.Outcome.Failed:
                        if (owner != null) await MessageDialog.ShowAsync(owner, Loc.Get("account_link_failed_title"),
                            error ?? Loc.GetF("account_link_failed_generic", provider));
                        break;
                }
            }
            catch (OperationCanceledException) { /* the user closed the browser flow */ }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to link {Provider}", provider);
                if (owner != null) await MessageDialog.ShowAsync(owner, Loc.Get("account_link_failed_title"),
                    Loc.GetF("account_link_failed_generic", provider) + "\n\n" + ex.Message);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
                (owner as MainShellWindow)?.UpdateQuickLoginUI(accountChanged: false);
            }
        }

        // ---- GDPR export (WPF MainWindow.CloudBackup.cs BtnExportData_Click) ----

        /// <summary>Tests only: the client the export uses.</summary>
        internal static Func<V2AuthService> NewExportClient = () => AccountSeed.NewV2();

        internal async Task ExportDataAsync()
        {
            var btn = Find<Button>("BtnExportData");
            if (btn is { IsEnabled: false } || OwnerWindow is not { } owner) return;
            if (btn != null) { btn.IsEnabled = false; btn.Content = Loc.Get("btn_exporting"); }
            try
            {
                var (success, error, json) = await NewExportClient().ExportDataAsync(CoreSettings.Current?.UnifiedId);
                if (!success || json == null)
                {
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_export_failed"), error ?? Loc.Get("msg_failed_to_export_data"));
                    return;
                }
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = Loc.Get("title_save_data_export"),
                    SuggestedFileName = $"my-data-export-{DateTime.Now:yyyy-MM-dd}.json",
                    DefaultExtension = ".json",
                    FileTypeChoices = new[] { new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } } },
                });
                if (file?.TryGetLocalPath() is not { } path) return;
                await File.WriteAllTextAsync(path, json);
                await MessageDialog.ShowAsync(owner, Loc.Get("title_export_complete"), Loc.GetF("msg_data_exported_to_0", path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Data export failed");
                await MessageDialog.ShowAsync(owner, Loc.Get("title_export_error"), Loc.GetF("msg_export_failed_0", ex.Message));
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = Loc.Get("btn_export_my_data"); }
            }
        }
    }
}
