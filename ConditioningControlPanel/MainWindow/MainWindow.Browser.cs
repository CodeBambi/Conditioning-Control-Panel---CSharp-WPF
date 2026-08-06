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
    // Browser tab: embedded WebView2 browser, popout/fullscreen, and Profile Viewer (nested).
    public partial class MainWindow
    {
        #region Browser

        // True only while InitializeBrowserAsync is between its first await and completion.
        // Re-entering there would tear down the browser it is still building.
        private bool _browserInitializing;

        // True between "CreateBrowserAsync handed back a control" and BrowserReady/BrowserInitFailed,
        // i.e. while CoreWebView2 is still coming up in WebView_Loaded. During that window the
        // browser legitimately fails the readiness check without being wedged — tearing it down
        // there would kill an initialization that is still in flight.
        private bool _browserCorePending;

        private async System.Threading.Tasks.Task InitializeBrowserAsync(string? overrideStartUrl = null)
        {
            if (_browserInitialized || _browserInitializing) return;

            // A browser whose CoreWebView2 never came up leaves the flag cleared but the dead
            // control still parented (embedded container or pop-out). Drop it before building a
            // replacement, otherwise two WebView2s stack up and the visible one is the dead one.
            if (_browser != null) TearDownBrowserForReinit("stale browser before re-init");

            _browserInitializing = true;
            try
            {
                SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_loading");
                SettingsTab.TxtBrowserStatus.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                SettingsTab.BrowserLoadingText.Text = Loc.Get("label_initializing_webview2");

                // If a previous BrowserService was disposed but the bridge
                // survived, it's still subscribed to the dead service's events
                // and pointing at a dead WebView. Drop it so the BrowserReady
                // handler below re-creates a bridge wired to the new service.
                if (App.BrowserEnhanceBridge != null)
                {
                    try { App.BrowserEnhanceBridge.MatchChanged -= OnBrowserEnhanceMatchChanged; } catch { }
                    try { App.BrowserEnhanceBridge.Dispose(); } catch { }
                    App.BrowserEnhanceBridge = null;
                }

                _browser = new BrowserService();

                // Arm the audio-sync vibe track if the device is connected AFTER the user
                // is already sitting on a HypnoTube page (the natural "open video, then turn
                // the toy on" order). Nav-time injection only fires when already connected, so
                // without this the track would silently never start for that ordering.
                HookHapticAudioSyncRearm();

                _browser.BrowserReady += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _browserCorePending = false;
                        SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_connected_2");
                        SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green

                        // Now that CoreWebView2 is ready, attach message handler for video end notifications
                        if (_browser?.WebView?.CoreWebView2 != null)
                        {
                            _browser.WebView.CoreWebView2.WebMessageReceived += OnBrowserWebMessageReceived;
                            // Same handler for subframe messages — iframe-hosted players post to
                            // the frame's event, not the top-level one.
                            _browser.FrameWebMessageReceived += OnBrowserWebMessageReceived;
                            App.Logger?.Information("Browser WebMessageReceived handler attached (top-level + frames)");
                        }

                        // Phase 9: wire Deeper auto-discovery onto the WebView.
                        // Discovery is a separate listener so it doesn't interfere
                        // with audio-sync injection above. Bound/Unbound events
                        // drive the inline badge in the browser status row.
                        if (_browser?.WebView != null)
                        {
                            App.DeeperBrowserDiscovery?.Attach(_browser.WebView);
                            if (App.DeeperBrowserDiscovery != null)
                            {
                                // The service outlives the BrowserService, so a re-init would
                                // stack a second copy of these handlers on the same instance.
                                App.DeeperBrowserDiscovery.Bound -= OnDeeperBrowserBound;
                                App.DeeperBrowserDiscovery.Unbound -= OnDeeperBrowserUnbound;
                                App.DeeperBrowserDiscovery.Bound += OnDeeperBrowserBound;
                                App.DeeperBrowserDiscovery.Unbound += OnDeeperBrowserUnbound;
                            }
                        }

                        // Browser Enhancement Bridge: when the user navigates to
                        // a URL we have a saved enhancement for, drive effects on
                        // top of the browser. Toggle ON/OFF via the toolbar.
                        if (_browser?.WebView != null && App.BrowserEnhanceBridge == null)
                        {
                            App.BrowserEnhanceBridge = new Services.Deeper.BrowserEnhancementBridge(_browser.WebView, _browser);
                            App.BrowserEnhanceBridge.MatchChanged += OnBrowserEnhanceMatchChanged;
                        }
                    });
                };
                
                _browser.NavigationCompleted += (s, url) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_connected_2");
                        SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green

                        // Inject audio sync script when navigating to video sites
                        var audioSyncEnabled = App.Settings.Current.Haptics.AudioSync.Enabled;
                        var hapticsConnected = App.Haptics?.IsConnected == true;
                        var isHypnotube = url.Contains("hypnotube", StringComparison.OrdinalIgnoreCase);

                        App.Logger?.Information("AudioSync check: Enabled={Enabled}, HapticsConnected={Connected}, IsHypnotube={IsHT}, URL={Url}",
                            audioSyncEnabled, hapticsConnected, isHypnotube, url);

                        if (audioSyncEnabled && hapticsConnected && isHypnotube)
                        {
                            App.Logger?.Information("AudioSync: Injecting script for HypnoTube page");
                            await _browser.InjectAudioSyncScriptAsync();
                        }

                        // W3 Piece 1 — fire a catalogue lookup for HT video URLs.
                        // Fully async, fire-and-forget; doesn't block navigation
                        // or anything else. Eligibility is re-checked inside the
                        // service so a non-HT URL just returns InvalidUrl
                        // without hitting the network.
                        TriggerCatalogueLookupForNavigation(url);
                    });
                };

                _browser.FullscreenChanged += (s, isFullscreen) =>
                {
                    Dispatcher.Invoke(() => HandleBrowserFullscreenChanged(isFullscreen));
                };

                // Chromium render/browser process crash. Tear down so the next
                // BrowserSiteToggle click lazy-reinits instead of throwing
                // InvalidOperationException at the user.
                _browser.BrowserProcessFailed += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var dead = _browser?.WebView;
                            if (dead != null) SettingsTab.BrowserContainer.Children.Remove(dead);
                            try { (_browser as IDisposable)?.Dispose(); } catch { }
                        }
                        catch (Exception ex) { App.Logger?.Debug("Browser teardown after ProcessFailed: {Error}", ex.Message); }
                        _browser = null;
                        _browserInitialized = false;
                        _browserCorePending = false;
                        SettingsTab.BrowserLoadingText.Visibility = Visibility.Visible;
                        SettingsTab.BrowserLoadingText.Text = "Browser crashed - click a site to restart";
                        SettingsTab.TxtBrowserStatus.Text = "Disconnected";
                        SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(230, 80, 80));
                    });
                };

                // CoreWebView2 never came up. _browserInitialized is set the moment
                // CreateBrowserAsync hands back the control — i.e. before this can fire — so
                // without clearing it here the flag latches true over a dead browser and every
                // later Navigate is silently dropped for the process lifetime (#760).
                _browser.BrowserInitFailed += (s, reason) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        App.Logger?.Warning("Browser init failed ({Reason}) - marking browser not ready", reason);
                        _browserInitialized = false;
                        _browserCorePending = false;
                        SettingsTab.BrowserLoadingText.Visibility = Visibility.Visible;
                        SettingsTab.BrowserLoadingText.Text = "Browser failed to start - click a site to retry";
                        SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_error_2");
                        SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                    });
                };

                SettingsTab.BrowserLoadingText.Text = Loc.Get("label_creating_browser");

                // Navigate directly to the requested URL when lazy-init was triggered by
                // a speech-bubble link click. Otherwise fall back to the mod-appropriate
                // default site. The WebView2's _pendingUrl is the FIRST page Chromium
                // navigates to once CoreWebView2 finishes initializing — if we don't pass
                // the user's URL here, a subsequent Navigate would race the default-URL
                // load and get silently dropped.
                var startUrl = overrideStartUrl ?? App.Mods?.GetDefaultBrowserUrl() ?? "https://bambicloud.com/";
                var webView = await _browser.CreateBrowserAsync(startUrl);

                if (webView != null)
                {
                    SettingsTab.BrowserLoadingText.Visibility = Visibility.Collapsed;
                    SettingsTab.BrowserContainer.Children.Add(webView);
                    _browserInitialized = true;
                    _browserCorePending = true;   // cleared by BrowserReady / BrowserInitFailed
                    SyncBrowserMuteIcon();

                    // Note: WebMessageReceived handler is attached in BrowserReady event
                    // because CoreWebView2 isn't ready until then

                    App.Logger?.Information("Browser initialized - {Site} loaded", startUrl);
                }
                else
                {
                    var errorMsg = Loc.Get("msg_webview2_returned_null");
                    SettingsTab.BrowserLoadingText.Text = Loc.GetF("label_0_n_ninstall_webview2_runtime_ngo_microsoft_c", errorMsg);
                    SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_error_2");
                    SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                    MessageBox.Show(errorMsg, Loc.Get("title_browser_error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (InvalidOperationException invEx)
            {
                SettingsTab.BrowserLoadingText.Text = $"❌ {invEx.Message}";
                SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_not_installed");
                SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(invEx.Message, Loc.Get("title_webview2_not_installed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                var errorMsg = Loc.GetF("msg_webview2_com_error_0_1", comEx.Message, comEx.HResult);
                SettingsTab.BrowserLoadingText.Text = Loc.Get("label_com_error_install_webview2");
                SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_com_error");
                SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_webview2_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.DllNotFoundException dllEx)
            {
                var errorMsg = Loc.GetF("msg_webview2_dll_not_found_0", dllEx.Message);
                SettingsTab.BrowserLoadingText.Text = Loc.Get("label_missing_dll_install_webview2");
                SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_missing_dll");
                SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_missing_dll"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                var stack = ex.StackTrace;
                var errorMsg = $"Browser Error:\n\nType: {ex.GetType().Name}\n\nMessage: {ex.Message}\n\nStack: {(stack != null ? stack.Substring(0, Math.Min(500, stack.Length)) : "(none)")}";
                SettingsTab.BrowserLoadingText.Text = $"❌ {ex.GetType().Name}\n{ex.Message}";
                SettingsTab.TxtBrowserStatus.Text = Loc.Get("label_error_2");
                SettingsTab.TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, Loc.Get("title_browser_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _browserInitializing = false;
            }
        }

        internal async void BrowserLoadingText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            await InitializeBrowserAsync();
        }

        /// <summary>
        /// Drops a browser that can no longer navigate (CoreWebView2 never came up, or died)
        /// so the next lazy-init builds a fresh one. Mirrors the BrowserProcessFailed teardown.
        /// </summary>
        private void TearDownBrowserForReinit(string reason)
        {
            App.Logger?.Warning("Tearing down browser for re-init: {Reason}", reason);

            var dead = _browser;
            var deadView = _browser?.WebView;

            // Clear the fields FIRST: closing the pop-out below runs its Closed handler, which
            // re-parents _browser.WebView back into the embedded container if it still sees one.
            _browser = null;
            _browserInitialized = false;
            _browserCorePending = false;

            try
            {
                if (deadView != null && SettingsTab.BrowserContainer.Children.Contains(deadView))
                    SettingsTab.BrowserContainer.Children.Remove(deadView);

                if (_browserPopoutWindow != null)
                {
                    _browserPopoutWindow.Content = null;
                    _browserPopoutWindow.Close();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Browser teardown before re-init: {Error}", ex.Message); }

            try { (dead as IDisposable)?.Dispose(); } catch { }
        }

        /// <summary>
        /// Brings whichever surface actually hosts the WebView to the front. When the browser is
        /// popped out, activating MainWindow buries the pop-out the page loads into behind it,
        /// which looks exactly like "the link did nothing" (#760).
        /// </summary>
        private void FocusBrowserSurface()
        {
            if (_browserPopoutWindow != null)
            {
                try
                {
                    if (_browserPopoutWindow.WindowState == WindowState.Minimized)
                        _browserPopoutWindow.WindowState = WindowState.Normal;
                    _browserPopoutWindow.Activate();
                    _browserPopoutWindow.Focus();
                }
                catch (Exception ex) { App.Logger?.Debug("Failed to focus browser pop-out: {Error}", ex.Message); }
                return;
            }

            ShowTab("settings");
            Activate();
            Focus();
        }

        private async System.Threading.Tasks.Task InitAndNavigateAsync(string url, bool autoPlayFullscreen)
        {
            // Pass the user's URL as the WebView2 start URL so initialization navigates
            // directly to it. Calling _browser.Navigate(url) right after init silently
            // dropped the call — BrowserService's _isInitialized only flips true inside
            // WebView_Loaded (which runs after we'd return), so the request never reached
            // CoreWebView2 and the start-URL load (BambiCloud) stuck.
            await InitializeBrowserAsync(url);
            if (!_browserInitialized || _browser == null)
            {
                // Init failed, and the synchronous caller already returned true — nothing else
                // will retry, so hand the link to the system browser rather than eat it (#760).
                OpenUrlExternallyAfterBrowserFailure(url);
                return;
            }

            // Sync the radio button to the URL we just initialized to so the toggle UI
            // matches the page. Suppress the toggle handler's homepage navigation since
            // the WebView2 is already on its way to the right URL.
            var lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.Contains("bambicloud.com"))
            {
                _skipSiteToggleNavigation = true;
                SettingsTab.RbBambiCloud.IsChecked = true;
            }
            else if (lowerUrl.Contains("hypnotube.com"))
            {
                _skipSiteToggleNavigation = true;
                SettingsTab.RbHypnoTube.IsChecked = true;
            }
            else
            {
                // External URL — deselect both so re-clicking either fires Checked again
                SettingsTab.RbBambiCloud.IsChecked = false;
                SettingsTab.RbHypnoTube.IsChecked = false;
            }

            _browser.ZoomFactor = 0.5;

            // Wire one-shot autoplay handler. BrowserService raises NavigationCompleted
            // for the start-URL load, so this catches it without us having to issue a
            // second Navigate. BambiCloud playlists need a different injection (audio,
            // no <video> element) — mirror the branch in NavigateToUrlInBrowser so the
            // first-ever click on a playlist link auto-plays just like subsequent ones.
            if (autoPlayFullscreen)
            {
                var isBambiCloudPlaylist = lowerUrl.Contains("bambicloud.com/playlist/");
                void OnNavCompleted(object? s, string completedUrl)
                {
                    _browser.NavigationCompleted -= OnNavCompleted;
                    if (isBambiCloudPlaylist)
                        _ = AutoPlayBambiCloudPlaylistAsync();
                    else
                        _ = AutoPlayAndFullscreenVideoAsync();
                }
                _browser.NavigationCompleted += OnNavCompleted;
            }

            // Bring the surface hosting the browser forward (embedded tab or pop-out window)
            FocusBrowserSurface();
        }

        /// <summary>
        /// Defers a navigation until an in-flight CoreWebView2 bring-up finishes. Bounded: a
        /// bring-up that never completes falls through to the external browser rather than
        /// swallowing the click. Continuations resume on the dispatcher (UI thread).
        /// </summary>
        private async System.Threading.Tasks.Task NavigateWhenBrowserReadyAsync(string url, bool autoPlayFullscreen)
        {
            // Surface the browser while it finishes coming up, exactly as the ready path does —
            // otherwise the click looks ignored for as long as the bring-up takes.
            FocusBrowserSurface();

            for (int i = 0; i < 60 && _browserCorePending; i++)
                await Task.Delay(250);

            if (_browser?.IsInitialized == true && _browser.WebView?.CoreWebView2 != null)
            {
                NavigateToUrlInBrowser(url, autoPlayFullscreen);
                return;
            }

            App.Logger?.Warning("Browser never finished initializing - opening externally: {Url}", url);
            OpenUrlExternallyAfterBrowserFailure(url);
        }

        /// <summary>
        /// Last-resort escape hatch when the embedded browser cannot be brought up: open the
        /// link in the system browser (HTTPS only) so the click isn't silently swallowed.
        /// </summary>
        private void OpenUrlExternallyAfterBrowserFailure(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
                App.Logger?.Warning("Embedded browser init failed, opening externally: {Url}", url);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open URL externally: {Url}", url);
            }
        }

        internal async void BrowserSiteToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return; // Don't auto-load browser during XAML init

            // Lazy-load browser on first toggle interaction. Pass the URL
            // matching the radio button the user just clicked — without an
            // override, InitializeBrowserAsync defaults to BambiCloud and the
            // first HT click would land on BC, forcing the user to bounce
            // BC→HT to actually get to HT.
            if (!_browserInitialized)
            {
                var initialUrl = SettingsTab.RbHypnoTube?.IsChecked == true
                    ? "https://hypnotube.com/"
                    : "https://bambicloud.com/";
                await InitializeBrowserAsync(initialUrl);
                return;
            }
            if (_browser == null) return;

            // Block navigation in offline mode
            if (App.Settings?.Current?.OfflineMode == true) return;

            // Skip navigation if we're already navigating to a specific URL (from speech bubble link)
            if (_skipSiteToggleNavigation)
            {
                _skipSiteToggleNavigation = false;
                return;
            }

            var isBambiCloud = SettingsTab.RbBambiCloud.IsChecked == true;
            var url = isBambiCloud
                ? "https://bambicloud.com/"
                : "https://hypnotube.com/";

            // Any property/method touching the WebView2 throws InvalidOperationException
            // if the underlying browser process has crashed. Tear down and lazy-reinit
            // on the next toggle rather than propagating the crash.
            try
            {
                _browser.ZoomFactor = 0.5;
                _browser.Navigate(url);
                App.Logger?.Information("Browser navigated to {Site} (zoom: 50%)",
                    isBambiCloud ? "BambiCloud" : "HypnoTube");
            }
            catch (InvalidOperationException ex)
            {
                App.Logger?.Warning(ex, "WebView2 unusable (browser process likely crashed) - resetting for next toggle");
                try { (_browser as IDisposable)?.Dispose(); } catch { }
                _browser = null;
                _browserInitialized = false;
            }
        }

        /// <summary>
        /// Navigates to a URL in the embedded browser, automatically selecting the correct tab.
        /// Called by speech bubble links in AvatarTubeWindow.
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
        /// <param name="autoPlayFullscreen">If true, auto-plays video and requests fullscreen on the video element</param>
        /// <returns>True if navigation was initiated, false if browser unavailable</returns>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false)
        {
            // Block navigation in offline mode
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Browser navigation blocked in offline mode: {Url}", url);
                return false;
            }

            // Lazy-load browser if it isn't REALLY ready. _browserInitialized only means
            // CreateBrowserAsync handed back a control — it is set before CoreWebView2 exists, so
            // it stays true over a browser whose init failed and every Navigate is then dropped in
            // silence for the process lifetime (#760). Consult the service's own state too and
            // re-init on mismatch so a wedged browser self-heals on the next click.
            var browserReady = _browserInitialized && _browser != null
                && _browser.IsInitialized && _browser.WebView?.CoreWebView2 != null;

            if (!browserReady)
            {
                if (_browserInitialized && _browserCorePending && _browser != null)
                {
                    // Not wedged - CoreWebView2 is simply still coming up (_browserInitialized is
                    // set the moment CreateBrowserAsync returns, BrowserReady only fires once
                    // WebView_Loaded finishes). Re-initializing here would tear down an init that
                    // is still in flight, so wait it out and navigate when it lands.
                    _ = NavigateWhenBrowserReadyAsync(url, autoPlayFullscreen);
                    return true;
                }
                if (_browserInitialized)
                {
                    App.Logger?.Warning("Browser flagged ready but is not usable (service init={Init}, core={HasCore}) - re-initializing for {Url}",
                        _browser?.IsInitialized == true, _browser?.WebView?.CoreWebView2 != null, url);
                    _browserInitialized = false; // let InitializeBrowserAsync tear down and rebuild
                }
                _ = InitAndNavigateAsync(url, autoPlayFullscreen);
                return true; // Navigation will happen after init completes
            }

            if (_browser == null)
            {
                App.Logger?.Warning("Browser not available for navigation: {Url}", url);
                return false;
            }

            try
            {
                // Bring the surface hosting the browser forward (embedded tab or pop-out window)
                FocusBrowserSurface();

                var lowerUrl = url.ToLowerInvariant();

                // Switch to correct site tab based on URL
                // Set flag to skip the homepage navigation in the toggle handler
                if (lowerUrl.Contains("bambicloud.com") && SettingsTab.RbBambiCloud.IsChecked != true)
                {
                    _skipSiteToggleNavigation = true;
                    SettingsTab.RbBambiCloud.IsChecked = true;
                }
                else if (lowerUrl.Contains("hypnotube.com") && SettingsTab.RbHypnoTube.IsChecked != true)
                {
                    _skipSiteToggleNavigation = true;
                    SettingsTab.RbHypnoTube.IsChecked = true;
                }
                else if (!lowerUrl.Contains("bambicloud.com") && !lowerUrl.Contains("hypnotube.com"))
                {
                    // External URL — deselect both radio buttons so clicking either one
                    // fires a Checked event to navigate back (RadioButton.Checked only fires
                    // on false→true transitions, so re-clicking an already-checked button does nothing)
                    SettingsTab.RbBambiCloud.IsChecked = false;
                    SettingsTab.RbHypnoTube.IsChecked = false;
                }

                _browser.ZoomFactor = 0.5;

                // If auto-play fullscreen requested, set up handler for when navigation completes.
                // BambiCloud playlists are audio (no <video> element, no fullscreen) — they need a
                // different injection that clicks the playlist's main play button.
                EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>? navCompletedHandler = null;
                if (autoPlayFullscreen && _browser.WebView?.CoreWebView2 != null)
                {
                    var isBambiCloudPlaylist = lowerUrl.Contains("bambicloud.com/playlist/");

                    void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
                    {
                        _browser.WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

                        if (e.IsSuccess)
                        {
                            if (isBambiCloudPlaylist)
                                _ = AutoPlayBambiCloudPlaylistAsync();
                            else
                                _ = AutoPlayAndFullscreenVideoAsync();
                        }
                    }

                    navCompletedHandler = OnNavigationCompleted;
                    _browser.WebView.CoreWebView2.NavigationCompleted += navCompletedHandler;
                }
                else if (autoPlayFullscreen)
                {
                    App.Logger?.Warning("Auto-play/fullscreen requested but CoreWebView2 is null - takeover skipped: {Url}", url);
                }

                // Navigate. A dropped Navigate must surface as failure: reporting success here is
                // what skipped the caller's external-browser fallback and made the click look
                // like it did nothing at all (#760).
                if (!_browser.Navigate(url))
                {
                    if (navCompletedHandler != null && _browser.WebView?.CoreWebView2 != null)
                        _browser.WebView.CoreWebView2.NavigationCompleted -= navCompletedHandler;

                    App.Logger?.Warning("Speech link navigation dropped by browser service: {Url}", url);
                    return false;
                }

                App.Logger?.Information("Speech link navigated to: {Url} (Site: {Site}, AutoPlay: {AutoPlay})",
                    url, lowerUrl.Contains("bambicloud") ? "BambiCloud" : "HypnoTube", autoPlayFullscreen);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Browser navigation failed for URL: {Url}", url);
                return false;
            }
        }

        /// <summary>
        /// Injects JavaScript to find the video element, play it, and request fullscreen.
        /// Also adds handlers for: video ended (exit fullscreen), double-click (exit fullscreen).
        /// Notifies AutonomyService when video playback ends.
        /// </summary>
        private async Task AutoPlayAndFullscreenVideoAsync()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;

            try
            {
                // Inject audio sync script if enabled
                if (App.Settings.Current.Haptics.AudioSync.Enabled && App.Haptics?.IsConnected == true)
                {
                    await _browser.InjectAudioSyncScriptAsync();
                }

                // Wait a moment for the page to fully render
                await Task.Delay(1500);

                // JavaScript to find video, play it, request fullscreen, and add event handlers
                // Posts message back to C# when video ends or fullscreen exits
                // Retries up to 10 times (5s total) if video element isn't in the DOM yet
                var script = @"
                    (async function() {
                        let video = document.querySelector('video');
                        if (!video) {
                            for (let i = 0; i < 10; i++) {
                                await new Promise(r => setTimeout(r, 500));
                                video = document.querySelector('video');
                                if (video) break;
                            }
                        }
                        if (video) {
                            // NOTE: playback lifecycle (started / duration / ended) is NOT reported
                            // here any more — the always-on reporter injected by BrowserService owns
                            // it, so user-started videos are tracked too. This script is now only
                            // responsible for the fullscreen takeover itself.

                            const exitFullscreen = () => {
                                if (document.exitFullscreen) {
                                    document.exitFullscreen();
                                } else if (document.webkitExitFullscreen) {
                                    document.webkitExitFullscreen();
                                } else if (document.msExitFullscreen) {
                                    document.msExitFullscreen();
                                }
                            };

                            let fsNotified = false;
                            const notifyFsExit = () => {
                                if (fsNotified) return;
                                fsNotified = true;
                                window.chrome.webview.postMessage({ type: 'fsExit' });
                            };

                            // When the clip ends, drop out of fullscreen. Whether the SESSION ended
                            // is the reporter's call — sites auto-advance, and the user may keep
                            // watching.
                            video.addEventListener('ended', () => { exitFullscreen(); notifyFsExit(); }, { once: true });

                            video.addEventListener('dblclick', (e) => {
                                if (document.fullscreenElement || document.webkitFullscreenElement) {
                                    exitFullscreen();
                                    notifyFsExit();
                                    e.preventDefault();
                                    e.stopPropagation();
                                }
                            });

                            // Track fullscreen exit properly. The old handler used { once: true }
                            // and was registered BEFORE requestFullscreen() — so ENTERING fullscreen
                            // fired it, the body no-opped (fullscreenElement was set) and the
                            // listener was consumed, meaning the real exit was never reported.
                            // Now: persistent listener, and we only arm the exit once we have
                            // actually observed fullscreen being entered.
                            let enteredFs = false;
                            document.addEventListener('fullscreenchange', () => {
                                const inFs = !!(document.fullscreenElement || document.webkitFullscreenElement);
                                if (inFs) { enteredFs = true; return; }
                                if (enteredFs) notifyFsExit();
                            });

                            video.muted = false;
                            const goFullscreen = () => {
                                if (video.requestFullscreen) {
                                    video.requestFullscreen();
                                } else if (video.webkitRequestFullscreen) {
                                    video.webkitRequestFullscreen();
                                } else if (video.msRequestFullscreen) {
                                    video.msRequestFullscreen();
                                }
                            };
                            video.play().then(goFullscreen).catch(e => {
                                console.log('Autoplay blocked:', e);
                                // Retry the fullscreen request when the user unblocks playback —
                                // previously fullscreen was simply never requested on this path,
                                // so the takeover silently degraded to a windowed video.
                                video.addEventListener('playing', goFullscreen, { once: true });
                            });
                        } else {
                            console.log('No video element found after retries');
                            // Only the takeover failed to find a player. Do NOT report the media
                            // session as stopped — the page may still play, and the C# heartbeat
                            // will retire the session if it genuinely never starts.
                            window.chrome.webview.postMessage({ type: 'fsExit' });
                        }
                    })();
                ";

                await _browser.WebView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Debug("Auto-play and fullscreen script injected with exit handlers");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to auto-play/fullscreen video");
            }
        }

        /// <summary>
        /// Force-ends a web-video takeover: pauses any playing media in the browser and
        /// exits both page and forced fullscreen. Called by AutonomyService when playback
        /// runs past the video's reported length (#484) — typically because the site
        /// auto-advanced to the next clip after our once-only 'ended' handlers fired.
        /// </summary>
        internal void EndWebVideoTakeover()
        {
            try
            {
                _ = _browser?.WebView?.CoreWebView2?.ExecuteScriptAsync(
                    "document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });" +
                    "if (document.exitFullscreen) document.exitFullscreen();");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EndWebVideoTakeover script failed: {Error}", ex.Message);
            }
            try { ExitBrowserFullscreen(); } catch { }
        }

        /// <summary>
        /// BambiCloud playlists are audio (no &lt;video&gt; element). The page renders a single
        /// .play-action button that starts the whole playlist; we click it once it hydrates,
        /// then post videoStarted/videoEnded messages so AutonomyService treats the playlist
        /// like a fullscreen video for blocking purposes.
        /// </summary>
        private async Task AutoPlayBambiCloudPlaylistAsync()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;

            try
            {
                // Wait for React hydration before looking for the button.
                await Task.Delay(1500);

                var script = @"
                    (async function() {
                        // Poll for the .play-action button - SPA hydration can take a few seconds.
                        let btn = document.querySelector('button.play-action');
                        for (let i = 0; i < 20 && !btn; i++) {
                            await new Promise(r => setTimeout(r, 250));
                            btn = document.querySelector('button.play-action');
                        }
                        if (!btn) {
                            window.chrome.webview.postMessage({ type: 'videoEnded', reason: 'noPlayButton' });
                            return;
                        }

                        let notified = false;
                        const notifyStarted = () => {
                            if (!notified) {
                                notified = true;
                                window.chrome.webview.postMessage({ type: 'videoStarted' });
                            }
                        };
                        const notifyEnded = (reason) => {
                            window.chrome.webview.postMessage({ type: 'videoEnded', reason: reason });
                        };

                        // Bind to any current/future <audio> element so we know when the
                        // playlist actually plays and when the last track ends.
                        const bindAudio = (audio) => {
                            if (!audio || audio.__bcBound) return;
                            audio.__bcBound = true;
                            audio.addEventListener('playing', notifyStarted);
                            audio.addEventListener('ended', () => notifyEnded('ended'));
                        };
                        document.querySelectorAll('audio').forEach(bindAudio);

                        // Also watch for audio elements added later (each track may swap one in).
                        const obs = new MutationObserver(() => {
                            document.querySelectorAll('audio').forEach(bindAudio);
                        });
                        obs.observe(document.body, { childList: true, subtree: true });

                        // Click the play button. Browser autoplay policies usually allow this
                        // because navigation-from-app counts as a user gesture in WebView2.
                        btn.click();

                        // Fallback: if no <audio> 'playing' fires within 3s, assume click took
                        // effect anyway and notify, so the autonomy watchdog doesn't fire.
                        setTimeout(notifyStarted, 3000);
                    })();
                ";

                await _browser.WebView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Debug("BambiCloud playlist auto-play script injected");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to auto-play BambiCloud playlist");
            }
        }

        /// <summary>
        /// Handles messages from JavaScript in the browser (video ended, fullscreen exit, etc.)
        /// </summary>
        private void OnBrowserWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Use TryGetWebMessageAsString to get the raw JSON (not double-encoded)
                var message = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(message))
                {
                    // Fallback to WebMessageAsJson if string is not available
                    message = e.WebMessageAsJson;
                }

                // Log audio sync messages at Information level for debugging
                if (message.Contains("audioSync"))
                {
                    App.Logger?.Information("AudioSync message received: {Message}", message);
                }
                else
                {
                    App.Logger?.Debug("Browser web message received: {Message}", message);
                }

                // Force-exit our WPF "forced fullscreen" surface — sent by the
                // dblclick / click-pair / fullscreenchange handlers injected
                // into every CCP WebView. Fires the same path Esc/F11 do.
                if (message == "ccp_exit_fullscreen")
                {
                    App.Logger?.Information("MainWindow: ccp_exit_fullscreen received (forced FS active = {Active})", _isBrowserFullscreen);
                    if (_isBrowserFullscreen) ExitBrowserFullscreen();
                    return;
                }

                // Always-on media reporter (BrowserService document-created script). Fires for
                // user-started playback as well as app-started, which is what makes browser
                // videos visible to the rest of the app at all.
                if (message.Contains("\"type\":\"ccpMedia\""))
                {
                    HandleBrowserMediaMessage(message);
                    return;
                }

                // Legacy one-shot takeover messages. The media lifecycle now comes from the
                // always-on reporter above; these remain for the BambiCloud playlist injection
                // (audio player with a bespoke play-button click) and are idempotent against it.
                if (message.Contains("\"type\":\"videoStarted\""))
                {
                    App.Logger?.Information("Web video playback started (takeover injection)");
                    App.BrowserMedia?.OnMediaPlaying(0);
                }
                else if (message.Contains("\"type\":\"videoEnded\""))
                {
                    App.Logger?.Information("Web video playback ended (takeover injection)");
                    App.BrowserMedia?.OnMediaStopped("takeover-injection");
                    ExitBrowserFullscreen();
                }
                else if (message.Contains("\"type\":\"videoDuration\""))
                {
                    var secMatch = System.Text.RegularExpressions.Regex.Match(message, "\"seconds\":([0-9.]+)");
                    if (secMatch.Success && double.TryParse(secMatch.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    {
                        App.BrowserMedia?.OnMediaPlaying(seconds);
                    }
                }
                // The user left fullscreen but the page may still be playing. This ends the
                // TAKEOVER only — conflating it with "media stopped" is what let a stray
                // fullscreenchange free the app to interrupt a video the user was still watching.
                else if (message.Contains("\"type\":\"fsExit\""))
                {
                    App.BrowserMedia?.OnFullscreenExited();
                    ExitBrowserFullscreen();
                }
                // Audio sync messages
                else if (message.Contains("\"type\":\"audioSyncVideoDetected\""))
                {
                    App.Logger?.Information("AudioSync: Video detected message received");
                    HandleAudioSyncVideoDetected(message);
                }
                else if (message.Contains("\"type\":\"audioSyncState\""))
                {
                    HandleAudioSyncState(message);
                }
                else if (message.Contains("\"type\":\"audioSyncSeek\""))
                {
                    App.Logger?.Information("AudioSync: Seek message received");
                    HandleAudioSyncSeek(message);
                }
                else if (message.Contains("\"type\":\"audioSyncEnded\""))
                {
                    App.Logger?.Information("AudioSync: Video ended message received");
                    HandleAudioSyncEnded();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to process browser web message");
            }
        }

        /// <summary>
        /// Routes a 'ccpMedia' report from the always-on media reporter into
        /// <see cref="Services.Browser.BrowserMediaService"/>. Shape:
        /// <c>{type:'ccpMedia', state:'playing'|'progress'|'stopped', pos, dur, reason}</c>.
        /// </summary>
        private void HandleBrowserMediaMessage(string message)
        {
            if (App.BrowserMedia == null) return;

            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(message);
                var state = (string?)o["state"];
                var pos = (double?)o["pos"] ?? 0;
                var dur = (double?)o["dur"] ?? 0;

                switch (state)
                {
                    case "playing":
                        App.BrowserMedia.OnMediaPlaying(dur);
                        break;
                    case "progress":
                        App.BrowserMedia.OnMediaProgress(pos, dur);
                        break;
                    case "stopped":
                        App.BrowserMedia.OnMediaStopped((string?)o["reason"] ?? "page");
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to parse ccpMedia message: {Error}", ex.Message);
            }
        }

        private void HandleAudioSyncVideoDetected(string message)
        {
            if (App.AudioSync == null)
            {
                App.Logger?.Warning("AudioSync: Service is null, cannot process video");
                // Signal ready anyway so video plays (without haptics)
                _ = _browser?.SignalHapticReadyAsync();
                return;
            }

            try
            {
                // Extract URL from message
                var urlMatch = System.Text.RegularExpressions.Regex.Match(message, "\"url\":\"([^\"]+)\"");
                if (urlMatch.Success)
                {
                    var videoUrl = urlMatch.Groups[1].Value;
                    App.Logger?.Information("AudioSync: Starting processing for video URL: {Url}", videoUrl);

                    // Wire up progress events
                    void OnProgress(object? sender, Services.Audio.ChunkProgressEventArgs e)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            if (_browser != null)
                            {
                                await _browser.UpdateHapticProgressAsync(e.PercentComplete, e.Status);
                            }
                        });
                    }

                    void OnCompleted(object? sender, EventArgs e)
                    {
                        // Unsubscribe
                        App.AudioSync!.ProcessingProgress -= OnProgress;
                        App.AudioSync.ProcessingCompleted -= OnCompleted;

                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Processing completed, signaling browser");
                            if (_browser != null)
                            {
                                await _browser.SignalHapticReadyAsync();
                            }
                        });
                    }

                    // Wire up chunk loading events (for seek to unloaded sections)
                    void OnChunkLoadingRequired(object? sender, int chunkIndex)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Chunk {Index} loading required, showing overlay", chunkIndex);
                            if (_browser != null)
                            {
                                await _browser.ShowChunkLoadingOverlayAsync(chunkIndex);
                            }
                        });
                    }

                    void OnChunkLoadingCompleted(object? sender, EventArgs e)
                    {
                        Dispatcher.BeginInvoke(async () =>
                        {
                            App.Logger?.Information("AudioSync: Chunk loading completed, hiding overlay");
                            if (_browser != null)
                            {
                                await _browser.HideChunkLoadingOverlayAsync();
                            }
                        });
                    }

                    App.AudioSync.ProcessingProgress += OnProgress;
                    App.AudioSync.ProcessingCompleted += OnCompleted;
                    App.AudioSync.ChunkLoadingRequired += OnChunkLoadingRequired;
                    App.AudioSync.ChunkLoadingCompleted += OnChunkLoadingCompleted;

                    // Start processing in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await App.AudioSync.OnVideoDetectedAsync(videoUrl);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "AudioSync: Processing failed");
                            // Signal ready anyway so video plays (without haptics)
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                if (_browser != null)
                                {
                                    await _browser.SignalHapticReadyAsync();
                                }
                            });
                        }
                    });
                }
                else
                {
                    // No URL found, signal ready so video plays
                    App.Logger?.Warning("AudioSync: No URL found in message");
                    _ = _browser?.SignalHapticReadyAsync();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to handle audio sync video detected");
                // Signal ready anyway so video plays (without haptics)
                _ = _browser?.SignalHapticReadyAsync();
            }
        }

        private void HandleAudioSyncState(string message)
        {
            if (App.AudioSync == null) return;

            try
            {
                // Extract currentTime and paused from message
                var timeMatch = System.Text.RegularExpressions.Regex.Match(message, "\"currentTime\":([\\d.]+)");
                var pausedMatch = System.Text.RegularExpressions.Regex.Match(message, "\"paused\":(true|false)");

                if (timeMatch.Success)
                {
                    var currentTime = double.Parse(timeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var paused = pausedMatch.Success && pausedMatch.Groups[1].Value == "true";

                    App.AudioSync.OnPlaybackStateUpdate(currentTime, paused);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to handle audio sync state: {Error}", ex.Message);
            }
        }

        private void HandleAudioSyncSeek(string message)
        {
            if (App.AudioSync == null) return;

            try
            {
                var timeMatch = System.Text.RegularExpressions.Regex.Match(message, "\"currentTime\":([\\d.]+)");
                if (timeMatch.Success)
                {
                    var newTime = double.Parse(timeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    App.AudioSync.OnVideoSeek(newTime);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to handle audio sync seek: {Error}", ex.Message);
            }
        }

        private void HandleAudioSyncEnded()
        {
            App.AudioSync?.OnVideoEnded();
        }

        // Subscribed once to App.Haptics.ConnectionChanged so a late device connection can
        // arm the vibe track on a page that's already open. The browser can be torn down and
        // re-created (process-failure recovery), so the handler always uses the live _browser.
        private bool _hapticAudioSyncConnHooked;

        private void HookHapticAudioSyncRearm()
        {
            if (_hapticAudioSyncConnHooked || App.Haptics == null) return;
            _hapticAudioSyncConnHooked = true;
            App.Haptics.ConnectionChanged += OnHapticConnectionChangedForAudioSync;
        }

        private void OnHapticConnectionChangedForAudioSync(object? sender, bool connected)
        {
            if (!connected) return;

            // Device just connected. If the user is already on a HypnoTube page with audio-sync
            // enabled, inject (idempotent) and re-arm so the currently-loaded/playing video gets
            // synced now — instead of forcing a re-navigation. Marshalled to the UI thread because
            // ConnectionChanged fires from the provider's thread and GetCurrentUrl touches the WebView.
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (!App.Settings.Current.Haptics.AudioSync.Enabled) return;
                    var url = _browser?.GetCurrentUrl();
                    if (string.IsNullOrEmpty(url) ||
                        !url.Contains("hypnotube", StringComparison.OrdinalIgnoreCase))
                        return;

                    App.Logger?.Information("AudioSync: Haptics connected on HypnoTube page — arming vibe track for the current video");
                    await _browser!.InjectAudioSyncScriptAsync();
                    await _browser.RearmAudioSyncAsync();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AudioSync rearm-on-connect failed: {Error}", ex.Message);
                }
            });
        }

        private void BtnDiscordTab_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("discord");
        }

        internal async void BtnDiscordTabLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                App.Discord.Logout();
                if (App.Patreon?.IsAuthenticated != true)
                {
                    // No provider left — full logout
                    ClearAccountData();
                }
                else
                {
                    // Patreon still active — just update Discord UI
                    UpdateDiscordTabUI();
                    UpdateDiscordUI();
                }
            }
            else
            {
                // Check if user is already logged in with another provider
                var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                if (hasUnifiedId)
                {
                    // Link Discord to existing account
                    DiscordTab.BtnDiscordTabLogin.IsEnabled = false;
                    DiscordTab.BtnDiscordTabLogin.Content = Loc.Get("login_connecting");

                    try
                    {
                        await App.Discord.StartOAuthFlowAsync();
                        var success = await AccountService.LinkProviderV2Async(this, "discord");

                        if (success)
                        {
                            UpdateQuickDiscordUI();
                            UpdateDiscordUI();
                            UpdateDiscordTabUI();
                            UpdatePatreonUI();
                            UpdateAccountLinkingUI();
                            UpdateBannerWelcomeMessage();
                            OfferAchievementSharingAfterDiscordLink();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to link Discord");
                        MessageBox.Show($"Failed to link Discord account.\n\n{ex.Message}",
                            "Link Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        DiscordTab.BtnDiscordTabLogin.IsEnabled = true;
                        UpdateDiscordTabUI();
                    }
                }
                else
                {
                    // No account yet - open unified login dialog
                    OpenUnifiedLoginDialog();
                }
            }
        }

        private void UpdateDiscordTabUI()
        {
            if (App.Discord == null) return;

            var isLoggedIn = App.Discord.IsAuthenticated;
            var s = App.Settings?.Current;

            // Update login status in Community Settings section
            if (DiscordTab.TxtDiscordTabStatus != null && DiscordTab.TxtDiscordTabInfo != null && DiscordTab.BtnDiscordTabLogin != null)
            {
                if (isLoggedIn)
                {
                    DiscordTab.TxtDiscordTabStatus.Text = Loc.GetF("label_connected_as_0", App.Discord.Username);
                    DiscordTab.TxtDiscordTabInfo.Text = Loc.Get("label_discord_account_linked");
                    DiscordTab.BtnDiscordTabLogin.Content = Loc.Get("btn_logout");
                }
                else
                {
                    // Check if user is logged in with another provider (has unified_id)
                    var hasUnifiedId = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

                    DiscordTab.TxtDiscordTabStatus.Text = Loc.Get("label_not_connected");
                    DiscordTab.TxtDiscordTabInfo.Text = Loc.Get("label_link_discord_for_community_features");

                    // Show "Link Discord" if logged in via Patreon, otherwise "Login"
                    DiscordTab.BtnDiscordTabLogin.Content = hasUnifiedId ? Loc.Get("btn_link_discord_2") : Loc.Get("btn_login");
                }
            }

            // Sync checkbox states
            if (s != null)
            {
                if (DiscordTab.ChkDiscordTabRichPresence != null) DiscordTab.ChkDiscordTabRichPresence.IsChecked = s.DiscordRichPresenceEnabled;
                if (DiscordTab.ChkDiscordTabShowLevel != null) DiscordTab.ChkDiscordTabShowLevel.IsChecked = s.DiscordShowLevelInPresence;
                if (DiscordTab.ChkDiscordTabShareAchievements != null) DiscordTab.ChkDiscordTabShareAchievements.IsChecked = s.DiscordShareAchievements;
                if (DiscordTab.ChkDiscordTabShareLevelUps != null) DiscordTab.ChkDiscordTabShareLevelUps.IsChecked = s.DiscordShareLevelUps;
                if (DiscordTab.ChkDiscordTabAllowDm != null) DiscordTab.ChkDiscordTabAllowDm.IsChecked = s.AllowDiscordDm;
                if (DiscordTab.ChkDiscordTabSharePfp != null) DiscordTab.ChkDiscordTabSharePfp.IsChecked = s.ShareProfilePicture;
                if (DiscordTab.ChkDiscordTabShowOnline != null) DiscordTab.ChkDiscordTabShowOnline.IsChecked = s.ShowOnlineStatus;
                // Goon Game sharing (all default off). The handlers no-op when the value is
                // unchanged, so these programmatic assignments never trigger a sync push.
                if (DiscordTab.ChkGoonShareAvatar != null) DiscordTab.ChkGoonShareAvatar.IsChecked = s.GoonShareAvatar;
                if (DiscordTab.ChkGoonShareDiscordDm != null) DiscordTab.ChkGoonShareDiscordDm.IsChecked = s.GoonShareDiscordDm;
                if (DiscordTab.ChkGoonRichPresence != null) DiscordTab.ChkGoonRichPresence.IsChecked = s.GoonRichPresence;
            }

            // Pre-fill search bar with user's unified display name (V2 auth) or fallback
            var displayName = App.Settings?.Current?.UserDisplayName
                ?? App.Discord?.CustomDisplayName ?? App.Discord?.DisplayName ?? App.Patreon?.DisplayName;
            if (DiscordTab.TxtProfileSearch != null && !string.IsNullOrEmpty(displayName))
            {
                DiscordTab.TxtProfileSearch.Text = displayName;
            }

            // Auto-display own profile when Discord tab is opened
            DisplayOwnProfile();
        }

        #region Profile Viewer

        internal void TxtProfileSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SearchAndDisplayProfile(DiscordTab.TxtProfileSearch?.Text);
            }
        }

        internal void BtnProfileSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchAndDisplayProfile(DiscordTab.TxtProfileSearch?.Text);
        }

        internal void BtnViewMyProfile_Click(object sender, RoutedEventArgs e)
        {
            // Find current user in leaderboard by their unified display name (V2 auth) or fallback
            var displayName = App.Settings?.Current?.UserDisplayName
                ?? App.Discord?.CustomDisplayName ?? App.Discord?.DisplayName ?? App.Patreon?.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                // Not logged in - show own local stats
                DisplayOwnProfile();
                return;
            }

            // Try leaderboard search first, fall back to local profile if not found
            if (!SearchAndDisplayProfile(displayName))
            {
                DisplayOwnProfile();
            }
        }

        internal void BtnClearProfile_Click(object sender, RoutedEventArgs e)
        {
            if (DiscordTab.TxtProfileSearch != null) DiscordTab.TxtProfileSearch.Text = "";
            ClearProfileViewer();
        }

        private void ClearProfileViewer()
        {
            if (DiscordTab.ProfileCardWrapper != null) DiscordTab.ProfileCardWrapper.Visibility = Visibility.Collapsed;
            if (DiscordTab.NoProfileSelected != null) DiscordTab.NoProfileSelected.Visibility = Visibility.Visible;
            if (DiscordTab.ProfileAchievementGrid != null) DiscordTab.ProfileAchievementGrid.ItemsSource = null;
            // Hide OG border and stop animation
            if (DiscordTab.OgBorderContainer != null)
            {
                DiscordTab.OgBorderContainer.Visibility = Visibility.Collapsed;
                if (DiscordTab.OgBorderContainer.Resources["OgBorderAnimation"] is System.Windows.Media.Animation.Storyboard storyboard)
                {
                    storyboard.Stop(DiscordTab.OgBorderContainer);
                }
            }
            // Hide OG banner badge
            if (DiscordTab.OgBannerBadge != null)
            {
                DiscordTab.OgBannerBadge.Visibility = Visibility.Collapsed;
            }
            // Hide Patreon tier badge
            if (DiscordTab.ProfilePatreonTierBadge != null)
            {
                DiscordTab.ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
            }
            // Nothing on screen belongs to anyone else any more, so the "back to me" chip retires.
            SetProfileViewingSelf(true);
        }

        private void ProfileDiscordHandle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var discordId = DiscordTab.TxtProfileDiscordId?.Text;
            if (!string.IsNullOrEmpty(discordId))
            {
                try
                {
                    System.Windows.Clipboard.SetText(discordId);
                    // Show brief feedback
                    var originalText = DiscordTab.TxtProfileDiscordId.Text;
                    DiscordTab.TxtProfileDiscordId.Text = Loc.Get("btn_copied");
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(() =>
                        {
                            if (DiscordTab.TxtProfileDiscordId != null)
                                DiscordTab.TxtProfileDiscordId.Text = originalText;
                        });
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to copy Discord ID to clipboard");
                }
            }
        }

        internal void BtnProfileDiscord_Click(object sender, RoutedEventArgs e)
        {
            // Get Discord ID from button's Tag
            var button = sender as Button;
            var discordId = button?.Tag as string;

            if (string.IsNullOrEmpty(discordId))
            {
                discordId = DiscordTab.TxtProfileDiscordId?.Text;
            }

            if (!string.IsNullOrEmpty(discordId))
            {
                try
                {
                    // Open Discord profile in browser using rundll32 to force browser
                    var profileUrl = $"https://discord.com/users/{discordId}";
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = $"url.dll,FileProtocolHandler {profileUrl}",
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    App.Logger?.Information("Opened Discord profile for user: {DiscordId}", discordId);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open Discord profile");
                    // Fallback: copy to clipboard
                    try
                    {
                        System.Windows.Clipboard.SetText(discordId);
                        if (DiscordTab.TxtProfileDiscordId != null)
                        {
                            var originalText = DiscordTab.TxtProfileDiscordId.Text;
                            DiscordTab.TxtProfileDiscordId.Text = Loc.Get("label_id_copied");
                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                                Dispatcher.Invoke(() =>
                                {
                                    if (DiscordTab.TxtProfileDiscordId != null)
                                        DiscordTab.TxtProfileDiscordId.Text = originalText;
                                });
                            });
                        }
                    }
                    catch { }
                }
            }
        }

        internal async void BtnChangeDisplayName_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentName = App.Settings?.Current?.UserDisplayName ?? "";
                var dialog = new DisplayNameDialog(isChangeName: true, currentName: currentName);
                dialog.Owner = this;
                if (dialog.ShowDialog() != true) return;

                var newName = dialog.DisplayName;
                if (string.Equals(newName, currentName, StringComparison.Ordinal)) return;

                if (App.ProfileSync == null) return;

                // Disable button during request
                if (DiscordTab.BtnChangeDisplayName != null) DiscordTab.BtnChangeDisplayName.IsEnabled = false;

                var (success, error, resultName) = await App.ProfileSync.ChangeDisplayNameAsync(newName);

                if (success && resultName != null)
                {
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.UserDisplayName = resultName;
                        App.Settings.Save();
                    }
                    if (DiscordTab.TxtProfileViewerName != null)
                        DiscordTab.TxtProfileViewerName.Text = resultName;
                    UpdateQuickLoginUI();
                }
                else
                {
                    MessageBox.Show(
                        error ?? Loc.Get("msg_failed_to_change_display_name"),
                        Loc.Get("title_name_change_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error changing display name");
                MessageBox.Show(
                    Loc.Get("msg_error_changing_name"),
                    Loc.Get("label_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (DiscordTab.BtnChangeDisplayName != null) DiscordTab.BtnChangeDisplayName.IsEnabled = true;
            }
        }

        internal async void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new DisplayNameDialog("delete");
                dialog.Owner = this;
                if (dialog.ShowDialog() != true) return;

                if (App.ProfileSync == null) return;

                // Disable button during request
                if (DiscordTab.BtnDeleteProfile != null) DiscordTab.BtnDeleteProfile.IsEnabled = false;

                var (success, error) = await App.ProfileSync.DeleteAccountAsync();

                if (success)
                {
                    App.ProfileSync?.StopHeartbeat();
                    App.Patreon?.Logout();
                    App.Discord?.Logout();

                    ClearAccountData();

                    MessageBox.Show(
                        Loc.Get("msg_profile_deleted"),
                        Loc.Get("title_profile_deleted"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        error ?? Loc.Get("msg_failed_to_delete_profile"),
                        Loc.Get("title_deletion_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error deleting profile");
                MessageBox.Show(
                    Loc.Get("msg_error_deleting_profile"),
                    Loc.Get("label_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (DiscordTab.BtnDeleteProfile != null) DiscordTab.BtnDeleteProfile.IsEnabled = true;
            }
        }

        /// <summary>
        /// Search leaderboard for a profile by display name and show it.
        /// Returns true if a match was found and displayed, false otherwise.
        /// </summary>
        private bool SearchAndDisplayProfile(string? searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName))
            {
                return false;
            }

            App.Logger?.Information("SearchAndDisplayProfile: Searching for '{SearchName}'", searchName);

            // Search in leaderboard entries
            var entries = App.Leaderboard?.Entries;
            if (entries == null || entries.Count == 0)
            {
                App.Logger?.Information("SearchAndDisplayProfile: No entries, refreshing leaderboard...");
                // Try to refresh leaderboard first
                _ = RefreshAndSearchAsync(searchName);
                return false;
            }

            App.Logger?.Information("SearchAndDisplayProfile: Searching {Count} entries", entries.Count);

            // Find matching entry (case-insensitive)
            var entry = entries.FirstOrDefault(e =>
                e.DisplayName?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);

            if (entry != null)
            {
                App.Logger?.Information("SearchAndDisplayProfile: Found exact match '{Name}'", entry.DisplayName);
                DisplayProfileEntry(entry);
                return true;
            }

            // No exact match - try partial match
            entry = entries.FirstOrDefault(e =>
                e.DisplayName?.Contains(searchName, StringComparison.OrdinalIgnoreCase) == true);

            if (entry != null)
            {
                App.Logger?.Information("SearchAndDisplayProfile: Found partial match '{Name}'", entry.DisplayName);
                DisplayProfileEntry(entry);
                return true;
            }

            App.Logger?.Information("SearchAndDisplayProfile: No match found for '{SearchName}'", searchName);
            // Show not found message
            if (DiscordTab.NoProfileSelected != null)
            {
                DiscordTab.NoProfileSelected.Visibility = Visibility.Visible;
            }
            if (DiscordTab.ProfileCardWrapper != null)
            {
                DiscordTab.ProfileCardWrapper.Visibility = Visibility.Collapsed;
            }
            return false;
        }

        private async Task RefreshAndSearchAsync(string searchName)
        {
            if (App.Leaderboard != null)
            {
                await App.Leaderboard.RefreshAsync();

                // After refresh, try to find the profile but don't recurse if still empty
                var entries = App.Leaderboard?.Entries;
                if (entries != null && entries.Count > 0)
                {
                    var entry = entries.FirstOrDefault(e =>
                        e.DisplayName?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);

                    if (entry == null)
                    {
                        entry = entries.FirstOrDefault(e =>
                            e.DisplayName?.Contains(searchName, StringComparison.OrdinalIgnoreCase) == true);
                    }

                    if (entry != null)
                    {
                        DisplayProfileEntry(entry);
                        return;
                    }
                }

                // Show not found message
                if (DiscordTab.NoProfileSelected != null)
                {
                    DiscordTab.NoProfileSelected.Visibility = Visibility.Visible;
                }
                if (DiscordTab.ProfileCardWrapper != null)
                {
                    DiscordTab.ProfileCardWrapper.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void DisplayOwnProfile()
        {
            // Display local profile when not on leaderboard
            if (DiscordTab.ProfileCardWrapper != null) DiscordTab.ProfileCardWrapper.Visibility = Visibility.Visible;
            if (DiscordTab.NoProfileSelected != null) DiscordTab.NoProfileSelected.Visibility = Visibility.Collapsed;

            // OG user animated border for own profile
            var isOg = App.Settings?.Current?.IsSeason0Og == true;
            if (DiscordTab.OgBorderContainer != null)
            {
                DiscordTab.OgBorderContainer.Visibility = isOg ? Visibility.Visible : Visibility.Collapsed;
                // See the sibling site above: ApplyOgBorderLoop owns the clock (PR-5).
                ApplyOgBorderLoop();
            }
            // OG GOOD GIRL banner badge for own profile
            if (DiscordTab.OgBannerBadge != null)
            {
                DiscordTab.OgBannerBadge.Visibility = isOg ? Visibility.Visible : Visibility.Collapsed;
            }

            // Avatar - load from Discord only if ShareProfilePicture is enabled
            if (DiscordTab.ProfileViewerAvatar != null)
            {
                string? avatarUrl = null;
                // Only show avatar if user has ShareProfilePicture enabled
                if (App.Settings?.Current?.ShareProfilePicture == true && App.Discord?.IsAuthenticated == true)
                {
                    avatarUrl = App.Discord.GetAvatarUrl(256);
                }

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(avatarUrl);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        DiscordTab.ProfileViewerAvatar.ImageSource = bitmap;
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to load profile avatar");
                        DiscordTab.ProfileViewerAvatar.ImageSource = null;
                    }
                }
                else
                {
                    DiscordTab.ProfileViewerAvatar.ImageSource = null;
                }
            }

            // Name - use V2 unified display name (leaderboard name), never raw provider names
            if (DiscordTab.TxtProfileViewerName != null)
                DiscordTab.TxtProfileViewerName.Text = App.Settings?.Current?.UserDisplayName
                    ?? App.Discord?.CustomDisplayName ?? App.Patreon?.DisplayName ?? "You";

            // Show edit name button for own profile (only if logged in with unified ID)
            if (DiscordTab.BtnChangeDisplayName != null)
                DiscordTab.BtnChangeDisplayName.Visibility = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Show delete profile button for own profile (only if logged in with unified ID)
            if (DiscordTab.BtnDeleteProfile != null)
                DiscordTab.BtnDeleteProfile.Visibility = !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Online status
            if (DiscordTab.TxtProfileViewerOnline != null)
            {
                DiscordTab.TxtProfileViewerOnline.Text = Loc.Get("label_online");
                DiscordTab.TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#43B581"));
            }
            if (DiscordTab.ProfileOnlineIndicator != null)
                DiscordTab.ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#43B581"));

            // Discord button
            if (DiscordTab.BtnProfileDiscord != null && DiscordTab.TxtProfileDiscordId != null)
            {
                if (App.Settings?.Current?.AllowDiscordDm == true && !string.IsNullOrEmpty(App.Discord?.UserId))
                {
                    DiscordTab.BtnProfileDiscord.Visibility = Visibility.Visible;
                    // Use V2 unified name for consistency, fall back to Discord display
                    DiscordTab.TxtProfileDiscordId.Text = App.Settings?.Current?.UserDisplayName
                        ?? App.Discord.CustomDisplayName ?? App.Discord.UserId;
                    DiscordTab.BtnProfileDiscord.Tag = App.Discord.UserId; // Store ID for click handler
                }
                else
                {
                    DiscordTab.BtnProfileDiscord.Visibility = Visibility.Collapsed;
                }
            }

            // Stats from local data
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var localXp = App.Settings?.Current?.PlayerXP ?? 0;
            var xp = App.Progression?.GetTotalXP(level, localXp) ?? localXp;
            var progress = App.Achievements?.Progress;

            if (DiscordTab.TxtProfileViewerLevel != null) DiscordTab.TxtProfileViewerLevel.Text = level.ToString();

            // Rank (own rank from leaderboard, if available)
            if (DiscordTab.TxtProfileViewerRank != null)
            {
                // Prefer server-provided rank (works even beyond top 200)
                var serverRank = App.Leaderboard?.YourRank;
                if (serverRank.HasValue && serverRank.Value > 0)
                {
                    DiscordTab.TxtProfileViewerRank.Text = $"#{serverRank.Value}";
                }
                else
                {
                    // Fallback: scan local entries by unified_id or display name
                    var unifiedId = App.UnifiedUserId;
                    var displayName = App.Settings?.Current?.UserDisplayName;

                    var ownEntry = !string.IsNullOrEmpty(unifiedId)
                        ? App.Leaderboard?.Entries?.FirstOrDefault(e =>
                            e.UnifiedId == unifiedId)
                        : null;

                    ownEntry ??= !string.IsNullOrEmpty(displayName)
                        ? App.Leaderboard?.Entries?.FirstOrDefault(e =>
                            e.DisplayName?.Equals(displayName, StringComparison.OrdinalIgnoreCase) == true)
                        : null;

                    DiscordTab.TxtProfileViewerRank.Text = ownEntry?.Rank > 0 ? $"#{ownEntry.Rank}" : "#-";
                }
            }
            if (DiscordTab.TxtProfileViewerXp != null) DiscordTab.TxtProfileViewerXp.Text = FormatNumber(xp);
            if (DiscordTab.TxtProfileViewerBubbles != null) DiscordTab.TxtProfileViewerBubbles.Text = FormatNumber(progress?.TotalBubblesPopped ?? 0);
            if (DiscordTab.TxtProfileViewerVideos != null)
            {
                var minutes = progress?.TotalVideoMinutes ?? 0;
                DiscordTab.TxtProfileViewerVideos.Text = minutes >= 60 ? $"{minutes / 60:F1}h" : $"{minutes:F0}m";
            }
            if (DiscordTab.TxtProfileViewerGifs != null) DiscordTab.TxtProfileViewerGifs.Text = FormatNumber(progress?.TotalFlashImages ?? 0);
            if (DiscordTab.TxtProfileViewerLockCards != null) DiscordTab.TxtProfileViewerLockCards.Text = FormatNumber(progress?.TotalLockCardsCompleted ?? 0);
            // Free-only count so the patron-exclusive set is never folded into this number.
            var unlockedCount = App.Achievements?.GetUnlockedCount(exclusive: false) ?? 0;
            var totalCount = App.Achievements?.GetTotalCount(exclusive: false)
                        ?? System.Linq.Enumerable.Count(Models.Achievement.All.Values, a => !a.IsExclusive && !a.IsHidden);
            if (DiscordTab.TxtProfileViewerAchievements != null)
            {
                DiscordTab.TxtProfileViewerAchievements.Text = $"{unlockedCount} / {totalCount}";
            }

            // Trainer Card surfaces (redesign Phase 1): hero XP meter, Showcase progress and the
            // "back to me" chip. PlayerXP is progress inside the level; `xp` above is lifetime.
            SetProfileViewingSelf(true);
            UpdateProfileXpMeter(level, localXp);
            UpdateProfileShowcase(unlockedCount, totalCount, progress?.UnlockedAchievements);
            // Phase 2 cosmetics. Applied AFTER UpdateProfileShowcase because both touch the empty
            // pin placeholders and this one is the authority on whether anything is pinned.
            ApplyOwnProfileCosmetics();

            // Patreon badge - use settings tier (works for Discord-only login with linked Patreon)
            var patreonTier = App.Settings?.Current?.PatreonTier ?? (int)(App.Patreon?.CurrentTier ?? 0);
            var hasPatreon = patreonTier >= 1 || App.Patreon?.IsWhitelisted == true;

            if (DiscordTab.ProfilePatreonBadge != null)
            {
                if (patreonTier > 0)
                {
                    DiscordTab.ProfilePatreonBadge.Visibility = Visibility.Visible;
                    DiscordTab.ProfilePatreonBadge.Source = LoadPatreonBadgeImage(patreonTier);
                }
                else
                {
                    DiscordTab.ProfilePatreonBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier badge next to Discord button (same as leaderboard)
            if (DiscordTab.ProfilePatreonTierBadge != null)
            {
                if (hasPatreon)
                {
                    DiscordTab.ProfilePatreonTierBadge.Visibility = Visibility.Visible;
                    // Use tier 1 as fallback for whitelisted users with tier 0
                    DiscordTab.ProfilePatreonTierBadge.Source = LoadPatreonBadgeImage(patreonTier > 0 ? patreonTier : 1);
                }
                else
                {
                    DiscordTab.ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier banner (Pink filter / Prime subject images)
            // Shows for tier 1+, tier 2+, tier 3, OR whitelisted users
            if (DiscordTab.ProfilePatreonTierBanner != null && DiscordTab.ImgPatreonTierBanner != null)
            {
                if (hasPatreon)
                {
                    DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Visible;
                    try
                    {
                        // Tier 3 = Prime subject, everyone else = Pink filter
                        var bannerImage = patreonTier >= 3 ? "prime subject.webp" : "Pink filter.webp";
                        DiscordTab.ImgPatreonTierBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri($"pack://application:,,,/Resources/{bannerImage}", UriKind.Absolute));
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to load Patreon tier banner image");
                        DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                }
            }

            // Load achievement images for own profile
            if (progress?.UnlockedAchievements != null && progress.UnlockedAchievements.Count > 0)
            {
                LoadProfileAchievementImages(progress.UnlockedAchievements);
            }
            else
            {
                if (DiscordTab.ProfileAchievementGrid != null) DiscordTab.ProfileAchievementGrid.ItemsSource = null;
                if (DiscordTab.TxtNoAchievements != null)
                {
                    DiscordTab.TxtNoAchievements.Text = Loc.Get("label_no_achievements_yet");
                    DiscordTab.TxtNoAchievements.Visibility = Visibility.Visible;
                }
            }
        }

        private void DisplayProfileEntry(Services.LeaderboardEntry entry)
        {
            try
            {
            if (DiscordTab.ProfileCardWrapper != null) DiscordTab.ProfileCardWrapper.Visibility = Visibility.Visible;
            if (DiscordTab.NoProfileSelected != null) DiscordTab.NoProfileSelected.Visibility = Visibility.Collapsed;

            // OG user animated border
            if (DiscordTab.OgBorderContainer != null)
            {
                DiscordTab.OgBorderContainer.Visibility = entry.IsSeason0Og
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                // Starting/stopping the rotation is ApplyOgBorderLoop's job (PR-5): it also
                // parks the loop when the tab is hidden, the window is inactive or the user
                // turned motion down. See MainWindow.ProfileFx.cs.
                ApplyOgBorderLoop();
            }
            // OG GOOD GIRL banner badge next to name
            if (DiscordTab.OgBannerBadge != null)
            {
                DiscordTab.OgBannerBadge.Visibility = entry.IsSeason0Og ? Visibility.Visible : Visibility.Collapsed;
            }

            // Avatar - clear previous, will be loaded async
            if (DiscordTab.ProfileViewerAvatar != null)
            {
                DiscordTab.ProfileViewerAvatar.ImageSource = null;
            }

            // Name
            if (DiscordTab.TxtProfileViewerName != null)
                DiscordTab.TxtProfileViewerName.Text = entry.DisplayName ?? "Unknown";

            // Online status (from cached data initially)
            if (DiscordTab.TxtProfileViewerOnline != null)
            {
                DiscordTab.TxtProfileViewerOnline.Text = entry.IsOnline ? "Online" : "Offline";
                DiscordTab.TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        entry.IsOnline ? "#43B581" : "#747F8D"));
            }
            if (DiscordTab.ProfileOnlineIndicator != null)
                DiscordTab.ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        entry.IsOnline ? "#43B581" : "#747F8D"));

            // Trigger async lookup to get fresh online status and avatar
            if (!string.IsNullOrEmpty(entry.DisplayName))
            {
                _ = RefreshProfileViewerAsync(entry.DisplayName);
            }

            // Discord button (only if they have it and allow DMs)
            if (DiscordTab.BtnProfileDiscord != null && DiscordTab.TxtProfileDiscordId != null)
            {
                if (entry.HasDiscord && !string.IsNullOrEmpty(entry.DiscordId))
                {
                    DiscordTab.BtnProfileDiscord.Visibility = Visibility.Visible;
                    DiscordTab.TxtProfileDiscordId.Text = entry.DisplayName ?? "Message on Discord";
                    DiscordTab.BtnProfileDiscord.Tag = entry.DiscordId; // Store ID for click handler
                }
                else
                {
                    DiscordTab.BtnProfileDiscord.Visibility = Visibility.Collapsed;
                }
            }

            // Stats
            if (DiscordTab.TxtProfileViewerLevel != null) DiscordTab.TxtProfileViewerLevel.Text = entry.Level.ToString();

            // Rank
            if (DiscordTab.TxtProfileViewerRank != null)
            {
                DiscordTab.TxtProfileViewerRank.Text = entry.Rank > 0 ? $"#{entry.Rank}" : "#-";
            }
            if (DiscordTab.TxtProfileViewerXp != null) DiscordTab.TxtProfileViewerXp.Text = entry.XpDisplay;
            if (DiscordTab.TxtProfileViewerBubbles != null) DiscordTab.TxtProfileViewerBubbles.Text = entry.BubblesPoppedDisplay;
            if (DiscordTab.TxtProfileViewerVideos != null)
            {
                var hours = entry.VideoMinutes / 60.0;
                DiscordTab.TxtProfileViewerVideos.Text = hours >= 1 ? $"{hours:F1}h" : $"{entry.VideoMinutes:F0}m";
            }
            if (DiscordTab.TxtProfileViewerGifs != null) DiscordTab.TxtProfileViewerGifs.Text = entry.GifsSpawnedDisplay;
            if (DiscordTab.TxtProfileViewerLockCards != null) DiscordTab.TxtProfileViewerLockCards.Text = entry.LockCardsCompleted.ToString();
            if (DiscordTab.TxtProfileViewerAchievements != null) DiscordTab.TxtProfileViewerAchievements.Text = entry.AchievementsDisplay;

            // Check if this is the current user's profile - if so, use local Patreon data
            // which is more accurate than leaderboard cache
            var isOwnProfile = entry.DisplayName?.Equals(
                App.Settings?.Current?.UserDisplayName, StringComparison.OrdinalIgnoreCase) == true;

            // Edit name button - only visible on own profile
            if (DiscordTab.BtnChangeDisplayName != null)
                DiscordTab.BtnChangeDisplayName.Visibility = isOwnProfile && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Delete profile button - only visible on own profile
            if (DiscordTab.BtnDeleteProfile != null)
                DiscordTab.BtnDeleteProfile.Visibility = isOwnProfile && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId)
                    ? Visibility.Visible : Visibility.Collapsed;

            int tierToUse;
            bool hasPatreonAccess;

            if (isOwnProfile)
            {
                // Use local Patreon data for own profile
                tierToUse = App.Settings?.Current?.PatreonTier ?? (int)(App.Patreon?.CurrentTier ?? 0);
                hasPatreonAccess = tierToUse >= 1 || App.Patreon?.IsWhitelisted == true;
            }
            else
            {
                // Use leaderboard entry data for other users
                tierToUse = entry.PatreonTier;
                hasPatreonAccess = entry.IsPatreon && entry.PatreonTier >= 1;
            }

            // Patreon badge (next to Level/Rank)
            if (DiscordTab.ProfilePatreonBadge != null)
            {
                if (hasPatreonAccess && tierToUse > 0)
                {
                    DiscordTab.ProfilePatreonBadge.Visibility = Visibility.Visible;
                    DiscordTab.ProfilePatreonBadge.Source = LoadPatreonBadgeImage(tierToUse);
                }
                else
                {
                    DiscordTab.ProfilePatreonBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier badge next to Discord button (same as leaderboard)
            if (DiscordTab.ProfilePatreonTierBadge != null)
            {
                if (hasPatreonAccess)
                {
                    DiscordTab.ProfilePatreonTierBadge.Visibility = Visibility.Visible;
                    // Use tier 1 as fallback for whitelisted users with tier 0
                    DiscordTab.ProfilePatreonTierBadge.Source = LoadPatreonBadgeImage(tierToUse > 0 ? tierToUse : 1);
                }
                else
                {
                    DiscordTab.ProfilePatreonTierBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Patreon tier banner (Pink filter / Prime subject images)
            // Shows for any Patreon supporter (tier 1+)
            if (DiscordTab.ProfilePatreonTierBanner != null && DiscordTab.ImgPatreonTierBanner != null)
            {
                if (hasPatreonAccess)
                {
                    DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Visible;
                    try
                    {
                        // Tier 3 = Prime subject, everyone else = Pink filter
                        var bannerImage = tierToUse >= 3 ? "prime subject.webp" : "Pink filter.webp";
                        DiscordTab.ImgPatreonTierBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri($"pack://application:,,,/Resources/{bannerImage}", UriKind.Absolute));
                    }
                    catch
                    {
                        DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    DiscordTab.ProfilePatreonTierBanner.Visibility = Visibility.Collapsed;
                }
            }

            // We don't have detailed achievement list from leaderboard, just the count
            // So hide the achievement grid for other users or show placeholder
            if (DiscordTab.ProfileAchievementGrid != null)
            {
                DiscordTab.ProfileAchievementGrid.ItemsSource = null;
            }
            if (DiscordTab.TxtNoAchievements != null)
            {
                DiscordTab.TxtNoAchievements.Text = $"{entry.AchievementsCount} achievements unlocked";
                DiscordTab.TxtNoAchievements.Visibility = Visibility.Visible;
            }

            // Trainer Card surfaces (redesign Phase 1). The leaderboard hands out a count, not the
            // unlocked ids, so the Showcase's "next up" line stays hidden for other people's cards.
            //
            // entry.Xp is LIFETIME xp — it is exactly what the client uploads via
            // ProgressionService.GetTotalXP, and it is what the ledger's "XP" row shows. The hero
            // meter wants progress INSIDE the current level, so run it back through the documented
            // inverse; feeding it the lifetime value pins every searched card's bar at 100%.
            SetProfileViewingSelf(isOwnProfile);
            UpdateProfileXpMeter(
                entry.Level,
                App.Progression?.GetCurrentLevelXP(entry.Level, entry.Xp) ?? 0);
            UpdateProfileShowcase(
                entry.AchievementsCount,
                App.Achievements?.GetTotalCount(exclusive: false)
                    ?? System.Linq.Enumerable.Count(Models.Achievement.All.Values, a => !a.IsExclusive && !a.IsHidden),
                isOwnProfile ? App.Achievements?.Progress?.UnlockedAchievements : null);

            // Phase 2 cosmetics. The leaderboard entry carries none, so someone else's card starts
            // bare and is dressed by the /user/lookup round-trip already in flight above; your own
            // card is dressed from settings immediately and never waits on the network.
            if (isOwnProfile) ApplyOwnProfileCosmetics();
            else ApplyViewedProfileCosmetics(null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "DisplayProfileEntry failed for {Name}", entry?.DisplayName);
            }
        }

        /// <summary>
        /// Refresh profile viewer with fresh data from server (online status, avatar)
        /// </summary>
        private async Task RefreshProfileViewerAsync(string displayName)
        {
            try
            {
                var lookup = await App.Leaderboard?.LookupUserAsync(displayName);
                if (lookup == null) return;

                // Update on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    // Verify we're still showing this user (user may have clicked away)
                    if (DiscordTab.TxtProfileViewerName?.Text != displayName) return;

                    // Update online status
                    if (DiscordTab.TxtProfileViewerOnline != null)
                    {
                        DiscordTab.TxtProfileViewerOnline.Text = lookup.IsOnline ? "Online" : "Offline";
                        DiscordTab.TxtProfileViewerOnline.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                                lookup.IsOnline ? "#43B581" : "#747F8D"));
                    }
                    if (DiscordTab.ProfileOnlineIndicator != null)
                    {
                        DiscordTab.ProfileOnlineIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                                lookup.IsOnline ? "#43B581" : "#747F8D"));
                    }

                    // Load avatar if available
                    if (DiscordTab.ProfileViewerAvatar != null)
                    {
                        string? avatarUrl = lookup.AvatarUrl;

                        // Fallback: if viewing own profile and server didn't return avatar, use local Discord avatar
                        // BUT only if user has ShareProfilePicture enabled (respect their privacy setting)
                        if (string.IsNullOrEmpty(avatarUrl) && App.Settings?.Current?.ShareProfilePicture == true)
                        {
                            var ownDisplayName = App.Settings?.Current?.UserDisplayName
                                               ?? App.Discord?.CustomDisplayName
                                               ?? App.Discord?.DisplayName
                                               ?? App.Patreon?.DisplayName;
                            if (displayName.Equals(ownDisplayName, StringComparison.OrdinalIgnoreCase) && App.Discord?.IsAuthenticated == true)
                            {
                                avatarUrl = App.Discord.GetAvatarUrl(256);
                            }
                        }

                        if (!string.IsNullOrEmpty(avatarUrl))
                        {
                            try
                            {
                                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(avatarUrl);
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                DiscordTab.ProfileViewerAvatar.ImageSource = bitmap;
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning(ex, "Failed to load profile avatar from {Url}", avatarUrl);
                                DiscordTab.ProfileViewerAvatar.ImageSource = null;
                            }
                        }
                        else
                        {
                            // No avatar URL - clear any previous image
                            DiscordTab.ProfileViewerAvatar.ImageSource = null;
                        }
                    }

                    // Trainer Card cosmetics (Phase 2). Your own card is dressed from settings
                    // instead: the local copy is what you just picked in the Customize dialog and
                    // the server echo may still be a sync behind it.
                    var ownName = App.Settings?.Current?.UserDisplayName
                                  ?? App.Discord?.CustomDisplayName
                                  ?? App.Discord?.DisplayName
                                  ?? App.Patreon?.DisplayName;
                    if (!string.IsNullOrEmpty(ownName) &&
                        displayName.Equals(ownName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyOwnProfileCosmetics();
                    }
                    else
                    {
                        ApplyViewedProfileCosmetics(lookup.Cosmetics);
                    }

                    // Load achievements from lookup result (for other users)
                    if (lookup.Achievements != null && lookup.Achievements.Count > 0)
                    {
                        var achievementSet = new HashSet<string>(lookup.Achievements);
                        LoadProfileAchievementImages(achievementSet);
                    }
                    else if (lookup.AchievementsCount > 0)
                    {
                        // Fallback: server returned count but no list (shouldn't happen with updated server)
                        if (DiscordTab.TxtNoAchievements != null)
                        {
                            DiscordTab.TxtNoAchievements.Text = $"{lookup.AchievementsCount} achievements unlocked";
                            DiscordTab.TxtNoAchievements.Visibility = Visibility.Visible;
                        }
                        if (DiscordTab.ProfileAchievementGrid != null)
                        {
                            DiscordTab.ProfileAchievementGrid.ItemsSource = null;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh profile viewer for {Name}", displayName);
            }
        }

        private System.Windows.Media.Imaging.BitmapImage? LoadPatreonBadgeImage(int tier)
        {
            try
            {
                var imageName = tier switch
                {
                    1 => "Patreon tier1.png",
                    2 => "Patreon tier2.png",
                    3 => "Patreon tier3.png",
                    _ => "Patreon tier1.png"
                };
                return new System.Windows.Media.Imaging.BitmapImage(
                    new Uri($"pack://application:,,,/Resources/{imageName}", UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        private void LoadProfileAchievementImages(HashSet<string>? unlockedAchievements)
        {
            if (DiscordTab.ProfileAchievementGrid == null) return;

            if (unlockedAchievements == null || unlockedAchievements.Count == 0)
            {
                DiscordTab.ProfileAchievementGrid.ItemsSource = null;
                if (DiscordTab.TxtNoAchievements != null) DiscordTab.TxtNoAchievements.Visibility = Visibility.Visible;
                return;
            }

            if (DiscordTab.TxtNoAchievements != null) DiscordTab.TxtNoAchievements.Visibility = Visibility.Collapsed;

            var achievementItems = new List<object>();
            foreach (var achievementId in unlockedAchievements)
            {
                var achievement = Models.Achievement.All.Values.FirstOrDefault(a => a.Id == achievementId);
                if (achievement != null)
                {
                    var image = LoadAchievementImage(achievement.ImageName);
                    if (image != null)
                    {
                        achievementItems.Add(new { Name = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name, Image = image });
                    }
                }
            }

            DiscordTab.ProfileAchievementGrid.ItemsSource = achievementItems;
        }

        private string FormatNumber(double number)
        {
            if (number >= 1_000_000) return $"{number / 1_000_000:F1}M";
            if (number >= 1_000) return $"{number / 1_000:F1}k";
            return number.ToString("N0");
        }

        #endregion

        /// <summary>
        /// Toggles the integrated browser's audio (BambiCloud / HypnoTube video).
        /// Persists to <see cref="AppSettings.BrowserVideoMuted"/> and applies live via
        /// CoreWebView2.IsMuted (the BrowserService re-applies it on init too).
        /// </summary>
        internal void BtnMuteBrowser_Click(object sender, RoutedEventArgs e)
        {
            var muted = !(App.Settings?.Current?.BrowserVideoMuted ?? false);
            if (App.Settings?.Current != null) App.Settings.Current.BrowserVideoMuted = muted;
            if (_browser != null) _browser.IsAudioMuted = muted;
            App.Settings?.Save();
            SyncBrowserMuteIcon();
        }

        /// <summary>Updates the header mute glyph to match the saved preference.</summary>
        internal void SyncBrowserMuteIcon()
        {
            if (SettingsTab?.TxtBrowserMute == null) return;
            SettingsTab.TxtBrowserMute.Text = App.Settings?.Current?.BrowserVideoMuted == true ? "🔇" : "🔊";
        }

        internal async void BtnPopOutBrowser_Click(object sender, RoutedEventArgs e)
        {
            // Block in offline mode
            if (App.Settings?.Current?.OfflineMode == true) return;

            // Lazy-load browser on first pop-out
            if (!_browserInitialized)
            {
                await InitializeBrowserAsync();
            }

            if (_browser?.WebView == null) return;

            // If already popped out, bring the window to front
            if (_browserPopoutWindow != null)
            {
                _browserPopoutWindow.Activate();
                return;
            }

            try
            {
                // Remove WebView from embedded container
                if (SettingsTab.BrowserContainer.Children.Contains(_browser.WebView))
                {
                    SettingsTab.BrowserContainer.Children.Remove(_browser.WebView);
                }

                // Show placeholder in the embedded container
                SettingsTab.BrowserLoadingText.Text = Loc.Get("label_browser_popped_out_nclick_to_focus_window");
                SettingsTab.BrowserLoadingText.Visibility = Visibility.Visible;

                // Create popup window
                _browserPopoutWindow = new Window
                {
                    Title = Loc.Get("title_browser_window"),
                    Width = 1024,
                    Height = 768,
                    MinWidth = 400,
                    MinHeight = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Content = _browser.WebView
                };

                // Handle window CLOSING (before close) - detach WebView to prevent parent/child errors
                _browserPopoutWindow.Closing += (s, args) =>
                {
                    // Exit browser fullscreen first if the popout is being closed while fullscreen
                    if (_isBrowserFullscreen && _browserFullscreenWasPopout)
                    {
                        _isBrowserFullscreen = false;
                        _browserFullscreenWasPopout = false;
                        if (_browser != null)
                            _browser.ZoomFactor = _browserPreFullscreenZoom;
                    }

                    if (_browserPopoutWindow != null)
                    {
                        // CRITICAL: Remove WebView from window content BEFORE closing
                        // This prevents "window is a parent/child of another" errors
                        _browserPopoutWindow.Content = null;
                    }
                };

                // Handle window CLOSED (after close) - return browser to embedded container
                _browserPopoutWindow.Closed += (s, args) =>
                {
                    if (_browser?.WebView != null)
                    {
                        // Add back to embedded container
                        if (!SettingsTab.BrowserContainer.Children.Contains(_browser.WebView))
                        {
                            SettingsTab.BrowserContainer.Children.Add(_browser.WebView);
                        }
                        SettingsTab.BrowserLoadingText.Visibility = Visibility.Collapsed;
                    }
                    _browserPopoutWindow = null;
                    SettingsTab.BtnPopOutBrowser.Content = Loc.Get("btn_pop_out");
                    SettingsTab.BtnPopOutBrowser.ToolTip = Loc.Get("tooltip_pop_out_browser_to_resizable_window");
                };

                // Update button to show it's popped out
                SettingsTab.BtnPopOutBrowser.Content = Loc.Get("btn_focus");
                SettingsTab.BtnPopOutBrowser.ToolTip = Loc.Get("tooltip_browser_is_popped_out_click_to_focus");

                _browserPopoutWindow.Show();
                App.Logger?.Information("Browser popped out to separate window");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to pop out browser");
                // Try to restore browser to container
                if (_browser?.WebView != null && !SettingsTab.BrowserContainer.Children.Contains(_browser.WebView))
                {
                    SettingsTab.BrowserContainer.Children.Add(_browser.WebView);
                    SettingsTab.BrowserLoadingText.Visibility = Visibility.Collapsed;
                }
                _browserPopoutWindow = null;
            }
        }

        private void HandleBrowserFullscreenChanged(bool isFullscreen)
        {
            if (_browser?.WebView == null) return;

            if (isFullscreen)
            {
                var screens = App.GetAllScreensCached();
                var useDualMonitor = App.Settings.Current.DualMonitorEnabled && screens.Length > 1;

                if (useDualMonitor)
                {
                    _isDualMonitorPlaybackActive = App.ScreenMirror.EnableMirror();
                    if (_isDualMonitorPlaybackActive)
                    {
                        App.Logger?.Information("Screen mirroring enabled for fullscreen video");
                    }
                }

                // Always reparent — single-monitor users still need real
                // full-monitor fullscreen, otherwise HT's HTML5 fullscreen
                // just renders inside the dashboard cell. The dblclick exit
                // works via the JS click-pair detector + ccp_exit_fullscreen
                // WebMessage path (window._ccpForcedFs flag covers the case
                // where the page lost HTML5 fullscreen during reparent).
                EnterBrowserFullscreen();
            }
            else
            {
                if (_isDualMonitorPlaybackActive)
                {
                    App.ScreenMirror.DisableMirror();
                    _isDualMonitorPlaybackActive = false;
                    App.Logger?.Information("Screen mirroring disabled");
                }

                ExitBrowserFullscreen();
            }
        }

        public void EnterBrowserFullscreen()
        {
            if (_browser?.WebView == null || _isBrowserFullscreen) return;

            try
            {
                // Save avatar attached state before entering fullscreen
                _avatarWasAttachedBeforeBrowserFullscreen = _avatarTubeWindow != null && !_avatarTubeWindow.IsDetached;
                _browserPreFullscreenZoom = _browser.ZoomFactor;
                _browser.ZoomFactor = 1.0;
                _isBrowserFullscreen = true;

                if (_browserPopoutWindow != null)
                {
                    // === POPOUT MODE: user already had browser popped out ===
                    _browserFullscreenWasPopout = true;

                    // Save popout window state for restore
                    _popoutPreFsStyle = _browserPopoutWindow.WindowStyle;
                    _popoutPreFsResize = _browserPopoutWindow.ResizeMode;
                    _popoutPreFsState = _browserPopoutWindow.WindowState;
                    _popoutPreFsLeft = _browserPopoutWindow.Left;
                    _popoutPreFsTop = _browserPopoutWindow.Top;
                    _popoutPreFsWidth = _browserPopoutWindow.Width;
                    _popoutPreFsHeight = _browserPopoutWindow.Height;
                    _popoutPreFsTopmost = _browserPopoutWindow.Topmost;

                    // Go fullscreen in-place
                    if (_browserPopoutWindow.WindowState == WindowState.Maximized)
                        _browserPopoutWindow.WindowState = WindowState.Normal;

                    _browserPopoutWindow.WindowStyle = WindowStyle.None;
                    _browserPopoutWindow.ResizeMode = ResizeMode.NoResize;
                    _browserPopoutWindow.Topmost = true;
                    _browserPopoutWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    _browserPopoutWindow.WindowState = WindowState.Maximized;
                }
                else
                {
                    // === EMBEDDED MODE: create fullscreen window directly ===
                    // Same approach as the mandatory video windows which work correctly:
                    // Create Window with WindowStyle.None from the start, Show, then Maximize.
                    _browserFullscreenWasPopout = false;

                    // Remove WebView from embedded container
                    if (SettingsTab.BrowserContainer.Children.Contains(_browser.WebView))
                    {
                        SettingsTab.BrowserContainer.Children.Remove(_browser.WebView);
                    }
                    SettingsTab.BrowserLoadingText.Text = "\ud83c\udf10 Browser in fullscreen";
                    SettingsTab.BrowserLoadingText.Visibility = Visibility.Visible;

                    var screen = System.Windows.Forms.Screen.FromHandle(
                        new System.Windows.Interop.WindowInteropHelper(this).Handle);

                    // Create window with fullscreen properties from the start (like video windows)
                    _browserPopoutWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Background = System.Windows.Media.Brushes.Black,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = screen.Bounds.X + 100,
                        Top = screen.Bounds.Y + 100,
                        Width = 400,
                        Height = 300,
                        Content = _browser.WebView
                    };

                    _browserPopoutWindow.Closing += (s, args) =>
                    {
                        if (_isBrowserFullscreen)
                        {
                            _isBrowserFullscreen = false;
                            if (_browser != null)
                                _browser.ZoomFactor = _browserPreFullscreenZoom;
                        }
                        if (_browserPopoutWindow != null)
                            _browserPopoutWindow.Content = null;
                    };

                    _browserPopoutWindow.Closed += (s, args) =>
                    {
                        if (_browser?.WebView != null && !SettingsTab.BrowserContainer.Children.Contains(_browser.WebView))
                        {
                            SettingsTab.BrowserContainer.Children.Add(_browser.WebView);
                            SettingsTab.BrowserLoadingText.Visibility = Visibility.Collapsed;
                        }
                        _browserPopoutWindow = null;
                    };

                    // Show small first, pump render queue, then maximize — exactly like video windows
                    _browserPopoutWindow.Show();
                    _browserPopoutWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    _browserPopoutWindow.WindowState = WindowState.Maximized;
                }

                // (Removed: ReRequestVideoFullscreenAsync.) That stacked a
                // second HTML5 fullscreen entry on top of HT's wrapper-level
                // one, and document.exitFullscreen() only pops one stack
                // entry per call — so HT's minimize button and dblclick
                // appeared to do nothing. Letting HT's original wrapper
                // fullscreen ride through the transition gives a single-layer
                // exit that pops cleanly on one exitFullscreen call.

                // Flag the page so the JS click-pair / dblclick handlers
                // (injected in BrowserService) fire even if the page lost
                // HTML5 fullscreen state during the reparent. The user can
                // always exit our WPF "forced fullscreen" by double-clicking
                // the video — same as Esc.
                try { _ = _browser.WebView.CoreWebView2.ExecuteScriptAsync("window._ccpForcedFs = true;"); }
                catch { }

                App.Logger?.Information("Browser entered fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to enter browser fullscreen");
                ExitBrowserFullscreen();
            }
        }

        private void ExitBrowserFullscreen()
        {
            if (!_isBrowserFullscreen) return;

            try
            {
                // Clear the JS flag and best-effort exit any lingering HTML5
                // fullscreen on the page side before we restore window state.
                try
                {
                    if (_browser?.WebView?.CoreWebView2 != null)
                    {
                        _ = _browser.WebView.CoreWebView2.ExecuteScriptAsync(
                            "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
                    }
                }
                catch { }

                if (_browserPopoutWindow != null)
                {
                    if (_browserFullscreenWasPopout)
                    {
                        // === Was already popped out by user — restore popout window state ===
                        _browserPopoutWindow.WindowStyle = _popoutPreFsStyle;
                        _browserPopoutWindow.ResizeMode = _popoutPreFsResize;
                        _browserPopoutWindow.Topmost = _popoutPreFsTopmost;
                        _browserPopoutWindow.Left = _popoutPreFsLeft;
                        _browserPopoutWindow.Top = _popoutPreFsTop;
                        _browserPopoutWindow.Width = _popoutPreFsWidth;
                        _browserPopoutWindow.Height = _popoutPreFsHeight;
                        _browserPopoutWindow.WindowState = _popoutPreFsState;
                    }
                    else
                    {
                        // === Was embedded — close the auto-popout to return to embedded ===
                        _browserPopoutWindow.Close();
                        // The Closed handler returns the WebView to SettingsTab.BrowserContainer
                    }
                }

                // Restore zoom
                if (_browser != null)
                    _browser.ZoomFactor = _browserPreFullscreenZoom;

                _isBrowserFullscreen = false;
                _avatarWasAttachedBeforeBrowserFullscreen = false;

                App.Logger?.Information("Browser exited fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to exit browser fullscreen");
            }
        }

        // True while a remote controller's "play_hypnotube" video is showing in the embedded
        // browser. Gates StopBrowserVideoFromRemote so panic/session-end only touches the
        // browser when the controller actually started a video here — never the page the user
        // was browsing themselves.
        private bool _remoteBrowserVideoActive;

        /// <summary>
        /// Play a controller-supplied HypnoTube URL in the embedded browser (remote-control
        /// "play_hypnotube" command). Marks the browser video as remote-active so a later panic
        /// / session-end can stop it. The URL has already been allowlist-validated by
        /// RemoteControlService (HtUrlHelper.IsEligibleHtUrl).
        /// </summary>
        public void PlayHypnotubeFromRemote(string url)
        {
            _remoteBrowserVideoActive = true;
            // A controller command is an explicit instruction from another person, so it takes
            // precedence over an in-flight video rather than being refused — but it must hand the
            // session over cleanly instead of navigating out from under the previous claim and
            // leaving it stranded.
            App.BrowserMedia?.ReplaceSession(
                Services.Browser.BrowserMediaService.MediaOwner.Remote, takeover: true);
            if (!NavigateToUrlInBrowser(url, autoPlayFullscreen: true))
            {
                // Nothing is playing here, so don't leave the claim standing until the heartbeat
                // retires it — panic/session-end would otherwise act on a video that never loaded.
                _remoteBrowserVideoActive = false;
                App.BrowserMedia?.OnMediaStopped("remote-browser-unavailable");
            }
        }

        /// <summary>
        /// Stop a video a remote controller started in the embedded browser (panic /
        /// session-end / controller-disconnect path). Exits forced fullscreen and navigates
        /// back to the currently-selected site's homepage — this tears down the playing
        /// &lt;video&gt; (halting playback) while leaving the browser on a usable page, rather
        /// than a dead-end about:blank. No-op unless a remote video was actually playing.
        /// </summary>
        public void StopBrowserVideoFromRemote()
        {
            if (!_remoteBrowserVideoActive) return;
            _remoteBrowserVideoActive = false;
            try
            {
                if (_isBrowserFullscreen) ExitBrowserFullscreen();
                NavigateBrowserToCurrentSiteHome();
                App.Logger?.Information("[RemoteControl] Stopped remote browser video, restored site homepage");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StopBrowserVideoFromRemote failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Navigate the embedded browser to the homepage of whichever site (HypnoTube /
        /// BambiCloud) is currently selected in the toggle. Shared by the remote video-stop
        /// path and the toolbar Reload button — re-selecting an already-checked site radio
        /// won't fire its Checked handler, so this gives a reliable way back to a live page.
        /// </summary>
        private void NavigateBrowserToCurrentSiteHome()
        {
            if (_browser?.WebView?.CoreWebView2 == null) return;
            try
            {
                var isBambiCloud = SettingsTab.RbBambiCloud?.IsChecked == true;
                var url = isBambiCloud ? "https://bambicloud.com/" : "https://hypnotube.com/";
                _browser.Navigate(url);
                App.Logger?.Information("Browser navigated to current site home: {Url}", url);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("NavigateBrowserToCurrentSiteHome failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Toolbar Reload button: reload the browser onto the currently-selected site's
        /// homepage (or lazy-init the browser if it was never opened). Gives the user a way
        /// out of a stuck/blank page — e.g. after a remote video was stopped.
        /// </summary>
        internal void BtnReloadBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!_browserInitialized)
            {
                var initialUrl = SettingsTab.RbHypnoTube?.IsChecked == true
                    ? "https://hypnotube.com/"
                    : "https://bambicloud.com/";
                _ = InitializeBrowserAsync(initialUrl);
                return;
            }
            if (App.Settings?.Current?.OfflineMode == true) return;
            NavigateBrowserToCurrentSiteHome();
        }

        #endregion
    }
}
