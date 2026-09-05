using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        /// <summary>
        /// Raised once CoreWebView2 exists and every shared setting/mapping is applied, but BEFORE
        /// the first navigation. The one seam for per-host plumbing that has to be in place before
        /// the start URL is fetched — the JustDrop host uses it to attach the request filter that
        /// puts the account's auth header on its sign-in navigation, which is worthless if it lands
        /// after the request has already gone out.
        ///
        /// <para>Anything thrown here is logged and swallowed: a host that fails to add an extra
        /// handler still gets its page.</para>
        /// </summary>
        public Action<CoreWebView2>? OnCoreCreated { get; init; }

        /// <summary>
        /// Extra origins this window may navigate to, beyond <see cref="PrimaryHost"/>. Empty by
        /// default, and deliberately so: the lockdown in OnNavigationStarting is what stops a
        /// hosted page wandering off to somewhere with no chrome to escape from.
        /// </summary>
        public IReadOnlyList<string> AdditionalNavigationHosts { get; init; } = Array.Empty<string>();

        /// <summary>Serilog-ish tag used in log lines, e.g. "DtrhHost".</summary>
        public string LogTag { get; init; } = "ChaosWebViewHost";

        /// <summary>Extra Chromium args appended to the shared anti-MPO/occlusion set
        /// (e.g. "--autoplay-policy=no-user-gesture-required" for the game's audio bed).</summary>
        public string? ExtraBrowserArguments { get; init; }

        /// <summary>true = fill the primary screen borderless at launch (passive tunnel backdrop);
        /// false = a normal titled, resizable, alt-tabbable window that the page can toggle to
        /// borderless fullscreen via the dock button (the DtRH game). Default true.</summary>
        public bool StartFullscreen { get; init; } = true;

        /// <summary>
        /// true = the WINDOW's fullscreen belongs to the host, not the page. Three things follow,
        /// and they only make sense together:
        /// <list type="number">
        /// <item>a page calling <c>requestFullscreen()</c> fills the WebView2's client area and
        /// nothing more - it can no longer take the whole window borderless;</item>
        /// <item>a visible toggle rides in a host-drawn strip above the page, present in BOTH
        /// modes, because in fullscreen it is the only exit that exists;</item>
        /// <item>Esc leaves fullscreen from either focus state, and the fullscreen the toggle does
        /// engage actually covers the taskbar.</item>
        /// </list>
        ///
        /// <para><b>Why it is opt-in.</b> The default (false) is the behaviour the tunnel backdrop
        /// and the DtRH game were built on: the page drives, Esc is the game's to own, and there is
        /// no host chrome over the canvas. JUST DROP is the opposite case - a REMOTE page whose
        /// player calls requestFullscreen when an order opens. That used to strip the title bar off
        /// the shop window with no affordance left to undo it, while the taskbar kept its z-order
        /// claim and painted over the page's own bottom-right exit. Windowed by default, escapable
        /// by construction.</para>
        /// </summary>
        public bool HostOwnedFullscreen { get; init; }

        /// <summary>Window title (shown on the taskbar/Alt-Tab in windowed mode).</summary>
        public string WindowTitle { get; init; } = "Conditioning Control Panel";

        /// <summary>Windowed width in DIPs. Null (default) = the historic 85%-of-primary-screen
        /// frame every existing caller was built on. Only consulted when the window is NOT
        /// host-owned-fullscreen; the fullscreen path still covers the screen.</summary>
        public double? WindowedWidth { get; init; }

        /// <summary>Windowed height in DIPs. See <see cref="WindowedWidth"/>.</summary>
        public double? WindowedHeight { get; init; }

        /// <summary>true = centre the windowed frame on MainWindow rather than on the primary
        /// screen. Falls back to the primary screen whenever main is missing, hidden, minimized or
        /// has no arranged size yet, so it can never place a window off in the dark.</summary>
        public bool CenterOnMainWindow { get; init; }

        /// <summary>true = glue this window ABOVE MainWindow via native (GWL_HWNDPARENT) ownership,
        /// so nothing the app does to main can bury the game surface. Set for the player-facing
        /// game windows (DtRH, Graded Intake, Bureau). See <see cref="AttachMainWindowGlue"/>.</summary>
        public bool OwnedByMainWindow { get; init; }
    }

    /// <summary>Virtual host serving DOWNLOADED content packs (audio that no longer ships in the
    /// installer — see docs/CONTENT_PACKS_PLAN.md §3). Mirrors the install-dir web layout, so a file
    /// at https://ccp.game/dtrh/assets/x.mp3 is at https://ccp.content/dtrh/assets/x.mp3 when the
    /// pack that owns it has been fetched. The page-side shims (dtrh/shared/audioSrc.js,
    /// intake/core/audioSrc.js) pick a host per file and fall back to the other one once.</summary>
    public const string ContentHost = "ccp.content";

    /// <summary>%LOCALAPPDATA%/ConditioningControlPanel/content/Resources/web — the pack mirror of
    /// {exe}/Resources/web. The install dir is not writable under Program Files, so downloads land
    /// here (see the plan's "Runtime" section). Anchored on the SAME root the C# probe uses
    /// (<see cref="Services.ContentLocator.ContentRoot"/>) so the two can never drift apart.</summary>
    public static string ContentWebRoot
    {
        get
        {
            var root = Services.ContentLocator.ContentRoot;
            // Empty = the probe couldn't resolve LocalApplicationData. Stay empty rather than
            // producing a RELATIVE path that CreateDirectory would honour next to the exe.
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "Resources", "web");
        }
    }

    /// <summary>The ccp.content mapping, with the folder created first — WebView2 SKIPS a mapping
    /// whose folder is missing (the same rule that forces ccp.spirals to be created before Launch).
    /// Allow, not Deny: the pages fetch()/decodeAudioData these files and route media elements
    /// through WebAudio from the ccp.game origin, both of which are CORS-checked — exactly why
    /// ccp.mod (a creator mod's audio, consumed the same way) is Allow too.</summary>
    public static (string, string, CoreWebView2HostResourceAccessKind) ContentMapping()
    {
        var root = ContentWebRoot;
        if (!string.IsNullOrEmpty(root))
        {
            try { Directory.CreateDirectory(root); }
            catch (Exception ex) { App.Logger?.Debug("ChaosWebViewHost: content dir create failed: {E}", ex.Message); }
        }
        return (ContentHost, root, CoreWebView2HostResourceAccessKind.Allow);
    }

    /// <summary>True when downloaded pack content is actually on disk (folder exists AND is not
    /// empty). Drives window.CCP_CONTENT_READY: false means "everything is still in the install
    /// dir", which is the legacy full-install case and the skipped-the-download case alike.</summary>
    private static bool HasPackContent()
    {
        try
        {
            var root = ContentWebRoot;
            return Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length > 0;
        }
        catch { return false; }
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
    private Button? _fsToggle;               // host chrome's fullscreen toggle (HostOwnedFullscreen)
    private bool _hasWindowedFrame;          // true once a real windowed frame has been captured
    private double _frameLeft, _frameTop, _frameW, _frameH;
    private bool _frameWasMaximized;

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
        if (_opts.HostOwnedFullscreen)
        {
            // Two rows: the host's strip, then the page. The strip is deliberately NOT hidden in
            // fullscreen - it is the exit, and an exit that disappears exactly when it is needed is
            // the bug this whole option exists to close.
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var chrome = BuildHostChrome();
            Grid.SetRow(chrome, 0);
            grid.Children.Add(chrome);
            Grid.SetRow(_web, 1);
        }
        grid.Children.Add(_web);

        // Default windowed size: 85% of the primary screen, centered. Options.WindowedWidth/Height
        // override it for hosts that want a fixed small frame (the Emergency Exit's 960x640 card).
        _windowedW = _opts.WindowedWidth is > 0 ? _opts.WindowedWidth.Value : SystemParameters.PrimaryScreenWidth * 0.85;
        _windowedH = _opts.WindowedHeight is > 0 ? _opts.WindowedHeight.Value : SystemParameters.PrimaryScreenHeight * 0.85;

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
        if (_opts.HostOwnedFullscreen) HookHostFullscreenInput();
        ApplyWindowMode(_opts.StartFullscreen);   // sets style / bounds / resize mode
        if (!_opts.InputEnabled)
            _window.SourceInitialized += (_, _) => ApplyPassiveExStyles(_window);
        _window.Show();
        _countedActive = true;
        System.Threading.Interlocked.Increment(ref _activeHostCount);
        if (_opts.OwnedByMainWindow) AttachMainWindowGlue();
        if (_opts.InputEnabled) { try { _window.Activate(); } catch (Exception ex) { Diag.Swallowed(ex); } }

        _ = InitWebAsync();
        App.Logger?.Information("{Tag}: window up (input={Input}, fullscreen={FS}) → {Url}",
            _opts.LogTag, _opts.InputEnabled, _isFullscreen, _opts.StartUrl);
    }

    /// <summary>Lay the window out as borderless-fullscreen or a normal titled window.</summary>
    private void ApplyWindowMode(bool fullscreen)
    {
        if (_window == null) return;
        if (_opts.HostOwnedFullscreen)
        {
            if (fullscreen) EnterHostFullscreen(); else LeaveHostFullscreen();
            _isFullscreen = fullscreen;
            UpdateChromeToggle();
            return;
        }
        if (fullscreen)
        {
            _window.WindowState = WindowState.Normal;   // manual bounds cover the whole screen (taskbar included)
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.Left = 0; _window.Top = 0;
            _window.Width = SystemParameters.PrimaryScreenWidth;
            _window.Height = SystemParameters.PrimaryScreenHeight;
        }
        else
        {
            _window.WindowStyle = WindowStyle.SingleBorderWindow;   // title bar = free Alt-Tab / minimize / move
            _window.ResizeMode = ResizeMode.CanResize;
            CenterDefaultWindowedBounds();
            _window.WindowState = WindowState.Normal;
        }
        _isFullscreen = fullscreen;
        // WindowStyle/ResizeMode churn re-stamps the frame; re-assert the owner link so a
        // fullscreen toggle can never silently unglue the window from main.
        RefreshNativeOwner();
    }

    /// <summary>The fallback windowed frame: 85% of the primary screen, centered. Used at launch
    /// and whenever fullscreen is left without a remembered frame to go back to.</summary>
    private void CenterDefaultWindowedBounds()
    {
        if (_window == null) return;
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
        double w = Math.Min(_windowedW, sw), h = Math.Min(_windowedH, sh);
        _window.Width = w; _window.Height = h;

        // Centre on MainWindow when the host asked for it AND main is actually a usable rectangle.
        // Everything else keeps the historic primary-screen centring.
        if (_opts.CenterOnMainWindow && TryCenterOnMainWindow(w, h)) return;

        _window.Left = Math.Max(0, (sw - w) / 2);
        _window.Top = Math.Max(0, (sh - h) / 2);
    }

    /// <summary>Place a w x h frame at the centre of MainWindow, clamped to the virtual desktop so a
    /// main window sitting half off-screen cannot push this one out of reach. Returns false (and
    /// touches nothing) when main is not a window we can measure, which is the caller's cue to fall
    /// back to primary-screen centring.</summary>
    private bool TryCenterOnMainWindow(double w, double h)
    {
        try
        {
            var main = Application.Current?.MainWindow;
            if (_window == null || main == null || !main.IsVisible) return false;
            if (main.WindowState == WindowState.Minimized) return false;

            double mw = main.ActualWidth > 0 ? main.ActualWidth : main.Width;
            double mh = main.ActualHeight > 0 ? main.ActualHeight : main.Height;
            if (double.IsNaN(mw) || double.IsNaN(mh) || mw <= 0 || mh <= 0) return false;

            double ml = main.Left, mt = main.Top;
            if (double.IsNaN(ml) || double.IsNaN(mt)) return false;
            if (main.WindowState == WindowState.Maximized) { ml = 0; mt = 0; mw = SystemParameters.PrimaryScreenWidth; mh = SystemParameters.PrimaryScreenHeight; }

            double left = ml + (mw - w) / 2;
            double top = mt + (mh - h) / 2;

            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            if (vw > 0 && vh > 0)
            {
                left = Math.Min(Math.Max(left, vl), vl + vw - w);
                top = Math.Min(Math.Max(top, vt), vt + vh - h);
            }

            _window.Left = left;
            _window.Top = top;
            return true;
        }
        catch { return false; }
    }

    /// <summary>True while the window is borderless-fullscreen (host-owned, not the browser API).</summary>
    public bool IsFullscreen => _isFullscreen;

    /// <summary>Toggle borderless fullscreen. The DtRH page drives this over the bridge
    /// (<c>fullscreen-set</c>) instead of the browser Fullscreen API, so Esc stays the game's
    /// to own; the passive tunnel backdrop still rides <c>ContainsFullScreenElementChanged</c>.
    /// Under <see cref="Options.HostOwnedFullscreen"/> the only callers are the chrome toggle and
    /// Esc - the page is not one of them.</summary>
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
        if (_opts.InputEnabled) { try { _window.Activate(); } catch (Exception ex) { Diag.Swallowed(ex); } }
    }

    // ======================= host-owned fullscreen (Options.HostOwnedFullscreen) =======================

    /// <summary>
    /// The strip the toggle lives in. Deliberately plain: a title on the left so the window still
    /// says what it is once the OS title bar is gone, and one accent pill on the right. It is host
    /// chrome, not shop UI - the doctrine that the desktop re-implements nothing about a drop is
    /// untouched by a window learning how to be a window.
    /// </summary>
    private UIElement BuildHostChrome()
    {
        var bar = new DockPanel { LastChildFill = true };

        _fsToggle = new Button
        {
            Style = TryTheme<Style>("SmallPinkButton"),
            Margin = new Thickness(8, 5, 10, 5),
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fsToggle.Click += (_, _) => SetFullscreen(!_isFullscreen);
        DockPanel.SetDock(_fsToggle, Dock.Right);
        bar.Children.Add(_fsToggle);

        bar.Children.Add(new TextBlock
        {
            Text = _opts.WindowTitle,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryTheme<Brush>("PinkBrush") ?? Brushes.HotPink,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return new Border
        {
            Background = TryTheme<Brush>("PanelBgBrush") ?? Brushes.Black,
            BorderBrush = TryTheme<Brush>("TransparentPink40Brush")
                          ?? TryTheme<Brush>("PinkBrush") ?? Brushes.HotPink,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    /// <summary>App-level theme lookup with a hard fallback. The host builds its chrome in code, so
    /// a missing key must degrade to a colour rather than throw a window away.</summary>
    private static T? TryTheme<T>(string key) where T : class
    {
        try { return Application.Current?.TryFindResource(key) as T; }
        catch { return null; }
    }

    /// <summary>Keep the toggle honest about which way it goes. Called from every window-mode
    /// change, so the label can never disagree with the window.</summary>
    private void UpdateChromeToggle()
    {
        if (_fsToggle == null) return;
        try
        {
            _fsToggle.Content = Localization.Loc.Get(_isFullscreen ? "btn_exit_fullscreen" : "btn_fullscreen");
            _fsToggle.ToolTip = Localization.Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back");
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.UpdateChromeToggle: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>
    /// Esc, route 1 of 2, plus the topmost rental.
    ///
    /// <para>This route only fires while WPF holds the keyboard - the toggle button just after it
    /// was clicked, or the strip. A focused WebView2 consumes the keystroke inside Chromium and the
    /// WPF tree never sees it, which is exactly why route 2 (the page-side listener injected in
    /// <see cref="InitWebAsync"/>) exists. The two cover disjoint focus states; neither is spare.</para>
    /// </summary>
    private void HookHostFullscreenInput()
    {
        if (_window == null) return;
        _window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_isFullscreen) return;
            SetFullscreen(false);
            e.Handled = true;
        };
        // TOPMOST IS RENTED, NOT OWNED - the #905 lesson from the fullscreen video window. Keeping
        // the claim after another window took focus is how a modal ends up alive and invisible
        // BEHIND the page, holding the input queue with no way to reach the button that dismisses
        // it. Dropped on deactivation, taken back on activation.
        _window.Deactivated += (_, _) => { try { if (_isFullscreen && _window != null) _window.Topmost = false; } catch (Exception ex) { Diag.Swallowed(ex); } };
        _window.Activated += (_, _) => { try { if (_isFullscreen && _window != null) _window.Topmost = true; } catch (Exception ex) { Diag.Swallowed(ex); } };
    }

    /// <summary>
    /// Borderless fullscreen that actually wins the argument with the taskbar, in the order the app
    /// already proved out in MainWindow.EnterBrowserFullscreen.
    ///
    /// <para><b>The trap.</b> Flipping WindowStyle while the window is already Maximized leaves
    /// Windows believing it is still a plain maximized window, so the taskbar keeps its z-order
    /// claim and paints OVER the "fullscreen" page - which is how the shop's own bottom-right exit
    /// ended up hidden behind it. Drop to Normal FIRST, take the frame off, let the render queue
    /// drain, then maximize. Topmost lands last: an owner-link re-slot inserts this window after a
    /// NON-topmost one, and SetWindowPos clears WS_EX_TOPMOST when it does.</para>
    /// </summary>
    private void EnterHostFullscreen()
    {
        if (_window == null) return;
        CaptureWindowedFrame();
        if (_window.WindowState != WindowState.Normal) _window.WindowState = WindowState.Normal;
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        RefreshNativeOwner();
        if (_window.IsVisible)
        {
            // Pump the render queue between the frame change and the maximize; skipping it is what
            // produces the mis-sized first frame on per-monitor-DPI displays.
            try { _window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
        _window.WindowState = WindowState.Maximized;
        _window.Topmost = true;
    }

    /// <summary>Back to the frame the user had. Un-maximize BEFORE the title bar returns, for the
    /// same reason the entry drops to Normal first: a style change under a maximized window leaves
    /// the frame and the state disagreeing about how big the window is.</summary>
    private void LeaveHostFullscreen()
    {
        if (_window == null) return;
        _window.Topmost = false;
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = WindowStyle.SingleBorderWindow;
        _window.ResizeMode = ResizeMode.CanResize;
        if (_hasWindowedFrame)
        {
            _window.Left = _frameLeft; _window.Top = _frameTop;
            _window.Width = _frameW; _window.Height = _frameH;
            if (_frameWasMaximized) _window.WindowState = WindowState.Maximized;
        }
        else
        {
            // Launched straight into fullscreen (the shelf replay): there is no prior frame to owe
            // the user, so the default centered one is the honest answer.
            CenterDefaultWindowedBounds();
        }
        RefreshNativeOwner();
    }

    /// <summary>Remember where the window was before fullscreen takes it. RestoreBounds is the only
    /// honest reading while maximized - Left/Top/Width/Height report the monitor, not the frame the
    /// user would expect back.</summary>
    private void CaptureWindowedFrame()
    {
        if (_window == null || !_window.IsVisible || _isFullscreen) return;
        try
        {
            _frameWasMaximized = _window.WindowState == WindowState.Maximized;
            var r = _frameWasMaximized
                ? _window.RestoreBounds
                : new Rect(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight);
            if (r.Width <= 0 || r.Height <= 0 || double.IsNaN(r.Left) || double.IsNaN(r.Top)) return;
            _frameLeft = r.Left; _frameTop = r.Top; _frameW = r.Width; _frameH = r.Height;
            _hasWindowedFrame = true;
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.CaptureWindowedFrame: {E}", _opts.LogTag, ex.Message); }
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
        catch (Exception ex) { Diag.Swallowed(ex); }
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
                catch (Exception ex) { Diag.Swallowed(ex); }
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
        if (_glueSuspended) { ApplyNativeOwner(false); return; }
        bool ownerDown = _glueOwner == null || _glueOwner.WindowState == WindowState.Minimized;
        ApplyNativeOwner(!ownerDown);
        // Main is minimized: nothing can bury us, so the link buys nothing and the cascade would
        // only take us down with it. Make sure we survived it.
        if (ownerDown) EnsureNativeVisible();
    }

    private bool _glueSuspended;

    /// <summary>Sever the owner link for as long as the caller needs the window to be immune to
    /// the OWNER's window-state changes, then re-apply it. Built for the For You ghost mode:
    /// while the feed window is parked off the virtual desktop, the link buys nothing (z-order
    /// is meaningless off-screen) and is the one path Show Desktop / Win+D still reaches the
    /// parked window through - minimizing the OWNER makes USER32 hide its owned windows, a
    /// hidden source stops being composed, and the DWM mirror freezes on its last frame.
    /// While suspended, main's StateChanged keeps firing RefreshNativeOwner - the guard there
    /// keeps the link severed until resume.</summary>
    public void SuspendMainWindowGlue(bool suspend)
    {
        _glueSuspended = suspend;
        if (!_glueAttached) return;
        if (suspend) ApplyNativeOwner(false);
        else RefreshNativeOwner();
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
            {
                // ...but "directly above main" is not high enough. An ATTACHED avatar tube is owned by
                // main too, so it is our SIBLING, and Win32 defines no order between two windows sharing
                // an owner — inserting directly above main drops us UNDER a tube already sitting there,
                // which is how the tube and this page interleaved into a torn half-and-half composite.
                // An attached tube is conceptually part of the main window (that is what "attached"
                // means), so it rides at main's level and we go above the pair, not between them.
                //
                // Ask the tube to drop to main's level FIRST. Slotting after the tube is not enough on
                // its own: if the tube is carrying WS_EX_TOPMOST (a Topmost pulse on main propagates to
                // its owned windows), inserting a non-topmost window "after" a topmost one only puts us
                // at the top of the NON-topmost band — still under the tube, which is how the tube ended
                // up floating over the Graded Intake window. SinkToMainZOrder self-marshals to the avatar
                // thread (never block main on it), so it lands right after our own insert below and wins.
                try { App.AvatarWindow?.SinkToMainZOrder(); } catch (Exception ex) { Diag.Swallowed(ex); }
                var insertAfter = _glueOwnerHandle;
                var tube = App.AvatarWindow?.AttachedHandleOrZero ?? IntPtr.Zero;
                if (tube != IntPtr.Zero && IsWindowVisible(tube)) insertAfter = tube;
                SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                // ...and that insert is precisely what would revoke a topmost claim: SetWindowPos
                // clears WS_EX_TOPMOST when it places a window after a NON-topmost one, so every
                // time main changed state a host-owned fullscreen window would quietly drop back
                // under the taskbar. Re-assert natively rather than through WPF's Topmost property,
                // which believes it is already true and would no-op.
                if (_window != null && _window.Topmost)
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
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
            // The swallow keeps USER32 from hiding the window, but the message may already have
            // been taken at face value elsewhere in the chain (WPF's own bookkeeping, the
            // WebView2 wrapper) on its way here. When that happens the WINDOW stays on screen
            // while CoreWebView2Controller.IsVisible drops underneath it: Chromium stops
            // producing frames, the page freezes on its last composed frame with document.hidden
            // stuck true (script and audio run on), and nothing ever flips it back because the
            // window never gets a real re-show. That stuck state is what the For You ghost
            // mirrored as a stale intro card (v6.9.0 report; main.js's re-mount in
            // setClickThrough could only repair the DOM half). Re-sync once the storm passes.
            try
            {
                _window?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => KickRenderVisibility("owner-minimize cascade vetoed")));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
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
            // The native re-show does not necessarily reach the WebView2 controller (the hide
            // may never have reached WPF either) — re-drive the render chain explicitly.
            try
            {
                _window.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => KickRenderVisibility("cascade hide healed")));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.EnsureNativeVisible: {E}", _opts.LogTag, ex.Message); }
    }

    /// <summary>Re-assert the WebView2 RENDER visibility chain for a window that is supposed to
    /// be composing — on screen, or parked-but-shown as the For You ghost's source window is.
    ///
    /// <para>Why it exists: a vetoed (or healed) owner-minimize cascade can leave the chain out
    /// of sync. USER32 says the window is visible, WPF says Visible, yet the wrapper has taken
    /// the WM_SHOWWINDOW hide at face value and dropped <c>CoreWebView2Controller.IsVisible</c>.
    /// Chromium then stops producing frames: the page freezes on its last composed frame,
    /// <c>document.hidden</c> sticks true, script and audio keep running — and because the window
    /// never gets a real re-show, no event ever restores the state. Live signature (v6.9.0 ghost
    /// report, 2026-08-31 log): the feed window frozen on the intro card from the moment main was
    /// minimized, remote fetches and XP still ticking behind it, ghost diag healthy
    /// (visible=true iconic=false cloaked=0).</para>
    ///
    /// <para>Two levers, belt and braces. First set the controller's IsVisible directly — the
    /// WPF control does not expose the controller, so this goes through reflection and is allowed
    /// to miss on a future SDK. Then bounce the ELEMENT's Visibility through a real
    /// Hidden→Visible transition so the wrapper itself re-pushes visibility into the controller,
    /// whichever direction it was stuck in. The bounce is synchronous (no frame is pumped between
    /// the two sets) and a no-op visually.</para>
    ///
    /// <para>Only kicks a window that is meant to be visible: a deliberate <c>Hide()</c> (tray)
    /// or a genuinely hidden native window is left alone.</para></summary>
    public void KickRenderVisibility(string reason)
    {
        try
        {
            if (_disposed || _window == null || _web == null) return;
            if (!_window.Dispatcher.CheckAccess())
            {
                try { _window.Dispatcher.BeginInvoke(new Action(() => KickRenderVisibility(reason))); } catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            if (_window.Visibility != Visibility.Visible) return;   // hidden on purpose (tray)
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return; // a real hide is not ours to undo

            bool? controllerWas = null;
            try
            {
                var t = _web.GetType();
                object? ctrl = t.GetProperty("CoreWebView2Controller",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public)?.GetValue(_web)
                    ?? t.GetField("_coreWebView2Controller",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(_web);
                if (ctrl is Microsoft.Web.WebView2.Core.CoreWebView2Controller c)
                {
                    controllerWas = c.IsVisible;
                    c.IsVisible = true;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "SDK internals moved, the element bounce below still re-drives the wrapper"); }

            bool elementWas = _web.IsVisible;
            _web.Visibility = Visibility.Hidden;
            _web.Visibility = Visibility.Visible;

            // Information on purpose: Serilog's floor is Information (App.xaml.cs), and this line
            // in an activity log is what turns the next frozen-feed report into a diagnosis —
            // controller=False here is the wedge caught red-handed.
            App.Logger?.Information(
                "{Tag}: render visibility kicked ({Reason}; controller={Ctrl}, element={El})",
                _opts.LogTag, reason, controllerWas?.ToString() ?? "n/a", elementWas);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.KickRenderVisibility: {E}", _opts.LogTag, ex.Message); }
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
        catch (Exception ex) { Diag.Swallowed(ex); }
        try
        {
            if (_glueHwndSource != null && _glueWndHook != null)
                _glueHwndSource.RemoveHook(_glueWndHook);
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        ApplyNativeOwner(false);
        _glueAttached = false;
        _glueOwner = null;
        _glueOwnerStateChanged = null;
        _glueOwnerHandle = IntPtr.Zero;
        _glueHwndSource = null;
        _glueWndHook = null;
    }

    /// <summary>Envelope the injected Esc listener posts. Namespaced so it can never collide with a
    /// message the hosted page sends on its own account.</summary>
    private const string HostEscapeMessageType = "ccp-host-escape";

    /// <summary>Capture-phase so a page that stops propagation on its own Esc handling still lets
    /// the host hear the key; guarded end to end because a page is free to have removed
    /// <c>chrome.webview</c> from under us.</summary>
    private const string EscapeBridgeScript =
        "(function(){try{window.addEventListener('keydown',function(e){" +
        "if(e.key!=='Escape'&&e.keyCode!==27)return;" +
        "try{window.chrome.webview.postMessage({type:'" + HostEscapeMessageType + "'});}catch(_){}" +
        "},true);}catch(_){}})();";

    /// <summary>
    /// Audio-output routing (#938, tester reports 0831): only pages served from the LOCAL virtual
    /// origin follow the user's chosen output device. Bureau / Just Drop navigate to REMOTE
    /// first-party sites, and granting mic permission to remote content is the exact trade the
    /// player-page fix (BrowserVideoSurface) refused for third-party origins - so they stay on the
    /// Windows default, same as before.
    /// </summary>
    private const string SinkRoutingHost = "ccp.game";

    internal static bool IsSinkRoutingHost(string? primaryHost) =>
        string.Equals(primaryHost, SinkRoutingHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Injected before any page script on every navigation. Two patches, one shared resolver:
    /// media elements get <c>setSinkId</c> the first time each one plays, and every
    /// <c>AudioContext</c> the page constructs is routed at creation (the Arcademy shell mixes
    /// EVERYTHING through a WebAudio graph, so the context - not the elements - is where its audio
    /// actually leaves the page). The device-id resolution is LAZY: silent pages never run the
    /// mic-permission probe at all.
    ///
    /// <para>Fail-safe contract, same as the player page: every failure path (unsupported API, no
    /// label match, setSinkId rejection, permission denied) changes nothing - audio stays on the
    /// Windows default and playback is never paused, muted, or delayed. Reporting rides the
    /// existing page-log bridge (info on the first successful route, warn on resolver failure).</para>
    /// </summary>
    private const string SinkRoutingScriptTemplate = """
        (function () {
          'use strict';
          var WANTED = __CCP_SINK_LABEL__;
          if (typeof WANTED !== 'string' || !WANTED.trim()) return;
          WANTED = WANTED.trim();
          var md = navigator.mediaDevices;
          function report(ok, detail) {
            try {
              window.chrome.webview.postMessage({ type: 'log', level: ok ? 'info' : 'warn',
                msg: ok ? ('audio routed to output device "' + WANTED + '" via setSinkId (' + detail + ')')
                        : ('could not route audio to "' + WANTED + '" (' + detail + ') - staying on the Windows default output') });
            } catch (e) { /* bridge absent - stay silent, stay default */ }
          }
          if (!md || typeof md.enumerateDevices !== 'function') return;
          function norm(s) { return (s || '').trim().toLowerCase(); }
          function pick(devices) {
            var w = norm(WANTED);
            var outs = devices.filter(function (d) {
              return d.kind === 'audiooutput' && d.deviceId && d.deviceId !== 'default' && d.deviceId !== 'communications';
            });
            var hit = outs.find(function (d) { return norm(d.label) === w; });
            if (hit) return hit;
            var open = w.indexOf('(');
            var close = w.lastIndexOf(')');
            var driver = open >= 0 && close > open ? w.substring(open + 1, close).trim() : w;
            hit = outs.find(function (d) {
              var l = norm(d.label);
              return !!l && (l.indexOf(driver) === 0 || driver.indexOf(l) === 0 || l.indexOf(driver) >= 0 || w.indexOf(l) >= 0);
            });
            return hit || null;
          }
          var idPromise = null;
          function resolveId() {
            if (idPromise) return idPromise;
            idPromise = md.enumerateDevices()
              .then(function (ds) {
                if (ds.some(function (d) { return d.kind === 'audiooutput' && d.label; })) return ds;
                // Labels are blank until the origin holds mic permission; the host auto-grants it
                // for this origin alone (OnPermissionRequested). Stop the probe stream at once -
                // nothing records anything.
                return md.getUserMedia({ audio: true }).then(function (stream) {
                  stream.getTracks().forEach(function (t) { try { t.stop(); } catch (e) { } });
                  return md.enumerateDevices();
                });
              })
              .then(function (ds) {
                var hit = pick(ds);
                if (!hit) { report(false, 'no audiooutput label matched'); return null; }
                return hit.deviceId;
              })
              .catch(function (err) {
                report(false, (err && (err.name || err.message)) || 'error');
                return null;
              });
            return idPromise;
          }
          var reported = false;
          function route(target, kind) {
            try {
              if (!target || typeof target.setSinkId !== 'function') return;
              resolveId().then(function (id) {
                if (!id) return;
                return Promise.resolve(target.setSinkId(id)).then(function () {
                  if (!reported) { reported = true; report(true, kind); }
                });
              }).catch(function (e) { /* stay on default */ });
            } catch (e) { /* stay on default */ }
          }
          try {
            var origPlay = HTMLMediaElement.prototype.play;
            HTMLMediaElement.prototype.play = function () {
              try {
                if (!this.__ccpSinkDone) { this.__ccpSinkDone = true; route(this, 'media element'); }
              } catch (e) { }
              return origPlay.apply(this, arguments);
            };
          } catch (e) { }
          try {
            if (window.AudioContext && AudioContext.prototype && typeof AudioContext.prototype.setSinkId === 'function') {
              var OrigCtx = window.AudioContext;
              var Patched = class extends OrigCtx {
                constructor() { super(...arguments); route(this, 'AudioContext'); }
              };
              if (window.webkitAudioContext === OrigCtx) window.webkitAudioContext = Patched;
              window.AudioContext = Patched;
            }
          } catch (e) { }
        })();
        """;

    /// <summary>Label is embedded as a JSON string literal, so a device name full of quotes or
    /// backslashes can never break out of the script.</summary>
    internal static string BuildSinkRoutingScript(string label) =>
        SinkRoutingScriptTemplate.Replace("__CCP_SINK_LABEL__",
            Newtonsoft.Json.JsonConvert.SerializeObject(label));

    /// <summary>
    /// Chromium blanks audiooutput labels until the origin holds microphone permission, so the
    /// injected resolver's one-shot probe surfaces here. Grant is scoped to exactly our own local
    /// pages: https scheme AND <see cref="SinkRoutingHost"/> (which is also what navigation is
    /// locked to). Any OTHER origin asking for the mic is denied outright. Every other permission
    /// kind is left untouched (<c>Handled</c> stays false), so this handler cannot change how any
    /// existing page behaves.
    /// </summary>
    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        try
        {
            if (e.PermissionKind != CoreWebView2PermissionKind.Microphone) return;
            bool ourPage = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.Equals(uri.Host, SinkRoutingHost, StringComparison.OrdinalIgnoreCase);
            e.State = ourPage ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("{Tag}: PermissionRequested handler failed: {E}", _opts.LogTag, ex.Message);
        }
    }

    /// <summary>
    /// WHO ANSWERS <c>prefers-reduced-motion</c> FOR A PAGE THIS APP HOSTS (ccp-bugs #980).
    ///
    /// <para><b>What broke.</b> Just Drop played as a set of still images: the spiral did not turn,
    /// the media never rotated on its interval, and only re-opening the session's own settings
    /// panel produced a single visible change before it froze again. Windowed or fullscreen, the
    /// same. That is not a stalled host or a dead network - it is the web player taking its
    /// reduced-motion path. Under <c>prefers-reduced-motion: reduce</c> it paints every canvas
    /// layer at a FROZEN clock (a fixed t) and serves the media Window one still poster that never
    /// advances, and re-opening its gear panel remounts the layers, which bakes one fresh still.
    /// Every symptom in the report, exactly.</para>
    ///
    /// <para><b>Why Chromium said "reduce".</b> Blink maps that media query on Windows to
    /// <c>SPI_GETCLIENTAREAANIMATION</c> - the "Animation effects" checkbox in Windows Settings.
    /// That is the very same flag <see cref="Services.MotionFx"/> reads through
    /// <c>SystemParameters.ClientAreaAnimation</c>, so this app already knows it has users running
    /// with it off: they are the ones MotionFx caps to <c>MotionLevel.Reduced</c>.</para>
    ///
    /// <para><b>Why that answer is wrong here.</b> In this app the reduced-motion gate governs
    /// CHROME - hover lifts, ambient loops, entrance staggers, tier livery. It has never governed
    /// CONTENT: no service under Services/ consults MotionFx, so flashes, spirals, subliminals and
    /// overlays all run at full motion whatever Windows says, because that content is the thing the
    /// user asked for. A page in one of these hosts is content too, and a browser engine cannot
    /// tell the two apart - so the host has to answer for it. That is already the doctrine one
    /// layer up: <c>Controls/SpiralEmbedView</c> hands its embed an explicit <c>reduced_motion</c>
    /// over the bridge rather than letting the page guess. A remote page we do not own has no such
    /// bridge, so we set the media query itself.</para>
    ///
    /// <para><b>The one preference that is still forwarded</b> is <c>MotionLevel.Off</c> - the
    /// user's explicit, in-app "no animation at all". An OS checkbox they ticked for Explorer is
    /// not that, and must not silently turn a session they ordered into a photograph.</para>
    ///
    /// <para>An older Chromium that does not know the switch ignores it, which is the pre-fix
    /// behaviour - there is no version floor to guard.</para>
    /// </summary>
    private string PrefersReducedMotionArgument()
    {
        var setting = Models.MotionLevel.Full;
        try { setting = App.Settings?.Current?.MotionLevel ?? Models.MotionLevel.Full; }
        catch (Exception ex) { Diag.Swallowed(ex, "no settings yet, so the default stands"); }
        var arg = PrefersReducedMotionArgument(setting);

        // THE BREADCRUMB THAT SETTLES THE NEXT REPORT, at Information because Serilog's floor is
        // Information (App.xaml.cs) and a Debug line can never reach a user's bug report. It is
        // written ONLY when the override actually changed something - the OS flag was off and we
        // overruled it - so it is silent for everyone whose Windows animations are on, and its
        // presence in an activity log is the confirmation that this machine is one of the ones
        // #980 was about.
        try
        {
            if (!SystemParameters.ClientAreaAnimation && setting != Models.MotionLevel.Off)
                App.Logger?.Information(
                    "{Tag}: Windows animation effects are OFF; hosted page is kept in motion anyway ({Arg}) - #980",
                    _opts.LogTag, arg);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "the flag is unreadable on some sessions, the argument still stands"); }

        return arg;
    }

    /// <summary>The pure half of <see cref="PrefersReducedMotionArgument()"/>, split out the same
    /// way <see cref="Services.MotionFx.ResolveLevel"/> is so the rule can be tested without an
    /// App. Note what is NOT a parameter: the OS animation flag. Reduced is not Off - a user who
    /// asked for calmer CHROME did not ask for a session that never moves.</summary>
    internal static string PrefersReducedMotionArgument(Models.MotionLevel setting)
        => setting == Models.MotionLevel.Off
            ? "--force-prefers-reduced-motion"
            : "--force-prefers-no-reduced-motion";

    /// <summary>
    /// Join the host's own switches to a caller's <see cref="Options.ExtraBrowserArguments"/> so the
    /// command line carries each Chromium FEATURE switch exactly once.
    ///
    /// <para>Chromium keys switches by NAME (base::CommandLine holds a map), so a command line that
    /// names --disable-features twice keeps only the LAST occurrence and silently drops every value
    /// in the others. The host and its callers both have a stake in that switch - the For You ghost
    /// mirror lives or dies on CalculateNativeWinOcclusion being off, and a parked window Chromium
    /// calls hidden takes the whole feed down with it - and appending a second copy costs nothing
    /// today only because both copies happen to carry the same value. Merge instead: one
    /// comma-joined switch per feature list, first-seen order, duplicates dropped.</para>
    ///
    /// <para>Everything else is passed through untouched (only exact repeats collapse), so a switch
    /// Chromium already resolves last-wins keeps resolving exactly as it does today.</para>
    /// </summary>
    internal static string ComposeBrowserArguments(string? hostArgs, string? extra)
    {
        var featureNames = new List<string>();
        var featureValues = new List<List<string>>();
        var plain = new List<string>();
        var tokens = $"{hostArgs} {extra}".Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            int eq = token.IndexOf('=');
            var name = eq > 0 ? token[..eq] : token;
            if (eq > 0 && (name == "--disable-features" || name == "--enable-features"))
            {
                int slot = featureNames.IndexOf(name);
                if (slot < 0) { featureNames.Add(name); featureValues.Add(new List<string>()); slot = featureNames.Count - 1; }
                foreach (var feature in token[(eq + 1)..].Split(',',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!featureValues[slot].Contains(feature, StringComparer.Ordinal))
                        featureValues[slot].Add(feature);
                continue;
            }
            if (!plain.Contains(token, StringComparer.Ordinal)) plain.Add(token);
        }
        for (int i = 0; i < featureNames.Count; i++)
            if (featureValues[i].Count > 0)
                plain.Add($"{featureNames[i]}={string.Join(',', featureValues[i])}");
        return string.Join(' ', plain);
    }

    private async Task InitWebAsync()
    {
        if (_initStarted || _web == null) return;
        _initStarted = true;
        try
        {
            var userDataFolder = Path.Combine(App.UserDataPath, _opts.UserDataFolderName);
            Directory.CreateDirectory(userDataFolder);

            // --disable-direct-composition-video-overlays: keep the WebGL swapchain composited
            // through DWM (the app's established anti-MPO flag). CalculateNativeWinOcclusion:
            // native payload windows stack over the page; Chromium's occlusion tracker would
            // decide the page is covered and throttle rAF — turn it off.
            var args = ComposeBrowserArguments(
                "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion "
                + PrefersReducedMotionArgument(),
                _opts.ExtraBrowserArguments);
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

            // Tell a page that maps ccp.content whether pack audio is actually there, BEFORE its
            // first script runs: the audio shims read window.CCP_CONTENT_READY to decide which host
            // to try first (they still fall back to the other one per file, so a wrong guess costs
            // one 404, never a missing sound). Pages without the mapping never see the flag.
            bool mapsContent = false;
            foreach (var (host, _, _) in _opts.Mappings)
                if (string.Equals(host, ContentHost, StringComparison.OrdinalIgnoreCase)) { mapsContent = true; break; }
            if (mapsContent)
            {
                var ready = HasPackContent() ? "true" : "false";
                try
                {
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(
                        "window.CCP_CONTENT_READY = " + ready + ";").ConfigureAwait(true);
                    if (_disposed) return;
                }
                catch (Exception ex)
                {
                    // Not fatal: the shims default to the install-dir host when the flag is absent.
                    App.Logger?.Debug("{Tag}: CCP_CONTENT_READY inject failed: {E}", _opts.LogTag, ex.Message);
                }
            }

            // Esc, route 2 of 2 (see HookHostFullscreenInput). This one runs INSIDE the page, which
            // is the focus state that matters in practice: the user is looking at the session and
            // the WebView2 owns the keyboard. It reports the keystroke and nothing else - no
            // preventDefault, no stopPropagation - because what Esc means to the page is the page's
            // business, and a host that swallowed it would break the page's own back-out.
            if (_opts.HostOwnedFullscreen)
            {
                try
                {
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(EscapeBridgeScript).ConfigureAwait(true);
                    if (_disposed) return;
                }
                catch (Exception ex)
                {
                    // Not fatal: the WPF route and the visible toggle are both still there.
                    App.Logger?.Debug("{Tag}: esc bridge inject failed: {E}", _opts.LogTag, ex.Message);
                }
            }

            // Audio-output routing (#938): tester reports 0831 - the Arcademy (and every other
            // hosted page) always played on the Windows default. Local first-party pages only;
            // resolved once at host creation, same as every other option on this window.
            var sinkLabel = IsSinkRoutingHost(_opts.PrimaryHost)
                ? Services.Video.Browser.BrowserSinkLabel.Resolve() : null;
            if (sinkLabel != null)
            {
                try
                {
                    core.PermissionRequested += OnPermissionRequested;
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(
                        BuildSinkRoutingScript(sinkLabel)).ConfigureAwait(true);
                    if (_disposed) return;
                }
                catch (Exception ex)
                {
                    // Not fatal: audio simply stays on the Windows default, as it always has.
                    App.Logger?.Debug("{Tag}: sink routing inject failed: {E}", _opts.LogTag, ex.Message);
                }
            }

            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            // The page's Fullscreen API (the dock's [ ] button) drives the WPF window: entering
            // element-fullscreen borderless-maximizes the host, exiting restores the titled window.
            core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;

            // Last seam before the first byte is requested - see Options.OnCoreCreated.
            try { _opts.OnCoreCreated?.Invoke(core); }
            catch (Exception ex) { App.Logger?.Warning("{Tag}: OnCoreCreated threw: {E}", _opts.LogTag, ex.Message); }

            core.Navigate(_opts.StartUrl);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("{Tag}.InitWebAsync failed: {E}", _opts.LogTag, ex.Message);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Only ever our own page. Block anything else (defence in depth).
        if (string.IsNullOrEmpty(e.Uri)) { e.Cancel = true; return; }
        if (IsAllowedNavigationHost(e.Uri, _opts.PrimaryHost)) return;
        foreach (var host in _opts.AdditionalNavigationHosts)
            if (IsAllowedNavigationHost(e.Uri, host)) return;
        App.Logger?.Debug("{Tag}: blocked navigation to {Uri}", _opts.LogTag, e.Uri);
        e.Cancel = true;
    }

    /// <summary>
    /// Prefix match on "https://host/", which is exact rather than a substring test: the trailing
    /// slash is what stops "https://ccp.game.evil.com/" from passing as ccp.game. A bare
    /// "https://host" with no path is accepted too, since that is a legal way to name the root.
    /// </summary>
    private static bool IsAllowedNavigationHost(string uri, string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var origin = "https://" + host;
        if (uri.Equals(origin, StringComparison.OrdinalIgnoreCase)) return true;
        return uri.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase);
    }

    private void OnContainsFullScreenElementChanged(object? sender, object e)
    {
        try
        {
            var core = _web?.CoreWebView2;
            if (core == null) return;
            bool fs = core.ContainsFullScreenElement;
            if (_opts.HostOwnedFullscreen)
            {
                // The element still goes fullscreen - inside the WebView2, filling the client area.
                // What it no longer does is take the WINDOW with it. A page that could strip the
                // title bar off its own host is a page that can trap the user, and this one is
                // remote: it takes fullscreen the moment an order opens.
                App.Logger?.Debug("{Tag}: page fullscreen={FS}; window stays host-owned", _opts.LogTag, fs);
                return;
            }
            _window?.Dispatcher.Invoke(() => SetFullscreen(fs));
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.FullScreenChanged: {E}", _opts.LogTag, ex.Message); }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.Logger?.Warning("{Tag}: WebView2 process failed ({Kind})", _opts.LogTag, e.ProcessFailedKind);
        try { _opts.OnProcessFailed?.Invoke(e.ProcessFailedKind); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>
    /// Map a page <c>{type:'log'}</c> envelope's <c>level</c> field onto a Serilog level.
    ///
    /// <para>The distinction that carries the weight here is ABSENT versus DECLARED. The global
    /// logger floor is Information and it is set unconditionally, in every build (App.OnStartup),
    /// so a message routed to Debug is not demoted - it is DROPPED, and page logs are the only
    /// devtools-less window a hosted surface has. Six of the ten bridges (dtrh, m2test, tunnel,
    /// goon, intake web-shim, player) send no level field at all, so an absent field has to keep
    /// meaning Information or those surfaces go dark. A page that declares 'debug' is opting out
    /// on purpose: that is how the Arcademy's chatter goes quiet without silencing anybody
    /// else.</para>
    ///
    /// <para>Case- and whitespace-insensitive. Unknown junk reads as chatter, never as loud.</para>
    /// </summary>
    internal static Serilog.Events.LogEventLevel PageLogLevel(string? level)
    {
        // Absent or blank: the legacy contract, kept verbatim for the bridges that predate the
        // level field entirely. Never route this to Debug: it is where six live surfaces sit.
        if (string.IsNullOrWhiteSpace(level)) return Serilog.Events.LogEventLevel.Information;
        return level.Trim().ToLowerInvariant() switch
        {
            "error" or "fatal" => Serilog.Events.LogEventLevel.Error,
            "warn" or "warning" => Serilog.Events.LogEventLevel.Warning,
            "info" or "information" => Serilog.Events.LogEventLevel.Information,
            _ => Serilog.Events.LogEventLevel.Debug,
        };
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
                case HostEscapeMessageType:
                    // Host chrome, not page business: consumed here rather than forwarded, so a
                    // keystroke the host injected the listener for never reaches OnMessage as an
                    // unknown envelope.
                    if (_opts.HostOwnedFullscreen && _isFullscreen) SetFullscreen(false);
                    break;
                case "log":
                    // Honour the page's own `level`. This used to write EVERY page log at
                    // Information because that was the global logger floor and page logs were the
                    // only devtools-less window into the page - fine for the tunnel's handful of
                    // sites, ruinous once the Arcademy landed with 118 of them behind one funnel
                    // and buried the real log under class chatter. Debug means DROPPED here, not
                    // demoted (the floor is Information in every build), so the router only
                    // silences a page that asked for it: say nothing and you are still heard.
                    App.Logger?.Write(PageLogLevel((string?)o["level"]),
                        "{Tag}[page]: {Msg}", _opts.LogTag, (string?)o["msg"]);
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
            try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch (Exception ex) { Diag.Swallowed(ex); }
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
                _web.CoreWebView2.PermissionRequested -= OnPermissionRequested;
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        try { DetachMainWindowGlue(); } catch (Exception ex) { Diag.Swallowed(ex); }
        try { _web?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        try { _window?.Close(); } catch (Exception ex) { Diag.Swallowed(ex); }
        if (_countedActive) { _countedActive = false; System.Threading.Interlocked.Decrement(ref _activeHostCount); }
        _web = null; _window = null; IsReady = false; _pending.Clear();
    }

    // How many game hosts (Bureau / Graded Intake / DtRH) currently have a window up. The ATTACHED
    // avatar tube consults this before its focus-stealing raise: an attached tube rides at main's
    // level by definition, so it must not lift itself over a game page the user is working in.
    private static int _activeHostCount;
    private bool _countedActive;
    internal static bool AnyHostActive => System.Threading.Volatile.Read(ref _activeHostCount) > 0;

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
        catch (Exception ex) { Diag.Swallowed(ex); }
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
    // A z-order SetWindowPos on this host (owned by main, insertAfter possibly the avatar-thread
    // tube) must never let USER32 reposition the OWNER too — that sends WM_WINDOWPOSCHANGING
    // synchronously into main, one half of the mixed-DPI drag deadlock cycle.
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
