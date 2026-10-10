using System;
using System.Diagnostics;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// An ambient loop on the shared 30 fps beat (<see cref="FrameClock"/>), for the loops that move
    /// something other than a Visual's Opacity (a brush colour, a gradient sweep, a glow's strength).
    /// It replaces an infinite Avalonia <c>Animation</c>, which ticks at render rate and keeps the WHOLE
    /// window composing 60 frames a second (the Home lag, 2026-10-09). Opacity breaths use BreathClock.
    /// </summary>
    internal sealed class AmbientLoop
    {
        private readonly FrameClock _clock;
        private readonly Stopwatch _watch = new();
        private readonly Action<double> _step;

        /// <param name="owner">The visual whose top level owns the beat.</param>
        /// <param name="step">Called with the seconds since <see cref="Start"/>.</param>
        public AmbientLoop(Visual owner, Action<double> step)
        {
            _step = step;
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => Step();
        }

        public bool IsRunning => _clock.IsEnabled;

        public void Start()
        {
            _watch.Restart();
            Step();
            _clock.Start();
        }

        public void Stop()
        {
            _clock.Stop();
            _watch.Reset();
        }

        private void Step()
        {
            try { _step(_watch.Elapsed.TotalSeconds); }
            catch { /* an ambient loop never takes the window down */ }
        }

        /// <summary>0..1..0 over two periods, sine eased: an alternating Animation with SineEaseInOut.</summary>
        public static double Breath(double seconds, double period) => (1 - Math.Cos(Math.PI * seconds / period)) / 2;

        /// <summary>0..1..0 over two periods, linear: an alternating Animation with no easing.</summary>
        public static double PingPong(double seconds, double period)
        {
            double t = seconds / period % 2;
            return t <= 1 ? t : 2 - t;
        }

        /// <summary>0..1 over one period, then again from 0: a repeating Animation.</summary>
        public static double Saw(double seconds, double period) => seconds / period % 1;
    }
}
