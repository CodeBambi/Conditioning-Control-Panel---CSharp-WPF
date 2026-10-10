// PORTED from WPF 7.1.5, the launcher's small feel (ledger P19):
//   - the status line answers the engine stopping at once (LauncherWindow.xaml.cs :416 OnEngineStopped),
//     not a second later on the tick;
//   - level, Sparkles and the chip's balance odometer from the last value shown, and the XP bar
//     tweens (RefreshStats :611, Choreo.cs RefreshSpReadout :610; MotionFx.Odometer / BarFill);
//   - the tier badge shakes once every 8 s and its big copy pops in (LauncherWindow.TierBadge.cs).
// Every tween is a 16 ms timer write (TransformTween's road: never Animation.RunAsync on a transform),
// snaps when transitions are off, and is dropped on hide.
// Cursor parallax (WPF LauncherWindow.Backdrop.cs :211) is LauncherWindow.Parallax.cs; the art drift, trail,
// comets and sheens are LauncherWindow.ArtMotion.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        private const double TierWobbleEverySeconds = 8;
        private const double TierWobbleDegrees = 9;
        private const double TierHoverStartScale = 0.35;

        private readonly Dictionary<object, DispatcherTimer> _numberTweens = new();
        private double _shownLevel, _shownSparkles, _shownXpWidth = -1;
        private double _spShown = double.NaN;
        private DispatcherTimer? _tierWobbleTimer;
        private RotateTransform? _tierBadgeTilt;
        private ScaleTransform? _tierBigScale;
        private DispatcherTimer? _tierTween, _tierBigTween;

        /// <summary>Tests: how many times the badge has shaken.</summary>
        internal int TierWobbles { get; private set; }

        private void HookFeel()
        {
            CoreTubeEvents.EngineStopped += OnEngineStoppedFeel;
            Closed += (_, _) =>
            {
                CoreTubeEvents.EngineStopped -= OnEngineStoppedFeel;
                FeelPark();
            };
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && !IsVisible) FeelPark();
            };
        }

        /// <summary>WPF OnEngineStopped: the line, the Stop link and the CTA follow at once.</summary>
        private void OnEngineStoppedFeel() => Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible) return;
            RefreshStatus();
            RefreshStats();
        });

        /// <summary>WPF OnHidden: no timer outlives the window being on screen, and the next show
        /// counts the balance up again.</summary>
        private void FeelPark()
        {
            foreach (var t in _numberTweens.Values) t.Stop();
            _numberTweens.Clear();
            _tierWobbleTimer?.Stop();
            _tierTween?.Stop();
            _tierBigTween?.Stop();
            if (_tierBadgeTilt != null) _tierBadgeTilt.Angle = 0;
            _spShown = double.NaN;
        }

        // ------------------------------------------------------------------ numbers

        /// <summary>WPF MotionFx.Odometer: the text counts from <paramref name="from"/> to
        /// <paramref name="to"/> on a quadratic ease-out; snaps with transitions off or no change.</summary>
        private void Odometer(TextBlock target, double from, double to, string format, double seconds)
        {
            NumberTween(target, from, to, seconds, v => target.Text = string.Format(CultureInfo.CurrentCulture, format, Math.Round(v)));
        }

        /// <summary>One number on a 16 ms timer. A new run for the same key replaces the old one.</summary>
        private void NumberTween(object key, double from, double to, double seconds, Action<double> write)
        {
            // The 1 s tick repeats the same number: a count already on its way there is left alone.
            if (Math.Abs(to - from) < 0.5 && _numberTweens.ContainsKey(key)) return;
            if (_numberTweens.Remove(key, out var old)) old.Stop();
            if (!Env.AllowTransitions || !IsVisible || Math.Abs(to - from) < 0.5 || seconds <= 0) { write(to); return; }
            write(from);
            var started = DateTime.UtcNow;
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                try
                {
                    double p = Math.Clamp((DateTime.UtcNow - started).TotalSeconds / seconds, 0, 1);
                    double e = 1 - (1 - p) * (1 - p);   // QuadraticEase, EaseOut
                    write(from + (to - from) * e);
                    if (p < 1) return;
                }
                catch (Exception ex) { Log.Debug(ex, "[Launcher] number tween failed"); }
                timer!.Stop();
                if (_numberTweens.TryGetValue(key, out var mine) && ReferenceEquals(mine, timer)) _numberTweens.Remove(key);
            });
            _numberTweens[key] = timer;
            timer.Start();
        }

        /// <summary>WPF RefreshStats' moving half: level and Sparkles from the last value shown (the
        /// first show counts up from zero), the XP bar from its last width.</summary>
        private void ShowStats(int level, int sparkles, double xpWidth)
        {
            Odometer(StatLevel, _shownLevel, level, "{0:0}", _shownLevel == 0 ? 0.9 : 0.5);
            Odometer(StatSparkles, _shownSparkles, sparkles, "{0:N0}", _shownSparkles == 0 ? 1.1 : 0.5);
            _shownLevel = level;
            _shownSparkles = sparkles;
            if (XpTrack.Bounds.Width <= 0 || Math.Abs(xpWidth - _shownXpWidth) < 0.5) return;
            NumberTween(XpFill, _shownXpWidth < 0 ? 0 : _shownXpWidth, xpWidth, _shownXpWidth < 0 ? 1.0 : 0.5,
                v => XpFill.Width = Math.Max(0, v));
            _shownXpWidth = xpWidth;
        }

        /// <summary>WPF RefreshSpReadout: counts up from zero on show, tweens between values after.</summary>
        private void ShowSp(int sp)
        {
            double from = double.IsNaN(_spShown) ? 0 : _spShown;
            if (!double.IsNaN(_spShown) && Math.Abs(from - sp) < 0.5) return;
            Odometer(SpReadout, from, sp, "{0:N0}", double.IsNaN(_spShown) ? 0.9 : 0.6);
            _spShown = sp;
        }

        // ------------------------------------------------------------------ tier badge

        /// <summary>WPF EnsureTierBadgeFx: the tilt, the big copy's scale and the 8 s timer, once.</summary>
        private void EnsureTierBadgeFx()
        {
            if (_tierBadgeTilt == null)
            {
                _tierBadgeTilt = new RotateTransform(0);
                TierBadge.RenderTransform = _tierBadgeTilt;
                TierBadge.RenderTransformOrigin = RelativePoint.Center;
                _tierBigScale = new ScaleTransform(1, 1);
                TierBadgeBig.RenderTransform = _tierBigScale;
                TierBadgeBig.RenderTransformOrigin = RelativePoint.Center;
                _tierWobbleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(TierWobbleEverySeconds) };
                _tierWobbleTimer.Tick += (_, _) => TierBadgeWobble();
            }
            if (IsVisible && !_tierWobbleTimer!.IsEnabled) _tierWobbleTimer.Start();
        }

        /// <summary>A damped shake: out, back past centre, a smaller echo, rest. About 0.7 s.</summary>
        internal void TierBadgeWobble()
        {
            try
            {
                if (_tierBadgeTilt == null || !IsVisible || !TierBadge.IsVisible) return;
                if (!Env.AllowAmbientLoops || TierBadgePopup.IsOpen) return;
                double a = TierWobbleDegrees;
                _tierTween?.Stop();
                _tierTween = TransformTween.Run(_tierBadgeTilt, TimeSpan.FromMilliseconds(700), new (double, AvaloniaProperty, double)[]
                {
                    (0, RotateTransform.AngleProperty, 0),
                    (0.15, RotateTransform.AngleProperty, -a),
                    (0.35, RotateTransform.AngleProperty, a * 0.8),
                    (0.55, RotateTransform.AngleProperty, -a * 0.45),
                    (0.75, RotateTransform.AngleProperty, a * 0.2),
                    (1, RotateTransform.AngleProperty, 0),
                });
                TierWobbles++;
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] tier badge wobble failed"); }
        }

        /// <summary>WPF TierBadge_MouseEnter's second half: the big copy grows in from 0.35 over
        /// 260 ms with a small overshoot (scale only, never opacity), or just sits with transitions off.</summary>
        private void TierBadgeBigPopIn()
        {
            try
            {
                EnsureTierBadgeFx();
                if (_tierBigScale == null) return;
                _tierBigTween?.Stop();
                if (!Env.AllowTransitions) { _tierBigScale.ScaleX = _tierBigScale.ScaleY = 1; return; }
                _tierBigTween = TransformTween.Run(_tierBigScale, TimeSpan.FromMilliseconds(260), new (double, AvaloniaProperty, double)[]
                {
                    (0, ScaleTransform.ScaleXProperty, TierHoverStartScale), (1, ScaleTransform.ScaleXProperty, 1),
                    (0, ScaleTransform.ScaleYProperty, TierHoverStartScale), (1, ScaleTransform.ScaleYProperty, 1),
                }, new BackEaseOut());
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] tier badge hover failed"); }
        }
    }
}
