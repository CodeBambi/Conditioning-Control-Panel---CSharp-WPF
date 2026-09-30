using System;
using System.Collections.Generic;
using System.Diagnostics;
using Serilog;

namespace ConditioningControlPanel.Services
{
    // The frame-free half of WebcamTrackingService: blink rules, gaze-side hysteresis and the
    // One Euro smoother. Numbers in, decisions out - no camera, no frames, nothing stored.
    // Same privacy contract as the tracker: logs are Debug-level counts and thresholds only.

    public enum GazeSide { Left, Right, Center }

    public enum BlinkEvent { None, Blink, EyesClosedLong }

    /// <summary>
    /// EAR blink detection (Soukupova &amp; Cech 2016) averaged across both eyes, with a rolling
    /// 90th-percentile baseline and enter/leave hysteresis. Capture-thread only; not thread-safe.
    /// </summary>
    public sealed class BlinkDetector
    {
        // EAR-based blink detection. Rolling 90-frame baseline. Window is generous: real blinks are
        // ~100ms but the calibration prompt tells users to "blink slowly and deliberately" so up to
        // 1.5s counts as one blink - anything longer is a stare.
        public const int EarBaselineFrames = 90;            // ~3s at 30fps
        public const int EarMinSamplesForBaseline = 15;     // need this many before any blink fires
        public const double EarClosedRatio = 0.80;          // EAR < 0.80 x baseline -> enter closed
        public const double EarOpenRatio = 0.88;            // EAR > 0.88 x baseline -> leave closed (hysteresis gap)
        public const double EarNearMissRatio = 0.90;        // dips below 0.90 x base log as near-miss for tuning
        public const int MinBlinkClosedMs = 60;             // shorter than this is noise (1-frame jitter)
        public const int MaxBlinkClosedMs = 1500;           // longer than this is a stare/squint, not a blink
        public const int BlinkCooldownMs = 500;             // gap required between consecutive blink fires
        public const int BlinkDiagLogIntervalMs = 3000;
        // Deliberate "hold your eyes shut" gesture. Sits ABOVE MaxBlinkClosedMs on purpose, so a
        // closure long enough to fire it can never also be reported as a blink.
        public const int EyesClosedLongMs = 2000;

        // EAR on the IRIS MODEL's 71-point eye contour (FaceMesh eyelids barely move mid-blink).
        // P1=0 outer, P2=11, P3=13 upper, P4=8 inner, P5=5, P6=3 lower. Same for both eyes (the
        // right-eye contour is un-flipped by the iris detector).
        public static readonly int[] IrisContourEarIndices = { 0, 11, 13, 8, 5, 3 };

        private DateTime _lastBlinkAt = DateTime.MinValue;  // deliberately survives Reset (as in WPF)
        private readonly Queue<double> _earBuffer = new();
        private double _earBaseline;
        private bool _eyesClosed;
        private DateTime? _eyesClosedAt;
        private bool _eyesClosedLongFired;
        private double _minEarThisClosure;
        private double _windowMinEar = double.MaxValue;
        private double _windowMaxEar = double.MinValue;
        private int _windowNearMissCount;
        private DateTime _lastBlinkDiagAt = DateTime.MinValue;
        private int _blinkCount;

        public int BlinkCount => _blinkCount;
        public bool EyesClosed => _eyesClosed;

        public void Reset()
        {
            _earBuffer.Clear();
            _earBaseline = 0;
            _eyesClosed = false;
            _eyesClosedAt = null;
            _eyesClosedLongFired = false;
            _minEarThisClosure = double.MaxValue;
            _windowMinEar = double.MaxValue;
            _windowMaxEar = double.MinValue;
            _windowNearMissCount = 0;
            _lastBlinkDiagAt = DateTime.MinValue;
            _blinkCount = 0;
        }

        /// <summary>Face lost: drop mid-closure state so a loss is never read as a giant blink.</summary>
        public void CancelClosure()
        {
            _eyesClosed = false;
            _eyesClosedAt = null;
            _eyesClosedLongFired = false;
        }

        /// <summary>Standard 6-point EAR: (|p2-p6| + |p3-p5|) / (2 |p1-p4|).</summary>
        public static double ComputeEar(float[][] landmarks, int[] idx)
        {
            var p1 = landmarks[idx[0]];
            var p2 = landmarks[idx[1]];
            var p3 = landmarks[idx[2]];
            var p4 = landmarks[idx[3]];
            var p5 = landmarks[idx[4]];
            var p6 = landmarks[idx[5]];
            double a = Distance(p2, p6);
            double b = Distance(p3, p5);
            double c = Distance(p1, p4);
            return c > 1e-6 ? (a + b) / (2.0 * c) : 0.0;
        }

