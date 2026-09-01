using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// A WebView2 running the player page (<c>https://ccp.game/player/index.html</c>) plus the JSON
    /// bridge from docs/BROWSER_VIDEO_ENGINE_PLAN.md §3, as a plain WPF element that any host can
    /// drop into its own visual tree.
    ///
    /// Two hosts use it:
    ///   * <see cref="BrowserVideoWindow"/> - one per monitor, owned by <see cref="BrowserVideoEngine"/>,
    ///     for mandatory videos (Stage 1).
    ///   * <c>BubbleCountWindow</c> - inside the game window itself, so the counting HUD, the strict
    ///     lock and the ESC handling keep working exactly as they do over a VideoView (Stage 2).
    ///
    /// HARD RULE (plan §8): the hosting window must keep <c>AllowsTransparency = false</c> and
    /// nothing may ever call <c>SetLayeredWindowAttributes</c> on it - a WebView2 does not paint at
    /// all inside a layered window, and the constant-alpha path turns the content solid black.
    ///
    /// Outbound JSON is QUEUED until the page posts <c>ready</c>, exactly like
    /// <see cref="ChaosWebViewHost"/>; the page itself queues nothing.
    /// </summary>
    internal sealed class BrowserVideoSurface : Grid
    {
        private readonly string _tag;
        private readonly List<string> _pending = new();   // JSON held until the page says 'ready'
        private WebView2? _web;
        private string _navHost = "ccp.game";
        private bool _initStarted;
        private bool _disposed;

        /// <summary>True once the page has completed its handshake and the queue has been flushed.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Every page message except the built-in <c>ready</c>/<c>log</c> handling.</summary>
        public event Action<BrowserVideoSurface, JObject>? Message;

        /// <summary>The browser or renderer process died. Hosts treat this as a session failure
        /// (never as the file's fault - see the plan §4).</summary>
        public event Action<BrowserVideoSurface, CoreWebView2ProcessFailedKind>? ProcessFailed;

        /// <summary>Raised on the UI thread once the page posts <c>ready</c>.</summary>
        public event Action<BrowserVideoSurface>? Ready;

        /// <summary>
        /// The WebView2 for THIS surface could not be brought up: <c>EnsureCoreWebView2Async</c> threw,
        /// or it completed and left the core null. Both used to be a Warning and nothing else - and
        /// because the surface stays on screen as an OPAQUE BLACK window, that is precisely the
        /// reported "the primary monitor is black with no sound while the other screens play fine"
        /// (the engine inits the primary FIRST, so a swallowed failure there let every secondary go on
        /// to play normally). The host now hears about it and can fall this surface back to LibVLC
        /// instead of waiting out the whole pre-ready budget on a black screen.
        /// </summary>
        public event Action<BrowserVideoSurface, string>? InitFailed;

        public BrowserVideoSurface(string tag)
        {
            _tag = tag;
            Background = Brushes.Black;
            _web = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(_web);
        }

        /// <summary>
        /// Build the CoreWebView2 and navigate. MUST be called once the surface is in the visual tree
        /// of a shown window - EnsureCoreWebView2Async does nothing before that.
        /// </summary>
        public async Task InitAsync(
            CoreWebView2Environment env,
            IReadOnlyList<(string Host, string Folder, CoreWebView2HostResourceAccessKind Access)> mappings,
            string startUrl,
            string primaryHost)
        {
            if (_initStarted || _web == null || _disposed) return;
            _initStarted = true;
            try
            {
                await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                if (_disposed || _web?.CoreWebView2 == null)
                {
                    if (!_disposed)
                    {
                        App.Logger?.Warning("BrowserVideo[{Tag}]: WebView2 core null after Ensure", _tag);
                        RaiseInitFailed("WebView2 core null after EnsureCoreWebView2Async");
                    }
                    return;
                }

                var core = _web.CoreWebView2;
                var s = core.Settings;
                s.AreDevToolsEnabled = false;
                s.AreDefaultContextMenusEnabled = false;
                s.IsStatusBarEnabled = false;
                // Second lock on F5/Ctrl+R/F11 etc; the page preventDefaults them too.
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsZoomControlEnabled = false;
                s.IsBuiltInErrorPageEnabled = false;
                s.IsWebMessageEnabled = true;

                foreach (var (host, folder, access) in mappings)
                {
                    // A mapping whose folder does not exist is SILENTLY SKIPPED by WebView2, which
                    // shows up much later as a 404 on the video. The engine creates them first.
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        App.Logger?.Warning("BrowserVideo[{Tag}]: virtual host {Host} folder missing: {Folder}",
                            _tag, host, folder);
                        continue;
                    }
                    core.SetVirtualHostNameToFolderMapping(host, folder, access);
                }

                core.NavigationStarting += OnNavigationStarting;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnCoreProcessFailed;
                core.PermissionRequested += OnPermissionRequested;
                _navHost = primaryHost;

                core.Navigate(startUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("BrowserVideo[{Tag}]: InitAsync failed: {E}", _tag, ex.Message);
                RaiseInitFailed(ex.Message);
            }
        }

        /// <summary>
        /// Tell the host this surface will never post <c>ready</c> or <c>playing</c>. Never throws: a
        /// broken handler must not turn a recoverable surface failure into an unhandled one.
        ///
        /// Posted to a LATER dispatcher turn, not raised inline, and that is not cosmetic. The engine's
        /// WebView2 environment task is cached and warmed at startup, so <c>InitWindowsAsync</c>'s await
        /// completes synchronously and <c>InitAsync</c> runs INSIDE
        /// <c>BrowserVideoEngine.StartSession</c> - before the host has set <c>_browserActive</c>,
        /// adopted the windows or recorded the primary. A synchronous raise from there reaches
        /// <c>VideoService.OnBrowserFailed</c> while <c>_browserActive</c> is still false, where its
        /// first line drops it on the floor: the black primary this event exists to end would survive
        /// the very report meant to fix it, and any handler that DID run would be re-entering a host
        /// mid-bookkeeping. One dispatcher hop puts the raise strictly after StartSession returns.
        /// </summary>
        private void RaiseInitFailed(string reason)
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                App.Logger?.Debug("BrowserVideo[{Tag}]: InitFailed dropped - the dispatcher is shutting down", _tag);
                return;
            }
            try
            {
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                {
                    if (_disposed) return;   // the session ended while the hop was in flight
                    try { InitFailed?.Invoke(this, reason); }
                    catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: InitFailed handler threw: {E}", _tag, ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: InitFailed dispatch failed: {E}", _tag, ex.Message); }
        }

        /// <summary>Give the page keyboard focus so its keydown handler (and therefore the
        /// <c>{type:'key'}</c> bridge) actually runs. Best-effort; never throws.</summary>
        public void FocusWeb()
        {
            try { _web?.Focus(); }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}].FocusWeb: {E}", _tag, ex.Message); }
        }

        /// <summary>Post a message to the page; queued until the page's <c>ready</c> handshake.</summary>
        public void Post(object msg)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
                if (IsReady && _web?.CoreWebView2 != null)
                    _web.CoreWebView2.PostWebMessageAsJson(json);
                else
                    _pending.Add(json);
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}].Post: {E}", _tag, ex.Message); }
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Only ever our local page (defence in depth - the page never navigates).
            if (string.IsNullOrEmpty(e.Uri) ||
                !e.Uri.StartsWith("https://" + _navHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// The player page's audio-output routing (#938 plumbing) needs device LABELS from
        /// <c>enumerateDevices()</c>, and Chromium blanks audiooutput labels until the origin holds
        /// microphone permission - so the page's probe (a getUserMedia stream it stops on the next
        /// line) surfaces here. Grant is scoped to exactly our own page: https scheme AND the host
        /// this surface navigated to (the ccp.game virtual host; OnNavigationStarting already
        /// cancels every navigation elsewhere). Everything else - other origins, other permission
        /// kinds - is denied outright rather than left to a prompt, because these are chromeless
        /// fullscreen windows where a permission bubble would be unanswerable.
        /// </summary>
        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            try
            {
                bool ourPage = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps
                    && string.Equals(uri.Host, _navHost, StringComparison.OrdinalIgnoreCase);
                e.State = ourPage && e.PermissionKind == CoreWebView2PermissionKind.Microphone
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserVideo[{Tag}]: PermissionRequested handler failed: {E}", _tag, ex.Message);
            }
        }

        private void OnCoreProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            App.Logger?.Warning("BrowserVideo[{Tag}]: WebView2 process failed ({Kind})", _tag, e.ProcessFailedKind);
            try { ProcessFailed?.Invoke(this, e.ProcessFailedKind); }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: ProcessFailed handler threw: {E}", _tag, ex.Message); }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // NOTHING in here may block or go modal: this runs inside the browser's message loop.
            try
            {
                var json = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(json)) return;
                var o = JObject.Parse(json);
                switch ((string?)o["type"])
                {
                    case "ready":
                        IsReady = true;
                        FlushPending();
                        try { Ready?.Invoke(this); }
                        catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: Ready handler threw: {E}", _tag, ex.Message); }
                        break;
                    case "log":
                        // Information, not Debug: the global logger floor is Information and page
                        // logs are the only devtools-less window into the player page.
                        App.Logger?.Information("BrowserVideo[{Tag}][page]: {Msg}", _tag, (string?)o["msg"]);
                        break;
                    default:
                        try { Message?.Invoke(this, o); }
                        catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: message handler threw: {E}", _tag, ex.Message); }
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}].OnWebMessageReceived: {E}", _tag, ex.Message); }
        }

        private void FlushPending()
        {
            if (_web?.CoreWebView2 == null) return;
            foreach (var json in _pending)
            {
                try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
            }
            _pending.Clear();
        }

        /// <summary>Unhook + dispose the WebView2. Idempotent; the host calls it from its own close
        /// path (closing the window alone would leave the browser process alive).</summary>
        public void DisposeWeb()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_web?.CoreWebView2 != null)
                {
                    _web.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    _web.CoreWebView2.ProcessFailed -= OnCoreProcessFailed;
                    _web.CoreWebView2.PermissionRequested -= OnPermissionRequested;
                }
            }
            catch { }
            try { _web?.Dispose(); }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}]: WebView2 dispose failed: {E}", _tag, ex.Message); }
            _web = null;
            IsReady = false;
            _pending.Clear();
        }
    }
}
