// PORTED from WPF 7.1.5 Controls/Leash/LeashPunishWindow.cs: the window a leash video plays in.
// Big by default (92% of the work area), resizable, fullscreen on F11 or its button. The video
// fills the page and cannot be clicked, paused, sought or put into the page's own fullscreen: the
// cage script (WPF CageScript, verbatim) puts a shield over it, eats keys and clicks, no-ops the
// Fullscreen API, plays a paused video again; navigation off the page is refused.
// A PUNISHMENT window cannot be closed: a close shakes it and says whose punishment it is. It ends
// when the video ends or its cap is reached (the runner decides), when the leash is cut, or on a
// panic press, which always works (the runner / panic path calls CloseNow). A TASK window closes
// normally. The way out sits in the head from the first frame: an always-enabled Cut leash button
// and the "hold the panic key 5 s" line.
// Head differences (WebHost = Avalonia NativeWebView): no document-created script injection, so the
// cage is injected on every NavigationCompleted and re-injected each second (idempotent); a local
// file plays from a small page written beside the user data (no virtual host mapping); no HTTP
// status on NavigationCompleted, so only a missing engine or the page's media error raise
// PlaybackFailed; the browser profile is WebHost's own.
using System;
using System.IO;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashPunishWindow : Window
{
    public static LeashPunishWindow? Current { get; private set; }

    /// <summary>The page's video reached its end.</summary>
    public static event Action? VideoEnded;

    /// <summary>The page could not load or the local video would not decode.</summary>
    public static event Action? PlaybackFailed;

    /// <summary>Test seam: false builds the cage without a web view (headless tests).</summary>
    internal static bool CreateWeb { get; set; } = true;

    /// <summary>Seam for the head's Cut leash: the one-click cut (LeashHead.Cut on this head).</summary>
    internal static Action CutLeash { get; set; } = () => Platform.LeashHead.Cut();

    private readonly bool _locked;
    private readonly string _holder;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly Grid _root;
    private WebHost? _web;
    private bool _failed;
    private bool _allowClose;
    private string? _pageUrl;
    private WindowState _prevState;
    private DispatcherTimer? _refuseTimer;
    private DispatcherTimer? _shakeTimer;
    private DispatcherTimer? _cageTimer;

    internal WebHost? View => _web;
    internal string TitleLine => _title.Text ?? "";
    internal bool Locked => _locked;

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
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1280;
        Height = 760;
        try
        {
            if (Screens.Primary is { } s)
            {
                var wa = s.WorkingArea;
                Width = Math.Max(640, wa.Width / s.Scaling * 0.92);
                Height = Math.Max(400, wa.Height / s.Scaling * 0.92);
            }
        }
        catch { }

        _root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(14, 6, 8, 6) };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = FriendsDrawer.Label(TitleText(), 13.5, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        _title.Tag = "leash-punish-title";
        words.Children.Add(_title);
        // The way out, told from the first frame.
        _hint = FriendsDrawer.Label(HintText(), 11.5, FriendsDrawer.Muted);
        _hint.Tag = "leash-punish-hint";
        _hint.Margin = new Thickness(0, 1, 0, 0);
        words.Children.Add(_hint);
        head.Children.Add(words);

        var buttonBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33));
        var cut = FriendsDrawer.Pill(Loc.Get("leash_cut"), buttonBg, FriendsDrawer.Mint, "leash-punish-cut", FriendsDrawer.Mint);
        ToolTip.SetTip(cut, Loc.Get("leash_cut_tip"));
        cut.Focusable = false;
        cut.Padding = new Thickness(10, 3, 10, 3);
        cut.Margin = new Thickness(8, 0, 6, 0);
        cut.Cursor = FriendsDrawer.Hand();
        cut.Click += (_, _) => CutFromWindow();
        Grid.SetColumn(cut, 1);
        head.Children.Add(cut);

        var fs = FriendsDrawer.Pill(Loc.Get("leash_punish_fullscreen"), buttonBg, FriendsDrawer.Text, "leash-punish-fullscreen", FriendsDrawer.Line2);
        ToolTip.SetTip(fs, Loc.Get("leash_punish_fullscreen_key"));
        fs.Focusable = false;
        fs.Padding = new Thickness(10, 3, 10, 3);
        fs.Cursor = FriendsDrawer.Hand();
        fs.Click += (_, _) => ToggleFullscreen();
        Grid.SetColumn(fs, 2);
        head.Children.Add(fs);
        // WPF FriendsLook.HeadBrush: Raised to Panel, top to bottom.
        _root.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x2C, 0x14, 0x50), 0), new GradientStop(Color.FromRgb(0x1A, 0x0E, 0x2E), 1) },
            },
            Child = head,
            Tag = "leash-punish-head",
        });

        Content = _root;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; } }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private string TitleText() => Loc.GetF(_locked ? "leash_punish_title" : "leash_task_title", _holder);

    private string HintText() => _locked
        ? Loc.GetF("leash_punish_hold", LeashHoldToCut.KeyLabel(CoreSettings.Current?.PanicKey))
        : Loc.Get("leash_task_close_hint");

    /// <summary>The head's Cut leash: the same one-click cut as the drawer card. The window goes
    /// first so nothing is left caged while the service works.</summary>
    internal void CutFromWindow()
    {
        Serilog.Log.Information("Leash: cut from the video window");
        CloseNow();
        try { CutLeash(); } catch (Exception ex) { Serilog.Log.Warning("Leash cut from window failed: {E}", ex.Message); }
    }

    // ---- open / close ------------------------------------------------------------------------

    /// <summary>Open a web page (a Hypnotube video) in the cage. Replaces any open one. https only.</summary>
    public static bool OpenUrl(string url, string holder, bool locked)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var w = Show(holder, locked);
        w._pageUrl = u.AbsoluteUri;
        w.Load(u);
        return true;
    }

    /// <summary>Open a local video file on a plain page in the cage.</summary>
    public static bool OpenFile(string path, string holder, bool locked)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        Uri page;
        try
        {
            var dir = Path.Combine(CorePaths.UserData, "leash_video");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "index.html");
            File.WriteAllText(file, LocalPage(new Uri(Path.GetFullPath(path)).AbsoluteUri));
            page = new Uri(file);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Leash video page could not be written: {E}", ex.Message);
            return false;
        }
        var w = Show(holder, locked);
        w._pageUrl = page.AbsoluteUri;
        w.Load(page);
        return true;
    }

    /// <summary>Close without a fuss: the video is done, the leash is off, or panic.</summary>
    public static void CloseNow()
    {
        var w = Current;
        if (w == null) return;
        w._allowClose = true;
        try { w.Close(); } catch { }
        if (ReferenceEquals(Current, w)) Current = null;
    }

    private static LeashPunishWindow Show(string holder, bool locked)
    {
        CloseNow();
        var w = new LeashPunishWindow(locked, holder);
        Current = w;
        w.Show();
        try { w.Activate(); } catch { }
        return w;
    }

    internal static string LocalPage(string src) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0;height:100%;background:#000;overflow:hidden}"
        + "video{width:100vw;height:100vh;object-fit:contain;background:#000}</style></head><body>"
        + "<video autoplay playsinline onerror=\"try{chrome.webview.postMessage('leash-error')}catch(e){}\" src=\""
        + WebUtility.HtmlEncode(src) + "\"></video></body></html>";

    // ---- the browser -------------------------------------------------------------------------

    private void Load(Uri page)
    {
        if (!CreateWeb) return;
        try
        {
            _web = new WebHost { AllowNavigation = Allowed };
            Grid.SetRow(_web, 1);
            _root.Children.Add(_web);
            if (!_web.HasEngine) { Fail("no web engine"); return; }
            _web.WebMessage += msg =>
            {
                if (msg == "leash-ended") VideoEnded?.Invoke();
                else if (msg == "leash-error") Fail("media error");
            };
            _web.NavigationCompleted += _ => { var t = InjectCageAsync(); };
            // No document-created injection on this head: re-run the (idempotent) cage each second
            // so a page that rebuilds its player is caged again.
            _cageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cageTimer.Tick += (_, _) => _ = InjectCageAsync();
            _cageTimer.Start();
            _web.Navigate(page);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Leash video window failed: {E}", ex.Message);
            Fail("web view failed");
        }
    }

    private async System.Threading.Tasks.Task InjectCageAsync()
    {
        try { if (_web is { } w && !_allowClose) await w.InvokeScriptAsync(CageScript); }
        catch { }
    }

    /// <summary>The first load goes through; after it, only the same page (a reload) may load.</summary>
    internal bool Allowed(Uri target) => _pageUrl == null || SamePage(_pageUrl, target.AbsoluteUri);

    /// <summary>WPF AppLeashTaskHost.SamePage: same host (www. ignored) and path.</summary>
    internal static bool SamePage(string? pageUrl, string? targetUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return false;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)) return false;
        static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return string.Equals(Host(page), Host(target), StringComparison.OrdinalIgnoreCase)
            && string.Equals(page.AbsolutePath.TrimEnd('/'), target.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Once per window: the video cannot play here.</summary>
    private void Fail(string why)
    {
        if (_failed || _allowClose) return;
        _failed = true;
        Serilog.Log.Information("Leash video window: will not play ({Why})", why);
        // Posted: the handler closes this window, which must not happen inside a web view callback.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(Current, this)) return;
            try { PlaybackFailed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("Leash playback-failed handler failed: {E}", ex.Message); }
        });
    }

    // ---- fullscreen, close, shake ------------------------------------------------------------

    internal void ToggleFullscreen()
    {
        if (WindowState != WindowState.FullScreen)
        {
            _prevState = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = _prevState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A punishment refuses a close, except when the app or the OS is shutting down (WPF SessionEnding).
        if (_locked && !_allowClose && e.CloseReason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown))
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
        _cageTimer?.Stop();
        _web = null;
    }

    /// <summary>A close on a punishment: the window shakes and the head says whose it is and how
    /// it ends, for a few seconds.</summary>
    internal void Refuse()
    {
        _title.Text = Loc.GetF("leash_punish_no_close", _holder);
        _title.Foreground = FriendsDrawer.Gold;
        _hint.Foreground = FriendsDrawer.Gold;
        _refuseTimer?.Stop();
        _refuseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refuseTimer.Tick += (_, _) =>
        {
            _refuseTimer?.Stop();
            _title.Text = TitleText();
            _title.Foreground = FriendsDrawer.Text;
            _hint.Foreground = FriendsDrawer.Muted;
        };
        _refuseTimer.Start();
        // FX: LeashFx.Denied();
        var level = CoreSettings.Current?.MotionLevel ?? global::ConditioningControlPanel.Models.MotionLevel.Full;
        if (level != global::ConditioningControlPanel.Models.MotionLevel.Off) Shake(level == global::ConditioningControlPanel.Models.MotionLevel.Reduced ? 7.0 : 16.0);
    }

    /// <summary>Moves the real window (a native web view does not follow a render transform), so
    /// it also shakes maximised; lands back exactly where it was.</summary>
    private void Shake(double amp)
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen) return;   // Position is ignored there
        var origin = Position;
        _shakeTimer?.Stop();
        var start = DateTime.UtcNow;
        const double ms = 420;
        _shakeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _shakeTimer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds;
            var dx = t >= ms ? 0 : (int)Math.Round(amp * (1 - t / ms) * Math.Sin(t / ms * Math.PI * 9));
            try { Position = new PixelPoint(origin.X + dx, origin.Y); } catch { }
            if (t >= ms) _shakeTimer?.Stop();
        };
        _shakeTimer.Start();
    }

    /// <summary>WPF CageScript, verbatim: runs in every document (here: after each load and each second).</summary>
    internal const string CageScript = @"(function(){
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
  tick();
  setInterval(tick, 400);
})();";
}
