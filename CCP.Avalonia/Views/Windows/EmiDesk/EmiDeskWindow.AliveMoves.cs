using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The two body moves of the alive poll: WPF EmiDeskWindow.Alive.cs RunWeightShift :599 (a one
    /// degree lean, held two seconds, released) and RunStretch :641 (four percent taller over
    /// 400 ms wearing <c>&gt;_&lt;</c>, then back down). Both ride <see cref="TransformTween"/>
    /// (never an Animation on a Transform), both end where they started, and a pick-up takes the
    /// lean over cleanly. The faces play at every motion level; the moves are Full only.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private static readonly EmiChain StretchChain = new(
            "stretch", "STRETCH (rare)",
            new[]
            {
                new EmiFrame(EmiAlive.StretchFace, EmiAlive.StretchUpMs + EmiAlive.StretchDownMs),
                new EmiFrame(EmiAlive.StretchSettleFace, 700)
            },
            BodyFrame: "idle");

        private DateTime _stretchDue = DateTime.MaxValue;
        private DispatcherTimer? _weightShiftTween;

        /// <summary>Test seams: how many of each she has done this sitting.</summary>
        internal int WeightShifts { get; private set; }
        internal int Stretches { get; private set; }
        internal double LeanDeg => _wobbleRotate.Angle;
        internal double BodyScaleY => _crtScale.ScaleY;
        internal bool RunFidgetForTest(EmiFidget kind) => RunFidget(kind);
        internal void MakeStretchDueForTest() => _stretchDue = DateTime.MinValue;

        private bool RunWeightShift()
        {
            try
            {
                if (!AliveMotionOk || _wobbleLive) return false;
                double deg = Rng.Next(2) == 0 ? -EmiAlive.WeightShiftDeg : EmiAlive.WeightShiftDeg;
                double total = EmiAlive.WeightShiftTravelMs * 2.0 + EmiAlive.WeightShiftHoldMs;
                double a = EmiAlive.WeightShiftTravelMs / total;
                double b = (EmiAlive.WeightShiftTravelMs + EmiAlive.WeightShiftHoldMs) / total;

                StopWeightShift();
                _weightShiftTween = TransformTween.Run(_wobbleRotate, TimeSpan.FromMilliseconds(total),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0.0, RotateTransform.AngleProperty, 0.0), (a, RotateTransform.AngleProperty, deg),
                        (b, RotateTransform.AngleProperty, deg), (1.0, RotateTransform.AngleProperty, 0.0),
                    });
                WeightShifts++;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] weight shift failed");
                return false;
            }
        }

        /// <summary>Let go of the lean. A drag that just took the rotation keeps what it wrote.</summary>
        private void StopWeightShift()
        {
            var t = _weightShiftTween;
            _weightShiftTween = null;
            if (t == null) return;
            try
            {
                bool live = t.IsEnabled;
                t.Stop();
                if (live && !_wobbleLive) _wobbleRotate.Angle = 0;
            }
            catch { /* she is gone */ }
        }

        private void RunStretch()
        {
            try
            {
                PlayChain(StretchChain);
                Stretches++;
                if (!AliveMotionOk) return;

                double total = EmiAlive.StretchUpMs + EmiAlive.StretchDownMs;
                double top = EmiAlive.StretchUpMs / total;
                _moveScaleTween?.Stop();   // one writer on CrtScale at a time; the last key is exactly 1
                _moveScaleTween = TransformTween.Run(_crtScale, TimeSpan.FromMilliseconds(total),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0.0, ScaleTransform.ScaleXProperty, 1.0), (top, ScaleTransform.ScaleXProperty, EmiAlive.StretchScale), (1.0, ScaleTransform.ScaleXProperty, 1.0),
                        (0.0, ScaleTransform.ScaleYProperty, 1.0), (top, ScaleTransform.ScaleYProperty, EmiAlive.StretchScale), (1.0, ScaleTransform.ScaleYProperty, 1.0),
                    });
                Log.Debug("[EmiDesk] stretch");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] stretch failed"); }
        }
    }
}
