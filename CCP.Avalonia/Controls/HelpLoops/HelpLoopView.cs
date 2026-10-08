using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>
    /// Plays a <see cref="HelpLoopScene"/> (WPF Controls/HelpLoops/HelpLoopView.cs): scales the
    /// 480x270 stage to its width, keeps the aspect, clips to 8px corners. Runs one frame per
    /// display frame only while attached, effectively visible and motion is not Off (Reduced plays
    /// at half speed); otherwise it shows the scene's still frame. A scene that throws is logged
    /// once and frozen, never re-thrown into the renderer.
    /// </summary>
    public sealed class HelpLoopView : Control
    {
        /// <summary>The clock frames are timed by; tests step it.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        /// <summary>The display-frame source; tests queue the callbacks instead.</summary>
        internal static Action<TopLevel, Action<TimeSpan>> RequestAnimationFrame = (top, cb) => top.RequestAnimationFrame(cb);

        private LoopPalette? _palette;
        private IDisposable? _visibilityWatch;
        private bool _running;
        private int _generation;               // bumped on every start/stop: stale frame callbacks drop out
        private DrawingGroup? _lastBack, _lastFront;   // the last good frame, kept up if the scene faults
        private long _startTs;
        private double _base, _speed = 1, _lastT = -1;

        public HelpLoopView(HelpLoopScene scene)
        {
            Scene = scene;
            ClipToBounds = true;
            CurrentTime = scene.StillMs;
        }

        public HelpLoopScene Scene { get; }
        public bool Failed { get; private set; }
        public bool IsRunning => _running;
        public double CurrentTime { get; private set; }

        /// <summary>Raised with the loop time each frame; the step chips light from it.</summary>
        public event Action<double>? TimeChanged;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _visibilityWatch?.Dispose();
            _visibilityWatch = EffectiveVisibility.Watch(this, UpdateRunning);
            UpdateRunning();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _visibilityWatch?.Dispose();
            _visibilityWatch = null;
            base.OnDetachedFromVisualTree(e);
            UpdateRunning();
        }

        private bool WantsToRun =>
            !Failed && IsEffectivelyVisible && TopLevel.GetTopLevel(this) != null
            && CoreSettings.Current.MotionLevel != MotionLevel.Off;

        /// <summary>Starts or stops the frame loop to match <see cref="WantsToRun"/>.</summary>
        public void UpdateRunning()
        {
            if (WantsToRun && !_running)
            {
                _speed = CoreSettings.Current.MotionLevel == MotionLevel.Reduced ? .5 : 1;
                _base = CurrentTime;
                _startTs = Time.GetTimestamp();
                _running = true;
                _generation++;
                RequestFrame();
            }
            else if (!WantsToRun && _running)
            {
                _running = false;
                _generation++;
            }
            if (!_running && CoreSettings.Current.MotionLevel == MotionLevel.Off) DrawStill();
        }

        /// <summary>Asks for one frame. A close and reopen inside one frame leaves the old callback
        /// pending; its generation no longer matches, so only one chain ever runs.</summary>
        private void RequestFrame()
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            var generation = _generation;
            RequestAnimationFrame(top, _ => { if (generation == _generation) Tick(); });
        }

        /// <summary>One display frame: advance the loop clock and ask for the next frame, or stop if
        /// the view went away since the last one.</summary>
        internal void Tick()
        {
            if (!_running) return;
            if (!WantsToRun) { _running = false; _generation++; return; }
            var ms = Time.GetElapsedTime(_startTs).TotalMilliseconds;
            RenderAt((_base + ms * _speed) % Scene.DurationMs);
            if (_running) RequestFrame();
        }

        public void DrawStill()
        {
            Scene.Reset();
            _lastT = -1;
            RenderAt(Scene.StillMs);
        }

        public void RenderAt(double t)
        {
            if (Failed) return;
            if (t < _lastT) Scene.Reset();
            _lastT = t;
            CurrentTime = t;
            InvalidateVisual();
            TimeChanged?.Invoke(t);
        }

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

        public override void Render(DrawingContext context)
        {
            double s = Bounds.Width / LoopFrame.StageWidth;
            if (Bounds.Height > 0) s = Math.Min(s, Bounds.Height / LoopFrame.StageHeight);
            if (s <= 0 || double.IsNaN(s)) return;
            try
            {
                _palette ??= new LoopPalette(this.TryFindResource("PinkBrush", ActualThemeVariant, out var pink) ? pink as IBrush : null);
                using (context.PushTransform(Matrix.CreateScale(s, s)))
                using (context.PushClip(new RoundedRect(new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight), 8 / s)))
                {
                    if (!Failed)
                    {
                        try
                        {
                            // Two recorded layers so Back always lands under Front, whatever order a
                            // scene paints them in (WPF's two DrawingVisuals).
                            var back = new DrawingGroup();
                            var front = new DrawingGroup();
                            using (var b = back.Open())
                            using (var fr = front.Open())
                            {
                                var f = new LoopFrame(b, fr, _palette);
                                f.Ground();
                                Scene.Draw(f, CurrentTime);
                            }
                            (_lastBack, _lastFront) = (back, front);
                        }
                        catch (Exception ex)
                        {
                            // WPF leaves the last frame up: stop the clock, keep drawing that frame.
                            Failed = true;
                            _running = false;
                            _generation++;
                            Log.Error(ex, "HelpLoopView: scene {Scene} failed at t={T}", Scene.Id, CurrentTime);
                        }
                    }
                    _lastBack?.Draw(context);
                    _lastFront?.Draw(context);
                }
            }
            catch (Exception ex)
            {
                Failed = true;
                _running = false;
                _generation++;
                Log.Error(ex, "HelpLoopView: render failed for {Scene}", Scene.Id);
            }
        }
    }
}
