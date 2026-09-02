using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Small, movable splash shown while the webcam / eye-tracking engine
    /// starts up. WebcamTrackingService.Start() opens the camera and constructs
    /// three ONNX inference sessions on a worker thread, which can block several
    /// seconds (longer on first run or slow USB cameras). This window is driven
    /// by WebcamTrackingService.OnStartupProgress so the user sees what's
    /// happening instead of an unresponsive button, and can drag it out of the
    /// way while they wait.
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamLoadingSplash.xaml.cs. Deviations:
    ///  - The progress DoubleAnimation is a DoubleTransition on the ScaleTransform in the XAML,
    ///    so SetProgress is a plain assignment. Same 180ms quadratic-ease-out shape.
    ///  - The breathing pulse is a keyframe animation on the "pulse" style class; Start/StopPulse
    ///    add and remove it, since Avalonia has no BeginAnimation(property, null) to cancel one.
    ///  - The fade-out uses Animation.RunAsync, Avalonia's equivalent of BeginAnimation.
    ///  - Dispatcher.CheckAccess/BeginInvoke -> Dispatcher.UIThread.CheckAccess/Post.
    ///  - DragMove() -> BeginMoveDrag(e), which does not throw on a released button, so the
    ///    swallowing try/catch the WPF original needed is gone.
    /// </summary>
    public partial class WebcamLoadingSplash : Window
    {
        private readonly TextBlock _txtStatus;
        private readonly Border _progressFill;
        private readonly ScaleTransform _progressScale;
        private bool _closing;

        public WebcamLoadingSplash()
        {
            AvaloniaXamlLoader.Load(this);

            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _progressFill = this.FindControl<Border>("ProgressFill")!;
            _progressScale = (ScaleTransform)_progressFill.RenderTransform!;

            PointerPressed += Window_PointerPressed;
            StartPulse();
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Borderless window — let the user drag it anywhere.
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        /// <summary>
        /// Update the progress bar (0.0–1.0) and status text. Safe to call from
        /// any thread.
        /// </summary>
        public void SetProgress(double progress, string status)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetProgress(progress, status));
                return;
            }
            if (_closing) return;

            if (!string.IsNullOrEmpty(status)) _txtStatus.Text = status;

            _progressScale.ScaleX = Math.Min(1.0, Math.Max(0.0, progress));
        }

        /// <summary>
        /// Show a failure message on the splash, then auto-close after a short
        /// beat so the user can read WHY eye-tracking didn't start (camera in
        /// use, OS-denied, engine error, or open timed out) instead of the bar
        /// silently vanishing or hanging forever (#300, #311). Safe to call from
        /// any thread; idempotent with CloseSplash.
        /// </summary>
        public void ShowErrorAndClose(string message)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ShowErrorAndClose(message));
                return;
            }
            if (_closing) return;
            StopPulse();
            if (!string.IsNullOrEmpty(message)) _txtStatus.Text = message;

            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
            hold.Tick += (s, e) => { hold.Stop(); CloseSplash(); };
            hold.Start();
        }

        /// <summary>
        /// Fade the splash out and close it. Safe to call from any thread, and
        /// idempotent.
        /// </summary>
        public async void CloseSplash()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(CloseSplash);
                return;
            }
            if (_closing) return;
            _closing = true;
            StopPulse();

            var fadeOut = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0.0) } }
                }
            };
            await fadeOut.RunAsync(this);
            try { Close(); } catch { /* already closed */ }
        }

        // Gentle breathing pulse on the fill so the long phases (camera warmup,
        // ONNX session construction) don't look frozen between the discrete
        // progress jumps.
        private void StartPulse() => _progressFill.Classes.Add("pulse");

        private void StopPulse()
        {
            _progressFill.Classes.Remove("pulse");
            _progressFill.Opacity = 1.0;
        }
    }
}
