using System;
using System.Collections.Generic;
using System.Diagnostics;
using ConditioningControlPanel.Models;
using OpenCvSharp;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The frame-free gaze half of WebcamTrackingService, shared by both heads: eye-corner
    /// normalisation, the two-eye consistency gate, the median/rolling-mean/One-Euro smoothing split,
    /// gaze-side stability, projection through the calibration (polynomial or homography, axis
    /// correction, quick-recal offset, bubble trim, soft edges), the cursor follower, face-lost
    /// counting and solvePnP head pose. Numbers in, numbers out; one instance per tracker, capture
    /// thread only. The tuning history behind every constant lives in WebcamTrackingService.cs.
    /// </summary>
    public sealed class GazeEngine
    {
        public const int FaceLostFramesThreshold = 15;      // ~0.5s
        private const int IrisSmoothFrames = 12;
        private const int SideStabilityFrames = 3;
        private const double OneEuroMinCutoff = 1.4, OneEuroBeta = 0.007, OneEuroDCutoff = 1.0;
        private const int EyeRefSmoothFrames = 6;
        private const int IrisMedianFrames = 3;             // do not drop to 2: see WebcamTrackingService
        private const double ScreenOneEuroMinCutoff = 1.5, ScreenOneEuroBetaDefault = 0.06;
        private const int EyeDisagreementBaselineFrames = 90, EyeDisagreementMinSamples = 15;
        private const double EyeDisagreementRatio = 3.0, EyeDisagreementFloor = 0.04;
        private const int TwoEyeStreakRequired = 15, MaxGateSkipFrames = 8;
        private const double FollowMinDefault = 0.22, FollowMaxDefault = 0.48, RampDistDefault = 360.0;
        private const double IrisClampMarginFrac = 0.35;

        private readonly Queue<double> _irisDxSmooth = new(), _irisDySmooth = new();
        private readonly OneEuroFilter _irisDxFilter = new(OneEuroMinCutoff, OneEuroBeta, OneEuroDCutoff);
        private readonly OneEuroFilter _irisDyFilter = new(OneEuroMinCutoff, OneEuroBeta, OneEuroDCutoff);
        private readonly Queue<double> _lCx = new(), _lCy = new(), _lW = new(), _rCx = new(), _rCy = new(), _rW = new();
        private readonly Queue<double> _dxMedian = new(), _dyMedian = new();
        private readonly OneEuroFilter _screenX = new(ScreenOneEuroMinCutoff, ScreenOneEuroBetaDefault, OneEuroDCutoff);
        private readonly OneEuroFilter _screenY = new(ScreenOneEuroMinCutoff, ScreenOneEuroBetaDefault, OneEuroDCutoff);
        private readonly Queue<double> _disagreement = new();
        private int _twoEyeStreak, _gateSkipStreak;
        private GazeSide _lastGazeSide = GazeSide.Center, _lastEmittedSide = GazeSide.Center, _pendingSide = GazeSide.Center;
        private int _pendingSideStreak;
        private double _followX = double.NaN, _followY = double.NaN;
        private bool _faceWasFound;
        private readonly Queue<double> _yaw = new(), _pitch = new();

        public int NoFaceFrames { get; private set; }
        public double LastYaw { get; private set; }
        public double LastPitch { get; private set; }
        public bool HeadPoseValid { get; private set; }

        /// <summary>WPF ResetHeuristicState, gaze half (the follower deliberately survives, as on WPF).</summary>
        public void Reset()
        {
            _faceWasFound = false; NoFaceFrames = 0;
            _irisDxSmooth.Clear(); _irisDySmooth.Clear(); _irisDxFilter.Reset(); _irisDyFilter.Reset();
            ClearRefs();
            _disagreement.Clear();
            _yaw.Clear(); _pitch.Clear(); HeadPoseValid = false; LastYaw = 0; LastPitch = 0;
            _lastGazeSide = _lastEmittedSide = _pendingSide = GazeSide.Center;
            _pendingSideStreak = 0;
        }

        /// <summary>A new or live-swapped calibration restarts the side hysteresis (WPF ApplyCalibration).</summary>
        public void ResetSideHysteresis() => _lastGazeSide = GazeSide.Center;

        private void ClearRefs()
        {
            _lCx.Clear(); _lCy.Clear(); _lW.Clear(); _rCx.Clear(); _rCy.Clear(); _rW.Clear();
            _dxMedian.Clear(); _dyMedian.Clear();
            _screenX.Reset(); _screenY.Reset();
            _twoEyeStreak = 0; _gateSkipStreak = 0;
        }

        /// <summary>A frame with no usable face (WPF HandleNoFace, gaze half). True exactly once when
        /// the face counts as lost; drops the smoothing state so the cursor never averages across the gap.</summary>
        public bool FaceMissing()
        {
            NoFaceFrames++;
            bool lost = _faceWasFound && NoFaceFrames >= FaceLostFramesThreshold;
            if (lost) _faceWasFound = false;
            HeadPoseValid = false;
            _yaw.Clear(); _pitch.Clear();
            ClearRefs();
            return lost;
        }

        /// <summary>A frame with a face (WPF HandleFaceFound). True exactly once when it comes back.</summary>
        public bool FaceSeen()
        {
            NoFaceFrames = 0;
            if (_faceWasFound) return false;
            _faceWasFound = true;
            return true;
        }

        // Canonical 3D face model (mm-ish, anchored at the nose tip, +Y up, +Z toward the camera) and the FaceMesh points that match it (nose tip, chin, outer eye
        // corners, mouth corners).
        public static readonly Point3f[] HeadPoseModelPoints =
        {
            new(0f, 0f, 0f), new(0f, -330f, -65f), new(225f, 170f, -135f),
            new(-225f, 170f, -135f), new(150f, -150f, -125f), new(-150f, -150f, -125f),
        };
        public static readonly int[] HeadPoseLandmarkIndices = { 1, 152, 33, 263, 61, 291 };

        // ponytail: solvePnP itself stays in each head. OpenCvSharp 4.13 (Avalonia) renamed the
        // SolvePnPFlags parameter to SolvePnPMethod, so a call compiled here against 4.9 (WPF's
        // version) throws MissingMethodException there. Move it here once both heads share a version.

        /// <summary>The image points matching <see cref="HeadPoseModelPoints"/>, or null (and no pose)
        /// when a landmark is missing or NaN.</summary>
        public Point2f[]? HeadPoseImagePoints(float[][] landmarks)
        {
            HeadPoseValid = false;
            if (landmarks == null || landmarks.Length < 468) return null;
            var pts = new Point2f[HeadPoseLandmarkIndices.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var lm = landmarks[HeadPoseLandmarkIndices[i]];
                if (lm == null || lm.Length < 2 || float.IsNaN(lm[0]) || float.IsNaN(lm[1])) return null;
                pts[i] = new Point2f(lm[0], lm[1]);
            }
            return pts;
        }

        /// <summary>Pinhole approximation: focal = frame width, principal point centred, no distortion.</summary>
        public static double[] HeadPoseCameraMatrix(int frameW, int frameH)
            => new double[] { frameW, 0, frameW / 2.0, 0, frameW, frameH / 2.0, 0, 0, 1 };

        /// <summary>Euler yaw/pitch from the bottom row of the solved rotation matrix, smoothed like the
        /// iris; null (no pose) when degenerate. Call <see cref="HeadPoseFailed"/> when the solve throws.</summary>
        public (double Yaw, double Pitch)? UpdateHeadPose(double r20, double r21, double r22)
        {
            double pitch = Math.Atan2(r21, r22);
            double yaw = Math.Atan2(-r20, Math.Sqrt(r21 * r21 + r22 * r22));
            if (double.IsNaN(yaw) || double.IsNaN(pitch)) { HeadPoseValid = false; return null; }
            Cap(_yaw, yaw, IrisSmoothFrames);
            Cap(_pitch, pitch, IrisSmoothFrames);
            LastYaw = Average(_yaw);
            LastPitch = Average(_pitch);
            HeadPoseValid = true;
            return (LastYaw, LastPitch);
        }

        public void HeadPoseFailed() => HeadPoseValid = false;

        /// <summary>Iris centre relative to the (smoothed) eye-corner midpoint, scaled by corner
        /// distance; roughly [-0.5, 0.5].</summary>
        public (double Dx, double Dy) NormalizeIris((double X, double Y) iris, float[] outer, float[] inner, bool rightEye)
        {
            var (cxBuf, cyBuf, wBuf) = rightEye ? (_rCx, _rCy, _rW) : (_lCx, _lCy, _lW);
            double cx = (outer[0] + inner[0]) / 2.0, cy = (outer[1] + inner[1]) / 2.0;
            double w = Math.Sqrt((outer[0] - inner[0]) * (outer[0] - inner[0]) + (outer[1] - inner[1]) * (outer[1] - inner[1]));
            if (w < 1.0) return (0, 0);
            Cap(cxBuf, cx, EyeRefSmoothFrames); Cap(cyBuf, cy, EyeRefSmoothFrames); Cap(wBuf, w, EyeRefSmoothFrames);
            double sw = Average(wBuf);
            if (sw < 1.0) return (0, 0);
            return ((iris.X - Average(cxBuf)) / sw, (iris.Y - Average(cyBuf)) / sw);
        }

        /// <summary>Two-eye consistency gate: the averaged vector, or null to hold the last output
        /// (one eye spiking, or a transient one-eye frame after a healthy two-eye run).</summary>
        public (double Dx, double Dy)? CombineEyes((double Dx, double Dy)? left, (double Dx, double Dy)? right)
        {
            if (left is { } l && right is { } r)
            {
                double ddx = l.Dx - r.Dx, ddy = l.Dy - r.Dy;
                double disagreement = Math.Sqrt(ddx * ddx + ddy * ddy);
                bool spike = _disagreement.Count >= EyeDisagreementMinSamples
                    && disagreement > BlinkDetector.PercentileOf(_disagreement, 0.50) * EyeDisagreementRatio + EyeDisagreementFloor;
                if (spike && _gateSkipStreak < MaxGateSkipFrames) { _gateSkipStreak++; return null; }
                _gateSkipStreak = 0;
                _twoEyeStreak++;
                Cap(_disagreement, disagreement, EyeDisagreementBaselineFrames);
                return ((l.Dx + r.Dx) / 2.0, (l.Dy + r.Dy) / 2.0);
            }
            var v = left ?? right;
            if (!v.HasValue) return null;
            if (_twoEyeStreak >= TwoEyeStreakRequired && _gateSkipStreak < MaxGateSkipFrames) { _gateSkipStreak++; return null; }
            _gateSkipStreak = 0;
            _twoEyeStreak = 0;
            return v;
        }

        /// <summary>3-sample median; its output is what OnRawIris reports and calibration fits against.</summary>
        public (double Dx, double Dy) PreFilter(double dx, double dy)
            => (MedianFilter(_dxMedian, dx, IrisMedianFrames), MedianFilter(_dyMedian, dy, IrisMedianFrames));

        /// <summary>Everything between OnRawIris and the gaze lock: side classification with its
        /// stability filter, the iris One-Euro, range clamp, projection, screen One-Euro, quick-recal
        /// offset, bubble trim and soft edges. Returns the pre-lock screen point (DIPs of the
        /// calibrated monitor), or null with no usable calibration.</summary>
        public (double X, double Y)? Step(WebcamCalibrationData? cal, double irisDx, double irisDy, long nowTicks, out GazeSide side)
        {
            Cap(_irisDxSmooth, irisDx, IrisSmoothFrames);
            Cap(_irisDySmooth, irisDy, IrisSmoothFrames);
            double sideSmoothDx = Average(_irisDxSmooth);
            double smoothDx = _irisDxFilter.Filter(irisDx, nowTicks);
            double smoothDy = _irisDyFilter.Filter(irisDy, nowTicks);

            var classified = GazeSideClassifier.Classify(sideSmoothDx, cal?.LeftRefVec, cal?.RightRefVec, ref _lastGazeSide);
            if (classified == _lastEmittedSide) { _pendingSide = classified; _pendingSideStreak = 0; }
            else if (classified == _pendingSide)
            {
                if (++_pendingSideStreak >= SideStabilityFrames) { _lastEmittedSide = _pendingSide; _pendingSideStreak = 0; }
            }
            else { _pendingSide = classified; _pendingSideStreak = 1; }
            side = _lastEmittedSide;

            if (cal?.IrisRange is { } range)
            {
                double spanX = Math.Max(1e-6, range.MaxX - range.MinX), spanY = Math.Max(1e-6, range.MaxY - range.MinY);
                smoothDx = Math.Max(range.MinX - spanX * IrisClampMarginFrac, Math.Min(range.MaxX + spanX * IrisClampMarginFrac, smoothDx));
                smoothDy = Math.Max(range.MinY - spanY * IrisClampMarginFrac, Math.Min(range.MaxY + spanY * IrisClampMarginFrac, smoothDy));
            }

            if (Project(cal, smoothDx, smoothDy) is not { } p) return null;
            double beta = ScreenOneEuroBeta;
            _screenX.Beta = beta; _screenY.Beta = beta;
            double x = _screenX.Filter(p.X, nowTicks), y = _screenY.Filter(p.Y, nowTicks);
            if (cal?.RuntimeOffset is { } off) { x += off.Dx; y += off.Dy; }
            if (cal?.GazeTrim is { } trim)
            {
                x += trim.X0 + trim.X1 * (x - trim.CenterX);
                y += trim.Y0 + trim.Y1 * (y - trim.CenterY);
            }
            if (cal?.MonitorBounds is { } b && b.Width > 0 && b.Height > 0)
            {
                x = SoftEdge(x, 0, b.Width, Math.Clamp(b.Width * 0.055, 28, 90));
                y = SoftEdge(y, 0, b.Height, Math.Clamp(b.Height * 0.055, 28, 90));
            }
            return (x, y);
        }

        /// <summary>Distance-adaptive exponential follower; the emitted cursor. Never snaps.</summary>
        public (double X, double Y) Follow(double tx, double ty)
        {
            double followMin = FollowMin, rampDist = RampDist, followMax = Math.Max(followMin, FollowMaxDefault);
            if (double.IsNaN(_followX)) { _followX = tx; _followY = ty; return (tx, ty); }
            double ex = tx - _followX, ey = ty - _followY;
            double t = Math.Min(1.0, Math.Sqrt(ex * ex + ey * ey) / rampDist);
            double alpha = followMin + (followMax - followMin) * t * t;
            _followX += ex * alpha;
            _followY += ey * alpha;
            return (_followX, _followY);
        }

        private static double ScreenOneEuroBeta
        {
            get { double v = CoreSettings.Current.GazeScreenOneEuroBeta; return double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : ScreenOneEuroBetaDefault; }
        }
        private static double FollowMin
        {
            get { double v = CoreSettings.Current.GazeCursorFollowMin; return double.IsFinite(v) ? Math.Clamp(v, 0.01, 0.9) : FollowMinDefault; }
        }
        private static double RampDist
        {
            get { double v = CoreSettings.Current.GazeCursorRampDist; return double.IsFinite(v) && v > 1.0 ? Math.Clamp(v, 40.0, 4000.0) : RampDistDefault; }
        }

        /// <summary>Iris vector to screen: 7-coeff Cerrolaza or legacy 6-coeff polynomial (with axis
        /// residual correction), else the homography; null with neither.</summary>
        public static (double X, double Y)? Project(WebcamCalibrationData? cal, double ix, double iy)
        {
            var poly = cal?.Polynomial;
            if (poly?.X != null && poly.Y != null && poly.X.Length == poly.Y.Length && (poly.X.Length == 6 || poly.X.Length == 7))
            {
                double ix2 = ix * ix, iy2 = iy * iy, ixy = ix * iy, x, y;
                if (poly.X.Length == 7)
                {
                    x = poly.X[0] + poly.X[1] * ix + poly.X[2] * iy + poly.X[3] * ixy + poly.X[4] * ix2 + poly.X[5] * iy2 + poly.X[6] * ix2 * iy;
                    y = poly.Y[0] + poly.Y[1] * ix + poly.Y[2] * iy + poly.Y[3] * ixy + poly.Y[4] * ix2 + poly.Y[5] * iy2 + poly.Y[6] * iy2 * ix;
                }
                else
                {
                    x = poly.X[0] + poly.X[1] * ix + poly.X[2] * iy + poly.X[3] * ix2 + poly.X[4] * iy2 + poly.X[5] * ixy;
                    y = poly.Y[0] + poly.Y[1] * ix + poly.Y[2] * iy + poly.Y[3] * ix2 + poly.Y[4] * iy2 + poly.Y[5] * ixy;
                }
                if (cal!.AxisCorrection is { } ac)
                {
                    x = ApplyAxisCurve(ac.SrcX, ac.DstX, x);
                    y = ApplyAxisCurve(ac.SrcY, ac.DstY, y);
                }
                return (x, y);
            }
            var h = cal?.Homography;
            if (h == null || h.Length != 3 || h[0].Length != 3) return null;
            double hx = h[0][0] * ix + h[0][1] * iy + h[0][2];
            double hy = h[1][0] * ix + h[1][1] * iy + h[1][2];
            double hw = h[2][0] * ix + h[2][1] * iy + h[2][2];
            if (Math.Abs(hw) < 1e-9) return null;
            return (hx / hw, hy / hw);
        }

        /// <summary>Piecewise-linear map through calibration anchors, end-segment slope beyond them.</summary>
        public static double ApplyAxisCurve(double[] src, double[] dst, double v)
        {
            int n = src.Length;
            if (n < 2 || dst.Length != n) return v;
            int i = 1;
            while (i < n - 1 && v > src[i]) i++;
            double t = (v - src[i - 1]) / (src[i] - src[i - 1]);
            return dst[i - 1] + t * (dst[i] - dst[i - 1]);
        }

        /// <summary>Soft edge compression: untouched inside, tanh-compressed in the outer band, never
        /// glued flat to the bezel.</summary>
        public static double SoftEdge(double v, double lo, double hi, double band)
        {
            double innerLo = lo + band, innerHi = hi - band;
            if (innerHi <= innerLo) return Math.Max(lo, Math.Min(hi, v));
            if (v < innerLo) return innerLo - band * 0.8 * Math.Tanh((innerLo - v) / band);
            if (v > innerHi) return innerHi + band * 0.8 * Math.Tanh((v - innerHi) / band);
            return v;
        }

        /// <summary>Quick recal's centre estimate: drop the saccade onto the dot (first
        /// <paramref name="dropFirst"/> samples, when enough remain), then the per-axis median.</summary>
        public static (double X, double Y) MedianAfterSaccadeSettle(IReadOnlyList<(double X, double Y)> samples, int dropFirst)
        {
            int start = samples.Count - dropFirst >= 15 ? dropFirst : 0;
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = start; i < samples.Count; i++) { xs.Add(samples[i].X); ys.Add(samples[i].Y); }
            xs.Sort(); ys.Sort();
            return (xs[xs.Count / 2], ys[ys.Count / 2]);
        }

        public static double MedianFilter(Queue<double> buffer, double value, int window)
        {
            buffer.Enqueue(value);
            while (buffer.Count > window) buffer.Dequeue();
            var arr = buffer.ToArray();
            Array.Sort(arr);
            return arr[arr.Length / 2];
        }

        private static double Average(Queue<double> q)
        {
            if (q.Count == 0) return 0;
            double s = 0;
            foreach (var v in q) s += v;
            return s / q.Count;
        }

        private static void Cap(Queue<double> q, double value, int cap)
        {
            q.Enqueue(value);
            while (q.Count > cap) q.Dequeue();
        }

        /// <summary>Stopwatch ticks for <see cref="Step"/>, as WPF.</summary>
        public static long Now() => Stopwatch.GetTimestamp();
    }
}
