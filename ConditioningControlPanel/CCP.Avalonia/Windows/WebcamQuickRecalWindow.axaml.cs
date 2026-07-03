using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;
using CorePoint = ConditioningControlPanel.Core.Platform.Point;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// One-dot quick recalibration — the Avalonia port of the WPF
/// WebcamQuickRecalWindow. Shows a center pink dot, samples ~2 s of the live
/// <see cref="IWebcamService.OnGazeMove"/> stream while the user stares at it,
/// computes the per-axis median drift from the calibrated monitor's center, and
/// persists it as the calibration's runtime offset via
/// <see cref="IWebcamService.SetRuntimeOffset"/>. Requires a running tracker
/// AND an existing calibration; otherwise it shows an honest error panel and
/// never fakes success.
/// </summary>
public partial class WebcamQuickRecalWindow : Window
{
    private const int ReadyMs = 600;
    private const int SampleMs = 2000;
    private const int FinishHoldMs = 350;
    private const int MinSamples = 15;
    private const int SaccadeSettleDrop = 10;

    public bool? DialogResult { get; set; }

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private readonly IWebcamService? _webcam;

    private readonly List<CorePoint> _samples = new();
    private RuntimeGazeOffset? _savedOffset;
    private bool _collecting;
    private bool _cancelled;
    private bool _completedOk;

    public WebcamQuickRecalWindow()
    {
        InitializeComponent();
    }

    public WebcamQuickRecalWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null, IWebcamService? webcam = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
        _webcam = webcam;
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_webcam == null || !_webcam.IsRunning)
        {
            ShowError(Loc.Get("window_webcam_quick_recal_tracking_not_running_error"));
            return;
        }
        if (!_webcam.HasCalibration)
        {
            ShowError(Loc.Get("window_webcam_quick_recal_no_calibration_error"));
            return;
        }

        // Snapshot any prior offset and clear it so we sample raw projection
        // output. On cancel we restore; on success the new offset replaces the
        // old one outright. SetRuntimeOffset/ClearRuntimeOffset swap the whole
        // calibration instance atomically — direct mutation would race the
        // capture thread, which reads the offset every frame.
        _savedOffset = _webcam.GetRuntimeOffset();
        _webcam.ClearRuntimeOffset(persist: false);

        _webcam.OnGazeMove += OnGazeMove;
        _webcam.OnTrackingStateChanged += OnTrackingStateChanged;
        try
        {
            await RunSequenceAsync();
        }
        catch (Exception)
        {
            ShowError(Loc.Get("window_webcam_quick_recal_failed_error"));
        }
    }

    private async Task RunSequenceAsync()
    {
        Dot.IsVisible = true;
        TxtStatus.Text = Loc.Get("window_webcam_quick_recal_get_comfortable_then_look_pink_dot_text");
        await Task.Delay(ReadyMs);
        if (_cancelled) return;

        TxtStatus.Text = Loc.Get("window_webcam_quick_recal_hold_gaze_status");
        _samples.Clear();
        _collecting = true;
        await Task.Delay(SampleMs);
        _collecting = false;
        if (_cancelled) return;

        // Need a usable sample count. Fewer than MinSamples means the face was
        // lost or gaze was suppressed (eyes closed) for most of the window.
        if (_samples.Count < MinSamples)
        {
            ShowError(Loc.GetF("window_webcam_quick_recal_not_enough_samples_error_fmt", _samples.Count));
            return;
        }

        // Per-axis median after dropping the first ~330 ms (≈10 frames @30fps),
        // which are contaminated by the saccade onto the dot. Median is robust
        // to residual blink / fixation-break samples in the rest of the window.
        var (meanX, meanY) = MedianAfterSaccadeSettle(_samples, SaccadeSettleDrop);

        // Center of the calibrated monitor — the gaze stream and the persisted
        // offset both live in that monitor's local coordinate space. Fall back
        // to the window center when the monitor can't be resolved.
        var screen = _webcam?.GetCalibratedScreen();
        double targetX = screen != null && screen.Bounds.Width > 0 ? screen.Bounds.Width / 2.0 : Bounds.Width / 2.0;
        double targetY = screen != null && screen.Bounds.Height > 0 ? screen.Bounds.Height / 2.0 : Bounds.Height / 2.0;

        double dx = targetX - meanX;
        double dy = targetY - meanY;

        _webcam?.SetRuntimeOffset(dx, dy, persist: true);

        _completedOk = true;
        TxtStatus.Text = Loc.GetF("window_webcam_quick_recal_done_nudged_status_fmt", dx, dy);
        await Task.Delay(FinishHoldMs);
        DialogResult = true;
        Close(true);
    }

    private static (double X, double Y) MedianAfterSaccadeSettle(List<CorePoint> samples, int dropFirst)
    {
        int start = (samples.Count - dropFirst >= MinSamples) ? dropFirst : 0;
        var trimmed = (start == 0) ? samples : samples.GetRange(start, samples.Count - start);
        var xs = trimmed.Select(s => s.X).OrderBy(v => v).ToList();
        var ys = trimmed.Select(s => s.Y).OrderBy(v => v).ToList();
        return (xs[xs.Count / 2], ys[ys.Count / 2]);
    }

    private void OnGazeMove(CorePoint p)
    {
        if (!_collecting) return;
        _samples.Add(p);
    }

    private void OnTrackingStateChanged(WebcamTrackingState state)
    {
        // Quick recal samples live OnGazeMove output. If tracking ends mid-flow,
        // cancel so the saved offset gets restored on close.
        if (state == WebcamTrackingState.Stopped || state == WebcamTrackingState.Error
            || state == WebcamTrackingState.CameraInUse || state == WebcamTrackingState.CameraDenied)
        {
            _cancelled = true;
            _collecting = false;
            if (DialogResult == null) DialogResult = false;
            Close();
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancelled = true;
            _collecting = false;
            DialogResult = false;
            Close(false);
        }
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = _completedOk;
        Close(_completedOk);
    }

    private void ShowError(string detail)
    {
        Dot.IsVisible = false;
        TxtStatus.IsVisible = false;
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

        // Restore the prior offset on cancel — never strand the user with a
        // cleared calibration after they bailed out of recal.
        if (!_completedOk)
        {
            if (_savedOffset is { } saved)
                _webcam?.SetRuntimeOffset(saved.Dx, saved.Dy, persist: false);
            else
                _webcam?.ClearRuntimeOffset(persist: false);
        }
        base.OnClosed(e);
    }
}
