using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Reusable visual for the Attention-Check mechanic: hot-pink progress
    /// ring around a glowing dot, lifted from WebcamCalibrationWindow's
    /// calibration target so the visual is instantly recognizable to users
    /// who've completed calibration. The control is intrinsically 84x84 DIPs;
    /// host it in a window at the desired screen position.
    ///
    /// Public surface:
    ///   SetProgress(0..1)  — fills the foreground ring clockwise.
    ///   StartPulse() / StopPulse() — gentle scale-pulse animation, useful
    ///                                 to signal "look here" on first appear.
    /// </summary>
    public partial class AttentionCheckControl : UserControl
    {
        private Storyboard? _pulse;

        public AttentionCheckControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the foreground-ring fill amount. progress is clamped to
        /// [0, 1]; 0 = empty, 1 = full ring. Implementation mirrors
        /// WebcamCalibrationWindow.UpdateProgressRing — same StrokeDashArray
        /// math so the visual matches calibration exactly.
        /// </summary>
        public void SetProgress(double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            double radius = (DotRingFg.Width - DotRingFg.StrokeThickness) / 2.0;
            double perimeter = 2.0 * Math.PI * radius;
            double units = perimeter / DotRingFg.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            DotRingFg.StrokeDashArray = new DoubleCollection(new[] { visible, gap });
        }

        public void StartPulse()
        {
            StopPulse();
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var sx = new DoubleAnimation
            {
                From = 1.0, To = 1.18,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            var sy = new DoubleAnimation
            {
                From = 1.0, To = 1.18,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(sx, DotRingScale);
            Storyboard.SetTargetProperty(sx, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(sy, DotRingScale);
            Storyboard.SetTargetProperty(sy, new PropertyPath("ScaleY"));
            sb.Children.Add(sx);
            sb.Children.Add(sy);
            sb.Begin();
            _pulse = sb;
        }

        public void StopPulse()
        {
            _pulse?.Stop();
            _pulse = null;
            DotRingScale.ScaleX = 1.0;
            DotRingScale.ScaleY = 1.0;
        }
    }
}
