using System.ComponentModel;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Available vibration modes/patterns for haptic feedback
    /// </summary>
    public enum VibrationMode
    {
        Constant,   // Steady vibration at set intensity
        Pulse,      // Quick on/off pulses
        Wave,       // Smooth ramp up/down
        Heartbeat,  // Double pulse pattern
        Escalate,   // Ramps up intensity
        Earthquake  // Random intensity variations
    }

    public class HapticSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _enabled = true;
        private HapticProviderType _provider = HapticProviderType.Mock;
        private bool _autoConnect = false;
        private double _globalIntensity = 0.7;

        // Per-event enabled flags
        private bool _bubblePopEnabled = true;
        private bool _flashDisplayEnabled = true;
        private bool _flashClickEnabled = true;
        private bool _videoEnabled = true;
        private bool _targetHitEnabled = true;
        private bool _subliminalEnabled = true;
        private bool _levelUpEnabled = true;
        private bool _achievementEnabled = true;
        private bool _bouncingTextEnabled = true;
        private bool _blinkEnabled = true;

        // Per-event intensity (0.0 to 1.0) - slider directly controls device power
        // 50% default so users can increase or decrease from baseline
        private double _bubblePopIntensity = 0.5;
        private double _flashDisplayIntensity = 0.5;
        private double _flashClickIntensity = 0.5;
        private double _videoIntensity = 0.5;
        private double _targetHitIntensity = 0.7;
        private double _subliminalIntensity = 0.5;
        private double _levelUpIntensity = 0.5;
        private double _achievementIntensity = 0.5;
        private double _bouncingTextIntensity = 0.5;
        private double _blinkIntensity = 0.6;

        // Per-event vibration mode - user selects pattern type
        private VibrationMode _bubblePopMode = VibrationMode.Constant;
        private VibrationMode _flashDisplayMode = VibrationMode.Constant;
        private VibrationMode _flashClickMode = VibrationMode.Constant;
        private VibrationMode _videoMode = VibrationMode.Constant;
        private VibrationMode _targetHitMode = VibrationMode.Pulse;
        private VibrationMode _subliminalMode = VibrationMode.Pulse;
        private VibrationMode _levelUpMode = VibrationMode.Escalate;
        private VibrationMode _achievementMode = VibrationMode.Heartbeat;
        private VibrationMode _bouncingTextMode = VibrationMode.Pulse;
        private VibrationMode _blinkMode = VibrationMode.Pulse;

        // Connection URLs - defaults shown in tooltip guide
        private string _lovenseUrl = "http://192.168.1.1:30010";
        private string _buttplugUrl = "ws://localhost:12345";

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public HapticProviderType Provider
        {
            get => _provider;
            set { _provider = value; OnPropertyChanged(); }
        }

        public bool AutoConnect
        {
            get => _autoConnect;
            set { _autoConnect = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// MASTER INTENSITY MULTIPLIER (0..1). Dead weight until v6.7 — the value was written by
        /// the Haptics tab slider and read by nothing. It is now applied by
        /// <see cref="Services.Haptics.Core.HapticMixer"/> to every output, after layer/pulse
        /// mixing and before <see cref="HapticSettingsV2.MasterCap"/> clamps it. Existing users
        /// already have a value in settings.json (default 0.7), so it is deliberately NOT
        /// renamed and NOT reset.
        /// </summary>
        public double GlobalIntensity
        {
            get => _globalIntensity;
            set { _globalIntensity = value; OnPropertyChanged(); }
        }

        public bool BubblePopEnabled
        {
            get => _bubblePopEnabled;
            set { _bubblePopEnabled = value; OnPropertyChanged(); }
        }

        public bool FlashDisplayEnabled
        {
            get => _flashDisplayEnabled;
            set { _flashDisplayEnabled = value; OnPropertyChanged(); }
        }

        public bool FlashClickEnabled
        {
            get => _flashClickEnabled;
            set { _flashClickEnabled = value; OnPropertyChanged(); }
        }

        public bool VideoEnabled
        {
            get => _videoEnabled;
            set { _videoEnabled = value; OnPropertyChanged(); }
        }

        public bool TargetHitEnabled
        {
            get => _targetHitEnabled;
            set { _targetHitEnabled = value; OnPropertyChanged(); }
        }

        public bool SubliminalEnabled
        {
            get => _subliminalEnabled;
            set { _subliminalEnabled = value; OnPropertyChanged(); }
        }

        public bool LevelUpEnabled
        {
            get => _levelUpEnabled;
            set { _levelUpEnabled = value; OnPropertyChanged(); }
        }

        public bool AchievementEnabled
        {
            get => _achievementEnabled;
            set { _achievementEnabled = value; OnPropertyChanged(); }
        }

        public bool BouncingTextEnabled
        {
            get => _bouncingTextEnabled;
            set { _bouncingTextEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Pulse the device on each blink detected by the Lab "Blink Trainer".
        /// Defaults on; no Haptics-tab UI yet (configurable via settings JSON) —
        /// dedicated toggle/slider deferred to a polish pass.
        /// </summary>
        public bool BlinkEnabled
        {
            get => _blinkEnabled;
            set { _blinkEnabled = value; OnPropertyChanged(); }
        }

        // Per-event intensity properties
        public double BubblePopIntensity
        {
            get => _bubblePopIntensity;
            set { _bubblePopIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double FlashDisplayIntensity
        {
            get => _flashDisplayIntensity;
            set { _flashDisplayIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double FlashClickIntensity
        {
            get => _flashClickIntensity;
            set { _flashClickIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double VideoIntensity
        {
            get => _videoIntensity;
            set { _videoIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double TargetHitIntensity
        {
            get => _targetHitIntensity;
            set { _targetHitIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double SubliminalIntensity
        {
            get => _subliminalIntensity;
            set { _subliminalIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double LevelUpIntensity
        {
            get => _levelUpIntensity;
            set { _levelUpIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double AchievementIntensity
        {
            get => _achievementIntensity;
            set { _achievementIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double BouncingTextIntensity
        {
            get => _bouncingTextIntensity;
            set { _bouncingTextIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        public double BlinkIntensity
        {
            get => _blinkIntensity;
            set { _blinkIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        // Per-event vibration mode properties
        public VibrationMode BubblePopMode
        {
            get => _bubblePopMode;
            set { _bubblePopMode = value; OnPropertyChanged(); }
        }

        public VibrationMode FlashDisplayMode
        {
            get => _flashDisplayMode;
            set { _flashDisplayMode = value; OnPropertyChanged(); }
        }

        public VibrationMode FlashClickMode
        {
            get => _flashClickMode;
            set { _flashClickMode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// DEAD. The video background vibe is a continuous layer, not a pattern, so a
        /// "vibration mode" never applied to it and nothing has ever read this. Kept so old
        /// settings.json files keep round-tripping; the Haptics tab combo that writes it is
        /// removed in the Phase E UI rebuild.
        /// </summary>
        [Obsolete("Never applied to anything. Kept for settings compatibility; the UI row is removed in the Phase E haptics tab rebuild.")]
        public VibrationMode VideoMode
        {
            get => _videoMode;
            set { _videoMode = value; OnPropertyChanged(); }
        }

        public VibrationMode TargetHitMode
        {
            get => _targetHitMode;
            set { _targetHitMode = value; OnPropertyChanged(); }
        }

        public VibrationMode SubliminalMode
        {
            get => _subliminalMode;
            set { _subliminalMode = value; OnPropertyChanged(); }
        }

        public VibrationMode LevelUpMode
        {
            get => _levelUpMode;
            set { _levelUpMode = value; OnPropertyChanged(); }
        }

        public VibrationMode AchievementMode
        {
            get => _achievementMode;
            set { _achievementMode = value; OnPropertyChanged(); }
        }

        public VibrationMode BouncingTextMode
        {
            get => _bouncingTextMode;
            set { _bouncingTextMode = value; OnPropertyChanged(); }
        }

        public VibrationMode BlinkMode
        {
            get => _blinkMode;
            set { _blinkMode = value; OnPropertyChanged(); }
        }

        public string LovenseUrl
        {
            get => _lovenseUrl;
            set { _lovenseUrl = value; OnPropertyChanged(); }
        }

        public string ButtplugUrl
        {
            get => _buttplugUrl;
            set { _buttplugUrl = value; OnPropertyChanged(); }
        }

        // ---- Down the Rabbit Hole (browser game) director ----
        private bool _dtrhEnabled = true;
        private double _dtrhIntensity = 0.6;
        private double _dtrhAmbientIntensity = 0.12;
        private int _dtrhDensity = 1;

        /// <summary>Event-driven haptics during a DtRH descent (DtrhHapticDirector).</summary>
        public bool DtrhEnabled
        {
            get => _dtrhEnabled;
            set { _dtrhEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>Accent ceiling: device power at the run's biggest moments; every
        /// event in the director's table plays at a fraction of this.</summary>
        public double DtrhIntensity
        {
            get => _dtrhIntensity;
            set { _dtrhIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        /// <summary>Ambient "depth gauge" floor at full depth (0 = accents only).
        /// The director scales this with the game's own 0..1 depth signal.</summary>
        public double DtrhAmbientIntensity
        {
            get => _dtrhAmbientIntensity;
            set { _dtrhAmbientIntensity = Math.Clamp(value, 0, 1); OnPropertyChanged(); }
        }

        /// <summary>How often accents may fire: 0=Sparse (big moments only),
        /// 1=Balanced, 2=Rich (micro-events too).</summary>
        public int DtrhDensity
        {
            get => _dtrhDensity;
            set { _dtrhDensity = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
        }

        // Audio Sync settings
        private AudioSyncSettings _audioSync = new();

        /// <summary>
        /// Settings for audio-synced haptics during video playback
        /// </summary>
        public AudioSyncSettings AudioSync
        {
            get => _audioSync;
            set { _audioSync = value ?? new(); OnPropertyChanged(); }
        }

        // ================================================================================
        // v2 engine config (mixer / device manager / routing matrix)
        // ================================================================================
        // EVERY member below is NEW, so every one carries an explicit [JsonProperty] — unlike
        // the legacy properties above, which have none and therefore serialize as PascalCase.
        // Renaming any legacy property silently resets that user's setting, so none of them
        // move, ever. The v2 block is additive: an old settings.json simply has no "v2" key,
        // the default instance survives deserialization untouched, and EnsureV2Migrated()
        // seeds the routing rows from the legacy per-feature values on first run.

        private HapticSettingsV2 _v2 = new();
        private bool _v2Migrated;

        /// <summary>Nested v2 engine config. Never null.</summary>
        [JsonProperty("v2")]
        public HapticSettingsV2 V2
        {
            get => _v2;
            set { _v2 = value ?? new HapticSettingsV2(); OnPropertyChanged(); }
        }

        /// <summary>
        /// One-shot migration from the flat legacy properties to the v2 routing matrix. Idempotent
        /// and cheap; called by HapticService/HapticMixer construction rather than by
        /// SettingsService so the settings loader stays untouched.
        /// </summary>
        public void EnsureV2Migrated()
        {
            if (_v2Migrated) return;
            _v2Migrated = true;      // set FIRST: the seeding below writes rows, not legacy props

            var v2 = _v2;
            v2.EnsureRows();

            if (v2.SchemaVersion < 1)
            {
                SeedRoutingFromLegacy(v2);
                SeedProvidersFromLegacy(v2);

                // GlobalIntensity ("Intensity" on the Haptics tab, default 70) was WRITTEN by
                // that slider but READ by nothing, so a user could have parked it at 0 and felt
                // no difference. It becomes a real master multiplier now, and a stored 0 would
                // mean total silence with no obvious cause. One-shot: rescue effectively-zero
                // values back to the default. Anything the user could plausibly have meant is
                // left exactly as it is.
                if (_globalIntensity <= 0.05)
                {
                    _globalIntensity = 0.7;
                    OnPropertyChanged(nameof(GlobalIntensity));
                }

                v2.SchemaVersion = 1;
            }
        }

        private void SeedRoutingFromLegacy(HapticSettingsV2 v2)
        {
            void Ev(HapticEventKind kind, bool enabled, double intensity, VibrationMode mode)
            {
                var r = v2.Rule(kind);
                r.Enabled = enabled;
                r.Intensity = Math.Clamp(intensity, 0, 1);
                r.Mode = mode;
            }

            Ev(HapticEventKind.FlashClick, _flashClickEnabled, _flashClickIntensity, _flashClickMode);
            Ev(HapticEventKind.FlashDecay, _flashDisplayEnabled, _flashDisplayIntensity, _flashDisplayMode);
            Ev(HapticEventKind.SubliminalTrigger, _subliminalEnabled, _subliminalIntensity, _subliminalMode);
            // Keyword haptics never had their own toggle: intensity comes from the trigger's own
            // action config, and only the master toggle gated it. Preserve that.
            Ev(HapticEventKind.KeywordTrigger, true, _subliminalIntensity, _subliminalMode);
            Ev(HapticEventKind.BubblePop, _bubblePopEnabled, _bubblePopIntensity, _bubblePopMode);
            Ev(HapticEventKind.BouncingTextBounce, _bouncingTextEnabled, _bouncingTextIntensity, _bouncingTextMode);
            Ev(HapticEventKind.BlinkPulse, _blinkEnabled, _blinkIntensity, _blinkMode);
            Ev(HapticEventKind.VideoTargetHit, _targetHitEnabled, _targetHitIntensity, _targetHitMode);
            Ev(HapticEventKind.Achievement, _achievementEnabled, _achievementIntensity, _achievementMode);
            // Quests and the avatar easter egg have always ridden the Achievement row.
            Ev(HapticEventKind.QuestComplete, _achievementEnabled, _achievementIntensity, _achievementMode);
            Ev(HapticEventKind.AvatarEasterEgg, _achievementEnabled, _achievementIntensity, _achievementMode);
            Ev(HapticEventKind.LevelUp, _levelUpEnabled, _levelUpIntensity, _levelUpMode);
            // Gaze rewards were routed through the subliminal pattern.
            Ev(HapticEventKind.GazeReward, _subliminalEnabled, _subliminalIntensity, _subliminalMode);
            Ev(HapticEventKind.DtrhAccent, _dtrhEnabled, _dtrhIntensity, VibrationMode.Constant);
            Ev(HapticEventKind.AiCommand, true, 0.7, VibrationMode.Pulse);
            Ev(HapticEventKind.ToyButtonReward, true, 0.5, VibrationMode.Pulse);

            void Ly(HapticLayer layer, bool enabled, double intensity)
            {
                var r = v2.Rule(layer);
                r.Enabled = enabled;
                r.Intensity = Math.Clamp(intensity, 0, 1);
            }

            // Layer "intensity" is a SCALE on whatever the feature sets, so 1.0 = pass through.
            Ly(HapticLayer.Video, _videoEnabled, 1.0);
            Ly(HapticLayer.AudioSync, _audioSync?.Enabled ?? false, 1.0);
            Ly(HapticLayer.Dtrh, _dtrhEnabled, 1.0);
            Ly(HapticLayer.Luminance, false, 1.0);   // Phase F, off until it exists
            Ly(HapticLayer.Session, true, 1.0);
            Ly(HapticLayer.Manual, true, 1.0);
        }

        private void SeedProvidersFromLegacy(HapticSettingsV2 v2)
        {
            // v1 could only ever have ONE provider active. Carry that choice forward; the v2
            // manager can run several at once, which the Phase E UI exposes.
            v2.Provider("mock").Enabled = _provider == HapticProviderType.Mock;
            v2.Provider("lovense").Enabled = _provider == HapticProviderType.Lovense;
            v2.Provider("buttplug").Enabled = _provider == HapticProviderType.Buttplug;
            v2.Provider("lovense").Url = _lovenseUrl;
            v2.Provider("buttplug").Url = _buttplugUrl;
        }

        /// <summary>
        /// Keeps the v2 routing rows in step with the LEGACY properties, which the current
        /// (pre-Phase-E) Haptics tab still writes. Without this, moving a slider in today's UI
        /// would change nothing, because the mixer reads rows.
        /// </summary>
        private void SyncLegacyToRouting(string? name)
        {
            if (!_v2Migrated || name == null) return;
            var v2 = _v2;

            switch (name)
            {
                case nameof(BubblePopEnabled): v2.Rule(HapticEventKind.BubblePop).Enabled = _bubblePopEnabled; break;
                case nameof(BubblePopIntensity): v2.Rule(HapticEventKind.BubblePop).Intensity = _bubblePopIntensity; break;
                case nameof(BubblePopMode): v2.Rule(HapticEventKind.BubblePop).Mode = _bubblePopMode; break;

                case nameof(FlashDisplayEnabled): v2.Rule(HapticEventKind.FlashDecay).Enabled = _flashDisplayEnabled; break;
                case nameof(FlashDisplayIntensity): v2.Rule(HapticEventKind.FlashDecay).Intensity = _flashDisplayIntensity; break;
                case nameof(FlashDisplayMode): v2.Rule(HapticEventKind.FlashDecay).Mode = _flashDisplayMode; break;

                case nameof(FlashClickEnabled): v2.Rule(HapticEventKind.FlashClick).Enabled = _flashClickEnabled; break;
                case nameof(FlashClickIntensity): v2.Rule(HapticEventKind.FlashClick).Intensity = _flashClickIntensity; break;
                case nameof(FlashClickMode): v2.Rule(HapticEventKind.FlashClick).Mode = _flashClickMode; break;

                case nameof(TargetHitEnabled): v2.Rule(HapticEventKind.VideoTargetHit).Enabled = _targetHitEnabled; break;
                case nameof(TargetHitIntensity): v2.Rule(HapticEventKind.VideoTargetHit).Intensity = _targetHitIntensity; break;
                case nameof(TargetHitMode): v2.Rule(HapticEventKind.VideoTargetHit).Mode = _targetHitMode; break;

                case nameof(SubliminalEnabled):
                    v2.Rule(HapticEventKind.SubliminalTrigger).Enabled = _subliminalEnabled;
                    v2.Rule(HapticEventKind.GazeReward).Enabled = _subliminalEnabled;
                    break;
                case nameof(SubliminalIntensity):
                    v2.Rule(HapticEventKind.SubliminalTrigger).Intensity = _subliminalIntensity;
                    v2.Rule(HapticEventKind.GazeReward).Intensity = _subliminalIntensity;
                    break;
                case nameof(SubliminalMode):
                    v2.Rule(HapticEventKind.SubliminalTrigger).Mode = _subliminalMode;
                    v2.Rule(HapticEventKind.GazeReward).Mode = _subliminalMode;
                    v2.Rule(HapticEventKind.KeywordTrigger).Mode = _subliminalMode;
                    break;

                case nameof(LevelUpEnabled): v2.Rule(HapticEventKind.LevelUp).Enabled = _levelUpEnabled; break;
                case nameof(LevelUpIntensity): v2.Rule(HapticEventKind.LevelUp).Intensity = _levelUpIntensity; break;
                case nameof(LevelUpMode): v2.Rule(HapticEventKind.LevelUp).Mode = _levelUpMode; break;

                case nameof(AchievementEnabled):
                    v2.Rule(HapticEventKind.Achievement).Enabled = _achievementEnabled;
                    v2.Rule(HapticEventKind.QuestComplete).Enabled = _achievementEnabled;
                    v2.Rule(HapticEventKind.AvatarEasterEgg).Enabled = _achievementEnabled;
                    break;
                case nameof(AchievementIntensity):
                    v2.Rule(HapticEventKind.Achievement).Intensity = _achievementIntensity;
                    v2.Rule(HapticEventKind.QuestComplete).Intensity = _achievementIntensity;
                    v2.Rule(HapticEventKind.AvatarEasterEgg).Intensity = _achievementIntensity;
                    break;
                case nameof(AchievementMode):
                    v2.Rule(HapticEventKind.Achievement).Mode = _achievementMode;
                    v2.Rule(HapticEventKind.QuestComplete).Mode = _achievementMode;
                    v2.Rule(HapticEventKind.AvatarEasterEgg).Mode = _achievementMode;
                    break;

                case nameof(BouncingTextEnabled): v2.Rule(HapticEventKind.BouncingTextBounce).Enabled = _bouncingTextEnabled; break;
                case nameof(BouncingTextIntensity): v2.Rule(HapticEventKind.BouncingTextBounce).Intensity = _bouncingTextIntensity; break;
                case nameof(BouncingTextMode): v2.Rule(HapticEventKind.BouncingTextBounce).Mode = _bouncingTextMode; break;

                case nameof(BlinkEnabled): v2.Rule(HapticEventKind.BlinkPulse).Enabled = _blinkEnabled; break;
                case nameof(BlinkIntensity): v2.Rule(HapticEventKind.BlinkPulse).Intensity = _blinkIntensity; break;
                case nameof(BlinkMode): v2.Rule(HapticEventKind.BlinkPulse).Mode = _blinkMode; break;

                case nameof(VideoEnabled): v2.Rule(HapticLayer.Video).Enabled = _videoEnabled; break;

                case nameof(DtrhEnabled):
                    v2.Rule(HapticLayer.Dtrh).Enabled = _dtrhEnabled;
                    v2.Rule(HapticEventKind.DtrhAccent).Enabled = _dtrhEnabled;
                    break;
                case nameof(DtrhIntensity): v2.Rule(HapticEventKind.DtrhAccent).Intensity = _dtrhIntensity; break;

                case nameof(Provider):
                    v2.Provider("mock").Enabled = _provider == HapticProviderType.Mock;
                    v2.Provider("lovense").Enabled = _provider == HapticProviderType.Lovense;
                    v2.Provider("buttplug").Enabled = _provider == HapticProviderType.Buttplug;
                    break;
                case nameof(LovenseUrl): v2.Provider("lovense").Url = _lovenseUrl; break;
                case nameof(ButtplugUrl): v2.Provider("buttplug").Url = _buttplugUrl; break;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            SyncLegacyToRouting(name);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ================================================================================
    // v2 config objects. All members carry explicit [JsonProperty] names.
    // ================================================================================

    /// <summary>Per-event routing row: does it fire, how hard, with what pattern, at which toys.</summary>
    public class HapticEventRule
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("intensity")] public double Intensity { get; set; } = 0.5;
        [JsonProperty("mode")] public VibrationMode Mode { get; set; } = VibrationMode.Constant;
        /// <summary>Which toy ROLE hears this row (the matrix is by role, not by individual toy).</summary>
        [JsonProperty("target")] public ToyRole Target { get; set; } = ToyRole.All;
    }

    /// <summary>Per-layer rule for the continuous sources. <c>Intensity</c> is a SCALE (1.0 = pass through).</summary>
    public class HapticLayerRule
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("intensity")] public double Intensity { get; set; } = 1.0;
        [JsonProperty("target")] public ToyRole Target { get; set; } = ToyRole.All;
    }

    /// <summary>User config for one physical toy, persisted by <c>"{provider}:{id}"</c>.</summary>
    public class HapticDeviceConfig
    {
        [JsonProperty("role")] public ToyRole Role { get; set; } = ToyRole.All;
        [JsonProperty("trim")] public double IntensityTrim { get; set; } = 1.0;
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("nickname")] public string Nickname { get; set; } = "";
        /// <summary>Last name we saw for this key, so the UI can list remembered-but-absent toys.</summary>
        [JsonProperty("name")] public string LastKnownName { get; set; } = "";
    }

    public class HapticProviderConfig
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("url")] public string Url { get; set; } = "";
    }

    /// <summary>
    /// Engine-level config for the v2 haptics stack. Additive: absent from old settings.json,
    /// defaulted here, seeded from the legacy properties by <see cref="HapticSettings.EnsureV2Migrated"/>.
    /// </summary>
    public class HapticSettingsV2
    {
        private readonly object _gate = new();

        /// <summary>0 = never migrated. Bump when a future release needs another seeding pass.</summary>
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }

        /// <summary>SAFETY: hard ceiling applied to every actuator after the master multiplier.
        /// 0.70 by default — full power is opt-in, not the accident you discover mid-session.</summary>
        [JsonProperty("master_cap")] public double MasterCap { get; set; } = HapticMixer.DefaultMasterCap;

        /// <summary>Time for the continuous floor to climb from silence to its target.</summary>
        [JsonProperty("soft_ramp_ms")] public int SoftRampMs { get; set; } = HapticMixer.DefaultSoftRampMs;

        /// <summary>Output loop rate. 10 Hz is the self-imposed Lovense LAN rate limit.</summary>
        [JsonProperty("output_hz")] public int OutputHz { get; set; } = 10;

        /// <summary>How many transient envelopes may overlap before the weakest gets evicted.</summary>
        [JsonProperty("max_concurrent_pulses")] public int MaxConcurrentPulses { get; set; } = HapticMixer.DefaultMaxConcurrentPulses;

        [JsonProperty("providers")] public Dictionary<string, HapticProviderConfig> Providers { get; set; } = new();
        [JsonProperty("devices")] public Dictionary<string, HapticDeviceConfig> Devices { get; set; } = new();
        /// <summary>Key = <see cref="HapticEventKind"/> member name (stable — they ARE settings keys).</summary>
        [JsonProperty("events")] public Dictionary<string, HapticEventRule> Events { get; set; } = new();
        /// <summary>Key = <see cref="HapticLayer"/> member name.</summary>
        [JsonProperty("layers")] public Dictionary<string, HapticLayerRule> Layers { get; set; } = new();

        /// <summary>Create every row up front. After this the mixer only ever reads existing rows,
        /// so it never races a dictionary mutation on the UI thread.</summary>
        public void EnsureRows()
        {
            lock (_gate)
            {
                Providers ??= new Dictionary<string, HapticProviderConfig>();
                Devices ??= new Dictionary<string, HapticDeviceConfig>();
                Events ??= new Dictionary<string, HapticEventRule>();
                Layers ??= new Dictionary<string, HapticLayerRule>();

                foreach (HapticEventKind kind in Enum.GetValues(typeof(HapticEventKind)))
                {
                    var key = kind.ToString();
                    if (!Events.ContainsKey(key)) Events[key] = new HapticEventRule();
                }
                foreach (HapticLayer layer in Enum.GetValues(typeof(HapticLayer)))
                {
                    var key = layer.ToString();
                    if (!Layers.ContainsKey(key)) Layers[key] = new HapticLayerRule();
                }
                foreach (var key in new[] { "mock", "lovense", "buttplug" })
                    if (!Providers.ContainsKey(key)) Providers[key] = new HapticProviderConfig();
            }
        }

        public HapticEventRule Rule(HapticEventKind kind)
        {
            var key = kind.ToString();
            if (Events != null && Events.TryGetValue(key, out var r) && r != null) return r;
            lock (_gate)
            {
                Events ??= new Dictionary<string, HapticEventRule>();
                if (!Events.TryGetValue(key, out var row) || row == null) Events[key] = row = new HapticEventRule();
                return row;
            }
        }

        public HapticLayerRule Rule(HapticLayer layer)
        {
            var key = layer.ToString();
            if (Layers != null && Layers.TryGetValue(key, out var r) && r != null) return r;
            lock (_gate)
            {
                Layers ??= new Dictionary<string, HapticLayerRule>();
                if (!Layers.TryGetValue(key, out var row) || row == null) Layers[key] = row = new HapticLayerRule();
                return row;
            }
        }

        public HapticDeviceConfig Device(string deviceKey)
        {
            lock (_gate)
            {
                Devices ??= new Dictionary<string, HapticDeviceConfig>();
                if (!Devices.TryGetValue(deviceKey, out var cfg) || cfg == null)
                    Devices[deviceKey] = cfg = new HapticDeviceConfig();
                return cfg;
            }
        }

        public HapticProviderConfig Provider(string providerKey)
        {
            lock (_gate)
            {
                Providers ??= new Dictionary<string, HapticProviderConfig>();
                if (!Providers.TryGetValue(providerKey, out var cfg) || cfg == null)
                    Providers[providerKey] = cfg = new HapticProviderConfig();
                return cfg;
            }
        }
    }
}
