using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The window a leash video plays in (owner, 2026-09-26). Big by default (92% of the work area),
/// resizable, and fullscreen on F11 or its button. The video fills the whole page and cannot be
/// clicked, paused, sought or put into the page's own fullscreen: a shield covers it, every key
/// and click is eaten in the page, the Fullscreen API is a no-op, a paused video plays again,
/// and navigation off the page is refused. Hypnotube pages and local files (a plain page this
/// window writes) get the same cage.
/// A PUNISHMENT window cannot be closed: a close shakes it and says whose punishment it is. It
/// ends when the video ends or its time cap is reached (the runner decides), when the leash is
/// cut, or on a panic press, which always works. A TASK window (a video assignment) closes
/// normally. The way out is told up front in the explainer, so there is no cut button here.
/// </summary>
internal sealed class LeashPunishWindow : Window
{
    public static LeashPunishWindow? Current { get; private set; }

    /// <summary>The page's video reached its end.</summary>
    public static event Action? VideoEnded;

    private const string LocalHost = "leash-video.ccp";

    private readonly bool _locked;
    private readonly string _holder;
    private readonly TextBlock _title;
    private readonly Border _head;
    private readonly Grid _root;
    private WebView2? _web;
    private bool _allowClose;
    private bool _fullscreen;
    private WindowState _prevState;
    private string? _pageUrl;
    private DispatcherTimer? _refuseTimer;
    private DispatcherTimer? _shakeTimer;

    public WebView2? View => _web;

    /// <summary>True while the window is up and not minimised (only then does watching count).</summary>
    public static bool Watchable
    {
        get
        {
            var w = Current;
            return w != null && w.IsVisible && w.WindowState != WindowState.Minimized;
        }
    }

    private LeashPunishWindow(bool locked, string holder)
    {
        _locked = locked;
        _holder = string.IsNullOrWhiteSpace(holder) ? "?" : holder.Trim();

        Title = TitleText();
        Background = Brushes.Black;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var wa = SystemParameters.WorkArea;
        Width = Math.Max(640, wa.Width * 0.92);
        Height = Math.Max(400, wa.Height * 0.92);
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid { Margin = new Thickness(14, 6, 8, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title = FriendsLook.Label(TitleText(), 13.5, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold);
        head.Children.Add(_title);
        var fs = FriendsLook.Pill(Loc.Get("leash_punish_fullscreen"), FriendsLook.ButtonBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 8, new Thickness(10, 3, 10, 3), FriendsLook.ButtonHoverBrush);
        fs.ToolTip = "F11";
        fs.Focusable = false;
        fs.Click += (_, _) => ToggleFullscreen();
        Grid.SetColumn(fs, 1);
        head.Children.Add(fs);
        _head = new Border { Background = FriendsLook.HeadBrush, Child = head };
        _root.Children.Add(_head);

        Content = _root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; } };
        try { if (Application.Current != null) Application.Current.SessionEnding += (_, _) => _allowClose = true; } catch { }
    }

    private string TitleText() => Loc.GetF(_locked ? "leash_punish_title" : "leash_task_title", _holder);

    // ---- open / close ------------------------------------------------------------------------

