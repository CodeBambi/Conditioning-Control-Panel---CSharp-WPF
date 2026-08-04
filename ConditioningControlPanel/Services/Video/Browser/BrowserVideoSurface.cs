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
                    if (!_disposed) App.Logger?.Warning("BrowserVideo[{Tag}]: WebView2 core null after Ensure", _tag);
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
                _navHost = primaryHost;

                core.Navigate(startUrl);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("BrowserVideo[{Tag}]: InitAsync failed: {E}", _tag, ex.Message);
            }
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
