using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Application settings model - matches Python DEFAULT_SETTINGS
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #region Presets

        private string _currentPresetName = "Custom";
        public string CurrentPresetName
        {
            get => _currentPresetName;
            set { _currentPresetName = value ?? "Custom"; OnPropertyChanged(); }
        }

        private List<Preset> _userPresets = new();
        public List<Preset> UserPresets
        {
            get => _userPresets;
            set { _userPresets = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Player Progress

        private int _playerLevel = 1;
        public int PlayerLevel
        {
            get => _playerLevel;
            set { _playerLevel = value; OnPropertyChanged(); }
        }

        private double _playerXP = 0.0;
        public double PlayerXP
        {
            get => _playerXP;
            set { _playerXP = value; OnPropertyChanged(); }
        }

        private int _selectedAvatarSet = 0; // 0 = auto (use max unlocked)
        /// <summary>
        /// User's selected avatar set (1-6). 0 means auto-select highest unlocked.
        /// </summary>
        public int SelectedAvatarSet
        {
            get => _selectedAvatarSet;
            set { _selectedAvatarSet = Math.Clamp(value, 0, 6); OnPropertyChanged(); }
        }

        private bool _welcomed = false;
        public bool Welcomed
        {
            get => _welcomed;
            set { _welcomed = value; OnPropertyChanged(); }
        }

        private string _lastSeenVersion = "";
        /// <summary>
        /// Last version the user has seen patch notes for. Used to show "What's New" after updates.
        /// </summary>
        public string LastSeenVersion
        {
            get => _lastSeenVersion;
            set { _lastSeenVersion = value ?? ""; OnPropertyChanged(); }
        }

        #endregion

        #region Flash Images

        private bool _flashEnabled = true;
        public bool FlashEnabled
        {
            get => _flashEnabled;
            set { _flashEnabled = value; OnPropertyChanged(); }
        }

        private int _flashFrequency = 10; // Flashes per hour (1-180)
        public int FlashFrequency
        {
            get => _flashFrequency;
            set { _flashFrequency = Math.Clamp(value, 1, 180); OnPropertyChanged(); }
        }

        private bool _flashClickable = true;
        public bool FlashClickable
        {
            get => _flashClickable;
            set { _flashClickable = value; OnPropertyChanged(); }
        }

        private bool _corruptionMode = false; // Hydra effect
        public bool CorruptionMode
        {
            get => _corruptionMode;
            set { _corruptionMode = value; OnPropertyChanged(); }
        }

        private int _hydraLimit = 20; // Max images on screen (hard cap: 20)
        public int HydraLimit
        {
            get => _hydraLimit;
            set { _hydraLimit = Math.Clamp(value, 1, 20); OnPropertyChanged(); }
        }

        private int _simultaneousImages = 5; // Images per flash (1-20)
        public int SimultaneousImages
        {
            get => _simultaneousImages;
            set { _simultaneousImages = Math.Clamp(value, 1, 20); OnPropertyChanged(); }
        }

        private int _imageScale = 100; // 50-250% (100 = normal size, 200 = double, etc)
        /// <summary>
        /// Image scale as percentage. 50 = half size, 100 = normal, 200 = double size.
        /// Base size is 40% of monitor, then multiplied by this percentage.
        /// </summary>
        public int ImageScale
        {
            get => _imageScale;
            set { _imageScale = Math.Clamp(value, 50, 250); OnPropertyChanged(); }
        }

        private int _flashOpacity = 100; // 10-100%
        public int FlashOpacity
        {
            get => _flashOpacity;
            set { _flashOpacity = Math.Clamp(value, 10, 100); OnPropertyChanged(); }
        }

        private int _fadeDuration = 40; // 0-200 (0-2 seconds, stored as percentage)
        public int FadeDuration
        {
            get => _fadeDuration;
            set { _fadeDuration = Math.Clamp(value, 0, 200); OnPropertyChanged(); }
        }

        private bool _flashAudioEnabled = true; // Link flash duration to audio
        public bool FlashAudioEnabled
        {
            get => _flashAudioEnabled;
            set { _flashAudioEnabled = value; OnPropertyChanged(); }
        }

        private int _flashDuration = 5; // Duration in seconds when audio is disabled (1-30)
        public int FlashDuration
        {
            get => _flashDuration;
            set { _flashDuration = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        #endregion

        #region Mandatory Videos

        private bool _mandatoryVideosEnabled = true;
        public bool MandatoryVideosEnabled
        {
            get => _mandatoryVideosEnabled;
            set { _mandatoryVideosEnabled = value; OnPropertyChanged(); }
        }

        private int _videosPerHour = 6; // Videos per hour (1-20)
        public int VideosPerHour
        {
            get => _videosPerHour;
            set { _videosPerHour = Math.Clamp(value, 1, 20); OnPropertyChanged(); }
        }

        private bool _strictLockEnabled = false; // DANGEROUS: Cannot close video
        public bool StrictLockEnabled
        {
            get => _strictLockEnabled;
            set { _strictLockEnabled = value; OnPropertyChanged(); }
        }

        private bool _forceVideoOnLaunch = false;
        public bool ForceVideoOnLaunch
        {
            get => _forceVideoOnLaunch;
            set { _forceVideoOnLaunch = value; OnPropertyChanged(); }
        }

        private string? _startupVideoPath = null; // Specific video to play on startup (null = random)
        public string? StartupVideoPath
        {
            get => _startupVideoPath;
            set { _startupVideoPath = value; OnPropertyChanged(); }
        }

        private bool _attentionChecksEnabled = false;
        public bool AttentionChecksEnabled
        {
            get => _attentionChecksEnabled;
            set { _attentionChecksEnabled = value; OnPropertyChanged(); }
        }

        private int _attentionDensity = 3; // Target count (1-10)
        public int AttentionDensity
        {
            get => _attentionDensity;
            set { _attentionDensity = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private bool _randomizeAttentionTargets = false; // Randomize target count (1 to AttentionDensity)
        public bool RandomizeAttentionTargets
        {
            get => _randomizeAttentionTargets;
            set { _randomizeAttentionTargets = value; OnPropertyChanged(); }
        }

        private int _attentionLifespan = 12; // Seconds - longer to give time to click
        public int AttentionLifespan
        {
            get => _attentionLifespan;
            set { _attentionLifespan = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        private int _attentionSize = 70; // Pixels
        public int AttentionSize
        {
            get => _attentionSize;
            set { _attentionSize = Math.Clamp(value, 30, 150); OnPropertyChanged(); }
        }

        // Attention target styling
        private string _attentionColor1 = "#FF1493"; // Bright fluo pink (DeepPink)
        public string AttentionColor1
        {
            get => _attentionColor1;
            set { _attentionColor1 = value; OnPropertyChanged(); }
        }

        private string _attentionColor2 = "#FF69B4"; // Hot pink
        public string AttentionColor2
        {
            get => _attentionColor2;
            set { _attentionColor2 = value; OnPropertyChanged(); }
        }

        private string _attentionTextColor = "#FF1493"; // Bright fluo pink (for floating text mode)
        public string AttentionTextColor
        {
            get => _attentionTextColor;
            set { _attentionTextColor = value; OnPropertyChanged(); }
        }

        private bool _attentionShowBorder = false; // No border by default (cleaner look)
        public bool AttentionShowBorder
        {
            get => _attentionShowBorder;
            set { _attentionShowBorder = value; OnPropertyChanged(); }
        }

        private string _attentionBorderColor = "#FF1493"; // Bright fluo pink
        public string AttentionBorderColor
        {
            get => _attentionBorderColor;
            set { _attentionBorderColor = value; OnPropertyChanged(); }
        }

        private string _attentionFont = "Segoe UI"; // Clean modern font
        public string AttentionFont
        {
            get => _attentionFont;
            set { _attentionFont = value; OnPropertyChanged(); }
        }

        private bool _attentionFloatingText = true; // Floating text mode by default (no background)
        public bool AttentionFloatingText
        {
            get => _attentionFloatingText;
            set { _attentionFloatingText = value; OnPropertyChanged(); }
        }

        #endregion

        #region Audio

        private int _masterVolume = 32; // 0-100%
        public int MasterVolume
        {
            get => _masterVolume;
            set { _masterVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private int _videoVolume = 50; // 0-100%
        public int VideoVolume
        {
            get => _videoVolume;
            set { _videoVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private bool _audioDuckingEnabled = true;
        public bool AudioDuckingEnabled
        {
            get => _audioDuckingEnabled;
            set { _audioDuckingEnabled = value; OnPropertyChanged(); }
        }

        private int _duckingLevel = 100; // 0-100%
        public int DuckingLevel
        {
            get => _duckingLevel;
            set { _duckingLevel = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private bool _excludeBambiCloudFromDucking = true;
        /// <summary>
        /// When true, the integrated BambiCloud browser audio will not be ducked
        /// </summary>
        public bool ExcludeBambiCloudFromDucking
        {
            get => _excludeBambiCloudFromDucking;
            set { _excludeBambiCloudFromDucking = value; OnPropertyChanged(); }
        }

        private bool _backgroundMusicEnabled = true;
        public bool BackgroundMusicEnabled
        {
            get => _backgroundMusicEnabled;
            set { _backgroundMusicEnabled = value; OnPropertyChanged(); }
        }

        #endregion

        #region Subliminals

        private bool _subliminalEnabled = false;
        public bool SubliminalEnabled
        {
            get => _subliminalEnabled;
            set { _subliminalEnabled = value; OnPropertyChanged(); }
        }

        private int _subliminalFrequency = 5; // Messages per minute (1-30)
        public int SubliminalFrequency
        {
            get => _subliminalFrequency;
            set { _subliminalFrequency = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        private int _subliminalDuration = 2; // Frames (1-10)
        public int SubliminalDuration
        {
            get => _subliminalDuration;
            set { _subliminalDuration = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private int _subliminalOpacity = 80; // 10-100%
        public int SubliminalOpacity
        {
            get => _subliminalOpacity;
            set { _subliminalOpacity = Math.Clamp(value, 10, 100); OnPropertyChanged(); }
        }

        private Dictionary<string, bool> _subliminalPool = new()
        {
            { "BAMBI FREEZE", true },
            { "BAMBI RESET", true },
            { "BAMBI SLEEP", true },
            { "BIMBO DOLL", true },
            { "GOOD GIRL", true },
            { "DROP FOR COCK", true },
            { "SNAP AND FORGET", true },
            { "PRIMPED AND PAMPERED", true },
            { "BAMBI DOES AS SHE'S TOLD", true },
            { "BAMBI CUM AND COLLAPSE", true },
            { "ZAP COCK DRAIN OBEY", true },
            { "GIGGLETIME", true },
            { "BAMBI UNIFORM LOCK", true },
            { "COCK ZOMBIE NOW", true }
        };
        public Dictionary<string, bool> SubliminalPool
        {
            get => _subliminalPool;
            set { _subliminalPool = value ?? new(); OnPropertyChanged(); }
        }

        private string _subBackgroundColor = "#000000";
        public string SubBackgroundColor
        {
            get => _subBackgroundColor;
            set { _subBackgroundColor = value ?? "#000000"; OnPropertyChanged(); }
        }

        private bool _subBackgroundTransparent = false;
        public bool SubBackgroundTransparent
        {
            get => _subBackgroundTransparent;
            set { _subBackgroundTransparent = value; OnPropertyChanged(); }
        }

        private string _subTextColor = "#FF00FF";
        public string SubTextColor
        {
            get => _subTextColor;
            set { _subTextColor = value ?? "#FF00FF"; OnPropertyChanged(); }
        }

        private bool _subTextTransparent = false;
        public bool SubTextTransparent
        {
            get => _subTextTransparent;
            set { _subTextTransparent = value; OnPropertyChanged(); }
        }

        private string _subBorderColor = "#FFFFFF";
        public string SubBorderColor
        {
            get => _subBorderColor;
            set { _subBorderColor = value ?? "#FFFFFF"; OnPropertyChanged(); }
        }

        private bool _subliminalStealsFocus = false;
        public bool SubliminalStealsFocus
        {
            get => _subliminalStealsFocus;
            set { _subliminalStealsFocus = value; OnPropertyChanged(); }
        }

        private bool _subAudioEnabled = false;
        public bool SubAudioEnabled
        {
            get => _subAudioEnabled;
            set { _subAudioEnabled = value; OnPropertyChanged(); }
        }

        private int _subAudioVolume = 50; // 0-100%
        public int SubAudioVolume
        {
            get => _subAudioVolume;
            set { _subAudioVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        #endregion

        #region System

        private string _bambiCloudUrl = "https://bambicloud.com/";
        public string BambiCloudUrl
        {
            get => _bambiCloudUrl;
            set { _bambiCloudUrl = value; OnPropertyChanged(); }
        }

        private string _customAssetsPath = "";
        /// <summary>
        /// Custom folder path for user assets (images, videos).
        /// Empty string means use default path.
        /// </summary>
        public string CustomAssetsPath
        {
            get => _customAssetsPath;
            set { _customAssetsPath = value ?? ""; OnPropertyChanged(); }
        }

        private bool _firstRunAssetsPromptShown = false;
        /// <summary>
        /// Whether the first-run assets folder prompt has been shown.
        /// Prevents repeatedly asking user to choose a folder.
        /// </summary>
        public bool FirstRunAssetsPromptShown
        {
            get => _firstRunAssetsPromptShown;
            set { _firstRunAssetsPromptShown = value; OnPropertyChanged(); }
        }

        #region Active Assets

        private HashSet<string> _activeAssetPaths = new();
        /// <summary>
        /// Set of relative paths to active assets. If empty and UseAssetWhitelist is false, all assets are active.
        /// Paths are relative to EffectiveAssetsPath.
        /// LEGACY: Kept for backward compatibility, use DisabledAssetPaths instead.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> ActiveAssetPaths
        {
            get => _activeAssetPaths;
            set { _activeAssetPaths = value ?? new(); OnPropertyChanged(); }
        }

        private HashSet<string> _disabledAssetPaths = new();
        /// <summary>
        /// Set of relative paths to DISABLED assets. Items NOT in this set are active.
        /// This is the inverse of a whitelist - items are active by default.
        /// Paths are relative to EffectiveAssetsPath.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> DisabledAssetPaths
        {
            get => _disabledAssetPaths;
            set { _disabledAssetPaths = value ?? new(); OnPropertyChanged(); }
        }

        private bool _useAssetWhitelist = false;
        /// <summary>
        /// When true, files in DisabledAssetPaths are excluded from use.
        /// When false, all files are active (default behavior).
        /// </summary>
        public bool UseAssetWhitelist
        {
            get => _useAssetWhitelist;
            set { _useAssetWhitelist = value; OnPropertyChanged(); }
        }

        private List<string> _installedPackIds = new();
        /// <summary>
        /// IDs of installed content packs.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> InstalledPackIds
        {
            get => _installedPackIds;
            set { _installedPackIds = value ?? new(); OnPropertyChanged(); }
        }

        private List<string> _activePackIds = new();
        /// <summary>
        /// IDs of active content packs (subset of InstalledPackIds).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> ActivePackIds
        {
            get => _activePackIds;
            set { _activePackIds = value ?? new(); OnPropertyChanged(); }
        }

        private Dictionary<string, string> _packGuidMap = new();
        /// <summary>
        /// Maps pack IDs to their obfuscated GUID folder names.
        /// Used to locate installed pack files in the hidden .packs directory.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, string> PackGuidMap
        {
            get => _packGuidMap;
            set { _packGuidMap = value ?? new(); OnPropertyChanged(); }
        }

        private List<AssetPreset> _assetPresets = new();
        /// <summary>
        /// Saved asset presets that store which files are disabled.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<AssetPreset> AssetPresets
        {
            get => _assetPresets;
            set { _assetPresets = value ?? new(); OnPropertyChanged(); }
        }

        private string? _currentAssetPresetId = null;
        /// <summary>
        /// ID of the currently selected asset preset, or null if none selected.
        /// </summary>
        [JsonProperty]
        public string? CurrentAssetPresetId
        {
            get => _currentAssetPresetId;
            set { _currentAssetPresetId = value; OnPropertyChanged(); }
        }

        #endregion

        private string _marqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
        /// <summary>
        /// Custom scrolling marquee banner message displayed in the UI.
        /// </summary>
        public string MarqueeMessage
        {
            get => _marqueeMessage;
            set { _marqueeMessage = value ?? ""; OnPropertyChanged(); }
        }

        private bool _dualMonitorEnabled = true;
        /// <summary>
        /// When enabled, content displays on ALL connected monitors (2, 3, or more).
        /// When disabled, content only appears on the primary monitor.
        /// Property name kept as "DualMonitor" for settings file backwards compatibility.
        /// </summary>
        public bool DualMonitorEnabled
        {
            get => _dualMonitorEnabled;
            set { _dualMonitorEnabled = value; OnPropertyChanged(); }
        }

        private bool _runOnStartup = false;
        public bool RunOnStartup
        {
            get => _runOnStartup;
            set { _runOnStartup = value; OnPropertyChanged(); }
        }

        private bool _startMinimized = false;
        public bool StartMinimized
        {
            get => _startMinimized;
            set { _startMinimized = value; OnPropertyChanged(); }
        }

        private bool _autoStartEngine = false;
        public bool AutoStartEngine
        {
            get => _autoStartEngine;
            set { _autoStartEngine = value; OnPropertyChanged(); }
        }

        private bool _panicKeyEnabled = true; // ESC to stop
        public bool PanicKeyEnabled
        {
            get => _panicKeyEnabled;
            set { _panicKeyEnabled = value; OnPropertyChanged(); }
        }

        private string _panicKey = "Escape"; // Default panic key
        public string PanicKey
        {
            get => _panicKey;
            set { _panicKey = value ?? "Escape"; OnPropertyChanged(); }
        }

        private bool _mercySystemEnabled = true;
        public bool MercySystemEnabled
        {
            get => _mercySystemEnabled;
            set { _mercySystemEnabled = value; OnPropertyChanged(); }
        }

        private string _lastPreset = "DEFAULT";
        public string LastPreset
        {
            get => _lastPreset;
            set { _lastPreset = value ?? "DEFAULT"; OnPropertyChanged(); }
        }

        private bool _discordRichPresenceEnabled = false;
        /// <summary>
        /// Enable Discord Rich Presence to show activity status in Discord
        /// </summary>
        public bool DiscordRichPresenceEnabled
        {
            get => _discordRichPresenceEnabled;
            set { _discordRichPresenceEnabled = value; OnPropertyChanged(); }
        }

        private bool _discordShowLevelInPresence = true;
        /// <summary>
        /// Show current level in Discord Rich Presence status
        /// </summary>
        public bool DiscordShowLevelInPresence
        {
            get => _discordShowLevelInPresence;
            set { _discordShowLevelInPresence = value; OnPropertyChanged(); }
        }

        private string _discordWebhookUrl = "";
        /// <summary>
        /// Discord webhook URL for achievement and level announcements
        /// </summary>
        public string DiscordWebhookUrl
        {
            get => _discordWebhookUrl;
            set { _discordWebhookUrl = value ?? ""; OnPropertyChanged(); }
        }

        private bool _discordShareAchievements = false;
        /// <summary>
        /// Share achievement unlocks to Discord webhook (opt-in)
        /// </summary>
        public bool DiscordShareAchievements
        {
            get => _discordShareAchievements;
            set { _discordShareAchievements = value; OnPropertyChanged(); }
        }

        private bool _discordShareLevelUps = false;
        /// <summary>
        /// Share level up milestones to Discord webhook (opt-in)
        /// </summary>
        public bool DiscordShareLevelUps
        {
            get => _discordShareLevelUps;
            set { _discordShareLevelUps = value; OnPropertyChanged(); }
        }

        private bool _discordUseAnonymousName = true;
        /// <summary>
        /// Use display name instead of Discord username for sharing (privacy)
        /// </summary>
        public bool DiscordUseAnonymousName
        {
            get => _discordUseAnonymousName;
            set { _discordUseAnonymousName = value; OnPropertyChanged(); }
        }

        private bool _offlineMode = false;
        /// <summary>
        /// Offline mode - disables all network features (updates, AI chat, leaderboard, Patreon verification).
        /// When enabled, the app operates completely offline with no external connections.
        /// </summary>
        public bool OfflineMode
        {
            get => _offlineMode;
            set { _offlineMode = value; OnPropertyChanged(); }
        }

        private DateTime? _patreonPremiumValidUntil = null;
        /// <summary>
        /// Cached premium access validity. When a user logs in with Patreon and has premium,
        /// this timestamp is set to 2 weeks from validation. Premium features remain available
        /// even if user logs in with Discord, as long as this hasn't expired.
        /// </summary>
        [JsonProperty("patreon_premium_valid_until")]
        public DateTime? PatreonPremiumValidUntil
        {
            get => _patreonPremiumValidUntil;
            set { _patreonPremiumValidUntil = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Check if cached Patreon premium access is still valid (within 2-week window)
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasCachedPremiumAccess => _patreonPremiumValidUntil.HasValue && DateTime.UtcNow < _patreonPremiumValidUntil.Value;

        #endregion

        #region Scheduler

        private bool _schedulerEnabled = false;
        public bool SchedulerEnabled
        {
            get => _schedulerEnabled;
            set { _schedulerEnabled = value; OnPropertyChanged(); }
        }

        private int _schedulerDurationMinutes = 60;
        public int SchedulerDurationMinutes
        {
            get => _schedulerDurationMinutes;
            set { _schedulerDurationMinutes = Math.Clamp(value, 5, 480); OnPropertyChanged(); }
        }

        private double _schedulerMultiplier = 1.0;
        public double SchedulerMultiplier
        {
            get => _schedulerMultiplier;
            set { _schedulerMultiplier = Math.Clamp(value, 1.0, 3.0); OnPropertyChanged(); }
        }

        private bool _schedulerLinkAlpha = false;
        public bool SchedulerLinkAlpha
        {
            get => _schedulerLinkAlpha;
            set { _schedulerLinkAlpha = value; OnPropertyChanged(); }
        }

        private bool _timeScheduleEnabled = false;
        public bool TimeScheduleEnabled
        {
            get => _timeScheduleEnabled;
            set { _timeScheduleEnabled = value; OnPropertyChanged(); }
        }

        private string _timeStartStr = "16:00";
        public string TimeStartStr
        {
            get => _timeStartStr;
            set { _timeStartStr = value ?? "16:00"; OnPropertyChanged(); }
        }

        private string _timeEndStr = "18:00";
        public string TimeEndStr
        {
            get => _timeEndStr;
            set { _timeEndStr = value ?? "18:00"; OnPropertyChanged(); }
        }

        private List<int> _activeWeekdays = new() { 0, 1, 2, 3, 4, 5, 6 };
        public List<int> ActiveWeekdays
        {
            get => _activeWeekdays;
            set { _activeWeekdays = value ?? new List<int> { 0, 1, 2, 3, 4, 5, 6 }; OnPropertyChanged(); }
        }

        // Scheduler time window
        private string _schedulerStartTime = "16:00";
        public string SchedulerStartTime
        {
            get => _schedulerStartTime;
            set { _schedulerStartTime = value ?? "16:00"; OnPropertyChanged(); }
        }

        private string _schedulerEndTime = "22:00";
        public string SchedulerEndTime
        {
            get => _schedulerEndTime;
            set { _schedulerEndTime = value ?? "22:00"; OnPropertyChanged(); }
        }

        // Scheduler active days
        private bool _schedulerMonday = true;
        public bool SchedulerMonday
        {
            get => _schedulerMonday;
            set { _schedulerMonday = value; OnPropertyChanged(); }
        }

        private bool _schedulerTuesday = true;
        public bool SchedulerTuesday
        {
            get => _schedulerTuesday;
            set { _schedulerTuesday = value; OnPropertyChanged(); }
        }

        private bool _schedulerWednesday = true;
        public bool SchedulerWednesday
        {
            get => _schedulerWednesday;
            set { _schedulerWednesday = value; OnPropertyChanged(); }
        }

        private bool _schedulerThursday = true;
        public bool SchedulerThursday
        {
            get => _schedulerThursday;
            set { _schedulerThursday = value; OnPropertyChanged(); }
        }

        private bool _schedulerFriday = true;
        public bool SchedulerFriday
        {
            get => _schedulerFriday;
            set { _schedulerFriday = value; OnPropertyChanged(); }
        }

        private bool _schedulerSaturday = true;
        public bool SchedulerSaturday
        {
            get => _schedulerSaturday;
            set { _schedulerSaturday = value; OnPropertyChanged(); }
        }

        private bool _schedulerSunday = true;
        public bool SchedulerSunday
        {
            get => _schedulerSunday;
            set { _schedulerSunday = value; OnPropertyChanged(); }
        }

        private bool _intensityRampEnabled = false;
        public bool IntensityRampEnabled
        {
            get => _intensityRampEnabled;
            set { _intensityRampEnabled = value; OnPropertyChanged(); }
        }

        private int _rampDurationMinutes = 60;
        public int RampDurationMinutes
        {
            get => _rampDurationMinutes;
            set { _rampDurationMinutes = Math.Clamp(value, 10, 180); OnPropertyChanged(); }
        }

        // Ramp link options
        private bool _rampLinkFlashOpacity = false;
        public bool RampLinkFlashOpacity
        {
            get => _rampLinkFlashOpacity;
            set { _rampLinkFlashOpacity = value; OnPropertyChanged(); }
        }

        private bool _rampLinkSpiralOpacity = false;
        public bool RampLinkSpiralOpacity
        {
            get => _rampLinkSpiralOpacity;
            set { _rampLinkSpiralOpacity = value; OnPropertyChanged(); }
        }

        private bool _rampLinkPinkFilterOpacity = false;
        public bool RampLinkPinkFilterOpacity
        {
            get => _rampLinkPinkFilterOpacity;
            set { _rampLinkPinkFilterOpacity = value; OnPropertyChanged(); }
        }

        private bool _rampLinkMasterAudio = false;
        public bool RampLinkMasterAudio
        {
            get => _rampLinkMasterAudio;
            set { _rampLinkMasterAudio = value; OnPropertyChanged(); }
        }

        private bool _rampLinkSubliminalAudio = false;
        public bool RampLinkSubliminalAudio
        {
            get => _rampLinkSubliminalAudio;
            set { _rampLinkSubliminalAudio = value; OnPropertyChanged(); }
        }

        private bool _endSessionOnRampComplete = false;
        public bool EndSessionOnRampComplete
        {
            get => _endSessionOnRampComplete;
            set { _endSessionOnRampComplete = value; OnPropertyChanged(); }
        }

        #endregion

        #region Spiral Overlay (Unlocks Lv.10)

        private bool _spiralEnabled = false;
        public bool SpiralEnabled
        {
            get => _spiralEnabled;
            set { _spiralEnabled = value; OnPropertyChanged(); }
        }

        private string _spiralPath = "";
        public string SpiralPath
        {
            get => _spiralPath;
            set { _spiralPath = value ?? ""; OnPropertyChanged(); }
        }

        private int _spiralOpacity = 10; // 0-50%
        public int SpiralOpacity
        {
            get => _spiralOpacity;
            set { _spiralOpacity = Math.Clamp(value, 0, 50); OnPropertyChanged(); }
        }

        private bool _spiralLinkRamp = false;
        public bool SpiralLinkRamp
        {
            get => _spiralLinkRamp;
            set { _spiralLinkRamp = value; OnPropertyChanged(); }
        }

        #endregion

        #region Bubbles (Unlocks Lv.20)
        private bool _bubblesEnabled = false;
        public bool BubblesEnabled
        {
            get => _bubblesEnabled;
            set { _bubblesEnabled = value; OnPropertyChanged(); }
        }
        private int _bubblesFrequency = 5;
        public int BubblesFrequency
        {
            get => _bubblesFrequency;
            set { _bubblesFrequency = Math.Clamp(value, 1, 15); OnPropertyChanged(); }
        }
        private int _bubblesVolume = 50;
        public int BubblesVolume
        {
            get => _bubblesVolume;
            set { _bubblesVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }
        private bool _bubblesLinkRamp = false;
        public bool BubblesLinkRamp
        {
            get => _bubblesLinkRamp;
            set { _bubblesLinkRamp = value; OnPropertyChanged(); }
        }
        private bool _bubblesClickable = true;
        public bool BubblesClickable
        {
            get => _bubblesClickable;
            set { _bubblesClickable = value; OnPropertyChanged(); }
        }
        #endregion

        #region Lock Card (Unlocks Lv.35)
        private bool _lockCardEnabled = false;
        public bool LockCardEnabled
        {
            get => _lockCardEnabled;
            set { _lockCardEnabled = value; OnPropertyChanged(); }
        }
        
        private int _lockCardFrequency = 2; // Per hour (1-10)
        public int LockCardFrequency
        {
            get => _lockCardFrequency;
            set { _lockCardFrequency = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }
        
        private int _lockCardRepeats = 3; // Times to type (1-10)
        public int LockCardRepeats
        {
            get => _lockCardRepeats;
            set { _lockCardRepeats = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }
        
        private bool _lockCardStrict = false; // No ESC escape
        public bool LockCardStrict
        {
            get => _lockCardStrict;
            set { _lockCardStrict = value; OnPropertyChanged(); }
        }
        
        private Dictionary<string, bool> _lockCardPhrases = new()
        {
            { "GOOD GIRLS OBEY", true },
            { "I LOVE BEING PROGRAMMED", true },
            { "BAMBI SLEEP", true },
            { "DROP FOR ME", true },
            { "EMPTY AND OBEDIENT", true }
        };
        public Dictionary<string, bool> LockCardPhrases
        {
            get => _lockCardPhrases;
            set { _lockCardPhrases = value ?? new(); OnPropertyChanged(); }
        }
        
        // Lock Card Colors
        private string _lockCardBackgroundColor = "#1A1A2E";
        public string LockCardBackgroundColor
        {
            get => _lockCardBackgroundColor;
            set { _lockCardBackgroundColor = value ?? "#1A1A2E"; OnPropertyChanged(); }
        }
        
        private string _lockCardTextColor = "#FF69B4";
        public string LockCardTextColor
        {
            get => _lockCardTextColor;
            set { _lockCardTextColor = value ?? "#FF69B4"; OnPropertyChanged(); }
        }
        
        private string _lockCardInputBackgroundColor = "#252542";
        public string LockCardInputBackgroundColor
        {
            get => _lockCardInputBackgroundColor;
            set { _lockCardInputBackgroundColor = value ?? "#252542"; OnPropertyChanged(); }
        }
        
        private string _lockCardInputTextColor = "#FFFFFF";
        public string LockCardInputTextColor
        {
            get => _lockCardInputTextColor;
            set { _lockCardInputTextColor = value ?? "#FFFFFF"; OnPropertyChanged(); }
        }
        
        private string _lockCardAccentColor = "#FF69B4";
        public string LockCardAccentColor
        {
            get => _lockCardAccentColor;
            set { _lockCardAccentColor = value ?? "#FF69B4"; OnPropertyChanged(); }
        }
        #endregion

        #region Bubble Count Game (Unlocks Lv.50)

        private bool _bubbleCountEnabled = false;
        public bool BubbleCountEnabled
        {
            get => _bubbleCountEnabled;
            set { _bubbleCountEnabled = value; OnPropertyChanged(); }
        }

        private int _bubbleCountFrequency = 2; // Games per hour (1-10)
        public int BubbleCountFrequency
        {
            get => _bubbleCountFrequency;
            set { _bubbleCountFrequency = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private int _bubbleCountDifficulty = 1; // 0=Easy, 1=Medium, 2=Hard
        public int BubbleCountDifficulty
        {
            get => _bubbleCountDifficulty;
            set { _bubbleCountDifficulty = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
        }

        private bool _bubbleCountStrictLock = false;
        public bool BubbleCountStrictLock
        {
            get => _bubbleCountStrictLock;
            set { _bubbleCountStrictLock = value; OnPropertyChanged(); }
        }

        #endregion

        #region Bouncing Text (Unlocks Lv.60)

        private bool _bouncingTextEnabled = false;
        public bool BouncingTextEnabled
        {
            get => _bouncingTextEnabled;
            set { _bouncingTextEnabled = value; OnPropertyChanged(); }
        }

        private int _bouncingTextSpeed = 5; // 1-10
        public int BouncingTextSpeed
        {
            get => _bouncingTextSpeed;
            set { _bouncingTextSpeed = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private int _bouncingTextSize = 100; // 50-300%
        public int BouncingTextSize
        {
            get => _bouncingTextSize;
            set { _bouncingTextSize = Math.Clamp(value, 50, 300); OnPropertyChanged(); }
        }

        private int _bouncingTextOpacity = 100; // 0-100%
        public int BouncingTextOpacity
        {
            get => _bouncingTextOpacity;
            set { _bouncingTextOpacity = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private Dictionary<string, bool> _bouncingTextPool = new()
        {
            { "GOOD GIRL", true },
            { "OBEY", true },
            { "SUBMIT", true },
            { "BIMBO", true },
            { "EMPTY", true },
            { "MINDLESS", true },
            { "OBEDIENT", true },
            { "PRETTY", true },
            { "PINK", true },
            { "DROP", true }
        };
        public Dictionary<string, bool> BouncingTextPool
        {
            get => _bouncingTextPool;
            set { _bouncingTextPool = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Pink Filter (Unlocks Lv.10)

        private bool _pinkFilterEnabled = false;
        public bool PinkFilterEnabled
        {
            get => _pinkFilterEnabled;
            set { _pinkFilterEnabled = value; OnPropertyChanged(); }
        }

        private int _pinkFilterOpacity = 10; // 0-50%
        public int PinkFilterOpacity
        {
            get => _pinkFilterOpacity;
            set { _pinkFilterOpacity = Math.Clamp(value, 0, 50); OnPropertyChanged(); }
        }

        private bool _pinkFilterLinkRamp = false;
        public bool PinkFilterLinkRamp
        {
            get => _pinkFilterLinkRamp;
            set { _pinkFilterLinkRamp = value; OnPropertyChanged(); }
        }

        #endregion

        #region Attention Game

        private Dictionary<string, bool> _attentionPool = new()
        {
            { "CLICK ME", true },
            { "GOOD GIRL", true },
            { "BAMBI FREEZE", true },
            { "BAMBI SLEEP", true },
            { "BAMBI RESET", true },
            { "DROP", true },
            { "OBEY", true },
            { "ACCEPT", true },
            { "SUBMIT", true },
            { "BLANK AND EMPTY", true },
            { "BAMBI LOVES COCK", true },
            { "UNIFORM ON", true }
        };
        public Dictionary<string, bool> AttentionPool
        {
            get => _attentionPool;
            set { _attentionPool = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Mind Wipe (Unlocks Lv.75)

        private bool _mindWipeEnabled = false;
        public bool MindWipeEnabled
        {
            get => _mindWipeEnabled;
            set { _mindWipeEnabled = value; OnPropertyChanged(); }
        }

        private int _mindWipeFrequency = 6; // 1-180 per hour
        public int MindWipeFrequency
        {
            get => _mindWipeFrequency;
            set { _mindWipeFrequency = Math.Clamp(value, 1, 180); OnPropertyChanged(); }
        }

        private int _mindWipeVolume = 50; // 0-100%
        public int MindWipeVolume
        {
            get => _mindWipeVolume;
            set { _mindWipeVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private bool _mindWipeLoop = false; // Loop single track in background
        public bool MindWipeLoop
        {
            get => _mindWipeLoop;
            set { _mindWipeLoop = value; OnPropertyChanged(); }
        }

        #endregion

        #region Brain Drain (Unlocks Lv.25)
        private bool _brainDrainEnabled = false;
        public bool BrainDrainEnabled
        {
            get => _brainDrainEnabled;
            set { _brainDrainEnabled = value; OnPropertyChanged(); }
        }

        private int _brainDrainIntensity = 20; // 1-100%
        public int BrainDrainIntensity
        {
            get => _brainDrainIntensity;
            set { _brainDrainIntensity = Math.Clamp(value, 1, 100); OnPropertyChanged(); }
        }

        private bool _brainDrainHighRefresh = false;
        /// <summary>
        /// High refresh rate mode - reduces timer interval from 5s to 500ms for smoother effect.
        /// May increase CPU usage on some systems.
        /// </summary>
        public bool BrainDrainHighRefresh
        {
            get => _brainDrainHighRefresh;
            set { _brainDrainHighRefresh = value; OnPropertyChanged(); }
        }
        #endregion

        #region Avatar Companion

        private bool _avatarEnabled = true;
        /// <summary>
        /// Whether to show the avatar companion window
        /// </summary>
        public bool AvatarEnabled
        {
            get => _avatarEnabled;
            set { _avatarEnabled = value; OnPropertyChanged(); }
        }

        private bool _useAlternativeTube = false;
        /// <summary>
        /// When true, use tube2.png instead of tube.png
        /// </summary>
        public bool UseAlternativeTube
        {
            get => _useAlternativeTube;
            set { _useAlternativeTube = value; OnPropertyChanged(); }
        }

        private bool _aiChatEnabled = true;
        /// <summary>
        /// Whether AI chat is enabled (requires OPENAI_API_KEY environment variable)
        /// </summary>
        public bool AiChatEnabled
        {
            get => _aiChatEnabled;
            set { _aiChatEnabled = value; OnPropertyChanged(); }
        }

        private int _idleGiggleIntervalSeconds = 10; // 10-600 seconds
        /// <summary>
        /// How often the companion speaks when idle (in seconds)
        /// </summary>
        public int IdleGiggleIntervalSeconds
        {
            get => _idleGiggleIntervalSeconds;
            set { _idleGiggleIntervalSeconds = Math.Clamp(value, 10, 600); OnPropertyChanged(); }
        }

        // ============================================================
        // AWARENESS MODE (Window Tracking) - Opt-in feature
        // ============================================================

        private bool _awarenessModeEnabled = false;
        /// <summary>
        /// Whether the companion monitors active windows to react to user activity.
        /// Requires explicit consent. Privacy-focused: only categorizes, never logs titles.
        /// </summary>
        public bool AwarenessModeEnabled
        {
            get => _awarenessModeEnabled;
            set { _awarenessModeEnabled = value; OnPropertyChanged(); }
        }

        private bool _awarenessConsentGiven = false;
        /// <summary>
        /// Whether the user has given consent for window monitoring.
        /// Must be true for awareness mode to function.
        /// </summary>
        public bool AwarenessConsentGiven
        {
            get => _awarenessConsentGiven;
            set { _awarenessConsentGiven = value; OnPropertyChanged(); }
        }

        private int _awarenessReactionCooldownSeconds = 10;
        /// <summary>
        /// Minimum seconds between awareness reactions (10-600)
        /// </summary>
        public int AwarenessReactionCooldownSeconds
        {
            get => _awarenessReactionCooldownSeconds;
            set { _awarenessReactionCooldownSeconds = Math.Clamp(value, 10, 600); OnPropertyChanged(); }
        }

        #endregion

        #region Companion Leveling System (v5.3)

        private int _activeCompanionId = 0;
        /// <summary>
        /// Currently active companion (0=OG Bambi Sprite, 1=Cult Bunny, 2=Brain Parasite, 3=Bambi Trainer).
        /// XP is only awarded to the active companion.
        /// </summary>
        public int ActiveCompanionId
        {
            get => _activeCompanionId;
            set { _activeCompanionId = Math.Clamp(value, 0, 3); OnPropertyChanged(); }
        }

        private Dictionary<int, CompanionProgress>? _companionProgressData;
        /// <summary>
        /// Progress data for each companion (keyed by CompanionId int value).
        /// Each companion has their own independent level and XP.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<int, CompanionProgress> CompanionProgressData
        {
            get => _companionProgressData ??= new Dictionary<int, CompanionProgress>();
            set { _companionProgressData = value ?? new Dictionary<int, CompanionProgress>(); OnPropertyChanged(); }
        }

        private List<string>? _installedCommunityPromptIds;
        /// <summary>
        /// IDs of installed community prompt presets.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> InstalledCommunityPromptIds
        {
            get => _installedCommunityPromptIds ??= new List<string>();
            set { _installedCommunityPromptIds = value ?? new List<string>(); OnPropertyChanged(); }
        }

        private string? _activeCommunityPromptId;
        /// <summary>
        /// Currently active community prompt ID (null = use built-in/custom).
        /// </summary>
        public string? ActiveCommunityPromptId
        {
            get => _activeCommunityPromptId;
            set { _activeCommunityPromptId = value; OnPropertyChanged(); }
        }

        private Dictionary<int, string>? _companionPromptAssignments;
        /// <summary>
        /// Maps companion IDs to their assigned AI prompt IDs.
        /// When a companion is activated, their assigned prompt is automatically loaded.
        /// Key: CompanionId (0-3), Value: CommunityPromptId (or null for default)
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<int, string> CompanionPromptAssignments
        {
            get => _companionPromptAssignments ??= new Dictionary<int, string>();
            set { _companionPromptAssignments = value ?? new Dictionary<int, string>(); OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the assigned prompt ID for a specific companion, or null if none assigned.
        /// </summary>
        public string? GetCompanionPromptId(int companionId)
        {
            return CompanionPromptAssignments.TryGetValue(companionId, out var promptId) ? promptId : null;
        }

        /// <summary>
        /// Assigns a prompt to a companion. Pass null to clear assignment.
        /// </summary>
        public void SetCompanionPromptId(int companionId, string? promptId)
        {
            if (string.IsNullOrEmpty(promptId))
            {
                CompanionPromptAssignments.Remove(companionId);
            }
            else
            {
                CompanionPromptAssignments[companionId] = promptId;
            }
            OnPropertyChanged(nameof(CompanionPromptAssignments));
        }

        /// <summary>
        /// Gets the progress for the currently active companion.
        /// Creates default progress if not yet tracked.
        /// </summary>
        [JsonIgnore]
        public CompanionProgress ActiveCompanionProgress
        {
            get
            {
                if (!CompanionProgressData.TryGetValue(ActiveCompanionId, out var progress))
                {
                    progress = CompanionProgress.CreateNew((CompanionId)ActiveCompanionId);
                    CompanionProgressData[ActiveCompanionId] = progress;
                }
                return progress;
            }
        }

        #endregion

        #region AI Configuration

        private string _openRouterApiKey = "";
        /// <summary>
        /// OpenRouter API key for AI chat features
        /// </summary>
        public string OpenRouterApiKey
        {
            get => _openRouterApiKey;
            set { _openRouterApiKey = value ?? ""; OnPropertyChanged(); }
        }

        private bool _slutModeEnabled = false;
        /// <summary>
        /// Enable less tame AI responses (Patreon only)
        /// </summary>
        public bool SlutModeEnabled
        {
            get => _slutModeEnabled;
            set { _slutModeEnabled = value; OnPropertyChanged(); }
        }

        private CompanionPromptSettings _companionPrompt = new();
        /// <summary>
        /// Custom AI companion prompt settings. Allows users to customize personality,
        /// reactions, knowledge base, and output rules.
        /// </summary>
        public CompanionPromptSettings CompanionPrompt
        {
            get => _companionPrompt;
            set { _companionPrompt = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Trigger Mode (Free)

        private bool _triggerModeEnabled = false;
        /// <summary>
        /// Enable random trigger phrases (no AI, free for all)
        /// </summary>
        public bool TriggerModeEnabled
        {
            get => _triggerModeEnabled;
            set { _triggerModeEnabled = value; OnPropertyChanged(); }
        }

        private int _triggerIntervalSeconds = 15;
        /// <summary>
        /// Seconds between random triggers (10-600)
        /// </summary>
        public int TriggerIntervalSeconds
        {
            get => _triggerIntervalSeconds;
            set { _triggerIntervalSeconds = Math.Clamp(value, 10, 600); OnPropertyChanged(); }
        }

        private bool _randomBubbleEnabled = false;
        /// <summary>
        /// Enable random bubble spawning from avatar (3-5 min intervals)
        /// </summary>
        public bool RandomBubbleEnabled
        {
            get => _randomBubbleEnabled;
            set { _randomBubbleEnabled = value; OnPropertyChanged(); }
        }

        private List<string> _customTriggers = new()
        {
            "GOOD GIRL",
            "BAMBI SLEEP",
            "BIMBO DOLL",
            "BAMBI FREEZE",
            "BAMBI RESET",
            "DROP FOR COCK",
            "GIGGLETIME",
            "BLONDE MOMENT",
            "ZAP COCK DRAIN OBEY",
            "SNAP AND FORGET",
            "PRIMPED AND PAMPERED",
            "SAFE AND SECURE",
            "COCK ZOMBIE NOW",
            "BAMBI UNIFORM LOCK",
            "AIRHEAD BARBIE",
            "BRAINDEAD BOBBLEHEAD",
            "COCKBLANK LOVEDOLL",
            "BAMBI CUM AND COLLAPSE"
        };
        /// <summary>
        /// Custom trigger phrases for Trigger Mode
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> CustomTriggers
        {
            get => _customTriggers;
            set { _customTriggers = value ?? new List<string>(); OnPropertyChanged(); }
        }

        #endregion

        #region Autonomy Mode (Unlocks Lv.100)

        private bool _autonomyModeEnabled = false;
        /// <summary>
        /// Enable autonomous companion behavior - she will trigger effects on her own.
        /// Requires level 100 and explicit consent.
        /// </summary>
        public bool AutonomyModeEnabled
        {
            get => _autonomyModeEnabled;
            set { _autonomyModeEnabled = value; OnPropertyChanged(); }
        }

        private bool _autonomyConsentGiven = false;
        /// <summary>
        /// Whether the user has given consent for autonomous behavior.
        /// Must acknowledge warning before first enable.
        /// </summary>
        public bool AutonomyConsentGiven
        {
            get => _autonomyConsentGiven;
            set { _autonomyConsentGiven = value; OnPropertyChanged(); }
        }

        private int _autonomyIntensity = 5;
        /// <summary>
        /// Intensity level 1-10 affecting frequency and action weights
        /// </summary>
        public int AutonomyIntensity
        {
            get => _autonomyIntensity;
            set { _autonomyIntensity = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private int _autonomyCooldownSeconds = 30;
        /// <summary>
        /// Minimum seconds between autonomous actions (10-300)
        /// </summary>
        public int AutonomyCooldownSeconds
        {
            get => _autonomyCooldownSeconds;
            set { _autonomyCooldownSeconds = Math.Clamp(value, 10, 300); OnPropertyChanged(); }
        }

        // Trigger Sources

        private bool _autonomyIdleTriggerEnabled = true;
        /// <summary>
        /// Trigger autonomous actions when user has been idle
        /// </summary>
        public bool AutonomyIdleTriggerEnabled
        {
            get => _autonomyIdleTriggerEnabled;
            set { _autonomyIdleTriggerEnabled = value; OnPropertyChanged(); }
        }

        private int _autonomyIdleTimeoutMinutes = 5;
        /// <summary>
        /// Minutes of inactivity before idle trigger fires (1-30)
        /// </summary>
        public int AutonomyIdleTimeoutMinutes
        {
            get => _autonomyIdleTimeoutMinutes;
            set { _autonomyIdleTimeoutMinutes = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        private bool _autonomyRandomTriggerEnabled = true;
        /// <summary>
        /// Trigger autonomous actions at random intervals
        /// </summary>
        public bool AutonomyRandomTriggerEnabled
        {
            get => _autonomyRandomTriggerEnabled;
            set { _autonomyRandomTriggerEnabled = value; OnPropertyChanged(); }
        }

        private int _autonomyRandomIntervalMinutes = 2;
        /// <summary>
        /// Average minutes between random triggers (2-60) - LEGACY, use AutonomyRandomIntervalSeconds
        /// </summary>
        public int AutonomyRandomIntervalMinutes
        {
            get => _autonomyRandomIntervalMinutes;
            set { _autonomyRandomIntervalMinutes = Math.Clamp(value, 2, 60); OnPropertyChanged(); }
        }

        private int _autonomyRandomIntervalSeconds = 60;
        /// <summary>
        /// Average seconds between random triggers (30-300)
        /// </summary>
        public int AutonomyRandomIntervalSeconds
        {
            get => _autonomyRandomIntervalSeconds;
            set { _autonomyRandomIntervalSeconds = Math.Clamp(value, 30, 300); OnPropertyChanged(); }
        }

        private bool _autonomyContextTriggerEnabled = false;
        /// <summary>
        /// Trigger autonomous actions based on window activity context.
        /// Requires Awareness Mode to be enabled.
        /// </summary>
        public bool AutonomyContextTriggerEnabled
        {
            get => _autonomyContextTriggerEnabled;
            set { _autonomyContextTriggerEnabled = value; OnPropertyChanged(); }
        }

        private bool _autonomyTimeAwareEnabled = false;
        /// <summary>
        /// Adjust intensity based on time of day (more active at night)
        /// </summary>
        public bool AutonomyTimeAwareEnabled
        {
            get => _autonomyTimeAwareEnabled;
            set { _autonomyTimeAwareEnabled = value; OnPropertyChanged(); }
        }

        private double _autonomyMorningMultiplier = 0.5;
        /// <summary>
        /// Intensity multiplier for morning hours (6am-12pm)
        /// </summary>
        public double AutonomyMorningMultiplier
        {
            get => _autonomyMorningMultiplier;
            set { _autonomyMorningMultiplier = Math.Clamp(value, 0.1, 2.0); OnPropertyChanged(); }
        }

        private double _autonomyAfternoonMultiplier = 0.75;
        /// <summary>
        /// Intensity multiplier for afternoon hours (12pm-6pm)
        /// </summary>
        public double AutonomyAfternoonMultiplier
        {
            get => _autonomyAfternoonMultiplier;
            set { _autonomyAfternoonMultiplier = Math.Clamp(value, 0.1, 2.0); OnPropertyChanged(); }
        }

        private double _autonomyEveningMultiplier = 1.0;
        /// <summary>
        /// Intensity multiplier for evening hours (6pm-10pm)
        /// </summary>
        public double AutonomyEveningMultiplier
        {
            get => _autonomyEveningMultiplier;
            set { _autonomyEveningMultiplier = Math.Clamp(value, 0.1, 2.0); OnPropertyChanged(); }
        }

        private double _autonomyNightMultiplier = 1.25;
        /// <summary>
        /// Intensity multiplier for night hours (10pm-6am)
        /// </summary>
        public double AutonomyNightMultiplier
        {
            get => _autonomyNightMultiplier;
            set { _autonomyNightMultiplier = Math.Clamp(value, 0.1, 2.0); OnPropertyChanged(); }
        }

        // Per-behavior toggles

        private bool _autonomyCanTriggerFlash = true;
        /// <summary>
        /// Allow autonomous flash image triggers
        /// </summary>
        public bool AutonomyCanTriggerFlash
        {
            get => _autonomyCanTriggerFlash;
            set { _autonomyCanTriggerFlash = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerVideo = true;
        /// <summary>
        /// Allow autonomous video triggers (NEVER uses strict mode)
        /// </summary>
        public bool AutonomyCanTriggerVideo
        {
            get => _autonomyCanTriggerVideo;
            set { _autonomyCanTriggerVideo = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerSubliminal = true;
        /// <summary>
        /// Allow autonomous subliminal triggers
        /// </summary>
        public bool AutonomyCanTriggerSubliminal
        {
            get => _autonomyCanTriggerSubliminal;
            set { _autonomyCanTriggerSubliminal = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerBrainDrain = true;
        /// <summary>
        /// Allow autonomous brain drain blur pulses (requires Lv.70)
        /// </summary>
        public bool AutonomyCanTriggerBrainDrain
        {
            get => _autonomyCanTriggerBrainDrain;
            set { _autonomyCanTriggerBrainDrain = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerBubbles = false;
        /// <summary>
        /// Allow autonomous bubble minigame starts (requires Lv.20)
        /// </summary>
        public bool AutonomyCanTriggerBubbles
        {
            get => _autonomyCanTriggerBubbles;
            set { _autonomyCanTriggerBubbles = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanComment = true;
        /// <summary>
        /// Allow autonomous AI-generated comments
        /// </summary>
        public bool AutonomyCanComment
        {
            get => _autonomyCanComment;
            set { _autonomyCanComment = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerMindWipe = true;
        /// <summary>
        /// Allow autonomous mindwipe audio triggers
        /// </summary>
        public bool AutonomyCanTriggerMindWipe
        {
            get => _autonomyCanTriggerMindWipe;
            set { _autonomyCanTriggerMindWipe = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerLockCard = true;
        /// <summary>
        /// Allow autonomous lock card triggers (Level 35+)
        /// </summary>
        public bool AutonomyCanTriggerLockCard
        {
            get => _autonomyCanTriggerLockCard;
            set { _autonomyCanTriggerLockCard = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerSpiral = true;
        /// <summary>
        /// Allow autonomous spiral overlay pulses
        /// </summary>
        public bool AutonomyCanTriggerSpiral
        {
            get => _autonomyCanTriggerSpiral;
            set { _autonomyCanTriggerSpiral = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerPinkFilter = true;
        /// <summary>
        /// Allow autonomous pink filter pulses
        /// </summary>
        public bool AutonomyCanTriggerPinkFilter
        {
            get => _autonomyCanTriggerPinkFilter;
            set { _autonomyCanTriggerPinkFilter = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerBouncingText = true;
        /// <summary>
        /// Allow autonomous bouncing text (Level 60+)
        /// </summary>
        public bool AutonomyCanTriggerBouncingText
        {
            get => _autonomyCanTriggerBouncingText;
            set { _autonomyCanTriggerBouncingText = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerBubbleCount = true;
        /// <summary>
        /// Allow autonomous bubble count minigame (Level 50+)
        /// </summary>
        public bool AutonomyCanTriggerBubbleCount
        {
            get => _autonomyCanTriggerBubbleCount;
            set { _autonomyCanTriggerBubbleCount = value; OnPropertyChanged(); }
        }

        private int _autonomyAnnouncementChance = 50;
        /// <summary>
        /// Chance (0-100%) that she announces before triggering an action
        /// </summary>
        public int AutonomyAnnouncementChance
        {
            get => _autonomyAnnouncementChance;
            set { _autonomyAnnouncementChance = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        #endregion

        #region Patreon Integration

        private int _patreonTier = 0;
        /// <summary>
        /// Cached Patreon subscription tier (0=None, 1=Level1, 2=Level2)
        /// Used for UI display only - actual validation done by PatreonService
        /// </summary>
        public int PatreonTier
        {
            get => _patreonTier;
            set { _patreonTier = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
        }

        private DateTime _lastPatreonVerification = DateTime.MinValue;
        /// <summary>
        /// Last time Patreon subscription was verified with the server
        /// </summary>
        public DateTime LastPatreonVerification
        {
            get => _lastPatreonVerification;
            set { _lastPatreonVerification = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether the cached Patreon tier is still valid (within 24 hours)
        /// </summary>
        [JsonIgnore]
        public bool PatreonCacheValid =>
            (DateTime.UtcNow - LastPatreonVerification).TotalHours < 24;

        #endregion

        #region Haptics

        private HapticSettings _haptics = new();
        /// <summary>
        /// Haptic feedback settings for Lovense/Buttplug devices
        /// </summary>
        public HapticSettings Haptics
        {
            get => _haptics;
            set { _haptics = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates and corrects any invalid settings
        /// </summary>
        public List<string> ValidateAndCorrect()
        {
            var corrections = new List<string>();

            // Clamp values to safe ranges
            if (_flashFrequency < 1 || _flashFrequency > 10)
            {
                corrections.Add($"Flash frequency adjusted from {_flashFrequency} to valid range");
                _flashFrequency = Math.Clamp(_flashFrequency, 1, 10);
            }

            if (_hydraLimit > 20)
            {
                corrections.Add($"Hydra limit reduced from {_hydraLimit} to 20 (hard cap)");
                _hydraLimit = 20;
            }

            if (_videosPerHour > 20)
            {
                corrections.Add($"Videos per hour reduced from {_videosPerHour} to 20 (hard cap)");
                _videosPerHour = 20;
            }

            if (_simultaneousImages > 20)
            {
                corrections.Add($"Simultaneous images reduced from {_simultaneousImages} to 20");
                _simultaneousImages = 20;
            }

            return corrections;
        }

        /// <summary>
        /// Checks for dangerous setting combinations
        /// </summary>
        public List<string> CheckDangerousCombinations()
        {
            var warnings = new List<string>();

            if (StrictLockEnabled && !PanicKeyEnabled)
            {
                warnings.Add("⚠ STRICT LOCK + NO PANIC KEY: You will NOT be able to exit videos!");
            }

            if (StrictLockEnabled && VideosPerHour > 10)
            {
                warnings.Add("⚠ High video frequency with strict lock enabled");
            }

            if (CorruptionMode && HydraLimit > 15)
            {
                warnings.Add("⚠ Hydra mode with high limit may cause performance issues");
            }

            if (!PanicKeyEnabled)
            {
                warnings.Add("⚠ Panic key (ESC) is disabled - you cannot emergency stop!");
            }

            return warnings;
        }

        /// <summary>
        /// Creates a deep copy of settings
        /// </summary>
        public AppSettings Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
        }

        #endregion
    }
}