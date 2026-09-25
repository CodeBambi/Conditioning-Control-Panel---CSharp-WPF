using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The BambiCloud window: a plain browser frame on a third-party fan site, opened on demand from
/// the race menu so a player can sign in over there and drive their own playlist while Racing
/// Thoughts follows the audio.
///
/// WE ARE A GUEST. The window shows their site and nothing else: no login form of ours, no API
/// call of ours, no markup added to their page. The one script that runs inside it (the watcher,
/// <see cref="WatcherScript"/>) reads the state of their audio element and the tab title, and
/// takes two instructions back: pause / resume, and start (the game's play button, which plays
/// their element or, before one exists, presses the play action on the track page). Everything
/// else about their page is theirs.
///
/// Own user-data folder, so a sign-in survives between sessions and the descent's WebView2
/// profile is never touched. Navigation stays on the site and its subdomains; anything else is
/// handed to the system browser rather than followed in here. Closing the window HIDES it (the
/// audio would die with the WebView, and a player who closes the frame is not asking to stop the
/// music); the race host disposes it for real when the race window goes.
/// </summary>
internal sealed class RaceCloudWindow : IDisposable
{
    /// <summary>The site, and the root of the only host suffix navigation may stay on.</summary>
    private const string SiteHost = "bambicloud.com";
    private const string StartUrl = "https://bambicloud.com/";

    private Window? _window;
    private WebView2? _web;
    private bool _initStarted;
    private bool _coreReady;
    /// <summary>The page to open on, when the levels panel named one. Read once by
    /// InitWebAsync, so a window built for a track lands on that track and not the front door.</summary>
    private string? _openOn;
    private bool _disposed;
    /// <summary>The game's play button asked for the track and their player has not started yet.</summary>
    private bool _startWanted;

    /// <summary>Every cloud-* frame the watcher posts. Raised on the UI thread.</summary>
    public event Action<JObject>? Message;

    /// <summary>Raised when the player closes the window (which hides it), so the host can take
    /// its "opening" plate back down.</summary>
    public event Action? Hidden;

    public bool IsOpen => _window != null && _window.IsVisible;

    /// <summary>Open the window, or bring it back if it was closed to the tray of the mind.
    /// <paramref name="url"/> is the page to land on (a track's own page, from the levels
    /// panel); null keeps whatever is already loaded, or the site's front door on a fresh
    /// window. Checked against the site here as well, never trusted from the caller.</summary>
    public void ShowOrFocus(string? url = null) => Open(url, background: false);

    /// <summary>Open the window BEHIND the game (owner, 2026-09-25): a level pick lands the track
    /// page without taking focus, and the game's own play button starts it (<see cref="RequestStart"/>).
    /// The caller hands focus back to the race window after this.</summary>
    public void ShowInBackground(string? url = null) => Open(url, background: true);

    /// <summary>Start the landed track from the game's play button. Remembered until their player
    /// reports it is playing, and re-sent when a page finishes loading, because a level pick and a
    /// play press can both land while the page is still on its way.</summary>
    public void RequestStart()
    {
        if (_disposed) return;
        _startWanted = true;
        if (_window == null) Open(null, background: true);
        PostToPage(new { type = "cloud-start" });
    }

    private void Open(string? url, bool background)
    {
        if (_disposed) return;
        try
        {
            var want = IsSiteUri(url) ? url : null;
            if (want != null) _startWanted = false;   // a new track page: an old press does not carry over
            if (_window == null) { _openOn = want; Build(background); }
            else if (want != null && _coreReady && _web?.CoreWebView2 != null) _web.CoreWebView2.Navigate(want);
            else if (want != null) _openOn = want;   // still starting up: InitWebAsync takes it
            if (_window == null) return;
            if (!_window.IsVisible)
            {
                _window.ShowActivated = !background;
                _window.Show();
            }
            if (background) return;
            _window.Activate();
            _web?.Focus();
        }
        catch (Exception ex) { App.Logger?.Warning("RaceCloud.ShowOrFocus: {E}", ex.Message); }
    }

