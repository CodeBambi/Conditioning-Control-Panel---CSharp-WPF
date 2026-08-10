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
        /// Opens Settings · Account: the sign-in / linking / cloud-backup / privacy page.
        /// </summary>
        /// <remarks>
        /// Phase 2 gave account management a real page, so this is one navigation instead of a
        /// popup that borrowed cards out of PatreonTab (gap-report R-2).
        /// </remarks>
        internal void ShowAccountSettings()
        {
            ShowTab("appsettings");
            AppSettingsTab?.FocusSection("account");
        }

        /// <summary>
        /// Compatibility entry point. Ten call sites — TierGate's "see tiers" action, the Programs
        /// / Blink Trainer / Lab / Awareness / FYP upsells, <c>BtnGateUnlock</c>, the
        /// <c>ShowTab("patreon")</c> redirect and the tutorial's <c>showPatreon</c> callback — all
        /// mean "send them where they can sign up". That destination is now Settings · Account, so
        /// the name is kept (it is reached from a service and from eight partials) and the body
        /// re-pointed, rather than touching ten files to say the same thing.
        ///
        /// The App Info popup itself still exists and is still opened by the dashboard's App Info
        /// tile — it is About + the three support forms now, and has no account content to send
        /// anyone to.
        /// </summary>
        internal void ShowAppInfoPopup()
        {
            ShowAccountSettings();
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
                SettingsTab.ChkQuickDiscordRichPresence.IsChecked = false;
                if (DiscordTab.ChkDiscordTabRichPresence != null) DiscordTab.ChkDiscordTabRichPresence.IsChecked = false;
                _isLoading = false;
                MessageBox.Show(Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"),
                    "Discord Not Linked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sync the surviving checkboxes without re-entrancy. Phase 8: the third copy
            // (ProgressionTab.ChkDiscordRichPresence) died with the ghost tab; the two live
            // surfaces are the Home quick toggle and the Profile tab's own switch. This handler
            // stays - Views/Controls/ProfilePrivacyPanel.xaml binds it.
            _isLoading = true;
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


        /// <summary>
        /// Guards the two language surfaces against each other. Populating a ComboBox and
        /// re-selecting it both raise SelectionChanged, so without this the chrome pill and the
        /// Settings · General list would ping-pong through <see cref="ApplyLanguageSelection"/>.
        /// </summary>
        private bool _syncingLanguageSelectors;

        /// <summary>
        /// Fills BOTH language surfaces. Owner decision #8 (PLAN §7) keeps the one-click pill in the
        /// window chrome and also lists languages on Settings · General; they are two surfaces over
        /// one code path, not two implementations. The pill shows short codes because it lives in a
        /// 32px-tall chrome slot; the settings list has room for the real language names.
        /// </summary>
        private void InitializeLanguageSelector()
        {
            PopulateLanguageCombo(CmbLanguagePill, shortLabels: true);
            PopulateLanguageCombo(AppSettingsTab?.CmbLanguageSetting, shortLabels: false);
        }

        private void PopulateLanguageCombo(ComboBox? combo, bool shortLabels)
        {
            if (combo == null) return;

            _syncingLanguageSelectors = true;
            try
            {
                combo.Items.Clear();
                int selectedIndex = 0;
                var currentLang = App.Settings?.Current?.Language ?? "en";

                for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
                {
                    var (code, displayName, shortName) = LocalizationManager.AvailableLanguages[i];
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = shortLabels ? $"🌐 {shortName}" : displayName,
                        Tag = code,
                        ToolTip = displayName
                    });
                    if (code == currentLang)
                        selectedIndex = i;
                }

                combo.SelectedIndex = selectedIndex;
            }
            finally { _syncingLanguageSelectors = false; }
        }

        private void CmbLanguagePill_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingLanguageSelectors) return;
            if (CmbLanguagePill?.SelectedItem is not ComboBoxItem selected) return;
            ApplyLanguageSelection(selected.Tag as string);
        }

        /// <summary>
        /// The single writer of <c>AppSettings.Language</c>. Called by the chrome pill and by
        /// Settings · General's <c>CmbLanguageSetting</c>; whichever fires, both are re-selected
        /// afterwards so the two surfaces can never disagree.
        /// </summary>
        internal void ApplyLanguageSelection(string? langCode)
        {
            if (_syncingLanguageSelectors) return;
            var code = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode!;

            if (App.Settings?.Current != null && App.Settings.Current.Language != code)
            {
                App.Settings.Current.Language = code;
                LocalizationManager.Instance.SetLanguage(code);
                App.Settings.Save();

                // XAML bindings update live; code-behind strings need a restart
                if (TxtBannerSecondary != null)
                {
                    TxtBannerSecondary.Text = Loc.Get("msg_restart_to_apply");
                    TxtBannerSecondary.Opacity = 1;
                    TxtBannerSecondary.IsHitTestVisible = true;
                }
            }

            SyncLanguageSelectors(code);
        }

        private void SyncLanguageSelectors(string langCode)
        {
            _syncingLanguageSelectors = true;
            try
            {
                Select(CmbLanguagePill);
                Select(AppSettingsTab?.CmbLanguageSetting);
            }
            finally { _syncingLanguageSelectors = false; }

            void Select(ComboBox? combo)
            {
                if (combo == null) return;
                foreach (var item in combo.Items)
                {
                    if (item is ComboBoxItem cbi && (cbi.Tag as string) == langCode)
                    {
                        combo.SelectedItem = cbi;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Phase 8: re-pointed, not deleted (audit ruling §1f).
        ///
        /// <para><b>Correction to that ruling, verified here:</b> the live Settings · Updates button
        /// does NOT route to this method. <c>UpdatesSettingsSection.xaml</c> binds
        /// <c>Click="BtnCheckUpdates_Click"</c> to that control's OWN private handler, which is
        /// self-contained (<c>App.CheckForUpdatesManuallyAsync</c>). So with ProgressionTab's copy
        /// deleted, this method currently has no binder at all.</para>
        ///
        /// <para>It is kept per the ruling rather than deleted, and re-aimed at
        /// <c>AppSettingsTab.BtnCheckUpdates</c> so that if it is ever re-bound it paints the
        /// "Checking…" affordance on the one surviving button instead of a deleted one. Note that
        /// affordance has therefore never been visible to users - a pre-existing gap, not a Phase 8
        /// regression. If it is wanted, add it to UpdatesSettingsSection's own handler.</para>
        /// </summary>
        internal async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var btn = AppSettingsTab?.BtnCheckUpdates;
            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = Loc.Get("btn_checking");
            }

            try
            {
                await App.CheckForUpdatesManuallyAsync(this);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = Loc.Get("btn_check_updates");
                }
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
