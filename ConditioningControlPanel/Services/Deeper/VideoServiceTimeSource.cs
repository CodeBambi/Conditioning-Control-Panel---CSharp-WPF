using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Adapts <see cref="VideoService"/>'s primary-monitor playback to the
    /// generic <see cref="IPlaybackTimeSource"/> the EnhancementEngine consumes.
    ///
    /// Subscribes lazily — call <see cref="Attach"/> when starting an engine,
    /// <see cref="Detach"/> when stopping. Multiple sources can be attached to
    /// one VideoService at once (each gets the same time stream).
    /// </summary>
    public sealed class VideoServiceTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly VideoService _video;
        private bool _attached;

        // Display aspect ratio (w/h, SAR-corrected) of the playing video, cached
        // once LibVLC reports a non-zero size. -1 = not known yet. The bridge
        // builds a fresh time source per video, so this never goes stale across
        // videos.
        private double _cachedAspect = -1;

        public event Action<double>? PlaybackTimeChanged;

        public VideoServiceTimeSource(VideoService video)
        {
            _video = video ?? throw new ArgumentNullException(nameof(video));
        }

        public void Attach()
        {
            if (_attached) return;
            _video.PrimaryPlaybackTimeMsChanged += OnTimeMs;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            _video.PrimaryPlaybackTimeMsChanged -= OnTimeMs;
            _attached = false;
        }

        private void OnTimeMs(long ms)
        {
            // LibVLC raises TimeChanged on its own (non-UI) thread. The other two
            // IPlaybackTimeSource implementations (audio timer, WebView2) raise their
            // tick on the UI thread, and the EnhancementEngine assumes that: it mutates
            // its band/fired-state collections from this tick AND from webcam handlers
            // (which marshal to the UI thread). Forwarding the raw LibVLC-thread callback
            // straight through would race those collections and dispatch WPF effects
            // off-thread. Marshal to the UI thread so this source matches the others.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess())
            {
                Fire(ms);
            }
            else
            {
                try { dispatcher.BeginInvoke(new Action(() => Fire(ms))); }
                catch (Exception ex) { App.Logger?.Debug("VideoServiceTimeSource marshal error: {Error}", ex.Message); }
            }
        }

        private void Fire(long ms)
        {
            try { PlaybackTimeChanged?.Invoke(ms / 1000.0); }
            catch (Exception ex) { App.Logger?.Debug("VideoServiceTimeSource handler error: {Error}", ex.Message); }
        }

        public double GetCurrentTimeSeconds()
        {
            var ms = _video.GetCurrentPlaybackTimeMs();
            return ms < 0 ? 0 : ms / 1000.0;
        }

        public double GetDurationSeconds()
        {
            try
            {
                var len = _video.PrimaryMediaPlayer?.Length ?? 0;
                return len > 0 ? len / 1000.0 : 0;
            }
            catch { return 0; }
        }

        public bool IsPlaying
        {
            get
            {
                try { return _video.PrimaryMediaPlayer?.IsPlaying ?? false; }
                catch { return false; }
            }
        }

        public void Seek(double seconds) => _video.SeekPrimary((long)Math.Max(0, seconds * 1000));
        public void Pause() => _video.PausePrimary();
        public void Play() => _video.PlayPrimary();

        public Rect GetVideoRect()
        {
            try
            {
                var w = _video.PrimaryVideoWindow;
                if (w == null) return Rect.Empty;
                if (w.ActualWidth <= 0 || w.ActualHeight <= 0) return Rect.Empty;

                // The video window is borderless + maximized, so its
                // Left/Top/Width/Height report stale restore bounds (~400x300 in
                // the corner). Derive the true on-screen client rect in DIPs from
                // the rendered size (ActualWidth/Height are already DIPs) and the
                // PointToScreen origin (device px -> DIPs via this window's DPI).
                // This also avoids the screen-bounds-vs-working-area ambiguity a
                // maximized borderless window has.
                var dpi = VisualTreeHelper.GetDpi(w);
                var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
                var originPx = w.PointToScreen(new Point(0, 0));
                var outer = new Rect(originPx.X / sx, originPx.Y / sy, w.ActualWidth, w.ActualHeight);

                var aspect = GetVideoAspect();
                if (aspect <= 0) return outer; // native size not known yet -> full area

                return FitContain(outer, aspect);
            }
            catch { return Rect.Empty; }
        }

        /// <summary>
        /// Contain-fit (letterbox/pillarbox) the content of the given aspect
        /// ratio inside <paramref name="outer"/>, returning the rendered picture
        /// box. Pure helper — the one piece reusable for the browser path later.
        /// </summary>
        internal static Rect FitContain(Rect outer, double contentAspect)
        {
            if (outer.Width <= 0 || outer.Height <= 0 || contentAspect <= 0) return outer;
            var outerAspect = outer.Width / outer.Height;
            double w, h, offX, offY;
            if (contentAspect >= outerAspect)
            {
                // Wider than the frame -> full width, bars top & bottom.
                w = outer.Width;
                h = outer.Width / contentAspect;
                offX = 0;
                offY = (outer.Height - h) / 2.0;
            }
            else
            {
                // Narrower than the frame -> full height, bars left & right.
                h = outer.Height;
                w = outer.Height * contentAspect;
                offX = (outer.Width - w) / 2.0;
                offY = 0;
            }
            return new Rect(outer.X + offX, outer.Y + offY, w, h);
        }

        // Lazily resolve the display aspect ratio of the playing video. Native
        // size is NOT valid at VideoStarted (fires before first frame), so this
        // returns 0 until LibVLC has a frame; GetVideoRect then falls back to the
        // full-screen rect. Cached once known.
        private double GetVideoAspect()
        {
            if (_cachedAspect > 0) return _cachedAspect;
            try
            {
                var mp = _video.PrimaryMediaPlayer;
                if (mp == null) return 0;

                // Preferred: SAR-corrected display aspect from the video track.
                var media = mp.Media;
                if (media?.Tracks != null)
                {
                    foreach (var track in media.Tracks)
                    {
                        if (track.TrackType != LibVLCSharp.Shared.TrackType.Video) continue;
                        var v = track.Data.Video;
                        if (v.Width == 0 || v.Height == 0) continue;
                        double num = v.Width * (v.SarNum > 0 ? v.SarNum : 1u);
                        double den = v.Height * (v.SarDen > 0 ? v.SarDen : 1u);
                        if (num > 0 && den > 0)
                        {
                            _cachedAspect = num / den;
                            return _cachedAspect;
                        }
                    }
                }

                // Fallback: decoded output size (square-pixel assumption).
                uint px = 0, py = 0;
                if (mp.Size(0, ref px, ref py) && px > 0 && py > 0)
                {
                    _cachedAspect = (double)px / py;
                    return _cachedAspect;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoServiceTimeSource.GetVideoAspect error: {Error}", ex.Message);
            }
            return 0;
        }

        public void Dispose() => Detach();
    }
}
