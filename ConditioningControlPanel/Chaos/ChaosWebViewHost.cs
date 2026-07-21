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
/// Reusable WebView2 host (windowed or borderless-fullscreen) for local three.js pages served over
/// virtual https origins.
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

        /// <summary>true = fill the primary screen borderless at launch (passive tunnel backdrop);
        /// false = a normal titled, resizable, alt-tabbable window that the page can toggle to
        /// borderless fullscreen via the dock button (the DtRH game). Default true.</summary>
        public bool StartFullscreen { get; init; } = true;

        /// <summary>Window title (shown on the taskbar/Alt-Tab in windowed mode).</summary>
        public string WindowTitle { get; init; } = "Conditioning Control Panel";

        /// <summary>true = glue this window ABOVE MainWindow via native (GWL_HWNDPARENT) ownership,
        /// so nothing the app does to main can bury the game surface. Set for the player-facing
        /// game windows (DtRH, Graded Intake, Bureau). See <see cref="AttachMainWindowGlue"/>.</summary>
        public bool OwnedByMainWindow { get; init; }
    }

    private readonly Options _opts;
    private readonly List<string> _pending = new();   // JSON queued until the page says 'ready'
    private Window? _window;
    private WebView2? _web;
    private bool _initStarted;
    private bool _disposed;
    private bool _isFullscreen;
    private double _windowedW, _windowedH;   // remembered windowed size (default 85% of screen)
    private Window? _glueOwner;              // MainWindow, while OwnedByMainWindow glue is live
    private EventHandler? _glueOwnerStateChanged;
    private IntPtr _glueOwnerHandle;
    private bool _glueAttached;
    private HwndSource? _glueHwndSource;     // our own window's source, while the cascade veto is hooked
    private HwndSourceHook? _glueWndHook;

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

        // Default windowed size: 85% of the primary screen, centered.
        _windowedW = SystemParameters.PrimaryScreenWidth * 0.85;
        _windowedH = SystemParameters.PrimaryScreenHeight * 0.85;

        _window = new Window
        {
            AllowsTransparency = false,   // WebView2 does not paint in a layered window; stay opaque
            Background = Brushes.Black,
            Title = _opts.WindowTitle,
            // Every effect is in-world now, so no native payload window needs to stack over the
            // page: the host never needs Topmost, which is what let it be Alt-Tabbed / minimized.
            Topmost = false,
            ShowInTaskbar = true,
            ShowActivated = _opts.InputEnabled,
            Focusable = _opts.InputEnabled,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = grid,
        };
        ApplyWindowMode(_opts.StartFullscreen);   // sets style / bounds / resize mode
        if (!_opts.InputEnabled)
            _window.SourceInitialized += (_, _) => ApplyPassiveExStyles(_window);
        _window.Show();
        if (_opts.OwnedByMainWindow) AttachMainWindowGlue();
        if (_opts.InputEnabled) { try { _window.Activate(); } catch { } }

        _ = InitWebAsync();
        App.Logger?.Information("{Tag}: window up (input={Input}, fullscreen={FS}) → {Url}",
            _opts.LogTag, _opts.InputEnabled, _isFullscreen, _opts.StartUrl);
    }

    /// <summary>Lay the window out as borderless-fullscreen or a normal titled window.</summary>
    private void ApplyWindowMode(bool fullscreen)
    {
        if (_window == null) return;
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
        if (fullscreen)
        {
            _window.WindowState = WindowState.Normal;   // manual bounds cover the whole screen (taskbar included)
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.Left = 0; _window.Top = 0;
            _window.Width = sw; _window.Height = sh;
        }
        else
        {
            _window.WindowStyle = WindowStyle.SingleBorderWindow;   // title bar = free Alt-Tab / minimize / move
            _window.ResizeMode = ResizeMode.CanResize;
            double w = Math.Min(_windowedW, sw), h = Math.Min(_windowedH, sh);
            _window.Width = w; _window.Height = h;
            _window.Left = Math.Max(0, (sw - w) / 2);
            _window.Top = Math.Max(0, (sh - h) / 2);
            _window.WindowState = WindowState.Normal;
        }
        _isFullscreen = fullscreen;
        // WindowStyle/ResizeMode churn re-stamps the frame; re-assert the owner link so a
        // fullscreen toggle can never silently unglue the window from main.
        RefreshNativeOwner();
    }

    /// <summary>True while the window is borderless-fullscreen (host-owned, not the browser API).</summary>
    public bool IsFullscreen => _isFullscreen;

    /// <summary>Toggle borderless fullscreen. The DtRH page drives this over the bridge
    /// (<c>fullscreen-set</c>) instead of the browser Fullscreen API, so Esc stays the game's
    /// to own; the passive tunnel backdrop still rides <c>ContainsFullScreenElementChanged</c>.</summary>
    public void SetFullscreen(bool on)
    {
        if (_window == null || _isFullscreen == on) return;
        // Going fullscreen: remember the current windowed size so exit restores it.
        if (on && _window.WindowState == WindowState.Normal
            && _window.WindowStyle == WindowStyle.SingleBorderWindow)
        {
            if (_window.ActualWidth > 0) _windowedW = _window.ActualWidth;
            if (_window.ActualHeight > 0) _windowedH = _window.ActualHeight;
        }
        ApplyWindowMode(on);
        if (_opts.InputEnabled) { try { _window.Activate(); } catch { } }
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

    // ============================ main-window glue ============================
    //
    // Native (Win32) ownership. A player-facing game window is OWNED by MainWindow, so the window
    // manager itself keeps it directly above main and raises/lowers the pair as a GROUP. Whatever
    // lifts main — the natively-owned avatar tube being re-activated by a bark, a flash/video
    // window handing focus back when it closes, a tray restore, a panic Topmost pulse (which
    // propagates to owned windows and back) — now carries the game surface up with it instead of
    // burying it. No polling, no manual raises, nothing to race.
    //
    // This is the same cure the avatar tube got (AvatarTubeWindow.Windowing.ApplyNativeOwner):
    // raw GWL_HWNDPARENT, NOT WPF's Window.Owner, which additionally drops the taskbar button and
    // couples managed visibility. Topmost is deliberately NOT used — it would float the game over
    // every OTHER application on the desktop, which is not what "above main" means.

    private const int GWL_HWNDPARENT = -8;

    /// <summary>Own this window to MainWindow and keep the link in step with main's window state.</summary>
    private void AttachMainWindowGlue()
    {
        try
        {
            if (_window == null || _glueAttached) return;
            var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
            if (main == null || ReferenceEquals(main, _window)) return;
            var ownerHwnd = new WindowInteropHelper(main).Handle;
            if (ownerHwnd == IntPtr.Zero) return;

            _glueOwner = main;
            _glueOwnerHandle = ownerHwnd;
            _glueAttached = true;
            RefreshNativeOwner();

            // Windows hides a window's owned windows while the owner is MINIMIZED — with the glue
            // on, minimizing main would make the game vanish (taskbar button and all) with no way
            // back. Drop the link for the duration of the minimize, restore it when main returns.
            // (Tray "minimize" is Hide(), not minimize, and does not cascade — DtRH relies on that.)
            //
            // StateChanged ALONE was never enough: it is raised off main's WM_SIZE, i.e. after the
            // window manager has already run the cascade, so by the time the link was dropped the
            // game window had been hidden and clearing GWL_HWNDPARENT did not bring it back. That
            // is why minimizing main still took the intake down with it. Three layers now:
            //   1. VetoCascadeHide  — the WM_SHOWWINDOW/SW_PARENTCLOSING notification the cascade
            //                         sends us first; un-glue there and refuse the hide (this is
            //                         the only layer that runs BEFORE we are hidden).
            //   2. StateChanged     — re-assert the link for main's new state (and re-raise above
            //                         main when it comes back).
            //   3. a deferred visibility repair — if the hide still landed (the ordering of the
            //                         cascade against the owner's WM_SIZE is not contractual), show
            //                         ourselves again once the pump has drained.
            HookCascadeVeto();
            _glueOwnerStateChanged = (_, _) =>
            {
                RefreshNativeOwner();
                try
                {
                    _window?.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(EnsureNativeVisible));
                }
                catch { }
            };
            main.StateChanged += _glueOwnerStateChanged;
            App.Logger?.Information("{Tag}: glued above MainWindow (native owner)", _opts.LogTag);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.AttachMainWindowGlue: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>Re-assert the owner link, honouring main's current window state. No-op when the
    /// host was not launched with <see cref="Options.OwnedByMainWindow"/>.</summary>
    private void RefreshNativeOwner()
    {
        if (!_glueAttached) return;
        bool ownerDown = _glueOwner == null || _glueOwner.WindowState == WindowState.Minimized;
        ApplyNativeOwner(!ownerDown);
        // Main is minimized: nothing can bury us, so the link buys nothing and the cascade would
        // only take us down with it. Make sure we survived it.
        if (ownerDown) EnsureNativeVisible();
    }

    private void ApplyNativeOwner(bool owned)
    {
        try
        {
            if (_window == null) return;
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowLongPtr(hwnd, GWL_HWNDPARENT, owned ? _glueOwnerHandle : IntPtr.Zero);
            // Owner changes are cached — flush the frame so the z-order link takes effect now.
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            // Ownership is a rule about the FUTURE ("main may never be placed above this window"),
            // not a reorder: a link re-attached while main happens to sit on top would leave the
            // page buried under it forever. Now that the link is dropped and restored across every
            // minimize, slot ourselves directly above main each time we take it — without
            // activating, so re-gluing never steals focus from whatever the user is doing.
            if (owned && _glueOwnerHandle != IntPtr.Zero)
                SetWindowPos(hwnd, _glueOwnerHandle, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.ApplyNativeOwner: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>
    /// Layer 1 of the minimize decoupling: WM_SHOWWINDOW with lParam == SW_PARENTCLOSING is the
    /// notification the window manager sends an OWNED window just before hiding it along with its
    /// minimizing owner. Handling it without letting DefWindowProc run refuses that hide — and it
    /// is the only hook that fires before we are gone, whichever way main got minimized (title-bar
    /// button, taskbar click, Win+D, or the app minimizing itself, as the intake launch does).
    /// The owner link is dropped in the same breath so nothing re-hides us afterwards.
    /// </summary>
    private void HookCascadeVeto()
    {
        try
        {
            if (_window == null || _glueWndHook != null) return;
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var src = HwndSource.FromHwnd(hwnd);
            if (src == null) return;
            _glueWndHook = VetoCascadeHide;
            _glueHwndSource = src;
            src.AddHook(_glueWndHook);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.HookCascadeVeto: {E}", _opts.LogTag, ex.Message); }
    }

    private IntPtr VetoCascadeHide(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_glueAttached && msg == WM_SHOWWINDOW
            && wParam == IntPtr.Zero                       // fShow = FALSE, i.e. "about to hide"
            && lParam.ToInt64() == SW_PARENTCLOSING)
        {
            ApplyNativeOwner(false);
            handled = true;   // swallow it: DefWindowProc is what would hide us
        }
        return IntPtr.Zero;
    }

    /// <summary>Layer 3: undo a cascade hide that got through. WPF still believes the window is
    /// Visible (the hide happened entirely in USER32, behind its back), so this is a native-only
    /// repair — no WPF visibility churn, no relayout, and SW_SHOWNA rather than SW_SHOW so ducking
    /// main never yanks focus back to the page.</summary>
    private void EnsureNativeVisible()
    {
        try
        {
            if (_disposed || _window == null) return;
            if (_window.Visibility != Visibility.Visible) return;   // we hid it on purpose
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero || IsWindowVisible(hwnd)) return;
            ShowWindow(hwnd, SW_SHOWNA);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.EnsureNativeVisible: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>Unhook the state listener and clear the native owner before the window closes, so
    /// no stale owner link or event subscription outlives the host.</summary>
    private void DetachMainWindowGlue()
    {
        if (!_glueAttached) return;
        try
        {
            if (_glueOwner != null && _glueOwnerStateChanged != null)
                _glueOwner.StateChanged -= _glueOwnerStateChanged;
        }
        catch { }
        try
        {
            if (_glueHwndSource != null && _glueWndHook != null)
                _glueHwndSource.RemoveHook(_glueWndHook);
        }
        catch { }
        ApplyNativeOwner(false);
        _glueAttached = false;
        _glueOwner = null;
        _glueOwnerStateChanged = null;
        _glueOwnerHandle = IntPtr.Zero;
        _glueHwndSource = null;
        _glueWndHook = null;
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
            // The page's Fullscreen API (the dock's [ ] button) drives the WPF window: entering
            // element-fullscreen borderless-maximizes the host, exiting restores the titled window.
            core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;

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

    private void OnContainsFullScreenElementChanged(object? sender, object e)
    {
        try
        {
            var core = _web?.CoreWebView2;
            if (core == null) return;
            bool fs = core.ContainsFullScreenElement;
            _window?.Dispatcher.Invoke(() => SetFullscreen(fs));
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.FullScreenChanged: {E}", _opts.LogTag, ex.Message); }
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
                _web.CoreWebView2.ContainsFullScreenElementChanged -= OnContainsFullScreenElementChanged;
            }
        }
        catch { }
        try { DetachMainWindowGlue(); } catch { }
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
    private const int WM_SHOWWINDOW = 0x0018;
    private const int SW_PARENTCLOSING = 1;   // lParam of the owner-minimize cascade's WM_SHOWWINDOW
    private const int SW_SHOWNA = 8;          // show at current size/pos WITHOUT activating
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
