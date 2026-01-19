using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages an embedded browser using WebView2.
    /// Much cleaner than the Python Win32 approach.
    /// </summary>
    public class BrowserService : IDisposable
    {
        private WebView2? _webView;
        private bool _isInitialized;
        private bool _disposed;

        private readonly string _userDataFolder;
        private readonly string _defaultUrl;

        /// <summary>
        /// Known ad/tracking domains to block. This is a basic list -
        /// covers common ad networks and trackers.
        /// </summary>
        private static readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            // Major ad networks
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "google-analytics.com", "googletagmanager.com", "googletagservices.com",
            "adservice.google.com", "pagead2.googlesyndication.com",
            "adsense.google.com", "adnxs.com", "adsrvr.org",

            // Common ad/tracking domains
            "facebook.net", "fbcdn.net", "connect.facebook.net",
            "ads.twitter.com", "analytics.twitter.com",
            "advertising.com", "adform.net", "adroll.com",
            "criteo.com", "criteo.net", "outbrain.com", "taboola.com",
            "amazon-adsystem.com", "aax.amazon.com",
            "moatads.com", "adsafeprotected.com", "doubleverify.com",

            // Tracking pixels and analytics
            "quantserve.com", "scorecardresearch.com", "imrworldwide.com",
            "mixpanel.com", "segment.io", "segment.com", "amplitude.com",
            "hotjar.com", "fullstory.com", "mouseflow.com", "crazyegg.com",

            // Pop-up/pop-under networks
            "popads.net", "popcash.net", "propellerads.com", "exoclick.com",
            "trafficjunky.com", "trafficfactory.biz", "juicyads.com",
            "plugrush.com", "clickadu.com", "adsterra.com",
            "hilltopads.net", "pushame.com", "pushnami.com",

            // Adult ad networks (common on hypnotube etc)
            "exosrv.com", "realsrv.com", "tsyndicate.com", "syndication.exoclick.com",
            "a.realsrv.com", "syndication.realsrv.com", "mc.yandex.ru",
            "static.exoclick.com", "ads.exoclick.com",
            "ero-advertising.com", "eroads.com", "traffichaus.com",
            "awempire.com", "aweptjmp.com", "contentabc.com",

            // Malware/sketchy domains
            "malware-site.com", "adexchangegate.com", "adexchangetracker.com",

            // More tracking
            "newrelic.com", "nr-data.net", "onetrust.com",
            "cookielaw.org", "trustarc.com", "evidon.com",
            "bounceexchange.com", "bouncex.net"
        };

        public event EventHandler? BrowserReady;
        public event EventHandler<string>? NavigationCompleted;
        public event EventHandler<string>? TitleChanged;
        public event EventHandler<bool>? FullscreenChanged;

        public bool IsInitialized => _isInitialized;
        public bool IsFullscreen { get; private set; }
        public WebView2? WebView => _webView;

        /// <summary>
        /// Gets or sets the browser zoom factor (1.0 = 100%)
        /// </summary>
        public double ZoomFactor
        {
            get => _webView?.ZoomFactor ?? 1.0;
            set { if (_webView != null) _webView.ZoomFactor = value; }
        }

        public BrowserService()
        {
            // Store browser data in AppData (not install folder) to avoid lock issues during updates/uninstall
            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel",
                "browser_data"
            );
            Directory.CreateDirectory(_userDataFolder);
            
            // Default URL - Bambi Cloud
            _defaultUrl = "https://bambicloud.com/";
        }

        /// <summary>
        /// Creates and initializes the WebView2 control.
        /// Call this and add the returned control to your UI.
        /// </summary>
        public async Task<WebView2?> CreateBrowserAsync(string? initialUrl = null)
        {
            try
            {
                // First check if WebView2 is available
                string? webView2Version = null;
                try
                {
                    webView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    App.Logger?.Information("WebView2 version available: {Version}", webView2Version);
                }
                catch (Exception versionEx)
                {
                    App.Logger?.Error("WebView2 not available: {Error}", versionEx.Message);
                    throw new InvalidOperationException($"WebView2 Runtime is not installed. Please install it from: go.microsoft.com/fwlink/p/?LinkId=2124703\n\nError: {versionEx.Message}", versionEx);
                }

                if (string.IsNullOrEmpty(webView2Version))
                {
                    throw new InvalidOperationException("WebView2 Runtime is not installed. Please install it from: go.microsoft.com/fwlink/p/?LinkId=2124703");
                }

                _webView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // Store the URL to navigate to after initialization
                _pendingUrl = initialUrl ?? _defaultUrl;
                
                // Create environment
                App.Logger?.Information("Creating WebView2 environment in: {Path}", _userDataFolder);
                _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                App.Logger?.Information("WebView2 environment created successfully");

                // Hook into the Loaded event - EnsureCoreWebView2Async must be called AFTER the control is in the visual tree
                _webView.Loaded += WebView_Loaded;
                
                App.Logger?.Information("WebView2 control created, waiting for it to be added to visual tree...");
                return _webView;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to create browser: {Type} - {Error}\n{StackTrace}", ex.GetType().Name, ex.Message, ex.StackTrace);
                throw;
            }
        }

        private CoreWebView2Environment? _environment;
        private string? _pendingUrl;

        private async void WebView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_webView == null || _environment == null) return;
            
            // Unhook to prevent multiple calls
            _webView.Loaded -= WebView_Loaded;
            
            try
            {
                App.Logger?.Information("WebView2 control loaded, ensuring CoreWebView2...");
                await _webView.EnsureCoreWebView2Async(_environment);
                App.Logger?.Information("CoreWebView2 ensured successfully");

                if (_webView.CoreWebView2 == null)
                {
                    App.Logger?.Error("CoreWebView2 is null after EnsureCoreWebView2Async");
                    return;
                }

                // Configure settings
                ConfigureBrowser();

                // Set default zoom to 75%
                _webView.ZoomFactor = 0.75;

                // Wire up events
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _webView.CoreWebView2.DocumentTitleChanged += OnTitleChanged;

                // Navigate to URL
                var url = _pendingUrl ?? _defaultUrl;
                App.Logger?.Information("Navigating to: {Url}", url);
                _webView.CoreWebView2.Navigate(url);

                _isInitialized = true;
                BrowserReady?.Invoke(this, EventArgs.Empty);
                
                App.Logger?.Information("Browser fully initialized with 75% zoom");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to initialize CoreWebView2: {Type} - {Error}", ex.GetType().Name, ex.Message);
            }
        }

        /// <summary>
        /// Configure browser settings for embedded use
        /// </summary>
        private void ConfigureBrowser()
        {
            if (_webView?.CoreWebView2?.Settings == null) return;

            var settings = _webView.CoreWebView2.Settings;

            // Enable/disable features as needed
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = false; // Disable dev tools for end users
            settings.IsZoomControlEnabled = true;
            settings.AreHostObjectsAllowed = true;
            settings.IsWebMessageEnabled = true;
            settings.AreBrowserAcceleratorKeysEnabled = true;

            // Set up ad blocking
            SetupAdBlocking();

            // Block popups - handle them internally
            _webView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                // Check if it's an ad popup (no user gesture or suspicious URL)
                if (IsAdUrl(e.Uri))
                {
                    App.Logger?.Debug("Blocked ad popup: {Url}", e.Uri);
                    e.Handled = true;
                    return;
                }

                // Open in same window instead of popup
                e.Handled = true;
                _webView.CoreWebView2.Navigate(e.Uri);
            };

            // Handle fullscreen requests (e.g., video fullscreen)
            _webView.CoreWebView2.ContainsFullScreenElementChanged += (s, e) =>
            {
                IsFullscreen = _webView.CoreWebView2.ContainsFullScreenElement;
                App.Logger?.Information("Browser fullscreen changed: {IsFullscreen}", IsFullscreen);
                FullscreenChanged?.Invoke(this, IsFullscreen);
            };
        }

        /// <summary>
        /// Set up ad blocking using request filtering
        /// </summary>
        private void SetupAdBlocking()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                // Filter all requests so we can block ads
                _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

                _webView.CoreWebView2.WebResourceRequested += (s, e) =>
                {
                    try
                    {
                        var uri = new Uri(e.Request.Uri);
                        var host = uri.Host.ToLowerInvariant();

                        // Check if this is an ad domain
                        if (IsBlockedDomain(host))
                        {
                            // Block by returning empty response
                            e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                null, 204, "No Content", "");
                            App.Logger?.Debug("Blocked ad request: {Host}", host);
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors
                    }
                };

                // Inject CSS to hide common ad elements after page loads
                _webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        await InjectAdBlockingCssAsync();
                    }
                };

                App.Logger?.Information("Ad blocking enabled - blocking {Count} known ad domains", _blockedDomains.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to set up ad blocking");
            }
        }

        /// <summary>
        /// Check if a host matches any blocked domain
        /// </summary>
        private static bool IsBlockedDomain(string host)
        {
            // Direct match
            if (_blockedDomains.Contains(host))
                return true;

            // Check if it's a subdomain of a blocked domain
            foreach (var blocked in _blockedDomains)
            {
                if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a URL looks like an ad
        /// </summary>
        private static bool IsAdUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                if (IsBlockedDomain(uri.Host.ToLowerInvariant()))
                    return true;

                // Check for common ad URL patterns
                var lowerUrl = url.ToLowerInvariant();
                return lowerUrl.Contains("/ads/") ||
                       lowerUrl.Contains("/ad/") ||
                       lowerUrl.Contains("doubleclick") ||
                       lowerUrl.Contains("googlesyndication") ||
                       lowerUrl.Contains("/popup") ||
                       lowerUrl.Contains("clicktrack") ||
                       lowerUrl.Contains("adserver");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Inject CSS to hide common ad elements
        /// </summary>
        private async Task InjectAdBlockingCssAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                // CSS to hide common ad elements
                var css = @"
                    /* Hide common ad containers */
                    [class*='ad-'], [class*='ads-'], [class*='advert'],
                    [id*='ad-'], [id*='ads-'], [id*='advert'],
                    [class*='banner'], [id*='banner'],
                    [class*='sponsor'], [id*='sponsor'],
                    .advertisement, .ad-container, .ad-wrapper,
                    iframe[src*='doubleclick'], iframe[src*='googlesyndication'],
                    iframe[src*='ads'], iframe[src*='adserver'],
                    [data-ad], [data-ads], [data-ad-slot],
                    div[aria-label*='Advertisement'],
                    /* Popup overlays */
                    .popup-ad, .pop-up, .modal-ad,
                    /* Specific to adult sites */
                    .exo-ad, .exoclick, .trafficjunky,
                    [class*='exo_'], [id*='exo_']
                    {
                        display: none !important;
                        visibility: hidden !important;
                        height: 0 !important;
                        width: 0 !important;
                        opacity: 0 !important;
                        pointer-events: none !important;
                    }
                ";

                var script = $@"
                    (function() {{
                        var style = document.createElement('style');
                        style.textContent = `{css.Replace("`", "\\`")}`;
                        document.head.appendChild(style);
                    }})();
                ";

                await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to inject ad-blocking CSS: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Navigate to a URL (only HTTPS allowed for security)
        /// </summary>
        public void Navigate(string url)
        {
            if (!_isInitialized || _webView?.CoreWebView2 == null) return;

            try
            {
                // Security: Sanitize and validate URL
                url = url?.Trim() ?? "";

                // Block dangerous URL schemes
                var lowerUrl = url.ToLowerInvariant();
                if (lowerUrl.StartsWith("javascript:") ||
                    lowerUrl.StartsWith("file:") ||
                    lowerUrl.StartsWith("data:") ||
                    lowerUrl.StartsWith("vbscript:"))
                {
                    App.Logger?.Warning("Blocked potentially dangerous URL scheme: {Url}", url);
                    return;
                }

                // Force HTTPS for security
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                // Upgrade HTTP to HTTPS
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url.Substring(7);
                    App.Logger?.Debug("Upgraded HTTP to HTTPS: {Url}", url);
                }

                // Validate URL format
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps))
                {
                    App.Logger?.Warning("Invalid URL rejected: {Url}", url);
                    return;
                }

                _webView.CoreWebView2.Navigate(url);
                App.Logger?.Debug("Navigating to: {Url}", url);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Navigation failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Navigate back
        /// </summary>
        public void GoBack()
        {
            if (_webView?.CoreWebView2?.CanGoBack == true)
                _webView.CoreWebView2.GoBack();
        }

        /// <summary>
        /// Navigate forward
        /// </summary>
        public void GoForward()
        {
            if (_webView?.CoreWebView2?.CanGoForward == true)
                _webView.CoreWebView2.GoForward();
        }

        /// <summary>
        /// Refresh current page
        /// </summary>
        public void Refresh()
        {
            _webView?.CoreWebView2?.Reload();
        }

        /// <summary>
        /// Go to home page
        /// </summary>
        public void GoHome()
        {
            var url = App.Settings?.Current?.BambiCloudUrl ?? _defaultUrl;
            Navigate(url);
        }

        /// <summary>
        /// Get current URL
        /// </summary>
        public string? GetCurrentUrl()
        {
            return _webView?.CoreWebView2?.Source;
        }

        /// <summary>
        /// Get current page title
        /// </summary>
        public string? GetTitle()
        {
            return _webView?.CoreWebView2?.DocumentTitle;
        }

        /// <summary>
        /// Execute JavaScript on the page
        /// </summary>
        public async Task<string?> ExecuteScriptAsync(string script)
        {
            if (_webView?.CoreWebView2 == null) return null;

            try
            {
                return await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Script execution failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extract the currently playing video URL from the page.
        /// Works with Hypnotube and similar sites that use HTML5 video.
        /// </summary>
        public async Task<string?> GetCurrentVideoUrlAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;

            try
            {
                // JavaScript to find the currently playing video's source URL
                // Tries multiple approaches: video src, source elements, data attributes
                var script = @"
                    (function() {
                        // Find video elements
                        var videos = document.querySelectorAll('video');
                        for (var v of videos) {
                            // Check if video is playing or in fullscreen
                            if (!v.paused || v === document.fullscreenElement || v.closest('.video-player')) {
                                // Try direct src
                                if (v.src && v.src.startsWith('http')) {
                                    return v.src;
                                }
                                // Try source elements
                                var source = v.querySelector('source');
                                if (source && source.src && source.src.startsWith('http')) {
                                    return source.src;
                                }
                                // Try currentSrc
                                if (v.currentSrc && v.currentSrc.startsWith('http')) {
                                    return v.currentSrc;
                                }
                            }
                        }
                        // Fallback: find any video with a source
                        for (var v of videos) {
                            if (v.currentSrc && v.currentSrc.startsWith('http')) {
                                return v.currentSrc;
                            }
                            if (v.src && v.src.startsWith('http')) {
                                return v.src;
                            }
                        }
                        return null;
                    })();
                ";

                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);

                // Result comes back as JSON string (with quotes) or "null"
                if (string.IsNullOrEmpty(result) || result == "null")
                    return null;

                // Remove surrounding quotes from JSON string result
                var url = result.Trim('"');

                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    App.Logger?.Information("Extracted video URL: {Url}", url);
                    return url;
                }

                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to extract video URL: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Exit fullscreen mode in the browser
        /// </summary>
        public async Task ExitFullscreenAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("document.exitFullscreen()");
                App.Logger?.Debug("Exited browser fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to exit fullscreen: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Clear browser cache and cookies
        /// </summary>
        public async Task ClearBrowsingDataAsync()
        {
            if (_webView?.CoreWebView2 == null) return;
            
            try
            {
                await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                App.Logger?.Information("Browser data cleared");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to clear browser data: {Error}", ex.Message);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var url = _webView?.CoreWebView2?.Source ?? "";
            NavigationCompleted?.Invoke(this, url);
        }

        private void OnTitleChanged(object? sender, object e)
        {
            var title = _webView?.CoreWebView2?.DocumentTitle ?? "";
            TitleChanged?.Invoke(this, title);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                    _webView.CoreWebView2.DocumentTitleChanged -= OnTitleChanged;
                }
                
                _webView?.Dispose();
                _webView = null;
                _isInitialized = false;
                
                App.Logger?.Debug("Browser disposed");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Error disposing browser: {Error}", ex.Message);
            }
        }
    }
}
