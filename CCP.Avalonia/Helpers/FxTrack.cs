using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// A finite effect as one function of its own age: the port's stand-in for a WPF storyboard of
    /// keyframe animations with BeginTimes. <see cref="Play"/> calls <c>at(ms)</c> from a 16 ms
    /// timer until the age reaches the length, then once more AT the length (the composed end
    /// state) and <c>done</c> (the OUT: remove the ring, hand the scale back). It never loops, so
    /// it never keeps a window composing; ambient loops belong on <see cref="VisibleBeat"/>.
    /// <c>at</c> writes plain properties (a Transform's fields, a Visual's Opacity), never an
    /// Effect, and never <c>Animation.RunAsync</c> on a Transform. Tests drive a run with
    /// <see cref="Run.Seek"/>; a page parks its live runs with <see cref="Run.Finish"/>.
    /// </summary>
    internal static class FxTrack
    {
        /// <summary>Tests only: runs start no timer and move only through <see cref="Run.Seek"/> /
        /// <see cref="Run.Finish"/>, so a slow headless layout pass cannot eat a 400 ms effect whole.</summary>
        internal static bool ManualClock;

        internal sealed class Run
        {
            private readonly double _ms;
            private readonly Action<double> _at;
            private readonly Action? _done;
            private readonly long _started = Stopwatch.GetTimestamp();
            private DispatcherTimer? _timer;

            internal Run(double ms, Action<double> at, Action? done)
            {
                _ms = Math.Max(1, ms);
                _at = at;
                _done = done;
            }

            public bool IsDone { get; private set; }
            public double LengthMs => _ms;

            internal void Start()
            {
                Apply(0);
                if (IsDone || ManualClock) return;
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
                    (_, _) => Seek(Stopwatch.GetElapsedTime(_started).TotalMilliseconds));
                _timer.Start();
            }

            /// <summary>Paints the effect at this age; at or past the length it also ends it.</summary>
            public void Seek(double ms)
            {
                if (IsDone) return;
                if (ms < _ms) { Apply(ms); return; }
                Finish();
            }

            /// <summary>Jumps to the end state and runs the OUT. Safe to call twice.</summary>
            public void Finish()
            {
                if (IsDone) return;
                Apply(_ms);
                IsDone = true;
                _timer?.Stop();
                _timer = null;
                try { _done?.Invoke(); }
                catch (Exception ex) { Log.Debug("FxTrack done: {E}", ex.Message); }
            }

            private void Apply(double ms)
            {
                try { _at(ms); }
                catch (Exception ex)
                {
                    // decoration never throws into the page: end the run where it is
                    Log.Debug("FxTrack: {E}", ex.Message);
                    IsDone = true;
                    _timer?.Stop();
                    _timer = null;
                    try { _done?.Invoke(); } catch { }
                }
            }
        }

        /// <summary>Starts a run of <paramref name="ms"/> milliseconds. With <paramref name="group"/>, the run
        /// is listed there while it lives, so its owner can finish every live run when it parks.</summary>
        public static Run Play(double ms, Action<double> at, Action? done = null, List<Run>? group = null)
        {
            Run? run = null;
            run = new Run(ms, at, () =>
            {
                group?.Remove(run!);
                done?.Invoke();
            });
            group?.Add(run);
            run.Start();
            return run;
        }

        /// <summary>Finishes every run in the group (each lands on its end state and removes itself).</summary>
        public static void FinishAll(List<Run> group)
        {
            foreach (var run in group.ToArray()) run.Finish();
            group.Clear();
        }

        // ---- the house eases (WPF EasingFunctionBase, progress 0..1) ----

        public static double Clamp01(double u) => u <= 0 ? 0 : u >= 1 ? 1 : u;
        public static double Linear(double u) => u;
        public static double QuadOut(double u) => 1 - ((1 - u) * (1 - u));
        public static double QuadIn(double u) => u * u;
        public static double QuadInOut(double u) => u < 0.5 ? 2 * u * u : 1 - (2 * (1 - u) * (1 - u));
        public static double CubicOut(double u) => 1 - Math.Pow(1 - u, 3);
        public static double CubicIn(double u) => u * u * u;
        public static double SineInOut(double u) => (1 - Math.Cos(Math.PI * u)) / 2;

        /// <summary>WPF BackEase, EaseOut: overshoots the target by a little, then settles on it.</summary>
        public static double BackOut(double u, double amplitude)
        {
            double v = 1 - u;
            return 1 - ((v * v * v) - (v * amplitude * Math.Sin(Math.PI * v)));
        }

        /// <summary>
        /// WPF DoubleAnimationUsingKeyFrames with EasingDoubleKeyFrames: between two keys the value
        /// runs from the earlier to the later on the LATER key's ease (null = linear). Holds the
        /// first value before the first key and the last after the last. <paramref name="at"/> and
        /// the keys' <c>At</c> share a unit (a 0..1 share of the length, or milliseconds).
        /// </summary>
        public static double Keys(double at, params (double At, double Value, Func<double, double>? Ease)[] keys)
        {
            if (keys.Length == 0) return 0;
            if (at <= keys[0].At) return keys[0].Value;
            for (int i = 1; i < keys.Length; i++)
            {
                if (at > keys[i].At) continue;
                double span = keys[i].At - keys[i - 1].At;
                double u = span <= 0 ? 1 : (at - keys[i - 1].At) / span;
                if (keys[i].Ease is { } ease) u = ease(u);
                return keys[i - 1].Value + ((keys[i].Value - keys[i - 1].Value) * u);
            }
            return keys[^1].Value;
        }

        /// <summary>The house pop (WPF FxPop / Choreo Pop): to the peak by 35 % on quad out, back to 1 on quad in-out.</summary>
        public static double Pop(double u, double peak) =>
            Keys(Clamp01(u), (0, 1, null), (0.35, peak, QuadOut), (1, 1, QuadInOut));
    }
}
