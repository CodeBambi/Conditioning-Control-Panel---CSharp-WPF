using System;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Watches the dashboard browser. When navigating to a URL we have a saved
    /// .ccpenh.json for in the local library, binds the EnhancementEngine to a
    /// BrowserVideoTimeSource on that WebView2 so effects fire in sync with the
    /// embedded video.
    ///
    /// Owns its own EnhancementHostService instance so it can't conflict with
    /// the standalone Player or the editor's preview path.
    /// </summary>
    public sealed class BrowserEnhancementBridge : IDisposable
    {
        private readonly WebView2 _webView;
        private readonly BrowserService _browser;
        private readonly EnhancementHostService _host = new();

        private BrowserVideoTimeSource? _source;
        private string? _currentBoundFilePath;
        private string? _lastUrl;

        /// <summary>Fires whenever the matched enhancement changes (null = no match / disabled).</summary>
        public event Action<EnhancementLibraryEntry?>? MatchChanged;

        public BrowserEnhancementBridge(WebView2 webView, BrowserService browser)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _browser = browser ?? throw new ArgumentNullException(nameof(browser));
            _browser.NavigationCompleted += OnNavigationCompleted;

            // Backfill: BrowserReady (where this bridge gets constructed) fires
            // after BrowserService has already wired its own NavigationCompleted
            // and issued the first Navigate. If that first page has finished
            // loading before our subscription above runs, _lastUrl stays null
            // and the user sees no match until they bounce navigation. Read the
            // current Source once so we evaluate whatever's already on screen.
            try
            {
                var current = _webView.CoreWebView2?.Source;
                if (!string.IsNullOrEmpty(current))
                {
                    _lastUrl = current;
                    Evaluate(current);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserEnhancementBridge backfill failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Re-evaluate the current URL against the library + the toggle setting.
        /// Call after the user toggles the setting on/off.
        /// </summary>
        public void Refresh() => Evaluate(_lastUrl);

        private void OnNavigationCompleted(object? sender, string url)
        {
            _lastUrl = url;
            Evaluate(url);
        }

        private void Evaluate(string? url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    Unbind();
                    MatchChanged?.Invoke(null);
                    return;
                }

                var enabled = App.Settings?.Current?.BrowserEnhanceIfPossible ?? false;
                if (!enabled)
                {
                    Unbind();
                    MatchChanged?.Invoke(null);
                    return;
                }

                var match = App.EnhancementLibrary?.FindMatch(url, MediaTypes.Video);
                if (match == null)
                {
                    LogNoMatch(url);
                    Unbind();
                    MatchChanged?.Invoke(null);
                    return;
                }

                if (string.Equals(_currentBoundFilePath, match.FilePath, StringComparison.OrdinalIgnoreCase)
                    && _host.IsRunning)
                {
                    // Already running this exact enhancement — leave it alone.
                    MatchChanged?.Invoke(match);
                    return;
                }

                Unbind();
                if (!_host.LoadFromFile(match.FilePath))
                {
                    MatchChanged?.Invoke(null);
                    return;
                }

                _source = new BrowserVideoTimeSource(_webView);
                var src = _source;
                _host.Bind(src,
                    attach: () => src?.Attach(),
                    detach: () => { src?.Detach(); });

                _currentBoundFilePath = match.FilePath;
                MatchChanged?.Invoke(match);
                App.Logger?.Information("BrowserEnhancementBridge: bound {Name} for {Url}", match.Name, UrlSafety.RedactUrl(url));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BrowserEnhancementBridge.Evaluate failed");
                Unbind();
                MatchChanged?.Invoke(null);
            }
        }

        // Logs one Information line when navigation lands on a known preview
        // host (hypnotube.com etc) but no library entry matched. Helps the user
        // (and us) debug "I saved an enhancement but it didn't fire" without
        // adding noise on every general browsing page.
        private static void LogNoMatch(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                var host = uri.Host.ToLowerInvariant();
                bool allowlisted = false;
                foreach (var h in DeeperConfig.PreviewHostAllowlist)
                {
                    if (host == h || host.EndsWith("." + h, StringComparison.Ordinal))
                    {
                        allowlisted = true;
                        break;
                    }
                }
                if (!allowlisted) return;

                var entries = App.EnhancementLibrary?.ScanLibrary();
                var count = entries?.Count ?? 0;
                var sample = "";
                if (entries != null && count > 0)
                {
                    var top = entries.Take(3).Select(e => e.MediaSource ?? "(none)");
                    sample = " | sample media_source: " + string.Join("  ;  ", top);
                }
                App.Logger?.Information(
                    "BrowserEnhancementBridge: no match for {Url} ({Count} library entries){Sample}",
                    UrlSafety.RedactUrl(url), count, sample);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserEnhancementBridge.LogNoMatch failed: {Error}", ex.Message);
            }
        }

        private void Unbind()
        {
            try { _host.UnbindEngine(); } catch { }
            try { _host.Unload(); } catch { }
            try { _source?.Dispose(); } catch { }
            _source = null;
            _currentBoundFilePath = null;
        }

        public void Dispose()
        {
            try { _browser.NavigationCompleted -= OnNavigationCompleted; } catch { }
            Unbind();
            try { _host.Dispose(); } catch { }
        }
    }
}