    /// <summary>Open a web page (a Hypnotube video) in the cage. Replaces any open one.</summary>
    public static bool OpenUrl(string url, string holder, bool locked)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var w = Show(holder, locked);
        w._pageUrl = u.AbsoluteUri;
        _ = w.InitAsync(core => core.Navigate(u.AbsoluteUri));
        return true;
    }

    /// <summary>Open a local video file on a plain page in the cage.</summary>
    public static bool OpenFile(string path, string holder, bool locked)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var src = "https://" + LocalHost + "/" + Uri.EscapeDataString(Path.GetFileName(path));
        var w = Show(holder, locked);
        w._pageUrl = null;
        _ = w.InitAsync(core =>
        {
            core.SetVirtualHostNameToFolderMapping(LocalHost, dir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigateToString(LocalPage(src));
        });
        return true;
    }

    /// <summary>Close without a fuss: the video is done, the leash is off, or panic.</summary>
    public static void CloseNow()
    {
        var w = Current;
        if (w == null) return;
        w._allowClose = true;
        try { w.Close(); } catch { }
    }

    private static LeashPunishWindow Show(string holder, bool locked)
    {
        CloseNow();
        var w = new LeashPunishWindow(locked, holder);
        Current = w;
        w.Show();
        w.Activate();
        return w;
    }

    private static string LocalPage(string src) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0;height:100%;background:#000;overflow:hidden}"
        + "video{width:100vw;height:100vh;object-fit:contain;background:#000}</style></head><body>"
        + "<video autoplay playsinline src=\"" + WebUtility.HtmlEncode(src) + "\"></video></body></html>";

    // ---- the browser -------------------------------------------------------------------------

    private async System.Threading.Tasks.Task InitAsync(Action<CoreWebView2> go)
    {
        try
        {
            _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
            Grid.SetRow(_web, 1);
            _root.Children.Add(_web);

            var env = await CreateEnvironmentAsync();
            if (_allowClose || !IsLoaded && !IsVisible) return;
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            if (core == null) return;

            var s = core.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;

            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += (_, e) =>
            {
                string? msg = null;
                try { msg = e.TryGetWebMessageAsString(); } catch { }
                if (msg == "leash-ended") VideoEnded?.Invoke();
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(CageScript);
            go(core);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Leash video window failed: {E}", ex.Message);
        }
    }

    /// <summary>The panel browser's own profile (its cookies carry the Hypnotube age check) with
    /// the same arguments, so both share one browser; a clash falls back to a profile of its own.</summary>
    private static async System.Threading.Tasks.Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-direct-composition-video-overlays",
        };
        try
        {
            var shared = Path.Combine(App.UserDataPath, "browser_data");
            Directory.CreateDirectory(shared);
            return await CoreWebView2Environment.CreateAsync(null, shared, options);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Leash video window: shared profile refused ({E}), using its own", ex.Message);
            var own = Path.Combine(App.UserDataPath, "browser_data_leash");
            Directory.CreateDirectory(own);
            return await CoreWebView2Environment.CreateAsync(null, own, options);
        }
    }

    /// <summary>The first load goes through; after it, only the same page (a reload) may load.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_pageUrl == null) return;
        if (AppLeashTaskHost.SamePage(e.Uri, _pageUrl)) return;
        e.Cancel = true;
    }

    // ---- fullscreen, close, shake ------------------------------------------------------------

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevState = WindowState;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _fullscreen = true;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _prevState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _fullscreen = false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_locked && !_allowClose)
        {
            e.Cancel = true;
            Refuse();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(Current, this)) Current = null;
        _refuseTimer?.Stop();
        _shakeTimer?.Stop();
        try { _web?.Dispose(); } catch { }
        _web = null;
    }

    /// <summary>A close on a punishment: the window shakes and the head says whose it is and how
    /// it ends, for a few seconds.</summary>
    private void Refuse()
    {
        _title.Text = Loc.GetF("leash_punish_no_close", _holder) + "  " +
            Loc.GetF("leash_punish_hold", LeashHoldToCut.KeyLabel(App.Settings?.Current?.PanicKey));
        _title.Foreground = FriendsLook.GoldBrush;
        _refuseTimer?.Stop();
        _refuseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refuseTimer.Tick += (_, _) =>
        {
            _refuseTimer?.Stop();
            _title.Text = TitleText();
            _title.Foreground = FriendsLook.TextBrush;
        };
        _refuseTimer.Start();
        LeashFx.Denied();
        if (MotionFx.AllowTransitions) Shake();
    }

    /// <summary>Moves the real window (a WebView2 does not follow a render transform), so it also
    /// shakes maximised; lands back exactly where it was.</summary>
    private void Shake()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
        _shakeTimer?.Stop();
        var start = DateTime.UtcNow;
        const double ms = 420;
        var amp = MotionFx.Level == ConditioningControlPanel.Models.MotionLevel.Reduced ? 7.0 : 16.0;
        _shakeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _shakeTimer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds;
            var dx = t >= ms ? 0 : (int)Math.Round(amp * (1 - t / ms) * Math.Sin(t / ms * Math.PI * 9));
            SetWindowPos(hwnd, IntPtr.Zero, r.Left + dx, r.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            if (t >= ms) _shakeTimer?.Stop();
        };
        _shakeTimer.Start();
    }

    /// <summary>Runs in every document before its own scripts.</summary>
    private const string CageScript = @"(function(){
  if (window.__leashCage) return; window.__leashCage = 1;
  var stop = function(e){ e.preventDefault(); e.stopImmediatePropagation(); };
  try {
    var no = function(){ return Promise.resolve(); };
    Element.prototype.requestFullscreen = no;
    Element.prototype.webkitRequestFullscreen = no;
    HTMLVideoElement.prototype.webkitEnterFullscreen = function(){};
    HTMLVideoElement.prototype.requestPictureInPicture = function(){ return Promise.reject(); };
  } catch (e) {}
  ['keydown','keyup','keypress','contextmenu','dblclick','auxclick','wheel'].forEach(function(t){ window.addEventListener(t, stop, true); });
  var css = 'html,body{overflow:hidden!important;background:#000!important}'
    + 'video.leash-cage{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;'
    + 'max-width:none!important;max-height:none!important;margin:0!important;transform:none!important;'
    + 'z-index:2147483646!important;background:#000!important;object-fit:contain!important;visibility:visible!important;opacity:1!important}'
    + '#leash-shield{position:fixed;left:0;top:0;width:100vw;height:100vh;z-index:2147483647;background:transparent;cursor:none}';
  var style = document.createElement('style'); style.textContent = css;
  var v = null, ended = false;
  var pick = function(){
    var best = null, score = -1;
    document.querySelectorAll('video').forEach(function(x){
      var r = x.getBoundingClientRect();
      var s = (r.width * r.height) + (isFinite(x.duration) ? x.duration * 1000 : 0);
      if (s > score) { best = x; score = s; }
    });
    return best;
  };
  var free = function(el){
    for (var p = el.parentElement; p && p !== document.documentElement; p = p.parentElement) {
      p.style.setProperty('transform', 'none', 'important');
      p.style.setProperty('filter', 'none', 'important');
      p.style.setProperty('contain', 'none', 'important');
      p.style.setProperty('perspective', 'none', 'important');
      p.style.setProperty('z-index', '2147483646', 'important');
    }
  };
  var tick = function(){
    var host = document.head || document.documentElement;
    if (host && !style.isConnected) host.appendChild(style);
    if (document.body && !document.getElementById('leash-shield')) {
      var s = document.createElement('div'); s.id = 'leash-shield';
      ['mousedown','mouseup','click','pointerdown','pointerup','touchstart','touchend'].forEach(function(t){ s.addEventListener(t, stop, true); });
      document.body.appendChild(s);
    }
    var nv = pick();
    if (nv && nv !== v) {
      v = nv;
      v.addEventListener('ended', function(){ ended = true; try { chrome.webview.postMessage('leash-ended'); } catch (e) {} });
    }
    if (v) {
      v.classList.add('leash-cage'); free(v);
      v.controls = false; v.loop = false;
      if (v.muted) v.muted = false;
      if (v.paused && !ended) { var p = v.play(); if (p && p.catch) p.catch(function(){}); }
    }
    if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(function(){});
  };
  try {
    ['pause','stop','seekbackward','seekforward','seekto','previoustrack','nexttrack'].forEach(function(a){
      try { navigator.mediaSession.setActionHandler(a, function(){ if (v && !ended) v.play(); }); } catch (e) {}
    });
  } catch (e) {}
  document.addEventListener('DOMContentLoaded', tick);
  setInterval(tick, 400);
})();";

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
