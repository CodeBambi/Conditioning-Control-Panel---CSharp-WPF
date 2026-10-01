using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The 16-point calibration maths, moved verbatim out of WebcamCalibrationWindow.xaml.cs so the
    /// WPF and Avalonia windows run one fit: grid layout, pose-gated per-dot robust means, inverse-
    /// spread weights, homography, the ridge Cerrolaza polynomial with outlier-dot rejection, the
    /// left/right reference vectors, iris range and the axis residual correction. Pure: no window,
    /// no camera. Callers fill MonitorBounds' identity (DeviceName, X, Y, DpiScale) themselves.
    /// </summary>
    public static class WebcamCalibrationFit
    {
        public const int MinSamplesPerPoint = 12;          // surviving iris samples to accept a dot (#382/#385)
        public const int GridSize = 4;                     // 4×4 = 16 points (corners + interior)
        public const double EdgeMargin = 40;               // screen edge to corner dots (DIPs)
        public const double MaxFitResidualFraction = 0.20; // fit-quality gate (#335): rms above this share of the screen

        /// <summary>The 4×4 dot layout, row-major: 0..3 top row, 12..15 bottom row; left column
        /// {0,4,8,12}, right column {3,7,11,15}.</summary>
        public static (string Label, Point2d Screen)[] BuildGrid(double w, double h)
        {
            double xL = EdgeMargin, xR = w - EdgeMargin;
            double yT = EdgeMargin, yB = h - EdgeMargin;
            string[] rowLabels = { "Top", "Upper", "Lower", "Bottom" };
            string[] colLabels = { "left", "mid-left", "mid-right", "right" };
            var positions = new (string Label, Point2d Screen)[GridSize * GridSize];
            for (int r = 0; r < GridSize; r++)
            {
                double y = yT + (yB - yT) * (r / (double)(GridSize - 1));
                for (int c = 0; c < GridSize; c++)
                {
                    double x = xL + (xR - xL) * (c / (double)(GridSize - 1));
                    positions[r * GridSize + c] = ($"{rowLabels[r]}-{colLabels[c]}", new Point2d(x, y));
                }
            }
            return positions;
        }

        /// <summary>Result of <see cref="Fit"/>. <see cref="Data"/> is null when no homography could be
        /// fitted (the caller shows its error); <see cref="TooInaccurate"/> drives the redo prompt.</summary>
        public sealed record Result(WebcamCalibrationData? Data, double RmsX, double RmsY, bool TooInaccurate);

        /// <summary>FinalizeCalibrationAsync's maths: per-dot samples (iris + head pose at that frame) and
        /// the session's pose samples to a "SixteenPoint" calibration in DIPs of a w×h window.</summary>
        public static Result Fit(
            IReadOnlyList<List<(double X, double Y, double Yaw, double Pitch, bool HasPose)>> samples,
            IReadOnlyList<(double Yaw, double Pitch)> poseSamples,
            Point2d[] dstPoints, double w, double h)
        {
            int n = dstPoints.Length;
            // Session-wide head-pose mean + σ: iris samples whose pose drifted beyond max(2σ, 3°) carry
            // the wrong head-relative frame and are dropped (unless that starves the dot).
            double meanYaw = 0, meanPitch = 0, sigmaYaw = 0, sigmaPitch = 0;
            bool havePoseRef = poseSamples.Count >= MinSamplesPerPoint;
            if (havePoseRef)
            {
                meanYaw = poseSamples.Average(p => p.Yaw);
                meanPitch = poseSamples.Average(p => p.Pitch);
                sigmaYaw = Math.Sqrt(poseSamples.Average(p => (p.Yaw - meanYaw) * (p.Yaw - meanYaw)));
                sigmaPitch = Math.Sqrt(poseSamples.Average(p => (p.Pitch - meanPitch) * (p.Pitch - meanPitch)));
            }
            const double PoseFloorRad = 0.052; // ~3°
            double tolYaw = Math.Max(2 * sigmaYaw, PoseFloorRad);
            double tolPitch = Math.Max(2 * sigmaPitch, PoseFloorRad);

            var srcMeans = new Point2d[n];
            var dotSpreads = new double[n];
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                if (havePoseRef)
                {
                    var f = s.Where(p => !p.HasPose || (Math.Abs(p.Yaw - meanYaw) <= tolYaw && Math.Abs(p.Pitch - meanPitch) <= tolPitch)).ToList();
                    if (f.Count >= MinSamplesPerPoint) s = f;
                }
                var (mx, my, spread, _) = RobustPerDotMean(s);
                srcMeans[i] = new Point2d(mx, my);
                dotSpreads[i] = spread;
            }

            // Inverse-spread weights scaled by the median spread: steady dots pull the fit ~2× harder,
            // scattered ones trend to 0.
            double medSpread = MedianOf(dotSpreads);
            double medSpreadSq = medSpread * medSpread;
            var dotWeights = new double[n];
            for (int i = 0; i < n; i++)
                dotWeights[i] = 1.0 / (dotSpreads[i] * dotSpreads[i] + medSpreadSq + 1e-12);
            // A dot is only an outlier candidate if its residual also exceeds 6% of the screen.
            double outlierFloorDip = Math.Max(w, h) * 0.06;

            double[][]? homography = null;
            try
            {
                using var hMat = Cv2.FindHomography(srcMeans, dstPoints);
                if (!hMat.Empty() && hMat.Rows == 3 && hMat.Cols == 3)
                    homography = Enumerable.Range(0, 3).Select(r => Enumerable.Range(0, 3).Select(c => hMat.At<double>(r, c)).ToArray()).ToArray();
            }
            catch (Exception ex) { Log.Warning(ex, "WebcamCalibrationWindow: FindHomography threw"); }
            if (homography == null) return new Result(null, double.PositiveInfinity, double.PositiveInfinity, false);

            var polynomial = FitCerrolazaPolynomial(srcMeans, dstPoints, dotWeights, outlierFloorDip, out double rmsX, out double rmsY);
            bool tooInaccurate = polynomial != null
                && (rmsX > w * MaxFitResidualFraction || rmsY > h * MaxFitResidualFraction);

            // Left/right column averages feed ClassifyGazeSide.
            double lx = 0, ly = 0, rx = 0, ry = 0;
            for (int i = 0; i < GridSize; i++)
            {
                int leftIdx = i * GridSize, rightIdx = i * GridSize + (GridSize - 1);
                lx += srcMeans[leftIdx].X; ly += srcMeans[leftIdx].Y;
                rx += srcMeans[rightIdx].X; ry += srcMeans[rightIdx].Y;
            }

            var data = new WebcamCalibrationData
            {
                Mode = "SixteenPoint",
                Timestamp = DateTime.UtcNow,
                // The window's own size, not the primary screen's: the dots were placed against it.
                MonitorBounds = new MonitorBoundsRecord { Width = (int)w, Height = (int)h },
                PrimaryDeviceId = "",
                LeftRefVec = new[] { lx / GridSize, ly / GridSize },
                RightRefVec = new[] { rx / GridSize, ry / GridSize },
                Homography = homography,
                Polynomial = polynomial,
                // Head-pose comp retired: new calibrations write null so no comp ever engages.
                BaselineHeadPose = null,
                HeadPoseComp = null,
                IrisRange = new IrisRangeData
                {
                    MinX = srcMeans.Min(m => m.X), MaxX = srcMeans.Max(m => m.X),
                    MinY = srcMeans.Min(m => m.Y), MaxY = srcMeans.Max(m => m.Y),
                },
                AxisCorrection = BuildAxisCorrection(polynomial, srcMeans, dstPoints),
            };
            return new Result(data, rmsX, rmsY, tooInaccurate);
        }

        // Saccade-settle drop (first ~210ms, Salvucci & Goldberg I-DT 2000) then median ± 3·MAD/0.6745
        // trim; returns the mean, RMS spread and the surviving samples.
        internal static (double X, double Y, double Spread, List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> Survivors)
            RobustPerDotMean(List<(double X, double Y, double Yaw, double Pitch, bool HasPose)> samples)
        {
            const int SaccadeSettleSamples = 7;          // ~210ms @ 30fps
            const double MadCutoff = 3.0;
            const double MadToSigmaScale = 1.0 / 0.6745;

            int start = (samples.Count - SaccadeSettleSamples >= MinSamplesPerPoint) ? SaccadeSettleSamples : 0;
            var trimmed = (start == 0) ? samples : samples.GetRange(start, samples.Count - start);

            var xs = trimmed.Select(p => p.X).OrderBy(v => v).ToList();
            var ys = trimmed.Select(p => p.Y).OrderBy(v => v).ToList();
            double medX = xs[xs.Count / 2];
            double medY = ys[ys.Count / 2];
            var devX = trimmed.Select(p => Math.Abs(p.X - medX)).OrderBy(v => v).ToList();
            var devY = trimmed.Select(p => Math.Abs(p.Y - medY)).OrderBy(v => v).ToList();
            double madX = devX[devX.Count / 2];
            double madY = devY[devY.Count / 2];

            var kept = trimmed;   // degenerate spread (perfectly stable iris): nothing to filter
            if (madX > 1e-9 || madY > 1e-9)
            {
                double thrX = MadCutoff * madX * MadToSigmaScale + 1e-9;
                double thrY = MadCutoff * madY * MadToSigmaScale + 1e-9;
                kept = trimmed.Where(p => Math.Abs(p.X - medX) <= thrX && Math.Abs(p.Y - medY) <= thrY).ToList();
                if (kept.Count < MinSamplesPerPoint) kept = trimmed;   // never starve the fit
            }

            double meanX = kept.Average(p => p.X), meanY = kept.Average(p => p.Y);
            double spread = Math.Sqrt(kept.Average(p => (p.X - meanX) * (p.X - meanX) + (p.Y - meanY) * (p.Y - meanY)));
            return (meanX, meanY, spread, kept);
        }

        // Cerrolaza et al. (2008, 2012) asymmetric design rows: ix²·iy on X, iy²·ix on Y.
        private static double[] CerrolazaRowX(double ix, double iy) => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, ix * ix * iy };
        private static double[] CerrolazaRowY(double ix, double iy) => new[] { 1.0, ix, iy, ix * iy, ix * ix, iy * iy, iy * iy * ix };

        // Ridge λ scaled by trace(AᵀA)/p: essentially no shrinkage, just numerical stability. (LOO-CV
        // and heavier fixed λ compressed the cursor's reach at the edges.)
        private const double RidgeLambdaScale = 1e-5;

        // min ||A·x - b||² + λ·||x||² with per-row weights (row scaled by sqrt(w)); null on a failed
        // solve or NaN/Infinity coefficients (which would otherwise reach Window.Left and throw).
        private static double[]? FitRidge(double[][] design, double[] targets, double lambda, double[]? weights = null)
        {
            int n = design.Length;
            int p = design[0].Length;
            using var A = new Mat(n + p, p, MatType.CV_64FC1, Scalar.All(0));
            using var b = new Mat(n + p, 1, MatType.CV_64FC1, Scalar.All(0));
            double sqrtL = Math.Sqrt(Math.Max(lambda, 1e-12));
            for (int i = 0; i < n; i++)
            {
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

        // Weighted ridge Cerrolaza fit per axis, then up to two outlier dots (residual > median + 3·MAD
        // AND > outlierFloorDip) dropped and refitted once. rms over the dots used; +∞ on failure so
        // the fit-quality gate fails closed.
        internal static PolynomialFitData? FitCerrolazaPolynomial(
            Point2d[] srcMeans, Point2d[] dstPoints, double[] dotWeights, double outlierFloorDip,
            out double rmsX, out double rmsY)
        {
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
                    for (int k = 0; k < p; k++) traceAtA += designX[i][k] * designX[i][k];
                }
                double lambda = RidgeLambdaScale * traceAtA / p;

                var w = (double[])dotWeights.Clone();
                double[]? coeffsX = null, coeffsY = null;
                var dropped = new List<int>();
                for (int pass = 0; pass < 2; pass++)
                {
                    coeffsX = FitRidge(designX, targetsX, lambda, w);
                    coeffsY = FitRidge(designY, targetsY, lambda, w);
                    if (coeffsX == null || coeffsY == null) return null;
                    if (pass == 1) break;

                    var mags = new List<(int Idx, double R)>();
                    for (int i = 0; i < n; i++)
                    {
                        if (w[i] <= 0) continue;
                        var ex = DotProduct(coeffsX, designX[i]) - targetsX[i];
                        var ey = DotProduct(coeffsY, designY[i]) - targetsY[i];
                        mags.Add((i, Math.Sqrt(ex * ex + ey * ey)));
                    }
                    if (mags.Count < 12) break;   // don't trim a small grid into instability

                    var rs = mags.Select(m => m.R).OrderBy(v => v).ToList();
                    double med = rs[rs.Count / 2];
                    var devs = rs.Select(v => Math.Abs(v - med)).OrderBy(v => v).ToList();
                    double thr = med + 3.0 * devs[devs.Count / 2] / 0.6745;
                    var cand = mags.Where(m => m.R > thr && m.R > outlierFloorDip).OrderByDescending(m => m.R).Take(2).ToList();
                    if (cand.Count == 0) break;
                    foreach (var c in cand) { w[c.Idx] = 0; dropped.Add(c.Idx); }
                }

                double ssX = 0, ssY = 0, maxX = 0, maxY = 0;
                int worstIdxX = -1, worstIdxY = -1, used = 0;
                var residualsY = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var ex = DotProduct(coeffsX!, designX[i]) - targetsX[i];
                    var ey = DotProduct(coeffsY!, designY[i]) - targetsY[i];
                    residualsY[i] = ey;
                    if (w[i] <= 0) continue;   // dropped outlier: excluded from rms/max
                    used++;
                    ssX += ex * ex; ssY += ey * ey;
                    if (Math.Abs(ex) > maxX) { maxX = Math.Abs(ex); worstIdxX = i; }
                    if (Math.Abs(ey) > maxY) { maxY = Math.Abs(ey); worstIdxY = i; }
                }
                if (used == 0) return null;

                // Per-row mean signed / mean absolute Y residual, top to bottom, on a square grid.
                int rowSize = (int)Math.Round(Math.Sqrt(n));
                string rowSummary = "";
                if (rowSize * rowSize == n)
                {
                    rowSummary = " | rows_y(mean/|abs|): " + string.Join(" ", Enumerable.Range(0, rowSize).Select(r =>
                    {
                        var row = residualsY.Skip(r * rowSize).Take(rowSize).ToArray();
                        return $"r{r}={row.Sum() / rowSize:+0;-0;0}/|{row.Sum(Math.Abs) / rowSize:F0}|";
                    }));
                }

                rmsX = Math.Sqrt(ssX / used);
                rmsY = Math.Sqrt(ssY / used);
                Log.Information(
                    "WebcamCalibration: polynomial fit n={N} used={Used} dropped={Dropped} λ={L:E2} | rms_x={Rx:F1} rms_y={Ry:F1} | max_x={Mx:F1}@{Wx} max_y={My:F1}@{Wy}{Rows} (DIPs)",
                    n, used, dropped.Count == 0 ? "none" : string.Join(",", dropped), lambda, rmsX, rmsY, maxX, worstIdxX, maxY, worstIdxY, rowSummary);
                return new PolynomialFitData { X = coeffsX!, Y = coeffsY! };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebcamCalibrationWindow: Cerrolaza polynomial fit threw");
                return null;
            }
        }

        // Piecewise-linear post-polynomial warp from the grid's own rows/columns: where the polynomial
        // put each row (column) on average vs where it is. Null when the polynomial is missing or both
        // axes fold/crush space; a single bad axis keeps identity anchors.
        internal static AxisCorrectionData? BuildAxisCorrection(PolynomialFitData? poly, Point2d[] srcMeans, Point2d[] dstPoints)
        {
            if (poly == null || poly.X.Length != 7 || poly.Y.Length != 7) return null;
            const double MinGain = 0.4, MaxGain = 3.0;
            try
            {
                var bare = new WebcamCalibrationData { Polynomial = poly };   // no AxisCorrection: the raw polynomial
                int n = GridSize;
                var srcY = new double[n]; var dstY = new double[n];
                var srcX = new double[n]; var dstX = new double[n];
                for (int a = 0; a < n; a++)
                {
                    double projY = 0, trueY = 0, projX = 0, trueX = 0;
                    for (int b = 0; b < n; b++)
                    {
                        int row = a * n + b, col = b * n + a;
                        projY += GazeEngine.Project(bare, srcMeans[row].X, srcMeans[row].Y)!.Value.Y;
                        trueY += dstPoints[row].Y;
                        projX += GazeEngine.Project(bare, srcMeans[col].X, srcMeans[col].Y)!.Value.X;
                        trueX += dstPoints[col].X;
                    }
                    srcY[a] = projY / n; dstY[a] = trueY / n;
                    srcX[a] = projX / n; dstX[a] = trueX / n;
                }

                static bool Usable(double[] src, double[] dst)
                {
                    for (int i = 1; i < src.Length; i++)
                    {
                        double ds = src[i] - src[i - 1], dd = dst[i] - dst[i - 1];
                        if (ds < 1.0 || dd < 1.0) return false;
                        double gain = dd / ds;
                        if (gain < MinGain || gain > MaxGain) return false;
                    }
                    return true;
                }

                bool xOk = Usable(srcX, dstX), yOk = Usable(srcY, dstY);
                if (!xOk && !yOk)
                {
                    Log.Information("WebcamCalibration: axis correction skipped — anchors unusable on both axes");
                    return null;
                }
                if (!xOk) srcX = (double[])dstX.Clone();
                if (!yOk) srcY = (double[])dstY.Clone();
                Log.Information(
                    "WebcamCalibration: axis correction built | rowY Δ=[{Y0:F0},{Y1:F0},{Y2:F0},{Y3:F0}] colX Δ=[{X0:F0},{X1:F0},{X2:F0},{X3:F0}] (DIPs, dst−src){Note}",
                    dstY[0] - srcY[0], dstY[1] - srcY[1], dstY[2] - srcY[2], dstY[3] - srcY[3],
                    dstX[0] - srcX[0], dstX[1] - srcX[1], dstX[2] - srcX[2], dstX[3] - srcX[3],
                    xOk && yOk ? "" : (xOk ? " [Y axis identity]" : " [X axis identity]"));
                return new AxisCorrectionData { SrcX = srcX, DstX = dstX, SrcY = srcY, DstY = dstY };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebcamCalibrationWindow: axis correction build threw");
                return null;
            }
        }
    }
}
