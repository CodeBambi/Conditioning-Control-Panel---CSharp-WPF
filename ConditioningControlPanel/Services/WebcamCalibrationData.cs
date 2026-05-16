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

        /// <summary>"TwoPoint" (gaze-side only), "FivePoint" (legacy), "NinePoint" (3×3), "SixteenPoint" (4×4), or "TwentyFivePoint" (current, 5×5 with margin=40). The 5×5 grid puts dots at every edge midpoint and shrinks the corner-to-bezel extrapolation from ~90 DIPs to ~40 DIPs — directly addresses the top/bottom undershoot that 4×4 had to extrapolate. Earlier 5×5 attempts had jumpy cursor + drift, but those traced to issues in the regression / outlier rejection / head-pose comp / edge-pull, all since fixed.</summary>
        [JsonProperty] public string Mode { get; set; } = "";

        [JsonProperty] public DateTime Timestamp { get; set; }

        [JsonProperty] public MonitorBoundsRecord? MonitorBounds { get; set; }

        [JsonProperty] public string PrimaryDeviceId { get; set; } = "";

        [JsonProperty] public double[] LeftRefVec { get; set; } = new double[2];

        [JsonProperty] public double[] RightRefVec { get; set; } = new double[2];

        /// <summary>
        /// Legacy. Iris vector at the top edge of the calibration grid, written
        /// by older builds that ran an iris-extreme edge-pull heuristic at
        /// runtime. The edge-pull was retired once the 5×5 grid + Cerrolaza
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
        /// Legacy. Calibration-time average head pose, paired with HeadPoseComp
        /// to drive a runtime iris-vector correction when the live head pose
        /// drifted off the calibration baseline. The comp pipeline was retired
        /// because the PnP head-pose estimator was noisier than natural head
        /// movement, so the correction injected variance instead of removing
        /// it. New calibrations set this to null; old saves are still
        /// deserialized but the field is ignored at runtime.
        /// </summary>
        [JsonProperty] public CalibrationHeadPose? BaselineHeadPose { get; set; }

        /// <summary>
        /// Legacy. Empirically-fit head-pose compensation coefficients for the
        /// iris vector. See <see cref="BaselineHeadPose"/> — this is the other
        /// half of the same retired pipeline. Ignored at runtime.
        /// </summary>
        [JsonProperty] public HeadPoseCompFit? HeadPoseComp { get; set; }

        /// <summary>
        /// Translational nudge in screen DIPs, applied after the polynomial
        /// projection. Set by the Quick Recal flow when the user wants to
        /// correct overall drift without redoing the full 25-point calibration.
        /// Null on calibrations from older app versions or when the user has
        /// never run quick-recal — projection path skips the nudge.
        /// </summary>
        [JsonProperty] public RuntimeOffsetData? RuntimeOffset { get; set; }

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
            };
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
    /// Iris-vector correction coefficients fit empirically from the natural
    /// head-pose variance during calibration sampling. Applied as
    ///   ix' = ix + AxYaw * sin(Δyaw) + AxPitch * sin(Δpitch)
    ///   iy' = iy + AyYaw * sin(Δyaw) + AyPitch * sin(Δpitch)
    /// where Δ is (live pose − BaselineHeadPose). Sign and magnitude come out
    /// of the LS fit, so they're correct by construction for this camera/face.
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
    }

    /// <summary>
    /// Translational offset (screen DIPs) added to every projected gaze point
    /// before it's emitted. Captured by the Quick Recal flow.
    /// </summary>
    public class RuntimeOffsetData
    {
        [JsonProperty] public double Dx { get; set; }
        [JsonProperty] public double Dy { get; set; }
        [JsonProperty] public DateTime CapturedAt { get; set; }
    }
}
