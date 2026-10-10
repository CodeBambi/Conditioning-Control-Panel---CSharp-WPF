using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// Keyframes on a Transform (or a brush) object. Avalonia's <c>Animation.RunAsync(aTransform)</c>
    /// resolves to TransformAnimator, which casts its target to Visual and throws
    /// InvalidCastException: inside a try it leaves the effect silently inert, outside one it kills
    /// the app (2026-10-09, the header wallet). This writes the property from a 16 ms timer instead
    /// (EmiDeskWindow.Tween's road). The easing runs over the whole progress and keys are linear in
    /// between, as Animation does with one Easing. Cancel through <paramref name="token"/> or Stop().
    /// </summary>
    internal static class TransformTween
    {
        public static DispatcherTimer Run(AvaloniaObject target, TimeSpan duration,
            IReadOnlyList<(double cue, AvaloniaProperty prop, double value)> keys,
            Easing? easing = null, bool loop = false, CancellationToken token = default)
        {
            var props = keys.Select(k => k.prop).Distinct()
                .Select(p => (p, k: keys.Where(x => x.prop == p).OrderBy(x => x.cue).ToArray())).ToArray();
            double ms = Math.Max(1, duration.TotalMilliseconds);
            var started = DateTime.UtcNow;

            void Apply(double p)
            {
                double e = easing?.Ease(p) ?? p;
                foreach (var (prop, k) in props)
                {
                    double v = k[^1].value;
                    for (int i = 0; i < k.Length; i++)
                    {
                        if (e > k[i].cue) continue;
                        if (i == 0) { v = k[0].value; break; }
                        double span = k[i].cue - k[i - 1].cue;
                        double t = span <= 0 ? 1 : (e - k[i - 1].cue) / span;
                        v = k[i - 1].value + (k[i].value - k[i - 1].value) * t;
                        break;
                    }
                    target.SetValue(prop, v);
                }
            }

            Apply(0);
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                try
                {
                    if (token.IsCancellationRequested) { timer!.Stop(); return; }
                    double elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
                    if (loop) { Apply(elapsed % ms / ms); return; }
                    if (elapsed < ms) { Apply(elapsed / ms); return; }
                    Apply(1);
                    timer!.Stop();
                }
                catch (Exception ex) { timer!.Stop(); Log.Debug("TransformTween: {E}", ex.Message); }
            });
            timer.Start();
            return timer;
        }
    }
}
