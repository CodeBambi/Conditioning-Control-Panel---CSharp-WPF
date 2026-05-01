using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Services;
using WpfPoint = System.Windows.Point;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Fullscreen black overlay with a single cyan dot that follows the user's
    /// calibrated gaze in real time. Pure visualization — does not modify the
    /// service or persist anything. Useful for eyeballing tracking precision
    /// after calibration.
    ///
    /// Caller must ensure App.Webcam is running AND has a calibration loaded
    /// (the dot's position comes from OnGazeMove, which only fires when a
    /// homography is available).
    /// </summary>
    public partial class WebcamGazeTrackerWindow : System.Windows.Window
    {
        private const int SmoothFrames = 5;     // small extra smoothing on top of the upstream iris-vector smoothing

        private readonly Queue<WpfPoint> _smoothBuffer = new();

        public WebcamGazeTrackerWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.Webcam == null || !App.Webcam.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before opening the tracker test.");
                return;
            }
            if (App.Webcam.Calibration == null)
            {
                ShowError("No calibration loaded. Run Calibrate (16-point) first — the tracker test needs a calibration to project gaze onto the screen.");
                return;
            }

            App.Webcam.OnGazeMove += OnGazeMove;
            App.Webcam.OnTrackingStateChanged += OnWebcamStateChanged;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (App.Webcam != null)
            {
                App.Webcam.OnGazeMove -= OnGazeMove;
                App.Webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
            }
        }

        private void OnWebcamStateChanged(WebcamTrackingState state)
        {
            // Tracker test is pure visualization on top of the live stream — if
            // the service stops for any reason, close the window so subscriptions
            // tear down and we don't sit alive waiting for events that won't fire.
            if (state == WebcamTrackingState.Stopped
                || state == WebcamTrackingState.Error
                || state == WebcamTrackingState.CameraInUse
                || state == WebcamTrackingState.CameraDenied)
            {
                Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void BtnErrorClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnGazeMove(WpfPoint screenPoint)
        {
            // OnGazeMove fires on the WPF dispatcher already (Service.Dispatch
            // marshals it). We can update UI directly.
            _smoothBuffer.Enqueue(screenPoint);
            while (_smoothBuffer.Count > SmoothFrames) _smoothBuffer.Dequeue();

            double sumX = 0, sumY = 0;
            foreach (var p in _smoothBuffer) { sumX += p.X; sumY += p.Y; }
            double cx = sumX / _smoothBuffer.Count;
            double cy = sumY / _smoothBuffer.Count;

            // Clip to window bounds so the dot stays visible even when the
            // homography projects the gaze slightly outside the display.
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double dotW = Dot.Width, dotH = Dot.Height;
            double left = Math.Max(0, Math.Min(w - dotW, cx - dotW / 2));
            double top  = Math.Max(0, Math.Min(h - dotH, cy - dotH / 2));

            System.Windows.Controls.Canvas.SetLeft(Dot, left);
            System.Windows.Controls.Canvas.SetTop(Dot, top);
            if (Dot.Visibility != Visibility.Visible) Dot.Visibility = Visibility.Visible;

            TxtCoords.Text = $"x={cx,7:F1}  y={cy,7:F1}";
        }

        private void ShowError(string detail)
        {
            DotCanvas.Visibility = Visibility.Collapsed;
            TxtErrorDetail.Text = detail;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
