using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Lights Down, the add-on to the mandatory video: the longer the picture is watched
    /// (cursor on it, or gaze when the tracker is already running and calibrated) the more the
    /// room falls away. Rides <see cref="VideoService"/>'s own lifecycle: it starts on
    /// VideoStarted and ends on VideoEnded, on the tick the video stops playing or begins its
    /// teardown, and with the video windows themselves (each overlay is OWNED by one). Panic and
    /// the emergency exit tear the video down, so they end this on the same frame. Click-through
    /// and non-activating: the video keeps every click, key, pause and seek it had.
    /// </summary>
    internal static class LightsDownService
    {
        private static VideoService? _video;
        private static readonly List<LightsDownOverlay> _overlays = new();
        private static readonly List<Rect> _cut = new();
        private static readonly Stopwatch _clock = new();
        private static LightsDownState? _state;
        private static bool _hooked, _gazeHooked, _idle;
        private static double _last, _nextCut, _nextProbe, _aspect;
        private static double _gazeX, _gazeY, _gazeAt = double.NegativeInfinity;

        /// <summary>Wire once, after the VideoService exists. Costs nothing until a video starts with the switch on.</summary>
        public static void Attach(VideoService video)
        {
            if (_video != null) return;
            _video = video;
            video.VideoStarted += (_, _) => OnUi(TryStart);
            video.VideoEnded += (_, _) => OnUi(Stop);
            SuperAccess.Changed += e =>
            {
                if (e != SuperEffect.LightsDown) return;
                OnUi(() => { if (SuperAccess.IsOn(SuperEffect.LightsDown)) TryStart(); else Stop(); });
            };
        }

        private static bool Wanted(VideoService v)
            => LightsDownMath.ShouldRun(SuperAccess.IsOn(SuperEffect.LightsDown), v.IsPlaying, v.IsCleaningUp, v.HasOpenWindows);

        private static void TryStart()
        {
            var v = _video;
            if (v == null || _overlays.Count > 0 || !Wanted(v)) return;
            try
            {
                var tier = PerformanceProfile.CurrentTier;
                bool glow = PerformanceProfile.AllowGlow(tier);
                double glowCap = tier == PerformanceTier.Quality ? 116 : 60;
                _aspect = ProbeAspect(v);
                foreach (var h in v.GetVideoWindowHandles())
                {
                    if (h == IntPtr.Zero || !IsWindow(h)) continue;
                    var screen = Screen.FromHandle(h);
                    double dpi = BubbleCountWindow.GetDpiForScreen(screen);
                    var owner = HwndSource.FromHwnd(h)?.RootVisual as Window;
                    var o = new LightsDownOverlay(h, screen.Bounds, dpi, _aspect, owner, glow, glowCap);
                    o.Closed += (_, _) => _overlays.Remove(o);
                    _overlays.Add(o);
                    o.Show();
                }
                if (_overlays.Count == 0) return;
                _state = new LightsDownState();
                _clock.Restart();
                _last = 0; _nextCut = 0; _nextProbe = 0.5; _idle = false;
                if (App.Webcam != null && !_gazeHooked) { App.Webcam.OnGazeMove += OnGaze; _gazeHooked = true; }
                if (!_hooked) { CompositionTarget.Rendering += OnRendering; _hooked = true; }
                App.Logger?.Information("Super Lights Down: riding {N} video window(s), aspect {A:0.000}", _overlays.Count, _aspect);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Super Lights Down: could not start, the video plays without it");
                Stop();
            }
        }

        /// <summary>Tear everything down at once. Idempotent; never touches the video itself
        /// beyond handing back the transform the swell borrowed.</summary>
        public static void Stop()
        {
            if (_hooked) { CompositionTarget.Rendering -= OnRendering; _hooked = false; }
            if (_gazeHooked && App.Webcam != null) { App.Webcam.OnGazeMove -= OnGaze; }
            _gazeHooked = false;
            _gazeAt = double.NegativeInfinity;
            foreach (var o in _overlays.ToArray())
            {
                try { o.RestoreVideo(); o.Close(); } catch { }
            }
            _overlays.Clear();
            _state = null;
            _clock.Reset();
        }

        /// <summary>
        /// Called by VideoService.CloseAll, the one funnel every teardown reaches (panic, emergency
        /// exit, skip, natural end, attention retry). The Rendering tick would also notice, but
        /// CloseAll can hold the dispatcher in a non-pumping wait for up to 500 ms first, so the
        /// dark goes the moment teardown begins instead. Does nothing when Lights Down is not up.
        /// </summary>
        internal static void OnVideoTeardown()
        {
            if (!IsActive) return;
            OnUi(Stop);
        }

        /// <summary>True while an overlay is up or the frame hook is armed.</summary>
        internal static bool IsActive => _hooked || _overlays.Count > 0;

        private static void OnRendering(object? sender, EventArgs e)
        {
            var v = _video;
            var s = _state;
            if (v == null || s == null || _overlays.Count == 0 || !Wanted(v)) { Stop(); return; }

            double now = _clock.Elapsed.TotalSeconds;
            var motion = MotionFx.Level switch
            {
                MotionLevel.Off => LightsDownMotion.Off,
                MotionLevel.Reduced => LightsDownMotion.Reduced,
                _ => LightsDownMotion.Full,
            };
            // 30 fps is plenty for slow light and halves what a fullscreen layered window costs;
            // the Performance tier drops to 20.
            double step = PerformanceProfile.CurrentTier == PerformanceTier.Performance ? 1.0 / 20 : 1.0 / 30;
            if (now - _last < step) return;
            double dt = now - _last;
            _last = now;

            if (now >= _nextProbe && _aspect <= 0)
            {
                _nextProbe = now + 0.5;
                _aspect = ProbeAspect(v);
                if (_aspect > 0) foreach (var o in _overlays) o.SetAspect(_aspect);
            }

            bool attended = IsAttended(v, now);
            bool photosafe = App.Settings?.Current?.LockdownPhotosafe == true;
            var f = s.Step(dt, attended, motion, photosafe);

            if (now >= _nextCut)
            {
                _nextCut = now + 0.1;
                v.CopyAttentionBounds(_cut);
                foreach (var o in _overlays) o.SetCutouts(_cut);
            }

            // At rest nothing changes, so nothing is written and the layered window never repaints.
            bool rest = f.Depth <= 0 && f.LockRun < 0 && f.SplitAlpha <= 0 && f.StreakAlpha <= 0;
            if (rest && _idle) return;
            _idle = rest;
            bool particles = MotionFx.AllowParticles;
            double moteSpeed = motion == LightsDownMotion.Reduced ? 0.5 : 1;
            foreach (var o in _overlays) o.Apply(f, s.Time, dt, particles, moteSpeed);
        }

        private static bool IsAttended(VideoService v, double now)
        {
            bool cursorOn = false, gazeOn = false;
            bool cursorKnown = GetCursorPos(out var cp);
            var webcam = App.Webcam;
            bool gazeLive = webcam != null && webcam.IsRunning && webcam.Calibration != null && now - _gazeAt < 0.5;
            foreach (var o in _overlays)
            {
                if (cursorKnown && o.PictureContainsPx(cp.X, cp.Y)) cursorOn = true;
                if (gazeLive && o.PictureContainsPx(_gazeX, _gazeY)) gazeOn = true;
            }
            return LightsDownMath.Attended(v.IsGracePaused, gazeLive, gazeOn, cursorOn);
        }

        /// <summary>Gaze arrives in DIPs local to the calibrated monitor; keep it in physical pixels.</summary>
        private static void OnGaze(Point p)
        {
            try
            {
                var webcam = App.Webcam;
                double ox = 0, oy = 0, dpi;
                if (webcam != null && webcam.Calibration?.MonitorBounds is { DeviceName: not null } mb
                    && webcam.TryGetCalibratedBounds(out var physical))
                {
                    dpi = mb.DpiScale is > 0.25 and < 8.0 ? mb.DpiScale : 1.0;
                    ox = physical.X; oy = physical.Y;
                }
                else dpi = Screen.PrimaryScreen is { } ps ? BubbleCountWindow.GetDpiForScreen(ps) : 1.0;
                _gazeX = ox + p.X * dpi;
                _gazeY = oy + p.Y * dpi;
                _gazeAt = _clock.Elapsed.TotalSeconds;
            }
            catch { }
        }

        /// <summary>The picture's display aspect: the browser page's meta, else the LibVLC track
        /// (SAR corrected). 0 until known; the overlay then treats the picture as full screen.</summary>
        private static double ProbeAspect(VideoService v)
        {
            try
            {
                if (v.BrowserVideoAspect > 0) return v.BrowserVideoAspect;
                var mp = v.PrimaryMediaPlayer;
                if (mp == null) return 0;
                // MediaPlayer.Media hands back a NEW ref-counted wrapper on every read (#1196).
                using var media = mp.Media;
                if (media?.Tracks == null) return 0;
                double best = 0; long area = 0;
                foreach (var t in media.Tracks)
                {
                    if (t.TrackType != LibVLCSharp.Shared.TrackType.Video) continue;
                    var vt = t.Data.Video;
                    if (vt.Width == 0 || vt.Height == 0) continue;
                    long a = (long)vt.Width * vt.Height;
                    if (a <= area) continue;
                    area = a;
                    best = vt.Width * (double)(vt.SarNum > 0 ? vt.SarNum : 1u) / (vt.Height * (double)(vt.SarDen > 0 ? vt.SarDen : 1u));
                }
                return best;
            }
            catch { return 0; }
        }

        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) a();
            else d.BeginInvoke(a);
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    }
}
