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

        /// <summary>"TwoPoint" (gaze-side only) or "FivePoint" (precise gaze).</summary>
        [JsonProperty] public string Mode { get; set; } = "";

        [JsonProperty] public DateTime Timestamp { get; set; }

        [JsonProperty] public MonitorBoundsRecord? MonitorBounds { get; set; }

        [JsonProperty] public string PrimaryDeviceId { get; set; } = "";

        [JsonProperty] public double[] LeftRefVec { get; set; } = new double[2];

        [JsonProperty] public double[] RightRefVec { get; set; } = new double[2];

        /// <summary>3x3 homography mapping iris vector to screen coords. Null in TwoPoint mode.</summary>
        [JsonProperty] public double[][]? Homography { get; set; }

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
}
