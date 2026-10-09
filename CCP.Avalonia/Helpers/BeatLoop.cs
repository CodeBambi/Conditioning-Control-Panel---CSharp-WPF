using System;
using System.Diagnostics;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// An endless ambient loop on the window's shared 30 fps beat (<see cref="FrameClock"/>). An
    /// infinite Avalonia Animation ticks at render rate and keeps the WHOLE window composing 60
    /// frames a second (AGENTS.md "GPU CACHE + 60 Hz TRAP"); on the beat the loop ticks with the fog,
    /// the logo dial and the tile breaths. <paramref name="step"/> gets the seconds since Start and
    /// writes plain properties (a Transform's Angle, a Visual's Opacity), never an Effect.
    /// </summary>
    internal sealed class BeatLoop
    {
        private readonly FrameClock _clock;
        private readonly Action<double> _step;
        private long _started;

        public BeatLoop(Visual owner, Action<double> step)
        {
            _step = step;
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => _step(Stopwatch.GetElapsedTime(_started).TotalSeconds);
        }

        public bool IsRunning => _clock.IsEnabled;

        public void Start()
        {
            if (IsRunning) return;
            _started = Stopwatch.GetTimestamp();
            _step(0);
            _clock.Start();
        }

        public void Stop() => _clock.Stop();

        /// <summary>0 -> 1 -> 0 over 2 x <paramref name="period"/> seconds: Alternate + SineEaseInOut.</summary>
        public static double Breath(double t, double period) => (1 - Math.Cos(Math.PI * t / period)) / 2;
    }
}
