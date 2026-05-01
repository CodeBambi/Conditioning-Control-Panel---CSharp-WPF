using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Watches a WebView2 for HypnoTube video pages, scrapes the description
    /// for a <c>ccp:&lt;url&gt;</c> reference, fetches the referenced
    /// .ccpenh.json via <see cref="EnhancementFetcher"/>, and binds it to a
    /// <see cref="BrowserVideoTimeSource"/> on the same WebView via
    /// <see cref="EnhancementHostService"/>.
    ///
    /// Lifecycle:
    ///   Attach(webView) → installs a NavigationCompleted listener on the
    ///                     CoreWebView2; at most one engine bound at a time.
    ///   Detach()        → removes the listener, unbinds the engine.
    ///
    /// Per-navigation flow: cancel any in-flight scrape, wait for DOM, scrape,
    /// fetch (cache-aware), validate, bind. Every failure path is silent + logged.
    /// </summary>
    public sealed class BrowserAutoDiscovery : IDisposable
    {
        private static readonly Regex CcpRefRegex = new(
            @"ccp:(?<url>https?://\S+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly EnhancementFetcher _fetcher;
        private readonly EnhancementHostService _host;
        private WebView2? _webView;
        private BrowserVideoTimeSource? _activeSource;
        private CancellationTokenSource? _scrapeCts;
        private string? _activeUrl;
        private bool _disposed;

        /// <summary>Fires (UI thread) when an enhancement gets bound to a page.</summary>
        public event Action<string, Models.Deeper.Enhancement>? Bound;
        /// <summary>Fires (UI thread) when the previously-bound engine unbinds.</summary>
        public event Action? Unbound;

        public bool HasActiveBinding => _activeSource != null;
        public string? ActiveUrl => _activeUrl;

        public BrowserAutoDiscovery(EnhancementFetcher fetcher, EnhancementHostService host)
        {
            _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Attach(WebView2 webView)
        {
            if (_disposed || webView == null) return;
            Detach();
            _webView = webView;
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            else
                _webView.CoreWebView2InitializationCompleted += OnCoreReady;
        }

        public void Detach()
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                if (_webView != null)
                    _webView.CoreWebView2InitializationCompleted -= OnCoreReady;
            }
            catch { }
            _webView = null;
            CancelScrape();
            UnbindActive();
        }

        private void OnCoreReady(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            try { if (_webView?.CoreWebView2 != null) _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted; }
            catch { }
        }

        private async void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;
            var url = _webView?.CoreWebView2?.Source ?? "";
            // Unbind the previous page's engine before we evaluate the new one;
            // bound enhancements are URL-scoped, not tab-scoped.
            UnbindActive();

            if (!IsHypnoTubeVideoPage(url)) return;
            CancelScrape();
            _scrapeCts = new CancellationTokenSource();
            var ct = _scrapeCts.Token;

            try
            {
                // Give the page a beat to settle so the description is in the DOM.
                await Task.Delay(700, ct).ConfigureAwait(true);

                var description = await ScrapeDescriptionAsync(ct).ConfigureAwait(true);
                if (string.IsNullOrEmpty(description)) return;

                var match = CcpRefRegex.Match(description);
                if (!match.Success) return;

                var refUrl = match.Groups["url"].Value.TrimEnd(',', '.', ')', ']');
                App.Logger?.Information("Deeper auto-discovery: found ccp ref {Url} on {Page}", refUrl, url);

                var enh = await _fetcher.FetchAsync(refUrl, ct).ConfigureAwait(true);
                if (enh == null || ct.IsCancellationRequested) return;

                BindActive(url, enh);
            }
            catch (OperationCanceledException) { /* superseded by next nav */ }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deeper auto-discovery error: {Error}", ex.Message);
            }
        }

        private async Task<string> ScrapeDescriptionAsync(CancellationToken ct)
        {
            if (_webView?.CoreWebView2 == null) return "";

            // HT puts the description in a few candidate locations. Sniff in
            // priority order and return the first non-empty hit. Safe even if
            // one selector throws - the JS just returns "".
            const string script = @"
                (function(){
                    try {
                        var sels = ['meta[name=description]', 'meta[property=""og:description""]',
                                    '.video-description', '#video-description',
                                    'div.description', 'div[class*=description]'];
                        var out = [];
                        for (var i = 0; i < sels.length; i++) {
                            var el = document.querySelector(sels[i]);
                            if (!el) continue;
                            var v = el.tagName === 'META' ? el.getAttribute('content') : el.innerText;
                            if (v) out.push(v);
                        }
                        return out.join('\n');
                    } catch (e) { return ''; }
                })();
            ";
            ct.ThrowIfCancellationRequested();
            var raw = await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
            if (string.IsNullOrEmpty(raw) || raw == "null") return "";
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(raw) ?? ""; }
            catch { return raw.Trim('"'); }
        }

        private void BindActive(string pageUrl, Models.Deeper.Enhancement enhancement)
        {
            try
            {
                if (_webView == null) return;
                _host.LoadFromMemory(enhancement, pageUrl);
                _activeSource = new BrowserVideoTimeSource(_webView);
                if (!_host.Bind(_activeSource,
                        attach: () => _activeSource?.Attach(),
                        detach: () =>
                        {
                            _activeSource?.Detach();
                            _activeSource?.Dispose();
                            _activeSource = null;
                        }))
                {
                    _activeSource?.Dispose();
                    _activeSource = null;
                    return;
                }
                _activeUrl = pageUrl;
                try { Bound?.Invoke(pageUrl, enhancement); }
                catch (Exception ex) { App.Logger?.Debug("BrowserAutoDiscovery Bound subscriber error: {Error}", ex.Message); }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserAutoDiscovery.BindActive error: {Error}", ex.Message);
                UnbindActive();
            }
        }

        private void UnbindActive()
        {
            if (_activeSource == null && _activeUrl == null) return;
            try { _host.UnbindEngine(); } catch { }
            try { _host.Unload(); } catch { }
            _activeSource = null;
            _activeUrl = null;
            try { Unbound?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("BrowserAutoDiscovery Unbound subscriber error: {Error}", ex.Message); }
        }

        private void CancelScrape()
        {
            try { _scrapeCts?.Cancel(); _scrapeCts?.Dispose(); } catch { }
            _scrapeCts = null;
        }

        private static bool IsHypnoTubeVideoPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            // HT video pages contain "/video/" in the path; the homepage and
            // category pages don't, so this avoids scraping when there's
            // nothing to bind to.
            return url.Contains("hypnotube.com", StringComparison.OrdinalIgnoreCase)
                && url.Contains("/video/", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }
    }
}
