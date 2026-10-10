using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// Deeper's <c>screen_shake</c> (WPF Services/UI/ScreenShakeService): the content of every visible app
    /// window jitters by up to 28 px x intensity on a 30 ms tick for the action's duration, easing out to
    /// rest, then each window gets its own transform back. The timer exists only while a shake runs.
    /// A new shake replaces the running one. The page inside a web view does not move (WPF drives a CSS
    /// transform there; not on this head).
    /// </summary>
    internal static class ScreenShake
    {
        internal const int TickMs = 30;
        internal const double MaxOffsetPx = 28.0;

        private static readonly Random Rng = new();
        private static readonly List<(Control Root, TranslateTransform Shake, ITransform? Prior)> Targets = new();
        private static DispatcherTimer? _timer;
        private static long _start;
        private static int _durationMs;
        private static double _amplitude;

        internal static bool IsRunning => _timer != null;
        internal static int TargetCount => Targets.Count;

        /// <summary>The windows to shake (tests swap it).</summary>
        internal static Func<IEnumerable<Window>> WindowsProvider = () =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? (IEnumerable<Window>)Array.Empty<Window>();

        internal static void Shake(double intensity, int durationMs)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Shake(intensity, durationMs)); return; }
            intensity = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
            if (intensity <= 0 || durationMs <= 0) return;
            try
            {
                Stop();   // restores the last shake's transforms before the windows are read again
                foreach (var win in WindowsProvider())
                {
                    if (!win.IsVisible || win.Content is not Control root) continue;
                    var shake = new TranslateTransform();
                    Targets.Add((root, shake, root.RenderTransform));
                    root.RenderTransform = shake;
                }
                if (Targets.Count == 0) return;
                _amplitude = MaxOffsetPx * intensity;
                _durationMs = durationMs;
                _start = Environment.TickCount64;
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(TickMs) };
                _timer.Tick += (_, _) => Tick();
                _timer.Start();
            }
            catch (Exception ex) { Log.Debug("Deeper screen_shake: {E}", ex.Message); Stop(); }
        }

        /// <summary>The offset range at an age: full, easing to nothing over the last third.</summary>
        internal static double AmplitudeAt(double ageMs, int durationMs, double amplitude)
        {
            if (ageMs < 0 || ageMs >= durationMs) return 0;
            double left = (durationMs - ageMs) / durationMs;
            return amplitude * Math.Clamp(left * 3, 0, 1);
        }

        private static void Tick()
        {
            double age = Environment.TickCount64 - _start;
            if (age >= _durationMs) { Stop(); return; }
            double a = AmplitudeAt(age, _durationMs, _amplitude);
            foreach (var t in Targets)
            {
                t.Shake.X = (Rng.NextDouble() * 2 - 1) * a;
                t.Shake.Y = (Rng.NextDouble() * 2 - 1) * a;
            }
        }

        /// <summary>At rest now, every window's own transform back (the end of a shake, engine stop, panic).</summary>
        internal static void Stop()
        {
            try { _timer?.Stop(); } catch { }
            _timer = null;
            foreach (var t in Targets)
            {
                try { if (ReferenceEquals(t.Root.RenderTransform, t.Shake)) t.Root.RenderTransform = t.Prior; }
                catch (Exception ex) { Log.Debug("Deeper screen_shake restore: {E}", ex.Message); }
            }
            Targets.Clear();
        }
    }
}
