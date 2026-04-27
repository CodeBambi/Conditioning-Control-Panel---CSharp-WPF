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

        /// <summary>"TwoPoint" (gaze-side only), "FivePoint" (legacy), "NinePoint" (3×3), or "SixteenPoint" (current, 4×4 grid for tighter polynomial fit at edges/corners and curved screens).</summary>
        [JsonProperty] public string Mode { get; set; } = "";

        [JsonProperty] public DateTime Timestamp { get; set; }

        [JsonProperty] public MonitorBoundsRecord? MonitorBounds { get; set; }

        [JsonProperty] public string PrimaryDeviceId { get; set; } = "";

        [JsonProperty] public double[] LeftRefVec { get; set; } = new double[2];

        [JsonProperty] public double[] RightRefVec { get; set; } = new double[2];

        /// <summary>3x3 homography mapping iris vector to screen coords. Null in TwoPoint mode.</summary>
        [JsonProperty] public double[][]? Homography { get; set; }

        /// <summary>
        /// 2nd-order polynomial fit (6 coefficients per axis) mapping iris
        /// vector to screen coords. Captures the nonlinear iris→screen
        /// response that a homography can't, so cursor accuracy at the
        /// edges/corners matches the center much more closely.
        /// Null on calibrations from older app versions — the projection
        /// path falls back to <see cref="Homography"/> when this is null.
        /// </summary>
        [JsonProperty] public PolynomialFitData? Polynomial { get; set; }

        /// <summary>
        /// Head pose (radians) averaged across all calibration samples — the
        /// "head still, looking forward" reference. At runtime, the projection
        /// path subtracts this from the live head pose to get a delta and
        /// applies a geometric correction to the iris vector before the
        /// polynomial fit consumes it. Lets the cursor stay roughly anchored
        /// when the user moves their head off the calibration pose.
        /// Null on calibrations from older app versions — compensation skipped.
        /// </summary>
        [JsonProperty] public CalibrationHeadPose? BaselineHeadPose { get; set; }

        /// <summary>
        /// Empirically-fit head-pose compensation coefficients for the iris
        /// vector. Replaces the old hardcoded constants (which had to be
        /// disabled because their sign/magnitude was wrong for this camera).
        /// Fitted at calibration finalize time from the natural head-pose
        /// variance across the sampling windows; null when the variance was
        /// too small to fit reliably (R² below threshold).
        /// </summary>
        [JsonProperty] public HeadPoseCompFit? HeadPoseComp { get; set; }

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
    }

    public class MonitorBoundsRecord
    {
        [JsonProperty] public int Width { get; set; }
        [JsonProperty] public int Height { get; set; }
        [JsonProperty] public double DpiScale { get; set; } = 1.0;
    }

    /// <summary>
    /// Coefficients for screen = a0 + a1*ix + a2*iy + a3*ix² + a4*iy² + a5*ix*iy,
    /// fit per axis from the 9 calibration means via least-squares.
    /// </summary>
    public class PolynomialFitData
    {
        /// <summary>X-axis coefficients [a0, a1, a2, a3, a4, a5].</summary>
        [JsonProperty] public double[] X { get; set; } = new double[6];

        /// <summary>Y-axis coefficients [b0, b1, b2, b3, b4, b5].</summary>
        [JsonProperty] public double[] Y { get; set; } = new double[6];
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
}
