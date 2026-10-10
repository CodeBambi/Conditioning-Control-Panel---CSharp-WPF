using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Focus Gaze on this head: WPF GazeFocusService's window half over the Core <see cref="GazeFocusEngine"/>.
    /// Look at a bubble or a flash to pop it (dwell), or blink while looking near it. Runs only while the
    /// tracker runs WITH a calibration and current consent, and only when something wants it (the Play
    /// switch, or a per-feature gaze option). It never opens the camera itself: the switch does, after
    /// consent, and panic stops the camera, which stands this down (WPF EvaluateDesiredState).
    ///
    /// Spaces: the tracker's gaze point is DIPs local to the calibrated monitor; overlays live in desktop
    /// pixels. Everything is converted to "desktop pixels / the calibrated monitor's scale" so the WPF
    /// sigmas (DIPs) keep their meaning.
    /// </summary>
    internal sealed class GazeFocusHead
    {
        internal static GazeFocusHead Instance { get; } = new();

        internal readonly GazeFocusEngine Engine = new();
        /// <summary>Tests step this clock and hand in targets.</summary>
        internal Func<DateTime> Now = () => DateTime.UtcNow;
        internal Func<IReadOnlyList<IGazeTarget>>? TargetsOverride;
        /// <summary>Tests: answer "can run" without a tracker (null = the real tracker).</summary>
        internal Func<bool>? CanRunOverride;

        private DispatcherTimer? _timer;
        private bool _wired;
        private bool _masterEnabled;
        private AppSettings? _watched;

        public bool IsActive { get; private set; }
        public event Action<bool>? OnActiveChanged;

        /// <summary>The Play switch. Per-feature consumers (flash / bubble gaze options) also start it.</summary>
        public bool MasterEnabled
        {
            get => _masterEnabled;
            set { if (_masterEnabled == value) return; _masterEnabled = value; EvaluateDesiredState(); }
        }

        /// <summary>App start: follow the tracker and the consumer settings. UI thread.</summary>
        internal void Wire()
        {
            if (_wired) return;
            _wired = true;
            WebcamTracker.Instance.StateChanged += EvaluateDesiredState;
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += () => Dispatcher.UIThread.Post(WatchSettings);
            WatchSettings();
        }

        private void WatchSettings()
        {
            if (_watched != null) _watched.PropertyChanged -= OnSettingsChanged;
            _watched = CoreSettings.Current;
            _watched.PropertyChanged += OnSettingsChanged;
            EvaluateDesiredState();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.FlashGazePopEnabled):
                case nameof(AppSettings.FlashGazeLingerEnabled):
                case nameof(AppSettings.BubbleGazePopEnabled):
                case nameof(AppSettings.VideoGazeClickEnabled):
                    Dispatcher.UIThread.Post(EvaluateDesiredState);
                    break;
            }
        }

        private static bool AnyConsumerOn()
        {
            var s = CoreSettings.Current;
            return s.FlashGazePopEnabled || s.FlashGazeLingerEnabled || s.BubbleGazePopEnabled || s.VideoGazeClickEnabled;
        }

        private bool BubblesGazeEnabled => _masterEnabled || CoreSettings.Current.BubbleGazePopEnabled;

        internal bool CanRun()
        {
            if (CanRunOverride is { } o) return o();
            var t = WebcamTracker.Instance;
            return t.IsRunning && t.Calibration != null && WebcamConsent.IsCurrent(CoreSettings.Current);
        }

        /// <summary>WPF EvaluateDesiredState: active exactly when wanted AND able.</summary>
        public void EvaluateDesiredState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(EvaluateDesiredState); return; }
            bool wants = _masterEnabled || AnyConsumerOn();
            if (wants && CanRun()) { if (!IsActive) Start(); }
            else if (IsActive) Stop();
        }

        private void Start()
        {
            var t = WebcamTracker.Instance;
            t.OnGazeMove += OnGaze;
            t.OnFaceLost += Engine.FaceLost;
            t.OnFaceFound += Engine.FaceFound;
            t.OnBlink += OnBlink;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(GazeFocusEngine.TickMs) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            IsActive = true;
            try { OnActiveChanged?.Invoke(true); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GazeFocus: active");
        }

        public void Stop()
        {
            if (!IsActive) return;
            var t = WebcamTracker.Instance;
            t.OnGazeMove -= OnGaze;
            t.OnFaceLost -= Engine.FaceLost;
            t.OnFaceFound -= Engine.FaceFound;
            t.OnBlink -= OnBlink;
            try { _timer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _timer = null;
            Engine.Reset();
            IsActive = false;
            try { OnActiveChanged?.Invoke(false); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GazeFocus: inactive");
        }

        private static (double OriginX, double OriginY, double Scale) CalSpace()
        {
            var mb = WebcamTracker.Instance.Calibration?.MonitorBounds;
            double scale = mb is { DpiScale: > 0 } ? mb.DpiScale : 1;
            return (mb?.X ?? 0, mb?.Y ?? 0, scale);
        }

        internal void OnGaze(Point p)
        {
            var (ox, oy, scale) = CalSpace();
            Engine.GazeMoved(ox / scale + p.X, oy / scale + p.Y);
        }

        private GazeFocusOptions Options()
        {
            var s = CoreSettings.Current;
            return new GazeFocusOptions(BubblesGazeEnabled, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGazeLingerExtensionMs);
        }

        internal void Tick()
        {
            try { Engine.Tick(Now(), Targets(), Options()); }
            catch (Exception ex) { Log.Debug("GazeFocus tick error: {Error}", ex.Message); }
        }

        private void OnBlink()
        {
            try { Engine.Blink(Now(), Targets(), Options()); }
            catch (Exception ex) { Log.Debug("GazeFocus blink error: {Error}", ex.Message); }
        }

        private IReadOnlyList<IGazeTarget> Targets()
        {
            if (TargetsOverride is { } o) return o();
            var list = new List<IGazeTarget>();
            var (ox, oy, scale) = CalSpace();
            var s = CoreSettings.Current;
            var mb = WebcamTracker.Instance.Calibration?.MonitorBounds;
            // WPF TargetOnCalScreen: with the restrict switch on, only content on the calibrated monitor.
            bool restrict = s.RestrictGazeContentToCalibratedScreen && mb is { Width: > 0, Height: > 0 };
            bool OnCal(double x, double y, double w, double h)
            {
                if (!restrict) return true;
                double cx = x + w / 2, cy = y + h / 2;
                return cx >= ox && cy >= oy && cx < ox + mb!.Width * scale && cy < oy + mb.Height * scale;
            }

            foreach (var w in FlashOverlay.GazeTargets())
            {
                try
                {
                    var pos = w.Position;
                    double rs = w.RenderScaling > 0 ? w.RenderScaling : 1;
                    double pw = w.ClientSize.Width * rs, ph = w.ClientSize.Height * rs;
                    if (pw <= 0 || ph <= 0 || !OnCal(pos.X, pos.Y, pw, ph)) continue;
                    list.Add(new FlashTarget(w, (pos.X / scale, pos.Y / scale, pw / scale, ph / scale)));
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            if (BubblesGazeEnabled && BubbleOverlay.IsRunning)
                foreach (var b in BubbleOverlay.Field.Bubbles)
                {
                    if (b.Popping) continue;
                    double bs = BubbleOverlay.ScalingOf(b.Screen);
                    double x = b.X * bs, y = b.Y * bs, size = b.Size * bs;
                    if (!OnCal(x, y, size, size)) continue;
                    list.Add(new BubbleTarget(b, (x / scale, y / scale, size / scale, size / scale)));
                }
            return list;
        }

        private sealed class FlashTarget(FlashOverlayWindow w, (double, double, double, double) bounds) : IGazeTarget
        {
            public GazeTargetKind Kind => GazeTargetKind.Flash;
            public object Key => w;
            public (double X, double Y, double W, double H) Bounds => bounds;
            public void SetDwellProgress(double t01) { }   // ponytail: WPF swells the flash as the dwell fills
            public void Activate() => FlashOverlay.GazePop(w);
            public void BoostLifetime(int extraMs) => w.BoostLifetime(extraMs);
        }

        private sealed class BubbleTarget(AmbientBubble b, (double, double, double, double) bounds) : IGazeTarget
        {
            public GazeTargetKind Kind => GazeTargetKind.Bubble;
            public object Key => b;
            public (double X, double Y, double W, double H) Bounds => bounds;
            public void SetDwellProgress(double t01) => b.GazeDwell = Math.Clamp(t01, 0, 1);   // WPF: swells to 1.25x
            public void Activate() { if (!b.Popping) BubbleOverlay.Pop(b); }
            public void BoostLifetime(int extraMs) { }
        }
    }
}
