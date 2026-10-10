using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The card host's tweens: WPF 7.1.5 used DoubleAnimation / BeginAnimation per property; this
    /// head runs every tween of one host on ONE <see cref="FrameClock"/> (60 fps, frame-locked to
    /// the window, stopped when nothing moves). A tween is keyed by (target, name): starting a new
    /// one on the same key replaces the old one from wherever the value stands, which is WPF's
    /// handoff behaviour. Keyframes are chained tweens; Forever loops re-arm themselves.
    /// </summary>
    internal sealed class BoardTween
    {
        private sealed class Run
        {
            public required Action<double> Set;
            public double From, To, DelayMs, Ms;
            public Func<double, double> Ease = t => t;
            public Action? Done;
            public double StartMs;
        }

        private readonly FrameClock _clock;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly Dictionary<(object, string), Run> _runs = new();

        /// <summary>Tests step time here instead of the wall clock.</summary>
        internal double? NowForTests { get; set; }

        public BoardTween(Visual owner)
        {
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
            _clock.Tick += (_, _) => Step();
        }

        private double Now => NowForTests ?? _watch.Elapsed.TotalMilliseconds;

        public int Active => _runs.Count;

        /// <summary>Animates from <paramref name="from"/> (null = keep the current value, read through <paramref name="get"/>) to <paramref name="to"/>.</summary>
        public void To(object target, string name, Func<double> get, Action<double> set, double? from, double to,
            double ms, Func<double, double>? ease = null, double delayMs = 0, Action? done = null)
        {
            _runs.Remove((target, name));
            // An overshooting ease (Thud) carries opacity past 1. WPF clamps it; Avalonia drew the
            // card's words nearly transparent for the overshoot frames, so each line blinked out as
            // it landed (owner, 2026-10-09: "this text here flickers when the card comes").
            if (name == "opacity") { var raw = set; set = v => raw(Math.Clamp(v, 0, 1)); }
            double start = from ?? get();
            if (ms <= 0 && delayMs <= 0)
            {
                set(to);
                done?.Invoke();
                return;
            }
            if (from.HasValue) set(start);
            _runs[(target, name)] = new Run
            {
                Set = set, From = start, To = to, DelayMs = delayMs, Ms = Math.Max(1, ms),
                Ease = ease ?? (t => t), Done = done, StartMs = Now,
            };
            if (!_clock.IsEnabled) _clock.Start();
        }

        /// <summary>Stops the tween on that key where it stands (WPF BeginAnimation(p, null)).</summary>
        public void Stop(object target, string name) => _runs.Remove((target, name));

        public bool IsRunning(object target, string name) => _runs.ContainsKey((target, name));

        /// <summary>Advances every tween to now. The clock calls it; tests call it after moving the time.</summary>
        internal void Step()
        {
            if (_runs.Count == 0) { _clock.Stop(); return; }
            double now = Now;
            List<((object, string) Key, Run Run)>? finished = null;
            foreach (var (key, r) in new List<KeyValuePair<(object, string), Run>>(_runs))
            {
                if (!_runs.TryGetValue(key, out var live) || !ReferenceEquals(live, r)) continue;
                double t = (now - r.StartMs - r.DelayMs) / r.Ms;
                if (t < 0) continue;
                if (t >= 1)
                {
                    r.Set(r.To);
                    (finished ??= new()).Add((key, r));
                    continue;
                }
                r.Set(r.From + (r.To - r.From) * r.Ease(t));
            }
            if (finished != null)
                foreach (var (key, r) in finished)
                {
                    if (!_runs.TryGetValue(key, out var live) || !ReferenceEquals(live, r)) continue;
                    _runs.Remove(key);
                    try { r.Done?.Invoke(); } catch { }
                }
            if (_runs.Count == 0) _clock.Stop();
        }

        /// <summary>Drops every tween (the host is going away).</summary>
        public void Clear()
        {
            _runs.Clear();
            _clock.Stop();
        }
    }
}
