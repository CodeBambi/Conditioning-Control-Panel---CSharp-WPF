using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Compositor;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Afterglow scheduler (UI thread). While the subliminal service runs and the switch is on,
    /// samples the cursor about 30 times a second and, every 1.5..4.5 s, pops a random word from the
    /// player's active subliminal pool near it on whichever monitor the cursor is on. Compositor only:
    /// without it nothing runs. Rechecks the switch every second and on <see cref="SuperAccess.Changed"/>.
    /// Visual only: no XP, no Sparkles.
    /// </summary>
    public sealed class AfterglowDriver : IDisposable
    {
        private const double TickS = 0.033;
        private const double RecheckS = 1.0;

        private readonly DispatcherTimer _tick;
        private readonly Random _rng = new();
        private AfterglowLayer? _layer;
        private bool _running, _disposed;
        private double _untilSpawn, _untilRecheck;
        private DateTime _last;

        public AfterglowDriver()
        {
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(TickS) };
            _tick.Tick += OnTick;
            SuperAccess.Changed += OnChanged;
        }

        /// <summary>The layer, once one exists (the OCR exclusion reads its rects).</summary>
        public AfterglowLayer? Layer => _layer;

        /// <summary>The subliminal service started.</summary>
        public void Start()
        {
            if (_disposed) return;
            _running = true;
            Arm();
        }

        /// <summary>Stop, panic, emergency exit: the timer stops and every pop and spark goes at once.</summary>
        public void Stop()
        {
            _running = false;
            _tick.Stop();
            try { _layer?.Clear(); } catch { }
        }

        private static bool Allowed()
            => SuperAccess.IsOn(SuperEffect.Afterglow)
               && App.CompositorEnabled && App.Compositor != null
               && App.Settings?.Current?.SubliminalEnabled == true;

        private void OnChanged(SuperEffect effect)
        {
            if (effect != SuperEffect.Afterglow) return;
            DispatcherHelper.RunOnUI(Arm);
        }

        /// <summary>Start ticking if allowed, tear down if not.</summary>
        private void Arm()
        {
            if (_disposed) return;
            if (!_running || !Allowed())
            {
                _tick.Stop();
                try { _layer?.Clear(); } catch { }
                return;
            }
            if (_tick.IsEnabled) return;
            _untilSpawn = AfterglowField.NextInterval(_rng.NextDouble());
            _untilRecheck = RecheckS;
            _last = DateTime.UtcNow;
            _layer?.ResetCursor();
            _tick.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                double dt = Math.Clamp((now - _last).TotalSeconds, 0, 1);
                _last = now;

                _untilRecheck -= dt;
                if (_untilRecheck <= 0)
                {
                    _untilRecheck = RecheckS;
                    if (!_running || !Allowed()) { Arm(); return; }
                }

                if (!GetCursorPos(out var cur)) return;
                var layer = EnsureLayer();
                if (layer == null) return;
                layer.SampleCursor(dt, cur.X, cur.Y);

                _untilSpawn -= dt;
                if (_untilSpawn > 0) return;
                _untilSpawn = AfterglowField.NextInterval(_rng.NextDouble());

                var text = PickWord();
                if (text == null) return;
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cur.X, cur.Y));
                var b = screen.Bounds;
                layer.Spawn(text, cur.X, cur.Y, MonitorScale(cur), new SkiaSharp.SKRectI(b.Left, b.Top, b.Right, b.Bottom));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Afterglow tick failed: {E}", ex.Message);
            }
        }

        private AfterglowLayer? EnsureLayer()
        {
            if (_layer != null) return _layer;
            var engine = App.Compositor;
            if (engine == null) return null;
            _layer = new AfterglowLayer(engine);
            engine.RegisterLayer(_layer);
            return _layer;
        }

        /// <summary>A random word from the ACTIVE pool (same source as FlashSubliminal); null if none.</summary>
        private string? PickWord()
        {
            var pool = App.Settings?.Current?.SubliminalPool;
            if (pool == null) return null;
            int n = 0;
            foreach (var kv in pool) if (kv.Value) n++;
            if (n == 0) return null;
            int pick = _rng.Next(n);
            foreach (var kv in pool)
                if (kv.Value && pick-- == 0) return kv.Key;
            return null;
        }

        private static double MonitorScale(POINT pt)
        {
            try
            {
                var mon = MonitorFromPoint(pt, 2);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out var dpiX, out _) == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
            catch { }
            return 1.0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            SuperAccess.Changed -= OnChanged;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
