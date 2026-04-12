using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A single entry in the Awareness Engine's live pulse ring buffer.
    /// Snapshots a trigger fire for display in the "Last Detected" feed.
    /// </summary>
    public class TriggerFireRecord
    {
        public string Keyword { get; set; } = "";
        public string TriggerId { get; set; } = "";
        public KeywordVisualEffect VisualEffect { get; set; }
        /// <summary>Source that detected the match: "OCR", "Keyboard", "Text"</summary>
        public string Source { get; set; } = "";
        public DateTime FiredAt { get; set; }

        /// <summary>
        /// Snapshot of the action types that ran for this fire, captured at
        /// fire time. Used by the "Last Detected" pulse feed to draw per-action
        /// icon chips with tooltips. Format is one entry per action:
        ///   "PlayAudio", "Highlight", "Haptic", "AddXp:5", "AvatarComment",
        ///   "VisualEffect:ImageFlash", "VisualEffect:ExactSubliminal", etc.
        /// Stored as strings (not live action references) so the feed survives
        /// preset uninstalls and later trigger edits.
        /// </summary>
        public List<string> ActionKeys { get; set; } = new();
    }

    public enum KeywordMatchType
    {
        PlainText,
        Regex
    }

    public enum KeywordVisualEffect
    {
        None,
        HighlightOnly,
        SubliminalFlash,
        ExactSubliminal,
        ImageFlash,
        OverlayPulse,
        MindWipe,
        Bubbles
    }

    /// <summary>
    /// Configuration for a single keyword trigger that fires multi-modal responses
    /// when the keyword is detected in typed text.
    /// </summary>
    public class KeywordTrigger : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _id = GenerateId();
        /// <summary>Unique identifier for this trigger</summary>
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _keyword = "";
        /// <summary>The keyword or regex pattern to match</summary>
        public string Keyword
        {
            get => _keyword;
            set { _keyword = value ?? ""; OnPropertyChanged(); }
        }

        private KeywordMatchType _matchType = KeywordMatchType.PlainText;
        /// <summary>Whether to match as plain text or regex</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public KeywordMatchType MatchType
        {
            get => _matchType;
            set { _matchType = value; OnPropertyChanged(); }
        }

        private bool _enabled = true;
        /// <summary>Whether this trigger is active</summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private int _cooldownSeconds = 30;
        /// <summary>Cooldown in seconds before this trigger can fire again (1-3600)</summary>
        public int CooldownSeconds
        {
            get => _cooldownSeconds;
            set { _cooldownSeconds = Math.Clamp(value, 1, 3600); OnPropertyChanged(); }
        }

        // --- Audio ---

        private string? _audioFilePath;
        /// <summary>Path to audio file to play when triggered</summary>
        public string? AudioFilePath
        {
            get => _audioFilePath;
            set { _audioFilePath = value; OnPropertyChanged(); }
        }

        private int _audioVolume = 80;
        /// <summary>Audio playback volume (0-100)</summary>
        public int AudioVolume
        {
            get => _audioVolume;
            set { _audioVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private int _audioPlayCount = 1;
        /// <summary>Number of times to play the audio (1-5)</summary>
        public int AudioPlayCount
        {
            get => _audioPlayCount;
            set { _audioPlayCount = Math.Clamp(value, 1, 5); OnPropertyChanged(); }
        }

        private int _audioDelayBetweenMs = 200;
        /// <summary>Delay between repeated audio plays in ms</summary>
        public int AudioDelayBetweenMs
        {
            get => _audioDelayBetweenMs;
            set { _audioDelayBetweenMs = Math.Clamp(value, 0, 5000); OnPropertyChanged(); }
        }

        // --- Visuals ---

        private KeywordVisualEffect _visualEffect = KeywordVisualEffect.SubliminalFlash;
        /// <summary>Visual effect to trigger</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public KeywordVisualEffect VisualEffect
        {
            get => _visualEffect;
            set { _visualEffect = value; OnPropertyChanged(); }
        }

        // --- Haptics ---

        private bool _hapticEnabled = true;
        /// <summary>Whether to fire haptic feedback</summary>
        public bool HapticEnabled
        {
            get => _hapticEnabled;
            set { _hapticEnabled = value; OnPropertyChanged(); }
        }

        private double _hapticIntensity = 0.5;
        /// <summary>Haptic intensity (0.0-1.0)</summary>
        public double HapticIntensity
        {
            get => _hapticIntensity;
            set { _hapticIntensity = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
        }

        // --- Other ---

        private bool _duckAudio = true;
        /// <summary>Whether to duck system audio during trigger response</summary>
        public bool DuckAudio
        {
            get => _duckAudio;
            set { _duckAudio = value; OnPropertyChanged(); }
        }

        private int _xpAward = 10;
        /// <summary>XP awarded when this trigger fires (0-100)</summary>
        public int XPAward
        {
            get => _xpAward;
            set { _xpAward = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        // --- Composable action list (Phase 1 refactor) ---
        //
        // When non-empty, KeywordTriggerService dispatches by iterating this list, which
        // allows a trigger to combine audio + visual + avatar comment + xp in a single
        // fire. When empty (legacy save / defensive fallback), the service falls back to
        // the flat fields above so older data keeps working. SettingsService synthesizes
        // this list on load for any trigger that doesn't have one, and the Exclusives
        // editor calls RebuildActionsFromFlatFields after each edit to keep them in sync.

        private List<KeywordAction> _actions = new();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<KeywordAction> Actions
        {
            get => _actions;
            set { _actions = value ?? new List<KeywordAction>(); OnPropertyChanged(); }
        }

        /// <summary>Last time this trigger fired (runtime only, not persisted)</summary>
        [JsonIgnore]
        public DateTime LastTriggeredAt { get; set; } = DateTime.MinValue;

        /// <summary>Whether this trigger is currently on cooldown</summary>
        [JsonIgnore]
        public bool IsOnCooldown => (DateTime.Now - LastTriggeredAt).TotalSeconds < CooldownSeconds;

        private static string GenerateId()
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        /// <summary>Create a deep copy of this trigger</summary>
        public KeywordTrigger Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<KeywordTrigger>(json)!;
        }
    }
}
