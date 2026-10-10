// PORTED from WPF 7.1.5, the Home browser card's header row (ledger P6 / H6):
//   BtnMuteBrowser_Click + SyncBrowserMuteIcon   MainWindow.Browser.cs :2629, :2639
//   BtnWebcamTracking_Click + its button refresh  MainWindow.DeeperTab.cs :245
//   ChkForceShowBambiCloud_Changed                MainWindow.DeeperTab.cs :505
// Mute rides the WebHost script seam (Views/Controls/WebHostMedia.cs) and is re-applied after every
// navigation, since a new document has no flag.
// Lane k16 (HA6):
//   BtnPopOutBrowser_Click      MainWindow.Browser.cs :2645. WPF re-parents the one WebView2 into a
//     1024x768 window; NativeWebView cannot be re-parented live, so the pop-out window gets its OWN
//     WebHost on the same profile (same sign-ins), opened on the page the card was showing. The
//     card's host is paused and hidden behind the "popped out" prompt, and takes the pop-out's page
//     back when the window closes. Difference from WPF: the page reloads on the way out and on the
//     way back (a playing video starts over). The shell's BrowserView follows LiveBrowserHost, so
//     navigation, pause and mute reach whichever surface is live.
//   BrowserLoadingText_Click    MainWindow.Browser.cs :280. WPF builds the WebView2 on that click;
//     here the engine exists from the start but nothing is loaded until asked, so the prompt stands
//     in the slot until the first navigation and a click loads the selected site (offline gate first).
// not ported: "Enhance if possible" (WPF BrowserEnhanceBridge matches the playing video against the
// Deeper library; there is no bridge on this head). Greyed with the "not on this build" line
// instead of sitting there dead. Also not ported: the pop-out's fullscreen rig
// (HandleBrowserFullscreenChanged: WebHost reports no fullscreen signal).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCardSyncing;

        /// <summary>Tests: how many times the mute script was sent to the page.</summary>
        internal int BrowserMuteApplies { get; private set; }

        /// <summary>Tests: stands in for the tracker (running?, toggle).</summary>
        internal Func<bool>? WebcamRunningOverride { get; set; }

        private void HookBrowserCard()
        {
            BrowserWebHost.NavigationCompleted += _ => ApplyBrowserMute();
            AttachedToVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                Platform.WebcamTracker.Instance.StateChanged += OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
                CoreMods.ModChanged += OnBrowserCardModChanged;
                SyncBrowserCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
            };

            BrowserWebHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == WebHost.SourceProperty) SyncBrowserPrompt();
            };
            DetachedFromVisualTree += (_, _) => CloseBrowserPopout();   // the page left: its window goes too

            // No host for this one on this head: greyed with the reason, never a dead click.
            foreach (Control c in new Control[] { ToggleEnhanceIfPossible })
            {
                c.IsEnabled = false;
                c.Bind(ToolTip.TipProperty, new Binding("[exclusives_not_on_this_build]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                ToolTip.SetShowOnDisabled(c, true);
            }
            SyncBrowserCard();
        }

        private void OnBrowserCardTrackerChanged() => Dispatcher.UIThread.Post(RefreshBrowserWebcamButton);
        private void OnBrowserCardModChanged(object? sender, ModPackage? mod) => Dispatcher.UIThread.Post(SyncBrowserCard);

        /// <summary>The saved state into the row: the mute glyph, the webcam pill, the BambiCloud
        /// override box and the radio it reveals.</summary>
        internal void SyncBrowserCard()
        {
            try
            {
                _browserCardSyncing = true;
                try { ChkForceShowBambiCloud.IsChecked = CoreSettings.Current.ForceShowBambiCloud; }
                finally { _browserCardSyncing = false; }
                SyncBrowserMuteIcon();
                RefreshBrowserWebcamButton();
                ApplyBambiCloudVisibility(navigateIfHidden: false);
                SyncBrowserPrompt();
            }
            catch (Exception ex) { Log.Debug("SyncBrowserCard: {E}", ex.Message); }
        }

        // ------------------------------------------------------------------ mute

        /// <summary>WPF BtnMuteBrowser_Click: flip the saved flag, apply it live, repaint the glyph.</summary>
        private void BtnMuteBrowser_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var s = CoreSettings.Current;
                s.BrowserVideoMuted = !s.BrowserVideoMuted;
                CoreSettings.Save();
                ApplyBrowserMute();
                SyncBrowserMuteIcon();
            }
            catch (Exception ex) { Log.Debug("BtnMuteBrowser_Click: {E}", ex.Message); }
        }

        /// <summary>Sends the saved mute state to the live page (WPF: BrowserService re-applies
        /// IsAudioMuted on init). Fire and forget: the page answers on its own thread.</summary>
        internal void ApplyBrowserMute()
        {
            try
            {
                BrowserMuteApplies++;
                _ = LiveBrowserHost.SetMutedAsync(CoreSettings.Current.BrowserVideoMuted || Shell?.BrowserPaused == true);
            }
            catch (Exception ex) { Log.Debug("ApplyBrowserMute: {E}", ex.Message); }
        }

        /// <summary>WPF SyncBrowserMuteIcon: the header glyph matches the saved preference.</summary>
        internal void SyncBrowserMuteIcon() =>
            TxtBrowserMute.Text = CoreSettings.Current.BrowserVideoMuted ? "🔇" : "🔊";

        // ------------------------------------------------------------------ webcam

        /// <summary>WPF BtnWebcamTracking_Click: stop if running; else the consent gate (declined =
        /// nothing happens), then start off the UI thread. The pill follows the tracker's own state.</summary>
        private async void BtnWebcamTracking_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                try { CoreBark.NotifyUiAction("webcam_tracking"); } catch (Exception ex) { Log.Debug("webcam bark: {E}", ex.Message); }
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning)
                {
                    await tracker.StopAsync();
                    RefreshBrowserWebcamButton();
                    return;
                }
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner) return;
                    await new WebcamConsentDialog().ShowDialogSafe(owner);
                    if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                BtnWebcamTracking.IsEnabled = false;
                bool started;
                try { started = await tracker.StartAsync(); }
                finally { BtnWebcamTracking.IsEnabled = true; }
                if (!started) Log.Information("Browser webcam button: tracker did not start ({E})", tracker.LastError);
                RefreshBrowserWebcamButton();
            }
            catch (Exception ex) { Log.Warning(ex, "Browser webcam button failed"); }
        }

        /// <summary>The pill's words and tooltip for the tracker's state, bound so a language switch follows.</summary>
        internal void RefreshBrowserWebcamButton()
        {
            try
            {
                bool on = WebcamRunningOverride?.Invoke() ?? Platform.WebcamTracker.Instance.IsRunning;
                TxtWebcamTracking.Bind(TextBlock.TextProperty, new Binding($"[{(on ? "btn_browser_webcam_tracking_on" : "btn_browser_webcam_tracking_off")}]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                BtnWebcamTracking.Bind(ToolTip.TipProperty, new Binding($"[{(on ? "tooltip_browser_webcam_tracking_on" : "tooltip_browser_webcam_tracking_off")}]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            }
            catch (Exception ex) { Log.Debug("RefreshBrowserWebcamButton: {E}", ex.Message); }
        }

        // ------------------------------------------------------------------ BambiCloud override

        /// <summary>WPF ChkForceShowBambiCloud_Changed (#596): reveal the BambiCloud site toggle on
        /// mods that hide it. Saves, then re-applies the toggle's visibility for the active mod.</summary>
        private void ChkForceShowBambiCloud_Changed(object? sender, RoutedEventArgs e)
        {
            if (_browserCardSyncing) return;
            try
            {
                CoreSettings.Current.ForceShowBambiCloud = ChkForceShowBambiCloud.IsChecked == true;
                CoreSettings.Save();
                ApplyBambiCloudVisibility(navigateIfHidden: true);
            }
            catch (Exception ex) { Log.Debug("ChkForceShowBambiCloud_Changed: {E}", ex.Message); }
        }

        /// <summary>The rule: the radio shows when the mod wants it OR the user forces it.</summary>
        internal static bool BambiCloudShown(bool modWants, bool forced) => modWants || forced;

        /// <summary>Hides or shows the BambiCloud radio. When it hides while selected, the dot moves to
        /// HypnoTube and, on a user change, the page follows (WPF SyncSiteRadiosToActiveMod with
        /// navigateIfChanged): a radio left on a page whose button was just hidden is the old bug.</summary>
        private void ApplyBambiCloudVisibility(bool navigateIfHidden)
        {
            bool modWants = true;
            try { modWants = App.Mods?.ShowBambiCloudOption() ?? true; }
            catch (Exception ex) { Log.Debug("ShowBambiCloudOption: {E}", ex.Message); }
            bool show = BambiCloudShown(modWants, CoreSettings.Current.ForceShowBambiCloud);
            RbBambiCloud.IsVisible = show;
            if (show || RbBambiCloud.IsChecked != true) return;
            RbHypnoTube.IsChecked = true;
            if (navigateIfHidden) Shell?.BrowserSiteToggle_Click(RbHypnoTube, new RoutedEventArgs());
        }

        // ------------------------------------------------------------------ pop out + the prompt

        private Window? _browserPopout;
        private WebHost? _popoutHost;

        /// <summary>The surface the page is on right now: the pop-out's host while it is open,
        /// else the card's. The shell's BrowserView reads this.</summary>
        internal WebHost LiveBrowserHost => _popoutHost ?? BrowserWebHost;

        /// <summary>True while the browser sits in its own window.</summary>
        internal bool BrowserPoppedOut => _browserPopout != null;

        /// <summary>Tests: the pop-out window, or null.</summary>
        internal Window? BrowserPopoutWindow => _browserPopout;

        /// <summary>Whether the prompt stands in the slot: always while popped out, else while an
        /// engine is there and nothing was ever loaded. With no engine the host's own panel says why.</summary>
        internal static bool BrowserPromptShown(bool poppedOut, bool hasEngine, bool loaded) =>
            poppedOut || (hasEngine && !loaded);

        /// <summary>The prompt's words, the host's visibility and the Pop Out button's label for the
        /// state (WPF BtnPopOutBrowser_Click sets the same three by hand). Bound, so a language
        /// switch follows.</summary>
        internal void SyncBrowserPrompt()
        {
            try
            {
                bool popped = _browserPopout != null;
                bool prompt = BrowserPromptShown(popped, BrowserWebHost.HasEngine, BrowserWebHost.Source != null);
                BrowserLoadingText.Bind(TextBlock.TextProperty, new Binding(
                    popped ? "[label_browser_popped_out_nclick_to_focus_window]" : "[label_click_to_load_browser]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                BrowserLoadingText.TextAlignment = global::Avalonia.Media.TextAlignment.Center;
                BrowserLoadingText.IsVisible = prompt;
                // A native web view draws over anything laid on it, so the prompt can only show with the host hidden.
                BrowserWebHost.IsVisible = !prompt;

                if (BtnPopOutBrowser.Content is TextBlock label)
                    label.Bind(TextBlock.TextProperty, new Binding(popped ? "[btn_focus]" : "[btn_pop_out]")
                        { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                BtnPopOutBrowser.Bind(ToolTip.TipProperty, new Binding(
                    popped ? "[tooltip_browser_is_popped_out_click_to_focus]" : "[tooltip_pop_out_browser_to_resizable_window]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            }
            catch (Exception ex) { Log.Debug("SyncBrowserPrompt: {E}", ex.Message); }
        }

        /// <summary>WPF #867: offline mode blocks the browser with a toast, before anything loads.</summary>
        private static bool BrowserCardBlockedOffline()
        {
            if (!CoreSettings.Current.OfflineMode) return false;
            try
            {
                Log.Information("Browser action blocked by offline mode");
                App.Notifications.Show(Loc.Get("browser_toast_offline_blocked"),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { Log.Debug("offline toast: {E}", ex.Message); }
            return true;
        }

        /// <summary>WPF BrowserLoadingText_Click: load the browser. Popped out, the click focuses the window.</summary>
        internal void OnBrowserPromptClick()
        {
            try
            {
                if (_browserPopout != null) { _browserPopout.Activate(); return; }
                if (BrowserCardBlockedOffline()) return;
                Shell?.RevealDashboardBrowser("load");
                BrowserWebHost.Navigate(new Uri(BrowserHomeUrl()));
                SyncBrowserPrompt();
            }
            catch (Exception ex) { Log.Debug("BrowserLoadingText_Click: {E}", ex.Message); }
        }

        /// <summary>The selected site's homepage; BambiCloud by default, as the shell's SiteHomeUrl.</summary>
        private string BrowserHomeUrl() =>
            RbHypnoTube.IsChecked == true ? "https://hypnotube.com/" : "https://bambicloud.com/";

        /// <summary>WPF BtnPopOutBrowser_Click: offline gate, then the browser moves to its own
        /// resizable window; a second click focuses it. Closing the window brings the page back.</summary>
        private void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (BrowserCardBlockedOffline()) return;
                if (_browserPopout != null) { _browserPopout.Activate(); return; }

                var page = BrowserWebHost.CurrentUrl ?? BrowserWebHost.Source ?? new Uri(BrowserHomeUrl());
                var host = new WebHost { AllowNavigation = BrowserWebHost.AllowNavigation, RequestHeader = BrowserWebHost.RequestHeader };
                host.NavigationCompleted += url =>
                {
                    ApplyBrowserMute();
                    Shell?.OnBrowserNavigationCompleted(url);
                };
                var window = new Window
                {
                    Width = 1024,
                    Height = 768,
                    MinWidth = 400,
                    MinHeight = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Content = host,
                };
                window.Bind(Window.TitleProperty, new Binding("[title_browser_window]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                window.Closed += (_, _) => OnBrowserPopoutClosed(window);

                _browserPopout = window;
                _popoutHost = host;
                // The card's copy goes quiet: one page, one sound.
                _ = BrowserWebHost.SetMutedAsync(true);
                _ = BrowserWebHost.PauseMediaAsync();
                SyncBrowserPrompt();

                window.Show();
                host.Navigate(page);
                Log.Information("Browser popped out to separate window");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to pop out browser");
                var failed = _browserPopout;
                _browserPopout = null;
                _popoutHost = null;
                try { failed?.Close(); } catch { }
                SyncBrowserPrompt();
                ApplyBrowserMute();
            }
        }

        /// <summary>WPF's Closed handler: the page is back in the card, the card cannot stay shut
        /// over it, and the button reads Pop Out again.</summary>
        private void OnBrowserPopoutClosed(Window window)
        {
            if (!ReferenceEquals(_browserPopout, window)) return;
            var back = _popoutHost?.CurrentUrl ?? _popoutHost?.Source;
            _browserPopout = null;
            _popoutHost = null;
            try
            {
                window.Content = null;
                if (back != null && !CoreSettings.Current.OfflineMode) BrowserWebHost.Navigate(back);
                SyncBrowserPrompt();
                ApplyBrowserMute();
                Shell?.RevealDashboardBrowser("popout-closed");
            }
            catch (Exception ex) { Log.Debug("Browser pop-out close: {E}", ex.Message); }
        }

        /// <summary>Closes the pop-out (the page or the shell is going away).</summary>
        internal void CloseBrowserPopout()
        {
            try { _browserPopout?.Close(); }
            catch (Exception ex) { Log.Debug("CloseBrowserPopout: {E}", ex.Message); }
        }

        private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e) { } // greyed: see the header
    }
}
