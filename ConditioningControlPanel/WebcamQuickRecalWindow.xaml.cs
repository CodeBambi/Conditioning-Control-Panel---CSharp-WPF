using System;
using System.Collections.Generic;
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
            // offset replaces the old one outright.
            _savedOffset = App.Webcam.Calibration.RuntimeOffset;
            App.Webcam.Calibration.RuntimeOffset = null;

            App.Webcam.OnGazeMove += OnGazeMove;
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
            if (App.Webcam != null) App.Webcam.OnGazeMove -= OnGazeMove;

            // Restore the prior offset on cancel — never strand the user with
            // a cleared calibration after they bailed out of recal.
            if (!_completedOk && App.Webcam?.Calibration != null && App.Webcam.Calibration.RuntimeOffset == null)
            {
                App.Webcam.Calibration.RuntimeOffset = _savedOffset;
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

            // Drop outliers (>2σ from the mean per axis) before computing the
            // final mean. A single blink or saccade burst can pull the mean
            // tens of pixels otherwise.
            var (meanX, meanY) = TrimmedMean(_samples);

            // Center of the calibration window itself (which is fullscreen
            // primary). Using ActualWidth/Height matches the same DIP frame
            // OnGazeMove emits in.
            double targetX = ActualWidth / 2.0;
            double targetY = ActualHeight / 2.0;

            double dx = targetX - meanX;
            double dy = targetY - meanY;

            App.Webcam!.Calibration!.RuntimeOffset = new RuntimeOffsetData
            {
                Dx = dx,
                Dy = dy,
                CapturedAt = DateTime.UtcNow,
            };
            App.Webcam.Calibration.Save();
            App.Logger?.Information(
                "WebcamQuickRecalWindow: offset captured dx={Dx:F1} dy={Dy:F1} from {N} samples (mean=({Mx:F1},{My:F1}), target=({Tx:F1},{Ty:F1}))",
                dx, dy, _samples.Count, meanX, meanY, targetX, targetY);

            _completedOk = true;
            TxtStatus.Text = $"Done. Cursor nudged by ({dx:F0}, {dy:F0}) px.";
            await Task.Delay(FinishHoldMs);
            DialogResult = true;
            Close();
        }

        private static (double X, double Y) TrimmedMean(List<WpfPoint> samples)
        {
            double sumX = 0, sumY = 0;
            foreach (var s in samples) { sumX += s.X; sumY += s.Y; }
            double mx = sumX / samples.Count;
            double my = sumY / samples.Count;

            double sx = 0, sy = 0;
            foreach (var s in samples)
            {
                sx += (s.X - mx) * (s.X - mx);
                sy += (s.Y - my) * (s.Y - my);
            }
            double stdX = Math.Sqrt(sx / samples.Count);
            double stdY = Math.Sqrt(sy / samples.Count);

            double thrX = 2.0 * stdX;
            double thrY = 2.0 * stdY;
            double sumX2 = 0, sumY2 = 0;
            int n = 0;
            foreach (var s in samples)
            {
                if (Math.Abs(s.X - mx) > thrX || Math.Abs(s.Y - my) > thrY) continue;
                sumX2 += s.X; sumY2 += s.Y; n++;
            }
            if (n == 0) return (mx, my);
            return (sumX2 / n, sumY2 / n);
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
