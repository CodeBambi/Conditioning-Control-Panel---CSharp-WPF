using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// IPlaybackTimeSource bound to the first <c>&lt;video&gt;</c> element on
    /// a WebView2 page (HypnoTube etc). Polls <c>currentTime</c> at ~10 Hz via
    /// a DispatcherTimer because cross-thread custom event subscriptions across
    /// the WebView2 boundary are fiddly and add a JS bridge surface that the
    /// rest of CCP doesn't need.
    ///
    /// Seek/Pause/Play go through ExecuteScriptAsync. GetVideoRect maps the
    /// video element's getBoundingClientRect through the WebView's screen rect
    /// so gaze rules normalize against what the user actually sees in the
    /// embedded player, not the browser chrome.
    ///
    /// Build the JS bridge here exactly once; later browser-driven phases
    /// reuse the same script surface.
    /// </summary>
    public sealed class BrowserVideoTimeSource : IPlaybackTimeSource, IDisposable
    {
        private const int PollIntervalMs = 100;
        private readonly WebView2 _webView;
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer? _pollTimer;
        private double _lastTimeSeconds = -1;
        private double _lastDurationSeconds = -1;
        private double _currentTimeSeconds;
        private double _durationSeconds;
        private bool _isPlaying;
        private double[] _videoRectNormalized = new[] { 0.0, 0.0, 1.0, 1.0 };
        private bool _attached;
        private volatile bool _pollInFlight;

        public event Action<double>? PlaybackTimeChanged;

        public BrowserVideoTimeSource(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _dispatcher = webView.Dispatcher;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += async (_, _) => await PollAsync();
            _pollTimer.Start();
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _pollTimer?.Stop();
            _pollTimer = null;
        }

        // Single round-trip JS that returns currentTime / duration / paused /
        // bounding rect (normalized 0..1 against the WebView client area), all
        // in one shot to keep the per-tick overhead tiny.
        private const string PollScript = @"
            (function() {
                var v = document.querySelector('video');
                if (!v) return null;
                var r = v.getBoundingClientRect();
                var w = window.innerWidth || document.documentElement.clientWidth || 1;
                var h = window.innerHeight || document.documentElement.clientHeight || 1;
                return {
                    t: v.currentTime || 0,
                    d: isFinite(v.duration) ? v.duration : 0,
                    paused: !!v.paused,
                    rx: r.left / w, ry: r.top / h,
                    rw: r.width / w, rh: r.height / h
                };
            })();
        ";

        private async Task PollAsync()
        {
            if (!_attached || _pollInFlight) return;
            _pollInFlight = true;
            try
            {
                // CoreWebView2 access must be inside the try: once the WebView2
                // control has been Disposed (typically during app shutdown), the
                // property throws ObjectDisposedException instead of returning
                // null, and a tick that landed mid-shutdown would route the
                // exception to DispatcherUnhandledException.
                if (_webView.CoreWebView2 == null) return;
                var json = await _webView.CoreWebView2.ExecuteScriptAsync(PollScript);
                if (string.IsNullOrEmpty(json) || json == "null") return;

                // Newtonsoft handles the JSON; CoreWebView2 returns it
                // already JSON-encoded (string in, string out, no extra parse).
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                _currentTimeSeconds = obj.Value<double?>("t") ?? 0;
                _durationSeconds = obj.Value<double?>("d") ?? 0;
                _isPlaying = !(obj.Value<bool?>("paused") ?? true);
                _videoRectNormalized[0] = obj.Value<double?>("rx") ?? 0;
                _videoRectNormalized[1] = obj.Value<double?>("ry") ?? 0;
                _videoRectNormalized[2] = obj.Value<double?>("rw") ?? 1;
                _videoRectNormalized[3] = obj.Value<double?>("rh") ?? 1;

                // Fire on time change OR duration change. HT pages load paused
                // with currentTime=0 and duration=NaN; once metadata loads,
                // duration becomes a real number but currentTime stays 0, so
                // a time-only check would never propagate "duration is now known"
                // until the user pressed play. Subscribers (the editor's
                // RebuildEffectVisuals) need to know about both.
                bool timeChanged = Math.Abs(_currentTimeSeconds - _lastTimeSeconds) > 0.001;
                bool durationChanged = Math.Abs(_durationSeconds - _lastDurationSeconds) > 0.001;
                if (timeChanged || durationChanged)
                {
                    _lastTimeSeconds = _currentTimeSeconds;
                    _lastDurationSeconds = _durationSeconds;
                    try { PlaybackTimeChanged?.Invoke(_currentTimeSeconds); }
                    catch (Exception ex) { App.Logger?.Debug("BrowserVideoTimeSource subscriber error: {Error}", ex.Message); }
                }
            }
            catch (ObjectDisposedException)
            {
                // WebView2 was disposed underneath us (app shutdown). Stop the
                // timer so we don't re-throw on every subsequent tick.
                Detach();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserVideoTimeSource poll error: {Error}", ex.Message);
            }
            finally
            {
                _pollInFlight = false;
            }
        }

        public double GetCurrentTimeSeconds() => _currentTimeSeconds;
        public double GetDurationSeconds() => _durationSeconds;
        public bool IsPlaying => _isPlaying;

        public void Seek(double seconds)
        {
            // try-catch swallows ObjectDisposedException too — the CoreWebView2
            // property throws if the WebView control was disposed mid-call.
            try
            {
                if (_webView.CoreWebView2 == null) return;
                var script = string.Format(CultureInfo.InvariantCulture,
                    "(function(){{var v=document.querySelector('video');if(v)v.currentTime={0:0.###};}})();",
                    Math.Max(0, seconds));
                _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideoTimeSource.Seek error: {Error}", ex.Message); }
        }

        public void Pause()
        {
            try
            {
                if (_webView.CoreWebView2 == null) return;
                _ = _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var v=document.querySelector('video');if(v)v.pause();})();");
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideoTimeSource.Pause error: {Error}", ex.Message); }
        }

        public void Play()
        {
            try
            {
                if (_webView.CoreWebView2 == null) return;
                _ = _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var v=document.querySelector('video');if(v)v.play();})();");
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideoTimeSource.Play error: {Error}", ex.Message); }
        }

        public Rect GetVideoRect()
        {
            try
            {
                // Map normalized video element rect through the WebView's
                // screen rect. Returns Rect.Empty if the WebView isn't laid
                // out yet (engine then skips gaze evaluation silently).
                if (_webView.ActualWidth <= 0 || _webView.ActualHeight <= 0) return Rect.Empty;
                var origin = _webView.PointToScreen(new Point(0, 0));
                var w = _webView.ActualWidth;
                var h = _webView.ActualHeight;
                return new Rect(
                    origin.X + _videoRectNormalized[0] * w,
                    origin.Y + _videoRectNormalized[1] * h,
                    _videoRectNormalized[2] * w,
                    _videoRectNormalized[3] * h);
            }
            catch { return Rect.Empty; }
        }

        public void Dispose() => Detach();
    }
}
