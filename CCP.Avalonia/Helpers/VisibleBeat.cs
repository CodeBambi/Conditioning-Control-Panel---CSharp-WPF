using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// An ambient loop that looks after itself: it runs on the window's shared 30 fps beat
    /// (<see cref="BeatLoop"/>) only while its target is in a window, effectively visible, wanted
    /// (<c>when</c>) and, for decoration, allowed by the motion level; it parks at <c>rest</c> the
    /// moment any of those stops being true, and re-arms when they come back (a tab switch, a motion
    /// level change). It replaces the infinite style Animations (a <c>Style.Animations</c> block with
    /// IterationCount Infinite), each of which kept its whole window composing at 60 Hz.
    /// </summary>
    internal sealed class VisibleBeat
    {
        private readonly Control _target;
        private readonly Action<double> _step;
        private readonly Action? _rest;
        private readonly Func<bool>? _when;
        private readonly bool _decoration;
        private readonly BeatLoop _loop;
        private IDisposable? _watch;
        private bool _attached;

        private VisibleBeat(Control target, Action<double> step, Action? rest, Func<bool>? when, bool decoration)
        {
            _target = target;
            _step = step;
            _rest = rest;
            _when = when;
            _decoration = decoration;
            _loop = new BeatLoop(target, Step);
        }

        /// <param name="target">The visual the loop paints; its window owns the beat.</param>
        /// <param name="step">Called on the beat with the seconds since the loop (re)started.</param>
        /// <param name="rest">Paints the still look; called whenever the loop parks.</param>
        /// <param name="when">An extra condition (a state flag); call <see cref="Refresh"/> when it changes.</param>
        /// <param name="decoration">True = ambient decoration, parked at motion Reduced / Off and on the
        /// low performance tier (MotionFx.AllowAmbientLoops). False = a status signal (a loading
        /// shimmer, thinking dots) that the motion level does not silence.</param>
        public static VisibleBeat Attach(Control target, Action<double> step, Action? rest = null,
                                         Func<bool>? when = null, bool decoration = true)
        {
            var beat = new VisibleBeat(target, step, rest, when, decoration);
            target.AttachedToVisualTree += (_, _) => beat.OnAttached();
            target.DetachedFromVisualTree += (_, _) => beat.OnDetached();
            if (target.IsAttachedToVisualTree()) beat.OnAttached();
            else beat.Park();
            return beat;
        }

        public bool IsRunning => _loop.IsRunning;

        /// <summary>Re-reads every condition; starts or parks the loop to match.</summary>
        public void Refresh()
        {
            bool want = _attached && _target.IsEffectivelyVisible
                        && (!_decoration || AmbientFxCanvas.Env.AllowAmbientLoops)
                        && (_when?.Invoke() ?? true);
            if (want)
            {
                if (!_loop.IsRunning) _loop.Start();
            }
            else Park();
        }

        private void Park()
        {
            _loop.Stop();
            try { _rest?.Invoke(); } catch { /* a still look never takes the window down */ }
        }

        private void Step(double t)
        {
            try { _step(t); } catch { /* an ambient loop never takes the window down */ }
        }

        private void OnAttached()
        {
            _attached = true;
            _watch?.Dispose();
            _watch = EffectiveVisibility.Watch(_target, Refresh);
            AmbientFxCanvas.Env.MotionGateChanged -= Refresh;
            AmbientFxCanvas.Env.MotionGateChanged += Refresh;
            Refresh();
        }

        private void OnDetached()
        {
            _attached = false;
            _watch?.Dispose();
            _watch = null;
            AmbientFxCanvas.Env.MotionGateChanged -= Refresh;
            Park();
        }
    }
}
