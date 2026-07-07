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

namespace ConditioningControlPanel;

/// <summary>
/// Reusable fullscreen WebView2 host for local three.js pages served over virtual https origins.
/// Extracted from <see cref="ChaosTunnelService"/> so the DtRH browser game (an INPUT-RECEIVING
/// game surface) and the legacy tunnel (a passive backdrop) share the fiddly plumbing:
/// environment creation with per-instance user-data folder, the anti-MPO / anti-occlusion browser
/// args, settings hardening, virtual-host registration, navigation lockdown, the queue-until-ready
/// message bridge, and ProcessFailed wiring.
///
/// The window is always OPAQUE and non-layered: a WebView2 child HWND does not paint reliably
/// inside AllowsTransparency=true, and staying out of the layered-window path keeps this host
/// clear of the app's historic render-thread deadlock cluster.
/// </summary>
internal sealed class ChaosWebViewHost : IDisposable
{
    public sealed class Options
    {
        /// <summary>Page to load, e.g. "https://ccp.game/dtrh/index.html".</summary>
        public required string StartUrl { get; init; }

        /// <summary>Host the page's own files are served from; navigation is locked to it.</summary>
        public required string PrimaryHost { get; init; }

        /// <summary>All virtual-host mappings to register (hostname → disk folder + access kind).</summary>
        public required IReadOnlyList<(string Host, string Folder, CoreWebView2HostResourceAccessKind Access)> Mappings { get; init; }

        /// <summary>Folder name under %LOCALAPPDATA%/ConditioningControlPanel for browser state.</summary>
        public required string UserDataFolderName { get; init; }

        /// <summary>true = game surface (activatable, focusable, topmost); false = passive backdrop.</summary>
        public bool InputEnabled { get; init; }

        /// <summary>Raised for every page message except the built-in "ready"/"log" handling.</summary>
        public Action<JObject>? OnMessage { get; init; }

        /// <summary>Raised (on the UI thread) once the page posts "ready".</summary>
        public Action? OnReady { get; init; }

        /// <summary>Raised when the WebView2 browser/render process dies. Host does NOT auto-recover.</summary>
        public Action<CoreWebView2ProcessFailedKind>? OnProcessFailed { get; init; }

        /// <summary>Serilog-ish tag used in log lines, e.g. "DtrhHost".</summary>
        public string LogTag { get; init; } = "ChaosWebViewHost";

        /// <summary>Extra Chromium args appended to the shared anti-MPO/occlusion set
        /// (e.g. "--autoplay-policy=no-user-gesture-required" for the game's audio bed).</summary>
        public string? ExtraBrowserArguments { get; init; }
    }

    private readonly Options _opts;
    private readonly List<string> _pending = new();   // JSON queued until the page says 'ready'
    private Window? _window;
    private WebView2? _web;
    private bool _initStarted;
    private bool _disposed;

    public bool IsReady { get; private set; }
    public Window? Window => _window;
    public WebView2? WebView => _web;

    public ChaosWebViewHost(Options opts) => _opts = opts;

    /// <summary>Build + show the fullscreen window on the primary screen and start loading the page.</summary>
    public void Show()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChaosWebViewHost));
        if (_window != null) return;

        _web = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var grid = new Grid { Background = Brushes.Black };
        grid.Children.Add(_web);

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,   // WebView2 does not paint in a layered window; stay opaque
            Background = Brushes.Black,
            Topmost = _opts.InputEnabled,
            ShowInTaskbar = false,
            ShowActivated = _opts.InputEnabled,
            Focusable = _opts.InputEnabled,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight,
            Content = grid,
        };
        if (!_opts.InputEnabled)
            _window.SourceInitialized += (_, _) => ApplyPassiveExStyles(_window);
        _window.Show();
        if (_opts.InputEnabled) { try { _window.Activate(); } catch { } }

        _ = InitWebAsync();
        App.Logger?.Information("{Tag}: window up (input={Input}) → {Url}", _opts.LogTag, _opts.InputEnabled, _opts.StartUrl);
    }

    /// <summary>Post a message to the page; queued until the page's 'ready' handshake.</summary>
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
        catch (Exception ex) { App.Logger?.Debug("{Tag}.Post: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>Return Win32 focus to the game surface (e.g. after a payload window closed).</summary>
    public void FocusWeb()
    {
        try
        {
            _window?.Activate();
            _web?.Focus();
        }
        catch { }
    }

    private async Task InitWebAsync()
    {
        if (_initStarted || _web == null) return;
        _initStarted = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel", _opts.UserDataFolderName);
            Directory.CreateDirectory(userDataFolder);

            // --disable-direct-composition-video-overlays: keep the WebGL swapchain composited
            // through DWM (the app's established anti-MPO flag). CalculateNativeWinOcclusion:
            // native payload windows stack over the page; Chromium's occlusion tracker would
            // decide the page is covered and throttle rAF — turn it off.
            var args = "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion";
            if (!string.IsNullOrWhiteSpace(_opts.ExtraBrowserArguments))
                args += " " + _opts.ExtraBrowserArguments;
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args };
            var env = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                .ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (_disposed) return;
            if (_web.CoreWebView2 == null) { App.Logger?.Warning("{Tag}: WebView2 core null", _opts.LogTag); return; }

            var core = _web.CoreWebView2;
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsWebMessageEnabled = true;

            foreach (var (host, folder, access) in _opts.Mappings)
            {
                if (!Directory.Exists(folder))
                {
                    App.Logger?.Warning("{Tag}: virtual host {Host} folder missing: {Folder}", _opts.LogTag, host, folder);
                    continue;
                }
                core.SetVirtualHostNameToFolderMapping(host, folder, access);
            }

            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;

            core.Navigate(_opts.StartUrl);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("{Tag}.InitWebAsync failed: {E}", _opts.LogTag, ex.Message);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Only ever our local page. Block anything else (defence in depth).
        if (string.IsNullOrEmpty(e.Uri) ||
            !e.Uri.StartsWith("https://" + _opts.PrimaryHost + "/", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.Logger?.Warning("{Tag}: WebView2 process failed ({Kind})", _opts.LogTag, e.ProcessFailedKind);
        try { _opts.OnProcessFailed?.Invoke(e.ProcessFailedKind); } catch { }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(json)) return;
            var o = JObject.Parse(json);
            var type = (string?)o["type"];
            switch (type)
            {
                case "ready":
                    IsReady = true;
                    FlushPending();
                    try { _opts.OnReady?.Invoke(); } catch { }
                    break;
                case "log":
                    // Information, not Debug: the global logger floor is Information and page
                    // logs are the only devtools-less window into the hosted page.
                    App.Logger?.Information("{Tag}[page]: {Msg}", _opts.LogTag, (string?)o["msg"]);
                    break;
                default:
                    _opts.OnMessage?.Invoke(o);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.OnWebMessageReceived: {E}", _opts.LogTag, ex.Message); }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_web?.CoreWebView2 != null)
            {
                _web.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
            }
        }
        catch { }
        try { _web?.Dispose(); } catch { }
        try { _window?.Close(); } catch { }
        _web = null; _window = null; IsReady = false; _pending.Clear();
    }

    // Passive backdrops absorb clicks (no WS_EX_TRANSPARENT) but never steal focus / show in Alt-Tab.
    private static void ApplyPassiveExStyles(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
