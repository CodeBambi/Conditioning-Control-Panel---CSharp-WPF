using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
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
    /// Everything WebView2-shaped (the control, the bridge, the outbound queue) lives in
    /// <see cref="BrowserVideoSurface"/>, which BubbleCount hosts inside its own game window; this
    /// class is only the fullscreen chrome around one.
    /// </summary>
    internal sealed class BrowserVideoWindow : Window
    {
        private readonly Screen _screen;
        private readonly string _tag;
        private readonly BrowserVideoSurface _surface;

        /// <summary>True for the audio-bearing window (one per session). Secondaries load muted.</summary>
        public bool IsPrimary { get; }

        /// <summary>True once the page has completed its handshake and the queue has been flushed.</summary>
        public bool IsReady => _surface.IsReady;

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

            _surface = new BrowserVideoSurface(_tag);
            _surface.Message += (_, o) => Message?.Invoke(this, o);
            _surface.ProcessFailed += (_, kind) => ProcessFailed?.Invoke(this, kind);
            _surface.Ready += _ => Ready?.Invoke(this);
            Content = _surface;

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
        public Task InitAsync(
            CoreWebView2Environment env,
            IReadOnlyList<(string Host, string Folder, CoreWebView2HostResourceAccessKind Access)> mappings,
            string startUrl,
            string primaryHost)
            => _surface.InitAsync(env, mappings, startUrl, primaryHost);

        /// <summary>Give the page keyboard focus so its keydown handler (and therefore the
        /// <c>{type:'key'}</c> bridge) actually runs. Best-effort; never throws.</summary>
        public void FocusWeb()
        {
            try { Activate(); }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo[{Tag}].FocusWeb: {E}", _tag, ex.Message); }
            _surface.FocusWeb();
        }

        /// <summary>Post a message to the page; queued until the page's <c>ready</c> handshake.</summary>
        public void Post(object msg) => _surface.Post(msg);

        /// <summary>Unhook + dispose the WebView2. Runs on Close() too, so the shared teardown funnel
        /// (VideoService.CloseAll closes every window in its list) is enough to end a session cleanly.</summary>
        protected override void OnClosed(EventArgs e)
        {
            _surface.DisposeWeb();
            base.OnClosed(e);
        }

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
