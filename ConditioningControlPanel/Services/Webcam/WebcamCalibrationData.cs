using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Calibration data for the webcam tracking pipeline. Persisted as a small JSON file
    /// in %APPDATA%/ConditioningControlPanel/webcam-calibration.json.
    ///
    /// Contains ONLY numbers — homography coefficients and reference vectors — derived
    /// from a few moments of looking at calibration dots. No images, no biometrics.
    /// Safe to delete at any time; user can recalibrate in seconds.
    /// </summary>
    public class WebcamCalibrationData
    {
        public const string FileName = "webcam-calibration.json";

        /// <summary>
        /// "TwoPoint" (gaze-side only), "FivePoint" (legacy), "NinePoint" (3×3),
        /// or "SixteenPoint" (4×4) — <b>SixteenPoint is what the calibration
        /// window actually writes today</b> (WebcamCalibrationWindow GridSize = 4).
        /// <para>
        /// CORRECTED 2026-08-27: this summary previously claimed "TwentyFivePoint
        /// (current, 5×5)" and that the 5×5 grid "directly addresses the top/bottom
        /// undershoot". No 5×5 mode is written by any current code path, and the
        /// top/bottom undershoot was NOT solved — it was simply never measured,
        /// because the bubble accuracy test only sampled the middle band
        /// (h × {0.30, 0.72}) and never probed the top or bottom 25% of the
        /// screen. That blind spot was fixed in the accuracy test itself (four
        /// distinct y-levels at 0.15 / 0.38 / 0.62 / 0.85), not by a grid change.
        /// Vertical error remains structurally ~1.5-2× horizontal: vertical
        /// eyeball rotation for a full-screen sweep is roughly half the
        /// horizontal, so the iris translates half as far per screen unit, and
        /// the eyelid occludes the iris exactly where it matters. Do not read
        /// this field as evidence that vertical is solved.
        /// </para>
        /// </summary>
        [JsonProperty] public string Mode { get; set; } = "";

        [JsonProperty] public DateTime Timestamp { get; set; }

        [JsonProperty] public MonitorBoundsRecord? MonitorBounds { get; set; }

        [JsonProperty] public string PrimaryDeviceId { get; set; } = "";

        [JsonProperty] public double[] LeftRefVec { get; set; } = new double[2];

        [JsonProperty] public double[] RightRefVec { get; set; } = new double[2];

        /// <summary>
        /// Legacy. Iris vector at the top edge of the calibration grid, written
        /// by older builds that ran an iris-extreme edge-pull heuristic at
        /// runtime. The edge-pull was retired once the calibration grid + Cerrolaza
        /// polynomial reached the screen edges on its own; new calibrations
        /// don't populate this. Kept here so saves from older builds still
        /// deserialize.
        /// </summary>
        [JsonProperty] public double[]? TopRefVec { get; set; }

        /// <summary>Legacy. See <see cref="TopRefVec"/>.</summary>
        [JsonProperty] public double[]? BottomRefVec { get; set; }

        /// <summary>3x3 homography mapping iris vector to screen coords. Null in TwoPoint mode.</summary>
        [JsonProperty] public double[][]? Homography { get; set; }

        /// <summary>
        /// 2nd-order polynomial fit (7 coefficients per axis, Cerrolaza
        /// asymmetric form) mapping iris vector to screen coords. Captures
        /// the nonlinear iris→screen response that a homography can't, so
        /// cursor accuracy at the edges/corners matches the center much
        /// more closely. Null on calibrations from older app versions — the
        /// projection path falls back to <see cref="Homography"/> when this
        /// is null. Calibrations from app versions before the 7-coefficient
        /// upgrade store 6-element arrays; the projection path transparently
        /// handles both lengths.
        /// </summary>
        [JsonProperty] public PolynomialFitData? Polynomial { get; set; }

        /// <summary>
        /// DEAD FIELD. Deserialized for back-compat with old saves; never
        /// populated by current code and never read at runtime.
        /// <para>
        /// CORRECTED 2026-08-27: this summary previously described a "guided
        /// head-motion step … see WebcamCalibrationWindow.RunHeadMotionPhaseAsync".
        /// <b>That method no longer exists.</b> Head-pose compensation has now
        /// been built and retired TWICE — first fit from natural head variance
        /// during dot sampling (users hold still, so the fit was noise), then
        /// again from a guided nod+turn phase (retired 2026-07-02). See the
        /// tombstone in WebcamTrackingService near the head-pose block.
        /// </para><para>
        /// Do not resurrect this. Multiplying head pose into the projection is
        /// a twice-failed design. Head pose IS used productively — but only as
        /// a TRIGGER, never as a correction term: GazeDriftCorrectionService
        /// watches LastYaw/LastPitch and, when it detects a settled reposition,
        /// temporarily raises the residual-folding gain so the existing proven
        /// drift machinery re-converges in seconds instead of minutes. That
        /// baseline is held in memory only, deliberately not persisted here.
        /// </para>
        /// </summary>
        [JsonProperty] public CalibrationHeadPose? BaselineHeadPose { get; set; }

        /// <summary>
        /// Head-pose compensation coefficients for the iris vector. Only
        /// applied at runtime when <see cref="HeadPoseCompFit.FromGuidedMotion"/>
        /// is true — fits from old builds (natural-variance, retired) load
        /// fine but stay inert.
        /// </summary>
        [JsonProperty] public HeadPoseCompFit? HeadPoseComp { get; set; }

        /// <summary>
        /// Translational nudge in screen DIPs, applied after the polynomial
        /// projection. Set by the Quick Recal flow when the user wants to
        /// correct overall drift without redoing the full 16-point calibration.
        /// Null on calibrations from older app versions or when the user has
        /// never run quick-recal — projection path skips the nudge.
        /// </summary>
        [JsonProperty] public RuntimeOffsetData? RuntimeOffset { get; set; }

        /// <summary>
        /// Min/max iris vector observed across the calibration grid. The
        /// runtime clamps the live (smoothed) iris vector to this range plus a
        /// margin before projecting: the 2nd-order polynomial is only trained
        /// inside the calibrated hull, and its quadratic terms extrapolate
        /// violently outside it — a single bad iris sample (glint, half-blink)
        /// used to sling the cursor across the screen. Null on calibrations
        /// from older app versions — projection path skips the clamp.
        /// </summary>
        [JsonProperty] public IrisRangeData? IrisRange { get; set; }

        /// <summary>
        /// Post-polynomial per-axis residual correction. The polynomial's
        /// systematic per-row/per-column bias (e.g. "everything in the bottom
        /// row projects 150 px too high" — the user literally can't reach the
        /// bottom of the screen) is measured at finalize time by re-projecting
        /// each grid row/column's mean iris vector and comparing to the true
        /// dot position; the anchors stored here define a piecewise-linear
        /// warp applied after the polynomial. Null on old saves or when the
        /// anchors came out non-monotonic (degenerate fit) — projection path
        /// skips the warp.
        /// </summary>
        [JsonProperty] public AxisCorrectionData? AxisCorrection { get; set; }

        /// <summary>
        /// Legacy. Short-lived lighting hint from a retired step-0 lighting
        /// picker (first "which direction is your light", then "is the room
        /// dim") — both were dropped in favor of a plain warning that dim
        /// rooms are inconsistent. Kept so saves written by those builds
        /// still deserialize; never read.
        /// </summary>
        [JsonProperty] public string? LightSource { get; set; }

        /// <summary>
        /// Per-axis linear trim fit from the bubble-test residuals — the
        /// bubbles are ground truth ("the user was trying to look THERE"),
        /// so the gap between each bubble and where the cursor actually
        /// hovered is a measured mapping error. Offset + scale per axis:
        ///   x' = x + X0 + X1·(x − CenterX),  y' = y + Y0 + Y1·(y − CenterY)
        /// which captures both whole-map drift and the "cursor went up but
        /// the bubble was down" stretch/compression error. Applied after
        /// RuntimeOffset; repeated bubble tests COMPOSE into this (see
        /// WebcamTrackingService.ApplyGazeTrim). Null until the user runs a
        /// bubble test; cleared by a fresh calibration.
        /// </summary>
        [JsonProperty] public GazeTrimData? GazeTrim { get; set; }

        public static string FilePath => Path.Combine(App.UserDataPath, FileName);

        public static WebcamCalibrationData? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var json = File.ReadAllText(FilePath);
                return JsonConvert.DeserializeObject<WebcamCalibrationData>(json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationData: failed to load");
                return null;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationData: failed to save");
            }
        }

        public static void DeleteIfExists()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamCalibrationData: failed to delete");
            }
        }

        /// <summary>
        /// Returns a shallow clone of this calibration with <see cref="RuntimeOffset"/>
        /// replaced. Use this (and re-publish via <see cref="WebcamTrackingService.SetRuntimeOffset"/>)
        /// instead of mutating the live instance — the capture thread reads
        /// <see cref="RuntimeOffset"/> every frame, and writes to fields of an already-
        /// published instance race against those reads. Reference assignment of the
        /// whole calibration is atomic, so swapping the instance is safe.
        /// </summary>
        public WebcamCalibrationData WithRuntimeOffset(RuntimeOffsetData? offset)
        {
            return new WebcamCalibrationData
            {
                Mode = this.Mode,
                Timestamp = this.Timestamp,
                MonitorBounds = this.MonitorBounds,
                PrimaryDeviceId = this.PrimaryDeviceId,
                LeftRefVec = this.LeftRefVec,
                RightRefVec = this.RightRefVec,
                TopRefVec = this.TopRefVec,
                BottomRefVec = this.BottomRefVec,
                Homography = this.Homography,
                Polynomial = this.Polynomial,
                BaselineHeadPose = this.BaselineHeadPose,
                HeadPoseComp = this.HeadPoseComp,
                RuntimeOffset = offset,
                IrisRange = this.IrisRange,
                AxisCorrection = this.AxisCorrection,
                LightSource = this.LightSource,
                GazeTrim = this.GazeTrim,
            };
        }

        /// <summary>
        /// Shallow clone with <see cref="GazeTrim"/> replaced — same
        /// swap-don't-mutate contract as <see cref="WithRuntimeOffset"/>.
        /// </summary>
        public WebcamCalibrationData WithGazeTrim(GazeTrimData? trim)
        {
            var clone = WithRuntimeOffset(this.RuntimeOffset);
            clone.GazeTrim = trim;
            return clone;
        }
    }

    public class MonitorBoundsRecord
    {
        [JsonProperty] public int Width { get; set; }
        [JsonProperty] public int Height { get; set; }
        [JsonProperty] public double DpiScale { get; set; } = 1.0;

        // Identity of the monitor calibration ran on. Pre-hotfix saves have these
        // as null / 0 — consumers must treat null DeviceName as "unknown monitor"
        // and fall back to primary, then prompt the user to recalibrate.
        [JsonProperty] public string? DeviceName { get; set; }
        [JsonProperty] public int X { get; set; }
        [JsonProperty] public int Y { get; set; }
    }

    /// <summary>
    /// 2nd-order polynomial fit, Cerrolaza et al. (2008, 2012) asymmetric
    /// form — the empirically-best 2nd-order family across 400+ variants on
    /// 9-25 point grids:
    ///   x_screen = a0 + a1·ix + a2·iy + a3·ix·iy + a4·ix² + a5·iy² + a6·ix²·iy
    ///   y_screen = b0 + b1·ix + b2·iy + b3·ix·iy + b4·ix² + b5·iy² + b6·iy²·ix
    /// The asymmetric high-order term (ix²·iy on X, iy²·ix on Y) gives
    /// ~0.15-0.25° DVA over the symmetric 6-coefficient form on webcam grids.
    /// Fit via ridge regression with a small fixed λ scaled to trace(AᵀA)/p —
    /// just enough for numerical stability, not enough to shrink the output
    /// range. (LOO-CV was tried and over-regularized: corner leave-outs force
    /// extrapolation, and LOO-error minimization picks heavier shrinkage,
    /// which compresses the cursor's reach.)
    /// 6-element arrays from older calibrations are still loadable and
    /// projected through the symmetric form (see WebcamTrackingService.ProjectGazeToScreen).
    /// </summary>
    public class PolynomialFitData
    {
        /// <summary>X-axis coefficients [a0, a1, a2, a3, a4, a5, a6]. 6-element legacy arrays decode as [a0..a5] and project through the old symmetric form.</summary>
        [JsonProperty] public double[] X { get; set; } = new double[7];

        /// <summary>Y-axis coefficients [b0, b1, b2, b3, b4, b5, b6]. 6-element legacy arrays decode as [b0..b5] and project through the old symmetric form.</summary>
        [JsonProperty] public double[] Y { get; set; } = new double[7];
    }

    /// <summary>
    /// Average head orientation captured during calibration (radians). Used as
    /// a "looking forward" reference; runtime pose deltas drive a geometric
    /// correction on the iris vector.
    /// </summary>
    public class CalibrationHeadPose
    {
        /// <summary>Rotation around vertical axis. Positive = subject turned head one way (sign empirical, set by solvePnP convention).</summary>
        [JsonProperty] public double Yaw { get; set; }

        /// <summary>Rotation around horizontal axis. Positive = subject pitched head one way (sign empirical).</summary>
        [JsonProperty] public double Pitch { get; set; }
    }

    /// <summary>
    /// Iris-vector correction coefficients fit from the guided head-motion
    /// step (gaze pinned to a center dot while the user deliberately nods and
    /// turns — pose varies, gaze doesn't, so the regression is
    /// well-conditioned). Applied at runtime as
    ///   ix' = ix − AxYaw·(sin(yaw)−sin(baseYaw)) − AxPitch·(sin(pitch)−sin(basePitch))
    ///   iy' = iy − AyYaw·(sin(yaw)−sin(baseYaw)) − AyPitch·(sin(pitch)−sin(basePitch))
    /// mapping the live iris vector back to its baseline-pose equivalent
    /// before the polynomial projection. Sign and magnitude come out of the
    /// LS fit, so they're correct by construction for this camera/face.
    /// </summary>
    public class HeadPoseCompFit
    {
        [JsonProperty] public double AxYaw { get; set; }
        [JsonProperty] public double AxPitch { get; set; }
        [JsonProperty] public double AyYaw { get; set; }
        [JsonProperty] public double AyPitch { get; set; }
        /// <summary>Coefficient of determination of the iris-X residual fit. Diagnostic only.</summary>
        [JsonProperty] public double RSquaredX { get; set; }
        /// <summary>Coefficient of determination of the iris-Y residual fit. Diagnostic only.</summary>
        [JsonProperty] public double RSquaredY { get; set; }
        /// <summary>Number of samples that contributed to the fit.</summary>
        [JsonProperty] public int SampleCount { get; set; }
        /// <summary>
        /// True when the coefficients came from the guided nod/turn step. The
        /// runtime only applies fits with this set — coefficients from the
        /// retired natural-variance pipeline (older builds) deserialize with
        /// false and stay inert.
        /// </summary>
        [JsonProperty] public bool FromGuidedMotion { get; set; }
    }

    /// <summary>
    /// Translational offset (screen DIPs) added to every projected gaze point
    /// before it's emitted. Captured by the Quick Recal flow, the calibration
    /// bubble-test fine-tune, and the click-driven drift correction.
    /// </summary>
    public class RuntimeOffsetData
    {
        [JsonProperty] public double Dx { get; set; }
        [JsonProperty] public double Dy { get; set; }
        [JsonProperty] public DateTime CapturedAt { get; set; }
    }

    /// <summary>
    /// Bounding box of the per-dot mean iris vectors from calibration
    /// (iris-vector units, roughly [-0.5, +0.5]). See
    /// <see cref="WebcamCalibrationData.IrisRange"/> for how it's used.
    /// </summary>
    public class IrisRangeData
    {
        [JsonProperty] public double MinX { get; set; }
        [JsonProperty] public double MaxX { get; set; }
        [JsonProperty] public double MinY { get; set; }
        [JsonProperty] public double MaxY { get; set; }
    }

    /// <summary>
    /// Piecewise-linear post-polynomial warp, one curve per axis. SrcX[i] is
    /// where the polynomial projected grid column i (mean over its dots),
    /// DstX[i] is where that column actually was; likewise SrcY/DstY for rows.
    /// Runtime maps the projected coordinate through the curve (linear
    /// between anchors, end-segment slope beyond them), which cancels the
    /// polynomial's systematic per-band bias — the dominant "cursor is skewed
    /// toward the top / can't reach the bottom" failure. Arrays are sorted
    /// ascending by Src and strictly monotonic in BOTH arrays (enforced at
    /// build time; non-monotonic fits store null instead).
    /// </summary>
    public class AxisCorrectionData
    {
        [JsonProperty] public double[] SrcX { get; set; } = Array.Empty<double>();
        [JsonProperty] public double[] DstX { get; set; } = Array.Empty<double>();
        [JsonProperty] public double[] SrcY { get; set; } = Array.Empty<double>();
        [JsonProperty] public double[] DstY { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Per-axis linear correction (offset + center-relative scale) measured
    /// by the bubble accuracy test. See <see cref="WebcamCalibrationData.GazeTrim"/>.
    /// </summary>
    public class GazeTrimData
    {
        [JsonProperty] public double X0 { get; set; }
        [JsonProperty] public double X1 { get; set; }
        [JsonProperty] public double Y0 { get; set; }
        [JsonProperty] public double Y1 { get; set; }
        [JsonProperty] public double CenterX { get; set; }
        [JsonProperty] public double CenterY { get; set; }
        [JsonProperty] public DateTime CapturedAt { get; set; }
    }
}
