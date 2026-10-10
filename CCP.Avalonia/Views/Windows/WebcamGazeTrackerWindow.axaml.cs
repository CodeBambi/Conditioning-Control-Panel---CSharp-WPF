using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Fullscreen black overlay with a single cyan dot that follows the user's
    /// calibrated gaze in real time. Pure visualization — does not modify the
    /// service or persist anything. Useful for eyeballing tracking precision
    /// after calibration.
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamGazeTrackerWindow.xaml.cs over
    /// Platform/WebcamTracker. Deviations: Loaded -> OnOpened, handlers wired in the constructor,
    /// ActualWidth/Height -> Bounds, and WPF's OnTrackingStateChanged(Stopped/Error/...) close
    /// becomes "close when the tracker is no longer running" (StateChanged).
    /// </summary>
    public partial class WebcamGazeTrackerWindow : Window
    {
        private const int SmoothFrames = 5;     // small extra smoothing on top of the upstream iris-vector smoothing

        private readonly Queue<Point> _smoothBuffer = new();

        private readonly Canvas _dotCanvas;
        private readonly Ellipse _dot;
        private readonly TextBlock _txtCoords, _txtErrorDetail;
        private readonly Border _errorPanel;

        public WebcamGazeTrackerWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _dotCanvas = this.FindControl<Canvas>("DotCanvas")!;
            _dot = this.FindControl<Ellipse>("Dot")!;
            _txtCoords = this.FindControl<TextBlock>("TxtCoords")!;
            _txtErrorDetail = this.FindControl<TextBlock>("TxtErrorDetail")!;
            _errorPanel = this.FindControl<Border>("ErrorPanel")!;

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            var tracker = WebcamTracker.Instance;
            if (!tracker.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before opening the tracker test.");
                return;
            }
            if (tracker.Calibration == null)
            {
                ShowError("No calibration loaded. Run Calibrate (16-point) first - the tracker test needs a calibration to project gaze onto the screen.");
                return;
            }
            tracker.OnGazeMove += OnGazeMove;
            tracker.StateChanged += OnTrackerStateChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            WebcamTracker.Instance.OnGazeMove -= OnGazeMove;
            WebcamTracker.Instance.StateChanged -= OnTrackerStateChanged;
        }

        private void OnTrackerStateChanged()
        {
            if (!WebcamTracker.Instance.IsRunning) Close();
        }

        private void OnGazeMove(Point screenPoint)
        {
            // OnGazeMove is marshalled onto the UI thread by the service (Service.Dispatch),
            // so we can update UI directly.
            _smoothBuffer.Enqueue(screenPoint);
            while (_smoothBuffer.Count > SmoothFrames) _smoothBuffer.Dequeue();

            double sumX = 0, sumY = 0;
            foreach (var p in _smoothBuffer) { sumX += p.X; sumY += p.Y; }
            double cx = sumX / _smoothBuffer.Count;
            double cy = sumY / _smoothBuffer.Count;

            // Clip to window bounds so the dot stays visible even when the
            // homography projects the gaze slightly outside the display.
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            double dotW = _dot.Width, dotH = _dot.Height;
            double left = Math.Max(0, Math.Min(w - dotW, cx - dotW / 2));
            double top  = Math.Max(0, Math.Min(h - dotH, cy - dotH / 2));

            Canvas.SetLeft(_dot, left);
            Canvas.SetTop(_dot, top);
            _dot.IsVisible = true;

            _txtCoords.Text = $"x={cx,7:F1}  y={cy,7:F1}";
        }

        private void ShowError(string detail)
        {
            _dotCanvas.IsVisible = false;
            _txtErrorDetail.Text = detail;
            _errorPanel.IsVisible = true;
        }
    }
}
