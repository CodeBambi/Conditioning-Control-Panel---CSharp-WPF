using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a companion phrase for display in the phrase manager.
    /// </summary>
    public class CompanionPhrase : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string Category { get; set; } = "";
        public bool IsBuiltIn { get; set; }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string? AudioFileName { get; set; }

        /// <summary>
        /// Override folder for audio lookup (e.g., flashes_audio/ for voice lines).
        /// If null, uses the default companion_audio/ folder.
        /// </summary>
        public string? AudioFolder { get; set; }

        public bool HasAudio => !string.IsNullOrEmpty(AudioFileName) && AudioFileExists;

        public bool AudioFileExists
        {
            get
            {
                if (string.IsNullOrEmpty(AudioFileName)) return false;
                var folder = AudioFolder ?? DefaultAudioFolder;
                return File.Exists(Path.Combine(folder, AudioFileName));
            }
        }

        /// <summary>
        /// Gets the full path to the audio file.
        /// </summary>
        public string? AudioFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(AudioFileName)) return null;
                var folder = AudioFolder ?? DefaultAudioFolder;
                var path = Path.Combine(folder, AudioFileName);
                return File.Exists(path) ? path : null;
            }
        }

        public static string DefaultAudioFolder =>
            Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "companion_audio");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Persisted model for custom (user-added) companion phrases.
    /// </summary>
    public class CustomCompanionPhrase
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("text")]
        public string Text { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "Custom";

        [JsonProperty("audioFileName")]
        public string? AudioFileName { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;
    }
}
