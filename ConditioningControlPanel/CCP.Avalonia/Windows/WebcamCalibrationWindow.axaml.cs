using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Help;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Avalonia.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the fullscreen 16-point (4x4) webcam gaze-calibration window.
///
/// The window OWNS the flow (intro, the moving dot grid, per-dot iris sampling) but not the
/// math or the calibration data model, which live in the Windows tracker behind the Core
/// <see cref="IWebcamService"/> seam. Per dot it collects raw iris samples (<see cref="IWebcamService.OnRawIris"/>,
/// paired with the latest head pose), then hands the whole grid to
/// <see cref="IWebcamService.BuildCalibrationPreview"/>, which solves + applies the fit in-memory.
/// The verify panel confirms; Done persists via <see cref="IWebcamService.CommitCalibration"/>,
/// Recalibrate/ESC discards via <see cref="IWebcamService.CancelCalibrationPreview"/>. On a platform
/// with no real tracker the seam returns a failed preview and the honest "not available" panel shows.
///
/// S2 (logged human-testing/verify mode) layers a live gaze cursor + telemetry log onto the verify
/// panel; S3 adds the WPF gesture warm-up + bubble-test gaze-trim. See docs/webcam-calibration-port-plan.md.
/// </summary>
public partial class WebcamCalibrationWindow : Window
{
    // Flow timing + acceptance constants (match the WPF WebcamCalibrationWindow contract).
    private const int ReadyMs = 600;            // dot moves, user re-fixates
    private const int SampleMs = 1100;          // ~33 raw frames @ 30fps
    private const int SettleMs = 200;           // pause between dots
    private const int RetryReadyMs = 900;       // longer pause before a retry
    private const int MinSamplesPerPoint = 12;  // acceptance floor after jitter drops
    private const int MaxAttemptsPerPoint = 2;  // miss twice in a row -> fail
    private const int GridSize = 4;             // 4x4 = 16 points
    private const int RingFullSampleTarget = 20;
    private const double EdgeMargin = 40;       // dot inset from the screen edge (DIPs)
    private const string CalibrationMode = "SixteenPoint";

    /// <summary>Holds the close result for ShowDialog-style callers.</summary>
    public bool? DialogResult { get; set; }

    /// <summary>True while a calibration window is on screen.</summary>
    public static bool IsShowing { get; private set; }

    /// <summary>Set when the user chooses Recalibrate so ShowDialogWithRecalibrateAsync re-opens the flow.</summary>
    public bool WantsRecalibrate { get; private set; }

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private readonly IDialogService? _dialogService;
    private readonly IWebcamService? _webcam;
    private readonly IScreenProvider? _screenProvider;
    private readonly ISettingsService? _settings;

    // Per-dot iris samples, allocated up-front so OnRawIris lands in the active dot's bucket.
    private readonly List<List<CalibrationIrisSample>> _allSamples = new();
    private (double Yaw, double Pitch)? _lastPose;
    private bool _collecting;
    private bool _cancelled;
    private bool _completedOk;
    private int _activeDotIndex = -1;
    private ScreenInfo? _calScreen;
    private double _calW, _calH;

    private DispatcherTimer? _ringPulse;
    private double _ringPulsePhaseMs;
    private readonly TaskCompletionSource<bool> _introDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<VerifyChoice> _verifyDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum VerifyChoice { Done, Recalibrate }

    public WebcamCalibrationWindow()
    {
        InitializeComponent();
        IsShowing = true;
        var services = global::ConditioningControlPanel.Avalonia.App.Services;
        _dialogService = services?.GetService<IDialogService>();
        _webcam = services?.GetService<IWebcamService>();
        _screenProvider = services?.GetService<IScreenProvider>();
        _settings = services?.GetService<ISettingsService>();
    }

