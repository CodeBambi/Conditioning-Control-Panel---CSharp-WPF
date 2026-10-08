using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The attention-style editor's Test target: WPF <c>FloatingText</c> (VideoService.cs) as spawned
    /// by AttentionTargetEditorDialog.BtnTest_Click. One borderless, topmost, non-activating window
    /// sized to the target (so the rest of the desktop stays clickable), bouncing at 187.5 DIP/s
    /// (WPF 3 px per 16 ms tick) inside the primary screen's working area minus WPF's margins, alive
    /// until clicked - no lifespan, as in WPF - then a 300 ms fade. Its timer runs only while the
    /// window is open (P01); panic closes every one (P06, "attention-test" in PanicSurfaces).
    /// </summary>
    internal sealed class AttentionTestTarget
    {
        /// <summary>Tests step this clock; motion uses real elapsed time, capped per tick (P40).</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        private static readonly List<AttentionTestTarget> _open = new();
        internal static IReadOnlyList<AttentionTestTarget> Open => _open;

        internal readonly Window Window;
        private readonly DispatcherTimer _timer;
        private readonly double _w, _h, _scale, _minX, _minY, _maxX, _maxY;
        private double _x, _y, _vx, _vy;
        private long _last;
        private bool _hit;

        internal double X => _x;
        internal double Y => _y;

        private AttentionTestTarget(string text, int size, Screen screen)
        {
            var root = MandatoryVideoOverlay.Target.Build(MandatoryVideoScheduler.FormatTriggerText(text), Math.Max(40, size), out _w, out _h);
            _scale = screen.Scaling;
            var area = screen.WorkingArea;
            double ax = area.X / _scale, ay = area.Y / _scale, aw = area.Width / _scale, ah = area.Height / _scale;
            double mx = Math.Min(150, aw * 0.08), my = Math.Min(100, ah * 0.08);   // WPF AttentionTargetVisual.MarginFor
            (_minX, _minY, _maxX, _maxY) = (ax + mx, ay + my, ax + aw - mx, ay + ah - my);
            _x = _minX + Random.Shared.NextDouble() * Math.Max(0, _maxX - _w - _minX);
            _y = _minY + Random.Shared.NextDouble() * Math.Max(0, _maxY - _h - _minY);
            var a = Random.Shared.NextDouble() * Math.PI * 2;
            (_vx, _vy) = (Math.Cos(a) * MandatoryVideoOverlay.Target.Speed, Math.Sin(a) * MandatoryVideoOverlay.Target.Speed);

            Window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,   // WPF: don't steal focus
                CanResize = false,
                Width = _w,
                Height = _h,
                Content = root,
            };
            Window.PointerPressed += (_, e) => { e.Handled = true; Hit(); };   // left or right, like WPF
            Window.Closed += (_, _) => { _timer.Stop(); _open.Remove(this); };
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
            Place();
        }

        /// <summary>WPF BtnTest_Click: the target on the primary screen; null when there is none.</summary>
        internal static AttentionTestTarget? Spawn(string text, int size, TopLevel owner)
        {
            var screen = owner.Screens?.Primary;
            if (screen == null) return null;
            var t = new AttentionTestTarget(text, size, screen);
            _open.Add(t);
            t._last = Clock.GetTimestamp();
            t.Window.Show();
            t._timer.Start();
            return t;
        }

        /// <summary>One motion step over the real time since the last one (capped at 0.25 s).</summary>
        internal void Tick()
        {
            var now = Clock.GetTimestamp();
            var dt = Math.Clamp(Clock.GetElapsedTime(_last, now).TotalSeconds, 0, 0.25);
            _last = now;
            if (_hit) return;
            _x += _vx * dt;
            _y += _vy * dt;
            if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
            if (_x + _w > _maxX) { _x = _maxX - _w; _vx = -Math.Abs(_vx); }
            if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
            if (_y + _h > _maxY) { _y = _maxY - _h; _vy = -Math.Abs(_vy); }
            Place();
        }

        private void Place() => Window.Position = new PixelPoint((int)(_x * _scale), (int)(_y * _scale));

        /// <summary>WPF: a click logs and fades the window out (~300 ms), then destroys it.</summary>
        internal void Hit()
        {
            if (_hit) return;
            _hit = true;
            Log.Debug("Test target clicked");
            Window.IsHitTestVisible = false;
            Window.Transitions = new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
            };
            Window.Opacity = 0;
            DispatcherTimer.RunOnce(Window.Close, TimeSpan.FromMilliseconds(320));
        }

        /// <summary>Panic: every open test target closes at once.</summary>
        internal static void CloseAll()
        {
            foreach (var t in _open.ToArray()) t.Window.Close();
        }
    }
}
