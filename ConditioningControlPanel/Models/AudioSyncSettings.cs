using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Settings for audio-synced haptic feedback during video playback.
    /// Analyzes video audio to generate intensity values that sync haptics to the content.
    /// </summary>
    public class AudioSyncSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _enabled = false;
        private double _sensitivity = 1.0;
        private double _bassWeight = 0.40;
        private double _rmsWeight = 0.35;
        private double _onsetWeight = 0.25;
        private double _smoothing = 0.3;
        private double _minIntensity = 0.0;  // Allow full range - quiet parts can be off
        private double _maxIntensity = 1.0;
        private int _manualLatencyOffsetMs = 0;
        private int _chunkDurationSeconds = 300;  // 5 minutes
        private int _minBufferAheadSeconds = 120; // 2 minutes

        /// <summary>
        /// Enable audio-synced haptics for web videos
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Sensitivity multiplier for intensity (0.5 = gentle, 2.0 = aggressive)
        /// Applied as: intensity = pow(intensity, 1/sensitivity)
        /// </summary>
        [JsonProperty("sensitivity")]
        public double Sensitivity
        {
            get => _sensitivity;
            set { _sensitivity = Math.Clamp(value, 0.1, 3.0); OnPropertyChanged(); }
        }

        /// <summary>
        /// Weight for bass frequency energy (20-250Hz) in intensity calculation
        /// </summary>
        [JsonProperty("bass_weight")]
        public double BassWeight
        {
            get => _bassWeight;
            set { _bassWeight = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        /// <summary>
        /// Weight for RMS (overall volume) in intensity calculation
        /// </summary>
        [JsonProperty("rms_weight")]
        public double RmsWeight
        {
            get => _rmsWeight;
            set { _rmsWeight = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        /// <summary>
        /// Weight for onset detection (transients/beats) in intensity calculation
        /// </summary>
        [JsonProperty("onset_weight")]
        public double OnsetWeight
        {
            get => _onsetWeight;
            set { _onsetWeight = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        /// <summary>
        /// Smoothing factor for intensity output (0 = raw, 0.9 = very smooth)
        /// Applied as exponential moving average
        /// </summary>
        [JsonProperty("smoothing")]
        public double Smoothing
        {
            get => _smoothing;
            set { _smoothing = Math.Clamp(value, 0, 0.95); OnPropertyChanged(); }
        }

        /// <summary>
        /// Minimum intensity floor (device needs ~5% to respond)
        /// </summary>
        [JsonProperty("min_intensity")]
        public double MinIntensity
        {
            get => _minIntensity;
            set { _minIntensity = Math.Clamp(value, 0, 0.5); OnPropertyChanged(); }
        }

        /// <summary>
        /// Maximum intensity ceiling
        /// </summary>
        [JsonProperty("max_intensity")]
        public double MaxIntensity
        {
            get => _maxIntensity;
            set { _maxIntensity = Math.Clamp(value, 0.5, 1.0); OnPropertyChanged(); }
        }

        /// <summary>
        /// Manual latency offset in milliseconds (user fine-tuning)
        /// Positive = haptics earlier, Negative = haptics later
        /// </summary>
        [JsonProperty("manual_latency_offset_ms")]
        public int ManualLatencyOffsetMs
        {
            get => _manualLatencyOffsetMs;
            set { _manualLatencyOffsetMs = Math.Clamp(value, -2000, 2000); OnPropertyChanged(); }
        }

        /// <summary>
        /// Duration of each processing chunk in seconds (default 5 minutes)
        /// </summary>
        [JsonProperty("chunk_duration_seconds")]
        public int ChunkDurationSeconds
        {
            get => _chunkDurationSeconds;
            set { _chunkDurationSeconds = Math.Clamp(value, 60, 600); OnPropertyChanged(); }
        }

        /// <summary>
        /// Minimum buffer to maintain ahead of playback position in seconds
        /// </summary>
        [JsonProperty("min_buffer_ahead_seconds")]
        public int MinBufferAheadSeconds
        {
            get => _minBufferAheadSeconds;
            set { _minBufferAheadSeconds = Math.Clamp(value, 30, 300); OnPropertyChanged(); }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
