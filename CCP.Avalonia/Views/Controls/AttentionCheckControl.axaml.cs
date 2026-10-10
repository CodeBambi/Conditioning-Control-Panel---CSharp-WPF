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
    ///
    /// <para><b>Nothing on this head constructs this control, matching WPF.</b> Its only caller is
    /// <c>AttentionCheckService.EnsureWindow()</c> (ConditioningControlPanel/Services/AttentionCheckService.cs:339),
    /// and WPF scrapped that mechanic pre-ship: App.xaml.cs constructs the service for BarkService's
    /// OnPass/OnFail wiring and never starts it, so WPF never shows the ring. If WPF revives it,
    /// <c>AttentionCheckParityScanTests</c> fails; then port the service (gaze feed from the head's
    /// WebcamTracker, topmost click-through host, panic stop, Lockdown) and wire this from it.</para>
    /// </summary>
    public partial class AttentionCheckControl : UserControl
    {
        private readonly Ellipse _dotRingFg;
        private readonly ScaleTransform _dotRingScale;
        private Helpers.BeatLoop? _pulseLoop;
        internal bool IsPulsing => _pulseLoop?.IsRunning == true;

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
            // WPF's Storyboard (Forever + AutoReverse): 1.0 -> 1.18 over 420 ms on a sine ease, and
            // back. On the shared beat; the loop writes the ScaleTransform directly.
            _pulseLoop ??= new Helpers.BeatLoop(_dotRingFg, t =>
            {
                double s = 1.0 + (0.18 * Helpers.BeatLoop.Breath(t, 0.42));
                _dotRingScale.ScaleX = s;
                _dotRingScale.ScaleY = s;
            });
            _pulseLoop.Start();
        }

        public void StopPulse()
        {
            _pulseLoop?.Stop();
            _dotRingScale.ScaleX = 1.0;
            _dotRingScale.ScaleY = 1.0;
        }
    }
}
