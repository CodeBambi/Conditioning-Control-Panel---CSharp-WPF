using System;
using System.Collections.Generic;
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
    /// polynomial (iris → screen DIPs), and persists via WebcamCalibrationData.
    ///
    /// Why 16 points: the polynomial is 6 DOF per axis — with 9 points it's
    /// 9×6 (3 redundancy), with 16 it's 16×6 (10 redundancy). The extra inner
    /// rows/columns dramatically tighten the fit at edges and corners, which
    /// is also where curved-screen geometry diverges most from a planar fit.
    ///
    /// Caller is responsible for ensuring App.Webcam is already running.
    /// </summary>
    public partial class WebcamCalibrationWindow : System.Windows.Window
    {
        private const int ReadyMs = 600;          // dot moves, user re-fixates
        private const int SampleMs = 1400;        // ~42 samples at 30fps; above MinSamplesPerPoint
        private const int SettleMs = 200;         // pause between dots
        private const int RetryReadyMs = 900;     // longer pause before retrying a missed dot
        private const int MinSamplesPerPoint = 20;
        private const int MaxAttemptsPerPoint = 2; // miss twice in a row → fail calibration

        // Sample target for the "ring is full" feedback. We treat the SampleMs
        // window's worth of frames at ~30fps as 100% — this is well above the
        // hard MinSamplesPerPoint floor, so the ring fills smoothly across the
        // window rather than maxing out almost immediately.
        private const int RingFullSampleTarget = 42;

        // Head-movement micro-phase (gaze fixed at center, head deliberately
        // moves). Anchors the head-pose comp fit with strong per-axis pose
        // variance — fixes the case where natural calibration head motion is
        // strong on yaw (sway) but weak on pitch (bob).
        private const int MovementReadyMs = 1800;
        private const int MovementSampleMs = 6000;

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
        private Storyboard? _ringPulse;
        private bool _ringIsFull;
        // Index of the dot currently being sampled. Read by OnRawIris so
        // samples land in the right per-dot bucket across retries (we can't
        // rely on "last list" because all 9 are allocated up-front).
        private int _activeDotIndex = -1;
        // Movement-phase iris+pose samples (gaze fixed, head varying).
        // Kept in a separate bucket from the per-dot samples and excluded
        // from BaselineHeadPose / pose-rejection filters.
        private readonly List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> _movementSamples = new();
        private bool _collectingMovement;

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
                return;
            }

            if (_collectingMovement)
            {
                _movementSamples.Add((dx, dy, pose?.Yaw ?? 0, pose?.Pitch ?? 0, pose.HasValue));
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
            const double margin = 90;
            // 4×4 grid, row-major. Inner rows/columns sit at 1/3 and 2/3 of the
            // usable span (between the margin-inset corners). Index layout:
            //   0  1  2  3      (top row)
            //   4  5  6  7
            //   8  9 10 11
            //  12 13 14 15      (bottom row)
            // Left column = {0,4,8,12}; right column = {3,7,11,15} —
            // referenced below when computing LeftRefVec / RightRefVec.
            double xL = margin, xR = w - margin;
            double yT = margin, yB = h - margin;
            double xML = xL + (xR - xL) / 3.0;
            double xMR = xL + 2.0 * (xR - xL) / 3.0;
            double yMU = yT + (yB - yT) / 3.0;
            double yMD = yT + 2.0 * (yB - yT) / 3.0;
            var positions = new (string Label, WpfPoint Screen)[]
            {
                ("Top-left",        new WpfPoint(xL,  yT)),
                ("Top-mid-left",    new WpfPoint(xML, yT)),
                ("Top-mid-right",   new WpfPoint(xMR, yT)),
                ("Top-right",       new WpfPoint(xR,  yT)),
                ("Upper-left",      new WpfPoint(xL,  yMU)),
                ("Upper-mid-left",  new WpfPoint(xML, yMU)),
                ("Upper-mid-right", new WpfPoint(xMR, yMU)),
                ("Upper-right",     new WpfPoint(xR,  yMU)),
                ("Lower-left",      new WpfPoint(xL,  yMD)),
                ("Lower-mid-left",  new WpfPoint(xML, yMD)),
                ("Lower-mid-right", new WpfPoint(xMR, yMD)),
                ("Lower-right",     new WpfPoint(xR,  yMD)),
                ("Bottom-left",     new WpfPoint(xL,  yB)),
                ("Bottom-mid-left", new WpfPoint(xML, yB)),
                ("Bottom-mid-right",new WpfPoint(xMR, yB)),
                ("Bottom-right",    new WpfPoint(xR,  yB)),
            };

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
            await RunHeadMovementPhaseAsync(new WpfPoint(w / 2, h / 2));
            if (_cancelled) return;

            await FinalizeCalibrationAsync(positions);
        }

        // Gaze-fixed head-movement phase. User looks at a single center dot
        // while deliberately nodding and turning their head. Provides clean
        // per-axis pose variance (especially pitch, which natural calibration
        // motion is often weak on) for the empirical head-pose comp fit.
        private async Task RunHeadMovementPhaseAsync(WpfPoint center)
        {
            StopRingPulse();
            ResetProgressRing();
            MoveDotTo(center);

            TxtProgress.Text = "Head movement check";
            TxtStatus.Text = "Look at the dot. Slowly nod your head up and down, then turn left and right. Keep your eyes on the dot.";
            await Task.Delay(MovementReadyMs);
            if (_cancelled) return;

            _collectingMovement = true;
            // Tick the ring fill as a 0→100% countdown over MovementSampleMs.
            // No pulse — this isn't a "got enough" signal, it's "keep going
            // until the ring is full".
            const int stepMs = 50;
            int elapsed = 0;
            while (elapsed < MovementSampleMs && !_cancelled)
            {
                await Task.Delay(stepMs);
                elapsed += stepMs;
                UpdateProgressRing(Math.Min(1.0, elapsed / (double)MovementSampleMs));
            }
            _collectingMovement = false;
            ResetProgressRing();
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

                double sumX = 0, sumY = 0;
                foreach (var p in s) { sumX += p.X; sumY += p.Y; }
                var mx = sumX / s.Count;
                var my = sumY / s.Count;

                // Two passes of tight inlier filtering (1.0σ). With pose
                // pre-filtering above, the surviving samples already represent
                // "head still" frames, so we can be aggressive on iris-axis
                // outliers (blinks, fixation breaks, micro-saccades). Track
                // the kept-sample list so residuals fit later uses the same
                // set as the per-dot mean.
                for (int pass = 0; pass < 2; pass++)
                {
                    double vx = 0, vy = 0;
                    foreach (var p in s) { vx += (p.X - mx) * (p.X - mx); vy += (p.Y - my) * (p.Y - my); }
                    var sx = Math.Sqrt(vx / s.Count);
                    var sy = Math.Sqrt(vy / s.Count);
                    var keptList = new List<(double, double, double, double, bool)>(s.Count);
                    double kx = 0, ky = 0;
                    foreach (var p in s)
                    {
                        if (Math.Abs(p.X - mx) <= 1.0 * sx + 1e-6 && Math.Abs(p.Y - my) <= 1.0 * sy + 1e-6)
                        {
                            kx += p.X; ky += p.Y;
                            keptList.Add(p);
                        }
                    }
                    if (keptList.Count >= MinSamplesPerPoint)
                    {
                        mx = kx / keptList.Count;
                        my = ky / keptList.Count;
                        s = keptList;
                    }
                    else break;
                }

                srcMeans[i] = new Point2d(mx, my);
                dstPoints[i] = new Point2d(positions[i].Screen.X, positions[i].Screen.Y);
                survivors[i] = s;
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

            // 2nd-order polynomial fit: N means → 12 coefficients (6 per axis).
            // Captures the nonlinear iris→screen response that the homography
            // can't, so accuracy at the edges/corners (and on curved screens)
            // matches the center much more closely. Solved via
            // Cv2.Solve(..., DecompTypes.Normal) on an overdetermined N×6
            // design matrix — N=16 gives 10× redundancy vs 6 unknowns per axis.
            PolynomialFitData? polynomial = null;
            try
            {
                int n = positions.Length;
                using var A = new Mat(n, 6, MatType.CV_64FC1);
                using var bX = new Mat(n, 1, MatType.CV_64FC1);
                using var bY = new Mat(n, 1, MatType.CV_64FC1);
                for (int i = 0; i < n; i++)
                {
                    double ix = srcMeans[i].X, iy = srcMeans[i].Y;
                    A.Set(i, 0, 1.0);
                    A.Set(i, 1, ix);
                    A.Set(i, 2, iy);
                    A.Set(i, 3, ix * ix);
                    A.Set(i, 4, iy * iy);
                    A.Set(i, 5, ix * iy);
                    bX.Set(i, 0, dstPoints[i].X);
                    bY.Set(i, 0, dstPoints[i].Y);
                }
                using var coeffsX = new Mat();
                using var coeffsY = new Mat();
                if (Cv2.Solve(A, bX, coeffsX, DecompTypes.Normal)
                 && Cv2.Solve(A, bY, coeffsY, DecompTypes.Normal))
                {
                    polynomial = new PolynomialFitData
                    {
                        X = new[]
                        {
                            coeffsX.At<double>(0, 0), coeffsX.At<double>(1, 0), coeffsX.At<double>(2, 0),
                            coeffsX.At<double>(3, 0), coeffsX.At<double>(4, 0), coeffsX.At<double>(5, 0),
                        },
                        Y = new[]
                        {
                            coeffsY.At<double>(0, 0), coeffsY.At<double>(1, 0), coeffsY.At<double>(2, 0),
                            coeffsY.At<double>(3, 0), coeffsY.At<double>(4, 0), coeffsY.At<double>(5, 0),
                        },
                    };
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationWindow: polynomial fit failed; falling back to homography only");
            }

            // LeftRefVec  = mean of left column  (indices 0, 4, 8, 12)
            // RightRefVec = mean of right column (indices 3, 7, 11, 15)
            // 4-point average per side — more robust to head-pose drift than
            // the 3-point (3×3) and 2-point (5-point) averages it replaces.
            var leftRef = new[] {
                (srcMeans[0].X + srcMeans[4].X + srcMeans[8].X + srcMeans[12].X) / 4.0,
                (srcMeans[0].Y + srcMeans[4].Y + srcMeans[8].Y + srcMeans[12].Y) / 4.0
            };
            var rightRef = new[] {
                (srcMeans[3].X + srcMeans[7].X + srcMeans[11].X + srcMeans[15].X) / 4.0,
                (srcMeans[3].Y + srcMeans[7].Y + srcMeans[11].Y + srcMeans[15].Y) / 4.0
            };

            var primary = SystemParameters.PrimaryScreenWidth;
            var primaryH = SystemParameters.PrimaryScreenHeight;

            // Reuse the session-wide head-pose mean computed up-front for the
            // sample-rejection filter — same definition: "looking forward with
            // head at rest". Skipped if the pose stream never produced enough
            // valid samples (e.g. solvePnP kept failing).
            CalibrationHeadPose? baselinePose = havePoseRef
                ? new CalibrationHeadPose { Yaw = meanYaw, Pitch = meanPitch }
                : null;

            // Empirically fit head-pose compensation coefficients from the
            // surviving samples. The model:
            //   residual_x ≈ AxYaw·sin(Δyaw) + AxPitch·sin(Δpitch)
            //   residual_y ≈ AyYaw·sin(Δyaw) + AyPitch·sin(Δpitch)
            // where residual = sample_iris - dot_mean_iris and Δ = pose - global_mean.
            // We need *some* head movement during calibration for the fit to be
            // meaningful — if the user held perfectly still, the LS fit is
            // degenerate and we discard it via the R² threshold.
            HeadPoseCompFit? headPoseComp = havePoseRef
                ? FitHeadPoseComp(survivors, srcMeans, _movementSamples, meanYaw, meanPitch)
                : null;

            var data = new WebcamCalibrationData
            {
                Mode = "SixteenPoint",
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
                BaselineHeadPose = baselinePose,
                HeadPoseComp = headPoseComp,
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
                settings.WebcamCalibrationMode = "SixteenPoint";
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
        //  Empirical head-pose compensation fit
        // ─────────────────────────────────────────────────────────────────────

        // Fit the iris-vector correction coefficients from per-sample residuals.
        // Returns null if the head moved too little during calibration to fit
        // anything meaningful (R² below threshold) — better to skip comp than
        // to apply a coefficient that's pure noise.
        private static HeadPoseCompFit? FitHeadPoseComp(
            List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>[] survivors,
            Point2d[] srcMeans,
            List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>? movementSamples,
            double globalMeanYaw,
            double globalMeanPitch)
        {
            // Gather (sin(Δyaw), sin(Δpitch), residual_x, residual_y) tuples
            // from per-dot survivors first.
            var sy = new List<double>();
            var sp = new List<double>();
            var rx = new List<double>();
            var ry = new List<double>();
            for (int i = 0; i < survivors.Length; i++)
            {
                if (survivors[i] == null) continue;
                var dotMeanX = srcMeans[i].X;
                var dotMeanY = srcMeans[i].Y;
                foreach (var p in survivors[i])
                {
                    if (!p.HasPose) continue;
                    sy.Add(Math.Sin(p.Yaw - globalMeanYaw));
                    sp.Add(Math.Sin(p.Pitch - globalMeanPitch));
                    rx.Add(p.X - dotMeanX);
                    ry.Add(p.Y - dotMeanY);
                }
            }

            // Then layer in movement-phase samples. Gaze was fixed at the
            // center dot, so all iris variance within this set is head-pose
            // driven — exactly the signal we want to fit. We compute a single
            // "iris reference" as the mean across all movement samples; with
            // roughly symmetric head motion (the prompt asks for nod up+down,
            // turn left+right), the mean approximates "iris at the
            // calibration head pose, looking at center".
            //
            // No iris-σ filter here — that would discard the very samples we
            // want. Eyes-closed frames are already gated upstream in
            // EmitGazeEvents; solvePnP failures gate the pose emit. So the
            // surviving stream is clean enough.
            int movementCount = 0;
            if (movementSamples != null && movementSamples.Count > 30)
            {
                double sumX = 0, sumY = 0;
                int n0 = 0;
                foreach (var p in movementSamples)
                {
                    if (!p.HasPose) continue;
                    sumX += p.X; sumY += p.Y; n0++;
                }
                if (n0 > 30)
                {
                    double mx = sumX / n0;
                    double my = sumY / n0;
                    foreach (var p in movementSamples)
                    {
                        if (!p.HasPose) continue;
                        sy.Add(Math.Sin(p.Yaw - globalMeanYaw));
                        sp.Add(Math.Sin(p.Pitch - globalMeanPitch));
                        rx.Add(p.X - mx);
                        ry.Add(p.Y - my);
                        movementCount++;
                    }
                }
            }

            int n = sy.Count;
            // Need a healthy sample count to fit two coefficients per axis.
            // Also need enough head-pose variance — if everyone was pixel-still,
            // the design matrix is rank-deficient. We check both via the σ of
            // the inputs and the R² of the output.
            if (n < 60) return null;

            // Compute σ of sin(Δyaw) and sin(Δpitch). If both are tiny, the
            // user held very still and there's nothing to fit. Threshold is
            // ~0.5° of effective head motion (sin(0.5°) ≈ 0.0087), below which
            // any "fit" is fitting noise.
            double meanSy = 0, meanSp = 0;
            for (int k = 0; k < n; k++) { meanSy += sy[k]; meanSp += sp[k]; }
            meanSy /= n; meanSp /= n;
            double varSy = 0, varSp = 0;
            for (int k = 0; k < n; k++)
            {
                varSy += (sy[k] - meanSy) * (sy[k] - meanSy);
                varSp += (sp[k] - meanSp) * (sp[k] - meanSp);
            }
            double sigmaSy = Math.Sqrt(varSy / n);
            double sigmaSp = Math.Sqrt(varSp / n);
            if (sigmaSy < 0.0087 && sigmaSp < 0.0087) return null;

            // Solve [sin(Δyaw) sin(Δpitch)] · [a_yaw; a_pitch] = residual,
            // separately for x and y, via OpenCV least-squares (normal equations).
            try
            {
                using var A = new Mat(n, 2, MatType.CV_64FC1);
                using var bX = new Mat(n, 1, MatType.CV_64FC1);
                using var bY = new Mat(n, 1, MatType.CV_64FC1);
                for (int k = 0; k < n; k++)
                {
                    A.Set(k, 0, sy[k]);
                    A.Set(k, 1, sp[k]);
                    bX.Set(k, 0, rx[k]);
                    bY.Set(k, 0, ry[k]);
                }
                using var coeffsX = new Mat();
                using var coeffsY = new Mat();
                if (!Cv2.Solve(A, bX, coeffsX, DecompTypes.Normal)) return null;
                if (!Cv2.Solve(A, bY, coeffsY, DecompTypes.Normal)) return null;

                double axYaw = coeffsX.At<double>(0, 0);
                double axPitch = coeffsX.At<double>(1, 0);
                double ayYaw = coeffsY.At<double>(0, 0);
                double ayPitch = coeffsY.At<double>(1, 0);

                // R² of each axis fit. If neither axis is well-explained by the
                // pose deltas (R² < 0.10), the head wasn't actually moving in a
                // way that affected gaze — skip the comp.
                double meanRx = 0, meanRy = 0;
                for (int k = 0; k < n; k++) { meanRx += rx[k]; meanRy += ry[k]; }
                meanRx /= n; meanRy /= n;
                double ssTotX = 0, ssTotY = 0, ssResX = 0, ssResY = 0;
                for (int k = 0; k < n; k++)
                {
                    double predX = axYaw * sy[k] + axPitch * sp[k];
                    double predY = ayYaw * sy[k] + ayPitch * sp[k];
                    ssTotX += (rx[k] - meanRx) * (rx[k] - meanRx);
                    ssTotY += (ry[k] - meanRy) * (ry[k] - meanRy);
                    ssResX += (rx[k] - predX) * (rx[k] - predX);
                    ssResY += (ry[k] - predY) * (ry[k] - predY);
                }
                double r2x = ssTotX > 1e-12 ? 1.0 - ssResX / ssTotX : 0.0;
                double r2y = ssTotY > 1e-12 ? 1.0 - ssResY / ssTotY : 0.0;

                // Per-axis gating. Each axis (X, Y) gets its coefficients
                // applied at runtime only if its own residuals were
                // well-explained by the pose deltas. This is the defensive
                // case for users whose natural calibration head motion is
                // strong on one axis (yaw — sway) but weak on the other
                // (pitch — bob): we keep the strong axis's correction and
                // skip the weak one rather than letting noisy coefficients
                // ride along.
                const double MinR2 = 0.10;
                bool xValid = r2x >= MinR2;
                bool yValid = r2y >= MinR2;
                if (!xValid && !yValid)
                {
                    App.Logger?.Information(
                        "WebcamCalibration: head-pose comp too weak on both axes (R²x={Rx:F3}, R²y={Ry:F3}, σyaw={Sy:F4}, σpitch={Sp:F4}, n={N}); skipping",
                        r2x, r2y, sigmaSy, sigmaSp, n);
                    return null;
                }

                double finalAxYaw = xValid ? axYaw : 0.0;
                double finalAxPitch = xValid ? axPitch : 0.0;
                double finalAyYaw = yValid ? ayYaw : 0.0;
                double finalAyPitch = yValid ? ayPitch : 0.0;

                App.Logger?.Information(
                    "WebcamCalibration: fitted head-pose comp x={Xstate} ax_yaw={AxY:F3} ax_pitch={AxP:F3} R²x={Rx:F3} | y={Ystate} ay_yaw={AyY:F3} ay_pitch={AyP:F3} R²y={Ry:F3} | σyaw={Sy:F4} σpitch={Sp:F4} n={N} (movement={M})",
                    xValid ? "ON" : "off",
                    finalAxYaw, finalAxPitch, r2x,
                    yValid ? "ON" : "off",
                    finalAyYaw, finalAyPitch, r2y,
                    sigmaSy, sigmaSp, n, movementCount);

                return new HeadPoseCompFit
                {
                    AxYaw = finalAxYaw,
                    AxPitch = finalAxPitch,
                    AyYaw = finalAyYaw,
                    AyPitch = finalAyPitch,
                    RSquaredX = r2x,
                    RSquaredY = r2y,
                    SampleCount = n,
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibration: head-pose comp fit threw");
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
