using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Services;
using WpfPoint = System.Windows.Point;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One-dot quick recal: shows a center pink dot, samples ~2 s of OnGazeMove
    /// projections while the user stares at it, computes the mean drift from
    /// screen center, and persists it as <see cref="WebcamCalibrationData.RuntimeOffset"/>.
    /// At runtime the offset is added after the polynomial projection in
    /// <see cref="WebcamTrackingService"/> so the cursor lands where the user
    /// is actually looking — no full 16-point recalibration needed.
    ///
    /// Caller must ensure App.Webcam is running AND has a calibration loaded.
    /// </summary>
    public partial class WebcamQuickRecalWindow : System.Windows.Window
    {
        private const int ReadyMs = 600;
        private const int SampleMs = 2000;
        private const int FinishHoldMs = 350;

        private readonly List<WpfPoint> _samples = new();
        private bool _collecting;
        private bool _cancelled;
        private bool _completedOk;
        private RuntimeOffsetData? _savedOffset;

        public WebcamQuickRecalWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.Webcam == null || !App.Webcam.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before quick-recalibrating.");
                return;
            }
            if (App.Webcam.Calibration == null)
            {
                ShowError("No calibration loaded. Run the full Calibrate (16-point) flow first — quick recal only nudges an existing calibration.");
                return;
            }

            // Snapshot any prior offset and clear it so we sample raw projection
            // output. If the user cancels, we restore. On success, the new
            // offset replaces the old one outright. SetRuntimeOffset swaps the
            // whole calibration instance atomically — direct mutation would
            // race the capture thread, which reads the offset every frame.
            _savedOffset = App.Webcam.Calibration.RuntimeOffset;
            App.Webcam.SetRuntimeOffset(null, persist: false);

            App.Webcam.OnGazeMove += OnGazeMove;
            App.Webcam.OnTrackingStateChanged += OnWebcamStateChanged;
            try
            {
                await RunSequenceAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamQuickRecalWindow: quick-recal sequence threw");
                ShowError("Quick recal failed unexpectedly. See logs/app.log for details.");
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (App.Webcam != null)
            {
                App.Webcam.OnGazeMove -= OnGazeMove;
                App.Webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
            }

            // Restore the prior offset on cancel — never strand the user with
            // a cleared calibration after they bailed out of recal.
            if (!_completedOk)
            {
                App.Webcam?.SetRuntimeOffset(_savedOffset, persist: false);
            }
        }

        private void OnWebcamStateChanged(WebcamTrackingState state)
        {
            // Quick recal samples live OnGazeMove output. If tracking ends mid-flow,
            // close so subscriptions tear down and the saved offset gets restored.
            if (state == WebcamTrackingState.Stopped || state == WebcamTrackingState.Error
                || state == WebcamTrackingState.CameraInUse || state == WebcamTrackingState.CameraDenied)
            {
                _cancelled = true;
                _collecting = false;
                if (DialogResult == null) DialogResult = false;
                Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _cancelled = true;
                _collecting = false;
                DialogResult = false;
                Close();
            }
        }

        private void BtnErrorClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = _completedOk;
            Close();
        }

        private void OnGazeMove(WpfPoint p)
        {
            if (!_collecting) return;
            _samples.Add(p);
        }

        private async Task RunSequenceAsync()
        {
            Dot.Visibility = Visibility.Visible;
            TxtStatus.Text = "Get comfortable, then look at the pink dot.";
            await Task.Delay(ReadyMs);
            if (_cancelled) return;

            TxtStatus.Text = "Hold your gaze on the dot…";
            _samples.Clear();
            CalibrationSoundService.DotSampleStart();
            _collecting = true;
            await Task.Delay(SampleMs);
            _collecting = false;
            if (_cancelled) return;

            // Need a usable sample count. Less than ~15 means face was lost or
            // gaze was suppressed (eyes closed) for most of the window — bail.
            if (_samples.Count < 15)
            {
                ShowError($"Didn't capture enough gaze samples ({_samples.Count}). Make sure your face is visible and try again.");
                return;
            }

            // Compute the per-axis median over samples after dropping the first
            // ~330 ms (≈10 frames at 30 fps). The early samples are
            // contaminated by the saccade onto the dot from wherever the eye
            // happened to be — typically the status text just above the dot
            // — and used to bias the mean upward, which made the offset a
            // downward nudge that pushed the cursor below the user's gaze
            // post-recal. Median (not mean) is robust to the residual blink
            // / fixation-break samples in the rest of the window without
            // having its center estimate inflated by them.
            var (meanX, meanY) = MedianAfterSaccadeSettle(_samples, dropFirst: 10);

            // Center of the calibration window itself. The window is
            // borderless-maximized so its bounds match the monitor it opened
            // on (not necessarily the primary monitor). ActualWidth/Height
            // matches the DIP frame OnGazeMove emits in for that calibration.
            double targetX = ActualWidth / 2.0;
            double targetY = ActualHeight / 2.0;

            double dx = targetX - meanX;
            double dy = targetY - meanY;

            App.Webcam!.SetRuntimeOffset(new RuntimeOffsetData
            {
                Dx = dx,
                Dy = dy,
                CapturedAt = DateTime.UtcNow,
            }, persist: true);
            App.Logger?.Information(
                "WebcamQuickRecalWindow: offset captured dx={Dx:F1} dy={Dy:F1} from {N} samples (mean=({Mx:F1},{My:F1}), target=({Tx:F1},{Ty:F1}))",
                dx, dy, _samples.Count, meanX, meanY, targetX, targetY);

            _completedOk = true;
            CalibrationSoundService.QuickRecalComplete();
            TxtStatus.Text = $"Done. Cursor nudged by ({dx:F0}, {dy:F0}) px.";
            await Task.Delay(FinishHoldMs);
            DialogResult = true;
            Close();
        }

        // Drops the first <paramref name="dropFirst"/> samples to skip the
        // saccade onto the dot, then returns the per-axis median of what's
        // left. Median is robust to blinks / fixation breaks that sometimes
        // contaminate the rest of the window — unlike the mean+σ trim it
        // replaces, which got its center and spread estimates inflated by
        // the very samples it was supposed to filter.
        private static (double X, double Y) MedianAfterSaccadeSettle(List<WpfPoint> samples, int dropFirst)
        {
            int start = (samples.Count - dropFirst >= 15) ? dropFirst : 0;
            var trimmed = (start == 0) ? samples : samples.GetRange(start, samples.Count - start);
            var xs = trimmed.Select(s => s.X).OrderBy(v => v).ToList();
            var ys = trimmed.Select(s => s.Y).OrderBy(v => v).ToList();
            return (xs[xs.Count / 2], ys[ys.Count / 2]);
        }

        private void ShowError(string detail)
        {
            Dot.Visibility = Visibility.Collapsed;
            TxtStatus.Visibility = Visibility.Collapsed;
            TxtErrorDetail.Text = detail;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
