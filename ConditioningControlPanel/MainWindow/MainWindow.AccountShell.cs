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
    // Account shell: login, update, language, and app-info helpers.
    public partial class MainWindow
    {
        #region Account Shell

        private void BtnPatreonExclusives_Click(object sender, RoutedEventArgs e)
        {
            // The launcher popup submenu was replaced by the Exclusives tab
            // ("the Velvet Vault") — the button is a plain tab button now.
            ShowTab("exclusives");
        }

        // Walks up the visual tree (with a logical-tree fallback for content like
        // popups) checking whether `node` is `ancestor` or descended from it.
        private static bool IsVisualDescendant(DependencyObject? node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (node == ancestor) return true;
                // VisualTreeHelper.GetParent only accepts Visual/Visual3D; content
                // elements (Run, Hyperlink, Span, …) throw "is not a Visual or
                // Visual3D". A click whose OriginalSource is a Run (text inside a
                // TextBlock/Hyperlink) would otherwise crash here, so fall back to
                // the logical tree for non-visual nodes.
                DependencyObject? parent =
                    (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                        ? VisualTreeHelper.GetParent(node)
                        : null;
                parent ??= LogicalTreeHelper.GetParent(node);
                node = parent;
            }
            return false;
        }

        /// <summary>
        /// Opens the dashboard's "App Info &amp; Data" popup. This is the new home
        /// for account management (Patreon/Discord login, cloud backup, data
        /// export, privacy policy, support links) that used to live in the
        /// Patreon Exclusives tab.
        /// </summary>
        internal void ShowAppInfoPopup()
        {
            VelvetBtnAppInfo_Click(this, new RoutedEventArgs());
        }

        private void BtnAwareness_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("awareness");
        }



        private async void BtnQuickPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleQuickPatreonLoginAsync();
        }

        private async Task HandleQuickPatreonLoginAsync()
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                if (App.Discord?.IsAuthenticated != true && App.SubscribeStar?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Discord still active — just update Patreon UI
                    App.Patreon.UnifiedUserId = null;
                    UpdateQuickPatreonUI();
                    UpdatePatreonUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow (legacy - now use LoginDialog instead)
                try
                {
                    await App.Patreon.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "patreon");

                    if (result.Success)
                    {
                        UpdateQuickPatreonUI();
                        UpdatePatreonUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Patreon login failed");
                    MessageBox.Show(
                        $"Failed to connect to Patreon.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    UpdateQuickPatreonUI();
                }
            }
        }

        private void UpdateQuickPatreonUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();
        }

        private async void BtnQuickDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleDiscordLoginAsync();
        }

        private async Task HandleDiscordLoginAsync()
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true && App.SubscribeStar?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    App.Discord.UnifiedUserId = null;
                    UpdateQuickDiscordUI();
                    UpdateBannerWelcomeMessage();
                }
            }
            else
            {
                // Start OAuth flow
                SetDiscordButtonsEnabled(false);
                SetDiscordButtonsContent("Connecting...");

                try
                {
                    await App.Discord.StartOAuthFlowAsync();

                    // Use V2 unified account flow (v5.5+ with seasons system)
                    var result = await AccountService.HandlePostAuthV2Async(this, "discord");

                    if (result.Success)
                    {
                        UpdateQuickDiscordUI();
                        UpdateBannerWelcomeMessage();
                        UpdateAccountLinkingUI();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Discord login failed");
                    MessageBox.Show(
                        $"Failed to connect to Discord.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    SetDiscordButtonsEnabled(true);
                    UpdateQuickDiscordUI();
                }
            }
        }

        private void SetDiscordButtonsEnabled(bool enabled)
        {
            // Old quick button removed - now using unified login
        }

        private void SetDiscordButtonsContent(string text)
        {
            // Old quick button removed - now using unified login
        }

        private void UpdateQuickDiscordUI()
        {
            // Now managed by unified login panel
            UpdateQuickLoginUI();

            // Also update the Patreon tab Discord UI
            UpdateDiscordUI();
        }

        internal void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.gg/YxVAMt4qaZ",
                    UseShellExecute = true
                });
                App.Logger?.Information("Opened Discord invite link");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Discord link");
            }
        }


        internal void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Get the state from whichever checkbox was clicked
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;

            // Block enabling Rich Presence if Discord is not linked — prevents accidental
            // exposure for users who chose anonymous invite-code accounts
            if (isEnabled && App.Settings?.Current?.HasLinkedDiscord != true)
            {
                _isLoading = true;
                ProgressionTab.ChkDiscordRichPresence.IsChecked = false;
                SettingsTab.ChkQuickDiscordRichPresence.IsChecked = false;
                if (DiscordTab.ChkDiscordTabRichPresence != null) DiscordTab.ChkDiscordTabRichPresence.IsChecked = false;
                _isLoading = false;
                MessageBox.Show(Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"),
                    "Discord Not Linked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sync all checkboxes without re-entrancy
            _isLoading = true;
            ProgressionTab.ChkDiscordRichPresence.IsChecked = isEnabled;
            SettingsTab.ChkQuickDiscordRichPresence.IsChecked = isEnabled;
            if (DiscordTab.ChkDiscordTabRichPresence != null) DiscordTab.ChkDiscordTabRichPresence.IsChecked = isEnabled;
            _isLoading = false;

            App.Settings.Current.DiscordRichPresenceEnabled = isEnabled;

            if (App.DiscordRpc != null)
            {
                App.DiscordRpc.IsEnabled = isEnabled;
                App.Logger?.Information("Discord Rich Presence {Status}", isEnabled ? "enabled" : "disabled");
            }
        }


        private void InitializeLanguageSelector()
        {
            if (CmbLanguagePill == null) return;

            CmbLanguagePill.Items.Clear();
            int selectedIndex = 0;
            var currentLang = App.Settings?.Current?.Language ?? "en";

            for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
            {
                var (code, displayName, shortName) = LocalizationManager.AvailableLanguages[i];
                CmbLanguagePill.Items.Add(new ComboBoxItem
                {
                    Content = $"🌐 {shortName}",
                    Tag = code,
                    ToolTip = displayName
                });
                if (code == currentLang)
                    selectedIndex = i;
            }

            CmbLanguagePill.SelectedIndex = selectedIndex;
        }

        private void CmbLanguagePill_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguagePill?.SelectedItem is not ComboBoxItem selected) return;
            var langCode = selected.Tag as string ?? "en";

            if (App.Settings?.Current != null && App.Settings.Current.Language != langCode)
            {
                App.Settings.Current.Language = langCode;
                LocalizationManager.Instance.SetLanguage(langCode);
                App.Settings.Save();

                // XAML bindings update live; code-behind strings need a restart
                if (TxtBannerSecondary != null)
                {
                    TxtBannerSecondary.Text = Loc.Get("msg_restart_to_apply");
                    TxtBannerSecondary.Opacity = 1;
                    TxtBannerSecondary.IsHitTestVisible = true;
                }
            }
        }

        internal async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            ProgressionTab.BtnCheckUpdates.IsEnabled = false;
            ProgressionTab.BtnCheckUpdates.Content = Loc.Get("btn_checking");

            try
            {
                await App.CheckForUpdatesManuallyAsync(this);
            }
            finally
            {
                ProgressionTab.BtnCheckUpdates.IsEnabled = true;
                ProgressionTab.BtnCheckUpdates.Content = Loc.Get("btn_check_updates");
            }
        }

        private async void BtnUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            // If server provided a URL, open it in browser instead of auto-updating
            if (!string.IsNullOrEmpty(_serverUpdateUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _serverUpdateUrl,
                        UseShellExecute = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to open update URL: {Error}", ex.Message);
                }
            }

            // Trigger the update installation
            await App.CheckForUpdatesManuallyAsync(this);
        }

        /// <summary>
        /// Sets the update button state in the tab bar.
        /// Called from App when an update is detected or after checking.
        /// </summary>
        public void ShowUpdateAvailableButton(bool updateAvailable)
        {
            Dispatcher.Invoke(() =>
            {
                BtnUpdateAvailable.Tag = updateAvailable ? "UpdateAvailable" : "NoUpdate";
                BtnUpdateAvailable.Content = updateAvailable ? "UPDATE" : "LATEST VERSION :3";
                BtnUpdateAvailable.ToolTip = updateAvailable
                    ? "Update Available - Click to install!"
                    : "You're on the latest version";
            });
        }
        #endregion
    }
}
