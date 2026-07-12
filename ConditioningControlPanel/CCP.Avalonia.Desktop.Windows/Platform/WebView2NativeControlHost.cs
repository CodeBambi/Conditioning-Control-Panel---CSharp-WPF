using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Platform;

/// <summary>
/// Avalonia <see cref="NativeControlHost"/> that embeds a WebView2 WinForms control.
/// The Win32 HWND owned by a WinForms <see cref="Panel"/> is reparented into the
/// Avalonia window by <see cref="NativeControlHost"/>; WebView2 fills the panel.
/// </summary>
/// <remarks>
/// The underlying WinForms panel and WebView2 are created once and reused when this
/// control is reparented (e.g. into an HTML5 fullscreen window). They are only disposed
/// when <see cref="Dispose"/> is called.
/// </remarks>
public sealed class WebView2NativeControlHost : NativeControlHost, IDisposable
{
    private readonly CoreWebView2Environment? _environment;
    private System.Windows.Forms.Panel? _panel;
    private WebView2? _webView;
    private Task? _initTask;

    /// <summary>
    /// Known ad/tracking domains to block (WPF BrowserService parity). Subdomain matches are
    /// handled by <see cref="IsBlockedDomain"/>.
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

    /// <summary>
    /// Partner sites where ad blocking is fully bypassed (and their subdomains), so we do not
    /// suppress their ad revenue inside the embedded browser. WPF BrowserService parity.
    /// </summary>
    private static readonly HashSet<string> _partnerSites = new(StringComparer.OrdinalIgnoreCase)
    {
        "hypnotube.com",
    };

    public WebView2NativeControlHost(CoreWebView2Environment? environment = null)
    {
        _environment = environment;
    }

    /// <summary>
    /// The underlying WebView2 control, or null before <see cref="CreateNativeControlCore"/> is called.
    /// </summary>
    public WebView2? WebView => _webView;

    /// <summary>
    /// Raised when the browser document title changes.
    /// </summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>
    /// Raised when the browser finishes navigating to a new URI.
    /// </summary>
    public event EventHandler<Uri>? Navigated;

    /// <summary>
    /// Raised when a video or other element enters/exits fullscreen.
    /// </summary>
    public event EventHandler<bool>? FullscreenChanged;

    /// <summary>
    /// Raised after the Chromium browser/render process crashed and this host reset itself
    /// so the next <see cref="EnsureInitializedAsync"/> lazily recreates the WebView2
    /// (WPF BrowserService.BrowserProcessFailed parity).
    /// </summary>
    public event EventHandler? ProcessFailed;

    /// <summary>
    /// Ensures the WebView2 core is initialized with the supplied environment.
    /// Safe to call multiple times; subsequent calls return the same task.
    /// </summary>
    public async Task EnsureInitializedAsync(CoreWebView2Environment environment)
    {
        if (_initTask != null)
        {
            await _initTask;
            return;
        }

        _initTask = InitializeCoreAsync(environment);
        await _initTask;
    }

    private async Task InitializeCoreAsync(CoreWebView2Environment environment)
    {
        EnsurePanelAndWebViewCreated();

        if (_webView?.CoreWebView2 != null)
            return;

        try
        {
            await _webView!.EnsureCoreWebView2Async(environment);
            WireEvents(_webView.CoreWebView2);
            await ConfigureCoreAsync(_webView.CoreWebView2);
        }
        catch (Exception)
        {
            // Initialization failures are left to consumers to surface; the control
            // remains usable as a placeholder so Avalonia layout does not break.
            throw;
        }
    }

