using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;
using CorePoint = ConditioningControlPanel.Core.Platform.Point;
using AvPoint = global::Avalonia.Point;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Fullscreen black overlay with a single cyan dot that follows the user's
/// calibrated gaze in real time — the Avalonia port of the WPF tracker test.
/// Pure visualization: it subscribes to <see cref="IWebcamService.OnGazeMove"/>,
/// applies a small 5-frame smoothing buffer, scales the monitor-local gaze
/// coordinate into this window's canvas, and renders the dot. It never modifies
/// the service or persists anything, and it never fabricates motion: with no
/// calibration loaded OnGazeMove does not fire, so the dot simply stays put and
/// an honest status line explains why.
/// </summary>
public partial class WebcamGazeTrackerWindow : Window
{
    private const int SmoothFrames = 5;

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private readonly IWebcamService? _webcam;
    private readonly Queue<AvPoint> _smoothBuffer = new();

    public WebcamGazeTrackerWindow()
    {
        InitializeComponent();
    }

    public WebcamGazeTrackerWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null, IWebcamService? webcam = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
        _webcam = webcam;
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        // Prefer the real IWebcamService (when the head injected one). Older
        // callers that only pass a frame source are treated as "no service".
        if (_webcam == null || !_webcam.IsRunning)
        {
            ShowError(Loc.Get("window_webcam_gaze_tracker_tracking_not_running_error"));
            return;
        }

        _webcam.OnGazeMove += OnGazeMove;
        _webcam.OnTrackingStateChanged += OnTrackingStateChanged;

        if (!_webcam.HasCalibration)
        {
            // OnGazeMove only fires once a calibration is loaded, so without one
            // the dot cannot track. Show an honest status line instead of fake
            // motion; if the user calibrates while the window is open, real
            // events start arriving and the line clears.
            TxtCoords.Text = Loc.Get("window_webcam_gaze_tracker_no_calibration_status");
            return;
        }

        TxtCoords.Text = "";
    }

    private void OnGazeMove(CorePoint screenGaze)
    {
        // OnGazeMove is marshalled to the UI thread by the provider; safe to
        // touch the canvas directly.
        double srcW;
        double srcH;
        var screen = _webcam?.GetCalibratedScreen();
        if (screen != null && screen.Bounds.Width > 0 && screen.Bounds.Height > 0)
        {
            srcW = screen.Bounds.Width;
            srcH = screen.Bounds.Height;
        }
        else
        {
            // Best effort when the calibrated monitor can't be resolved: treat
            // the gaze coordinate as already window-local (the window is
            // borderless-maximized on the calibrated monitor in the common case).
            srcW = Bounds.Width > 0 ? Bounds.Width : 1;
            srcH = Bounds.Height > 0 ? Bounds.Height : 1;
        }

        double destW = Bounds.Width;
        double destH = Bounds.Height;
        if (destW <= 0 || destH <= 0) return;

        double sx = destW / srcW;
        double sy = destH / srcH;

        // Scale the monitor-local gaze point proportionally into the canvas, then
        // smooth over the last few frames (mirrors the WPF window).
        double cx = screenGaze.X * sx;
        double cy = screenGaze.Y * sy;

        _smoothBuffer.Enqueue(new AvPoint(cx, cy));
        while (_smoothBuffer.Count > SmoothFrames) _smoothBuffer.Dequeue();

        double sumX = 0, sumY = 0;
        foreach (var p in _smoothBuffer) { sumX += p.X; sumY += p.Y; }
        cx = sumX / _smoothBuffer.Count;
        cy = sumY / _smoothBuffer.Count;

        double dotW = Dot.Width, dotH = Dot.Height;
        double left = Math.Max(0, Math.Min(destW - dotW, cx - dotW / 2));
        double top = Math.Max(0, Math.Min(destH - dotH, cy - dotH / 2));

        Canvas.SetLeft(Dot, left);
        Canvas.SetTop(Dot, top);
        Dot.IsVisible = true;

        // Clear any stale "no calibration" line now that real gaze is arriving.
        if (!string.IsNullOrEmpty(TxtCoords.Text) && TxtCoords.Text == Loc.Get("window_webcam_gaze_tracker_no_calibration_status"))
            TxtCoords.Text = "";

        TxtCoords.Text = $"x={cx,7:F1}  y={cy,7:F1}";
    }

    private void OnTrackingStateChanged(WebcamTrackingState state)
    {
        // Tracker test is pure visualization on top of the live stream — if the
        // service stops for any reason, close so subscriptions tear down.
        if (state == WebcamTrackingState.Stopped
            || state == WebcamTrackingState.Error
            || state == WebcamTrackingState.CameraInUse
            || state == WebcamTrackingState.CameraDenied)
        {
            Close();
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void ShowError(string detail)
    {
        DotCanvas.IsVisible = false;
        TxtErrorDetail.Text = detail;
        ErrorPanel.IsVisible = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_webcam != null)
        {
            _webcam.OnGazeMove -= OnGazeMove;
            _webcam.OnTrackingStateChanged -= OnTrackingStateChanged;
        }
        base.OnClosed(e);
    }
}