    /// <summary>Send one frame into the page (cloud-set-paused). Silently does nothing before the
    /// core exists: the only frame we send answers a state the page itself just reported, so a
    /// dropped one is corrected by the next tick.</summary>
    public void PostToPage(object msg)
    {
        try
        {
            if (!_coreReady || _web?.CoreWebView2 == null) return;
            _web.CoreWebView2.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(msg));
        }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.PostToPage: {E}", ex.Message); }
    }

    // ============================ window ============================

    private void Build(bool background)
    {
        _web = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var s = App.Settings?.Current;
        double w = s?.RaceCloudWindowWidth ?? 0, h = s?.RaceCloudWindowHeight ?? 0;
        double left = s?.RaceCloudWindowLeft ?? 0, top = s?.RaceCloudWindowTop ?? 0;
        _window = new Window
        {
            // WebView2 does not paint reliably in a layered window, the same rule the game hosts
            // live by (ChaosWebViewHost): stay opaque.
            AllowsTransparency = false,
            Background = Brushes.Black,
            Title = "BambiCloud",
            Width = w > 200 ? w : Math.Min(1100, SystemParameters.PrimaryScreenWidth * 0.6),
            Height = h > 200 ? h : Math.Min(820, SystemParameters.PrimaryScreenHeight * 0.8),
            ShowInTaskbar = true,
            WindowStartupLocation = left > 0 || top > 0 ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
            Content = _web,
        };
        if (left > 0 || top > 0) { _window.Left = left; _window.Top = top; }
        ClampOnScreen(_window);
        // Close hides: the audio lives in this WebView, and a closed frame that killed the
        // playlist would be a trap. Dispose is the only real close.
        _window.Closing += (_, e) =>
        {
            if (_disposed) return;
            e.Cancel = true;
            SaveBounds();
            try { _window?.Hide(); } catch (Exception ex) { App.Logger?.Debug("RaceCloud.hide: {E}", ex.Message); }
            try { Hidden?.Invoke(); } catch (Exception ex) { App.Logger?.Debug("RaceCloud.Hidden: {E}", ex.Message); }
        };
        _window.ShowActivated = !background;
        _window.Show();
        _ = InitWebAsync();
        App.Logger?.Information("RaceCloud: window up");
    }

    /// <summary>A remembered position on a monitor that is no longer there would open the window
    /// where nobody can reach it. Nudge it back onto the virtual desktop.</summary>
    private static void ClampOnScreen(Window win)
    {
        try
        {
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            if (vw <= 0 || vh <= 0) return;
            if (win.WindowStartupLocation != WindowStartupLocation.Manual) return;
            win.Left = Math.Min(Math.Max(win.Left, vl), vl + vw - 200);
            win.Top = Math.Min(Math.Max(win.Top, vt), vt + vh - 120);
        }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.ClampOnScreen: {E}", ex.Message); }
    }

    private void SaveBounds()
    {
        try
        {
            var win = _window;
            var s = App.Settings?.Current;
            if (win == null || s == null || win.WindowState != WindowState.Normal) return;
            if (win.Width < 200 || win.Height < 200) return;
            s.RaceCloudWindowLeft = win.Left;
            s.RaceCloudWindowTop = win.Top;
            s.RaceCloudWindowWidth = win.Width;
            s.RaceCloudWindowHeight = win.Height;
            App.Settings?.Save();
        }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.SaveBounds: {E}", ex.Message); }
    }

    // ============================ webview ============================

    private async Task InitWebAsync()
    {
        if (_initStarted || _web == null) return;
        _initStarted = true;
        try
        {
            // Its own profile: the sign-in persists between sessions and nothing here can disturb
            // the descent's or the race's own WebView2 state.
            var userDataFolder = Path.Combine(App.UserDataPath, "browser_data_bambicloud");
            Directory.CreateDirectory(userDataFolder);
            // Autoplay without a gesture in THIS profile only: the game's play button starts their
            // player from the race window, so the click that "counts" happened somewhere else.
            // The argument string is constant, which a user-data folder needs across launches.
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                .ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (_disposed || _web.CoreWebView2 == null) return;

            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;   // their site, their reading size
            core.Settings.IsWebMessageEnabled = true;

            try { await core.AddScriptToExecuteOnDocumentCreatedAsync(WatcherScript).ConfigureAwait(true); }
            catch (Exception ex)
            {
                // Fail loud: with no watcher there is no clock, and a silent half-working window
                // is worse than a plain message.
                App.Logger?.Warning("RaceCloud: watcher inject failed: {E}", ex.Message);
                RaiseWatcherFailure();
            }
            if (_disposed) return;

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            core.NavigationCompleted += OnNavigationCompleted;
            _coreReady = true;
            var landing = _openOn; _openOn = null;
            core.Navigate(IsSiteUri(landing) ? landing! : StartUrl);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("RaceCloud.InitWebAsync failed: {E}", ex.Message);
            RaiseWatcherFailure();
        }
    }

    private void RaiseWatcherFailure()
    {
        try { Message?.Invoke(JObject.FromObject(new { type = "cloud-failed" })); }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.RaiseWatcherFailure: {E}", ex.Message); }
    }

    /// <summary>Only the site and its subdomains load in here. Anything else (an outbound link,
    /// a payment page, a social profile) goes to the system browser, where the player has their
    /// own session and their own address bar.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsSiteUri(e.Uri)) return;
        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsSiteUri(e.Uri))
        {
            try { _web?.CoreWebView2?.Navigate(e.Uri); }
            catch (Exception ex) { App.Logger?.Debug("RaceCloud.NewWindow navigate: {E}", ex.Message); }
            return;
        }
        OpenExternally(e.Uri);
    }

    /// <summary>https on the site or one of its subdomains. Host-suffix matched on a dot boundary,
    /// so "bambicloud.com.example.net" is not the site.</summary>
    public static bool IsSiteUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            if (!string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
            var host = u.Host;
            return host.Equals(SiteHost, StringComparison.OrdinalIgnoreCase)
                   || host.EndsWith("." + SiteHost, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void OpenExternally(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        // http(s) only: a page must never talk this window into launching a local handler.
        if (!uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.OpenExternally: {E}", ex.Message); }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // Their page may post messages of its own; only our watcher's envelopes are read, and
            // only when they arrived from the site itself.
            if (!IsSiteUri(e.Source)) return;
            var json = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(json)) return;
            var o = JObject.Parse(json);
            var type = (string?)o["type"];
            if (string.IsNullOrEmpty(type) || !type.StartsWith("cloud-", StringComparison.Ordinal)) return;
            if (type == "cloud-play") _startWanted = false;
            Message?.Invoke(o);
        }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.OnWebMessageReceived: {E}", ex.Message); }
    }

    /// <summary>A play press that landed while the page was still loading is sent again once it is up.</summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_startWanted) PostToPage(new { type = "cloud-start" });
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.Logger?.Warning("RaceCloud: browser process failed ({Kind})", e.ProcessFailedKind);
        RaiseWatcherFailure();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { SaveBounds(); } catch (Exception ex) { App.Logger?.Debug("RaceCloud.Dispose bounds: {E}", ex.Message); }
        try
        {
            if (_web?.CoreWebView2 != null)
            {
                _web.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _web.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _web.CoreWebView2.ProcessFailed -= OnProcessFailed;
                _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("RaceCloud.Dispose unhook: {E}", ex.Message); }
        try { _web?.Dispose(); } catch (Exception ex) { App.Logger?.Debug("RaceCloud.Dispose web: {E}", ex.Message); }
        try { _window?.Close(); } catch (Exception ex) { App.Logger?.Debug("RaceCloud.Dispose window: {E}", ex.Message); }
        _web = null; _window = null; _coreReady = false;
        Message = null; Hidden = null;
        App.Logger?.Information("RaceCloud: closed");
    }

    // ============================ the watcher ============================

    /// <summary>
    /// The only code we run inside somebody else's page, and this is the whole of what it may do:
    /// watch their audio element, report where it is, and take a pause / resume back.
    ///
    /// It reads no data of theirs beyond the element's own state and the tab title, calls no API,
    /// adds no markup, removes nothing. ES5 with every path wrapped, because it is a string handed
    /// to a stranger's bundle and a throw of ours must never surface as a bug in their player.
    /// The listeners are capture phase on `document` (media events do not bubble, so a capture
    /// listener on the document is the one binding that still hears an element their router swaps
    /// in five minutes later) and a MutationObserver picks up elements added without an event.
    /// </summary>
    private const string WatcherScript = @"(function () {
  try {
    if (window.__ccpRaceCloud) return;
    var CLOCK_MS = 250, ECHO_MS = 800;
    var current = null, lastSrc = '', lastPlaying = null, quietUntil = 0;
    window.__ccpRaceCloud = { version: 1, tracked: function () { return current; } };

    function post(msg) {
      try {
        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
          window.chrome.webview.postMessage(msg);
      } catch (e) {}
    }
    function isMedia(el) { return !!el && (el.tagName === 'AUDIO' || el.tagName === 'VIDEO'); }
    function num(v) { return (typeof v === 'number' && isFinite(v) && v >= 0) ? v : 0; }
    function srcOf(el) { try { return (el && (el.currentSrc || el.src)) || ''; } catch (e) { return ''; } }
    function durOf(el) { try { return num(el.duration); } catch (e) { return 0; } }
    function isPlaying(el) { try { return !!el && !el.paused && !el.ended; } catch (e) { return false; } }
    /** Their media session title if their player set one, else the tab title, else the file name. */
    function titleOf(el) {
      try {
        var ms = navigator.mediaSession;
        if (ms && ms.metadata && ms.metadata.title) return String(ms.metadata.title).slice(0, 160);
      } catch (e) {}
      try { if (document.title) return String(document.title).slice(0, 160); } catch (e) {}
      try {
        var parts = srcOf(el).split('?')[0].split('/');
        return decodeURIComponent(parts[parts.length - 1] || '').slice(0, 160);
      } catch (e) {}
      return '';
    }
    function take(el) { if (isMedia(el)) current = el; }
    /** The page's element, preferring one that is actually running. */
    function scan() {
      try {
        if (current && document.contains && !document.contains(current)) current = null;
        var nodes = document.querySelectorAll('audio,video');
        var first = null, playing = null;
        for (var i = 0; i < nodes.length; i++) {
          if (!first) first = nodes[i];
          if (!playing && isPlaying(nodes[i])) playing = nodes[i];
        }
        var pick = playing || first;
        if (pick) current = pick;
      } catch (e) {}
      return current;
    }
    /** A new source is playing: one frame, once per source. */
    function announce(el) {
      var src = srcOf(el);
      if (!src || src === lastSrc) return;
      lastSrc = src;
      post({ type: 'cloud-track', src: src, title: titleOf(el), durationSec: durOf(el) });
    }
    /** State changes only, never an echo of a pause we were told to make. */
    function state(playing) {
      if (lastPlaying === playing) return;
      lastPlaying = playing;
      if (Date.now() < quietUntil) return;
      post({ type: playing ? 'cloud-play' : 'cloud-pause' });
    }
    function onMedia(e) {
      try {
        var el = e.target;
        if (!isMedia(el)) return;
        take(el);
        if (e.type === 'play' || e.type === 'playing') { announce(el); state(true); }
        else if (e.type === 'pause') { state(false); }
        else if (e.type === 'ended') { lastSrc = ''; lastPlaying = false; post({ type: 'cloud-ended' }); }
        else if (e.type === 'emptied') { lastSrc = ''; }
      } catch (err) {}
    }
    var EVENTS = ['play', 'playing', 'pause', 'ended', 'emptied'];
    for (var i = 0; i < EVENTS.length; i++) document.addEventListener(EVENTS[i], onMedia, true);

    function observe() {
      try {
        if (typeof MutationObserver !== 'function') return;
        var root = document.documentElement || document;
        if (!root) return;
        new MutationObserver(function (recs) {
          for (var a = 0; a < recs.length; a++) {
            var added = recs[a].addedNodes;
            if (!added) continue;
            for (var b = 0; b < added.length; b++) {
              var n = added[b];
              if (isMedia(n)) { take(n); return; }
              if (n && n.querySelector) { var f = n.querySelector('audio,video'); if (f) { take(f); return; } }
            }
          }
        }).observe(root, { childList: true, subtree: true });
      } catch (e) {}
    }
    observe();
    try { document.addEventListener('DOMContentLoaded', observe, false); } catch (e) {}

    setInterval(function () {
      try {
        var el = (current && document.contains && document.contains(current)) ? current : scan();
        if (!el) return;
        post({ type: 'cloud-clock', t: num(el.currentTime), playing: isPlaying(el), durationSec: durOf(el) });
      } catch (e) {}
    }, CLOCK_MS);

    // cloud-start: the game's play button. Press the play action in the track page's header (the
    // round play button beside the title), or play their element on a page without one, and keep
    // trying for eight seconds while their page is still putting itself together.
    var startTimer = 0;
    function start() {
      try {
        if (startTimer) clearInterval(startTimer);
        var tries = 0, pressed = 0;
        var attempt = function () {
          try {
            tries++;
            var el = scan();
            if (el && isPlaying(el)) { clearInterval(startTimer); startTimer = 0; return; }
            // The track page's own play action first: their player may still hold the LAST track,
            // and resuming that would start the wrong song. Only pressed while nothing plays (it
            // toggles), as soon as it exists and then every 3 s until the audio runs.
            var btn = document.querySelector('.section-header .action');
            if (btn && btn.click) { if (!pressed || tries - pressed >= 6) { pressed = tries; btn.click(); } }
            else if (el && srcOf(el)) { var p = el.play(); if (p && p['catch']) p['catch'](function () {}); }
            if (tries >= 16) { clearInterval(startTimer); startTimer = 0; }
          } catch (e) {}
        };
        startTimer = setInterval(attempt, 500);
        attempt();
      } catch (e) {}
    }

    // The instructions we take: the race's Brake pauses their player, letting go resumes it,
    // and cloud-start (above) starts the landed track.
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.addEventListener) {
        window.chrome.webview.addEventListener('message', function (ev) {
          try {
            var m = ev && ev.data;
            if (m && m.type === 'cloud-start') { start(); return; }
            if (!m || m.type !== 'cloud-set-paused') return;
            var el = current || scan();
            if (!el) return;
            quietUntil = Date.now() + ECHO_MS;
            if (m.on) el.pause();
            else { var p = el.play(); if (p && p['catch']) p['catch'](function () {}); }
          } catch (e) {}
        });
      }
    } catch (e) {}
  } catch (e) {}
})();";
}
