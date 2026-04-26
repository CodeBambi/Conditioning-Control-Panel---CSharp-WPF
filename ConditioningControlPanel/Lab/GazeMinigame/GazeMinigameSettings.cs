using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// Where to deliver the haptic cue, if any. Reward = on Correct, Punishment = on Wrong.
    /// </summary>
    public enum GazeVibrationMode { None, OnCorrect, OnWrong }

    /// <summary>
    /// What visual/audio effect to fire when the user nails a Correct round.
    /// Mirrors the small effect set used by the keyword-trigger system.
    /// </summary>
    public enum GazeRewardEffect { None, Flashes, Bubbles, Audio, MindWipe, OverlayPulse }

    /// <summary>
    /// Per-user persisted settings for the gaze minigame. Stored separately
    /// from AppSettings.cs so the minigame stays self-contained and adding
    /// future minigame knobs doesn't bloat the main settings file.
    /// </summary>
    public sealed class GazeMinigameSettings
    {
        [JsonProperty] public int ImageCount { get; set; } = 8;
        [JsonProperty] public int VideoCount { get; set; } = 2;
        // ImageDurationSec / VideoMaxDurationSec are MAX-DISPLAY safety nets.
        // The actual win condition is PassTimeSec — gaze-on-correct dwell time.
        // The display caps just bound how long an asset can stay on screen if
        // the user neither passes nor fails, so the round can't hang forever.
        [JsonProperty] public int ImageDurationSec { get; set; } = 5;
        [JsonProperty] public int VideoMaxDurationSec { get; set; } = 30;
        [JsonProperty] public int PassTimeSec { get; set; } = 3;
        // 0 = strict mode: any glance at the noise side fires WRONG immediately.
        // Tuning knob retained (no UI) in case saccades cause spurious fires.
        [JsonProperty] public int WrongHoldMs { get; set; } = 0;

        // Reward / punishment hooks. Persisted as strings so the JSON file
        // stays human-readable + adding new enum members later doesn't shift
        // numeric values.
        [JsonProperty][JsonConverter(typeof(StringEnumConverter))]
        public GazeVibrationMode VibrationMode { get; set; } = GazeVibrationMode.None;

        [JsonProperty][JsonConverter(typeof(StringEnumConverter))]
        public GazeRewardEffect RewardEffect { get; set; } = GazeRewardEffect.None;

        // File name only (resolved at runtime to Resources/AwarenessPresets/audio/).
        // Limited to bundled audios so the dropdown stays in sync with what ships.
        [JsonProperty] public string RewardAudioFile { get; set; } = "bell.wav";

        public static readonly string[] BundledAudioFiles =
        {
            "bell.wav", "chime.wav", "clicker.mp3", "lock-click.mp3"
        };

        // Range guards — kept here so the UI sliders have a single source of truth.
        public const int ImageCountMin = 0, ImageCountMax = 20;
        public const int VideoCountMin = 0, VideoCountMax = 10;
        public const int ImageDurationMin = 2, ImageDurationMax = 10;
        public const int VideoDurationMin = 10, VideoDurationMax = 120;
        public const int PassTimeMin = 3, PassTimeMax = 30;
        public const int WrongHoldMinMs = 0, WrongHoldMaxMs = 2000;

        public const string FileName = "gaze-minigame-settings.json";

        private static string FilePath => Path.Combine(App.UserDataPath, FileName);

        public static GazeMinigameSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new GazeMinigameSettings();
                var json = File.ReadAllText(FilePath);
                var loaded = JsonConvert.DeserializeObject<GazeMinigameSettings>(json);
                return loaded != null ? loaded.Clamp() : new GazeMinigameSettings();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigameSettings.Load: failed, using defaults");
                return new GazeMinigameSettings();
            }
        }

        public void Save()
        {
            try
            {
                Clamp();
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigameSettings.Save: failed");
            }
        }

        private GazeMinigameSettings Clamp()
        {
            ImageCount = Math.Clamp(ImageCount, ImageCountMin, ImageCountMax);
            VideoCount = Math.Clamp(VideoCount, VideoCountMin, VideoCountMax);
            ImageDurationSec = Math.Clamp(ImageDurationSec, ImageDurationMin, ImageDurationMax);
            VideoMaxDurationSec = Math.Clamp(VideoMaxDurationSec, VideoDurationMin, VideoDurationMax);
            PassTimeSec = Math.Clamp(PassTimeSec, PassTimeMin, PassTimeMax);
            WrongHoldMs = Math.Clamp(WrongHoldMs, WrongHoldMinMs, WrongHoldMaxMs);
            return this;
        }
    }
}
