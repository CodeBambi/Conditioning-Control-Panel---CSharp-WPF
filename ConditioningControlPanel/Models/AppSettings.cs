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