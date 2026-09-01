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

        /// <summary>True for reactive "bark" voicelines surfaced from <see cref="Services.BarkService"/>.</summary>
        public bool IsBark { get; set; }

        /// <summary>
        /// Display-only label the Phrase Manager groups rows under. For barks this is "Bark · {trigger}"
        /// so each rule gets its own header; for everything else it's the category's friendly name. Set
        /// by the manager when building rows — not persisted.
        /// </summary>
        public string GroupLabel { get; set; } = "";

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

        /// <summary>Voice-line phrases derive their text from the audio filename (see CompanionPhraseService.VoiceLineCategory).</summary>
        public bool IsVoiceLine => Category == "VoiceLine";

        /// <summary>What the audio column shows: built-in voice lines have inherent audio (no filename to surface).</summary>
        public string AudioDisplayName => (IsVoiceLine && IsBuiltIn) ? "Built-in audio" : (AudioFileName ?? "");

        /// <summary>Built-in voice-line and bark audio is inherent to the file, so it can't be cleared; everything else with audio can.</summary>
        public bool CanClearAudio => HasAudio && !((IsVoiceLine || IsBark) && IsBuiltIn);

        /// <summary>Barks play their own packaged audio (BarkService resolves it), so a manager-side override is meaningless — hide Browse for them.</summary>
        public bool CanBrowseAudio => !IsBark;

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
