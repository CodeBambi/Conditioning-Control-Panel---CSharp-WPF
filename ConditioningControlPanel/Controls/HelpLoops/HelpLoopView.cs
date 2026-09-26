using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Plays a <see cref="HelpLoopScene"/>: three DrawingVisuals (the stage ground, the desktop layer,
    /// which alone can blur, and the front layer) in the 480x270 stage space, scaled uniformly to the element's width (16:9)
    /// and clipped to an 8px rounded rect. Native drawing on purpose: the ? popover is a layered
    /// window (AllowsTransparency) and WebView2 cannot paint in one.
    ///
    /// Ticks on CompositionTarget.Rendering only while loaded AND visible. Motion Full = real time,
    /// Reduced = half speed, Off = the scene's still frame, drawn once, no clock at all.
    /// </summary>
    public sealed class HelpLoopView : FrameworkElement
    {
        public static readonly DependencyProperty SceneIdProperty = DependencyProperty.Register(
            nameof(SceneId), typeof(string), typeof(HelpLoopView),
            new PropertyMetadata(null, (d, _) => ((HelpLoopView)d).OnSceneIdChanged()));

        private readonly ContainerVisual _root = new();
        private readonly DrawingVisual _ground = new();   // never blurred
        private readonly DrawingVisual _back = new();
        private readonly DrawingVisual _front = new();
        private readonly BlurEffect _blur = new() { Radius = 0, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        private readonly Stopwatch _clock = new();

        private HelpLoopScene? _scene;
        private LoopPalette? _palette;
        private bool _hooked;
        private bool _failed;
        private double _base;          // loop time at the moment the clock last (re)started
        private double _speed = 1;
        private double _lastT = -1;

        public HelpLoopView()
        {
            _root.Children.Add(_ground);
            _root.Children.Add(_back);
            _root.Children.Add(_front);
            AddVisualChild(_root);
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            Loaded += (_, _) => UpdateRunning();
            Unloaded += (_, _) => UpdateRunning();
            IsVisibleChanged += (_, _) => UpdateRunning();
        }

        public HelpLoopView(HelpLoopScene scene) : this()
        {
            SetScene(scene);
        }

        /// <summary>Section id of the scene to play (registry lookup).</summary>
        public string? SceneId
        {
            get => (string?)GetValue(SceneIdProperty);
            set => SetValue(SceneIdProperty, value);
        }

        public HelpLoopScene? Scene => _scene;

        /// <summary>True once the scene threw; the view stopped and keeps its last frame.</summary>
        public bool Failed => _failed;

        /// <summary>The loop time of the frame last drawn.</summary>
        public double CurrentTime { get; private set; }

        /// <summary>Raised after every drawn frame with its loop time (the steps strip listens).</summary>
        public event Action<double>? TimeChanged;

        /// <summary>Raised when the scene changes.</summary>
        public event Action? SceneChanged;

        private void OnSceneIdChanged()
        {
            if (HelpLoopRegistry.TryGet(SceneId, out var scene)) SetScene(scene);
            else SetScene(null);
        }

        public void SetScene(HelpLoopScene? scene)
        {
            _scene = scene;
            _failed = false;
            _lastT = -1;
            _base = 0;
            _clock.Reset();
            SceneChanged?.Invoke();
            UpdateRunning();
            if (!_hooked) DrawStill();
        }

        // ---------------------------------------------------------------- layout

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) =>
            index == 0 ? _root : throw new ArgumentOutOfRangeException(nameof(index));

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? LoopFrame.StageWidth : available.Width;
            double h = w * LoopFrame.StageHeight / LoopFrame.StageWidth;
            if (!double.IsInfinity(available.Height) && h > available.Height)
            {
                h = available.Height;
                w = h * LoopFrame.StageWidth / LoopFrame.StageHeight;
            }
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double s = finalSize.Width / LoopFrame.StageWidth;
            if (finalSize.Height > 0) s = Math.Min(s, finalSize.Height / LoopFrame.StageHeight);
            if (s <= 0 || double.IsNaN(s)) s = 1;
            _root.Transform = new ScaleTransform(s, s);
            _root.Clip = new RectangleGeometry(new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight), 8 / s, 8 / s);
            return finalSize;
        }

        // ---------------------------------------------------------------- clock

        private void UpdateRunning()
        {
            bool want = _scene != null && !_failed && IsLoaded && IsVisible
                        && MotionFx.Level != MotionLevel.Off;
            if (want && !_hooked)
            {
                _speed = MotionFx.Level == MotionLevel.Reduced ? .5 : 1;
                _base = CurrentTime;
                _clock.Restart();
                CompositionTarget.Rendering += OnRendering;
                _hooked = true;
                RenderAt(CurrentTime);
            }
            else if (!want && _hooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _hooked = false;
                _clock.Stop();
                if (_scene != null && MotionFx.Level == MotionLevel.Off) DrawStill();
            }
            else if (!want && IsLoaded)
            {
                DrawStill();
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_scene == null) return;
            double t = (_base + _clock.Elapsed.TotalMilliseconds * _speed) % _scene.DurationMs;
            RenderAt(t);
        }

        /// <summary>The motion-Off frame: the scene's still, drawn once.</summary>
        public void DrawStill()
        {
            if (_scene == null) { ClearVisuals(); return; }
            _scene.Reset();
            _lastT = -1;
            RenderAt(_scene.StillMs);
        }

        /// <summary>Draws the scene at loop time <paramref name="t"/> (also the test entry point).</summary>
        public void RenderAt(double t)
        {
            if (_scene == null || _failed) return;
            try
            {
                if (t < _lastT) _scene.Reset();
                _lastT = t;
                CurrentTime = t;

                _palette ??= new LoopPalette(HelpTooltipBuilder.FindThemeResource<Brush>(this, "PinkBrush"));
                double ppd = 1.0;
                try { ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

                LoopFrame f;
                using (var ground = _ground.RenderOpen())
                using (var back = _back.RenderOpen())
                using (var front = _front.RenderOpen())
                {
                    f = new LoopFrame(ground, back, front, _palette, ppd);
                    f.Ground();
                    _scene.Draw(f, t);
                }

                if (f.BackBlur > 0.05)
                {
                    _blur.Radius = f.BackBlur;
                    if (_back.Effect == null) _back.Effect = _blur;
                }
                else if (_back.Effect != null)
                {
                    _back.Effect = null;
                }
                TimeChanged?.Invoke(t);
            }
            catch (Exception ex)
            {
                // Never throw out of Rendering: log once, stop this loop, leave the last frame up.
                _failed = true;
                if (_hooked)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    _hooked = false;
                }
                App.Logger?.Error(ex, "HelpLoopView: scene {Scene} failed at t={T}", _scene?.Id, t);
            }
        }

        private void ClearVisuals()
        {
            using (_ground.RenderOpen()) { }
            using (_back.RenderOpen()) { }
            using (_front.RenderOpen()) { }
        }
    }
}
