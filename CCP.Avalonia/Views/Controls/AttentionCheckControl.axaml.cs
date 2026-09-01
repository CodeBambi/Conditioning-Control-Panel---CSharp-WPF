using System;
using System.Linq;
using System.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace ConditioningControlPanel.Avalonia.Views.Controls
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
        private readonly Ellipse _dotRingFg;
        private readonly ScaleTransform _dotRingScale;
        private CancellationTokenSource? _pulse;

        public AttentionCheckControl()
        {
            AvaloniaXamlLoader.Load(this);

            _dotRingFg = this.FindControl<Ellipse>("DotRingFg")!;
            // The transform is pulled out of the group rather than looked up by name: Avalonia
            // rejects x:Name on a ScaleTransform outright (AVLN2000, only a StyledElement can be
            // named), so the WPF markup's x:Name="DotRingScale" could not be kept.
            _dotRingScale = ((TransformGroup)_dotRingFg.RenderTransform!).Children.OfType<ScaleTransform>().First();
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
            double radius = (_dotRingFg.Width - _dotRingFg.StrokeThickness) / 2.0;
            double perimeter = 2.0 * Math.PI * radius;
            double units = perimeter / _dotRingFg.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            _dotRingFg.StrokeDashArray = new AvaloniaList<double> { visible, gap };
        }

        public void StartPulse()
        {
            StopPulse();
            // WPF's Storyboard + RepeatBehavior.Forever + AutoReverse is Avalonia's
            // IterationCount.Infinite + PlaybackDirection.Alternate; same 420ms sine ease.
            var sb = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(420),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.18),
                            new Setter(ScaleTransform.ScaleYProperty, 1.18),
                        },
                    },
                },
            };
            _pulse = new CancellationTokenSource();
            // Target the ELLIPSE, not the transform: Avalonia's TransformAnimator casts its target
            // to Visual and then finds the matching transform inside the visual's RenderTransform.
            // Passing the ScaleTransform compiles and throws InvalidCastException at run time.
            _ = sb.RunAsync(_dotRingFg, _pulse.Token);
        }

        public void StopPulse()
        {
            _pulse?.Cancel();
            _pulse?.Dispose();
            _pulse = null;
            _dotRingScale.ScaleX = 1.0;
            _dotRingScale.ScaleY = 1.0;
        }
    }
}
