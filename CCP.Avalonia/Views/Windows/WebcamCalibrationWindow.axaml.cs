using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Fullscreen 16-point gaze calibration (4×4 grid), PORTED from
    /// ConditioningControlPanel/Windows/WebcamCalibrationWindow.xaml.cs over Platform/WebcamTracker
    /// (OnRawIris / OnHeadPose / OnBlink / StateChanged / SetCalibrationLive / ApplyCalibration). The
    /// fit is Core WebcamCalibrationFit, which the WPF window calls too; the result is saved to the
    /// same profile file in the same format (WebcamCalibrationData.Save).
    ///
    /// Same timing, retries, messages and gesture checks as WPF. Deviations: Escape, a tracker stop
    /// (panic, revoke) or the X before the save restores the previously live calibration (WPF leaves the
    /// unsaved candidate live); the mouth prompts always time out (no MAR detector yet); the redo prompt
    /// is ConfirmAsync (Yes / Cancel); Close(bool) for DialogResult; Screens.ScreenFromWindow for the monitor.
    /// ponytail: the verify cursor and the bubble test need GazeDebugCursorService (a WPF overlay
    /// window), not ported: Verify runs its countdown only and the bubble test only places a bubble.
    /// The ring pulse is a DispatcherTimer: Avalonia's Animation cannot target a Transform.
    /// </summary>
    public partial class WebcamCalibrationWindow : Window
    {
        private const int ReadyMs = 600, SampleMs = 1100, SampleCeilingMs = 4000, SampleSliceMs = 100;
        private const int SettleMs = 200, RetryReadyMs = 900, MaxAttemptsPerPoint = 2, RingFullSampleTarget = 20;
        private const int MinSamplesPerPoint = WebcamCalibrationFit.MinSamplesPerPoint;

        /// <summary>Clock for every wait of the flow; tests step a manual one instead of sleeping.</summary>
        internal static TimeProvider Time = TimeProvider.System;
        private static Task Delay(int ms) => Task.Delay(TimeSpan.FromMilliseconds(ms), Time);
        private static WebcamTracker Tracker => WebcamTracker.Instance;

        private readonly Canvas _dotCanvas;
        private readonly Ellipse _dot;
        private readonly Ellipse _dotRingBg;
        private readonly Ellipse _dotRingFg;
        private readonly ScaleTransform _dotRingScale;
        private readonly Border _shortcutHintBanner;
        private readonly StackPanel _statusPanel;
        private readonly TextBlock _txtTitle;
        private readonly TextBlock _txtStatus;
        private readonly TextBlock _txtProgress;
        private readonly Border _introPanel;
        private readonly Grid _validationPanel;
        private readonly TextBlock _txtValidationCue;
        private readonly TextBlock _txtValidationPrompt;
        private readonly TextBlock _txtValidationDetail;
        private readonly TextBlock _txtValidationAttempt;
        private readonly Grid _bubbleTestPanel;
        private readonly Ellipse _testBubble;
        private readonly Ellipse _testBubbleRingBg;
        private readonly Ellipse _testBubbleRingFg;
        private readonly Border _errorPanel;
        private readonly TextBlock _txtErrorDetail;
        private readonly Border _verifyPanel;
        private readonly TextBlock _txtVerifyStatus;
        private readonly Button _btnVerifyAccuracy;

        private DispatcherTimer? _ringPulse;
        private DispatcherTimer? _verifyCountdownTimer;
        private int _verifyCountdownSecondsLeft;
        private bool _completedOk;

        // Per-dot iris samples tagged with the head pose at that frame, and the session's poses.
        private readonly List<List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>> _allSamples = new();
        private readonly List<(double Yaw, double Pitch)> _allPoseSamples = new();
        private (double Yaw, double Pitch)? _lastPose;
        private bool _collecting, _cancelled, _ringIsFull, _subscribed, _saved;
        internal int ActiveDotIndex { get; private set; } = -1;
        internal (string Label, Point Screen)[] Positions { get; private set; } = Array.Empty<(string, Point)>();
        private readonly TaskCompletionSource<bool> _introDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebcamCalibrationData? _before, _candidate;

        /// <summary>True while a calibration window is on screen (the 6-blink gesture checks it).</summary>
        public static bool IsShowing { get; private set; }

        /// <summary>The user asked to redo (verify panel, or Yes on the inaccurate-fit prompt);
        /// <see cref="ShowDialogWithRecalibrate"/> re-opens while this is true.</summary>
        public bool WantsRecalibrate { get; private set; }

        public WebcamCalibrationWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _dotCanvas = this.FindControl<Canvas>("DotCanvas")!;
            _dot = this.FindControl<Ellipse>("Dot")!;
            _dotRingBg = this.FindControl<Ellipse>("DotRingBg")!;
            _dotRingFg = this.FindControl<Ellipse>("DotRingFg")!;
            _shortcutHintBanner = this.FindControl<Border>("ShortcutHintBanner")!;
            _statusPanel = this.FindControl<StackPanel>("StatusPanel")!;
            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtProgress = this.FindControl<TextBlock>("TxtProgress")!;
            _introPanel = this.FindControl<Border>("IntroPanel")!;
            _validationPanel = this.FindControl<Grid>("ValidationPanel")!;
            _txtValidationCue = this.FindControl<TextBlock>("TxtValidationCue")!;
            _txtValidationPrompt = this.FindControl<TextBlock>("TxtValidationPrompt")!;
            _txtValidationDetail = this.FindControl<TextBlock>("TxtValidationDetail")!;
            _txtValidationAttempt = this.FindControl<TextBlock>("TxtValidationAttempt")!;
            _bubbleTestPanel = this.FindControl<Grid>("BubbleTestPanel")!;
            _testBubble = this.FindControl<Ellipse>("TestBubble")!;
            _testBubbleRingBg = this.FindControl<Ellipse>("TestBubbleRingBg")!;
            _testBubbleRingFg = this.FindControl<Ellipse>("TestBubbleRingFg")!;
            _errorPanel = this.FindControl<Border>("ErrorPanel")!;
            _txtErrorDetail = this.FindControl<TextBlock>("TxtErrorDetail")!;
            _verifyPanel = this.FindControl<Border>("VerifyPanel")!;
            _txtVerifyStatus = this.FindControl<TextBlock>("TxtVerifyStatus")!;
            _btnVerifyAccuracy = this.FindControl<Button>("BtnVerifyAccuracy")!;

            // FindControl is constrained to Control, so the named ScaleTransform is reached through
            // its owner's RenderTransform (authored in this file, so Single() is deterministic).
            _dotRingScale = ((TransformGroup)_dotRingFg.RenderTransform!).Children
                .OfType<ScaleTransform>().Single();

            this.FindControl<Button>("BtnCalibrationHelp")!.Click += (_, _) => BtnCalibrationHelp_Click();
            this.FindControl<Button>("BtnIntroContinue")!.Click += (_, _) => _introDone.TrySetResult(true);
            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close(_completedOk);
            _btnVerifyAccuracy.Click += (_, _) => BtnVerifyAccuracy_Click();
            this.FindControl<Button>("BtnVerifyBubbleTest")!.Click += (_, _) => BtnVerifyBubbleTest_Click();
            this.FindControl<Button>("BtnVerifyRecalibrate")!.Click += (_, _) => BtnVerifyRecalibrate_Click();
            this.FindControl<Button>("BtnVerifyDone")!.Click += (_, _) => BtnVerifyDone_Click();
            KeyDown += Window_KeyDown;
            Loaded += Window_Loaded;
            Closed += Window_Closed;

            IsShowing = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private async void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            if (!Tracker.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before calibrating.");
                return;
            }
            _before = Tracker.Calibration;
            Tracker.OnRawIris += OnRawIris;
            Tracker.OnHeadPose += OnHeadPose;
            // Auto-close if tracking ends mid-flow (panic, consent revoked, camera gone).
            Tracker.StateChanged += OnTrackerStateChanged;
            _subscribed = true;

            // Intro first; the dot grid and the blink hint banner wait for Continue (ESC cancels).
            _dotCanvas.IsVisible = false;
            _statusPanel.IsVisible = false;
            _introPanel.IsVisible = true;
            _shortcutHintBanner.IsVisible = true;

            if (!await _introDone.Task || _cancelled) return;

            _introPanel.IsVisible = false;
            _shortcutHintBanner.IsVisible = false;
            _dotCanvas.IsVisible = true;
            _statusPanel.IsVisible = true;
            try { await RunSequenceAsync(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebcamCalibrationWindow: calibration sequence threw");
                ShowError("Calibration failed unexpectedly. See logs/app.log for details.");
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            IsShowing = false;
            _cancelled = true;
            _collecting = false;
            _introDone.TrySetResult(false);
            StopRingPulse();
            _verifyCountdownTimer?.Stop();
            if (!_subscribed) return;
            Tracker.OnRawIris -= OnRawIris;
            Tracker.OnHeadPose -= OnHeadPose;
            Tracker.StateChanged -= OnTrackerStateChanged;
            // Nothing partial survives a cancel - but only undo our own candidate: a revoke (or any
            // other change) made while the window was open stands.
            if (!_saved && ReferenceEquals(Tracker.Calibration, _candidate)) Tracker.SetCalibrationLive(_before);
        }

        private void OnTrackerStateChanged()
        {
            if (Tracker.IsRunning) return;
            _cancelled = true;
            Close(false);
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            _cancelled = true;
            _collecting = false;
            Close(false);
        }

        internal void OnRawIris(double dx, double dy)
        {
            if (!_collecting || ActiveDotIndex < 0 || ActiveDotIndex >= _allSamples.Count) return;
            var pose = _lastPose;
            var list = _allSamples[ActiveDotIndex];
            list.Add((dx, dy, pose?.Yaw ?? 0, pose?.Pitch ?? 0, pose.HasValue));
            UpdateProgressRing(Math.Min(1.0, list.Count / (double)RingFullSampleTarget));
            if (!_ringIsFull && list.Count >= RingFullSampleTarget) RingFull();
        }

        internal void OnHeadPose(double yaw, double pitch)
        {
            _lastPose = (yaw, pitch);
            if (_collecting) _allPoseSamples.Add((yaw, pitch));
        }

        private void RingFull()
        {
            _ringIsFull = true;
            StartRingPulse();
            WebcamQuickRecalWindow.Play("bubbles/Pop.mp3", 0.35f);   // CalibrationSoundService.RingFull
        }

        private void BtnCalibrationHelp_Click()
        {
            // topmost: true so the popup layers above this full-screen calibration window. The clip
            // cannot play on this head, so the popup takes its fail-soft text branch.
            HelpVideoWindow.Show(Services.HelpContentService.GetContent("WebcamCalibration"), this, topmost: true);
        }

        private async Task RunSequenceAsync()
        {
            var positions = WebcamCalibrationFit.BuildGrid(Bounds.Width, Bounds.Height)
                .Select(p => (p.Label, Screen: new Point(p.Screen.X, p.Screen.Y))).ToArray();
            Positions = positions;
            foreach (var _ in positions) _allSamples.Add(new());

            for (int i = 0; i < positions.Length; i++)
            {
                if (_cancelled) return;
                MoveDotTo(positions[i].Screen);
                _txtProgress.Text = $"Point {i + 1} / {positions.Length}  ({positions[i].Label})";

                bool succeeded = false;
                for (int attempt = 1; attempt <= MaxAttemptsPerPoint && !succeeded; attempt++)
                {
                    if (_cancelled) return;
                    StopRingPulse();
                    ResetProgressRing();
                    _allSamples[i].Clear();
                    _ringIsFull = false;
                    ActiveDotIndex = i;

                    _txtStatus.Text = attempt == 1
                        ? "Look at the pink dot…"
                        : "Missed that one — let's try again. Look at the pink dot…";
                    await Delay(attempt == 1 ? ReadyMs : RetryReadyMs);
                    if (_cancelled) return;

                    _txtStatus.Text = "Hold steady — sampling…";
                    WebcamQuickRecalWindow.Play("lvup.mp3", 0.25f);   // CalibrationSoundService.DotSampleStart
                    _collecting = true;
                    await Delay(SampleMs);
                    // Slow camera (#909): stretch the window until the count is met, up to the ceiling.
                    long stretchFrom = Time.GetTimestamp();
                    while (!_cancelled && _allSamples[i].Count < MinSamplesPerPoint
                           && Time.GetElapsedTime(stretchFrom).TotalMilliseconds < SampleCeilingMs - SampleMs)
                        await Delay(SampleSliceMs);
                    _collecting = false;
                    if (_cancelled) return;

                    if (_allSamples[i].Count >= MinSamplesPerPoint)
                    {
                        succeeded = true;
                        UpdateProgressRing(1.0);   // an accepted dot always shows a full ring
                        if (!_ringIsFull) RingFull();
                    }
                }
                ActiveDotIndex = -1;

                if (!succeeded)
                {
                    ShowError(
                        $"Couldn't sample point {i + 1} ({positions[i].Label}) after " +
                        $"{MaxAttemptsPerPoint} tries. " +
                        $"Got {_allSamples[i].Count} samples (need at least {MinSamplesPerPoint}). " +
                        "Make sure you're well-lit, facing the camera, and your face fits in frame.");
                    return;
                }
                StopRingPulse();
                await Delay(SettleMs);
            }
            if (_cancelled) return;
            WebcamQuickRecalWindow.Play("chime2.mp3", 0.5f);   // CalibrationSoundService.AllDotsCollected
            await FinalizeCalibrationAsync(positions);
        }

        private async Task FinalizeCalibrationAsync((string Label, Point Screen)[] positions)
        {
            var fit = WebcamCalibrationFit.Fit(_allSamples, _allPoseSamples,
                positions.Select(p => new OpenCvSharp.Point2d(p.Screen.X, p.Screen.Y)).ToArray(), Bounds.Width, Bounds.Height);
            if (fit.Data is not { } data)
            {
                ShowError("Couldn't fit calibration from your samples. The points may have been too similar — try again and make sure to look directly at each dot.");
                return;
            }
            if (fit.TooInaccurate)
            {
                Log.Warning("WebcamCalibration: fit residual too high — rms_x={Rx:F0}, rms_y={Ry:F0} DIPs; prompting redo", fit.RmsX, fit.RmsY);
                bool redo = await Dialogs.MessageDialog.ConfirmAsync(this, "Calibration inaccurate",
                    "This calibration came out very inaccurate — the dots didn't line up, so eye tracking would be unreliable.\n\n" +
                    "For a better result: good, even lighting; avoid glare on glasses (or try without them); keep your head still and look right at each dot.\n\n" +
                    "Try the calibration again?", okText: "Yes");
                if (_cancelled) return;
                if (redo) { WantsRecalibrate = true; Close(false); return; }
                Log.Information("WebcamCalibration: user kept low-quality calibration despite high residual");
            }

            // The monitor calibration ran on (WPF Screen.FromHandle), for calibrated-screen placement.
            if (Screens.ScreenFromWindow(this) is { } sc)
            {
                data.MonitorBounds!.DeviceName = sc.DisplayName;
                data.MonitorBounds.X = sc.Bounds.X;
                data.MonitorBounds.Y = sc.Bounds.Y;
            }
            data.MonitorBounds!.DpiScale = RenderScaling;

            _candidate = data;
            Tracker.SetCalibrationLive(data);   // in memory only until the gesture checks finish
            await RunValidationPhaseAsync();
            if (_cancelled) return;

            if (!Tracker.ApplyCalibration(data))
            {
                _validationPanel.IsVisible = false;
                ShowError("Couldn't save the calibration. See logs/app.log for details.");
                return;
            }
            _saved = true;
            CoreSettings.Current.WebcamCalibrated = true;
            CoreSettings.Current.WebcamCalibrationMode = "SixteenPoint";
            CoreSettings.Save();
            WebcamQuickRecalWindow.Play("result.mp3", 0.6f);   // CalibrationSoundService.CalibrationVerified
            ShowVerifyPanel();
        }

        private async Task RunValidationPhaseAsync()
        {
            _dotCanvas.IsVisible = false;
            _validationPanel.IsVisible = true;
            _txtTitle.Text = "Verifying calibration";
            _txtStatus.Text = "Follow the prompts to confirm the system can read your blinks and mouth.";
            _txtProgress.Text = "";
            _txtValidationCue.Text = "";
            _txtValidationPrompt.Text = "Get ready…";
            _txtValidationDetail.Text = "A couple of quick gesture checks and you're done.";
            _txtValidationAttempt.Text = "";
            await Delay(1400);
            if (_cancelled) return;

            await RunGestureCheckAsync("👁", "Blink a couple of times", 2, h => Tracker.OnBlink += h, h => Tracker.OnBlink -= h);
            if (_cancelled) return;
            await RunGestureCheckAsync("😮", "Open your mouth wide", 1, null, null);
            if (_cancelled) return;
            _txtValidationDetail.Text = "Good — close, and once more in a moment…";
            await Delay(1000);
            if (_cancelled) return;
            await RunGestureCheckAsync("😮", "Open your mouth wide again", 1, null, null);
        }

        /// <summary>WPF RunGestureCheckAsync + WaitFor*Async: up to 5 s for <paramref name="needed"/>
        /// events, then advance either way. A null subscribe (no detector) just times out.</summary>
        private async Task RunGestureCheckAsync(string cue, string prompt, int needed, Action<Action>? add, Action<Action>? remove)
        {
            const int TimeoutMs = 5000;
            _txtValidationCue.Text = cue;
            _txtValidationPrompt.Text = prompt;
            _txtValidationDetail.Text = $"Detected: 0 / {needed}";
            _txtValidationAttempt.Text = "";

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;
            void Handler()
            {
                count++;
                _txtValidationDetail.Text = $"Detected: {count} / {needed}";
                if (count >= needed) tcs.TrySetResult(true);
            }
            add?.Invoke(Handler);
            bool got;
            try { got = await Task.WhenAny(tcs.Task, Delay(TimeoutMs)) == tcs.Task; }
            finally { remove?.Invoke(Handler); }
            if (_cancelled) return;

            if (got)
            {
                WebcamQuickRecalWindow.Play("chime1.mp3", 0.45f);   // CalibrationSoundService.ValidationStepPass
                var prevCue = _txtValidationCue.Text;
                var prevColor = _txtValidationCue.Foreground;
                _txtValidationCue.Text = "✓";
                _txtValidationCue.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xE0, 0x80));
                _txtValidationDetail.Text = "Detected.";
                await Delay(700);
                _txtValidationCue.Text = prevCue;
                _txtValidationCue.Foreground = prevColor;
            }
            else
            {
                _txtValidationDetail.Text = "No worries — moving on.";
                await Delay(700);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Verify panel
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Shows the dialog, re-opening while the user asks to recalibrate; true when a
        /// calibration was accepted. Opens on the calibrated monitor (WPF ApplyCalibrationScreenPlacement).</summary>
        public static async Task<bool?> ShowDialogWithRecalibrate(Window owner)
        {
            bool? final;
            while (true)
            {
                var dlg = new WebcamCalibrationWindow();
                PlaceOnCalibratedScreen(dlg);
                final = await dlg.ShowDialogSafe<bool?>(owner);
                if (!dlg.WantsRecalibrate) break;
            }
            return final;
        }

        /// <summary>WPF App.ApplyCalibrationScreenPlacement: start on the monitor the last calibration
        /// ran on (matched by pixel origin) so Maximized lands there; unknown monitor: leave it.</summary>
        private static void PlaceOnCalibratedScreen(Window window)
        {
            if (Tracker.Calibration?.MonitorBounds is not { } mb || window.Screens is not { } screens) return;
            if (screens.All.FirstOrDefault(s => s.Bounds.X == mb.X && s.Bounds.Y == mb.Y) is not { } sc) return;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = sc.Bounds.Position;
        }

        private void BtnVerifyAccuracy_Click()
        {
            _verifyCountdownSecondsLeft = 15;
            UpdateVerifyCountdownUi();
            if (_verifyCountdownTimer == null)
            {
                _verifyCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _verifyCountdownTimer.Tick += (_, _) =>
                {
                    _verifyCountdownSecondsLeft--;
                    if (_verifyCountdownSecondsLeft <= 0) StopVerifyCountdown();
                    else UpdateVerifyCountdownUi();
                };
            }
            _verifyCountdownTimer.Stop();
            _verifyCountdownTimer.Start();
            _btnVerifyAccuracy.IsEnabled = false;
        }

        private void UpdateVerifyCountdownUi() =>
            _txtVerifyStatus.Text = $"Move your eyes around — the pink dot should track them. {_verifyCountdownSecondsLeft}s left.";

        private void StopVerifyCountdown()
        {
            _verifyCountdownTimer?.Stop();
            _btnVerifyAccuracy.IsEnabled = true;
            _txtVerifyStatus.Text = "Click Verify to preview accuracy with a live gaze cursor, or close when ready.";
        }

        private void BtnVerifyBubbleTest_Click()
        {
            StopVerifyCountdown();
            _verifyPanel.IsVisible = false;
            _shortcutHintBanner.IsVisible = false;
            _bubbleTestPanel.IsVisible = true;
            MoveBubbleTo(new Point(Bounds.Width / 2, Bounds.Height / 2));
            UpdateRing(_testBubbleRingFg, 0.0);
        }

        private void BtnVerifyRecalibrate_Click()
        {
            StopVerifyCountdown();
            WantsRecalibrate = true;
            Close(false);
        }

        private void BtnVerifyDone_Click()
        {
            StopVerifyCountdown();
            Close(true);
        }

        private void ShowVerifyPanel()
        {
            _validationPanel.IsVisible = false;
            _dotCanvas.IsVisible = false;
            _statusPanel.IsVisible = false;
            _verifyPanel.IsVisible = true;
            _shortcutHintBanner.IsVisible = true;
            _completedOk = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Bubble + dot placement
        // ─────────────────────────────────────────────────────────────────────

        private void MoveBubbleTo(Point p)
        {
            Canvas.SetLeft(_testBubble, p.X - _testBubble.Width / 2);
            Canvas.SetTop(_testBubble, p.Y - _testBubble.Height / 2);
            Canvas.SetLeft(_testBubbleRingBg, p.X - _testBubbleRingBg.Width / 2);
            Canvas.SetTop(_testBubbleRingBg, p.Y - _testBubbleRingBg.Height / 2);
            Canvas.SetLeft(_testBubbleRingFg, p.X - _testBubbleRingFg.Width / 2);
            Canvas.SetTop(_testBubbleRingFg, p.Y - _testBubbleRingFg.Height / 2);
            _testBubble.IsVisible = true;
            _testBubbleRingBg.IsVisible = true;
            _testBubbleRingFg.IsVisible = true;
        }

        private void HideBubble()
        {
            _testBubble.IsVisible = false;
            _testBubbleRingBg.IsVisible = false;
            _testBubbleRingFg.IsVisible = false;
        }

        private void MoveDotTo(Point screenPoint)
        {
            Canvas.SetLeft(_dot, screenPoint.X - _dot.Width / 2);
            Canvas.SetTop(_dot, screenPoint.Y - _dot.Height / 2);
            Canvas.SetLeft(_dotRingBg, screenPoint.X - _dotRingBg.Width / 2);
            Canvas.SetTop(_dotRingBg, screenPoint.Y - _dotRingBg.Height / 2);
            Canvas.SetLeft(_dotRingFg, screenPoint.X - _dotRingFg.Width / 2);
            Canvas.SetTop(_dotRingFg, screenPoint.Y - _dotRingFg.Height / 2);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Per-dot progress ring
        // ─────────────────────────────────────────────────────────────────────

        // Update the foreground ring's stroke-dash to display the given fraction of the perimeter
        // as filled (0..1). StrokeDashArray is expressed in *stroke-thickness multiples* on
        // Avalonia exactly as on WPF, so we divide the pixel perimeter by the stroke thickness to
        // get the right unit.
        private void UpdateProgressRing(double progress) => UpdateRing(_dotRingFg, progress);

        private static void UpdateRing(Ellipse ring, double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            // The bubble-test ring is an ELLIPSE (taller than wide, matching its target), so the
            // perimeter can't be 2*pi*r. Ramanujan's second approximation is exact when a == b —
            // the circular calibration dot ring keeps its previous numbers to the last decimal —
            // and errs by well under 1e-6 relative at our aspect ratio.
            double a = (ring.Width - ring.StrokeThickness) / 2.0;
            double b = (ring.Height - ring.StrokeThickness) / 2.0;
            double t = (a - b) * (a - b) / Math.Max(1e-9, (a + b) * (a + b));
            double perimeter = Math.PI * (a + b)
                * (1.0 + 3.0 * t / (10.0 + Math.Sqrt(Math.Max(0.0, 4.0 - 3.0 * t))));
            double units = perimeter / ring.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            ring.StrokeDashArray = new AvaloniaList<double> { visible, gap };
        }

        private void ResetProgressRing()
        {
            _dotRingFg.StrokeDashArray = new AvaloniaList<double> { 0.0, 10000.0 };
            _dotRingScale.ScaleX = 1.0;
            _dotRingScale.ScaleY = 1.0;
            _dotRingFg.Opacity = 1.0;
        }

        // WPF ran a Storyboard on DotRingScale: a 420ms SineEase DoubleAnimation 1.0 -> 1.18 with
        // RepeatBehavior.Forever + AutoReverse. Avalonia's Animation cannot drive a Transform -
        // TransformAnimator casts its target to Visual and throws InvalidCastException (found by
        // rendering, not by reading) - and the TransformOperations alternative would mean rewriting
        // the ring's TransformGroup in XAML. A timer driving the same sinusoid is the smaller and
        // exactly equivalent change: SineEase-in-out over 420ms plus auto-reverse IS one full
        // cosine period of 840ms between 1.0 and 1.18.
        private const double RingPulsePeriodMs = 840.0;
        private const double RingPulseAmplitude = 0.18;

        private void StartRingPulse()
        {
            StopRingPulse();
            long start = Environment.TickCount64;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                double phase = ((Environment.TickCount64 - start) % (long)RingPulsePeriodMs) / RingPulsePeriodMs;
                double scale = 1.0 + RingPulseAmplitude * (1.0 - Math.Cos(phase * 2.0 * Math.PI)) / 2.0;
                _dotRingScale.ScaleX = scale;
                _dotRingScale.ScaleY = scale;
            };
            timer.Start();
            _ringPulse = timer;
        }

        private void StopRingPulse()
        {
            _ringPulse?.Stop();
            _ringPulse = null;
            _dotRingScale.ScaleX = 1.0;
            _dotRingScale.ScaleY = 1.0;
        }

        private void ShowError(string detail)
        {
            StopRingPulse();
            _dotCanvas.IsVisible = false;
            _introPanel.IsVisible = false;
            _txtErrorDetail.Text = detail;
            _errorPanel.IsVisible = true;
        }
    }
}