    /// <summary>
    /// Applies WPF-parity hardening + ad blocking + the forced-fullscreen exit script to a
    /// freshly-initialized CoreWebView2.
    /// </summary>
    private async Task ConfigureCoreAsync(CoreWebView2? core)
    {
        if (core == null) return;

        // B12: reduce the embedded browser's attack surface (WPF ConfigureBrowser).
        try
        {
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
        }
        catch { /* older runtimes may not expose every setting */ }

        // B3: ad/tracker/popup request blocking.
        SetupAdBlocking(core);

        // B8: WPF forced-fullscreen dblclick-exit detector.
        try
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ForcedFullscreenExitScript);
        }
        catch { /* script injection is best-effort */ }
    }

    /// <summary>
    /// B3: block known ad/tracker domains via WebResourceRequested, with subdomain matching and
    /// a partner-site (hypnotube) full bypass. WPF BrowserService.SetupAdBlocking parity.
    /// </summary>
    private void SetupAdBlocking(CoreWebView2 core)
    {
        try
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                try
                {
                    // Partner pages get full pass-through so we do not suppress their ad revenue.
                    if (IsOnPartnerSite(core)) return;

                    var host = new Uri(e.Request.Uri).Host.ToLowerInvariant();
                    if (IsBlockedDomain(host))
                    {
                        e.Response = core.Environment.CreateWebResourceResponse(null, 204, "No Content", string.Empty);
                    }
                }
                catch { /* ignore parsing errors */ }
            };
        }
        catch { /* ad blocking is best-effort */ }
    }

    private static bool IsBlockedDomain(string host)
    {
        if (_blockedDomains.Contains(host))
            return true;

        foreach (var blocked in _blockedDomains)
        {
            if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsPartnerSite(string host)
    {
        if (_partnerSites.Contains(host))
            return true;

        foreach (var partner in _partnerSites)
        {
            if (host.EndsWith("." + partner, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>True if the current top-level document belongs to a partner site.</summary>
    private static bool IsOnPartnerSite(CoreWebView2 core)
    {
        try
        {
            var src = core.Source;
            if (string.IsNullOrEmpty(src)) return false;
            return IsPartnerSite(new Uri(src).Host.ToLowerInvariant());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        try
        {
            var uri = new Uri(url);
            if (IsBlockedDomain(uri.Host.ToLowerInvariant()))
                return true;

            var lower = url.ToLowerInvariant();
            return lower.Contains("/ads/") ||
                   lower.Contains("/ad/") ||
                   lower.Contains("doubleclick") ||
                   lower.Contains("googlesyndication") ||
                   lower.Contains("/popup") ||
                   lower.Contains("clicktrack") ||
                   lower.Contains("adserver");
        }
        catch
        {
            return false;
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        EnsurePanelAndWebViewCreated();

        // Accessing Handle forces creation of the Win32 HWND.  NativeControlHost will
        // reparent this HWND into the Avalonia window and keep it sized to this control.
        var handle = _panel!.Handle;
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Intentionally no-op: the panel and WebView2 are owned by this instance and
        // must survive reparenting into a fullscreen window. They are released in Dispose().
    }

    private void EnsurePanelAndWebViewCreated()
    {
        // The panel owns the reparented HWND and must survive across a browser-process
        // crash (B7). The WebView2 child, however, may be recreated after a ProcessFailed.
        _panel ??= new System.Windows.Forms.Panel();

        if (_webView == null)
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _panel.Controls.Add(_webView);
        }

        // Force HWND creation immediately so the handle is stable across reparenting.
        _ = _panel.Handle;
    }

    private void WireEvents(CoreWebView2? core)
    {
        if (core == null) return;

        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;
    }

    private void UnwireEvents(CoreWebView2? core)
    {
        if (core == null) return;

        core.DocumentTitleChanged -= OnDocumentTitleChanged;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.ContainsFullScreenElementChanged -= OnContainsFullScreenElementChanged;
        core.NewWindowRequested -= OnNewWindowRequested;
        core.ProcessFailed -= OnProcessFailed;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is { } core)
            TitleChanged?.Invoke(this, core.DocumentTitle);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core &&
            Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
        {
            Navigated?.Invoke(this, uri);
        }
    }

    private void OnContainsFullScreenElementChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is { } core)
            FullscreenChanged?.Invoke(this, core.ContainsFullScreenElement);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Keep all navigation inside the embedded browser instead of spawning extra windows.
        e.Handled = true;
        if (_webView?.CoreWebView2 is not { } core) return;

        // Partner pages: route popups in-window (never a new OS window), no ad filtering.
        if (IsOnPartnerSite(core))
        {
            core.Navigate(e.Uri);
            return;
        }

        // Drop ad/popup URLs entirely; otherwise keep navigation in-window (WPF parity).
        if (IsAdUrl(e.Uri))
            return;

        core.Navigate(e.Uri);
    }

    /// <summary>
    /// B7: a Chromium render/browser-process crash leaves WebView2 in a zombie state where every
    /// subsequent call throws. Dispose the crashed child control and reset init state so the next
    /// <see cref="EnsureInitializedAsync"/> lazily recreates it inside the still-reparented panel HWND
    /// (WPF BrowserService ProcessFailed parity).
    /// </summary>
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        try
        {
            if (_webView != null)
            {
                UnwireEvents(_webView.CoreWebView2);
                _panel?.Controls.Remove(_webView);
                _webView.Dispose();
                _webView = null;
            }
        }
        catch { /* best effort */ }
        finally
        {
            // Allow a fresh init on next navigation.
            _initTask = null;
        }

        ProcessFailed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_webView != null)
        {
            UnwireEvents(_webView.CoreWebView2);
            _webView.Dispose();
            _webView = null;
        }

        _panel?.Dispose();
        _panel = null;
    }

    /// <summary>
    /// B8: WPF forced-fullscreen exit detector. When the host reparents the WebView into a
    /// borderless fullscreen window and sets <c>window._ccpForcedFs = true</c>, HTML5
    /// <c>document.exitFullscreen()</c> is a no-op; this script gives dblclick parity with Esc/F11
    /// by posting <c>ccp_exit_fullscreen</c> to the host. Inert until <c>_ccpForcedFs</c> is set.
    /// Copied verbatim from WPF BrowserService for parity.
    /// </summary>
    private const string ForcedFullscreenExitScript = @"
        (function() {
            function inAnyFs() {
                return !!(document.fullscreenElement || window._ccpForcedFs);
            }
            function postExit() {
                try { window.chrome.webview.postMessage('ccp_exit_fullscreen'); } catch (_) {}
            }
            function exitLoop(remaining) {
                if (remaining <= 0 || !document.fullscreenElement) return;
                try {
                    var p = document.exitFullscreen ? document.exitFullscreen()
                          : (document.webkitExitFullscreen ? document.webkitExitFullscreen() : null);
                    if (p && p.then) {
                        p.then(function(){ exitLoop(remaining - 1); },
                               function(){ setTimeout(function(){ exitLoop(remaining - 1); }, 30); });
                    } else {
                        setTimeout(function(){ exitLoop(remaining - 1); }, 30);
                    }
                } catch (_) {
                    setTimeout(function(){ exitLoop(remaining - 1); }, 30);
                }
            }
            function dblHandler(e) {
                if (!inAnyFs()) return;
                if (e) {
                    try { e.stopImmediatePropagation(); } catch (_) {}
                    try { e.preventDefault(); } catch (_) {}
                }
                exitLoop(5);
                postExit();
            }
            document.addEventListener('dblclick', dblHandler, true);
            document.addEventListener('dblclick', dblHandler, false);
            window.addEventListener('dblclick', dblHandler, true);
            function bindOnVideo() {
                try {
                    var vids = document.querySelectorAll('video');
                    for (var i = 0; i < vids.length; i++) {
                        var v = vids[i];
                        if (v._ccpBound) continue;
                        v._ccpBound = true;
                        v.addEventListener('dblclick', dblHandler, true);
                        v.addEventListener('dblclick', dblHandler, false);
                    }
                } catch (_) {}
            }
            bindOnVideo();
            setInterval(function() {
                if (inAnyFs()) bindOnVideo();
            }, 1000);
            document.addEventListener('fullscreenchange', function() {
                if (!document.fullscreenElement && window._ccpForcedFs) {
                    postExit();
                }
            });
        })();
    ";
}
