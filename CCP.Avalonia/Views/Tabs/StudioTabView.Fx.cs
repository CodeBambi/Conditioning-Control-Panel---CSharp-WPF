using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The rack's two one-shot motions, ported from WPF StudioTabView.xaml.cs: the 120ms detail
    /// crossfade (FadeInDetail, :1384) and the 260ms state-dot pop after a right-click quick-toggle
    /// (PingDot, :1257). Both gated on MotionFx.AllowTransitions and both end on the plain resting
    /// value (Opacity 1, scale 1), like WPF's FillBehavior.Stop plus explicit clear. One clock,
    /// running only while a tween is live and the tab is shown (P01); hidden or detached snaps to
    /// the end. Tests step <see cref="Time"/> and call <see cref="StepFx"/>.
    /// </summary>
    public partial class StudioTabView
    {
        private const int DetailFadeMs = 120, DotPopMs = 260;

        internal static TimeProvider Time = TimeProvider.System;

        private sealed record Tween(object Target, long Start, double Ms, double From, Action<double> Set);

        private readonly List<Tween> _tweens = new();
        private DispatcherTimer? _fxTimer;

        internal bool FxRunning => _fxTimer?.IsEnabled == true;

        private static double QuadOut(double t) => 1 - (1 - t) * (1 - t);

        private void Animate(object target, double from, int ms, Action<double> set)
        {
            _tweens.RemoveAll(t => ReferenceEquals(t.Target, target));
            if (!Env.AllowTransitions || !IsVisible) { set(1); return; }
            _tweens.Add(new Tween(target, Time.GetTimestamp(), ms, from, set));
            set(from);
            _fxTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => StepFx());
            _fxTimer.Start();
        }

        /// <summary>One clock tick at <see cref="Time"/>'s now. Hidden or detached: finish everything.</summary>
        internal void StepFx()
        {
            bool shown = IsVisible && VisualRoot != null;
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                double p = shown ? Math.Min(Time.GetElapsedTime(tw.Start).TotalMilliseconds / tw.Ms, 1) : 1;
                tw.Set(tw.From + (1 - tw.From) * QuadOut(p));
                if (p >= 1) _tweens.RemoveAt(i);
            }
            if (_tweens.Count == 0) _fxTimer?.Stop();
        }

        private void FadeInDetail(Control host) => Animate(host, 0, DetailFadeMs, v => host.Opacity = v);

        private void PingDot(Ellipse? dot)
        {
            if (dot == null || !Env.AllowTransitions) return;
            if (dot.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                dot.RenderTransformOrigin = RelativePoint.Center;
                dot.RenderTransform = scale;
            }
            Animate(dot, 2.0, DotPopMs, v => scale.ScaleX = scale.ScaleY = v);
        }
    }
}
