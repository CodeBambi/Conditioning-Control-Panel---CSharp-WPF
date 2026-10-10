using System;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.VisualTree;
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

        /// <summary>0 -> 1 -> 0 over 2 x <paramref name="period"/> seconds, linear: Alternate, no easing.</summary>
        public static double PingPong(double t, double period)
        {
            double u = t / period % 2;
            return u <= 1 ? u : 2 - u;
        }

        /// <summary>
        /// Starts a loop that lives as long as <paramref name="token"/>: the drop-in for
        /// <c>infiniteAnimation.RunAsync(target, token)</c>. It also drops itself once
        /// <paramref name="owner"/> has been in a window and left it, so a loop on a rebuilt row
        /// never ticks on behind the page. <paramref name="step"/> never takes the window down.
        /// </summary>
        public static BeatLoop Run(Visual owner, CancellationToken token, Action<double> step)
        {
            BeatLoop? loop = null;
            bool seen = false;
            loop = new BeatLoop(owner, t =>
            {
                bool attached = owner.IsAttachedToVisualTree();
                if (attached) seen = true;
                else if (seen) { loop?.Stop(); return; }
                try { step(t); } catch { /* an ambient loop never takes the window down */ }
            });
            if (token.IsCancellationRequested) return loop;
            loop.Start();
            if (token.CanBeCanceled) token.Register(loop.Stop);
            return loop;
        }

        /// <summary>0 -> 1 over <paramref name="period"/> seconds, then again from 0: a repeating clock.</summary>
        public static double Saw(double t, double period) => t / period % 1;

        /// <summary>
        /// A keyframe track read at <paramref name="at"/> (same unit as the keys' <c>At</c>). Linear
        /// between keys, or sine ease-in-out per segment with <paramref name="sine"/> (WPF's
        /// EasingDoubleKeyFrame + SineEase). Holds the first value before and the last value after.
        /// </summary>
        public static double Keys(double at, (double At, double Value)[] keys, bool sine = false)
        {
            if (keys.Length == 0) return 0;
            if (at <= keys[0].At) return keys[0].Value;
            for (int i = 1; i < keys.Length; i++)
            {
                if (at > keys[i].At) continue;
                double span = keys[i].At - keys[i - 1].At;
                double u = span <= 0 ? 1 : (at - keys[i - 1].At) / span;
                if (sine) u = (1 - Math.Cos(Math.PI * u)) / 2;
                return keys[i - 1].Value + ((keys[i].Value - keys[i - 1].Value) * u);
            }
            return keys[^1].Value;
        }
    }
}
