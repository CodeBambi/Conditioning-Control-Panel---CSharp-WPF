using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Fullscreen 9-point gaze calibration (3×3 grid: TL, TC, TR, ML, MC, MR,
    /// BL, BC, BR). Samples raw iris vectors at each point, fits a 3×3
    /// homography (iris → screen DIPs), and persists via WebcamCalibrationData.
    ///
    /// Why 9 points: the homography is 8 DOF — with 5 points we're at the bare
    /// minimum and the fit floats. With 9 we get a real least-squares fit, which
    /// noticeably tightens accuracy at the screen edges where 5-point drifts
    /// most. Per-point sampling time is shorter to keep total calibration time
    /// roughly the same as the 5-point version.
    ///
    /// Caller is responsible for ensuring App.Webcam is already running.
    /// </summary>
    public partial class WebcamCalibrationWindow : System.Windows.Window
    {
        private const int ReadyMs = 600;          // dot moves, user re-fixates
        private const int SampleMs = 1400;        // ~42 samples at 30fps; above MinSamplesPerPoint
        private const int SettleMs = 200;         // pause between dots
        private const int MinSamplesPerPoint = 20;

        private readonly List<List<(double X, double Y)>> _allSamples = new();
        private bool _collecting;
        private bool _cancelled;
        private bool _completedOk;

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
            if (App.Webcam != null) App.Webcam.OnRawIris -= OnRawIris;
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
            if (!_collecting) return;
            _allSamples[_allSamples.Count - 1].Add((dx, dy));
        }

        private async Task RunSequenceAsync()
        {
            // Wait one frame for the window to fully lay out so ActualWidth/Height are valid.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            var w = ActualWidth;
            var h = ActualHeight;
            const double margin = 90;
            // 3×3 grid in row-major order. Index layout:
            //   0=TL  1=TC  2=TR
            //   3=ML  4=MC  5=MR
            //   6=BL  7=BC  8=BR
            // (Used downstream when picking left-column / right-column points
            // for the LeftRefVec / RightRefVec averages.)
            var positions = new (string Label, WpfPoint Screen)[]
            {
                ("Top-left",      new WpfPoint(margin,     margin)),
                ("Top-center",    new WpfPoint(w / 2,      margin)),
                ("Top-right",     new WpfPoint(w - margin, margin)),
                ("Middle-left",   new WpfPoint(margin,     h / 2)),
                ("Center",        new WpfPoint(w / 2,      h / 2)),
                ("Middle-right",  new WpfPoint(w - margin, h / 2)),
                ("Bottom-left",   new WpfPoint(margin,     h - margin)),
                ("Bottom-center", new WpfPoint(w / 2,      h - margin)),
                ("Bottom-right",  new WpfPoint(w - margin, h - margin)),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                if (_cancelled) return;

                _allSamples.Add(new List<(double, double)>());

                MoveDotTo(positions[i].Screen);
                TxtProgress.Text = $"Point {i + 1} / {positions.Length}  ({positions[i].Label})";
                TxtStatus.Text = "Look at the orange dot…";
                await Task.Delay(ReadyMs);
                if (_cancelled) return;

                TxtStatus.Text = "Hold steady — sampling…";
                _collecting = true;
                await Task.Delay(SampleMs);
                _collecting = false;
                if (_cancelled) return;

                if (_allSamples[i].Count < MinSamplesPerPoint)
                {
                    ShowError(
                        $"Couldn't sample point {i + 1} ({positions[i].Label}). " +
                        $"Got {_allSamples[i].Count} samples (need at least {MinSamplesPerPoint}). " +
                        "Make sure you're well-lit, facing the camera, and your face fits in frame.");
                    return;
                }

                await Task.Delay(SettleMs);
            }

            await FinalizeCalibrationAsync(positions);
        }

        private async Task FinalizeCalibrationAsync((string Label, WpfPoint Screen)[] positions)
        {
            // Average each point's iris samples (trim wild outliers via simple
            // mean-then-mean-of-inliers — good enough for the prototype).
            var srcMeans = new Point2d[positions.Length];
            var dstPoints = new Point2d[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                var s = _allSamples[i];
                double sumX = 0, sumY = 0;
                foreach (var p in s) { sumX += p.X; sumY += p.Y; }
                var mx = sumX / s.Count;
                var my = sumY / s.Count;

                // Two passes of tight inlier filtering (1.5σ). With ~75 samples
                // per point at 30 fps × 2.5s, we can afford to be aggressive
                // about throwing out frames where the user blinked or shifted
                // their head mid-sample.
                for (int pass = 0; pass < 2; pass++)
                {
                    double vx = 0, vy = 0;
                    foreach (var p in s) { vx += (p.X - mx) * (p.X - mx); vy += (p.Y - my) * (p.Y - my); }
                    var sx = Math.Sqrt(vx / s.Count);
                    var sy = Math.Sqrt(vy / s.Count);
                    int kept = 0; double kx = 0, ky = 0;
                    foreach (var p in s)
                    {
                        if (Math.Abs(p.X - mx) <= 1.5 * sx + 1e-6 && Math.Abs(p.Y - my) <= 1.5 * sy + 1e-6)
                        {
                            kx += p.X; ky += p.Y; kept++;
                        }
                    }
                    if (kept >= MinSamplesPerPoint) { mx = kx / kept; my = ky / kept; }
                    else break;
                }

                srcMeans[i] = new Point2d(mx, my);
                dstPoints[i] = new Point2d(positions[i].Screen.X, positions[i].Screen.Y);
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

            // LeftRefVec  = mean of left column (TL=0, ML=3, BL=6) iris vectors
            // RightRefVec = mean of right column (TR=2, MR=5, BR=8) iris vectors
            // 3-point average per side is more robust to head-pose drift than
            // the old 2-point (TL+BL / TR+BR) average from the 5-point flow.
            var leftRef = new[] {
                (srcMeans[0].X + srcMeans[3].X + srcMeans[6].X) / 3.0,
                (srcMeans[0].Y + srcMeans[3].Y + srcMeans[6].Y) / 3.0
            };
            var rightRef = new[] {
                (srcMeans[2].X + srcMeans[5].X + srcMeans[8].X) / 3.0,
                (srcMeans[2].Y + srcMeans[5].Y + srcMeans[8].Y) / 3.0
            };

            var primary = SystemParameters.PrimaryScreenWidth;
            var primaryH = SystemParameters.PrimaryScreenHeight;

            var data = new WebcamCalibrationData
            {
                Mode = "NinePoint",
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
                settings.WebcamCalibrationMode = "NinePoint";
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

            // Sequence: L, R, L, R, blink×2, mouth-open×1, tongue-out×1.
            // Each step gets up to 3 attempts (12 s timeout each) before failing
            // the whole calibration.
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
            if (!await ValidateMouthOpenStepAsync(needed: 1)) return false;
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

        private void MoveDotTo(WpfPoint screenPoint)
        {
            System.Windows.Controls.Canvas.SetLeft(Dot, screenPoint.X - Dot.Width / 2);
            System.Windows.Controls.Canvas.SetTop(Dot, screenPoint.Y - Dot.Height / 2);
        }

        private void ShowError(string detail)
        {
            _collecting = false;
            DotCanvas.Visibility = Visibility.Collapsed;
            TxtErrorDetail.Text = detail;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