        private static double Distance(float[] a, float[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double PercentileOf(Queue<double> q, double pct)
        {
            if (q.Count == 0) return 0;
            var arr = q.ToArray();
            Array.Sort(arr);
            int idx = (int)(arr.Length * pct);
            if (idx >= arr.Length) idx = arr.Length - 1;
            if (idx < 0) idx = 0;
            return arr[idx];
        }

        /// <summary>One frame's per-eye EAR. Returns the gesture this frame completed, if any.</summary>
        public BlinkEvent Update(double earL, double earR, DateTime now)
        {
            // Averaging absorbs per-eye asymmetry that would stop both eyes reading closed at once.
            double avgEar = (earL + earR) / 2.0;

            // 90th percentile, NOT max: raised eyebrows spike EAR and would pollute a max for 3s.
            _earBuffer.Enqueue(avgEar);
            while (_earBuffer.Count > EarBaselineFrames) _earBuffer.Dequeue();
            _earBaseline = PercentileOf(_earBuffer, 0.90);

            if (avgEar < _windowMinEar) _windowMinEar = avgEar;
            if (avgEar > _windowMaxEar) _windowMaxEar = avgEar;
            MaybeLogDiag(now);

            // No blink while the baseline is still seeded by the first few frames.
            if (_earBuffer.Count < EarMinSamplesForBaseline) return BlinkEvent.None;
            if (_earBaseline <= 0) return BlinkEvent.None;

            if (!_eyesClosed && avgEar < EarNearMissRatio * _earBaseline) _windowNearMissCount++;

            bool nowClosed = _eyesClosed
                ? avgEar < EarOpenRatio * _earBaseline
                : avgEar < EarClosedRatio * _earBaseline;

            var result = BlinkEvent.None;
            if (nowClosed && !_eyesClosed)
            {
                _eyesClosedAt = now;
                _minEarThisClosure = avgEar;
                _eyesClosedLongFired = false;
            }
            else if (nowClosed)
            {
                if (avgEar < _minEarThisClosure) _minEarThisClosure = avgEar;
                // Still closed: the 2s hold fires here, once per closure, without waiting to reopen.
                if (!_eyesClosedLongFired && _eyesClosedAt.HasValue
                    && (now - _eyesClosedAt.Value).TotalMilliseconds >= EyesClosedLongMs)
                {
                    _eyesClosedLongFired = true;
                    result = BlinkEvent.EyesClosedLong;
                }
            }
            else if (_eyesClosed && _eyesClosedAt.HasValue)
            {
                var closedMs = (now - _eyesClosedAt.Value).TotalMilliseconds;
                bool fired = false;
                if (closedMs >= MinBlinkClosedMs && closedMs <= MaxBlinkClosedMs
                    && (now - _lastBlinkAt).TotalMilliseconds >= BlinkCooldownMs)
                {
                    _lastBlinkAt = now;
                    _blinkCount++;
                    fired = true;
                    result = BlinkEvent.Blink;
                }
                // Debug, not Information: per-blink timing is a biometric correlate.
                Log.Debug(
                    "WebcamTrackingService: blink {Outcome} (closed for {Ms:F0}ms, baseline EAR={Base:F3}, min EAR during closure={Min:F3}, ratio={Ratio:F2}× baseline)",
                    fired ? $"#{_blinkCount} FIRED" : "rejected",
                    closedMs, _earBaseline, _minEarThisClosure, _minEarThisClosure / _earBaseline);
                _eyesClosedAt = null;
                _minEarThisClosure = double.MaxValue;
                _eyesClosedLongFired = false;
            }
            else
            {
                _eyesClosedAt = null;
                _minEarThisClosure = double.MaxValue;
                _eyesClosedLongFired = false;
            }

            _eyesClosed = nowClosed;
            return result;
        }

        private void MaybeLogDiag(DateTime now)
        {
            // Counts and aggregate state only, no per-frame data.
            if (_lastBlinkDiagAt == DateTime.MinValue) { _lastBlinkDiagAt = now; return; }
            if ((now - _lastBlinkDiagAt).TotalMilliseconds < BlinkDiagLogIntervalMs) return;
            _lastBlinkDiagAt = now;

            double winMinRatio = _earBaseline > 0 ? _windowMinEar / _earBaseline : 0;
            double winMaxRatio = _earBaseline > 0 ? _windowMaxEar / _earBaseline : 0;
            Log.Debug(
                "WebcamTrackingService: blink-diag baseline={Base:F3} closedThr={CT:F3} openThr={OT:F3} winMin={WMin:F3}({WMinR:P0}) winMax={WMax:F3}({WMaxR:P0}) nearMiss={NM} state={State} blinks={N} samples={S}",
                _earBaseline, _earBaseline * EarClosedRatio, _earBaseline * EarOpenRatio,
                _windowMinEar, winMinRatio, _windowMaxEar, winMaxRatio,
                _windowNearMissCount,
                _eyesClosed ? "CLOSED" : "open", _blinkCount, _earBuffer.Count);

            _windowMinEar = double.MaxValue;
            _windowMaxEar = double.MinValue;
            _windowNearMissCount = 0;
        }
    }

    public static class GazeSideClassifier
    {
        /// <summary>
        /// Left/Right/Center from the smoothed horizontal iris vector. Calibrated (both reference
        /// vectors present) uses enter/leave bands of 17.5% / 7.5% of the reference spread around
        /// their midpoint, so small jitter cannot flicker; <paramref name="last"/> carries that
        /// hysteresis. Uncalibrated falls back to raw +-0.10 thresholds without hysteresis.
        /// </summary>
        public static GazeSide Classify(double irisDx, double[]? leftRefVec, double[]? rightRefVec, ref GazeSide last)
        {
            if (leftRefVec is { Length: >= 1 } left && rightRefVec is { Length: >= 1 } right)
            {
                var leftRef = left[0];
                var rightRef = right[0];
                var midpoint = (leftRef + rightRef) / 2.0;
                var spread = Math.Abs(leftRef - rightRef);
                if (spread < 1e-6) return GazeSide.Center;

                // Positive = toward the left reference, whichever way the axis runs.
                var towardLeft = leftRef < rightRef ? (midpoint - irisDx) : (irisDx - midpoint);
                var enterBand = spread * 0.175;
                var leaveBand = spread * 0.075;

                switch (last)
                {
                    case GazeSide.Left:
                        if (towardLeft < -enterBand) last = GazeSide.Right;
                        else if (towardLeft < leaveBand) last = GazeSide.Center;
                        break;
                    case GazeSide.Right:
                        if (towardLeft > enterBand) last = GazeSide.Left;
                        else if (towardLeft > -leaveBand) last = GazeSide.Center;
                        break;
                    default:
                        if (towardLeft > enterBand) last = GazeSide.Left;
                        else if (towardLeft < -enterBand) last = GazeSide.Right;
                        break;
                }
                return last;
            }

            if (irisDx < -0.10) return GazeSide.Left;
            if (irisDx > 0.10) return GazeSide.Right;
            return GazeSide.Center;
        }
    }

    /// <summary>
    /// One Euro filter (Casiez et al. 2012): MinCutoff is the floor cutoff at zero speed, Beta how
    /// hard the cutoff scales with |dx/dt|, DCutoff smooths the velocity estimate itself.
    /// </summary>
    public sealed class OneEuroFilter
    {
        private readonly double _minCutoff;
        private readonly double _dCutoff;
        /// <summary>Settable so the screen-space pair can follow the live setting; not carried state.</summary>
        public double Beta { get; set; }
        private double _xPrev;
        private double _dxPrev;
        private long _tPrevTicks;
        private bool _initialized;

        public OneEuroFilter(double minCutoff, double beta, double dCutoff)
        {
            _minCutoff = minCutoff;
            Beta = beta;
            _dCutoff = dCutoff;
        }

        public void Reset()
        {
            _initialized = false;
            _xPrev = 0;
            _dxPrev = 0;
            _tPrevTicks = 0;
        }

        /// <param name="tTicks"><see cref="Stopwatch"/> ticks.</param>
        public double Filter(double x, long tTicks)
        {
            if (!_initialized)
            {
                _initialized = true;
                _xPrev = x;
                _dxPrev = 0;
                _tPrevTicks = tTicks;
                return x;
            }

            double dt = (tTicks - _tPrevTicks) / (double)Stopwatch.Frequency;
            // Clock anomalies / capture stalls: never let dt collapse to 0 or grow huge.
            if (dt <= 0 || dt > 1.0) dt = 1.0 / 30.0;

            double dx = (x - _xPrev) / dt;
            double aD = Alpha(dt, _dCutoff);
            double dxHat = aD * dx + (1 - aD) * _dxPrev;

            double cutoff = _minCutoff + Beta * Math.Abs(dxHat);
            double a = Alpha(dt, cutoff);
            double xHat = a * x + (1 - a) * _xPrev;

            _xPrev = xHat;
            _dxPrev = dxHat;
            _tPrevTicks = tTicks;
            return xHat;
        }

        private static double Alpha(double dt, double cutoff)
        {
            double tau = 1.0 / (2.0 * Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
