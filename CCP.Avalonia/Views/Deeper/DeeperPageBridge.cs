// PORTED from WPF 7.1.5 (hunt IB5, HB17): the Deeper player's and editor's page zoom and page fullscreen.
// WPF used three CoreWebView2 members the port's NativeWebView does not expose: ZoomFactor,
// ContainsFullScreenElementChanged and AddScriptToExecuteOnDocumentCreatedAsync. What the adapter DOES have
// is a script channel (WebHost.InvokeScriptAsync), a page -> host message (window.invokeCSharpAction) and
// NavigationCompleted, so:
//   - zoom is a CSS zoom on the document, +/-10 % clamped to 0.25..5.0 (WPF AdjustVideoZoom), re-applied
//     after every navigation so it is not lost with the document;
//   - Ctrl+wheel over the page (the native control keeps the wheel, the host never sees it) is caught by a
//     listener in the page that posts ccp_zoom_in / ccp_zoom_out, WPF's own message names;
//   - HTML5 fullscreen is seen by a fullscreenchange listener that posts ccp_fullscreen_on / _off, and the
//     host answers with its WINDOW state (full screen and back), not WPF's reparent into a second window.
// The listeners go in after NavigationCompleted, not at document creation: a page that enters fullscreen or
// zooms before its load completes is missed until the next navigation.
using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    internal sealed class DeeperPageBridge : IDisposable
    {
        internal const double MinZoom = 0.25, MaxZoom = 5.0;
        internal const string ZoomIn = "ccp_zoom_in", ZoomOut = "ccp_zoom_out";
        internal const string FullscreenOn = "ccp_fullscreen_on", FullscreenOff = "ccp_fullscreen_off";

        private readonly WebHost _host;
        private readonly Window _window;
        private WindowState _stateBefore;
        private bool _wentFullscreen, _disposed;

        /// <summary>Host -> page. Tests capture the scripts; the default is the host's own channel.</summary>
        internal Func<string, Task<string?>> Invoke { get; set; }

        /// <summary>The page went into (true) or out of (false) HTML5 fullscreen; the window state has already followed.</summary>
        internal event Action<bool>? FullscreenChanged;

        internal double Zoom { get; private set; } = 1.0;
        internal bool PageFullscreen { get; private set; }

        internal DeeperPageBridge(WebHost host, Window window)
        {
            _host = host;
            _window = window;
            Invoke = host.InvokeScriptAsync;
            host.NavigationCompleted += OnNavigationCompleted;
            host.WebMessage += OnWebMessage;
        }

        /// <summary>WPF AdjustVideoZoom: +/-10 % clamped to [0.25, 5.0].</summary>
        internal static double Next(double zoom, double delta) => Math.Clamp(Math.Round(zoom + delta, 2), MinZoom, MaxZoom);

        internal void Adjust(double delta)
        {
            if (_disposed) return;
            Zoom = Next(Zoom, delta);
            _ = ApplyAsync();
        }

        /// <summary>Sets the document's zoom and installs the two listeners once per document.</summary>
        internal static string Script(double zoom) =>
            "(function(){try{document.documentElement.style.zoom='" + zoom.ToString("0.##", CultureInfo.InvariantCulture) + "';}catch(e){}"
            + "if(window.__ccpDeeperBridge)return 'ok';window.__ccpDeeperBridge=1;"
            + "var post=function(m){try{window.invokeCSharpAction(m);}catch(e){}};"
            + "window.addEventListener('wheel',function(e){if(!e.ctrlKey)return;e.preventDefault();"
            + "post(e.deltaY<0?'" + ZoomIn + "':'" + ZoomOut + "');},{passive:false,capture:true});"
            + "var fs=function(){post((document.fullscreenElement||document.webkitFullscreenElement)?'" + FullscreenOn + "':'" + FullscreenOff + "');};"
            + "document.addEventListener('fullscreenchange',fs);document.addEventListener('webkitfullscreenchange',fs);"
            + "return 'ok';})()";

        /// <summary>WPF ExitFullscreenViaScript: asks the page itself to leave, so its own player chrome stays right.</summary>
        internal const string ExitFullscreenScript =
            "(function(){try{if(document.fullscreenElement)document.exitFullscreen();"
            + "else if(document.webkitFullscreenElement)document.webkitExitFullscreen();}catch(e){}return 'ok';})()";

        private async Task ApplyAsync()
        {
            try { await Invoke(Script(Zoom)); }
            catch (Exception ex) { Log.Debug("DeeperPageBridge: zoom script failed: {Error}", ex.Message); }
        }

        private void OnNavigationCompleted(Uri url)
        {
            if (_disposed) return;
            // A new document: it is not fullscreen, it has no listeners and it has lost the zoom.
            SetFullscreen(false);
            _ = ApplyAsync();
        }

        private void OnWebMessage(string body)
        {
            if (_disposed) return;
            switch (body.Trim('"'))
            {
                case ZoomIn: Adjust(+0.10); break;
                case ZoomOut: Adjust(-0.10); break;
                case FullscreenOn: SetFullscreen(true); break;
                case FullscreenOff: SetFullscreen(false); break;
            }
        }

        /// <summary>The host half: the window goes full screen with the page and returns to the state it had.
        /// A window the user had already put full screen is left alone on the way out.</summary>
        private void SetFullscreen(bool on)
        {
            if (on == PageFullscreen) return;
            PageFullscreen = on;
            try
            {
                if (on)
                {
                    _wentFullscreen = _window.WindowState != WindowState.FullScreen;
                    if (_wentFullscreen)
                    {
                        _stateBefore = _window.WindowState;
                        _window.WindowState = WindowState.FullScreen;
                    }
                }
                else if (_wentFullscreen)
                {
                    _wentFullscreen = false;
                    _window.WindowState = _stateBefore;
                }
            }
            catch (Exception ex) { Log.Debug("DeeperPageBridge: window state failed: {Error}", ex.Message); }
            try { FullscreenChanged?.Invoke(on); }
            catch (Exception ex) { Log.Debug("DeeperPageBridge: fullscreen handler failed: {Error}", ex.Message); }
        }

        /// <summary>The page leaves fullscreen (panic, a swapped clip): the script asks, the window follows at once.</summary>
        internal void LeaveFullscreen()
        {
            if (!PageFullscreen) return;
            _ = Invoke(ExitFullscreenScript);
            SetFullscreen(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _host.NavigationCompleted -= OnNavigationCompleted;
            _host.WebMessage -= OnWebMessage;
            SetFullscreen(false);
            _disposed = true;
        }
    }
}
