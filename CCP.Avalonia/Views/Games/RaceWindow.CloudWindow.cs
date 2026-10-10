// PORTED from WPF 7.1.5 Services/Race/RaceCloudWindow.cs: the BambiCloud browser frame. A plain
// window on the site, the player's own sign-in and playlist, and one small watcher script that reports
// what their audio element is doing (cloud-track / cloud-clock / cloud-play / cloud-pause /
// cloud-ended) and takes two instructions back (cloud-set-paused, cloud-start).
// READ-ONLY GUEST: nothing here calls their API, signs anybody in or reads anything beyond the audio
// element's own state and the tab title. Navigation stays on the site; any other link opens in the
// player's browser. Closing the window hides it (the playlist lives in it); the race closing is the
// only real close, and that is what stops their audio.
//
// Differences from WPF, all from the port's WebHost (Avalonia.Controls.WebView) having fewer seams
// than a raw WebView2:
//  - the watcher is injected when a navigation completes, not at document creation (no
//    AddScriptToExecuteOnDocumentCreated); it scans for the element, so a late start loses nothing;
//  - page -> host goes through the engine's invokeCSharpAction, host -> page is a script call into
//    the watcher (window.__ccpRaceCloud.host), so the same code runs on WebView2 and WebKitGTK;
//  - SEAM(platform): no per-window browser profile, no "--autoplay-policy=no-user-gesture-required"
//    and no NewWindowRequested hook. If their player refuses a scripted play, the page's own fallback
//    (cloud-open {front:true} after 9 s) brings this window forward for a press by hand.
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>What the race host needs from the browser frame (a seam for tests).</summary>
    internal interface IRaceCloudWindow : IDisposable
    {
        /// <summary>Every cloud-* frame the watcher posts, on the UI thread.</summary>
        event Action<JObject>? Message;
        /// <summary>The player closed (hid) the window.</summary>
        event Action? Hidden;
        void ShowOrFocus(string? url = null);
        void ShowInBackground(string? url = null);
        /// <summary>The game's play button: press play over there.</summary>
        void RequestStart();
        void PostToPage(object msg);
    }

    internal sealed class RaceCloudWindow : IRaceCloudWindow
    {
        private const string SiteHost = "bambicloud.com";
        private const string StartUrl = "https://bambicloud.com/";

        private Window? _window;
        private WebHost? _web;
        private bool _disposed;
        /// <summary>A play press that has not been answered by a cloud-play yet: re-sent when a page lands.</summary>
        private bool _startWanted;

        public event Action<JObject>? Message;
        public event Action? Hidden;

        public bool IsOpen => _window != null && _window.IsVisible;

        public void ShowOrFocus(string? url = null) => Open(url, background: false);

        public void ShowInBackground(string? url = null) => Open(url, background: true);

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
                if (_window == null) Build(want ?? StartUrl);
                else if (want != null) _web?.Navigate(new Uri(want));
                if (_window == null) return;
                if (!_window.IsVisible)
                {
                    _window.ShowActivated = !background;
                    _window.Show();
                }
                if (background) return;
                _window.Activate();
            }
            catch (Exception ex) { Log.Warning("RaceCloud.ShowOrFocus: {E}", ex.Message); }
        }

        /// <summary>Host to page: a call into the watcher. Quiet when the page has no watcher yet.</summary>
        public void PostToPage(object msg)
        {
            try
            {
                if (_disposed || _web == null) return;
                _ = _web.InvokeScriptAsync(HostCall(JsonConvert.SerializeObject(msg)));
            }
            catch (Exception ex) { Log.Debug("RaceCloud.PostToPage: {E}", ex.Message); }
        }

        internal static string HostCall(string json) =>
            "(function(){try{var c=window.__ccpRaceCloud;if(c&&c.host)c.host(" + json + ");}catch(e){}})();";

        // ============================ window ============================

        private void Build(string landing)
        {
            _web = new WebHost { Profile = Platform.WebProfiles.BambiCloud };
            _web.AllowNavigation = uri =>
            {
                // Their own pages stay in the frame; anything else goes to the player's browser.
                if (IsSiteUri(uri.AbsoluteUri)) return true;
                OpenExternally(uri);
                return false;
            };
            _web.NavigationCompleted += _ => OnNavigationCompleted();
            _web.WebMessage += body => OnPageMessage(body, _web?.CurrentUrl?.AbsoluteUri);

            var s = CoreSettings.Current;
            double w = s?.RaceCloudWindowWidth ?? 0, h = s?.RaceCloudWindowHeight ?? 0;
            double left = s?.RaceCloudWindowLeft ?? 0, top = s?.RaceCloudWindowTop ?? 0;
            _window = new Window
            {
                Background = Brushes.Black,
                Title = "BambiCloud",
                Width = w > 200 ? w : 1100,
                Height = h > 200 ? h : 820,
                ShowInTaskbar = true,
                WindowStartupLocation = left > 0 || top > 0 ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
                Content = _web,
            };
            if (left > 0 || top > 0) _window.Position = new PixelPoint((int)left, (int)top);
            // Close hides: the audio lives in this frame, and a closed frame that killed the playlist
            // would be a trap. Dispose is the only real close. A close that is not the player's own
            // (the app shutting down, the OS logging off) is never refused.
            _window.Closing += (_, e) =>
            {
                if (_disposed || e.IsProgrammatic || e.CloseReason != WindowCloseReason.WindowClosing) return;
                e.Cancel = true;
                SaveBounds();
                try { _window?.Hide(); } catch (Exception ex) { Log.Debug("RaceCloud.hide: {E}", ex.Message); }
                try { Hidden?.Invoke(); } catch (Exception ex) { Log.Debug("RaceCloud.Hidden: {E}", ex.Message); }
            };
            Log.Information("RaceCloud: window up");
            if (!_web.HasEngine)
            {
                // Fail loud: with no browser engine there is no watcher and no clock, and a silent
                // half-working window is worse than a plain message.
                Log.Warning("RaceCloud: no browser engine on this machine ({Reason})", WebHost.UnavailableReason);
                RaiseWatcherFailure();
                return;
            }
            _web.Navigate(new Uri(landing));
        }

        private void SaveBounds()
        {
            try
            {
                var win = _window;
                var s = CoreSettings.Current;
                if (win == null || s == null || win.WindowState != WindowState.Normal) return;
                if (win.Width < 200 || win.Height < 200) return;
                s.RaceCloudWindowLeft = win.Position.X;
                s.RaceCloudWindowTop = win.Position.Y;
                s.RaceCloudWindowWidth = win.Width;
                s.RaceCloudWindowHeight = win.Height;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Debug("RaceCloud.SaveBounds: {E}", ex.Message); }
        }

        // ============================ webview ============================

        private void RaiseWatcherFailure()
        {
            try { Message?.Invoke(JObject.FromObject(new { type = "cloud-failed" })); }
            catch (Exception ex) { Log.Debug("RaceCloud.RaiseWatcherFailure: {E}", ex.Message); }
        }

        private void OnNavigationCompleted()
        {
            if (_disposed || _web == null) return;
            // Only ever into their own pages (the gate above keeps the frame there; this is the belt).
            if (!IsSiteUri(_web.CurrentUrl?.AbsoluteUri)) return;
            _ = _web.InvokeScriptAsync(WatcherScript);
            if (_startWanted) PostToPage(new { type = "cloud-start" });
        }

        /// <summary>True for https://bambicloud.com and its subdomains, nothing else.</summary>
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

        /// <summary>Test seam: how a link that leaves the site is opened.</summary>
        internal static Action<string> OpenInBrowser = url =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Debug("RaceCloud.OpenExternally: {E}", ex.Message); }
        };

        internal static void OpenExternally(Uri? uri)
        {
            // http(s) only: a page must never talk this window into launching a local handler.
            if (uri == null || !uri.IsAbsoluteUri) return;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;
            var url = uri.AbsoluteUri;
            foreach (char c in url) if (c == '"' || char.IsControl(c)) return;
            OpenInBrowser(url);
        }

        /// <summary>A string their page posted. Their page may post messages of its own; only our
        /// watcher's envelopes are read, and only when they arrived from the site itself.</summary>
        internal void OnPageMessage(string? body, string? source)
        {
            try
            {
                if (_disposed || string.IsNullOrEmpty(body) || !IsSiteUri(source)) return;
                var o = JObject.Parse(GameWindow.Unwrap(body));
                var type = (string?)o["type"];
                if (string.IsNullOrEmpty(type) || !type.StartsWith("cloud-", StringComparison.Ordinal)) return;
                if (type == "cloud-play") _startWanted = false;
                if (Dispatcher.UIThread.CheckAccess()) Message?.Invoke(o);
                else Dispatcher.UIThread.Post(() => { if (!_disposed) Message?.Invoke(o); });
            }
            catch (Exception ex) { Log.Debug("RaceCloud.OnPageMessage: {E}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            try { SaveBounds(); } catch (Exception ex) { Log.Debug("RaceCloud.Dispose bounds: {E}", ex.Message); }
            _disposed = true;
            try { _window?.Close(); } catch (Exception ex) { Log.Debug("RaceCloud.Dispose window: {E}", ex.Message); }
            _web = null; _window = null;
            Message = null; Hidden = null;
            Log.Information("RaceCloud: closed");
        }

        // ============================ the watcher ============================
        //
        // The only code of ours that runs inside their page. It reads the audio element and nothing
        // else, posts five kinds of frame, and takes two instructions.

        internal const string WatcherScript = @"(function () {
  try {
    if (window.__ccpRaceCloud) return;
    var CLOCK_MS = 250, ECHO_MS = 800;
    var current = null, lastSrc = '', lastPlaying = null, quietUntil = 0;
    window.__ccpRaceCloud = { version: 1, tracked: function () { return current; } };

    function post(msg) {
      try {
        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(JSON.stringify(msg));
        else if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
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
    window.__ccpRaceCloud.host = function (m) {
      try {
        if (m && m.type === 'cloud-start') { start(); return; }
        if (!m || m.type !== 'cloud-set-paused') return;
        var el = current || scan();
        if (!el) return;
        quietUntil = Date.now() + ECHO_MS;
        if (m.on) el.pause();
        else { var p = el.play(); if (p && p['catch']) p['catch'](function () {}); }
      } catch (e) {}
    };
  } catch (e) {}
})();";
    }
}
