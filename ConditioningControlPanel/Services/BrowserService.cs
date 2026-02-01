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
        /// Inject audio sync monitoring script that reports video playback state.
        /// This script intercepts play events and pauses video until haptic processing is ready.
        /// Call this after navigating to a video page.
        /// </summary>
        public async Task InjectAudioSyncScriptAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                var script = @"
                    (function() {
                        if (window.__hapticSyncInjected) return;
                        window.__hapticSyncInjected = true;
                        window.__hapticReady = false;
                        window.__hapticVideoReported = false;
                        window.__hapticProcessing = false;
                        window.__hapticOverlay = null;

                        console.log('[HapticSync] Script injected');

                        // Create processing overlay - supports both normal and fullscreen modes
                        const createOverlay = () => {
                            if (window.__hapticOverlay) return window.__hapticOverlay;

                            const overlay = document.createElement('div');
                            overlay.id = 'hapticSyncOverlay';
                            overlay.style.cssText = `
                                position: fixed;
                                top: 0; left: 0; right: 0; bottom: 0;
                                background: rgba(0, 0, 0, 0.85);
                                display: flex;
                                flex-direction: column;
                                justify-content: center;
                                align-items: center;
                                z-index: 2147483647;
                                color: white;
                                font-family: 'Segoe UI', sans-serif;
                            `;
                            overlay.innerHTML = `
                                <div style='font-size: 24px; margin-bottom: 20px; color: #FF69B4;'>Preparing Haptic Sync...</div>
                                <div style='width: 200px; height: 4px; background: #333; border-radius: 2px; overflow: hidden;'>
                                    <div id='hapticProgress' style='width: 0%; height: 100%; background: linear-gradient(90deg, #FF69B4, #FF1493); transition: width 0.3s;'></div>
                                </div>
                                <div id='hapticStatus' style='margin-top: 15px; font-size: 14px; color: #aaa;'>Analyzing audio...</div>
                            `;

                            // For fullscreen support, try to attach near the video element
                            const video = document.querySelector('video');
                            const fullscreenEl = document.fullscreenElement || document.webkitFullscreenElement;
                            if (fullscreenEl) {
                                // Attach to fullscreen container for visibility
                                fullscreenEl.appendChild(overlay);
                            } else if (video && video.parentElement) {
                                // Attach to video's parent so overlay works when video goes fullscreen
                                video.parentElement.style.position = 'relative';
                                video.parentElement.appendChild(overlay);
                            } else {
                                document.body.appendChild(overlay);
                            }
                            window.__hapticOverlay = overlay;
                            return overlay;
                        };

                        const showOverlay = () => {
                            const overlay = createOverlay();
                            overlay.style.display = 'flex';
                        };

                        const hideOverlay = () => {
                            if (window.__hapticOverlay) {
                                window.__hapticOverlay.style.display = 'none';
                            }
                        };

                        const updateProgress = (percent, status) => {
                            const progress = document.getElementById('hapticProgress');
                            const statusEl = document.getElementById('hapticStatus');
                            if (progress) progress.style.width = percent + '%';
                            if (statusEl) statusEl.textContent = status;
                        };

                        // Expose progress update for C# to call
                        window.__hapticUpdateProgress = updateProgress;

                        // Find video element
                        const findVideo = () => document.querySelector('video');

                        // Get best available video URL
                        const getVideoUrl = (video) => {
                            if (video.currentSrc && video.currentSrc.startsWith('http')) return video.currentSrc;
                            if (video.src && video.src.startsWith('http')) return video.src;
                            const source = video.querySelector('source');
                            if (source && source.src && source.src.startsWith('http')) return source.src;
                            return null;
                        };

                        // Report playback state continuously
                        let syncInterval = null;
                        const startSync = () => {
                            if (syncInterval) return;
                            console.log('[HapticSync] Starting sync interval');
                            syncInterval = setInterval(() => {
                                const video = findVideo();
                                if (video && window.__hapticReady && !video.paused) {
                                    window.chrome.webview.postMessage(JSON.stringify({
                                        type: 'audioSyncState',
                                        currentTime: video.currentTime,
                                        paused: video.paused,
                                        duration: video.duration || 0
                                    }));
                                }
                            }, 50);
                        };

                        const stopSync = () => {
                            if (syncInterval) {
                                clearInterval(syncInterval);
                                syncInterval = null;
                            }
                        };

                        // Setup video interception
                        const setupVideoInterception = (video) => {
                            if (!video || video.__hapticIntercepted) return;
                            video.__hapticIntercepted = true;
                            console.log('[HapticSync] Setting up video interception');

                            // Store original play function
                            const originalPlay = video.play.bind(video);

                            // Intercept play
                            video.play = function() {
                                console.log('[HapticSync] Play intercepted, ready=' + window.__hapticReady + ', processing=' + window.__hapticProcessing);

                                if (window.__hapticReady) {
                                    // Haptics ready, allow play
                                    console.log('[HapticSync] Haptics ready, allowing play');
                                    startSync();
                                    return originalPlay();
                                }

                                if (!window.__hapticProcessing && !window.__hapticVideoReported) {
                                    // First play attempt - start processing
                                    window.__hapticProcessing = true;
                                    window.__hapticVideoReported = true;

                                    const url = getVideoUrl(video);
                                    if (url) {
                                        console.log('[HapticSync] Intercepting play, starting processing for: ' + url);
                                        showOverlay();

                                        // Report video URL to C#
                                        window.chrome.webview.postMessage(JSON.stringify({
                                            type: 'audioSyncVideoDetected',
                                            url: url,
                                            duration: video.duration || 0
                                        }));
                                    }
                                }

                                // Return a promise that resolves when ready
                                return Promise.resolve();
                            };

                            // Setup other event listeners
                            video.addEventListener('pause', () => {
                                console.log('[HapticSync] Video pause event');
                                if (window.__hapticReady) {
                                    window.chrome.webview.postMessage(JSON.stringify({
                                        type: 'audioSyncState',
                                        currentTime: video.currentTime,
                                        paused: true
                                    }));
                                }
                            });

                            video.addEventListener('seeked', () => {
                                console.log('[HapticSync] Video seeked to ' + video.currentTime);
                                if (window.__hapticReady) {
                                    window.chrome.webview.postMessage(JSON.stringify({
                                        type: 'audioSyncSeek',
                                        currentTime: video.currentTime
                                    }));
                                }
                            });

                            video.addEventListener('ended', () => {
                                console.log('[HapticSync] Video ended');
                                stopSync();
                                window.chrome.webview.postMessage(JSON.stringify({
                                    type: 'audioSyncEnded'
                                }));
                            });
                        };

                        // Function to signal haptics ready and resume video
                        window.__hapticSignalReady = () => {
                            console.log('[HapticSync] Ready signal received');
                            window.__hapticReady = true;
                            window.__hapticProcessing = false;
                            hideOverlay();

                            // Auto-play the video
                            const video = findVideo();
                            if (video) {
                                console.log('[HapticSync] Starting video playback');
                                const originalPlay = HTMLMediaElement.prototype.play.bind(video);
                                startSync();
                                originalPlay().catch(e => console.log('[HapticSync] Play failed:', e));
                            }
                        };

                        // Setup on existing video
                        const video = findVideo();
                        if (video) {
                            setupVideoInterception(video);
                        }

                        // Watch for dynamically added videos
                        const observer = new MutationObserver(() => {
                            const v = findVideo();
                            if (v) setupVideoInterception(v);
                        });
                        observer.observe(document.body, { childList: true, subtree: true });

                        console.log('[HapticSync] Script fully initialized with play interception');
                    })();
                ";

                await _webView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Debug("Audio sync monitoring script injected");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to inject audio sync script: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Signal to the browser that haptic processing is ready and video can play
        /// </summary>
        public async Task SignalHapticReadyAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                // Call the ready function which hides overlay and starts playback
                var script = @"
                    if (window.__hapticSignalReady) {
                        window.__hapticSignalReady();
                    } else {
                        window.__hapticReady = true;
                        console.log('[HapticSync] Haptic ready (fallback)');
                    }
                ";
                await _webView.CoreWebView2.ExecuteScriptAsync(script);
                App.Logger?.Information("Signaled haptic ready to browser");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to signal haptic ready: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Update the haptic processing overlay with progress
        /// </summary>
        public async Task UpdateHapticProgressAsync(int percent, string status)
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                var escapedStatus = status.Replace("'", "\\'");
                var script = $"if (window.__hapticUpdateProgress) window.__hapticUpdateProgress({percent}, '{escapedStatus}');";
                await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to update haptic progress: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Pause video playback in browser
        /// </summary>
        public async Task PauseVideoAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('video')?.pause();");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to pause video: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Resume video playback in browser
        /// </summary>
        public async Task PlayVideoAsync()
        {
            if (_webView?.CoreWebView2 == null) return;

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('video')?.play();");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play video: {Error}", ex.Message);
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
