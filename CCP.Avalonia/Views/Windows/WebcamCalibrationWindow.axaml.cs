using System;
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

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Fullscreen 16-point gaze calibration (4×4 grid). The WPF original samples raw iris vectors
    /// at each point, fits a 3×3 homography and an over-determined 2nd-order polynomial
    /// (Cerrolaza asymmetric form, ridge-regularized with λ chosen by leave-one-out CV, iris →
    /// screen DIPs), and persists via WebcamCalibrationData.
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamCalibrationWindow.xaml.cs (2,116 lines).
    ///
    /// <para><b>No Win32 and no WebView2 here.</b> This view was scheduled in the Win32/WebView2
    /// wave, but reading it end to end there is exactly one interop site and no web view at all:
    /// <c>WindowInteropHelper(this).Handle</c> fed to <c>System.Windows.Forms.Screen.FromHandle</c>
    /// inside <c>FinalizeCalibrationAsync</c>, to record which monitor calibration ran on. That
    /// maps to <c>Screens.ScreenFromWindow(this)</c> plus <c>screen.Bounds</c> and
    /// <c>RenderScaling</c> (which also replaces <c>VisualTreeHelper.GetDpi</c>) — but the whole
    /// method is service-bound and stubbed below, so the mapping is recorded rather than written.
    /// Nothing here needs <c>X11Overlay</c>: the window is opaque black, focusable and clickable,
    /// so <c>Topmost</c> + <c>ShowInTaskbar="False"</c> cover it.</para>
    ///
    /// <para><b>What is real in this port</b> — everything that only touches the view: the panel
    /// choreography, the 4×4 grid layout, dot and bubble placement, the Ramanujan stroke-dash
    /// progress rings, the ring pulse, the error panel and the verify-panel countdown.</para>
    ///
    /// <para><b>What is stubbed</b> — everything reaching a service or the camera:
    /// <c>App.Webcam</c> (OnRawIris / OnHeadPose / OnTrackingStateChanged / SetCalibrationLive /
    /// ApplyCalibration / ClearGazeAttractor), the entire fit pipeline (TrimmedMean,
    /// FitCerrolazaPolynomial, FitRidge, EvalPolynomial, BuildAxisCorrection, FitAxisTrim,
    /// ~1,300 lines of OpenCvSharp-free maths that still needs <c>WebcamCalibrationData</c> from
    /// the WPF head), the gesture warm-up waiters, <c>RunBubbleTestAsync</c>,
    /// <c>CalibrationSoundService</c>, <c>App.GazeCursor</c>, <c>App.Settings</c>,
    /// <c>App.Notifications</c>, <c>App.Logger</c> and the HelpContentService/HelpVideoWindow
    /// popup.</para>
    ///
    /// Other deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>, as in TextEditorDialog.
    ///  - <c>ShowDialogWithRecalibrate</c> becomes async: Avalonia's <c>ShowDialog</c> is awaitable
    ///    and has no synchronous form.
    ///  - The ring-pulse <c>Storyboard</c> becomes a <c>DispatcherTimer</c> driving the same
    ///    sinusoid. Avalonia's <c>Animation</c> CANNOT target a <c>Transform</c> — its
    ///    TransformAnimator casts the target to <c>Visual</c> and throws at run time, which the
    ///    compiler says nothing about. See the comment on StartRingPulse.
    ///  - <c>ActualWidth/Height</c> become <c>Bounds.Width/Height</c>.
    ///  - The named <c>ScaleTransform</c>s are reached through <c>RenderTransform</c> rather than
    ///    <c>FindControl</c>, which is constrained to <c>Control</c>.
    ///  - The pipeline's timing constants (SampleMs, SettleMs, MinSamplesPerPoint, …) are not
    ///    copied: they belong with the sampling loop, and it is a stub. Only the two the layout
    ///    actually uses are here.
    /// </summary>
    public partial class WebcamCalibrationWindow : Window
    {
        private const int GridSize = 4;       // 4×4 = 16 calibration points (corners + interior)
        private const double EdgeMargin = 40; // distance from screen edge for corner dots (DIPs)

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

        /// <summary>
        /// True while a calibration window is on screen. The global 6-blink recalibrate gesture
        /// (MainWindow) checks this so blinking during the verify step — or while calibration is
        /// already open — can't re-trigger another calibration.
        /// </summary>
        public static bool IsShowing { get; private set; }

        /// <summary>
        /// Set to true when the user clicked Recalibrate on the verify panel. Callers that want to
        /// loop should re-open the dialog while this is true. Use
        /// <see cref="ShowDialogWithRecalibrate"/> for the canonical loop pattern.
        /// </summary>
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

            // FindControl is constrained to Control, so the two named ScaleTransforms are reached
            // through their owner's RenderTransform instead. Deterministic: the shapes and their
            // transforms are both authored in this file.
            // Throwing rather than falling back to a detached ScaleTransform: a silent fallback
            // would pulse an object nobody renders, and a still ring is exactly what a broken
            // pulse looks like anyway.
            _dotRingScale = ((TransformGroup)_dotRingFg.RenderTransform!).Children
                .OfType<ScaleTransform>().Single();

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Button>("BtnCalibrationHelp")!.Click += (_, _) => BtnCalibrationHelp_Click();
            this.FindControl<Button>("BtnIntroContinue")!.Click += (_, _) => BtnIntroContinue_Click();
            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => BtnErrorClose_Click();
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

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Webcam (IsRunning gate + OnRawIris / OnHeadPose /
            // OnTrackingStateChanged subscriptions), wired when the webcam service moves to Core.
            // The WPF original bails to ShowError when tracking is not running; with no service to
            // ask, the intro is shown unconditionally so the view still has a first frame.

            // Show the intro overlay first so users know what's coming — the dot grid + validation
            // checks are otherwise a surprise. DotCanvas / StatusPanel stay hidden until the user
            // clicks Continue (or presses ESC, which cancels).
            _dotCanvas.IsVisible = false;
            _statusPanel.IsVisible = false;
            _introPanel.IsVisible = true;
            // Surface the blink-shortcut hint while the user is reading the intro (and again on
            // the verify panel) — but not during the dot grid, where it would sit over the top-row
            // dots.
            _shortcutHintBanner.IsVisible = true;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            IsShowing = false;
            StopRingPulse();
            _verifyCountdownTimer?.Stop();
            // ponytail: needs App.Webcam / App.GazeCursor to unsubscribe the iris + pose streams
            // and release the "calibration-verify" and "calibration-bubbletest" cursor keys and the
            // gaze attractor, wired when those services move to Core.
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                StopRingPulse();
                Close(false);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Intro / error panels
        // ─────────────────────────────────────────────────────────────────────

        private void BtnIntroContinue_Click()
        {
            _introPanel.IsVisible = false;
            _shortcutHintBanner.IsVisible = false;
            _dotCanvas.IsVisible = true;
            _statusPanel.IsVisible = true;

            // ponytail: needs App.Webcam's iris/pose streams + CalibrationSoundService for the
            // real per-dot sampling loop (RunSequenceAsync), wired when they move to Core. The
            // grid layout, the dot placement and the progress ring below are the WPF original's,
            // parked on the first point so the view still shows what the sequence looks like.
            var positions = BuildGrid(Bounds.Width, Bounds.Height);
            MoveDotTo(positions[0].Screen);
            _txtProgress.Text = $"Point 1 / {positions.Length}  ({positions[0].Label})";
            _txtStatus.Text = "Look at the pink dot…";
            ResetProgressRing();
        }

        /// <summary>
        /// The 4×4 dot layout from RunSequenceAsync, verbatim. Row-major, 16 dots evenly spaced
        /// across cols/rows 0..3 of the usable span:
        /// <code>
        ///    0  1  2  3      (top row)
        ///    4  5  6  7
        ///    8  9 10 11
        ///   12 13 14 15      (bottom row)
        /// </code>
        /// Left column = {0,4,8,12}; right column = {3,7,11,15}.
        /// </summary>
        private static (string Label, Point Screen)[] BuildGrid(double w, double h)
        {
            double xL = EdgeMargin, xR = w - EdgeMargin;
            double yT = EdgeMargin, yB = h - EdgeMargin;
            string[] rowLabels = { "Top", "Upper", "Lower", "Bottom" };
            string[] colLabels = { "left", "mid-left", "mid-right", "right" };
            var positions = new (string Label, Point Screen)[GridSize * GridSize];
            for (int r = 0; r < GridSize; r++)
            {
                double y = yT + (yB - yT) * (r / (double)(GridSize - 1));
                for (int c = 0; c < GridSize; c++)
                {
                    double x = xL + (xR - xL) * (c / (double)(GridSize - 1));
                    positions[r * GridSize + c] = ($"{rowLabels[r]}-{colLabels[c]}", new Point(x, y));
                }
            }
            return positions;
        }

        private void BtnErrorClose_Click() => Close(_completedOk);

        private void BtnCalibrationHelp_Click()
        {
            // ponytail: needs Services.HelpContentService.GetContent("WebcamCalibration") and the
            // HelpVideoWindow popup (topmost:true so it layers above this fullscreen window),
            // wired when the help service moves to Core.
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Verify panel
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Helper for callers: shows the dialog, re-opens automatically when the user clicks
        /// Recalibrate on the verify panel. Returns the terminal result — true when calibration was
        /// accepted, false when cancelled. Async because Avalonia's ShowDialog has no synchronous
        /// form; the WPF original returned <c>bool?</c> directly.
        /// </summary>
        public static async Task<bool?> ShowDialogWithRecalibrate(Window owner)
        {
            bool? final;
            while (true)
            {
                var dlg = new WebcamCalibrationWindow();
                // ponytail: needs App.ApplyCalibrationScreenPlacement to pick the monitor to open
                // on (Screens.ScreenFromWindow(owner) / screen.Bounds on this head), wired when
                // that helper moves to Core.
                final = await dlg.ShowDialog<bool?>(owner);
                if (!dlg.WantsRecalibrate) break;
            }
            return final;
        }

        private void BtnVerifyAccuracy_Click()
        {
            // ponytail: needs App.GazeCursor.Show/Hide("calibration-verify") to actually draw the
            // live gaze cursor, wired when the cursor service moves to Core. The 15s countdown is
            // view-only and runs for real.
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
            // ponytail: needs App.Webcam's gaze stream + SetGazeAttractor and App.GazeCursor for
            // RunBubbleTestAsync (dwell detection, residual capture, the FitAxisTrim fine-tune),
            // wired when they move to Core. Placing the first bubble and its rings uses the real
            // view code so the panel is not empty.
            var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
            MoveBubbleTo(centre);
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

        /// <summary>
        /// The tail of FinalizeCalibrationAsync: swap the dot UI for the verify panel. Reached
        /// from the stubbed pipeline today; kept because it is pure view choreography.
        /// </summary>
        private void ShowVerifyPanel()
        {
            _validationPanel.IsVisible = false;
            _dotCanvas.IsVisible = false;
            _statusPanel.IsVisible = false;
            _verifyPanel.IsVisible = true;
            _shortcutHintBanner.IsVisible = true;
            _completedOk = true;
        }

        /// <summary>
        /// The prompt half of RunValidationPhaseAsync / RunGestureCheckAsync. The detection half
        /// (WaitForBlinksAsync / WaitForMouthOpensAsync / WaitForTongueOutsAsync) is a stub.
        /// </summary>
        private void ShowValidationPrompt(string cue, string prompt, string detail, string attempt = "")
        {
            _dotCanvas.IsVisible = false;
            _validationPanel.IsVisible = true;
            _txtTitle.Text = "Verifying calibration";
            _txtStatus.Text = "Follow the prompts to confirm the system can read your blinks and mouth.";
            _txtProgress.Text = "";
            _txtValidationCue.Text = cue;
            _txtValidationPrompt.Text = prompt;
            _txtValidationDetail.Text = detail;
            _txtValidationAttempt.Text = attempt;
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
