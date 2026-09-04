using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Windows
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
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamGazeTrackerWindow.xaml.cs. Deviations:
    ///  - Loaded -> <see cref="OnOpened"/>, and the KeyDown / Click handlers are wired in the
    ///    constructor rather than in markup, per the porting convention.
    ///  - <c>ActualWidth</c>/<c>ActualHeight</c> -> <c>Bounds.Width</c>/<c>Bounds.Height</c>.
    ///  - <c>OnWebcamStateChanged</c> is gone with its <c>WebcamTrackingState</c> enum, which lives
    ///    in the WPF head's Services/Webcam and may not be referenced from here.
    ///  - <c>Window_Closed</c> only unsubscribed from the service, so it goes with the
    ///    subscription; there is nothing left for it to do.
    ///
    /// <para><b>NO OPENER, DELIBERATELY.</b> WPF opens this from
    /// <c>MainWindow.BtnWebcamDebugTrackerTest_Click</c> (MainWindow.LabTab.cs:1059) and only after
    /// two preconditions it can evaluate and this head cannot: the tracking service is RUNNING
    /// (starting it here if needed) and <c>svc.Calibration != null</c>. Neither exists on this head
    /// — Services/Webcam/WebcamTrackingService.cs is the device, and Core holds only WebcamConsent
    /// plus CoreWebcam (capability + revoke, no feed) — so a button wired to this window would put
    /// up a full-screen "gaze
    /// tracker" whose dot can never move, with the two checks that would have refused honestly
    /// silently dropped. That is the judgement already recorded for the Lab status pills
    /// (MainShellWindow.LabTab.cs) and it holds here. The window stays reachable only from
    /// <c>--render-view</c> until a tracker seam lands.</para>
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

            // ponytail: needs ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs
            // (IsRunning / Calibration / OnGazeMove / OnTrackingStateChanged). It is head-only by
            // construction. CoreWebcam now exists but carries capability + consent-revoke ONLY,
            // deliberately not IsRunning/Calibration - see its class doc. Gating on those two alone
            // would be the worse half-port: the preconditions would pass and the dot would still
            // never move, because OnGazeMove is what draws it. With the feed back, this checks both
            // preconditions, subscribes OnGazeMove and closes the window when the service stops.
            // Without it there is no tracking to visualise, which is exactly the WPF original's
            // first error path, so it takes that path verbatim.
            ShowError("Webcam tracking is not running. Start tracking before opening the tracker test.");
        }

        /// <summary>
        /// Pure view maths, kept intact: it averages the last <see cref="SmoothFrames"/> gaze
        /// projections, clips the dot to the window and moves it. Unreachable until the service
        /// above is back to raise it.
        /// </summary>
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
