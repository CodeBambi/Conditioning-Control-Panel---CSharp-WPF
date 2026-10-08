// Ported from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs (SetOfflineDisabled,
// UpdateOfflineModeUI, DisconnectNetworkServices) and MainWindow.xaml.cs SyncOfflineModeState.
// Not ported: Discord Rich Presence (no such service on this head) and SettingsTab.BtnUnifiedLogin
// (dead markup on this head, SettingsTabView.axaml "live code (BtnUnifiedLogin ...").

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF LoadSettings (MainWindow.Settings.cs:122): a saved offline mode greys the
        /// online controls from the first frame.</summary>
        private void InitializeOfflineModeUI()
        {
            if (CoreSettings.Current.OfflineMode) UpdateOfflineModeUI(true);
        }

        /// <summary>WPF SyncOfflineModeState / the tail of ChkOfflineMode_Changed: offline stops
        /// the network services, and either way the UI follows the saved flag.</summary>
        internal void SyncOfflineModeState()
        {
            var isOffline = CoreSettings.Current.OfflineMode;
            if (isOffline) DisconnectNetworkServices();
            UpdateOfflineModeUI(isOffline);
        }

        /// <summary>WPF DisconnectNetworkServices: the profile-sync heartbeat stops (server pings).</summary>
        private static void DisconnectNetworkServices()
        {
            try
            {
                Platform.AccountSeed.Sync?.StopHeartbeat();
                Log.Debug("Network services disconnected for offline mode");
            }
            catch (Exception ex) { Log.Warning(ex, "Error disconnecting network services"); }
        }

        /// <summary>Any named control under this window, across the tab/section name scopes.</summary>
        private T? Deep<T>(string name) where T : Control =>
            this.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

        /// <summary>WPF SetOfflineDisabled: greyed, and the tooltip says why (shown while disabled).</summary>
        private static void SetOfflineDisabled(Control? element, bool isOffline)
        {
            if (element == null) return;
            element.IsEnabled = !isOffline;
            element.Opacity = isOffline ? 0.5 : 1.0;
            ToolTip.SetShowOnDisabled(element, true);
            Features.SessionLock.ApplyLockToolTip(
                element, isOffline, isOffline ? Loc.Get("tooltip_disabled_in_offline_mode") : null);
        }

        private void UpdateOfflineModeUI(bool isOffline)
        {
            try
            {
                // Login buttons: Settings · Account and the Profile tab's privacy panel.
                SetOfflineDisabled(Deep<Button>("BtnPatreonLogin"), isOffline);
                SetOfflineDisabled(Deep<Button>("BtnDiscordLogin"), isOffline);
                SetOfflineDisabled(Deep<Button>("BtnDiscordTabLogin"), isOffline);

                // Browser card.
                var page = BrowserPage;
                SetOfflineDisabled(page?.FindControl<RadioButton>("RbBambiCloud"), isOffline);
                SetOfflineDisabled(page?.FindControl<RadioButton>("RbHypnoTube"), isOffline);
                SetOfflineDisabled(page?.FindControl<Button>("BtnPopOutBrowser"), isOffline);
                if (page?.FindControl<TextBlock>("TxtBrowserStatus") is { } status)
                {
                    status.Text = isOffline ? "● Offline" : "● Ready";
                    if (isOffline) status.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                    else status.Bind(TextBlock.ForegroundProperty, status.GetResourceObservable("PinkBrush"));
                }

                var loading = page?.FindControl<TextBlock>("BrowserLoadingText");
                var container = page?.FindControl<Grid>("BrowserContainer");
                var host = BrowserView;
                if (isOffline)
                {
                    // Stop whatever was loading, as WPF navigates to about:blank.
                    if (host?.Source != null) host.Navigate(new Uri("about:blank"));
                    if (loading != null)
                    {
                        loading.IsVisible = true;
                        loading.Text = Loc.Get("label_browser_disabled_in_offline_mode");
                    }
                    if (container != null) container.Opacity = 0.3;
                }
                else
                {
                    if (loading != null) loading.IsVisible = false;
                    if (container != null) container.Opacity = 1.0;
                    // WPF reloads the selected site only when the browser was already up.
                    if (host?.Source != null) NavigateBrowser(SiteHomeUrl());
                }

                UpdateBannerWelcomeMessage();
                Log.Debug("Offline mode UI updated: {State}", isOffline ? "disabled" : "enabled");
            }
            catch (Exception ex) { Log.Warning(ex, "Error updating offline mode UI"); }
        }
    }
}
