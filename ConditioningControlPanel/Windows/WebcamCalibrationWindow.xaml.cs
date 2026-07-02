using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Fullscreen 16-point gaze calibration (4×4 grid). Samples raw iris vectors
    /// at each point, fits a 3×3 homography and an over-determined 2nd-order
    /// polynomial (Cerrolaza asymmetric form, ridge-regularized with λ chosen
    /// by leave-one-out CV, iris → screen DIPs), and persists via
    /// WebcamCalibrationData.
    ///
    /// Grid sizing: 4×4 with margin=40 keeps the calibration short while the
    /// ridge-LOO polynomial fit (plus I-DT saccade-settle + MAD trim) handles
    /// corner reach. The pipeline replaced an earlier unregularized Tikhonov
    /// fit, mean+σ outlier rejection, head-pose comp, and edge-pull that caused
    /// jumpy cursor + drift.
    ///
    /// Caller is responsible for ensuring App.Webcam is already running.
    /// </summary>
    public partial class WebcamCalibrationWindow : System.Windows.Window
    {
        private const int ReadyMs = 600;          // dot moves, user re-fixates
        private const int SampleMs = 1100;        // ~33 samples at 30fps; well above MinSamplesPerPoint after the saccade-settle drop
        private const int SettleMs = 200;         // pause between dots
        private const int RetryReadyMs = 900;     // longer pause before retrying a missed dot
        // Minimum surviving iris samples to accept a dot. The SampleMs window yields
        // ~33 raw frames at 30fps, but the gaze-jitter pass (3-frame iris median warm-up
        // + head-pose validity gating) drops a chunk of those — and the first/top-left
        // dot is sampled before the eye-corner smoothing buffers warm up, so it routinely
        // landed at 16-17 against a floor of 20 and failed calibration outright (#382/#385).
        // 12 keeps a meaningful sample count while staying reliably reachable cold.
        private const int MinSamplesPerPoint = 12;
        private const int MaxAttemptsPerPoint = 2; // miss twice in a row → fail calibration
        private const int GridSize = 4;            // 4×4 = 16 calibration points (corners + interior)
        private const double EdgeMargin = 40;      // distance from screen edge for corner dots (DIPs); small enough that polynomial extrapolation to bezel is negligible

        // Sample target for the "ring is full" feedback. Was 33 (the raw
        // frame count of the SampleMs window) — but the tracking pipeline
        // legitimately drops frames (blink suppression, eye-consistency
        // gates, median warm-up), so a good dot typically delivers ~20-28
        // samples and the ring visibly never filled, which read as "it
        // didn't work" (bad UX, user report). 20 fills reliably on a good
        // dot while still animating across most of the window; the sampling
        // loop additionally snaps the ring to 100% whenever a dot SUCCEEDS,
        // so a filled ring and an accepted dot always agree.
        private const int RingFullSampleTarget = 20;

        // Per-dot iris samples paired with the head-pose at the same frame.
        // Pairing lets the finalize pass reject frames where the user's head
        // drifted off-center — those samples carry the right iris but the
        // wrong head-relative reference frame, so they bias the mean.
        private readonly List<List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>> _allSamples = new();
        // Head-pose samples accumulated across all 16 dots (single flat list,
        // not per-dot — we just want a "head still, looking forward" average
        // for the baseline). Sampled in lockstep with iris during _collecting.
        private readonly List<(double Yaw, double Pitch)> _allPoseSamples = new();
        // Most-recent head pose seen on the OnHeadPose stream, regardless of
        // whether we're currently in a sampling window. Read by OnRawIris so
        // each iris sample can be tagged with the head pose at its frame.
        private (double Yaw, double Pitch)? _lastPose;
        private bool _collecting;
        private bool _cancelled;
        private bool _completedOk;
        // RunContinuationsAsynchronously: without this, TrySetResult continues
        // the awaiting state machine inline on the dispatcher thread that
        // raised the event, which can re-enter the multicast delegate while
        // it's still being invoked (e.g. the unsubscribe in the finally of an
        // outer await firing during the same OnGazeSide invocation list).
        // Async continuations get posted instead of inlined — same semantics,
        // no re-entrancy.
        private readonly TaskCompletionSource<bool> _introDone =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Step-0 lighting picker: "left"/"right"/"top"/"front"/"back" or ""
        // (skipped). Feeds the shadow-side weight floor in the fit and is
        // stamped into the calibration for the runtime clamp asymmetry.
        private TaskCompletionSource<string?>? _lightDone;
        private string _lightChoice = "";
        private Storyboard? _ringPulse;
        private bool _ringIsFull;
        // Index of the dot currently being sampled. Read by OnRawIris so
        // samples land in the right per-dot bucket across retries (we can't
        // rely on "last list" because all 16 are allocated up-front).
        private int _activeDotIndex = -1;

        /// <summary>
        /// True while a calibration window is on screen. The global 6-blink
        /// recalibrate gesture (MainWindow) checks this so blinking during the
        /// verify step — or while calibration is already open — can't re-trigger
        /// another calibration.
        /// </summary>
        public static bool IsShowing { get; private set; }

        public WebcamCalibrationWindow()
        {
            InitializeComponent();
            IsShowing = true;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.Webcam == null || !App.Webcam.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before calibrating.");
                return;
            }

            App.Webcam.OnRawIris += OnRawIris;
            App.Webcam.OnHeadPose += OnHeadPose;
            // Auto-close if tracking ends mid-flow (consent revoked from another
            // window, panic key, camera unplugged, fatal error). Without this
            // the dialog sits alive with stale subscriptions waiting for events
            // that will never fire.
            App.Webcam.OnTrackingStateChanged += OnWebcamStateChanged;

            DotCanvas.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Collapsed;
            // Surface the blink-shortcut hint while the user is reading the
            // pre-calibration panels (and again on the verify panel) — but
            // not during the dot grid, where it would sit over the top-row
            // dots.
            ShortcutHintBanner.Visibility = Visibility.Visible;

            // Step 0 (skippable): lighting picker. Preselect the remembered
            // choice so returning users can just confirm with one click.
            HighlightLightTile(App.Settings?.Current?.WebcamLightSource ?? "");
            _lightDone = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            LightSourcePanel.Visibility = Visibility.Visible;
            var light = await _lightDone.Task;
            LightSourcePanel.Visibility = Visibility.Collapsed;
            if (_cancelled) return;
            _lightChoice = light ?? "";
            if (!string.IsNullOrEmpty(_lightChoice))
            {
                var s = App.Settings?.Current;
                if (s != null && s.WebcamLightSource != _lightChoice)
                {
                    s.WebcamLightSource = _lightChoice;
                    App.Settings?.Save();
                }
            }

            // Then the intro overlay so users know what's coming — the dot
            // grid + validation checks are otherwise a surprise. DotCanvas /
            // StatusPanel stay hidden until the user clicks Continue (or
            // presses ESC, which cancels).
            IntroPanel.Visibility = Visibility.Visible;

            var proceed = await _introDone.Task;
            if (!proceed || _cancelled) return;

            IntroPanel.Visibility = Visibility.Collapsed;
            ShortcutHintBanner.Visibility = Visibility.Collapsed;
            DotCanvas.Visibility = Visibility.Visible;
            StatusPanel.Visibility = Visibility.Visible;

            try
            {
                await RunSequenceAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: calibration sequence threw");
                ShowError("Calibration failed unexpectedly. See logs/app.log for details.");
            }
        }

        private void BtnIntroContinue_Click(object sender, RoutedEventArgs e)
        {
            _introDone.TrySetResult(true);
        }

        private void BtnLightPick_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;
            HighlightLightTile(tag);
            _lightDone?.TrySetResult(tag);
        }

        private void BtnLightSkip_Click(object sender, RoutedEventArgs e)
            => _lightDone?.TrySetResult("");

        /// <summary>Pink border on the tile matching the current choice, default chrome on the rest.</summary>
        private void HighlightLightTile(string choice)
        {
            var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            var dim = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x55));
            foreach (var btn in new[] { BtnLightLeft, BtnLightRight, BtnLightTop, BtnLightFront, BtnLightBack })
            {
                try { btn.BorderBrush = (btn.Tag as string) == choice ? pink : dim; }
                catch { }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            IsShowing = false;

            // Unblock the intro awaiter if the user closed via title-bar X
            // (or owner-cascade) instead of ESC / Continue. Window_Loaded is
            // async void and awaits _introDone — without this, the awaiter
            // sits orphaned and the calibration handlers stay subscribed
            // until the next GC pass. Same for the lighting picker.
            _introDone.TrySetResult(false);
            _lightDone?.TrySetResult(null);

            if (App.Webcam != null)
            {
                App.Webcam.OnRawIris -= OnRawIris;
                App.Webcam.OnHeadPose -= OnHeadPose;
                App.Webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
            }

            // Release any debug-cursor requests this window made. ESC closes
            // the window without running the button handlers, and a leaked
            // Show key keeps the gaze dot on screen forever after. Same for
            // the bubble-test attractor — leaking it would leave the live
            // cursor magnetized to a stale point.
            _verifyCountdownTimer?.Stop();
            App.GazeCursor?.Hide("calibration-verify");
            App.GazeCursor?.Hide(BubbleTestCursorKey);
            App.Webcam?.ClearGazeAttractor();
        }

        private void OnWebcamStateChanged(WebcamTrackingState state)
        {
            if (state == WebcamTrackingState.Stopped || state == WebcamTrackingState.Error
                || state == WebcamTrackingState.CameraInUse || state == WebcamTrackingState.CameraDenied)
            {
                _cancelled = true;
                _introDone.TrySetResult(false);
                if (DialogResult == null) DialogResult = false;
                Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _cancelled = true;
                _collecting = false;
                _introDone.TrySetResult(false);
                _lightDone?.TrySetResult(null);
                DialogResult = false;
                Close();
            }
        }

        private void BtnErrorClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = _completedOk;
            Close();
        }

        private void BtnCalibrationHelp_Click(object sender, RoutedEventArgs e)
        {
            // topmost: true so the popup layers above this full-screen calibration window.
            var content = Services.HelpContentService.GetContent("WebcamCalibration");
            HelpVideoWindow.Show(content, this, topmost: true);
        }

        private void OnRawIris(double dx, double dy)
        {
            var pose = _lastPose;

            if (_collecting && _activeDotIndex >= 0 && _activeDotIndex < _allSamples.Count)
            {
                var list = _allSamples[_activeDotIndex];
                list.Add((dx, dy, pose?.Yaw ?? 0, pose?.Pitch ?? 0, pose.HasValue));

                // Drive the per-dot progress ring: fill toward the target sample
                // count. When we cross the threshold, kick off the pulse so the
                // user has a clear "got it, hold a moment" cue.
                double progress = Math.Min(1.0, list.Count / (double)RingFullSampleTarget);
                UpdateProgressRing(progress);
                if (!_ringIsFull && list.Count >= RingFullSampleTarget)
                {
                    _ringIsFull = true;
                    StartRingPulse();
                    CalibrationSoundService.RingFull();
                }
            }
        }

        private void OnHeadPose(double yaw, double pitch)
        {
            // Always track the latest head pose so OnRawIris can pair the next
            // iris frame with it, even between sampling windows.
            _lastPose = (yaw, pitch);
            if (!_collecting) return;
            _allPoseSamples.Add((yaw, pitch));
        }

        private async Task RunSequenceAsync()
        {
            // Wait one frame for the window to fully lay out so ActualWidth/Height are valid.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            var w = ActualWidth;
            var h = ActualHeight;
            // 4×4 grid, row-major. 16 dots evenly spaced across cols/rows 0..3
            // of the usable span. Layout:
            //    0  1  2  3      (top row)
            //    4  5  6  7
            //    8  9 10 11
            //   12 13 14 15      (bottom row)
            // Left column = {0,4,8,12}; right column = {3,7,11,15};
            // top row = {0..3}; bottom row = {12..15}.
            double xL = EdgeMargin, xR = w - EdgeMargin;
            double yT = EdgeMargin, yB = h - EdgeMargin;
            string[] rowLabels = { "Top", "Upper", "Lower", "Bottom" };
            string[] colLabels = { "left", "mid-left", "mid-right", "right" };
            var positions = new (string Label, WpfPoint Screen)[GridSize * GridSize];
            for (int r = 0; r < GridSize; r++)
            {
                double y = yT + (yB - yT) * (r / (double)(GridSize - 1));
                for (int c = 0; c < GridSize; c++)
                {
                    double x = xL + (xR - xL) * (c / (double)(GridSize - 1));
                    positions[r * GridSize + c] = ($"{rowLabels[r]}-{colLabels[c]}", new WpfPoint(x, y));
                }
            }

            // Allocate per-dot sample lists up-front so the OnRawIris callback's
            // `_allSamples.Count - 1` indexing always points at the right slot
            // for the current dot, even across retries.
            for (int i = 0; i < positions.Length; i++)
            {
                _allSamples.Add(new List<(double, double, double, double, bool)>());
            }

            for (int i = 0; i < positions.Length; i++)
            {
                if (_cancelled) return;

                MoveDotTo(positions[i].Screen);
                TxtProgress.Text = $"Point {i + 1} / {positions.Length}  ({positions[i].Label})";

                bool succeeded = false;
                for (int attempt = 1; attempt <= MaxAttemptsPerPoint && !succeeded; attempt++)
                {
                    if (_cancelled) return;

                    // Reset state for this attempt: clear any stale samples
                    // from a prior failed attempt, reset the progress ring,
                    // stop any leftover pulse. _collecting is already false
                    // here, so any pending dispatched OnRawIris events from
                    // the prior window early-return without writing to the
                    // list we're about to clear.
                    StopRingPulse();
                    ResetProgressRing();
                    _allSamples[i].Clear();
                    _ringIsFull = false;
                    _activeDotIndex = i;

                    TxtStatus.Text = attempt == 1
                        ? "Look at the pink dot…"
                        : "Missed that one — let's try again. Look at the pink dot…";
                    int readyDelay = attempt == 1 ? ReadyMs : RetryReadyMs;
                    await Task.Delay(readyDelay);
                    if (_cancelled) return;

                    TxtStatus.Text = "Hold steady — sampling…";
                    CalibrationSoundService.DotSampleStart();
                    _collecting = true;
                    await Task.Delay(SampleMs);
                    _collecting = false;
                    if (_cancelled) return;

                    if (_allSamples[i].Count >= MinSamplesPerPoint)
                    {
                        succeeded = true;
                        // The dot is accepted — always show it as such. With
                        // frame gates upstream, the live count can land just
                        // under RingFullSampleTarget; a 90%-filled ring on a
                        // dot that actually succeeded reads as failure.
                        UpdateProgressRing(1.0);
                        if (!_ringIsFull)
                        {
                            _ringIsFull = true;
                            StartRingPulse();
                            CalibrationSoundService.RingFull();
                        }
                    }
                }
                _activeDotIndex = -1;

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
                await Task.Delay(SettleMs);
            }

            if (_cancelled) return;

            CalibrationSoundService.AllDotsCollected();

            // Guided head-motion step — trains the head-pose compensation
            // that keeps the cursor put when the user nods/turns slightly
            // during normal use. Skipped silently when no pose stream.
            await FinalizeCalibrationAsync(positions);
        }

        private async Task FinalizeCalibrationAsync((string Label, WpfPoint Screen)[] positions)
        {
            // Compute the session-wide head-pose mean + σ once, up-front, so we
            // can use it both as the baseline (BaselineHeadPose, written below)
            // and as the reference for rejecting iris samples whose head-pose
            // drifted off-center mid-dot. Tolerance is max(2σ, 3°) — tight when
            // the user was steady, looser when they were squirming, with a
            // floor for solvePnP measurement noise.
            double meanYaw = 0, meanPitch = 0;
            double sigmaYaw = 0, sigmaPitch = 0;
            bool havePoseRef = _allPoseSamples.Count >= MinSamplesPerPoint;
            if (havePoseRef)
            {
                foreach (var p in _allPoseSamples) { meanYaw += p.Yaw; meanPitch += p.Pitch; }
                meanYaw /= _allPoseSamples.Count;
                meanPitch /= _allPoseSamples.Count;
                double vy = 0, vp = 0;
                foreach (var p in _allPoseSamples)
                {
                    vy += (p.Yaw - meanYaw) * (p.Yaw - meanYaw);
                    vp += (p.Pitch - meanPitch) * (p.Pitch - meanPitch);
                }
                sigmaYaw = Math.Sqrt(vy / _allPoseSamples.Count);
                sigmaPitch = Math.Sqrt(vp / _allPoseSamples.Count);
            }
            const double PoseFloorRad = 0.052; // ~3°
            double tolYaw = Math.Max(2 * sigmaYaw, PoseFloorRad);
            double tolPitch = Math.Max(2 * sigmaPitch, PoseFloorRad);

            // Average each point's iris samples (trim wild outliers via simple
            // mean-then-mean-of-inliers — good enough for the prototype).
            var srcMeans = new Point2d[positions.Length];
            var dstPoints = new Point2d[positions.Length];
            // Surviving samples per dot — same set that contributed to srcMeans.
            // Reused below to fit the head-pose compensation coefficients on
            // residuals from these (rather than from the noisy unfiltered set).
            var survivors = new List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>[positions.Length];
            // Per-dot sample spread (iris-vector units) — drives the inverse-
            // spread fit weighting below so jittery dots pull the polynomial less.
            var dotSpreads = new double[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                // First pass: drop samples whose head pose was outside the
                // session-wide tolerance window. If we don't have a reliable
                // baseline (pose stream never produced enough), keep everything.
                List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> s;
                if (havePoseRef)
                {
                    s = new List<(double, double, double, double, bool)>(_allSamples[i].Count);
                    foreach (var p in _allSamples[i])
                    {
                        if (!p.HasPose) { s.Add(p); continue; }
                        if (Math.Abs(p.Yaw - meanYaw) <= tolYaw &&
                            Math.Abs(p.Pitch - meanPitch) <= tolPitch)
                        {
                            s.Add(p);
                        }
                    }
                    // If head-pose filter ate too many samples (user's "steady"
                    // pose for this dot was an outlier from the global mean —
                    // e.g. they tilted their head to look at corners), back
                    // off rather than starve the fit.
                    if (s.Count < MinSamplesPerPoint) s = new List<(double, double, double, double, bool)>(_allSamples[i]);
                }
                else
                {
                    s = new List<(double, double, double, double, bool)>(_allSamples[i]);
                }

                // Saccade-settle drop + median/MAD outlier rejection. Drops
                // the first ~210ms of samples (gaze is still saccading onto
                // and settling on the new dot — Salvucci & Goldberg I-DT
                // 2000), then trims at median ± 3·MAD/0.6745. Robust 3σ
                // envelope that rejects blinks, micro-saccades, and fixation
                // breaks without the center/spread estimates themselves being
                // inflated by those outliers — which the previous mean+1σ
                // two-pass was vulnerable to.
                var (mx, my, spread, kept) = RobustPerDotMean(s);
                srcMeans[i] = new Point2d(mx, my);
                dstPoints[i] = new Point2d(positions[i].Screen.X, positions[i].Screen.Y);
                survivors[i] = kept;
                dotSpreads[i] = spread;
            }

            // Inverse-spread weights, scaled by the median spread so the result
            // is invariant to the absolute iris-vector magnitude: a dot at the
            // median spread gets weight ~0.5/med², a rock-steady dot ~1/med²
            // (≈2×), a wildly-scattered dot trends toward 0. The squared form
            // makes the falloff sharp enough to matter without any single dot
            // dominating the fit.
            double medSpread = MedianOf(dotSpreads);
            double medSpreadSq = medSpread * medSpread;
            var dotWeights = new double[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                dotWeights[i] = 1.0 / (dotSpreads[i] * dotSpreads[i] + medSpreadSq + 1e-12);

            // Lighting compensation (step-0 picker): one-sided room light
            // inflates iris-sample spread on the SHADOW side of the grid, so
            // the inverse-spread weighting above would demote exactly the
            // dots that side needs and the fit comes out great on the lit
            // side, poor on the other ("with the window on my right I
            // struggled to reach the left side of the screen"). Floor the
            // shadow-side weights at the median weight so both halves pull
            // the polynomial equally. Light from the user's right shadows
            // their gaze range toward screen-LEFT (they reported the far
            // side from the window as the weak one), and overhead light
            // (brow shadow) hurts looking DOWN.
            if (_lightChoice is "left" or "right" or "top")
            {
                double medWeight = MedianOf(dotWeights);
                for (int i = 0; i < positions.Length; i++)
                {
                    int row = i / GridSize, col = i % GridSize;
                    bool shadow = _lightChoice switch
                    {
                        "right" => col < GridSize / 2,   // screen-left half
                        "left" => col >= GridSize - GridSize / 2, // screen-right half
                        "top" => row >= GridSize - GridSize / 2,  // bottom half
                        _ => false,
                    };
                    if (shadow && dotWeights[i] < medWeight) dotWeights[i] = medWeight;
                }
                App.Logger?.Information("WebcamCalibration: lighting comp active (source={Light})", _lightChoice);
            }

            // Absolute residual floor for outlier-dot rejection: a dot is only a
            // drop candidate if its fit residual exceeds BOTH the robust
            // statistical threshold AND this fraction of the screen, so a tight
            // calibration where every dot fits to within a few % never loses one.
            double outlierFloorDip = Math.Max(ActualWidth, ActualHeight) * 0.06;

            // Fit homography iris → screen.
            double[][]? homography = null;
            try
            {
                using var hMat = Cv2.FindHomography(srcMeans, dstPoints);
                if (!hMat.Empty() && hMat.Rows == 3 && hMat.Cols == 3)
                {
                    homography = new double[3][];
                    for (int r = 0; r < 3; r++)
                    {
                        homography[r] = new double[3];
                        for (int c = 0; c < 3; c++)
                            homography[r][c] = hMat.At<double>(r, c);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: FindHomography threw");
            }

            if (homography == null)
            {
                ShowError("Couldn't fit calibration from your samples. The points may have been too similar — try again and make sure to look directly at each dot.");
                return;
            }

            // 2nd-order polynomial fit, Cerrolaza et al. (2008, 2012)
            // asymmetric form — the empirical winner from a 400+ variant
            // sweep on 9-25 point grids:
            //   x = a0 + a1·ix + a2·iy + a3·ix·iy + a4·ix² + a5·iy² + a6·ix²·iy
            //   y = b0 + b1·ix + b2·iy + b3·ix·iy + b4·ix² + b5·iy² + b6·iy²·ix
            // The asymmetric high-order term (ix²·iy on X, iy²·ix on Y)
            // gives ~0.15-0.25° DVA over the symmetric 6-coefficient form
            // we used previously. Ridge λ is selected by leave-one-out CV
            // from log-spaced candidates {1e-5..1e-1} × trace(AᵀA)/p, which
            // trims 15-30% off corner error vs the previous fixed-λ fit
            // on webcam data (Hansen & Ji 2010, Zhang & Hornof 2014). 16
            // points × 5 candidates × 2 axes = ~160 small linear-system
            // solves at finalize time — milliseconds.
            PolynomialFitData? polynomial = FitCerrolazaPolynomial(srcMeans, dstPoints, dotWeights, outlierFloorDip, out double polyRmsX, out double polyRmsY);

            // Fit-quality gate (#335). The fit's own training residual tells us whether
            // the polynomial can even reproduce the dots the user just looked at. A usable
            // calibration lands within a few % of the screen; a residual this large means
            // the iris signal was too noisy/degenerate to fit (poor light, lens glare, a
            // high-res feed downscaled to mush, or head movement) and the result is
            // unusable. Previously it was saved silently and the user was left with
            // tracking that "completed" but was wildly off ("very badly inaccurate, not
            // usable"). The ceiling is deliberately generous — 20% of the screen — so it
            // only catches clearly-broken fits, never a merely-imperfect one. Warn and
            // offer a redo instead of hard-blocking, so a user whose hardware genuinely
            // can't do better can still keep what they've got.
            if (polynomial != null)
            {
                const double MaxFitResidualFraction = 0.20;
                double ceilX = ActualWidth * MaxFitResidualFraction;
                double ceilY = ActualHeight * MaxFitResidualFraction;
                if (polyRmsX > ceilX || polyRmsY > ceilY)
                {
                    App.Logger?.Warning(
                        "WebcamCalibration: fit residual too high — rms_x={Rx:F0} (ceil {Cx:F0}), rms_y={Ry:F0} (ceil {Cy:F0}) DIPs; prompting redo",
                        polyRmsX, ceilX, polyRmsY, ceilY);

                    var choice = System.Windows.MessageBox.Show(
                        this,
                        "This calibration came out very inaccurate — the dots didn't line up, so eye tracking would be unreliable.\n\n" +
                        "For a better result: good, even lighting; avoid glare on glasses (or try without them); keep your head still and look right at each dot.\n\n" +
                        "Try the calibration again?",
                        "Calibration inaccurate",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    if (choice == System.Windows.MessageBoxResult.Yes)
                    {
                        // Exactly what the Recalibrate button does: set WantsRecalibrate
                        // so ShowDialogWithRecalibrate's loop RE-OPENS the dialog. Without
                        // this flag the loop breaks and the user who clicked "try again"
                        // is silently dropped to no calibration (the close alone only
                        // cancels — it does not restart).
                        WantsRecalibrate = true;
                        DialogResult = false;
                        Close();
                        return;
                    }

                    // "No" = keep it anyway — fall through and persist as before.
                    App.Logger?.Information("WebcamCalibration: user kept low-quality calibration despite high residual");
                }
            }

            // LeftRefVec / RightRefVec are the per-side averages of the
            // calibration grid's left and right columns — used by
            // ClassifyGazeSide for left/right gaze-side detection (minigame
            // and validation paths). One sample per row keeps the side
            // reference robust to per-row head-pose drift.
            //
            // TopRefVec / BottomRefVec previously fed an iris-extreme edge-pull
            // heuristic that biased the cursor toward the screen edge when the
            // live iris matched the calibration extreme. The polynomial alone
            // (Cerrolaza + ridge LOO) handles edge accuracy on its own, so the
            // edge-pull is gone — and the Top/Bottom refs along with it.
            double lx = 0, ly = 0, rx = 0, ry = 0;
            for (int i = 0; i < GridSize; i++)
            {
                int leftIdx   = i * GridSize;                       // col 0,  rows 0..N
                int rightIdx  = i * GridSize + (GridSize - 1);      // col N,  rows 0..N
                lx += srcMeans[leftIdx].X;   ly += srcMeans[leftIdx].Y;
                rx += srcMeans[rightIdx].X;  ry += srcMeans[rightIdx].Y;
            }
            var leftRef  = new[] { lx / GridSize, ly / GridSize };
            var rightRef = new[] { rx / GridSize, ry / GridSize };

            // Use the cal window's actual dimensions, NOT
            // SystemParameters.PrimaryScreenWidth/Height. Borderless-maximized
            // sizes the window to the monitor it opened on, which can be the
            // secondary monitor in a multi-monitor setup. The dots above were
            // placed against ActualWidth/ActualHeight, so the polynomial is
            // trained against those screen dims — and the runtime clamp must
            // use the same dims, otherwise the cursor caps at the primary
            // monitor's edge even when calibration was done on a wider
            // secondary monitor.
            var calMonitorWidth = ActualWidth;
            var calMonitorHeight = ActualHeight;

            // Identify which physical monitor calibration ran on, so the
            // multi-monitor hotfix can clamp gaze-reactive content to the
            // calibrated screen at runtime.
            System.Windows.Forms.Screen? calScreen = null;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    calScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: failed to identify calibration monitor");
            }

            // Axis residual correction: the polynomial routinely lands with a
            // SYSTEMATIC per-row bias on Y (e.g. bottom row +145 px too high →
            // "can't reach the bottom of the screen") even when its overall
            // rms looks acceptable. Measure it directly — re-project each grid
            // row/column's mean iris through the fitted polynomial, compare to
            // the true dot position — and store a piecewise-linear warp that
            // cancels it at runtime.
            var axisCorrection = BuildAxisCorrection(polynomial, srcMeans, dstPoints);

            // Calibrated iris range — bounding box of the per-dot mean iris
            // vectors. The runtime clamps the live iris vector to this range
            // (plus a margin) before projecting, which stops the polynomial's
            // quadratic terms from extrapolating a stray sample into a
            // cursor fly-away. See WebcamCalibrationData.IrisRange.
            double irMinX = double.MaxValue, irMaxX = double.MinValue;
            double irMinY = double.MaxValue, irMaxY = double.MinValue;
            foreach (var m in srcMeans)
            {
                if (m.X < irMinX) irMinX = m.X;
                if (m.X > irMaxX) irMaxX = m.X;
                if (m.Y < irMinY) irMinY = m.Y;
                if (m.Y > irMaxY) irMaxY = m.Y;
            }

            var data = new WebcamCalibrationData
            {
                Mode = "SixteenPoint",
                Timestamp = DateTime.UtcNow,
                MonitorBounds = new MonitorBoundsRecord
                {
                    Width = (int)calMonitorWidth,
                    Height = (int)calMonitorHeight,
                    DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX,
                    DeviceName = calScreen?.DeviceName,
                    X = calScreen?.Bounds.X ?? 0,
                    Y = calScreen?.Bounds.Y ?? 0,
                },
                PrimaryDeviceId = "",
                LeftRefVec = leftRef,
                RightRefVec = rightRef,
                Homography = homography,
                Polynomial = polynomial,
                // Head-pose comp retired entirely (see the tombstone where the
                // runtime block used to live in WebcamTrackingService) — new
                // calibrations write null so no comp ever engages.
                BaselineHeadPose = null,
                HeadPoseComp = null,
                IrisRange = new IrisRangeData { MinX = irMinX, MaxX = irMaxX, MinY = irMinY, MaxY = irMaxY },
                AxisCorrection = axisCorrection,
                LightSource = string.IsNullOrEmpty(_lightChoice) ? null : _lightChoice,
            };

            // Live-apply (in-memory only, no disk write yet) so the verify
            // panel can preview the live cursor against this candidate fit.
            App.Webcam?.SetCalibrationLive(data);

            // Guided gesture checks. These NEVER fail the calibration anymore —
            // once the grid is sampled, calibration always completes. The blink/
            // mouth prompts are best-effort warm-ups that advance on a timeout
            // whether or not detection fires (see RunValidationPhaseAsync).
            await RunValidationPhaseAsync();
            if (_cancelled) return;

            // Persist permanently.
            App.Webcam?.ApplyCalibration(data);

            // Clear the legacy-calibration sticky toast (if it was up) now
            // that the user has a fresh calibration with a DeviceName.
            App.Notifications?.Dismiss("recalibrate-multimonitor");

            var settings = App.Settings?.Current;
            if (settings != null)
            {
                settings.WebcamCalibrated = true;
                settings.WebcamCalibrationMode = "SixteenPoint";
                App.Settings?.Save();
            }

            _completedOk = true;
            CalibrationSoundService.CalibrationVerified();

            ValidationPanel.Visibility = Visibility.Collapsed;
            DotCanvas.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Collapsed;

            // Show the verify panel instead of auto-closing. The user gets a
            // chance to preview accuracy via the debug cursor and either
            // accept (Done) or redo (Recalibrate).
            VerifyPanel.Visibility = Visibility.Visible;
            ShortcutHintBanner.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Set to true when the user clicked Recalibrate on the verify panel.
        /// Callers that want to loop should re-open the dialog while this is
        /// true. Use <see cref="ShowDialogWithRecalibrate"/> for the canonical
        /// loop pattern.
        /// </summary>
        public bool WantsRecalibrate { get; private set; }

        /// <summary>
        /// Helper for callers: shows the dialog, re-opens automatically when
        /// the user clicks Recalibrate on the verify panel. Returns the
        /// terminal DialogResult — true when calibration was accepted, false
        /// when cancelled.
        /// </summary>
        public static bool? ShowDialogWithRecalibrate(System.Windows.Window owner)
        {
            bool? final;
            while (true)
            {
                var dlg = new WebcamCalibrationWindow();

                // Only parent to the owner if it's still a live, loaded window.
                // Setting Owner to a window that has already been closed throws
                // "Cannot set Owner property to a Window that has been closed"
                // (the May-22 crash in bug #297 / BUG-988DL9V99S) — e.g. when the
                // caller window was closed between requesting calibration and this
                // dialog opening. Fall back to an unparented dialog in that case.
                if (owner != null && owner.IsLoaded)
                {
                    try { dlg.Owner = owner; }
                    catch (InvalidOperationException) { /* owner closed mid-flight */ }
                }

                App.ApplyCalibrationScreenPlacement(dlg);
                final = dlg.ShowDialog();
                if (!dlg.WantsRecalibrate) break;
            }
            return final;
        }

        private System.Windows.Threading.DispatcherTimer? _verifyCountdownTimer;
        private int _verifyCountdownSecondsLeft;

        private void BtnVerifyAccuracy_Click(object sender, RoutedEventArgs e)
        {
            // Enable debug cursor for ~15s so the user can sanity-check the
            // cursor follows their gaze. Re-clicking before the countdown
            // expires just resets the timer.
            App.GazeCursor?.Show("calibration-verify");
            _verifyCountdownSecondsLeft = 15;
            UpdateVerifyCountdownUi();

            if (_verifyCountdownTimer == null)
            {
                _verifyCountdownTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                _verifyCountdownTimer.Tick += (_, _) =>
                {
                    _verifyCountdownSecondsLeft--;
                    if (_verifyCountdownSecondsLeft <= 0)
                    {
                        StopVerifyCountdown();
                    }
                    else
                    {
                        UpdateVerifyCountdownUi();
                    }
                };
            }
            _verifyCountdownTimer.Stop();
            _verifyCountdownTimer.Start();

            BtnVerifyAccuracy.IsEnabled = false;
        }

        private void UpdateVerifyCountdownUi()
        {
            TxtVerifyStatus.Text = $"Move your eyes around — the pink dot should track them. {_verifyCountdownSecondsLeft}s left.";
        }

        private void StopVerifyCountdown()
        {
            _verifyCountdownTimer?.Stop();
            App.GazeCursor?.Hide("calibration-verify");
            BtnVerifyAccuracy.IsEnabled = true;
            TxtVerifyStatus.Text = "Click Verify to preview accuracy with a live gaze cursor, or close when ready.";
        }

        private void BtnVerifyRecalibrate_Click(object sender, RoutedEventArgs e)
        {
            StopVerifyCountdown();
            WantsRecalibrate = true;
            DialogResult = false;
            Close();
        }

        private void BtnVerifyDone_Click(object sender, RoutedEventArgs e)
        {
            StopVerifyCountdown();
            DialogResult = true;
            Close();
        }

        private async Task RunValidationPhaseAsync()
        {
            // Hide the dot UI; show the validation prompt panel.
            DotCanvas.Visibility = Visibility.Collapsed;
            ValidationPanel.Visibility = Visibility.Visible;
            TxtTitle.Text = "Verifying calibration";
            TxtStatus.Text = "Follow the prompts to confirm the system can read your blinks and mouth.";
            TxtProgress.Text = "";

            // Brief preface so the user can register the mode change before
            // the first prompt appears.
            TxtValidationCue.Text = "";
            TxtValidationPrompt.Text = "Get ready…";
            TxtValidationDetail.Text = "A couple of quick gesture checks and you're done.";
            TxtValidationAttempt.Text = "";
            await Task.Delay(1400);
            if (_cancelled) return;

            // Calibration NEVER fails once the grid has been sampled. The blink
            // and mouth prompts are guided, best-effort warm-ups: each gets a
            // single ~5s window and we advance whether or not detection fires.
            // A missed gesture used to roll back the user's entire calibration
            // (bug #297 / BUG-988DL9V99S), which was a miserable experience for
            // a detection gate that was never fully reliable.
            //
            // The look-left / look-right gaze-direction round was removed
            // entirely: the 16-point grid sample already validates the gaze
            // mapping, so the explicit side check was superfluous. The gaze-side
            // classifier (LeftRefVec / RightRefVec / OnGazeSide) is unaffected —
            // it still drives the gaze minigame at runtime.
            //
            // Tongue-out is likewise not in the sequence (HSV heuristic too
            // unreliable across lighting/angle). The detection plumbing
            // (OnTongueOut / WaitForTongueOutsAsync / ValidateTongueOutStepAsync)
            // is left in place but unused.
            await RunGestureCheckAsync("👁", "Blink a couple of times", needed: 2, WaitForBlinksAsync);
            if (_cancelled) return;

            // Two distinct mouth-opens with a deliberate ~1s gap between them,
            // rather than one needed:2 check. The pause lets the user fully
            // close (so the MAR baseline + open/close hysteresis reset cleanly)
            // and makes the second open a separate, unambiguous gesture instead
            // of a fast double that the 800ms detection cooldown might merge.
            await RunGestureCheckAsync("😮", "Open your mouth wide", needed: 1, WaitForMouthOpensAsync);
            if (_cancelled) return;
            TxtValidationDetail.Text = "Good — close, and once more in a moment…";
            await Task.Delay(1000);
            if (_cancelled) return;
            await RunGestureCheckAsync("😮", "Open your mouth wide again", needed: 1, WaitForMouthOpensAsync);
        }

        /// <summary>
        /// Guided, non-blocking gesture check used during the verify phase.
        /// Shows the prompt, waits up to ~5s for <paramref name="needed"/>
        /// detections via <paramref name="waiter"/>, then advances regardless —
        /// it can never fail the calibration. Shows a check on success or a
        /// gentle "moving on" if the timeout elapses.
        /// </summary>
        private async Task RunGestureCheckAsync(string cue, string prompt, int needed,
            Func<int, int, Action<int>, Task<bool>> waiter)
        {
            const int TimeoutMs = 5000;

            TxtValidationCue.Text = cue;
            TxtValidationPrompt.Text = prompt;
            TxtValidationDetail.Text = $"Detected: 0 / {needed}";
            TxtValidationAttempt.Text = "";

            var got = await waiter(needed, TimeoutMs, count =>
            {
                TxtValidationDetail.Text = $"Detected: {count} / {needed}";
            });
            if (_cancelled) return;

            if (got)
            {
                await FlashSuccessAsync();
            }
            else
            {
                TxtValidationDetail.Text = "No worries — moving on.";
                await Task.Delay(700);
            }
        }

        private async Task<bool> ValidateTongueOutStepAsync(int needed)
        {
            const int TimeoutMs = 12000;
            const int MaxAttempts = 3;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (_cancelled) return false;

                TxtValidationCue.Text = "👅";
                TxtValidationPrompt.Text = needed == 1 ? "Stick out your tongue" : $"Stick out your tongue {needed} times";
                TxtValidationDetail.Text = "Detected: 0 / " + needed;
                TxtValidationAttempt.Text = $"Attempt {attempt} / {MaxAttempts}";

                var got = await WaitForTongueOutsAsync(needed, TimeoutMs, count =>
                {
                    TxtValidationDetail.Text = $"Detected: {count} / {needed}";
                });
                if (_cancelled) return false;

                if (got)
                {
                    await FlashSuccessAsync();
                    return true;
                }

                if (attempt < MaxAttempts)
                {
                    TxtValidationDetail.Text = "Didn't detect a tongue-out. Try again — open your mouth and stick your tongue out clearly.";
                    await Task.Delay(900);
                }
            }
            return false;
        }

        private async Task FlashSuccessAsync()
        {
            CalibrationSoundService.ValidationStepPass();
            var prevCue = TxtValidationCue.Text;
            var prevColor = TxtValidationCue.Foreground;
            TxtValidationCue.Text = "✓";
            TxtValidationCue.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xE0, 0x80));
            TxtValidationDetail.Text = "Detected.";
            await Task.Delay(700);
            TxtValidationCue.Text = prevCue;
            TxtValidationCue.Foreground = prevColor;
        }

        private async Task<bool> WaitForBlinksAsync(int needed, int timeoutMs, Action<int> onProgress)
        {
            if (App.Webcam == null) return false;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;

            void Handler()
            {
                count++;
                onProgress(count);
                if (count >= needed) tcs.TrySetResult(true);
            }

            App.Webcam.OnBlink += Handler;
            try
            {
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return winner == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                App.Webcam.OnBlink -= Handler;
            }
        }

        private async Task<bool> WaitForMouthOpensAsync(int needed, int timeoutMs, Action<int> onProgress)
        {
            if (App.Webcam == null) return false;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;

            void Handler()
            {
                count++;
                onProgress(count);
                if (count >= needed) tcs.TrySetResult(true);
            }

            App.Webcam.OnMouthOpen += Handler;
            try
            {
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return winner == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                App.Webcam.OnMouthOpen -= Handler;
            }
        }

        private async Task<bool> WaitForTongueOutsAsync(int needed, int timeoutMs, Action<int> onProgress)
        {
            if (App.Webcam == null) return false;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;

            void Handler()
            {
                count++;
                onProgress(count);
                if (count >= needed) tcs.TrySetResult(true);
            }

            App.Webcam.OnTongueOut += Handler;
            try
            {
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return winner == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                App.Webcam.OnTongueOut -= Handler;
            }
        }

        private async Task CloseAfterDelayAsync()
        {
            try
            {
                await Task.Delay(1600);
                // Dispatcher may have begun shutting down between the delay
                // starting and finishing — touching DialogResult/Close after
                // shutdown throws TaskCanceledException up to the unhandled
                // dispatcher handler. Guard mirrors the pattern in CLAUDE.md
                // for fire-and-forget Task.Delay continuations.
                if (Application.Current?.Dispatcher == null
                    || Application.Current.Dispatcher.HasShutdownStarted) return;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: CloseAfterDelayAsync threw");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Per-dot iris-vector reduction: saccade-settle drop + MAD trim
        // ─────────────────────────────────────────────────────────────────────

        // Reduces a per-dot sample list to a single iris-vector mean. Drops the
        // first ~210ms of frames as saccade onset + fixation settle (Salvucci &
        // Goldberg I-DT 2000), then keeps samples within median ± 3·MAD/0.6745
        // — a robust 3σ envelope that survives blinks, micro-saccades, and
        // fixation breaks because the median/MAD center+spread aren't inflated
        // by them the way the previous mean+1σ pass was. Returns the surviving
        // sample list too so the head-pose comp fit downstream uses the same
        // set as the per-dot mean.
        private static (double X, double Y, double Spread, List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> Survivors)
            RobustPerDotMean(List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> samples)
        {
            const int SaccadeSettleSamples = 7;          // ~210ms @ 30fps — gaze still saccading + settling
            const double MadCutoff = 3.0;                 // 3 robust σ-equivalent
            const double MadToSigmaScale = 1.0 / 0.6745;  // MAD → robust σ under Gaussian assumption

            int start = (samples.Count - SaccadeSettleSamples >= MinSamplesPerPoint)
                      ? SaccadeSettleSamples : 0;
            var trimmed = (start == 0) ? samples : samples.GetRange(start, samples.Count - start);

            var xs = trimmed.Select(p => p.X).OrderBy(v => v).ToList();
            var ys = trimmed.Select(p => p.Y).OrderBy(v => v).ToList();
            double medX = xs[xs.Count / 2];
            double medY = ys[ys.Count / 2];

            var devX = trimmed.Select(p => Math.Abs(p.X - medX)).OrderBy(v => v).ToList();
            var devY = trimmed.Select(p => Math.Abs(p.Y - medY)).OrderBy(v => v).ToList();
            double madX = devX[devX.Count / 2];
            double madY = devY[devY.Count / 2];

            List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> kept;
            if (madX > 1e-9 || madY > 1e-9)
            {
                double thrX = MadCutoff * madX * MadToSigmaScale + 1e-9;
                double thrY = MadCutoff * madY * MadToSigmaScale + 1e-9;
                kept = trimmed
                    .Where(p => Math.Abs(p.X - medX) <= thrX && Math.Abs(p.Y - medY) <= thrY)
                    .ToList();
                // Back off if the filter ate too many — better a slightly noisier
                // mean than a fit starved of samples by an over-aggressive trim.
                if (kept.Count < MinSamplesPerPoint) kept = trimmed;
            }
            else
            {
                kept = trimmed; // degenerate spread (perfectly stable iris) — nothing to filter
            }

            double sx = 0, sy2 = 0;
            foreach (var p in kept) { sx += p.X; sy2 += p.Y; }
            double meanX = sx / kept.Count;
            double meanY = sy2 / kept.Count;

            // Per-dot spread = RMS distance of the surviving samples from their
            // mean (iris-vector units). Feeds the inverse-spread fit weighting:
            // a dot the user held rock-steady on is trustworthy and should pull
            // the polynomial harder than one whose samples scattered (a wandering
            // gaze, a half-blink, a glance away).
            double varSum = 0;
            foreach (var p in kept)
            {
                double ddx = p.X - meanX, ddy = p.Y - meanY;
                varSum += ddx * ddx + ddy * ddy;
            }
            double spread = Math.Sqrt(varSum / kept.Count);
            return (meanX, meanY, spread, kept);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cerrolaza polynomial fit (light-touch ridge regularization)
        // ─────────────────────────────────────────────────────────────────────

        // Cerrolaza et al. (2008, 2012) X-axis design row.
        // [1, ix, iy, ix·iy, ix², iy², ix²·iy] — the asymmetric ix²·iy term is
        // what beats the symmetric 6-coef form by ~0.15-0.25° DVA on webcam.
        private static double[] CerrolazaRowX(double ix, double iy)
            => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, ix * ix * iy };

        // Cerrolaza Y-axis design row: mirror of X — the high-order term is iy²·ix.
        private static double[] CerrolazaRowY(double ix, double iy)
            => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, iy * iy * ix };

        // Tikhonov-regularization weight, scaled by trace(AᵀA)/p (the mean
        // diagonal of the normal-equations matrix) so the value is invariant
        // to iris-vector magnitude. 1e-5 is essentially zero shrinkage on a
        // well-posed 25-point fit but still keeps the system numerically
        // stable when the iris-vector distribution is degenerate (e.g. all
        // corners collapsed onto one axis from a bad calibration session).
        //
        // Earlier this was a leave-one-out CV across {1e-5..1e-1}, but on
        // 25-point grids LOO punishes corner predictions hard (every leave-out
        // removes a corner from the training set, forcing the remaining fit
        // to extrapolate). LOO biases λ-selection toward heavier shrinkage,
        // which compresses the polynomial's output range — the cursor only
        // reaches a fraction of the screen even at the user's calibrated iris
        // extremes. Then dropped to a fixed 1e-4, then 1e-5; each step opens
        // up the polynomial's reach at the edges another notch.
        private const double RidgeLambdaScale = 1e-5;

        // Solves min ||A·x - b||² + λ·||x||² by stacking sqrt(λ)·I onto A and
        // running OpenCV's normal-equations solve. Returns null if the solve
        // fails (rank-deficient, NaN inputs) or if the solve "succeeds" but
        // produces NaN/Infinity coefficients (Cv2.Solve can return true on
        // near-degenerate systems and silently emit NaN entries — happens
        // when the user moves their head significantly between dots and the
        // iris-vector cluster collapses onto a line). Caller falls back to
        // homography-only projection in that case; without this guard the
        // NaN coefficients would propagate to ProjectGazeToScreen and end
        // up as Window.Left = NaN, which throws on first cursor emit.
        private static double[]? FitRidge(double[][] design, double[] targets, double lambda, double[]? weights = null)
        {
            int n = design.Length;
            int p = design[0].Length;
            using var A = new Mat(n + p, p, MatType.CV_64FC1, Scalar.All(0));
            using var b = new Mat(n + p, 1, MatType.CV_64FC1, Scalar.All(0));
            double sqrtL = Math.Sqrt(Math.Max(lambda, 1e-12));
            for (int i = 0; i < n; i++)
            {
                // Weighted least squares via row scaling: scaling row i by
                // sqrt(w_i) makes the normal equations minimise Σ w_i·resid².
                // A zero weight (an outlier dot dropped below) scales the row to
                // all-zeros, removing it from the fit while the λ·I rows keep
                // the system solvable.
                double sw = weights != null ? Math.Sqrt(Math.Max(weights[i], 0.0)) : 1.0;
                for (int k = 0; k < p; k++) A.Set(i, k, design[i][k] * sw);
                b.Set(i, 0, targets[i] * sw);
            }
            for (int k = 0; k < p; k++) A.Set(n + k, k, sqrtL);
            using var x = new Mat();
            if (!Cv2.Solve(A, b, x, DecompTypes.Normal)) return null;
            var result = new double[p];
            for (int k = 0; k < p; k++)
            {
                var v = x.At<double>(k, 0);
                if (double.IsNaN(v) || double.IsInfinity(v)) return null;
                result[k] = v;
            }
            return result;
        }

        private static double DotProduct(double[] coeffs, double[] features)
        {
            double y = 0;
            for (int k = 0; k < coeffs.Length; k++) y += coeffs[k] * features[k];
            return y;
        }

        private static double MedianOf(double[] values)
        {
            if (values.Length == 0) return 0;
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        // Builds the per-axis Cerrolaza design matrices and fits each with a
        // weighted, light-touch ridge solve. Two robustness layers ride on top
        // of the base fit:
        //   • Inverse-spread weighting (<paramref name="dotWeights"/>): dots the
        //     user held steady on pull the polynomial harder than scattered ones.
        //   • Outlier-dot rejection: after the first fit, up to two dots whose
        //     residual is both a robust statistical outlier (median + 3·MAD) AND
        //     above <paramref name="outlierFloorDip"/> are dropped and the fit
        //     re-run once. This catches a single dot the user blinked or glanced
        //     away on, which would otherwise warp the whole mapping.
        // Logs a diagnostic line with λ, per-axis residuals, and any dropped dots
        // so a compressed / wildly-off / over-trimmed calibration is observable.
        private static PolynomialFitData? FitCerrolazaPolynomial(
            Point2d[] srcMeans, Point2d[] dstPoints, double[] dotWeights, double outlierFloorDip,
            out double rmsX, out double rmsY)
        {
            // Default to "unusable" so the fit-quality gate fails closed on any path
            // that returns null (degenerate solve, exception) without a real residual.
            rmsX = double.PositiveInfinity;
            rmsY = double.PositiveInfinity;
            try
            {
                int n = srcMeans.Length;
                int p = 7;
                var designX = new double[n][];
                var designY = new double[n][];
                var targetsX = new double[n];
                var targetsY = new double[n];
                double traceAtA = 0;
                for (int i = 0; i < n; i++)
                {
                    designX[i] = CerrolazaRowX(srcMeans[i].X, srcMeans[i].Y);
                    designY[i] = CerrolazaRowY(srcMeans[i].X, srcMeans[i].Y);
                    targetsX[i] = dstPoints[i].X;
                    targetsY[i] = dstPoints[i].Y;
                    // Sum the squared-feature magnitudes from the X design;
                    // X and Y share most features (only the high-order term
                    // differs), so this is a representative scale.
                    for (int k = 0; k < p; k++) traceAtA += designX[i][k] * designX[i][k];
                }
                double lambda = RidgeLambdaScale * traceAtA / p;

                // Working weight vector — outlier dots get zeroed and the fit re-run.
                var w = new double[n];
                for (int i = 0; i < n; i++) w[i] = dotWeights[i];

                double[]? coeffsX = null, coeffsY = null;
                var dropped = new List<int>();
                // Pass 0 fits, detects outliers, zeroes them; pass 1 refits once.
                for (int pass = 0; pass < 2; pass++)
                {
                    coeffsX = FitRidge(designX, targetsX, lambda, w);
                    coeffsY = FitRidge(designY, targetsY, lambda, w);
                    if (coeffsX == null || coeffsY == null) return null;
                    if (pass == 1) break;

                    // Residual magnitude for every dot still in the fit.
                    var mags = new List<(int Idx, double R)>();
                    for (int i = 0; i < n; i++)
                    {
                        if (w[i] <= 0) continue;
                        var ex = DotProduct(coeffsX, designX[i]) - targetsX[i];
                        var ey = DotProduct(coeffsY, designY[i]) - targetsY[i];
                        mags.Add((i, Math.Sqrt(ex * ex + ey * ey)));
                    }
                    // Don't trim a small grid into instability (need a healthy
                    // margin over the 7 coefficients for the refit to stay sane).
                    if (mags.Count < 12) break;

                    var rs = mags.Select(m => m.R).OrderBy(v => v).ToList();
                    double med = rs[rs.Count / 2];
                    var devs = rs.Select(v => Math.Abs(v - med)).OrderBy(v => v).ToList();
                    double mad = devs[devs.Count / 2];
                    double thr = med + 3.0 * mad / 0.6745;

                    var cand = mags
                        .Where(m => m.R > thr && m.R > outlierFloorDip)
                        .OrderByDescending(m => m.R)
                        .Take(2)
                        .ToList();
                    if (cand.Count == 0) break;
                    foreach (var c in cand) { w[c.Idx] = 0; dropped.Add(c.Idx); }
                }

                // Diagnostic: per-axis residuals over the dots that survived into
                // the fit + per-row breakdown (top → bottom). rms is computed over
                // the USED dots only so a deliberately-dropped outlier doesn't
                // inflate the figure the fit-quality gate keys on. The per-row
                // breakdown surfaces axis asymmetries — e.g. top-row residuals far
                // worse than bottom-row points at upward-gaze iris bias (upper-
                // eyelid occlusion when looking up).
                double ssX = 0, ssY = 0, maxX = 0, maxY = 0;
                int worstIdxX = -1, worstIdxY = -1, used = 0;
                var residualsY = new double[n];
                var residualsX = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var ex = DotProduct(coeffsX, designX[i]) - targetsX[i];
                    var ey = DotProduct(coeffsY, designY[i]) - targetsY[i];
                    residualsX[i] = ex;
                    residualsY[i] = ey;
                    if (w[i] <= 0) continue; // dropped outlier — exclude from rms/max
                    used++;
                    ssX += ex * ex; ssY += ey * ey;
                    if (Math.Abs(ex) > maxX) { maxX = Math.Abs(ex); worstIdxX = i; }
                    if (Math.Abs(ey) > maxY) { maxY = Math.Abs(ey); worstIdxY = i; }
                }
                if (used == 0) return null;

                // Per-row Y residual summary if we recognize a square grid.
                int rowSize = (int)Math.Round(Math.Sqrt(n));
                string rowSummary = "";
                if (rowSize * rowSize == n)
                {
                    var rowParts = new System.Text.StringBuilder();
                    for (int r = 0; r < rowSize; r++)
                    {
                        double sumY = 0, sumAbsY = 0;
                        for (int c = 0; c < rowSize; c++)
                        {
                            sumY += residualsY[r * rowSize + c];
                            sumAbsY += Math.Abs(residualsY[r * rowSize + c]);
                        }
                        if (rowParts.Length > 0) rowParts.Append(" ");
                        // Mean signed Y residual / mean absolute Y residual per row.
                        rowParts.Append($"r{r}={sumY / rowSize:+0;-0;0}/|{sumAbsY / rowSize:F0}|");
                    }
                    rowSummary = " | rows_y(mean/|abs|): " + rowParts;
                }

                rmsX = Math.Sqrt(ssX / used);
                rmsY = Math.Sqrt(ssY / used);
                string droppedStr = dropped.Count == 0 ? "none" : string.Join(",", dropped);
                App.Logger?.Information(
                    "WebcamCalibration: polynomial fit n={N} used={Used} dropped={Dropped} λ={L:E2} | rms_x={Rx:F1} rms_y={Ry:F1} | max_x={Mx:F1}@{Wx} max_y={My:F1}@{Wy}{Rows} (DIPs)",
                    n, used, droppedStr, lambda, rmsX, rmsY, maxX, worstIdxX, maxY, worstIdxY, rowSummary);

                return new PolynomialFitData { X = coeffsX, Y = coeffsY };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: Cerrolaza polynomial fit threw");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Head-motion compensation fit (guided nod/turn phase)
        // ─────────────────────────────────────────────────────────────────────

        // Regresses the iris vector on head pose over the guided-motion
        // samples: per axis, iris ≈ c0 + kYaw·(sin(yaw)−sin(baseYaw)) +
        // kPitch·(sin(pitch)−sin(basePitch)). Because the user's gaze was
        // pinned to the center dot while the pose varied deliberately, the
        // slope coefficients isolate exactly the pose-induced iris shift the
        // runtime needs to subtract. Robustness: one MAD-trim pass drops
        // blink / glance-away frames; per-axis R² gates zero out an axis the
        // motion didn't constrain; absurd slopes reject the whole fit.
        // ── Axis residual correction ─────────────────────────────────────────────

        /// <summary>
        /// Evaluates the fitted 7-coefficient Cerrolaza polynomial at an iris
        /// vector — must match WebcamTrackingService.ProjectGazeToScreen.
        /// </summary>
        private static WpfPoint EvalPolynomial(PolynomialFitData poly, double ix, double iy)
        {
            double ix2 = ix * ix, iy2 = iy * iy, ixy = ix * iy;
            double x = poly.X[0] + poly.X[1] * ix + poly.X[2] * iy
                     + poly.X[3] * ixy + poly.X[4] * ix2 + poly.X[5] * iy2
                     + poly.X[6] * ix2 * iy;
            double y = poly.Y[0] + poly.Y[1] * ix + poly.Y[2] * iy
                     + poly.Y[3] * ixy + poly.Y[4] * ix2 + poly.Y[5] * iy2
                     + poly.Y[6] * iy2 * ix;
            return new WpfPoint(x, y);
        }

        /// <summary>
        /// Builds the post-polynomial piecewise-linear warp from the grid's
        /// own data: for each grid ROW, where the polynomial projected the
        /// row's dots on average vs where the row actually is (same per
        /// COLUMN for X). The polynomial routinely lands with a systematic
        /// per-row Y bias even at acceptable rms — e.g. bottom row +145 px
        /// too high, which the user experiences as "the cursor is skewed
        /// toward the top, I can't reach the bottom of the screen". Mapping
        /// the projected coordinate through these anchors cancels that bias;
        /// random per-dot noise averages away inside each band. Returns null
        /// (no warp at runtime) when the polynomial is missing, an anchor
        /// sequence is non-monotonic, or a segment's gain is implausible —
        /// a fit that folds or crushes space can't be warped safely.
        /// </summary>
        private static AxisCorrectionData? BuildAxisCorrection(
            PolynomialFitData? poly, Point2d[] srcMeans, Point2d[] dstPoints)
        {
            if (poly == null || poly.X.Length != 7 || poly.Y.Length != 7) return null;
            const double MinGain = 0.4, MaxGain = 3.0; // dst-per-src slope sanity per segment
            try
            {
                int n = GridSize;
                var srcY = new double[n]; var dstY = new double[n];
                var srcX = new double[n]; var dstX = new double[n];
                for (int r = 0; r < n; r++)
                {
                    double projSum = 0, trueSum = 0;
                    for (int c = 0; c < n; c++)
                    {
                        int idx = r * n + c;
                        projSum += EvalPolynomial(poly, srcMeans[idx].X, srcMeans[idx].Y).Y;
                        trueSum += dstPoints[idx].Y;
                    }
                    srcY[r] = projSum / n;
                    dstY[r] = trueSum / n;
                }
                for (int c = 0; c < n; c++)
                {
                    double projSum = 0, trueSum = 0;
                    for (int r = 0; r < n; r++)
                    {
                        int idx = r * n + c;
                        projSum += EvalPolynomial(poly, srcMeans[idx].X, srcMeans[idx].Y).X;
                        trueSum += dstPoints[idx].X;
                    }
                    srcX[c] = projSum / n;
                    dstX[c] = trueSum / n;
                }

                // Anchors must be strictly increasing (dst is by grid
                // construction; src only if the fit didn't fold space) and
                // every segment's gain sane, on both axes.
                static bool Usable(double[] src, double[] dst)
                {
                    for (int i = 1; i < src.Length; i++)
                    {
                        double ds = src[i] - src[i - 1];
                        double dd = dst[i] - dst[i - 1];
                        if (ds < 1.0 || dd < 1.0) return false;
                        double gain = dd / ds;
                        if (gain < MinGain || gain > MaxGain) return false;
                    }
                    return true;
                }

                bool xOk = Usable(srcX, dstX);
                bool yOk = Usable(srcY, dstY);
                if (!xOk && !yOk)
                {
                    App.Logger?.Information("WebcamCalibration: axis correction skipped — anchors unusable on both axes");
                    return null;
                }
                // A dead axis keeps identity anchors so the runtime code path
                // stays uniform.
                if (!xOk) { srcX = (double[])dstX.Clone(); }
                if (!yOk) { srcY = (double[])dstY.Clone(); }

                App.Logger?.Information(
                    "WebcamCalibration: axis correction built | rowY Δ=[{Y0:F0},{Y1:F0},{Y2:F0},{Y3:F0}] colX Δ=[{X0:F0},{X1:F0},{X2:F0},{X3:F0}] (DIPs, dst−src){Note}",
                    dstY[0] - srcY[0], dstY[1] - srcY[1], dstY[2] - srcY[2], dstY[3] - srcY[3],
                    dstX[0] - srcX[0], dstX[1] - srcX[1], dstX[2] - srcX[2], dstX[3] - srcX[3],
                    xOk && yOk ? "" : (xOk ? " [Y axis identity]" : " [X axis identity]"));

                return new AxisCorrectionData { SrcX = srcX, DstX = dstX, SrcY = srcY, DstY = dstY };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: axis correction build threw");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Bubble accuracy test (optional, launched from the verify panel)
        // ─────────────────────────────────────────────────────────────────────
        //  Four bubbles, one at a time, at spread positions. The live gaze
        //  cursor is shown; holding the cursor near a bubble for ~0.7 s pops
        //  it (same dwell mechanic as Focus Gaze pop). Doubles as an implicit
        //  RECALIBRATION: the dwell samples ARE ground truth ("user is
        //  looking at the bubble"), so the per-bubble residuals feed a
        //  per-axis linear correction — offset AND scale — applied as the
        //  calibration's GazeTrim (see FitAxisTrim / ApplyGazeTrim). A
        //  multi-point quick-recal the user experiences as a minigame.

        private const string BubbleTestCursorKey = "calibration-bubbletest";
        private const double BubblePopRadiusDips = 130;     // gaze-to-center distance that counts as "on it" (bubble visual radius 64 + slack)
        private const int BubbleDwellNeededMs = 700;
        private const int BubbleAvgWindowSamples = 12;      // ~400ms rolling mean drives the on-target decision
        private const int BubbleTimeoutMs = 10000;
        private const double NearMissMaxDips = 250;          // timeout bubbles still yield a recal sample within this

        private bool _bubbleTestRunning;
        private WpfPoint? _bubbleGaze;
        private DateTime _bubbleGazeAt = DateTime.MinValue;

        private async void BtnVerifyBubbleTest_Click(object sender, RoutedEventArgs e)
        {
            if (_bubbleTestRunning) return;
            _bubbleTestRunning = true;
            StopVerifyCountdown();
            VerifyPanel.Visibility = Visibility.Collapsed;
            ShortcutHintBanner.Visibility = Visibility.Collapsed;
            BubbleTestPanel.Visibility = Visibility.Visible;
            App.GazeCursor?.Show(BubbleTestCursorKey);
            if (App.Webcam != null) App.Webcam.OnGazeMove += OnBubbleTestGaze;

            string summary;
            try
            {
                summary = await RunBubbleTestAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: bubble test threw");
                summary = "Bubble test hit an error — see logs/app.log.";
            }
            finally
            {
                if (App.Webcam != null) App.Webcam.OnGazeMove -= OnBubbleTestGaze;
                App.Webcam?.ClearGazeAttractor();
                App.GazeCursor?.Hide(BubbleTestCursorKey);
                BubbleTestPanel.Visibility = Visibility.Collapsed;
                _bubbleTestRunning = false;
            }

            if (_cancelled) return;
            VerifyPanel.Visibility = Visibility.Visible;
            ShortcutHintBanner.Visibility = Visibility.Visible;
            TxtVerifyStatus.Text = summary;
        }

        private void OnBubbleTestGaze(WpfPoint p)
        {
            _bubbleGaze = p;
            _bubbleGazeAt = DateTime.UtcNow;
        }

        private async Task<string> RunBubbleTestAsync()
        {
            const int TickMs = 33;

            double w = ActualWidth, h = ActualHeight;
            // Quadrant spread, clockwise from top-left. Off the extreme
            // corners (where residuals are naturally worst) but far enough
            // apart to exercise the whole mapping.
            var spots = new[]
            {
                new WpfPoint(w * 0.24, h * 0.30),
                new WpfPoint(w * 0.76, h * 0.30),
                new WpfPoint(w * 0.76, h * 0.72),
                new WpfPoint(w * 0.24, h * 0.72),
            };

            int popped = 0;
            var errors = new List<double>();          // |gaze−bubble| per popped bubble
            // Ground-truth pairs for the recalibration fit: where the bubble
            // was vs where the (pre-lock) cursor actually hovered.
            var samples = new List<(WpfPoint Target, WpfPoint Measured)>();

            for (int i = 0; i < spots.Length; i++)
            {
                if (_cancelled) return "";
                var center = spots[i];
                MoveBubbleTo(center);
                UpdateRing(TestBubbleRingFg, 0);
                TestBubble.Opacity = 1.0;
                TestBubbleScale.ScaleX = 1.0;
                TestBubbleScale.ScaleY = 1.0;
                TxtBubbleTestStatus.Text = $"Bubble {i + 1} / {spots.Length} — pop it with your eyes.";

                // Declare this bubble as the current intent target: the gaze
                // attractor contracts wobble around it so the cursor stays in
                // the zone once the user is clearly trying to hit it.
                App.Webcam?.SetGazeAttractor(center.X, center.Y, BubblePopRadiusDips);

                double dwellMs = 0;
                var insideSamples = new List<WpfPoint>();
                var recentSamples = new Queue<(DateTime At, WpfPoint P)>();
                var avgWindow = new Queue<WpfPoint>();
                var started = DateTime.UtcNow;
                bool didPop = false;

                while (true)
                {
                    await Task.Delay(TickMs);
                    if (_cancelled) return "";
                    var now = DateTime.UtcNow;
                    if ((now - started).TotalMilliseconds > BubbleTimeoutMs) break;

                    var gaze = _bubbleGaze;
                    bool fresh = gaze.HasValue && (now - _bubbleGazeAt).TotalMilliseconds < 300;
                    WpfPoint raw = default;
                    if (fresh)
                    {
                        // Residual/accuracy math must not be flattered by the
                        // lock-on — record the pre-lock point the tracking
                        // service snapshots each frame. The dwell decision
                        // below deliberately DOES use the locked point
                        // (that's the forgiveness).
                        raw = App.Webcam?.LastPreLockGaze ?? gaze!.Value;
                        recentSamples.Enqueue((now, raw));
                        while (recentSamples.Count > 0 && (now - recentSamples.Peek().At).TotalMilliseconds > 2000)
                            recentSamples.Dequeue();
                        avgWindow.Enqueue(gaze.Value);
                        while (avgWindow.Count > BubbleAvgWindowSamples) avgWindow.Dequeue();
                    }

                    // On-target decision runs on the ROLLING MEAN, not the
                    // instantaneous point: if the user is lingering on the
                    // zone on average, a single wobble frame outside the
                    // radius must not drop the target (user report — "at the
                    // first wobble out of the bubble zone we lose it").
                    double dist = double.MaxValue;
                    if (fresh && avgWindow.Count >= 3)
                    {
                        double ax = 0, ay = 0;
                        foreach (var q in avgWindow) { ax += q.X; ay += q.Y; }
                        ax /= avgWindow.Count;
                        ay /= avgWindow.Count;
                        dist = Math.Sqrt((ax - center.X) * (ax - center.X) + (ay - center.Y) * (ay - center.Y));
                    }

                    if (dist <= BubblePopRadiusDips)
                    {
                        dwellMs += TickMs;
                        insideSamples.Add(raw);
                    }
                    else
                    {
                        // Leaky decay instead of a hard reset — a stray
                        // stretch shouldn't wipe accumulated dwell.
                        dwellMs = Math.Max(0, dwellMs - TickMs);
                    }
                    UpdateRing(TestBubbleRingFg, dwellMs / BubbleDwellNeededMs);

                    if (dwellMs >= BubbleDwellNeededMs)
                    {
                        didPop = true;
                        break;
                    }
                }
                App.Webcam?.ClearGazeAttractor();

                if (didPop)
                {
                    popped++;
                    var med = MedianPoint(insideSamples);
                    double err = Math.Sqrt(Math.Pow(med.X - center.X, 2) + Math.Pow(med.Y - center.Y, 2));
                    errors.Add(err);
                    samples.Add((center, med));
                    CalibrationSoundService.BubblePop();
                    await PlayBubblePopAnimationAsync();
                }
                else
                {
                    // Timeout. If the gaze hovered at a consistent spot near
                    // the bubble, that's still a usable sample — the user was
                    // looking at it, the mapping just missed. These are the
                    // MOST valuable pairs for the fit: the biggest errors.
                    if (recentSamples.Count >= 10)
                    {
                        var med = MedianPoint(recentSamples.Select(s => s.P).ToList());
                        double dist = Math.Sqrt(Math.Pow(med.X - center.X, 2) + Math.Pow(med.Y - center.Y, 2));
                        if (dist <= NearMissMaxDips)
                            samples.Add((center, med));
                    }
                    TxtBubbleTestStatus.Text = "Moving on…";
                    TestBubble.Opacity = 0.25;
                    await Task.Delay(500);
                    if (_cancelled) return "";
                }
            }

            HideBubble();

            // Recalibrate from the residuals. Each bubble is ground truth —
            // the user was trying to look THERE — so fit a per-axis linear
            // correction (offset + center-relative scale) through the
            // (measured → target) pairs. The scale term is what a plain
            // offset can't express: if the cursor sat too high on the LOW
            // bubbles but was fine on the high ones, the map is vertically
            // stretched, and the slope measures exactly that. Falls back to
            // offset-only when the sample geometry is too thin for a slope.
            bool tuned = false;
            double rmsBefore = 0, rmsAfter = 0;
            if (samples.Count >= 2)
            {
                double cx = ActualWidth / 2.0, cy = ActualHeight / 2.0;
                var (x0, x1) = FitAxisTrim(samples.Select(s => (s.Target.X, s.Measured.X)).ToList(), cx, ActualWidth * 0.15);
                var (y0, y1) = FitAxisTrim(samples.Select(s => (s.Target.Y, s.Measured.Y)).ToList(), cy, ActualHeight * 0.15);

                foreach (var (t, m) in samples)
                {
                    double rx = t.X - m.X, ry = t.Y - m.Y;
                    rmsBefore += rx * rx + ry * ry;
                    double ax = m.X + x0 + x1 * (m.X - cx);
                    double ay = m.Y + y0 + y1 * (m.Y - cy);
                    rmsAfter += (t.X - ax) * (t.X - ax) + (t.Y - ay) * (t.Y - ay);
                }
                rmsBefore = Math.Sqrt(rmsBefore / samples.Count);
                rmsAfter = Math.Sqrt(rmsAfter / samples.Count);

                // Apply only when it measurably helps and isn't noise-level.
                if (rmsAfter < rmsBefore - 2
                    && (Math.Abs(x0) >= 3 || Math.Abs(y0) >= 3 || Math.Abs(x1) >= 0.01 || Math.Abs(y1) >= 0.01))
                {
                    App.Webcam?.ApplyGazeTrim(x0, x1, y0, y1, cx, cy, persist: true);
                    tuned = true;
                    App.Logger?.Information(
                        "WebcamCalibrationWindow: bubble-test recal — x0={X0:F0} x1={X1:F3} y0={Y0:F0} y1={Y1:F3} from {N} bubbles (rms {B:F0}→{A:F0} DIPs)",
                        x0, x1, y0, y1, samples.Count, rmsBefore, rmsAfter);
                }
            }

            if (popped == 0 && !tuned)
                return "Couldn't pop any bubbles — tracking may be off for this setup. Consider recalibrating.";

            var text = popped > 0
                ? $"Popped {popped} / {spots.Length} · average accuracy {errors.Average():F0} px"
                : $"Popped 0 / {spots.Length}";
            if (tuned) text += $" · recalibrated from your aim ({rmsBefore:F0}→{rmsAfter:F0} px). Run it again to verify!";
            else text += ".";
            return text;
        }

        // LS fit of residual = c0 + c1·(measured − center) over the bubble
        // samples of one axis. The slope needs ≥3 samples spanning enough of
        // the axis (minSpreadForScale) to be identifiable — with less
        // geometry it degrades to offset-only. Clamps keep one bad test from
        // warping the map; ApplyGazeTrim clamps the composed total again.
        private static (double C0, double C1) FitAxisTrim(
            List<(double Target, double Measured)> pts, double center, double minSpreadForScale)
        {
            int n = pts.Count;
            double meanU = 0, meanR = 0;
            double minU = double.MaxValue, maxU = double.MinValue;
            foreach (var (t, m) in pts)
            {
                double u = m - center;
                meanU += u; meanR += t - m;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }
            meanU /= n; meanR /= n;

            double c1 = 0;
            if (n >= 3 && maxU - minU >= minSpreadForScale)
            {
                double varU = 0, cov = 0;
                foreach (var (t, m) in pts)
                {
                    double du = m - center - meanU;
                    double dr = (t - m) - meanR;
                    varU += du * du;
                    cov += du * dr;
                }
                if (varU > 1e-6) c1 = Math.Clamp(cov / varU, -0.25, 0.25);
            }
            double c0 = Math.Clamp(meanR - c1 * meanU, -250, 250);
            return (c0, c1);
        }

        private async Task PlayBubblePopAnimationAsync()
        {
            var scale = new DoubleAnimation(1.0, 1.7, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
            TestBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
            TestBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
            TestBubble.BeginAnimation(OpacityProperty, fade);
            await Task.Delay(320);
            TestBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            TestBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            TestBubble.BeginAnimation(OpacityProperty, null);
        }

        private void MoveBubbleTo(WpfPoint p)
        {
            System.Windows.Controls.Canvas.SetLeft(TestBubble, p.X - TestBubble.Width / 2);
            System.Windows.Controls.Canvas.SetTop(TestBubble, p.Y - TestBubble.Height / 2);
            System.Windows.Controls.Canvas.SetLeft(TestBubbleRingBg, p.X - TestBubbleRingBg.Width / 2);
            System.Windows.Controls.Canvas.SetTop(TestBubbleRingBg, p.Y - TestBubbleRingBg.Height / 2);
            System.Windows.Controls.Canvas.SetLeft(TestBubbleRingFg, p.X - TestBubbleRingFg.Width / 2);
            System.Windows.Controls.Canvas.SetTop(TestBubbleRingFg, p.Y - TestBubbleRingFg.Height / 2);
            TestBubble.Visibility = Visibility.Visible;
            TestBubbleRingBg.Visibility = Visibility.Visible;
            TestBubbleRingFg.Visibility = Visibility.Visible;
        }

        private void HideBubble()
        {
            TestBubble.Visibility = Visibility.Collapsed;
            TestBubbleRingBg.Visibility = Visibility.Collapsed;
            TestBubbleRingFg.Visibility = Visibility.Collapsed;
        }

        private static WpfPoint MedianPoint(List<WpfPoint> pts)
        {
            var xs = pts.Select(p => p.X).OrderBy(v => v).ToList();
            var ys = pts.Select(p => p.Y).OrderBy(v => v).ToList();
            return new WpfPoint(xs[xs.Count / 2], ys[ys.Count / 2]);
        }

        private void MoveDotTo(WpfPoint screenPoint)
        {
            System.Windows.Controls.Canvas.SetLeft(Dot, screenPoint.X - Dot.Width / 2);
            System.Windows.Controls.Canvas.SetTop(Dot, screenPoint.Y - Dot.Height / 2);
            System.Windows.Controls.Canvas.SetLeft(DotRingBg, screenPoint.X - DotRingBg.Width / 2);
            System.Windows.Controls.Canvas.SetTop(DotRingBg, screenPoint.Y - DotRingBg.Height / 2);
            System.Windows.Controls.Canvas.SetLeft(DotRingFg, screenPoint.X - DotRingFg.Width / 2);
            System.Windows.Controls.Canvas.SetTop(DotRingFg, screenPoint.Y - DotRingFg.Height / 2);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Per-dot progress ring
        // ─────────────────────────────────────────────────────────────────────

        // Update the foreground ring's stroke-dash to display the given fraction
        // of the perimeter as filled (0..1). StrokeDashArray is expressed in
        // *stroke-thickness multiples*, so we divide the pixel perimeter by the
        // stroke thickness to get the right unit.
        private void UpdateProgressRing(double progress) => UpdateRing(DotRingFg, progress);

        private static void UpdateRing(System.Windows.Shapes.Ellipse ring, double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            double radius = (ring.Width - ring.StrokeThickness) / 2.0;
            double perimeter = 2.0 * Math.PI * radius;
            double units = perimeter / ring.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            ring.StrokeDashArray = new DoubleCollection(new[] { visible, gap });
        }

        private void ResetProgressRing()
        {
            DotRingFg.StrokeDashArray = new DoubleCollection(new[] { 0.0, 10000.0 });
            DotRingScale.ScaleX = 1.0;
            DotRingScale.ScaleY = 1.0;
            DotRingFg.Opacity = 1.0;
        }

        private void StartRingPulse()
        {
            StopRingPulse();
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var scaleX = new DoubleAnimation
            {
                From = 1.0, To = 1.18,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            var scaleY = new DoubleAnimation
            {
                From = 1.0, To = 1.18,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(scaleX, DotRingScale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(scaleY, DotRingScale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Begin();
            _ringPulse = sb;
        }

        private void StopRingPulse()
        {
            _ringPulse?.Stop();
            _ringPulse = null;
            DotRingScale.ScaleX = 1.0;
            DotRingScale.ScaleY = 1.0;
        }

        private void ShowError(string detail)
        {
            _collecting = false;
            StopRingPulse();
            DotCanvas.Visibility = Visibility.Collapsed;
            TxtErrorDetail.Text = detail;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