    /// <summary>Constructor preserved for callers that still pass the frame/video seams (unused here).</summary>
    public WebcamCalibrationWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_webcam == null || !_webcam.IsRunning)
        {
            ShowError(Loc.Get("window_webcam_calibration_not_available_detail"));
            return;
        }

        _calScreen = ResolveScreen();
        if (_calScreen == null)
        {
            ShowError(Loc.Get("window_webcam_calibration_not_available_detail"));
            return;
        }
        var scaling = _calScreen.Scaling <= 0 ? 1.0 : _calScreen.Scaling;
        _calW = _calScreen.Bounds.Width / scaling;
        _calH = _calScreen.Bounds.Height / scaling;

        _webcam.OnRawIris += OnRawIris;
        _webcam.OnHeadPose += OnHeadPose;
        _webcam.OnTrackingStateChanged += OnWebcamStateChanged;

        DotCanvas.IsVisible = false;
        StatusPanel.IsVisible = false;
        VerifyPanel.IsVisible = false;
        ValidationPanel.IsVisible = false;
        ErrorPanel.IsVisible = false;
        IntroPanel.IsVisible = true;
        if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = true;

        var proceed = await _introDone.Task;
        if (!proceed || _cancelled) return;

        IntroPanel.IsVisible = false;
        if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = false;
        DotCanvas.IsVisible = true;
        StatusPanel.IsVisible = true;

        try
        {
            await RunSequenceAsync();
        }
        catch (Exception)
        {
            ShowError(Loc.Get("window_webcam_calibration_not_available_detail"));
        }
    }

    private ScreenInfo? ResolveScreen()
    {
        var sp = _screenProvider;
        if (sp == null) return null;
        var all = sp.GetAllScreens();
        var pos = Position; // device px, monitor top-left for a maximized window
        foreach (var s in all)
        {
            if (pos.X >= s.Bounds.X && pos.X < s.Bounds.Right && pos.Y >= s.Bounds.Y && pos.Y < s.Bounds.Bottom)
                return s;
        }
        return sp.GetPrimaryScreen() ?? (all.Count > 0 ? all[0] : null);
    }

    private async Task RunSequenceAsync()
    {
        // Let the maximized window finish laying out so the dot coordinate space is stable.
        await Task.Delay(50);
        if (_cancelled) return;

        double xL = EdgeMargin, xR = _calW - EdgeMargin;
        double yT = EdgeMargin, yB = _calH - EdgeMargin;
        var positions = new (string Label, double X, double Y)[GridSize * GridSize];
        string[] rowLabels = { "Top", "Upper", "Lower", "Bottom" };
        string[] colLabels = { "left", "mid-left", "mid-right", "right" };
        for (int r = 0; r < GridSize; r++)
        {
            double y = yT + (yB - yT) * (r / (double)(GridSize - 1));
            for (int c = 0; c < GridSize; c++)
            {
                double x = xL + (xR - xL) * (c / (double)(GridSize - 1));
                positions[r * GridSize + c] = ($"{rowLabels[r]}-{colLabels[c]}", x, y);
            }
        }

        _allSamples.Clear();
        for (int i = 0; i < positions.Length; i++) _allSamples.Add(new List<CalibrationIrisSample>());

        for (int i = 0; i < positions.Length; i++)
        {
            if (_cancelled) return;
            MoveDotTo(positions[i].X, positions[i].Y);
            TxtProgress.Text = $"Point {i + 1} / {positions.Length}  ({positions[i].Label})";

            bool succeeded = false;
            for (int attempt = 1; attempt <= MaxAttemptsPerPoint && !succeeded; attempt++)
            {
                if (_cancelled) return;
                StopRingPulse();
                ResetProgressRing();
                _allSamples[i].Clear();
                _activeDotIndex = i;

                TxtStatus.Text = attempt == 1 ? "Look at the pink dot..." : "Missed that one - look at the pink dot again...";
                await Task.Delay(attempt == 1 ? ReadyMs : RetryReadyMs);
                if (_cancelled) return;

                TxtStatus.Text = "Hold steady - sampling...";
                _collecting = true;
                await Task.Delay(SampleMs);
                _collecting = false;
                if (_cancelled) return;

                if (_allSamples[i].Count >= MinSamplesPerPoint)
                {
                    succeeded = true;
                    UpdateProgressRing(1.0);
                    StartRingPulse();
                }
            }
            _activeDotIndex = -1;

            if (!succeeded)
            {
                ShowError($"Couldn't sample point {i + 1} ({positions[i].Label}) after {MaxAttemptsPerPoint} tries " +
                          $"(got {_allSamples[i].Count}, need {MinSamplesPerPoint}). Make sure you're well-lit, facing the camera, and your face fits in frame.");
                return;
            }

            StopRingPulse();
            await Task.Delay(SettleMs);
        }

        if (_cancelled) return;

        var dots = new List<CalibrationDotSamples>(positions.Length);
        for (int i = 0; i < positions.Length; i++)
            dots.Add(new CalibrationDotSamples(positions[i].X, positions[i].Y, _allSamples[i]));

        var result = _webcam!.BuildCalibrationPreview(dots, _calScreen!, CalibrationMode);
        if (!result.Success)
        {
            ShowError(result.Error ?? "Calibration failed. Improve lighting and try again.");
            return;
        }

        // Verify panel: the candidate fit is live (SetCalibrationLive). Show its residual and confirm.
        DotCanvas.IsVisible = false;
        StatusPanel.IsVisible = false;
        TxtVerifyStatus.Text = $"Fit residual ~{result.RmsX:F0} x {result.RmsY:F0} px. " +
                               "Click Done to keep it, or Recalibrate to try again.";
        VerifyPanel.IsVisible = true;
        if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = true;

        var choice = await _verifyDone.Task;
        if (_cancelled) return;

        if (choice == VerifyChoice.Recalibrate)
        {
            _webcam.CancelCalibrationPreview();
            WantsRecalibrate = true;
            DialogResult = false;
            Close(false);
            return;
        }

        _webcam.CommitCalibration();
        if (_settings?.Current is { } s)
        {
            s.WebcamCalibrated = true;
            s.WebcamCalibrationMode = CalibrationMode;
            _settings.Save();
        }
        _completedOk = true;
        DialogResult = true;
        Close(true);
    }

    private void OnRawIris(double dx, double dy)
    {
        if (!_collecting || _activeDotIndex < 0 || _activeDotIndex >= _allSamples.Count) return;
        var pose = _lastPose;
        var list = _allSamples[_activeDotIndex];
        list.Add(new CalibrationIrisSample(dx, dy, pose?.Yaw ?? 0, pose?.Pitch ?? 0, pose.HasValue));
        UpdateProgressRing(Math.Min(1.0, list.Count / (double)RingFullSampleTarget));
    }

    private void OnHeadPose(double yaw, double pitch) => _lastPose = (yaw, pitch);

    private void OnWebcamStateChanged(WebcamTrackingState state)
    {
        if (state is WebcamTrackingState.Stopped or WebcamTrackingState.Error
            or WebcamTrackingState.CameraInUse or WebcamTrackingState.CameraDenied)
        {
            _cancelled = true;
            _introDone.TrySetResult(false);
            _verifyDone.TrySetResult(VerifyChoice.Recalibrate);
            DialogResult ??= false;
            Dispatcher.UIThread.Post(() => { try { Close(false); } catch { } });
        }
    }

    private void MoveDotTo(double x, double y)
    {
        Canvas.SetLeft(Dot, x - Dot.Width / 2);
        Canvas.SetTop(Dot, y - Dot.Height / 2);
        Canvas.SetLeft(DotRingBg, x - DotRingBg.Width / 2);
        Canvas.SetTop(DotRingBg, y - DotRingBg.Height / 2);
        Canvas.SetLeft(DotRingFg, x - DotRingFg.Width / 2);
        Canvas.SetTop(DotRingFg, y - DotRingFg.Height / 2);
    }

    private void UpdateProgressRing(double progress) => DotRingFg.Opacity = 0.15 + 0.85 * Math.Clamp(progress, 0, 1);

    private void ResetProgressRing() => DotRingFg.Opacity = 0.15;

    // Looping "got it" pulse. v12: a DispatcherTimer, NOT an infinite Animation.RunAsync (throws by design).
    private void StartRingPulse()
    {
        StopRingPulse();
        _ringPulsePhaseMs = 0;
        _ringPulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _ringPulse.Tick += (_, _) =>
        {
            _ringPulsePhaseMs += 16;
            double t = (1 - Math.Cos(_ringPulsePhaseMs / 500.0 * Math.PI)) / 2.0; // 0..1..0 over ~1s
            DotRingFg.Opacity = 0.55 + 0.45 * t;
        };
        _ringPulse.Start();
    }

    private void StopRingPulse()
    {
        _ringPulse?.Stop();
        _ringPulse = null;
    }

    private void BtnIntroContinue_Click(object? sender, RoutedEventArgs e) => _introDone.TrySetResult(true);

    private void BtnCalibrationHelp_Click(object? sender, RoutedEventArgs e)
    {
        var content = HelpContentService.GetContent("WebcamCalibration");
        if (content?.SectionId == "WebcamCalibration")
        {
            HelpVideoWindow.Show(content, this, topmost: true);
            return;
        }
        _ = _dialogService?.ShowMessageAsync(
            Loc.Get("window_webcam_calibration_help_title"),
            Loc.Get("window_webcam_calibration_help_not_ported_message"),
            DialogSeverity.Info);
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = _completedOk;
        Close(_completedOk);
    }

    // Verify panel: "Verify Accuracy" is the S2 logged live-gaze test; not wired in S1c.
    private void BtnVerifyAccuracy_Click(object? sender, RoutedEventArgs e) { }

    private void BtnVerifyRecalibrate_Click(object? sender, RoutedEventArgs e) => _verifyDone.TrySetResult(VerifyChoice.Recalibrate);

    private void BtnVerifyDone_Click(object? sender, RoutedEventArgs e) => _verifyDone.TrySetResult(VerifyChoice.Done);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancelled = true;
            _collecting = false;
            _introDone.TrySetResult(false);
            _verifyDone.TrySetResult(VerifyChoice.Recalibrate);
            _webcam?.CancelCalibrationPreview();
            DialogResult = false;
            Close(false);
        }
    }

    private void ShowError(string detail)
    {
        StopRingPulse();
        _collecting = false;
        DotCanvas.IsVisible = false;
        IntroPanel.IsVisible = false;
        StatusPanel.IsVisible = false;
        ValidationPanel.IsVisible = false;
        VerifyPanel.IsVisible = false;
        if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = false;
        TxtErrorDetail.Text = detail;
        ErrorPanel.IsVisible = true;
    }

    /// <summary>
    /// ShowDialog helper that honors the Recalibrate loop: re-opens the flow while the user keeps
    /// choosing Recalibrate, returns the final result otherwise.
    /// </summary>
    public static async Task<bool?> ShowDialogWithRecalibrateAsync(Window? owner)
    {
        while (true)
        {
            WebcamCalibrationWindow dlg;
            try { dlg = new WebcamCalibrationWindow(); }
            catch { return false; }

            bool? result;
            try { result = await dlg.ShowDialog<bool?>(owner!); }
            catch { return false; }

            if (dlg.WantsRecalibrate) continue;
            return result;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        IsShowing = false;
        _cancelled = true;
        _collecting = false;
        StopRingPulse();
        _introDone.TrySetResult(false);
        _verifyDone.TrySetResult(VerifyChoice.Recalibrate);
        if (_webcam != null)
        {
            _webcam.OnRawIris -= OnRawIris;
            _webcam.OnHeadPose -= OnHeadPose;
            _webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
        }
        base.OnClosed(e);
    }
}
