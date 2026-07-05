using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Webcam;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Webcam
{
    /// <summary>
    /// Gaze-calibration solver for the Avalonia head. Ported VERBATIM from the WPF
    /// WebcamCalibrationWindow numerical pipeline (RobustPerDotMean saccade-settle + MAD trim,
    /// weighted ridge-regularized Cerrolaza 2nd-order polynomial fit, homography, axis-correction
    /// warp, iris range). The shared CCP.Avalonia calibration window cannot reach the Windows-only
    /// tracker/data types, so it collects per-dot iris samples and hands them here through the Core
    /// IWebcamService seam; this class turns them into a WebcamCalibrationData. UI/dialog logic
    /// (redo prompts, verify phase) stays in the window/tracker; this file is pure math.
    /// </summary>
    internal static class WebcamCalibrationSolver
    {
        // Matches the WPF grid + acceptance floor. The window MUST sample a GridSize x GridSize grid.
        private const int MinSamplesPerPoint = 12;
        private const int GridSize = 4; // 4x4 = 16 calibration points

        /// <summary>
        /// Solve a WebcamCalibrationData from per-dot iris samples (Core DTO) + the calibrated screen.
        /// Returns null with <paramref name="error"/> set on any unfittable input; otherwise the fit,
        /// with per-axis training RMS (screen px) in <paramref name="rmsX"/>/<paramref name="rmsY"/>
        /// for the window's fit-quality gate. Does NOT persist or apply - the tracker seam does that.
        /// </summary>
        public static WebcamCalibrationData? BuildCalibrationData(
            IReadOnlyList<CalibrationDotSamples> dots, ScreenInfo screen, string mode,
            out double rmsX, out double rmsY, out string? error, ILogger? logger = null,
            string featureMode = "Current", string? deepModel = null)
        {
            rmsX = double.PositiveInfinity; rmsY = double.PositiveInfinity; error = null;
            int n = dots.Count;
            if (n < 4) { error = "Too few calibration points."; return null; }

            // Rebuild per-dot sample lists + the flat pose list from the DTO (mirrors the window's
            // _allSamples / _allPoseSamples that FinalizeCalibrationAsync consumed).
            var allSamples = new List<List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>>(n);
            var allPose = new List<(double Yaw, double Pitch)>();
            foreach (var d in dots)
            {
                var list = new List<(double, double, double, double, bool)>(d.Samples.Count);
                foreach (var s in d.Samples)
                {
                    list.Add((s.Dx, s.Dy, s.Yaw, s.Pitch, s.HasPose));
                    if (s.HasPose) allPose.Add((s.Yaw, s.Pitch));
                }
                allSamples.Add(list);
            }

            double meanYaw = 0, meanPitch = 0, sigmaYaw = 0, sigmaPitch = 0;
            bool havePoseRef = allPose.Count >= MinSamplesPerPoint;
            if (havePoseRef)
            {
                foreach (var p in allPose) { meanYaw += p.Yaw; meanPitch += p.Pitch; }
                meanYaw /= allPose.Count; meanPitch /= allPose.Count;
                double vy = 0, vp = 0;
                foreach (var p in allPose) { vy += (p.Yaw - meanYaw) * (p.Yaw - meanYaw); vp += (p.Pitch - meanPitch) * (p.Pitch - meanPitch); }
                sigmaYaw = Math.Sqrt(vy / allPose.Count); sigmaPitch = Math.Sqrt(vp / allPose.Count);
            }
            const double PoseFloorRad = 0.052; // ~3 degrees
            double tolYaw = Math.Max(2 * sigmaYaw, PoseFloorRad);
            double tolPitch = Math.Max(2 * sigmaPitch, PoseFloorRad);

            var srcMeans = new Point2d[n];
            var dstPoints = new Point2d[n];
            var dotSpreads = new double[n];
            for (int i = 0; i < n; i++)
            {
                List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> s;
                if (havePoseRef)
                {
                    s = new List<(double, double, double, double, bool)>(allSamples[i].Count);
                    foreach (var p in allSamples[i])
                    {
                        if (!p.HasPose) { s.Add(p); continue; }
                        if (Math.Abs(p.Yaw - meanYaw) <= tolYaw && Math.Abs(p.Pitch - meanPitch) <= tolPitch) s.Add(p);
                    }
                    if (s.Count < MinSamplesPerPoint) s = new List<(double, double, double, double, bool)>(allSamples[i]);
                }
                else s = new List<(double, double, double, double, bool)>(allSamples[i]);

                if (s.Count == 0) { error = $"No usable samples for calibration point {i + 1}."; return null; }
                var (mx, my, spread, _) = RobustPerDotMean(s);
                srcMeans[i] = new Point2d(mx, my);
                dstPoints[i] = new Point2d(dots[i].TargetX, dots[i].TargetY);
                dotSpreads[i] = spread;
            }

            double medSpread = MedianOf(dotSpreads);
            double medSpreadSq = medSpread * medSpread;
            var dotWeights = new double[n];
            for (int i = 0; i < n; i++) dotWeights[i] = 1.0 / (dotSpreads[i] * dotSpreads[i] + medSpreadSq + 1e-12);

            double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            double calW = screen.Bounds.Width / scaling;
            double calH = screen.Bounds.Height / scaling;
            double outlierFloorDip = Math.Max(calW, calH) * 0.06;

            double[][]? homography = null;
            try
            {
                using var hMat = Cv2.FindHomography(srcMeans, dstPoints);
                if (!hMat.Empty() && hMat.Rows == 3 && hMat.Cols == 3)
                {
                    homography = new double[3][];
                    for (int r = 0; r < 3; r++) { homography[r] = new double[3]; for (int c = 0; c < 3; c++) homography[r][c] = hMat.At<double>(r, c); }
                }
            }
            catch (Exception ex) { logger?.LogWarning(ex, "WebcamCalibrationSolver: FindHomography threw"); }
            if (homography == null) { error = "Couldn't fit calibration - the points may have been too similar. Look directly at each dot and try again."; return null; }

            // Tier1 ("improved-classical") uses a 3rd-order cubic ridge fit; Current/DeepModel keep the
            // 2nd-order Cerrolaza fit byte-for-byte. The FEATURE difference (roll-normalized iris for Tier1)
            // is applied upstream in the tracker, so the solver only needs the richer basis here.
            bool cubic = string.Equals(featureMode, "Tier1", StringComparison.OrdinalIgnoreCase);
            var polynomial = FitCerrolazaPolynomial(srcMeans, dstPoints, dotWeights, outlierFloorDip, logger, out rmsX, out rmsY, cubic);
            if (polynomial == null) { error = "Calibration fit failed - the iris signal was too noisy. Improve lighting, avoid glare, and try again."; return null; }

            double[] leftRef = new double[2], rightRef = new double[2];
            bool squareGrid = n == GridSize * GridSize;
            if (squareGrid)
            {
                double lx = 0, ly = 0, rx = 0, ry = 0;
                for (int i = 0; i < GridSize; i++)
                {
                    int leftIdx = i * GridSize, rightIdx = i * GridSize + (GridSize - 1);
                    lx += srcMeans[leftIdx].X; ly += srcMeans[leftIdx].Y;
                    rx += srcMeans[rightIdx].X; ry += srcMeans[rightIdx].Y;
                }
                leftRef = new[] { lx / GridSize, ly / GridSize };
                rightRef = new[] { rx / GridSize, ry / GridSize };
            }

            var axisCorrection = squareGrid ? BuildAxisCorrection(polynomial, srcMeans, dstPoints, logger) : null;

            double irMinX = double.MaxValue, irMaxX = double.MinValue, irMinY = double.MaxValue, irMaxY = double.MinValue;
            foreach (var m in srcMeans)
            {
                if (m.X < irMinX) irMinX = m.X; if (m.X > irMaxX) irMaxX = m.X;
                if (m.Y < irMinY) irMinY = m.Y; if (m.Y > irMaxY) irMaxY = m.Y;
            }

            return new WebcamCalibrationData
            {
                Mode = string.IsNullOrEmpty(mode) ? "SixteenPoint" : mode,
                FeatureMode = string.IsNullOrEmpty(featureMode) ? "Current" : featureMode,
                DeepModel = deepModel,
                Timestamp = DateTime.UtcNow,
                MonitorBounds = new MonitorBoundsRecord
                {
                    Width = (int)calW, Height = (int)calH, DpiScale = scaling,
                    DeviceName = screen.Name, X = (int)screen.Bounds.X, Y = (int)screen.Bounds.Y,
                },
                PrimaryDeviceId = "",
                LeftRefVec = leftRef, RightRefVec = rightRef,
                Homography = homography, Polynomial = polynomial,
                BaselineHeadPose = null, HeadPoseComp = null,
                IrisRange = new IrisRangeData { MinX = irMinX, MaxX = irMaxX, MinY = irMinY, MaxY = irMaxY },
                AxisCorrection = axisCorrection,
            };
        }

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

        // Full 3rd-order (cubic) design row for the Tier-1 improved-classical fit. Symmetric (same
        // basis for X and Y); a strict superset of the Cerrolaza 2nd-order terms plus ix³/iy³ and
        // BOTH mixed cubics. The order is CANONICAL and MUST match EvalPolynomial's length-10 branch
        // below AND WebcamTrackingService.ProjectGazeToScreen's length-10 branch:
        //   [1, ix, iy, ix², ix·iy, iy², ix³, ix²·iy, ix·iy², iy³]
        private static double[] CubicRow(double ix, double iy)
        {
            double ix2 = ix * ix, iy2 = iy * iy, ixy = ix * iy;
            return new[] { 1.0, ix, iy, ix2, ixy, iy2, ix2 * ix, ix2 * iy, ix * iy2, iy2 * iy };
        }

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
            Point2d[] srcMeans, Point2d[] dstPoints, double[] dotWeights, double outlierFloorDip, ILogger? logger,
            out double rmsX, out double rmsY, bool cubic = false)
        {
            // Default to "unusable" so the fit-quality gate fails closed on any path
            // that returns null (degenerate solve, exception) without a real residual.
            rmsX = double.PositiveInfinity;
            rmsY = double.PositiveInfinity;
            try
            {
                int n = srcMeans.Length;
                int p = cubic ? 10 : 7;
                var designX = new double[n][];
                var designY = new double[n][];
                var targetsX = new double[n];
                var targetsY = new double[n];
                double traceAtA = 0;
                for (int i = 0; i < n; i++)
                {
                    designX[i] = cubic ? CubicRow(srcMeans[i].X, srcMeans[i].Y) : CerrolazaRowX(srcMeans[i].X, srcMeans[i].Y);
                    designY[i] = cubic ? CubicRow(srcMeans[i].X, srcMeans[i].Y) : CerrolazaRowY(srcMeans[i].X, srcMeans[i].Y);
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
                    // margin over the fit's p coefficients for the refit to stay
                    // sane): p+5 → 12 for the 7-coef Cerrolaza fit (unchanged),
                    // 15 for the 10-coef cubic.
                    if (mags.Count < p + 5) break;

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
                logger?.LogInformation(
                    "WebcamCalibration: polynomial fit n={N} used={Used} dropped={Dropped} λ={L:E2} | rms_x={Rx:F1} rms_y={Ry:F1} | max_x={Mx:F1}@{Wx} max_y={My:F1}@{Wy}{Rows} (DIPs)",
                    n, used, droppedStr, lambda, rmsX, rmsY, maxX, worstIdxX, maxY, worstIdxY, rowSummary);

                return new PolynomialFitData { X = coeffsX, Y = coeffsY };
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "WebcamCalibrationWindow: Cerrolaza polynomial fit threw");
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
        private static (double X, double Y) EvalPolynomial(PolynomialFitData poly, double ix, double iy)
        {
            double ix2 = ix * ix, iy2 = iy * iy, ixy = ix * iy;
            if (poly.X.Length == 10 && poly.Y.Length == 10)
            {
                double ix3 = ix2 * ix, iy3 = iy2 * iy;
                double xc = poly.X[0] + poly.X[1] * ix + poly.X[2] * iy + poly.X[3] * ix2 + poly.X[4] * ixy + poly.X[5] * iy2
                          + poly.X[6] * ix3 + poly.X[7] * ix2 * iy + poly.X[8] * ix * iy2 + poly.X[9] * iy3;
                double yc = poly.Y[0] + poly.Y[1] * ix + poly.Y[2] * iy + poly.Y[3] * ix2 + poly.Y[4] * ixy + poly.Y[5] * iy2
                          + poly.Y[6] * ix3 + poly.Y[7] * ix2 * iy + poly.Y[8] * ix * iy2 + poly.Y[9] * iy3;
                return (xc, yc);
            }
            double x = poly.X[0] + poly.X[1] * ix + poly.X[2] * iy
                     + poly.X[3] * ixy + poly.X[4] * ix2 + poly.X[5] * iy2
                     + poly.X[6] * ix2 * iy;
            double y = poly.Y[0] + poly.Y[1] * ix + poly.Y[2] * iy
                     + poly.Y[3] * ixy + poly.Y[4] * ix2 + poly.Y[5] * iy2
                     + poly.Y[6] * iy2 * ix;
            return (x, y);
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
            PolynomialFitData? poly, Point2d[] srcMeans, Point2d[] dstPoints, ILogger? logger)
        {
            if (poly == null
                || (poly.X.Length != 7 && poly.X.Length != 10)
                || (poly.Y.Length != 7 && poly.Y.Length != 10)) return null;
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
                    logger?.LogInformation("WebcamCalibration: axis correction skipped — anchors unusable on both axes");
                    return null;
                }
                // A dead axis keeps identity anchors so the runtime code path
                // stays uniform.
                if (!xOk) { srcX = (double[])dstX.Clone(); }
                if (!yOk) { srcY = (double[])dstY.Clone(); }

                logger?.LogInformation(
                    "WebcamCalibration: axis correction built | rowY Δ=[{Y0:F0},{Y1:F0},{Y2:F0},{Y3:F0}] colX Δ=[{X0:F0},{X1:F0},{X2:F0},{X3:F0}] (DIPs, dst−src){Note}",
                    dstY[0] - srcY[0], dstY[1] - srcY[1], dstY[2] - srcY[2], dstY[3] - srcY[3],
                    dstX[0] - srcX[0], dstX[1] - srcX[1], dstX[2] - srcX[2], dstX[3] - srcX[3],
                    xOk && yOk ? "" : (xOk ? " [Y axis identity]" : " [X axis identity]"));

                return new AxisCorrectionData { SrcX = srcX, DstX = dstX, SrcY = srcY, DstY = dstY };
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "WebcamCalibrationWindow: axis correction build threw");
                return null;
            }
        }
    }
}
