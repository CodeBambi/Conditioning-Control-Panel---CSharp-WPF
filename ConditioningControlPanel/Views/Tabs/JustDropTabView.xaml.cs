using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ConditioningControlPanel.Services.JustDrop;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Just Drop door: a thin WebView2 host of <see cref="JustDropService.ExpressUrl"/>.
    ///
    /// <para><b>Lazy by construction.</b> The control is instantiated with MainWindow like every
    /// other tab, but the WebView2 - and therefore the second Chromium environment, its processes
    /// and its ~100MB - is not created until <see cref="OnTabShown"/> runs for the first time. A
    /// user who never opens this door never pays for it.</para>
    ///
    /// <para><b>The login lives in the page.</b> The environment's user-data folder is
    /// <c>%LOCALAPPDATA%\ConditioningControlPanel\browser_data_justdrop</c> - persistent and its
    /// own, so the dashboard's session cookie survives an app restart and the user signs in once.
    /// It is deliberately NOT the integrated browser's <c>browser_data</c> folder: one WebView2
    /// user-data folder cannot be opened by two environments at once, and the dashboard would then
    /// fight the Home door's browser for it. A real token handoff is a later phase - see the door's
    /// open questions.</para>
    ///
    /// <para><b>Three states, one visible at a time.</b> The web surface is a native HWND, so
    /// nothing WPF draws can sit on top of it: the loading and error panels are siblings and the
    /// browser box is Collapsed whenever either is showing. That is also why the browser box only
    /// becomes visible once a navigation has actually succeeded - a half-loaded page must never be
    /// the thing the user is looking at behind a spinner they cannot see.</para>
    /// </summary>
    public partial class JustDropTabView : UserControl
    {
        private WebView2? _webView;
        private CoreWebView2Environment? _environment;

        /// <summary>True once <see cref="EnsureWebViewAsync"/> has completed successfully. A
        /// failed attempt leaves this false so the Retry button can try the whole thing again,
        /// including the environment, which is the half that fails when the WebView2 Runtime is
        /// missing.</summary>
        private bool _webViewReady;

        /// <summary>Guards against a second init while the first is still in flight - the door can
        /// be opened, left and re-opened faster than CoreWebView2 comes up.</summary>
        private bool _initializing;

        private bool _messageHookAttached;

        public JustDropTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Per-show hook, called from MainWindow.ShowTab. First call builds the browser; later
        /// calls are a no-op unless the last attempt failed, in which case arriving at the door
        /// again retries - the common case being "the user was offline a minute ago".
        /// </summary>
        internal void OnTabShown()
        {
            if (_webViewReady || _initializing) return;
            _ = EnsureWebViewAsync();
        }

        /// <summary>
        /// Builds the WebView2, adds it to the card and navigates. Every failure lands in
        /// <see cref="ShowError"/> rather than throwing: this is called fire-and-forget from a tab
        /// switch, and an unhandled exception here would surface as a crash on navigation.
        /// </summary>
        private async Task EnsureWebViewAsync()
        {
            if (_initializing) return;
            _initializing = true;
            ShowLoading();

            try
            {
                // Same first check BrowserService makes: without the runtime nothing below can
                // work, and the error is worth naming rather than letting CreateAsync throw.
                var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrEmpty(runtimeVersion))
                    throw new InvalidOperationException("WebView2 Runtime is not installed.");

                if (_environment == null)
                {
                    var userDataFolder = Path.Combine(App.UserDataPath, "browser_data_justdrop");
                    Directory.CreateDirectory(userDataFolder);
                    _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                    App.Logger?.Information("JustDrop: WebView2 environment created in {Path}", userDataFolder);
                }

                if (_webView == null)
                {
                    _webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                    };
                    // Must be in the visual tree before EnsureCoreWebView2Async, or the control
                    // never gets an HWND to host the browser in.
                    WebHost.Children.Add(_webView);
                }

                await _webView.EnsureCoreWebView2Async(_environment);
                var core = _webView.CoreWebView2;
                if (core == null) throw new InvalidOperationException("CoreWebView2 was null after EnsureCoreWebView2Async.");

                if (!_messageHookAttached)
                {
                    core.WebMessageReceived += OnWebMessageReceived;
                    core.NavigationCompleted += OnNavigationCompleted;
                    core.ProcessFailed += OnProcessFailed;
                    // The shop is the whole point of the door; a link out of it belongs in the
                    // user's own browser, not in a chromeless popup with no address bar.
                    core.NewWindowRequested += OnNewWindowRequested;
                    _messageHookAttached = true;
                }

                // Chrome the page has no use for. The site is our own, so this is tidying, not
                // sandboxing.
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                core.Navigate(JustDropService.ExpressUrl);
                App.Logger?.Information("JustDrop: navigating to {Url}", JustDropService.ExpressUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "JustDrop: WebView2 initialisation failed");
                ShowError();
            }
            finally
            {
                _initializing = false;
            }
        }

        // ============================== events ==============================

        /// <summary>
        /// The bridge seam. The parse, the envelope check and everything downstream of it live in
        /// <see cref="JustDropService"/>; this method's whole job is to hand over the raw JSON
        /// without letting a bad message escape as an exception into the WebView2 event.
        /// </summary>
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // WebMessageAsJson, not TryGetWebMessageAsString: the player posts an object, and
                // TryGetWebMessageAsString throws on anything that is not a bare string.
                JustDropService.HandleWebMessage(e.WebMessageAsJson);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "JustDrop: WebMessageReceived failed");
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (e.IsSuccess)
                {
                    _webViewReady = true;
                    ShowWeb();
                    return;
                }

                // A failed navigation is almost always offline or DNS. Keep _webViewReady false so
                // the next visit to the door retries on its own.
                _webViewReady = false;
                App.Logger?.Warning("JustDrop: navigation failed ({Status})", e.WebErrorStatus);
                ShowError();
            }
            catch (Exception ex) { App.Logger?.Debug("JustDrop: NavigationCompleted: {E}", ex.Message); }
        }

        /// <summary>The Chromium render/browser process died. Everything on the control throws
        /// after this, so the browser is torn down and rebuilt from scratch on the next show or
        /// Retry rather than left as a zombie (BrowserService learned this the hard way).
        /// <para>The teardown is deferred to the dispatcher rather than run inline: this fires
        /// FROM the CoreWebView2 whose control we are about to dispose, and disposing an object
        /// while it is raising is how a recoverable crash becomes an unrecoverable one.</para>
        /// </summary>
        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            try
            {
                App.Logger?.Warning("JustDrop: WebView2 process failed ({Kind})", e.ProcessFailedKind);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        DisposeWebView();
                        ShowError();
                    }
                    catch (Exception ex) { App.Logger?.Debug("JustDrop: ProcessFailed teardown: {E}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("JustDrop: ProcessFailed: {E}", ex.Message); }
        }

        /// <summary>Links out of the shop (Discord, Patreon, a share URL) open in the default
        /// browser instead of a WebView2 popup window with no chrome to escape from.</summary>
        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            try
            {
                e.Handled = true;
                var uri = e.Uri;
                if (string.IsNullOrWhiteSpace(uri)) return;
                if (!uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex) { App.Logger?.Debug("JustDrop: NewWindowRequested: {E}", ex.Message); }
        }

        private void BtnJustDropReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_webView?.CoreWebView2 == null) { _ = EnsureWebViewAsync(); return; }
                ShowLoading();
                _webView.CoreWebView2.Navigate(JustDropService.ExpressUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "JustDrop: reload failed");
                ShowError();
            }
        }

        private void BtnJustDropRetry_Click(object sender, RoutedEventArgs e)
        {
            try { _ = EnsureWebViewAsync(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "JustDrop: retry failed"); }
        }

        // ============================== states ==============================

        private void ShowLoading() => SetState(loading: true, error: false, web: false);

        private void ShowError() => SetState(loading: false, error: true, web: false);

        private void ShowWeb() => SetState(loading: false, error: false, web: true);

        /// <summary>Exactly one of the three is visible. The browser box is Collapsed rather than
        /// merely covered because a native HWND cannot be covered - see the class remarks.</summary>
        private void SetState(bool loading, bool error, bool web)
        {
            try
            {
                LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                ErrorPanel.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
                WebHost.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("JustDrop: SetState: {E}", ex.Message); }
        }

        private void DisposeWebView()
        {
            try
            {
                if (_webView != null)
                {
                    var core = _webView.CoreWebView2;
                    if (core != null && _messageHookAttached)
                    {
                        core.WebMessageReceived -= OnWebMessageReceived;
                        core.NavigationCompleted -= OnNavigationCompleted;
                        core.ProcessFailed -= OnProcessFailed;
                        core.NewWindowRequested -= OnNewWindowRequested;
                    }
                    WebHost.Children.Remove(_webView);
                    _webView.Dispose();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("JustDrop: DisposeWebView: {E}", ex.Message); }
            finally
            {
                _webView = null;
                _messageHookAttached = false;
                _webViewReady = false;
                // The environment is kept: it is bound to the user-data folder, it is expensive to
                // rebuild, and it is not what died.
            }
        }
    }
}
