using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// One fullscreen, borderless, topmost, OPAQUE window per monitor hosting the player page
    /// (<c>https://ccp.game/player/index.html</c>) in a WebView2. The mandatory-video surface for the
    /// browser engine; the LibVLC equivalent is <c>VideoService.CreateLibVLCVideoWindow</c>.
    ///
    /// HARD RULES (see docs/BROWSER_VIDEO_ENGINE_PLAN.md §8):
    ///   * <c>AllowsTransparency</c> stays FALSE - a WebView2 does not paint at all inside a layered
    ///     window.
    ///   * NOTHING may ever call <c>SetLayeredWindowAttributes</c> on this window - the constant-alpha
    ///     path turns the Chromium content solid black (reproduced live on the FYP feed, 2026-08-03).
    ///
    /// Message bridge: outbound JSON is QUEUED until the page posts <c>ready</c>, exactly like
    /// <see cref="ChaosWebViewHost"/>; the page itself queues nothing.
    /// </summary>
    internal sealed class BrowserVideoWindow : Window
    {
        private readonly Screen _screen;
        private readonly string _tag;
        private readonly List<string> _pending = new();   // JSON held until the page says 'ready'
        private WebView2? _web;
        private bool _initStarted;
        private bool _disposed;

        /// <summary>True for the audio-bearing window (one per session). Secondaries load muted.</summary>
        public bool IsPrimary { get; }

        /// <summary>True once the page has completed its handshake and the queue has been flushed.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Every page message except the built-in <c>ready</c>/<c>log</c> handling.</summary>
        public event Action<BrowserVideoWindow, JObject>? Message;

        /// <summary>The browser or renderer process died. The engine treats this as a session failure
        /// (never as the file's fault - see the plan §4).</summary>
        public event Action<BrowserVideoWindow, CoreWebView2ProcessFailedKind>? ProcessFailed;

        /// <summary>Raised on the UI thread once the page posts <c>ready</c>.</summary>
        public event Action<BrowserVideoWindow>? Ready;

        public BrowserVideoWindow(Screen screen, bool primary)
        {
            _screen = screen;
            IsPrimary = primary;
            _tag = primary ? "primary" : "secondary";

            var dpiScale = BubbleCountWindow.GetDpiForScreen(screen);

            AllowsTransparency = false;   // WebView2 never paints in a layered window - see class comment
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            // The audio-bearing window takes focus ONCE at show (same as the LibVLC primary), which
            // is what lets the page see keydown at all - keyboard over a focused WebView2 goes to
            // Chromium, so the page's {type:'key'} reports are the only route ESC/panic have back to
            // C#. The host stamps WS_EX_NOACTIVATE straight after, so the window can never RE-raise
            // itself over the attention targets / chaos layer afterwards.
            ShowActivated = primary;
            Topmost = true;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Full screen bounds up front (no start-small-then-maximize white frame, #368). The DIP
            // math below is only correct on the creation monitor; PinToScreen fixes mixed DPI.
            Left = screen.Bounds.X / dpiScale;
            Top = screen.Bounds.Y / dpiScale;
            Width = screen.Bounds.Width / dpiScale;
            Height = screen.Bounds.Height / dpiScale;

            _web = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(_web);
            Content = grid;

            PinToScreen();
        }

        /// <summary>
        /// Pins the window to the monitor's true PHYSICAL bounds. Mirrors
        /// <c>VideoService.ForceFullScreenBounds</c>: the app is PerMonitorV2-aware, so DIP bounds set
        /// before the HWND exists are realized in the CREATION monitor's DPI and a secondary monitor
        /// at a different scale gets a part-width window. SetWindowPos works in real pixels. Applied
        /// twice because WM_DPICHANGED can resize the window back after the move.
        /// </summary>
        private void PinToScreen()
        {
            void Apply()
            {
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd == IntPtr.Zero) return;
                    var b = _screen.Bounds;
                    SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BrowserVideoWindow.PinToScreen failed: {E}", ex.Message);
                }
            }

            SourceInitialized += (_, _) => Apply();
            Loaded += (_, _) => Apply();
        }

        /// <summary>
        /// Build the CoreWebView2 and navigate. MUST be called after <see cref="Window.Show"/> -
        /// EnsureCoreWebView2Async only works once the control is in the visual tree.
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

        private string _navHost = "ccp.game";

        /// <summary>Give the page keyboard focus so its keydown handler (and therefore the
        /// <c>{type:'key'}</c> bridge) actually runs. Best-effort; never throws.</summary>
        public void FocusWeb()
        {
            try
            {
                Activate();
                _web?.Focus();
            }
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

        /// <summary>Unhook + dispose the WebView2. Runs on Close() too, so the shared teardown funnel
        /// (VideoService.CloseAll closes every window in its list) is enough to end a session cleanly.</summary>
        protected override void OnClosed(EventArgs e)
        {
            DisposeWeb();
            base.OnClosed(e);
        }

        private void DisposeWeb()
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

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
