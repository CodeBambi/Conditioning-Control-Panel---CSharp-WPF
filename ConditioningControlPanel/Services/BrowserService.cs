using System;
using System.IO;
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

        public event EventHandler? BrowserReady;
        public event EventHandler<string>? NavigationCompleted;
        public event EventHandler<string>? TitleChanged;

        public bool IsInitialized => _isInitialized;
        public WebView2? WebView => _webView;

        public BrowserService()
        {
            // Store browser data in app directory
            _userDataFolder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, 
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
            
            // Block popups - handle them internally
            _webView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                // Open in same window instead of popup
                e.Handled = true;
                _webView.CoreWebView2.Navigate(e.Uri);
            };
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
