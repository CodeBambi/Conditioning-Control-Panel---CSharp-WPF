using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger? _verifyLogger;

    // Per-dot iris samples, allocated up-front so OnRawIris lands in the active dot's bucket.
    private readonly List<List<CalibrationIrisSample>> _allSamples = new();
    private (double Yaw, double Pitch)? _lastPose;
    private bool _collecting;
    private bool _cancelled;
    private bool _completedOk;
    private int _activeDotIndex = -1;
    private ScreenInfo? _calScreen;
    private double _calW, _calH;

    // S2 verify/testing mode state (live gaze cursor + per-target accuracy).
    private Ellipse? _gazeCursor;
    private bool _verifyRunning;
    private bool _verifyCollecting;
    private readonly List<(double X, double Y)> _verifyGaze = new();

    private DispatcherTimer? _ringPulse;
    private double _ringPulsePhaseMs;
    private readonly TaskCompletionSource<bool> _introDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<VerifyChoice>? _verifyChoice;

    // Gaze-pipeline A/B/C selection (intro panel). The tracker's runtime feature
    // strictly follows the LOADED calibration; the selector only chooses what the
    // NEXT calibrate run trains. _baseline* snapshot the loaded calibration's
    // mode+backbone at open so the notice can tell the user when a selection needs
    // (re)calibration, and so a close-without-commit restores it.
    private GazeFeatureMode _selectedMode = GazeFeatureMode.Current;
    private DeepGazeBackbone _selectedBackbone = DeepGazeBackbone.MobileOneS0;
    private GazeFeatureMode _baselineMode = GazeFeatureMode.Current;
    private DeepGazeBackbone _baselineBackbone = DeepGazeBackbone.MobileOneS0;
    private bool _committedMode;
    private bool _initSelectors;
    private static readonly DeepGazeBackbone[] BackboneOrder =
    {
        DeepGazeBackbone.MobileOneS0, DeepGazeBackbone.MobileNetV2,
        DeepGazeBackbone.ResNet18, DeepGazeBackbone.ResNet34, DeepGazeBackbone.ResNet50,
    };

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
        _verifyLogger = services?.GetService<ILoggerFactory>()?.CreateLogger("WebcamVerify");
    }

    /// <summary>Constructor preserved for callers that still pass the frame/video seams (unused here).</summary>
    public WebcamCalibrationWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private async void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_webcam == null)
        {
            ShowError(Loc.Get("window_webcam_calibration_not_available_detail"));
            return;
        }
        if (!_webcam.IsRunning)
        {
            // The tracker exists on this platform - it's just not running. Don't claim the
            // feature is missing; tell the user to start webcam tracking first.
            ShowError(Loc.Get("window_webcam_tracking_off_detail"), Loc.Get("window_webcam_tracking_off_title"));
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
        InitPipelineSelectors();
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

        // Retry loop: Recalibrate (and declining a too-inaccurate fit) re-runs the whole grid
        // in-window without closing, so it works whether the window was opened via .Show() or
        // ShowDialog. Mirrors the WPF ShowDialogWithRecalibrate loop.
        while (!_cancelled)
        {
            VerifyPanel.IsVisible = false;
            DotCanvas.IsVisible = true;
            StatusPanel.IsVisible = true;

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

            // Fit-quality gate (WPF WebcamCalibrationWindow:528): a fit whose residual exceeds ~20%
            // of the screen "completed but is wildly off" - warn prominently and steer to Recalibrate
            // instead of letting the user unknowingly keep an unusable calibration.
            bool poorFit = result.RmsX > _calW * 0.20 || result.RmsY > _calH * 0.20;

            StopRingPulse();
            DotCanvas.IsVisible = false;
            StatusPanel.IsVisible = false;
            TxtVerifyStatus.Text = poorFit
                ? $"This calibration came out very inaccurate (fit error ~{result.RmsX:F0} x {result.RmsY:F0} px) - eye tracking would be unreliable. For a better result: good even lighting, no glare on glasses, hold your head still and look right at each dot. Recalibrate is strongly recommended."
                : $"Fit residual ~{result.RmsX:F0} x {result.RmsY:F0} px. Click Done to keep it, or Recalibrate to try again.";
            VerifyPanel.IsVisible = true;
            if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = true;

            _verifyChoice = new TaskCompletionSource<VerifyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var choice = await _verifyChoice.Task;
            if (_cancelled) return;

            if (choice == VerifyChoice.Recalibrate)
            {
                _webcam.CancelCalibrationPreview();
                continue; // re-run the grid in-window
            }

            _webcam.CommitCalibration();
            _committedMode = true;
            if (_settings?.Current is { } s)
            {
                s.WebcamCalibrated = true;
                s.WebcamCalibrationMode = CalibrationMode;
                _settings.Save();
            }
            _completedOk = true;
            DialogResult = true;
            Close(true);
            return;
        }
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
            _verifyChoice?.TrySetResult(VerifyChoice.Recalibrate);
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

    private void BtnIntroContinue_Click(object? sender, RoutedEventArgs e)
    {
        // Commit the pipeline selection to the tracker BEFORE sampling so the
        // grid captures the chosen feature and the fit is stamped with it.
        if (_webcam != null)
        {
            _webcam.SetGazePipelineMode(_selectedMode);
            if (_selectedMode == GazeFeatureMode.DeepModel) _webcam.SetDeepGazeModel(_selectedBackbone);
        }
        _introDone.TrySetResult(true);
    }

    /// <summary>
    /// Populate the gaze-engine + backbone selectors from the tracker's current
    /// (loaded-calibration) mode, and snapshot it as the baseline for the
    /// "needs calibration" notice + close-without-commit restore.
    /// </summary>
    private void InitPipelineSelectors()
    {
        if (_webcam == null || CmbGazeMode == null) return;
        _initSelectors = true;
        try
        {
            _baselineMode = _webcam.GazePipelineMode;
            _baselineBackbone = _webcam.DeepGazeModel;
            _selectedMode = _baselineMode;
            _selectedBackbone = _baselineBackbone;

            CmbGazeMode.SelectedIndex = _selectedMode == GazeFeatureMode.DeepModel ? 1 : 0;
            if (CmbBackbone != null)
            {
                int bi = Array.IndexOf(BackboneOrder, _selectedBackbone);
                CmbBackbone.SelectedIndex = bi >= 0 ? bi : 0;
            }
            if (BackbonePanel != null) BackbonePanel.IsVisible = _selectedMode == GazeFeatureMode.DeepModel;
        }
        finally { _initSelectors = false; }
        UpdatePipelineNotice();
    }

    private void CmbGazeMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initSelectors || CmbGazeMode == null) return;
        _selectedMode = CmbGazeMode.SelectedIndex == 1 ? GazeFeatureMode.DeepModel : GazeFeatureMode.Current;
        if (BackbonePanel != null) BackbonePanel.IsVisible = _selectedMode == GazeFeatureMode.DeepModel;
        UpdatePipelineNotice();
    }

    private void CmbBackbone_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initSelectors || CmbBackbone == null) return;
        int ix = CmbBackbone.SelectedIndex;
        if (ix >= 0 && ix < BackboneOrder.Length) _selectedBackbone = BackboneOrder[ix];
        UpdatePipelineNotice();
    }

    /// <summary>
    /// Tell the user whether the current selection is already calibrated (active)
    /// or needs a (re)calibration, and block Continue when Deep is picked but the
    /// model files are missing.
    /// </summary>
    private void UpdatePipelineNotice()
    {
        if (TxtPipelineNotice == null) return;

        if (_selectedMode == GazeFeatureMode.DeepModel && !(_webcam?.DeepGazeModelAvailable ?? false))
        {
            TxtPipelineNotice.Text = Loc.Get("window_webcam_cal_pipeline_deep_missing");
            if (BtnIntroContinue != null) BtnIntroContinue.IsEnabled = false;
            return;
        }
        if (BtnIntroContinue != null) BtnIntroContinue.IsEnabled = true;

        bool matches = _selectedMode == _baselineMode
            && (_selectedMode != GazeFeatureMode.DeepModel || _selectedBackbone == _baselineBackbone)
            && (_webcam?.HasCalibration ?? false);
        TxtPipelineNotice.Text = matches
            ? Loc.Get("window_webcam_cal_pipeline_calibrated")
            : Loc.Get("window_webcam_cal_pipeline_needs_cal");
    }

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

    // S2: "Verify Accuracy" runs a logged live-gaze test against known targets so the user can
    // confirm tracking works (live cursor on-screen) and the log captures AGGREGATE error only.
    private void BtnVerifyAccuracy_Click(object? sender, RoutedEventArgs e) => _ = RunLiveGazeTestAsync();

    private async Task RunLiveGazeTestAsync()
    {
        if (_verifyRunning || _webcam == null || _cancelled) return;
        _verifyRunning = true;
        try
        {
            VerifyPanel.IsVisible = false;
            if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = false;
            DotCanvas.IsVisible = true;
            StatusPanel.IsVisible = true;
            EnsureGazeCursor();
            _webcam.OnGazeMove += OnVerifyGaze;

            // Spread targets in monitor-local DIP space (same space OnGazeMove emits): center + insets.
            var targets = new (double X, double Y)[]
            {
                (_calW * 0.5, _calH * 0.5),
                (_calW * 0.15, _calH * 0.15),
                (_calW * 0.85, _calH * 0.15),
                (_calW * 0.15, _calH * 0.85),
                (_calW * 0.85, _calH * 0.85),
            };

            double sumErr = 0, maxErr = 0; int measured = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (_cancelled) break;
                MoveDotTo(targets[i].X, targets[i].Y);
                TxtStatus.Text = "Look at the pink dot...";
                TxtProgress.Text = $"Accuracy check {i + 1} / {targets.Length}";
                await Task.Delay(650);
                if (_cancelled) break;

                _verifyGaze.Clear();
                _verifyCollecting = true;
                await Task.Delay(1400);
                _verifyCollecting = false;
                if (_cancelled) break;

                if (_verifyGaze.Count >= 5)
                {
                    double mx = 0, my = 0;
                    foreach (var g in _verifyGaze) { mx += g.X; my += g.Y; }
                    mx /= _verifyGaze.Count; my /= _verifyGaze.Count;
                    double err = Math.Sqrt((mx - targets[i].X) * (mx - targets[i].X) + (my - targets[i].Y) * (my - targets[i].Y));
                    sumErr += err; if (err > maxErr) maxErr = err; measured++;
                    TxtStatus.Text = $"~{err:F0} px off";
                    await Task.Delay(250);
                }
            }

            _webcam.OnGazeMove -= OnVerifyGaze;
            RemoveGazeCursor();
            DotCanvas.IsVisible = false;
            if (_cancelled) return;

            if (measured > 0)
            {
                double mean = sumErr / measured;
                // Privacy: only AGGREGATE accuracy metrics are logged (like the fit RMS) - never
                // per-frame gaze points / iris vectors; the live cursor was on-screen only.
                _verifyLogger?.LogInformation(
                    "webcam-verify: targets={Measured}/{Total} mean_err={Mean:F1} max_err={Max:F1} DIPs",
                    measured, targets.Length, mean, maxErr);
                string verdict = mean <= 90 ? "looks accurate" : mean <= 180 ? "usable but loose" : "inaccurate - consider Recalibrate";
                TxtVerifyStatus.Text = $"Accuracy: mean ~{mean:F0} px, worst ~{maxErr:F0} px ({verdict}). " +
                                       "Keep it with Done, or try again with Recalibrate.";
            }
            else
            {
                TxtVerifyStatus.Text = "Couldn't measure accuracy (no gaze samples - is your face in frame and lit?). " +
                                       "You can still keep the calibration or recalibrate.";
            }

            StatusPanel.IsVisible = false;
            VerifyPanel.IsVisible = true;
            if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = true;
        }
        catch (Exception)
        {
            try { _webcam.OnGazeMove -= OnVerifyGaze; } catch { }
            RemoveGazeCursor();
        }
        finally
        {
            _verifyCollecting = false;
            _verifyRunning = false;
        }
    }

    private void OnVerifyGaze(Point p)
    {
        if (_gazeCursor != null)
        {
            Canvas.SetLeft(_gazeCursor, p.X - _gazeCursor.Width / 2);
            Canvas.SetTop(_gazeCursor, p.Y - _gazeCursor.Height / 2);
        }
        if (_verifyCollecting) _verifyGaze.Add((p.X, p.Y));
    }

    private void EnsureGazeCursor()
    {
        if (_gazeCursor != null) return;
        _gazeCursor = new Ellipse
        {
            Width = 30, Height = 30,
            Fill = new SolidColorBrush(Color.FromArgb(160, 80, 220, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(220, 200, 245, 255)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        DotCanvas.Children.Add(_gazeCursor);
    }

    private void RemoveGazeCursor()
    {
        if (_gazeCursor == null) return;
        DotCanvas.Children.Remove(_gazeCursor);
        _gazeCursor = null;
    }

    private void BtnVerifyRecalibrate_Click(object? sender, RoutedEventArgs e) => _verifyChoice?.TrySetResult(VerifyChoice.Recalibrate);

    private void BtnVerifyDone_Click(object? sender, RoutedEventArgs e) => _verifyChoice?.TrySetResult(VerifyChoice.Done);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancelled = true;
            _collecting = false;
            _introDone.TrySetResult(false);
            _verifyChoice?.TrySetResult(VerifyChoice.Recalibrate);
            _webcam?.CancelCalibrationPreview();
            DialogResult = false;
            Close(false);
        }
    }

    private void ShowError(string detail, string? title = null)
    {
        StopRingPulse();
        _collecting = false;
        DotCanvas.IsVisible = false;
        IntroPanel.IsVisible = false;
        StatusPanel.IsVisible = false;
        ValidationPanel.IsVisible = false;
        VerifyPanel.IsVisible = false;
        if (ShortcutHintBanner != null) ShortcutHintBanner.IsVisible = false;
        if (TxtErrorTitle != null) TxtErrorTitle.Text = title ?? Loc.Get("window_webcam_calibration_not_available_title");
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
        _verifyChoice?.TrySetResult(VerifyChoice.Recalibrate);
        if (_webcam != null)
        {
            // If the user changed the pipeline selection but never committed a
            // calibration for it, restore the tracker to the loaded calibration's
            // mode so runtime never runs an uncalibrated feature.
            if (!_committedMode)
            {
                try
                {
                    _webcam.SetGazePipelineMode(_baselineMode);
                    if (_baselineMode == GazeFeatureMode.DeepModel) _webcam.SetDeepGazeModel(_baselineBackbone);
                }
                catch { }
            }
            _webcam.OnRawIris -= OnRawIris;
            _webcam.OnHeadPose -= OnHeadPose;
            _webcam.OnGazeMove -= OnVerifyGaze;
            _webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
        }
        base.OnClosed(e);
    }
}
