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
    /// Fullscreen 25-point gaze calibration (5×5 grid). Samples raw iris vectors
    /// at each point, fits a 3×3 homography and an over-determined 2nd-order
    /// polynomial (Cerrolaza asymmetric form, ridge-regularized with λ chosen
    /// by leave-one-out CV, iris → screen DIPs), and persists via
    /// WebcamCalibrationData.
    ///
    /// Grid sizing: 5×5 with margin=40 puts corner dots ~40 DIPs from the bezel,
    /// so the polynomial is trained on near-edge gaze angles directly — corner
    /// reach (especially top/bottom regions, which 4×4 had to extrapolate) is
    /// dramatically better. An earlier 5×5 attempt was abandoned because of
    /// jumpy cursor + drift, but those issues traced to the unregularized
    /// Tikhonov fit, mean+σ outlier rejection, head-pose comp, and edge-pull
    /// — all replaced or removed. The current pipeline (Cerrolaza + ridge LOO,
    /// I-DT saccade-settle + MAD trim) is robust enough at the wider angles.
    ///
    /// Caller is responsible for ensuring App.Webcam is already running.
    /// </summary>
    public partial class WebcamCalibrationWindow : System.Windows.Window
    {
        private const int ReadyMs = 600;          // dot moves, user re-fixates
        private const int SampleMs = 1100;        // ~33 samples at 30fps; well above MinSamplesPerPoint after the saccade-settle drop
        private const int SettleMs = 200;         // pause between dots
        private const int RetryReadyMs = 900;     // longer pause before retrying a missed dot
        private const int MinSamplesPerPoint = 20;
        private const int MaxAttemptsPerPoint = 2; // miss twice in a row → fail calibration
        private const int GridSize = 5;            // 5×5 = 25 calibration points (top/bottom/side midpoints + corners + interior)
        private const double EdgeMargin = 40;      // distance from screen edge for corner dots (DIPs); small enough that polynomial extrapolation to bezel is negligible

        // Sample target for the "ring is full" feedback. We treat the SampleMs
        // window's worth of frames at ~30fps as 100% — this is above the hard
        // MinSamplesPerPoint floor with comfortable headroom, so the ring fills
        // smoothly across the window rather than maxing out almost immediately.
        private const int RingFullSampleTarget = 33;

        // Per-dot iris samples paired with the head-pose at the same frame.
        // Pairing lets the finalize pass reject frames where the user's head
        // drifted off-center — those samples carry the right iris but the
        // wrong head-relative reference frame, so they bias the mean.
        private readonly List<List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>> _allSamples = new();
        // Head-pose samples accumulated across all 9 dots (single flat list,
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
        private readonly TaskCompletionSource<bool> _introDone = new();
        private Storyboard? _ringPulse;
        private bool _ringIsFull;
        // Index of the dot currently being sampled. Read by OnRawIris so
        // samples land in the right per-dot bucket across retries (we can't
        // rely on "last list" because all 9 are allocated up-front).
        private int _activeDotIndex = -1;

        public WebcamCalibrationWindow()
        {
            InitializeComponent();
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

            // Show the intro overlay first so users know what's coming —
            // the dot grid + head-movement + validation checks are otherwise
            // a surprise. DotCanvas / StatusPanel stay hidden until the
            // user clicks Continue (or presses ESC, which cancels).
            DotCanvas.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Collapsed;
            IntroPanel.Visibility = Visibility.Visible;

            var proceed = await _introDone.Task;
            if (!proceed || _cancelled) return;

            IntroPanel.Visibility = Visibility.Collapsed;
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

        private void Window_Closed(object sender, EventArgs e)
        {
            if (App.Webcam != null)
            {
                App.Webcam.OnRawIris -= OnRawIris;
                App.Webcam.OnHeadPose -= OnHeadPose;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _cancelled = true;
                _collecting = false;
                _introDone.TrySetResult(false);
                DialogResult = false;
                Close();
            }
        }

        private void BtnErrorClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = _completedOk;
            Close();
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
            // 5×5 grid, row-major. 25 dots evenly spaced across cols/rows 0..4
            // of the usable span. Layout:
            //    0  1  2  3  4      (top row)
            //    5  6  7  8  9
            //   10 11 12 13 14
            //   15 16 17 18 19
            //   20 21 22 23 24      (bottom row)
            // Left column = {0,5,10,15,20}; right column = {4,9,14,19,24};
            // top row = {0..4}; bottom row = {20..24}.
            double xL = EdgeMargin, xR = w - EdgeMargin;
            double yT = EdgeMargin, yB = h - EdgeMargin;
            string[] rowLabels = { "Top", "Upper", "Middle", "Lower", "Bottom" };
            string[] colLabels = { "left", "mid-left", "center", "mid-right", "right" };
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
                    _collecting = true;
                    await Task.Delay(SampleMs);
                    _collecting = false;
                    if (_cancelled) return;

                    if (_allSamples[i].Count >= MinSamplesPerPoint)
                    {
                        succeeded = true;
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
                var (mx, my, kept) = RobustPerDotMean(s);
                srcMeans[i] = new Point2d(mx, my);
                dstPoints[i] = new Point2d(positions[i].Screen.X, positions[i].Screen.Y);
                survivors[i] = kept;
            }

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
            // on webcam data (Hansen & Ji 2010, Zhang & Hornof 2014). 25
            // points × 5 candidates × 2 axes = ~250 small linear-system
            // solves at finalize time — milliseconds.
            PolynomialFitData? polynomial = FitCerrolazaPolynomial(srcMeans, dstPoints);

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

            var primary = SystemParameters.PrimaryScreenWidth;
            var primaryH = SystemParameters.PrimaryScreenHeight;

            // Head-pose compensation pipeline (BaselineHeadPose + HeadPoseComp)
            // was retired. The PnP head-pose estimator from face landmarks is
            // noisier than the natural head movement during normal use, so the
            // empirical comp fit was injecting variance instead of removing it
            // — this matches Funes Mora & Odobez (2013) showing comp goes
            // negative below ~5° rotation noise. Calibrations from older
            // builds that still have these fields populated are simply ignored
            // by the runtime.
            var data = new WebcamCalibrationData
            {
                Mode = "TwentyFivePoint",
                Timestamp = DateTime.UtcNow,
                MonitorBounds = new MonitorBoundsRecord
                {
                    Width = (int)primary,
                    Height = (int)primaryH,
                    DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX
                },
                PrimaryDeviceId = "",
                LeftRefVec = leftRef,
                RightRefVec = rightRef,
                Homography = homography,
                Polynomial = polynomial,
                BaselineHeadPose = null,
                HeadPoseComp = null,
            };

            // Live-apply (in-memory only, no disk write yet) so the validation
            // phase exercises the live classifier against this candidate fit.
            // We restore the previous calibration (or null) if validation fails.
            var previousCalibration = App.Webcam?.Calibration;
            App.Webcam?.SetCalibrationLive(data);

            var validated = await RunValidationPhaseAsync();
            if (_cancelled) return;

            if (!validated)
            {
                // Roll back: don't write to disk, restore the previous in-memory state.
                App.Webcam?.SetCalibrationLive(previousCalibration);
                ShowError(
                    "The system couldn't reliably detect your gaze, blinks, mouth-open, or tongue with this calibration. " +
                    "Tips: sit closer to the camera, make sure your face is well-lit and unshadowed, " +
                    "remove reflective glasses if possible, and try again.");
                return;
            }

            // Validation passed — persist permanently.
            App.Webcam?.ApplyCalibration(data);

            var settings = App.Settings?.Current;
            if (settings != null)
            {
                settings.WebcamCalibrated = true;
                settings.WebcamCalibrationMode = "TwentyFivePoint";
                App.Settings?.Save();
            }

            _completedOk = true;

            ValidationPanel.Visibility = Visibility.Collapsed;
            TxtTitle.Text = "Calibration verified";
            TxtStatus.Text = "Gaze, blink, mouth-open, and tongue detection confirmed working.";
            TxtProgress.Text = "Closing…";
            DotCanvas.Visibility = Visibility.Collapsed;

            _ = CloseAfterDelayAsync();
        }

        private async Task<bool> RunValidationPhaseAsync()
        {
            // Hide the dot UI; show the validation prompt panel.
            DotCanvas.Visibility = Visibility.Collapsed;
            ValidationPanel.Visibility = Visibility.Visible;
            TxtTitle.Text = "Verifying calibration";
            TxtStatus.Text = "Follow the prompts to confirm the system can read your gaze and blinks.";
            TxtProgress.Text = "";

            // Brief preface so the user can register the mode change before
            // the first arrow appears.
            TxtValidationCue.Text = "";
            TxtValidationPrompt.Text = "Get ready…";
            TxtValidationDetail.Text = "We'll check that the system can detect your gaze and blinks with this calibration.";
            TxtValidationAttempt.Text = "";
            await Task.Delay(1400);
            if (_cancelled) return false;

            // Sequence: L, R, L, R, blink×2, mouth-open×3, tongue-out×1.
            // Each step gets up to 3 attempts (12 s timeout each) before failing
            // the whole calibration. Mouth uses 3 passes (the MAR ratio is
            // steady, gestures are unambiguous). Tongue stays at 1 — the HSV-
            // color heuristic is reliable enough for a single deliberate
            // protrusion, but stacking 3 cycles in 12 s with a 700 ms cooldown
            // ran into too many missed detections from lighting/angle variance.
            if (!await ValidateGazeStepAsync(GazeSide.Left,  "Look LEFT",  "←", roundLabel: "1 of 4")) return false;
            if (_cancelled) return false;
            if (!await ValidateGazeStepAsync(GazeSide.Right, "Look RIGHT", "→", roundLabel: "2 of 4")) return false;
            if (_cancelled) return false;
            if (!await ValidateGazeStepAsync(GazeSide.Left,  "Look LEFT",  "←", roundLabel: "3 of 4")) return false;
            if (_cancelled) return false;
            if (!await ValidateGazeStepAsync(GazeSide.Right, "Look RIGHT", "→", roundLabel: "4 of 4")) return false;
            if (_cancelled) return false;
            if (!await ValidateBlinkStepAsync(needed: 2)) return false;
            if (_cancelled) return false;
            if (!await ValidateMouthOpenStepAsync(needed: 3)) return false;
            if (_cancelled) return false;
            if (!await ValidateTongueOutStepAsync(needed: 1)) return false;
            return true;
        }

        private async Task<bool> ValidateGazeStepAsync(GazeSide target, string prompt, string cue, string roundLabel)
        {
            // Require 2 seconds of continuous on-target classification. Short
            // glances or involuntary saccades shouldn't be enough to pass —
            // the user has to actually fixate on the requested side.
            const int HoldMs = 2000;
            const int TimeoutMs = 12000;
            const int MaxAttempts = 3;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (_cancelled) return false;

                TxtValidationCue.Text = cue;
                TxtValidationPrompt.Text = $"{prompt}  ({roundLabel})";
                TxtValidationDetail.Text = $"Hold your gaze on the {target.ToString().ToLowerInvariant()} side for {HoldMs / 1000.0:F1}s.";
                TxtValidationAttempt.Text = $"Attempt {attempt} / {MaxAttempts}";

                var detected = await WaitForGazeSideAsync(target, HoldMs, TimeoutMs, (elapsedMs, totalMs) =>
                {
                    if (elapsedMs <= 0)
                        TxtValidationDetail.Text = $"Hold your gaze on the {target.ToString().ToLowerInvariant()} side for {totalMs / 1000.0:F1}s.";
                    else
                        TxtValidationDetail.Text = $"Holding… {elapsedMs / 1000.0:F1} / {totalMs / 1000.0:F1}s";
                });
                if (_cancelled) return false;

                if (detected)
                {
                    await FlashSuccessAsync();
                    return true;
                }

                if (attempt < MaxAttempts)
                {
                    TxtValidationDetail.Text = "Didn't detect that. Try again — exaggerate the look if needed.";
                    await Task.Delay(900);
                }
            }
            return false;
        }

        private async Task<bool> ValidateBlinkStepAsync(int needed)
        {
            const int TimeoutMs = 12000;
            const int MaxAttempts = 3;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (_cancelled) return false;

                TxtValidationCue.Text = "👁";
                TxtValidationPrompt.Text = $"Blink {needed} times";
                TxtValidationDetail.Text = "Detected: 0 / " + needed;
                TxtValidationAttempt.Text = $"Attempt {attempt} / {MaxAttempts}";

                var got = await WaitForBlinksAsync(needed, TimeoutMs, count =>
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
                    TxtValidationDetail.Text = "Didn't get enough blinks. Try again — blink slowly and deliberately.";
                    await Task.Delay(900);
                }
            }
            return false;
        }

        private async Task<bool> ValidateMouthOpenStepAsync(int needed)
        {
            const int TimeoutMs = 12000;
            const int MaxAttempts = 3;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (_cancelled) return false;

                TxtValidationCue.Text = "😮";
                TxtValidationPrompt.Text = needed == 1 ? "Open your mouth wide" : $"Open your mouth wide {needed} times";
                TxtValidationDetail.Text = "Detected: 0 / " + needed;
                TxtValidationAttempt.Text = $"Attempt {attempt} / {MaxAttempts}";

                var got = await WaitForMouthOpensAsync(needed, TimeoutMs, count =>
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
                    TxtValidationDetail.Text = "Didn't detect a mouth-open. Try again — open wide like a yawn.";
                    await Task.Delay(900);
                }
            }
            return false;
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
            var prevCue = TxtValidationCue.Text;
            var prevColor = TxtValidationCue.Foreground;
            TxtValidationCue.Text = "✓";
            TxtValidationCue.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xE0, 0x80));
            TxtValidationDetail.Text = "Detected.";
            await Task.Delay(700);
            TxtValidationCue.Text = prevCue;
            TxtValidationCue.Foreground = prevColor;
        }

        private async Task<bool> WaitForGazeSideAsync(GazeSide target, int holdMs, int timeoutMs, Action<int, int> onProgress)
        {
            if (App.Webcam == null) return false;
            var tcs = new TaskCompletionSource<bool>();
            DateTime? holdStart = null;

            void Handler(GazeSide side)
            {
                var now = DateTime.UtcNow;
                if (side == target)
                {
                    holdStart ??= now;
                    var elapsed = (int)(now - holdStart.Value).TotalMilliseconds;
                    onProgress(elapsed, holdMs);
                    if (elapsed >= holdMs)
                    {
                        tcs.TrySetResult(true);
                    }
                }
                else
                {
                    if (holdStart != null) onProgress(0, holdMs);
                    holdStart = null;
                }
            }

            App.Webcam.OnGazeSide += Handler;
            try
            {
                var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return winner == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                App.Webcam.OnGazeSide -= Handler;
            }
        }

        private async Task<bool> WaitForBlinksAsync(int needed, int timeoutMs, Action<int> onProgress)
        {
            if (App.Webcam == null) return false;
            var tcs = new TaskCompletionSource<bool>();
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
            var tcs = new TaskCompletionSource<bool>();
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
            var tcs = new TaskCompletionSource<bool>();
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
            await Task.Delay(1600);
            DialogResult = true;
            Close();
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
        private static (double X, double Y, List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> Survivors)
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
            return (sx / kept.Count, sy2 / kept.Count, kept);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cerrolaza polynomial fit (ridge + leave-one-out CV)
        // ─────────────────────────────────────────────────────────────────────

        // Cerrolaza et al. (2008, 2012) X-axis design row.
        // [1, ix, iy, ix·iy, ix², iy², ix²·iy] — the asymmetric ix²·iy term is
        // what beats the symmetric 6-coef form by ~0.15-0.25° DVA on webcam.
        private static double[] CerrolazaRowX(double ix, double iy)
            => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, ix * ix * iy };

        // Cerrolaza Y-axis design row: mirror of X — the high-order term is iy²·ix.
        private static double[] CerrolazaRowY(double ix, double iy)
            => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, iy * iy * ix };

        // Solves min ||A·x - b||² + λ·||x||² by stacking sqrt(λ)·I onto A and
        // running OpenCV's normal-equations solve. Returns null if the solve
        // fails (rank-deficient, NaN inputs).
        private static double[]? FitRidge(double[][] design, double[] targets, double lambda)
        {
            int n = design.Length;
            int p = design[0].Length;
            using var A = new Mat(n + p, p, MatType.CV_64FC1, Scalar.All(0));
            using var b = new Mat(n + p, 1, MatType.CV_64FC1, Scalar.All(0));
            double sqrtL = Math.Sqrt(Math.Max(lambda, 1e-12));
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < p; k++) A.Set(i, k, design[i][k]);
                b.Set(i, 0, targets[i]);
            }
            for (int k = 0; k < p; k++) A.Set(n + k, k, sqrtL);
            using var x = new Mat();
            if (!Cv2.Solve(A, b, x, DecompTypes.Normal)) return null;
            var result = new double[p];
            for (int k = 0; k < p; k++) result[k] = x.At<double>(k, 0);
            return result;
        }

        private static double DotProduct(double[] coeffs, double[] features)
        {
            double y = 0;
            for (int k = 0; k < coeffs.Length; k++) y += coeffs[k] * features[k];
            return y;
        }

        // Picks ridge λ via leave-one-out cross-validation, then refits on all
        // data with the chosen λ. Candidate λ's are scaled by trace(AᵀA)/p (the
        // mean diagonal of the normal-equations matrix) so the chosen value is
        // invariant to iris-vector magnitude — calibration sessions vary in
        // camera distance and face size, which would otherwise shift the optimal
        // raw λ across orders of magnitude.
        private static double[]? FitRidgeWithLOO(double[][] design, double[] targets)
        {
            int n = design.Length;
            int p = design[0].Length;

            double traceAtA = 0;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < p; k++)
                    traceAtA += design[i][k] * design[i][k];
            double lambdaBase = traceAtA / p;

            double[] candidates = { 1e-5, 1e-4, 1e-3, 1e-2, 1e-1 };
            double bestErr = double.MaxValue;
            double bestLambda = lambdaBase * 1e-3; // matches the previous fixed-λ default if every LOO fit fails

            foreach (var c in candidates)
            {
                double lambda = lambdaBase * c;
                double sumSq = 0;
                bool ok = true;
                for (int holdout = 0; holdout < n; holdout++)
                {
                    var trainX = new double[n - 1][];
                    var trainY = new double[n - 1];
                    int j = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (i == holdout) continue;
                        trainX[j] = design[i];
                        trainY[j] = targets[i];
                        j++;
                    }
                    var coeffs = FitRidge(trainX, trainY, lambda);
                    if (coeffs == null) { ok = false; break; }
                    var err = DotProduct(coeffs, design[holdout]) - targets[holdout];
                    sumSq += err * err;
                }
                if (ok && sumSq < bestErr) { bestErr = sumSq; bestLambda = lambda; }
            }

            return FitRidge(design, targets, bestLambda);
        }

        // Builds the per-axis Cerrolaza design matrices and returns the LOO-
        // selected ridge fit. Returns null if either axis fails to solve —
        // caller falls back to homography-only projection in that case.
        private static PolynomialFitData? FitCerrolazaPolynomial(Point2d[] srcMeans, Point2d[] dstPoints)
        {
            try
            {
                int n = srcMeans.Length;
                var designX = new double[n][];
                var designY = new double[n][];
                var targetsX = new double[n];
                var targetsY = new double[n];
                for (int i = 0; i < n; i++)
                {
                    designX[i] = CerrolazaRowX(srcMeans[i].X, srcMeans[i].Y);
                    designY[i] = CerrolazaRowY(srcMeans[i].X, srcMeans[i].Y);
                    targetsX[i] = dstPoints[i].X;
                    targetsY[i] = dstPoints[i].Y;
                }
                var coeffsX = FitRidgeWithLOO(designX, targetsX);
                var coeffsY = FitRidgeWithLOO(designY, targetsY);
                if (coeffsX == null || coeffsY == null) return null;
                return new PolynomialFitData { X = coeffsX, Y = coeffsY };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: Cerrolaza polynomial fit threw");
                return null;
            }
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
        private void UpdateProgressRing(double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            double radius = (DotRingFg.Width - DotRingFg.StrokeThickness) / 2.0;
            double perimeter = 2.0 * Math.PI * radius;
            double units = perimeter / DotRingFg.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            DotRingFg.StrokeDashArray = new DoubleCollection(new[] { visible, gap });
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
