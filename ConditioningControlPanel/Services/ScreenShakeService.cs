using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Applies a brief jitter to every visible top-level window's content so a
    /// Deeper <c>screen_shake</c> action (or any caller) produces a visible
    /// camera-shake effect across the whole app — main window, Deeper Player,
    /// avatar tube, etc. Each tick applies the SAME random offset to every
    /// tracked window so they wobble together (not independently — that reads
    /// as separate shakes rather than one earthquake).
    ///
    /// Single-flight: starting a new shake while one is in progress restarts
    /// with the new intensity/duration; targets are re-snapshotted from the
    /// current window list at start time.
    /// </summary>
    public sealed class ScreenShakeService : IDisposable
    {
        private const int TickIntervalMs = 30;
        private const double MaxOffsetPx = 28.0; // at intensity=1.0

        private readonly Random _rng = new();
        private DispatcherTimer? _timer;
        // Tracked windows: each remembers its content target + the transform we
        // installed + the original transform (to restore on stop).
        private readonly List<TargetEntry> _targets = new();
        private DateTime _endsAt;
        private double _amplitude;
        private bool _disposed;

        public bool IsRunning => _timer?.IsEnabled == true;

        /// <summary>
        /// Shake every visible top-level window for <paramref name="durationMs"/>
        /// at <paramref name="intensity"/> (clamped to 0..1; 0 short-circuits).
        /// Safe to call from any thread; marshalled to the UI thread.
        /// </summary>
        public void Shake(double intensity, int durationMs)
        {
            if (_disposed) return;
            intensity = Math.Clamp(intensity, 0, 1);
            if (intensity <= 0 || durationMs <= 0) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess()) StartOnUi(intensity, durationMs);
            else dispatcher.BeginInvoke(() => StartOnUi(intensity, durationMs));
        }

        private void StartOnUi(double intensity, int durationMs)
        {
            try
            {
                // Re-snapshot targets every shake so windows opened/closed since
                // the last shake are picked up. Reset() restores any prior
                // transforms before we clear the list.
                Reset();

                var windows = Application.Current?.Windows;
                if (windows == null) return;

                foreach (Window win in windows)
                {
                    try
                    {
                        if (!win.IsVisible) continue;
                        if (win.Content is not FrameworkElement root) continue;
                        // Skip if some other code is mid-animating a transform on
                        // this root — clobbering would visibly fight that animation.
                        var prior = root.RenderTransform;
                        var transform = new TranslateTransform();
                        root.RenderTransform = transform;
                        _targets.Add(new TargetEntry(root, transform, prior));
                    }
                    catch { /* skip windows that throw on Content access */ }
                }

                _amplitude = MaxOffsetPx * intensity;
                _endsAt = DateTime.UtcNow.AddMilliseconds(durationMs);

                // RenderTransform does NOT propagate into WebView2's Chromium
                // surface (HwndHost lives outside WPF's render tree). When a
                // Deeper player or editor goes fullscreen, the WebView2 IS the
                // entire visible content of the fullscreen window, so the
                // WPF-only shake above is invisible there. Also drive a CSS
                // transform inside every visible WebView2 so the page content
                // (and anything mirrored from it via ScreenMirror) shakes too.
                foreach (Window win in windows)
                {
                    try
                    {
                        if (!win.IsVisible) continue;
                        TriggerWebViewShake(win, _amplitude, durationMs);
                    }
                    catch { }
                }

                if (_targets.Count == 0) return;

                if (_timer == null)
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(TickIntervalMs)
                    };
                    _timer.Tick += OnTick;
                }
                if (!_timer.IsEnabled) _timer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ScreenShakeService.Start error: {Error}", ex.Message);
            }
        }

        // Walks a window's visual tree, finds every WebView2 control, and
        // injects a self-contained CSS shake into each. The JS clears any
        // prior shake timer before starting so back-to-back shakes don't
        // double up. Self-expires on the page side after durationMs.
        private static void TriggerWebViewShake(Window win, double amplitudePx, int durationMs)
        {
            try
            {
                if (win.Content is not DependencyObject root) return;
                var webViews = new List<Microsoft.Web.WebView2.Wpf.WebView2>();
                FindWebViews(root, webViews);
                if (webViews.Count == 0) return;

                var amp = amplitudePx.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                var dur = durationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Target the <video> element directly when present. HT applies
                // :fullscreen rules with high specificity to the wrapper, which
                // a plain inline `style.transform` would not always override —
                // so we use setProperty(..., 'important'). Falls back to
                // documentElement for non-video pages. Picking the <video>
                // unconditionally also means the shake is visible in BOTH
                // embedded mode AND HTML5 fullscreen without re-querying
                // document.fullscreenElement (which can flip-flop between
                // wrapper and video element across HT player versions).
                var script = "(function(amp, durationMs){"
                    + "try{ if(window._ccpShakeTimer){ clearInterval(window._ccpShakeTimer); var p=window._ccpShakeTarget; if(p&&p.style){p.style.removeProperty('transform');} } }catch(e){}"
                    + "var t=document.querySelector('video')||document.fullscreenElement||document.documentElement; if(!t)return;"
                    + "window._ccpShakeTarget=t; var endsAt=Date.now()+durationMs;"
                    + "window._ccpShakeTimer=setInterval(function(){"
                    + "  if(Date.now()>=endsAt){ try{clearInterval(window._ccpShakeTimer);}catch(e){} window._ccpShakeTimer=null; try{t.style.removeProperty('transform');}catch(e){} return; }"
                    + "  var dx=(Math.random()*2-1)*amp, dy=(Math.random()*2-1)*amp;"
                    + "  try{ t.style.setProperty('transform','translate('+dx.toFixed(1)+'px,'+dy.toFixed(1)+'px)','important'); }catch(e){}"
                    + "},30);"
                    + "})(" + amp + "," + dur + ");";

                foreach (var wv in webViews)
                {
                    try
                    {
                        if (wv.CoreWebView2 == null) continue;
                        _ = wv.CoreWebView2.ExecuteScriptAsync(script);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void FindWebViews(DependencyObject root, List<Microsoft.Web.WebView2.Wpf.WebView2> results)
        {
            try
            {
                if (root is Microsoft.Web.WebView2.Wpf.WebView2 w)
                {
                    // Don't descend into the WebView's internal visual tree —
                    // nothing reachable inside is something we want to shake.
                    results.Add(w);
                    return;
                }
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    FindWebViews(VisualTreeHelper.GetChild(root, i), results);
                }
            }
            catch { }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                if (_targets.Count == 0 || DateTime.UtcNow >= _endsAt)
                {
                    Stop();
                    return;
                }
                // Same offset for every tracked window so the whole app shakes
                // as a unit. Random jitter in [-amp, +amp]; symmetric so the
                // average drift stays near zero — windows don't crawl.
                var dx = (_rng.NextDouble() * 2 - 1) * _amplitude;
                var dy = (_rng.NextDouble() * 2 - 1) * _amplitude;
                foreach (var t in _targets)
                {
                    try { t.Transform.X = dx; t.Transform.Y = dy; }
                    catch { /* target may be torn down mid-shake */ }
                }
            }
            catch { /* swallow per-tick errors */ }
        }

        private void Stop()
        {
            try { _timer?.Stop(); } catch { }
            // Zero-out before restoring prior transforms so nothing sticks if a
            // caller swapped out the prior transform between snapshots.
            foreach (var t in _targets)
            {
                try { t.Transform.X = 0; t.Transform.Y = 0; }
                catch { }
            }
        }

        private void Reset()
        {
            Stop();
            // Restore each target's pre-shake RenderTransform if our installed
            // transform is still the active one (we don't want to overwrite
            // something the app started using mid-shake).
            foreach (var t in _targets)
            {
                try
                {
                    if (ReferenceEquals(t.Target.RenderTransform, t.Transform))
                        t.Target.RenderTransform = t.Prior ?? Transform.Identity;
                }
                catch { }
            }
            _targets.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Reset(); } catch { }
            _timer = null;
        }

        private sealed class TargetEntry
        {
            public FrameworkElement Target { get; }
            public TranslateTransform Transform { get; }
            public Transform? Prior { get; }
            public TargetEntry(FrameworkElement target, TranslateTransform t, Transform? prior)
            {
                Target = target;
                Transform = t;
                Prior = prior;
            }
        }
    }
}
