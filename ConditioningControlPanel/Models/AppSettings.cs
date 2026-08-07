using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A single emote slot: an icon (usually an emoji, may be empty) and a short
    /// text label. Persisted as part of AppSettings.RemoteEmotePresets — exactly
    /// 5 entries are kept; OnDeserialized pads/truncates.
    /// </summary>
    public class EmotePreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _icon = "";
        [JsonProperty("Icon")]
        public string Icon
        {
            get => _icon;
            set { _icon = value ?? ""; OnPropertyChanged(); }
        }

        private string _text = "";
        [JsonProperty("Text")]
        public string Text
        {
            get => _text;
            set { _text = value ?? ""; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// Legacy content mode enum. Kept for settings deserialization backward compatibility.
    /// Use App.Mods (ModService) instead.
    /// </summary>
    [Obsolete("Use App.Mods (ModService) and ActiveModId instead")]
    public enum ContentMode
    {
        BambiSleep,
        SissyHypno
    }

    /// <summary>
    /// Rendering quality tier used to scale down expensive work (image decode resolution,
    /// bitmap scaling quality, glow effects, Brain Drain blur cost, animation FPS, window caps)
    /// when the machine is under load or the user opts into a lighter mode.
    /// Quality = full fidelity; Performance = cheapest. See Services/PerformanceProfile.cs.
    /// </summary>
    public enum PerformanceTier
    {
        Quality,
        Balanced,
        Performance
    }

    /// <summary>
    /// How much motion the UI is allowed to show.
    /// Full = everything (ambient loops, particles, parallax, entrance staggers).
    /// Reduced = crossfades and state transitions only — no looping FX, no particles, no parallax.
    /// Off = no animation at all; every helper snaps straight to the end state.
    /// Capped to Reduced automatically when Windows' "Animation effects" is off
    /// (SystemParameters.ClientAreaAnimation). See Services/MotionFx.cs.
    /// </summary>
    public enum MotionLevel
    {
        Full,
        Reduced,
        Off
    }

    /// <summary>
    /// Application settings model - matches Python DEFAULT_SETTINGS
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            // Bark hook: surface every numeric/bool setting change as a SettingChanged trigger so
            // the avatar can react to toggles, thresholds and easter-egg values. BarkService reads
            // the new value off this instance by name and ignores non-numeric props. App.Bark is
            // null during startup load, so no spurious barks while settings deserialize.
            try { ConditioningControlPanel.App.Bark?.NotifySettingChanged(name); } catch { /* never break settings for a bark */ }
        }

        #region Language

        private string _language = "en";
        public string Language
        {
            get => _language;
            set { _language = value ?? "en"; OnPropertyChanged(); }
        }

        #endregion

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

        // Remote-control emote slots (5 fixed, user-editable). OnDeserialized
        // pads or truncates to exactly 5 so the UI never has to defend against
        // odd counts. Default set lives in DefaultRemoteEmotePresets() below.
        private List<EmotePreset> _remoteEmotePresets = DefaultRemoteEmotePresets();
        public List<EmotePreset> RemoteEmotePresets
        {
            get => _remoteEmotePresets;
            set { _remoteEmotePresets = value ?? DefaultRemoteEmotePresets(); OnPropertyChanged(); }
        }

        internal static List<EmotePreset> DefaultRemoteEmotePresets() => new()
        {
            // Emoji written as \U escapes (not literal glyphs) so they survive
            // compilation regardless of the build machine's source code page —
            // this file has no UTF-8 BOM, and literal emoji here were being
            // mangled into mojibake (e.g. "ðŸ™") in the emote picker.
            new EmotePreset { Icon = "\U0001F64F", Text = "yes" },       // 🙏 folded hands
            new EmotePreset { Icon = "\U0001F97A", Text = "more" },      // 🥺 pleading face
            new EmotePreset { Icon = "\U0001FAE0", Text = "drifting" },  // 🫠 melting face
            new EmotePreset { Icon = "\U0001F49C", Text = "thank you" }, // 💜 purple heart
            new EmotePreset { Icon = "\u26A0\uFE0F", Text = "too much" }, // ⚠️ warning + emoji variation selector
        };

        [OnDeserialized]
        internal void OnDeserializedNormalizeEmotePresets(StreamingContext _)
        {
            if (_remoteEmotePresets == null)
            {
                _remoteEmotePresets = DefaultRemoteEmotePresets();
                return;
            }
            // Pad short → use defaults for the missing tail slots.
            var defaults = DefaultRemoteEmotePresets();
            while (_remoteEmotePresets.Count < 5)
            {
                _remoteEmotePresets.Add(defaults[_remoteEmotePresets.Count]);
            }
            // Truncate long → keep the first 5 only.
            if (_remoteEmotePresets.Count > 5)
            {
                _remoteEmotePresets = _remoteEmotePresets.GetRange(0, 5);
            }
            // Migration: older builds compiled the emoji defaults from a BOM-less
            // source as Windows-1252, persisting mojibake icons (the "yes" preset
            // showed a garbled "df Y(tm)" string instead of a folded-hands emoji).
            // A real emote icon is ASCII text or an emoji whose chars are all
            // >= U+2000 or surrogate pairs; mojibake always contains a Latin-1
            // supplement char (U+00A0..U+00FF). Detect that and restore the correct
            // default icon for that slot.
            for (int i = 0; i < _remoteEmotePresets.Count && i < defaults.Count; i++)
            {
                if (_remoteEmotePresets[i] != null && LooksLikeEmojiMojibake(_remoteEmotePresets[i].Icon))
                    _remoteEmotePresets[i].Icon = defaults[i].Icon;
            }
        }

        /// <summary>
        /// True when an emote icon carries the signature of "UTF-8 bytes mis-decoded
        /// as Windows-1252" mojibake: at least one character in the Latin-1 supplement
        /// range (U+00A0..U+00FF). Legitimate icons (ASCII text or real emoji whose
        /// code points are all >= U+2000 or surrogate pairs) never contain those.
        /// </summary>
        private static bool LooksLikeEmojiMojibake(string? icon)
        {
            if (string.IsNullOrEmpty(icon)) return false;
            foreach (var ch in icon)
            {
                if (ch >= 0x00A0 && ch <= 0x00FF) return true;
            }
            return false;
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
            set { _selectedAvatarSet = Math.Clamp(value, 0, 7); OnPropertyChanged(); }
        }

        private bool _welcomed = false;
        public bool Welcomed
        {
            get => _welcomed;
            set { _welcomed = value; OnPropertyChanged(); }
        }

        private bool _modPickerShown = false;
        /// <summary>
        /// True once the first-run mod picker (<c>ModPickerDialog</c>) has been offered FOR REAL. The
        /// picker is a first-launch courtesy, not a recurring prompt — after this, mods are downloaded
        /// from the Mod Manager. Set BEFORE the dialog is shown so a crash inside it cannot turn the
        /// picker into an every-launch popup. Defaults false, so existing installs upgrading into the
        /// modular build see it once too (docs/CONTENT_PACKS_PLAN.md §4/§5).
        ///
        /// Handed BACK (set false again) when that showing ended in the offline state: with no
        /// manifest every card is dead, so latching would cost an upgrader the content picker
        /// forever for the crime of launching without network. <see cref="ModPickerOfflineOffers"/>
        /// bounds how many times that re-arm can happen.
        /// </summary>
        public bool ModPickerShown
        {
            get => _modPickerShown;
            set { _modPickerShown = value; OnPropertyChanged(); }
        }

        private int _modPickerOfflineOffers = 0;
        /// <summary>
        /// How many times the mod picker has opened only to land in its offline (no-manifest) state.
        /// The re-arm above stops at <c>ModPickerDialog.MaxOfflineOffers</c>, so a user who is
        /// deliberately offline forever sees the dead screen a handful of times, not every launch.
        /// Never reset — a successful showing latches <see cref="ModPickerShown"/> and ends the
        /// question either way.
        /// </summary>
        public int ModPickerOfflineOffers
        {
            get => _modPickerOfflineOffers;
            set { _modPickerOfflineOffers = value; OnPropertyChanged(); }
        }

        private string _pendingModActivationId = "";
        /// <summary>
        /// Mod the user picked in the first-run mod picker whose content was still downloading, so it
        /// could not be activated yet (<c>Services.PendingModActivation</c>). Persisted because the
        /// download can outlive the session that started it — a restart mid-download still ends up on
        /// the mod the user chose. Cleared once applied, and dropped the moment the user switches mods
        /// by hand: a manual choice outranks a queued one.
        /// </summary>
        public string PendingModActivationId
        {
            get => _pendingModActivationId;
            set { _pendingModActivationId = value ?? ""; OnPropertyChanged(); }
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

        private List<string> _recentBugReports = new();
        /// <summary>
        /// Ring buffer of the report numbers (BUG-XXXXXXXXXX) the server handed back for bug
        /// reports and suggestions this user filed (#769). Kept so the number survives the
        /// success dialog and can be quoted in Discord later — surfaced by the "My Reports"
        /// list in App Info. Entry format: "{token}|{ISO-8601 UTC timestamp}|{kind}" where
        /// kind is "bug" or "suggestion". Newest last; capped at
        /// <see cref="Services.BugReportService.MaxRecentReports"/> (oldest trimmed on insert).
        /// </summary>
        [JsonProperty("recent_bug_reports")]
        public List<string> RecentBugReports
        {
            get => _recentBugReports;
            set { _recentBugReports = value ?? new List<string>(); OnPropertyChanged(); }
        }

        private string _dismissedAnnouncementId = "";
        /// <summary>
        /// ID of the last server announcement the user dismissed. Prevents showing the same announcement again.
        /// </summary>
        public string DismissedAnnouncementId
        {
            get => _dismissedAnnouncementId;
            set { _dismissedAnnouncementId = value ?? ""; OnPropertyChanged(); }
        }

        private string _lastSeasonResetSeen = "";
        /// <summary>
        /// "YYYY-MM" (UTC) of the most recent monthly season-reset popup the user has dismissed.
        /// The leaderboard rotates seasons on the 1st of every month UTC, which also resets
        /// current level/XP and daily streak. Achievements, HighestLevelEver, skills, and
        /// lifetime XP are preserved server-side. Empty for users who have never seen the
        /// popup; we only show it to users who have any progression to lose (HighestLevelEver >= 2).
        /// </summary>
        public string LastSeasonResetSeen
        {
            get => _lastSeasonResetSeen;
            set { _lastSeasonResetSeen = value ?? ""; OnPropertyChanged(); }
        }

        private bool _seasonResetPending = false;
        /// <summary>
        /// Set by ProfileSyncService when the server returns <c>level_reset</c> (monthly rollover
        /// OR an admin reset of this account). Tells MainWindow.TryPresentSeasonRecap to surface
        /// the recap card even when the UTC month already matches LastSeasonResetSeen (i.e. a
        /// mid-month admin reset). Cleared once the card has been presented. Persisted so a reset
        /// that arrives late in a session still surfaces on the next launch.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool SeasonResetPending
        {
            get => _seasonResetPending;
            set { _seasonResetPending = value; OnPropertyChanged(); }
        }

        #endregion

        #region Skill Tree / Enhancements

        private int _skillPoints = 0;
        /// <summary>
        /// Available skill points to spend on the enhancement tree.
        /// Earned per level-up (SkillTreeService.PointsPerLevel) and per 100 bubbles popped.
        /// </summary>
        public int SkillPoints
        {
            get => _skillPoints;
            set { _skillPoints = Math.Max(0, value); OnPropertyChanged(); }
        }

        /// <summary>
        /// Persisted flag indicating we need to acknowledge a force_skills_reset to the server.
        /// Survives crashes so we don't re-apply the reset on restart.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool PendingSkillsResetAck { get; set; }

        private List<string> _unlockedSkills = new();
        /// <summary>
        /// IDs of skills that have been unlocked in the enhancement tree.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> UnlockedSkills
        {
            get => _unlockedSkills;
            set { _unlockedSkills = value ?? new(); OnPropertyChanged(); }
        }

        private double _totalConditioningMinutes = 0;
        /// <summary>
        /// Total conditioning time across all sessions (accumulated).
        /// Used by the "Pink Hours" skill display.
        /// </summary>
        public double TotalConditioningMinutes
        {
            get => _totalConditioningMinutes;
            set { _totalConditioningMinutes = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _totalSessions = 0;
        /// <summary>
        /// Total number of conditioning sessions started.
        /// </summary>
        public int TotalSessions
        {
            get => _totalSessions;
            set { _totalSessions = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _dailyQuestStreak = 0;
        /// <summary>
        /// Consecutive days of completing the daily quest.
        /// Used by "Perfect Bimbo Week" skill.
        /// </summary>
        public int DailyQuestStreak
        {
            get => _dailyQuestStreak;
            set { _dailyQuestStreak = Math.Max(0, value); OnPropertyChanged(); }
        }

        #region Bark system

        private int _barkChatSuppressionMs = 10000;
        /// <summary>
        /// How long (ms) to suppress non-safety barks after the companion is busy / a chat
        /// exchange, so barks don't talk over an active conversation. (Bark system, Fork E.)
        /// </summary>
        public int BarkChatSuppressionMs
        {
            get => _barkChatSuppressionMs;
            set { _barkChatSuppressionMs = Math.Max(0, value); OnPropertyChanged(); }
        }

        private bool _newYearNoteReactionSeen = false;
        /// <summary>Once-ever latch for the New Year note companion reaction (egg PR uses this).</summary>
        public bool NewYearNoteReactionSeen
        {
            get => _newYearNoteReactionSeen;
            set { _newYearNoteReactionSeen = value; OnPropertyChanged(); }
        }

        private List<string> _barkLifetimeFired = new();
        /// <summary>
        /// Persisted one-shot latches for barks scoped lifetime/tier. Lifetime keys are the
        /// rule id; tier keys are "id@Tier" so a tier change naturally re-arms the bark.
        /// Session-scope one-shots stay in-memory and are NOT stored here.
        /// </summary>
        public List<string> BarkLifetimeFired
        {
            get => _barkLifetimeFired;
            set { _barkLifetimeFired = value ?? new(); OnPropertyChanged(); }
        }

        /// <summary>Record a lifetime/tier bark latch key; returns false if already present. Persists on change.</summary>
        public bool MarkBarkFired(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_barkLifetimeFired.Contains(key)) return false;
            _barkLifetimeFired.Add(key);
            OnPropertyChanged(nameof(BarkLifetimeFired));
            return true;
        }

        public bool IsBarkFired(string key) =>
            !string.IsNullOrEmpty(key) && _barkLifetimeFired.Contains(key);

        private Dictionary<string, List<string>> _barkVariantRotation = new();
        /// <summary>
        /// Persisted per-rule variant rotation: rule id → bark line ids (BarkService.BarkLineId)
        /// already spoken in the CURRENT cycle. Carries the no-repeat-until-exhausted guarantee across
        /// sessions so a rule's pool doesn't restart every launch (the main cause of "same few" webcam
        /// lines). Reset for a rule when its pool recycles.
        /// </summary>
        public Dictionary<string, List<string>> BarkVariantRotation
        {
            get => _barkVariantRotation;
            set { _barkVariantRotation = value ?? new(); OnPropertyChanged(); }
        }

        private List<string> _barkIdleRotation = new();
        /// <summary>
        /// Persisted idle-bark rotation: rule ids of idle lines already played this cycle (idle lines are
        /// single-variant rules, tracked by id). Same cross-session no-repeat intent as
        /// <see cref="BarkVariantRotation"/>. Reset when the idle pool is exhausted.
        /// </summary>
        public List<string> BarkIdleRotation
        {
            get => _barkIdleRotation;
            set { _barkIdleRotation = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        private DateTime? _lastDailyQuestDate = null;
        /// <summary>
        /// Last date a daily quest was completed (UTC date only).
        /// </summary>
        public DateTime? LastDailyQuestDate
        {
            get => _lastDailyQuestDate;
            set { _lastDailyQuestDate = value; OnPropertyChanged(); }
        }

        private int _streakShieldsRemaining = 0;
        /// <summary>
        /// Weekly streak shields remaining.
        /// Granted by "Good Girl Streak" skill.
        /// </summary>
        public int StreakShieldsRemaining
        {
            get => _streakShieldsRemaining;
            set { _streakShieldsRemaining = Math.Max(0, value); OnPropertyChanged(); }
        }

        private DateTime? _lastStreakShieldResetDate = null;
        /// <summary>
        /// Date when weekly streak shields were last reset.
        /// Resets on Sunday.
        /// </summary>
        public DateTime? LastStreakShieldResetDate
        {
            get => _lastStreakShieldResetDate;
            set { _lastStreakShieldResetDate = value; OnPropertyChanged(); }
        }

        private List<DateTime> _streakShieldUsedDates = new();
        /// <summary>
        /// Dates where a streak shield was used to cover a missed day.
        /// </summary>
        public List<DateTime> StreakShieldUsedDates
        {
            get => _streakShieldUsedDates;
            set { _streakShieldUsedDates = value ?? new(); OnPropertyChanged(); }
        }

        private bool _seasonalStreakRecoveryUsed = false;
        /// <summary>
        /// Whether "Oopsie Insurance" streak recovery has been used this season.
        /// </summary>
        public bool SeasonalStreakRecoveryUsed
        {
            get => _seasonalStreakRecoveryUsed;
            set { _seasonalStreakRecoveryUsed = value; OnPropertyChanged(); }
        }

        private int _streakFixCharges = 0;
        /// <summary>
        /// Cumulable streak-fix charges ("Oopsie Insurance"). Granted +1 every season
        /// rollover, server-authoritative, never expires. Spending one is free.
        /// </summary>
        public int StreakFixCharges
        {
            get => _streakFixCharges;
            set { _streakFixCharges = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _nightTimeUsageCount = 0;
        /// <summary>
        /// Number of times app was used between 11pm-5am.
        /// Used to unlock "Night Shift" secret skill.
        /// </summary>
        public int NightTimeUsageCount
        {
            get => _nightTimeUsageCount;
            set { _nightTimeUsageCount = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _earlyMorningUsageCount = 0;
        /// <summary>
        /// Number of times app was used between 5am-8am.
        /// Used to unlock "Early Bird Bimbo" secret skill.
        /// </summary>
        public int EarlyMorningUsageCount
        {
            get => _earlyMorningUsageCount;
            set { _earlyMorningUsageCount = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _freeRerollsUsedToday = 0;
        /// <summary>
        /// Number of free quest rerolls used today.
        /// Resets daily. Max determined by skills.
        /// </summary>
        public int FreeRerollsUsedToday
        {
            get => _freeRerollsUsedToday;
            set { _freeRerollsUsedToday = Math.Max(0, value); OnPropertyChanged(); }
        }

        private DateTime? _lastRerollResetDate = null;
        /// <summary>
        /// Date when daily free rerolls were last reset.
        /// </summary>
        public DateTime? LastRerollResetDate
        {
            get => _lastRerollResetDate;
            set { _lastRerollResetDate = value; OnPropertyChanged(); }
        }

        private int _bonusDailyRerolls = 0;
        /// <summary>
        /// Admin-granted bonus daily quest rerolls (from server).
        /// </summary>
        public int BonusDailyRerolls
        {
            get => _bonusDailyRerolls;
            set { _bonusDailyRerolls = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _bonusWeeklyRerolls = 0;
        /// <summary>
        /// Admin-granted bonus weekly quest rerolls (from server).
        /// </summary>
        public int BonusWeeklyRerolls
        {
            get => _bonusWeeklyRerolls;
            set { _bonusWeeklyRerolls = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _currentStreak = 0;
        /// <summary>
        /// Current consecutive day streak (used for streak multiplier skill).
        /// </summary>
        public int CurrentStreak
        {
            get => _currentStreak;
            set
            {
                _currentStreak = Math.Max(0, value);
                // Track highest streak achieved
                if (_currentStreak > HighestStreak)
                {
                    HighestStreak = _currentStreak;
                }
                OnPropertyChanged();
            }
        }

        private int _highestStreak = 0;
        /// <summary>
        /// Highest consecutive day streak ever achieved (for Trophy Case display).
        /// </summary>
        public int HighestStreak
        {
            get => _highestStreak;
            set { _highestStreak = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _lastAnnouncedStreakMilestone = 0;
        /// <summary>
        /// Highest daily-streak milestone (7/14/30/60/100/365) the companion has already
        /// celebrated in her app-open greeting, so each milestone is voiced once. Reset
        /// downward when the streak drops below it so re-reaching it announces again.
        /// </summary>
        public int LastAnnouncedStreakMilestone
        {
            get => _lastAnnouncedStreakMilestone;
            set { _lastAnnouncedStreakMilestone = Math.Max(0, value); OnPropertyChanged(); }
        }

        private DateTime? _lastStreakDate = null;
        /// <summary>
        /// Last date the streak was maintained.
        /// </summary>
        public DateTime? LastStreakDate
        {
            get => _lastStreakDate;
            set { _lastStreakDate = value; OnPropertyChanged(); }
        }

        private bool _pinkRushActive = false;
        /// <summary>
        /// Whether a Pink Rush bonus window is currently active.
        /// </summary>
        [JsonIgnore]
        public bool PinkRushActive
        {
            get => _pinkRushActive;
            set { _pinkRushActive = value; OnPropertyChanged(); }
        }

        private DateTime? _pinkRushEndTime = null;
        /// <summary>
        /// When the current Pink Rush window ends.
        /// </summary>
        [JsonIgnore]
        public DateTime? PinkRushEndTime
        {
            get => _pinkRushEndTime;
            set { _pinkRushEndTime = value; OnPropertyChanged(); }
        }

        #endregion

        #region Companion Greeting

        private DateTime? _lastSeenUtc = null;
        /// <summary>
        /// Local-only UTC timestamp of when the app was last open. Used solely to vary the
        /// companion's warm in-app welcome-back greeting by absence length (see
        /// AvatarTubeWindow.ShowGreeting / BuildAbsenceGreeting). Persisted to the local
        /// settings file only — it is never added to any server request, sync payload, or
        /// telemetry.
        /// </summary>
        public DateTime? LastSeenUtc
        {
            get => _lastSeenUtc;
            set { _lastSeenUtc = value; OnPropertyChanged(); }
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
            set
            {
                _flashClickable = value;
                // Self-heal for the decoupling migration: it turned the gaze toggles off
                // to preserve "no interaction" intent while clicking was off. The moment
                // the user turns clicking back ON, that intent is gone — restore the gaze
                // toggles the migration took, exactly once (support: "gaze-to-click
                // doesn't work", v6.2.11). Users who toggled gaze off themselves never
                // have the flag set, so their choice is untouched.
                if (value && FlashGazeDisabledByDecoupling)
                {
                    FlashGazeDisabledByDecoupling = false;
                    FlashGazePopEnabled = true;
                    FlashGazeLingerEnabled = true;
                }
                OnPropertyChanged();
            }
        }

        // Set by RunFlashClickableDecouplingMigration when IT (not the user) turned the
        // gaze toggles off; consumed by the FlashClickable setter's self-heal above.
        private bool _flashGazeDisabledByDecoupling = false;
        public bool FlashGazeDisabledByDecoupling
        {
            get => _flashGazeDisabledByDecoupling;
            set { _flashGazeDisabledByDecoupling = value; OnPropertyChanged(); }
        }

        private bool _corruptionMode = false; // Hydra effect
        public bool CorruptionMode
        {
            get => _corruptionMode;
            set { _corruptionMode = value; OnPropertyChanged(); }
        }

        private bool _hydraLinkedTiming = true;
        /// <summary>
        /// Controls hydra spawn timing~ 🐙✨
        /// true  = "Linked" — hydra children expire when the original flash event expires.
        /// false = "Independent" — each hydra spawn gets its own full-duration lifetime.
        /// CopilotNotes: Default true preserves legacy behavior where all windows died together.
        /// </summary>
        public bool HydraLinkedTiming
        {
            get => _hydraLinkedTiming;
            set { _hydraLinkedTiming = value; OnPropertyChanged(); }
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

        private bool _flashGlowEnabled = true;
        public bool FlashGlowEnabled
        {
            get => _flashGlowEnabled;
            set { _flashGlowEnabled = value; OnPropertyChanged(); }
        }

        // Solid mode: render flashes as children of the ONE shared click-through host window
        // (ChaosBubbleHostOverlay) instead of one topmost layered window per flash. The per-flash
        // window churn near screen centre is what some fullscreen games (e.g. Overwatch) react
        // badly to — the same reason bubble solid mode exists. Solid-mode flashes are click-through
        // (no mouse pop/hydra clicks); gaze-pop and stare-linger still work.
        private bool _flashSolidMode = false;
        public bool FlashSolidMode
        {
            get => _flashSolidMode;
            set { _flashSolidMode = value; OnPropertyChanged(); }
        }

        private int _flashDuration = 5; // Duration in seconds when audio is disabled (1-30)
        public int FlashDuration
        {
            get => _flashDuration;
            set { _flashDuration = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        // Gaming quality-of-life (#770): keep flashes out of a centered square on every monitor so
        // they never land on the crosshair / HUD centre. This is a PURE GLOBAL USER PREFERENCE —
        // deliberately absent from SessionSettings, SessionEngine's save/restore, Preset and the
        // remote/quiz generators, so no session or preset can ever stomp a gamer's exclusion box.
        private bool _flashAvoidCenter = false;
        public bool FlashAvoidCenter
        {
            get => _flashAvoidCenter;
            set { _flashAvoidCenter = value; OnPropertyChanged(); }
        }

        private int _flashCenterExclusionPercent = 25; // 5-60% of the SHORTER monitor edge
        /// <summary>
        /// Size of the centered no-flash square, as a percentage of the shorter monitor edge.
        /// The 60 ceiling is deliberate: above that the legal spawn bands vanish for large images
        /// (high ImageScale), which would force the unconstrained fallback on every spawn.
        /// </summary>
        public int FlashCenterExclusionPercent
        {
            get => _flashCenterExclusionPercent;
            set { _flashCenterExclusionPercent = Math.Clamp(value, 5, 60); OnPropertyChanged(); }
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

        // Video duration filter (seconds). 0 = no limit. Applied when refilling
        // the video queue; videos outside the [min, max] range are excluded so
        // a session can be pinned to short clips or long ones without
        // shuffling content packs.
        private int _videoMinDurationSeconds = 0;
        public int VideoMinDurationSeconds
        {
            get => _videoMinDurationSeconds;
            set { _videoMinDurationSeconds = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _videoMaxDurationSeconds = 0;
        public int VideoMaxDurationSeconds
        {
            get => _videoMaxDurationSeconds;
            set { _videoMaxDurationSeconds = Math.Max(0, value); OnPropertyChanged(); }
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

        private int _duckingLevel = 80; // 0-100% (80% = reduce other audio to 20%)
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

        private bool _forceShowBambiCloud = false;
        /// <summary>
        /// User override: reveal the BambiCloud browser toggle even on mods whose
        /// manifest hides it (ShowBambiCloudOption = false). The mod's own default
        /// site (usually HypnoTube) stays selected; this only makes the BambiCloud
        /// radio available to click. Mods that already show BambiCloud are unaffected.
        /// </summary>
        public bool ForceShowBambiCloud
        {
            get => _forceShowBambiCloud;
            set { _forceShowBambiCloud = value; OnPropertyChanged(); }
        }

        private bool _backgroundMusicEnabled = true;
        public bool BackgroundMusicEnabled
        {
            get => _backgroundMusicEnabled;
            set { _backgroundMusicEnabled = value; OnPropertyChanged(); }
        }

        private bool _browserVideoMuted = false;
        /// <summary>
        /// When true, the integrated browser's audio (BambiCloud / HypnoTube video)
        /// is muted via CoreWebView2.IsMuted. Lets users run their own audio
        /// alongside CCP without the browser video doubling on top.
        /// </summary>
        public bool BrowserVideoMuted
        {
            get => _browserVideoMuted;
            set { _browserVideoMuted = value; OnPropertyChanged(); }
        }

        private bool _protectBrowserVideoPlayback = true;
        /// <summary>
        /// When true, nothing interrupts a video playing in the integrated browser — not the
        /// mandatory-video scheduler, not Takeover actions, not chaos effect bubbles. Applies to
        /// videos the user started themselves as well as ones the app started, and holds for
        /// <see cref="BrowserVideoGraceSeconds"/> after playback stops so a clip isn't immediately
        /// followed by an interruption. Default on: web videos being interruptible was reported
        /// as the single most disruptive behaviour of the browser feature.
        /// </summary>
        [JsonProperty]
        public bool ProtectBrowserVideoPlayback
        {
            get => _protectBrowserVideoPlayback;
            set { _protectBrowserVideoPlayback = value; OnPropertyChanged(); }
        }

        private int _browserVideoGraceSeconds = 45;
        /// <summary>
        /// Cool-off after a browser video ends during which interruptions are still deferred.
        /// Without this, the mandatory-video scheduler's reschedule and Takeover's retry tick can
        /// both fire on the very next tick, which read as "it restarted a video immediately after".
        /// </summary>
        [JsonProperty]
        public int BrowserVideoGraceSeconds
        {
            get => _browserVideoGraceSeconds;
            set { _browserVideoGraceSeconds = Math.Max(0, Math.Min(600, value)); OnPropertyChanged(); }
        }

        private string? _rememberedConfigJson;
        /// <summary>
        /// One-slot snapshot for the header "Remember" button — the conditioning
        /// config (as a Preset) plus the premium toggle states + browser mute.
        /// Null/empty = nothing remembered yet. Progression/XP are never captured.
        /// </summary>
        public string? RememberedConfigJson
        {
            get => _rememberedConfigJson;
            set { _rememberedConfigJson = value; OnPropertyChanged(); }
        }

        // MMDevice ID of the playback endpoint the user wants CCP audio routed to.
        // Empty = system default. Streaming use case: route CCP to a private headset
        // while the stream's default endpoint stays clean.
        private string _audioOutputDeviceId = "";
        public string AudioOutputDeviceId
        {
            get => _audioOutputDeviceId;
            set { _audioOutputDeviceId = value ?? ""; OnPropertyChanged(); }
        }

        // Friendly name of the chosen device, persisted as a fallback in case the
        // MMDevice ID changes across reboots/driver updates — we then re-resolve by name.
        private string _audioOutputDeviceName = "";
        public string AudioOutputDeviceName
        {
            get => _audioOutputDeviceName;
            set { _audioOutputDeviceName = value ?? ""; OnPropertyChanged(); }
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
            { "COCK ZOMBIE NOW", true },
            { "JUST OBEY", true },
            { "TURN YOUR BRAIN OFF", true },
            { "GOOD GIRLS DONT THINK", true },
            { "DONT THINK SILLY", true },
            { "COCK TURNS MY BRAIN OFF", true },
            { "I CANT RESIST MY TRIGGERS", true },
            { "THERES NO NEED TO THINK", true }
        };
        public Dictionary<string, bool> SubliminalPool
        {
            get => _subliminalPool;
            set { _subliminalPool = value ?? new(); OnPropertyChanged(); }
        }

        /// <summary>
        /// Tracks default subliminal triggers the user explicitly removed,
        /// so they don't get re-added on startup by MergeNewDefaultSubliminalTriggers.
        /// </summary>
        private HashSet<string> _removedDefaultSubliminals = new();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> RemovedDefaultSubliminals
        {
            get => _removedDefaultSubliminals;
            set => _removedDefaultSubliminals = value ?? new();
        }

        /// <summary>
        /// Subliminal phrases the user added manually via the editor. Protected from
        /// ModService.PruneCrossModSubliminals so a custom phrase that happens to match
        /// another built-in mod's default is never silently deleted on startup/mod-switch.
        /// Case-insensitive to match the prune's comparison and the editor's upper-casing.
        /// </summary>
        private HashSet<string> _userAddedSubliminals = new(StringComparer.OrdinalIgnoreCase);
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> UserAddedSubliminals
        {
            get => _userAddedSubliminals;
            set => _userAddedSubliminals = value == null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(value, StringComparer.OrdinalIgnoreCase);
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

        // Solid mode: render subliminal text cards as children of the ONE shared click-through
        // host window (ChaosBubbleHostOverlay) instead of a keep-alive layered window per screen.
        // Each subliminal keep-alive window is another full-screen layered surface contending on
        // WPF's single render thread — part of the freeze cluster (#461). Ignored while
        // SubliminalStealsFocus is on (the shared host is NOACTIVATE and can't steal focus).
        private bool _subliminalSolidMode = false;
        public bool SubliminalSolidMode
        {
            get => _subliminalSolidMode;
            set { _subliminalSolidMode = value; OnPropertyChanged(); }
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

        private bool _subAudioMuted = false;
        /// <summary>
        /// A plain MUTE for whisper/trigger audio, deliberately separate from
        /// <see cref="SubAudioEnabled"/>.
        ///
        /// The avatar's "Mute whispers" menu item and the Companion tab used to flip
        /// SubAudioEnabled, i.e. the feature's master ENABLE - which a session prescribes
        /// (SessionSettings.AudioWhispersEnabled) and the session feature lock therefore locks. So
        /// "mute" was really "opt out of the prescribed whispers dose", and once the lock landed it
        /// would have been unavailable exactly when a user most wants it: someone walks in and the
        /// sound needs to stop NOW.
        ///
        /// Splitting them lets the mute stay available during a session (it is a comfort/safety
        /// reflex, like volume) while the dose itself stays locked. Nothing here changes how much
        /// conditioning is scheduled - only whether you can currently hear it.
        /// </summary>
        public bool SubAudioMuted
        {
            get => _subAudioMuted;
            set { _subAudioMuted = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubAudioAudible)); }
        }

        /// <summary>
        /// The single gate every whisper/trigger playback path should test: the feature is on AND
        /// the user has not muted it. Prefer this over reading SubAudioEnabled directly, so a new
        /// playback site cannot silently ignore the mute.
        /// </summary>
        [JsonIgnore]
        public bool SubAudioAudible => SubAudioEnabled && !SubAudioMuted;

        private int _subAudioVolume = 50; // 0-100%
        public int SubAudioVolume
        {
            get => _subAudioVolume;
            set { _subAudioVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        #endregion

        #region System

        private ContentMode _contentMode = ContentMode.BambiSleep;
        /// <summary>
        /// [LEGACY] Content mode determines theming. Kept for migration only.
        /// New code should use ActiveModId instead.
        /// </summary>
        public ContentMode ContentMode
        {
            get => _contentMode;
            set
            {
                if (_contentMode != value)
                {
                    _contentMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBambiMode));
                    OnPropertyChanged(nameof(IsSissyMode));
                    OnPropertyChanged(nameof(ActiveHypnotubeLinks));
                    OnPropertyChanged(nameof(ContentModeDisplay));
                }
            }
        }

        /// <summary>
        /// Convenience property - true when active mod is BambiSleep.
        /// </summary>
        [JsonIgnore]
        public bool IsBambiMode => ActiveModId == BuiltInMods.BambiSleepId;

        /// <summary>
        /// Convenience property - true when active mod is SissyHypno.
        /// </summary>
        [JsonIgnore]
        public bool IsSissyMode => ActiveModId == BuiltInMods.SissyHypnoId;

        private string _activeModId = BuiltInMods.CCPDefaultId;
        /// <summary>
        /// The ID of the currently active mod. Replaces ContentMode enum.
        /// Fresh installs land on CCP Default; upgraded users retain their persisted choice.
        /// </summary>
        public string ActiveModId
        {
            get => _activeModId;
            set
            {
                if (_activeModId != value)
                {
                    _activeModId = value;
                    // Keep legacy field in sync for backward compat (only Bambi/Sissy map cleanly to the old enum)
                    _contentMode = value == BuiltInMods.SissyHypnoId ? ContentMode.SissyHypno : ContentMode.BambiSleep;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBambiMode));
                    OnPropertyChanged(nameof(IsSissyMode));
                    OnPropertyChanged(nameof(ActiveHypnotubeLinks));
                    OnPropertyChanged(nameof(ContentModeDisplay));
                }
            }
        }

        private bool _contentModeChosen = false;
        /// <summary>
        /// Whether the user has chosen a content mode / mod (shown on first run).
        /// </summary>
        public bool ContentModeChosen
        {
            get => _contentModeChosen;
            set { _contentModeChosen = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Alias for ContentModeChosen — used by new mod system code.
        /// </summary>
        [JsonIgnore]
        public bool ModChosen
        {
            get => _contentModeChosen;
            set => ContentModeChosen = value;
        }

        // Schema version stamped on every save by this v6.0 binary (see OnSerializingBumpSchemaVersion).
        // Default 0 covers every pre-v6 JSON and any v6 JSON written before this field existed.
        // MigrateFromContentModeToMod uses this as its primary gate so v6-saved settings don't
        // re-trigger the ContentMode→mod-ID mapping (which previously forced deliberate CCP Default
        // selections back to Bambi on second launch because ContentModeChosen=true looked like a
        // v5.x modal acceptance).
        private int _settingsSchemaVersion = 0;
        [JsonProperty("SettingsSchemaVersion")]
        public int SettingsSchemaVersion
        {
            get => _settingsSchemaVersion;
            set { _settingsSchemaVersion = value; OnPropertyChanged(); }
        }

        [OnSerializing]
        internal void OnSerializingBumpSchemaVersion(StreamingContext _)
        {
            // Any save written by this binary is a v6 save. Lock the migration gate so
            // subsequent launches skip the ContentMode→mod-ID mapping unconditionally.
            if (_settingsSchemaVersion < 6) _settingsSchemaVersion = 6;
        }

        /// <summary>
        /// [LEGACY] Per-mode pool backups. Kept for migration to *ByMod dictionaries.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<ContentMode, Dictionary<string, bool>>? SubliminalPoolByMode { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<ContentMode, Dictionary<string, bool>>? AttentionPoolByMode { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<ContentMode, Dictionary<string, bool>>? LockCardPhrasesByMode { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<ContentMode, List<string>>? CustomTriggersByMode { get; set; }

        /// <summary>
        /// Per-mod pool backups so custom edits survive mod switching.
        /// Keyed by mod ID string.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, Dictionary<string, bool>>? SubliminalPoolByMod { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, Dictionary<string, bool>>? AttentionPoolByMod { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, Dictionary<string, bool>>? LockCardPhrasesByMod { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, List<string>>? CustomTriggersByMod { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, Dictionary<string, bool>>? BouncingTextPoolByMod { get; set; }
        /// <summary>
        /// Per-mod video link pool (name -> URL) so the user's curated/added links survive mod
        /// switching. When set for a mod, this overrides the mod's shipped DefaultVideoLinks
        /// (ModService.GetVideoLinks). Keyed by mod ID string.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, Dictionary<string, string>>? VideoLinksByMod { get; set; }

        /// <summary>
        /// Per-mod user overrides for avatar tube layout (set via the Mod Manager's Tube Fit editor).
        /// When a mod id has an entry here it REPLACES the mod manifest's tubeLayout values.
        /// Keyed by mod ID string.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, ModTubeLayout>? TubeLayoutOverridesByMod { get; set; }

        /// <summary>
        /// Migrate legacy ContentMode-based settings to mod-based settings.
        /// Called once after deserialization when ActiveModId hasn't been set yet.
        /// </summary>
        internal void MigrateFromContentModeToMod()
        {
            // Primary gate: a v6-saved JSON is already past this migration. Without this guard,
            // a v6 user who deliberately picks CCP Default via the dropdown gets bumped to Bambi
            // on next launch because ContentModeChosen=true (set by ApplyActiveModChange on every
            // pick, including CCP Default) looks identical to "v5.x user who accepted the modal".
            if (_settingsSchemaVersion >= 6) return;

            // Secondary gate: if ActiveModId already deserialized to anything non-default, the user
            // has an explicit choice persisted and we shouldn't touch it.
            if (_activeModId != BuiltInMods.CCPDefaultId)
            {
                _settingsSchemaVersion = 6;
                return;
            }

            // Pre-v6 upgrade path: legacy users had ContentMode persisted but no ActiveModId yet.
            // Map their old enum choice (Bambi was the implicit default) onto a real mod ID.
            if (_contentMode == ContentMode.SissyHypno)
            {
                _activeModId = BuiltInMods.SissyHypnoId;
            }
            else if (ContentModeChosen)
            {
                // ContentModeChosen=true on a legacy install means they accepted the first-launch modal
                // and were assigned Bambi (the v5.x default). Preserve that choice on upgrade.
                _activeModId = BuiltInMods.BambiSleepId;
            }
            // else: fresh-install-like state → leave on CCPDefaultId

            // Lock the gate so this migration never re-fires for this user, even if a future
            // code path resets ActiveModId back to CCPDefaultId (e.g. CCP Default deliberate pick).
            _settingsSchemaVersion = 6;

            // Migrate *ByMode dictionaries to *ByMod
            if (SubliminalPoolByMode != null && SubliminalPoolByMod == null)
            {
                SubliminalPoolByMod = new Dictionary<string, Dictionary<string, bool>>();
                foreach (var kvp in SubliminalPoolByMode)
                {
                    var modId = kvp.Key == ContentMode.SissyHypno ? BuiltInMods.SissyHypnoId : BuiltInMods.BambiSleepId;
                    SubliminalPoolByMod[modId] = kvp.Value;
                }
            }
            if (AttentionPoolByMode != null && AttentionPoolByMod == null)
            {
                AttentionPoolByMod = new Dictionary<string, Dictionary<string, bool>>();
                foreach (var kvp in AttentionPoolByMode)
                {
                    var modId = kvp.Key == ContentMode.SissyHypno ? BuiltInMods.SissyHypnoId : BuiltInMods.BambiSleepId;
                    AttentionPoolByMod[modId] = kvp.Value;
                }
            }
            if (LockCardPhrasesByMode != null && LockCardPhrasesByMod == null)
            {
                LockCardPhrasesByMod = new Dictionary<string, Dictionary<string, bool>>();
                foreach (var kvp in LockCardPhrasesByMode)
                {
                    var modId = kvp.Key == ContentMode.SissyHypno ? BuiltInMods.SissyHypnoId : BuiltInMods.BambiSleepId;
                    LockCardPhrasesByMod[modId] = kvp.Value;
                }
            }
            if (CustomTriggersByMode != null && CustomTriggersByMod == null)
            {
                CustomTriggersByMod = new Dictionary<string, List<string>>();
                foreach (var kvp in CustomTriggersByMode)
                {
                    var modId = kvp.Key == ContentMode.SissyHypno ? BuiltInMods.SissyHypnoId : BuiltInMods.BambiSleepId;
                    CustomTriggersByMod[modId] = kvp.Value;
                }
            }
        }

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

        private HashSet<string> _disabledAssetPaths = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Set of relative paths to DISABLED assets. Items NOT in this set are active.
        /// This is the inverse of a whitelist - items are active by default.
        /// Paths are relative to EffectiveAssetsPath, stored with forward-slash separators
        /// and matched case-insensitively (Windows is case-insensitive at the FS level).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> DisabledAssetPaths
        {
            get => _disabledAssetPaths;
            set
            {
                if (value != null)
                {
                    _disabledAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in value)
                    {
                        if (!string.IsNullOrEmpty(p))
                            _disabledAssetPaths.Add(p.Replace('\\', '/'));
                    }
                }
                else
                {
                    _disabledAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                OnPropertyChanged();
            }
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

        private Dictionary<string, InstalledPackStamp> _installedContentPacks = new();
        /// <summary>
        /// Release-hosted content packs (audio/mod payload stripped out of the installer and fetched
        /// from the vX.Y.0 GitHub release) that are installed under
        /// <c>%LOCALAPPDATA%\ConditioningControlPanel\content\</c>. Maps pack id ->
        /// {contentVersion, sha256}: a SET, not a bool, so we can tell "installed and current" from
        /// "installed but the pack's bytes moved". Written by ReleaseContentService.
        /// Unrelated to <see cref="InstalledPackIds"/> (those are the encrypted creator packs).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, InstalledPackStamp> InstalledContentPacks
        {
            get => _installedContentPacks;
            set { _installedContentPacks = value ?? new(); OnPropertyChanged(); }
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

        private long _transferCacheCapBytes = 8L * 1024 * 1024 * 1024;
        /// <summary>
        /// Disk budget for the Goon Game transfer cache (compressed copies of the active pool).
        /// Clamped to 1-64 GB by TransferCacheStore - the settings file is never trusted.
        /// </summary>
        [JsonProperty]
        public long TransferCacheCapBytes
        {
            get => _transferCacheCapBytes;
            set { _transferCacheCapBytes = value; OnPropertyChanged(); }
        }

        private bool _transferCacheAutoCompress = false;
        /// <summary>
        /// When true, the compression queue starts itself instead of waiting for the user to press
        /// "Compress everything". Off by default: this is hours of GPU time on a big library.
        /// </summary>
        [JsonProperty]
        public bool TransferCacheAutoCompress
        {
            get => _transferCacheAutoCompress;
            set { _transferCacheAutoCompress = value; OnPropertyChanged(); }
        }

        private string? _lastSeenAssetPresetId = null;
        /// <summary>
        /// The preset the transfer cache last planned against. When this drifts from
        /// <see cref="CurrentAssetPresetId"/> the user gets the "your preset changed - N assets need
        /// compressing" nudge exactly once.
        /// </summary>
        [JsonProperty]
        public string? LastSeenAssetPresetId
        {
            get => _lastSeenAssetPresetId;
            set { _lastSeenAssetPresetId = value; OnPropertyChanged(); }
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

        // ---- Per-effect monitor targeting (suggestion #639) ----------------
        // Overrides the global DualMonitorEnabled screen selection for a single
        // effect. Sentinels: -1 = follow DualMonitorEnabled (default, backward
        // compatible), -2 = all monitors, 0..N = that specific monitor index
        // (into Screen.AllScreens). An index beyond the current monitor count is
        // NOT clamped here — it falls back to -1 behavior at resolve time (via
        // App.ResolveScreens) so a temporarily-unplugged monitor's target survives
        // a reconnect. See App.ResolveScreens for the resolution semantics.
        private int _spiralTargetMonitor = -1;
        /// <summary>Monitor target for the Spiral overlay. -1 = follow DualMonitorEnabled,
        /// -2 = all monitors, 0..N = specific monitor index. See <see cref="DualMonitorEnabled"/>.</summary>
        public int SpiralTargetMonitor
        {
            get => _spiralTargetMonitor;
            set { _spiralTargetMonitor = value; OnPropertyChanged(); }
        }

        private int _pinkFilterTargetMonitor = -1;
        /// <summary>Monitor target for the Pink filter tint. -1 = follow DualMonitorEnabled,
        /// -2 = all monitors, 0..N = specific monitor index. See <see cref="DualMonitorEnabled"/>.</summary>
        public int PinkFilterTargetMonitor
        {
            get => _pinkFilterTargetMonitor;
            set { _pinkFilterTargetMonitor = value; OnPropertyChanged(); }
        }

        private bool _fillAllMonitorsWithVideo;
        /// <summary>
        /// On 3+ monitors, give every secondary screen its own video decoder. Each LibVLC
        /// decoder is a full decode pass, so 3+ independent decoders lag high-res rigs (#389).
        /// Default off: with DualMonitor on, 1–2 monitor setups still fill every screen, but
        /// 3+ monitors decode the primary only unless the user opts in here. No effect on
        /// 1–2 monitor setups.
        /// </summary>
        public bool FillAllMonitorsWithVideo
        {
            get => _fillAllMonitorsWithVideo;
            set { _fillAllMonitorsWithVideo = value; OnPropertyChanged(); }
        }

        private bool _videoBlurredBackgroundEnabled = true;
        /// <summary>
        /// Fill the letterbox/pillarbox bars around a video that doesn't match the screen
        /// aspect (e.g. a vertical clip on a widescreen monitor) with an upscaled, blurred
        /// copy of the same video — the "blurred background" look from TikTok / YouTube Shorts,
        /// instead of flat black bars. Still one decoder per screen: the blurred fill and the
        /// sharp centred video are the SAME decoded frame composited in WPF (LibVLC memory
        /// callbacks, no airspace). Turn off to fall back to the classic VideoView render path
        /// with plain black bars.
        /// </summary>
        public bool VideoBlurredBackgroundEnabled
        {
            get => _videoBlurredBackgroundEnabled;
            set { _videoBlurredBackgroundEnabled = value; OnPropertyChanged(); }
        }

        private bool _browserVideoEngineEnabled = true;
        /// <summary>
        /// Play mandatory videos in out-of-process WebView2 windows (the player page at
        /// Resources/web/player) instead of in-process LibVLC. LibVLC stays the automatic fallback
        /// for anything the browser cannot decode, so turning this on never removes a playback path —
        /// it only changes which one is tried first. See docs/BROWSER_VIDEO_ENGINE_PLAN.md.
        ///
        /// Default ON from 6.7 (owner call for the pre-release; the engine shipped OFF-by-default
        /// after v6.6.3 and was never released, so no user has the key persisted and every install
        /// — fresh or upgrading — lands on true). Turning it off is still a one-click revert in
        /// Settings ▸ System, and <c>BrowserVideoGate</c> already routes to LibVLC on its own when
        /// the WebView2 runtime is missing, so ON is safe on a machine without Evergreen.
        /// </summary>
        [JsonProperty]
        public bool BrowserVideoEngineEnabled
        {
            get => _browserVideoEngineEnabled;
            set { _browserVideoEngineEnabled = value; OnPropertyChanged(); }
        }

        private bool _restrictGazeContentToCalibratedScreen = true;
        /// <summary>
        /// When enabled (and a webcam calibration exists), all gaze-reactive
        /// content (Bubble Pop, Blink Trainer, Flash gaze-pop targets, etc.)
        /// is pinned to the monitor calibration ran on, overriding
        /// <see cref="DualMonitorEnabled"/>. Prevents the multi-monitor
        /// case where content spawns on a screen the gaze pipeline can't
        /// project to. No-op when no calibration is loaded.
        /// </summary>
        public bool RestrictGazeContentToCalibratedScreen
        {
            get => _restrictGazeContentToCalibratedScreen;
            set { _restrictGazeContentToCalibratedScreen = value; OnPropertyChanged(); }
        }

        // ---- Gaze-reactive flash behavior (Phase 3) -----------------------
        // FlashGazePopEnabled gates the gaze-pop pipeline (dwell threshold
        // triggers a click). FlashGazeLingerEnabled gates the stare-linger
        // behavior (dwelling extends the flash's lifetime via BoostLifetime).
        // Both are independent; (Pop=OFF, Linger=ON) is a valid combination
        // and produces "stare to keep the flash alive but never auto-dismiss"
        // semantics. GazeFocusService branches the two paths so a disabled
        // pop flag never suppresses linger, and an enabled linger never
        // forces a pop.

        private bool _flashGazePopEnabled = true;
        public bool FlashGazePopEnabled
        {
            get => _flashGazePopEnabled;
            set { _flashGazePopEnabled = value; OnPropertyChanged(); }
        }

        private bool _flashGazeLingerEnabled = true;
        public bool FlashGazeLingerEnabled
        {
            get => _flashGazeLingerEnabled;
            set { _flashGazeLingerEnabled = value; OnPropertyChanged(); }
        }

        // How far out to push a flash window's death time on each linger
        // boost. CancelAfter is replaced each call, so this effectively
        // pins "alive for N more ms from now" while gaze is on the window.
        private int _flashGazeLingerExtensionMs = 1500;
        public int FlashGazeLingerExtensionMs
        {
            get => _flashGazeLingerExtensionMs;
            set { _flashGazeLingerExtensionMs = Math.Clamp(value, 250, 10000); OnPropertyChanged(); }
        }

        // VideoGazeClickEnabled gates the gaze-dwell shortcut for the video
        // attention minigame (look at a FloatingText target long enough to
        // fire its onHit callback, same as a mouse click).
        private bool _videoGazeClickEnabled = true;
        public bool VideoGazeClickEnabled
        {
            get => _videoGazeClickEnabled;
            set { _videoGazeClickEnabled = value; OnPropertyChanged(); }
        }

        // One-shot migration flag. Pre-3.4 builds had FlashClickable as a
        // master switch for both mouse and gaze interaction. Phase 3
        // decoupled them — gaze-pop and stare-linger have their own toggles,
        // both default ON. To preserve the intent of existing users who
        // had FlashClickable=false (hands-free / accessibility / deep-trance
        // configs), App.OnStartup runs RunFlashClickableDecouplingMigration
        // once: if FlashClickable was off, the new gaze toggles are also
        // turned off. Flag prevents re-migration after the user later
        // configures the new toggles independently.
        private bool _migratedFlashClickableDecoupling = false;
        public bool MigratedFlashClickableDecoupling
        {
            get => _migratedFlashClickableDecoupling;
            set { _migratedFlashClickableDecoupling = value; OnPropertyChanged(); }
        }


        // ---- Phase 4: Attention-Check headline mechanic --------------------

        public enum AttentionCheckFailModeKind { LockCard, XpPenalty, None }
        public enum AttentionCheckScopeKind { Always, DuringSessionsOnly }

        // Scrapped pre-ship per design call — feature stays in the codebase
        // but is disabled by default and has no UI surface in this release.
        // To revive: flip default to true, re-add the Lab toggle, re-add the
        // App.OnStartup wiring (see git history for the integration points).
        private bool _attentionCheckEnabled = false;
        public bool AttentionCheckEnabled
        {
            get => _attentionCheckEnabled;
            set { _attentionCheckEnabled = value; OnPropertyChanged(); }
        }

        private int _attentionCheckMinPerSession = 1;
        public int AttentionCheckMinPerSession
        {
            get => _attentionCheckMinPerSession;
            set { _attentionCheckMinPerSession = Math.Clamp(value, 0, 20); OnPropertyChanged(); }
        }

        private int _attentionCheckMaxPerSession = 5;
        public int AttentionCheckMaxPerSession
        {
            get => _attentionCheckMaxPerSession;
            set { _attentionCheckMaxPerSession = Math.Clamp(value, 1, 30); OnPropertyChanged(); }
        }

        private int _attentionCheckGraceMs = 4000;
        public int AttentionCheckGraceMs
        {
            get => _attentionCheckGraceMs;
            set { _attentionCheckGraceMs = Math.Clamp(value, 1000, 15000); OnPropertyChanged(); }
        }

        private AttentionCheckFailModeKind _attentionCheckFailMode = AttentionCheckFailModeKind.XpPenalty;
        public AttentionCheckFailModeKind AttentionCheckFailMode
        {
            get => _attentionCheckFailMode;
            set { _attentionCheckFailMode = value; OnPropertyChanged(); }
        }

        // Pass reward and miss penalty are fixed by design — not user-tunable.
        // See AttentionCheckService.PassXp / FailXpPenalty for the values.
        // (Pre-ship the values had sliders here; removed so the mechanic
        // can't be tuned into a grind lever.)

        private AttentionCheckScopeKind _attentionCheckScope = AttentionCheckScopeKind.Always;
        public AttentionCheckScopeKind AttentionCheckScope
        {
            get => _attentionCheckScope;
            set { _attentionCheckScope = value; OnPropertyChanged(); }
        }

        // Per-key sticky-notification dismissal memory. Toasts that call
        // ShowSticky(key, ...) record the key here when dismissed so they
        // don't re-appear next launch.
        private List<string> _dismissedNotificationKeys = new();
        [JsonProperty]
        public List<string> DismissedNotificationKeys
        {
            get => _dismissedNotificationKeys;
            set { _dismissedNotificationKeys = value ?? new List<string>(); OnPropertyChanged(); }
        }

        // Catalogue submissions the user has made, keyed by the canonical
        // .ccpenh.json path. Drives the Deeper library status badge + the
        // one-time "published to the catalogue" notification. See
        // DeeperSubmissionRecord.
        private Dictionary<string, DeeperSubmissionRecord> _deeperSubmissions = new();
        [JsonProperty]
        public Dictionary<string, DeeperSubmissionRecord> DeeperSubmissions
        {
            get => _deeperSubmissions;
            set { _deeperSubmissions = value ?? new Dictionary<string, DeeperSubmissionRecord>(); OnPropertyChanged(); }
        }

        // Session catalogue submissions, keyed by the canonical .session.json file
        // path (custom sessions are file-backed). Drives the share status badge +
        // accepted notification. See DeeperSubmissionRecord / MainWindow.CatalogueSubmissions.
        private Dictionary<string, DeeperSubmissionRecord> _catalogueSessionSubmissions = new();
        [JsonProperty]
        public Dictionary<string, DeeperSubmissionRecord> CatalogueSessionSubmissions
        {
            get => _catalogueSessionSubmissions;
            set { _catalogueSessionSubmissions = value ?? new Dictionary<string, DeeperSubmissionRecord>(); OnPropertyChanged(); }
        }

        // Preset catalogue submissions, keyed by the in-memory preset Id (presets
        // live in UserPresets, not on disk). Drives the share status badge +
        // accepted notification.
        private Dictionary<string, DeeperSubmissionRecord> _cataloguePresetSubmissions = new();
        [JsonProperty]
        public Dictionary<string, DeeperSubmissionRecord> CataloguePresetSubmissions
        {
            get => _cataloguePresetSubmissions;
            set { _cataloguePresetSubmissions = value ?? new Dictionary<string, DeeperSubmissionRecord>(); OnPropertyChanged(); }
        }

        // Mod catalogue submissions, keyed by the mod id (installed mods live in
        // %UserData%/mods/{id}). The catalogue stores metadata + an external
        // download link only — the .ccpmod binary is hosted by the creator (MEGA).
        private Dictionary<string, DeeperSubmissionRecord> _catalogueModSubmissions = new();
        [JsonProperty]
        public Dictionary<string, DeeperSubmissionRecord> CatalogueModSubmissions
        {
            get => _catalogueModSubmissions;
            set { _catalogueModSubmissions = value ?? new Dictionary<string, DeeperSubmissionRecord>(); OnPropertyChanged(); }
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

        // When enabled, blinking fast 6 times in a row (within ~3.5s) stops all
        // active conditioning (engine, session, videos, audio) — leaving the
        // webcam capture running — and prompts the user to recalibrate. Toggled
        // via the checkbox shown on every webcam card.
        private bool _blinkRecalibrateShortcutEnabled = true;
        public bool BlinkRecalibrateShortcutEnabled
        {
            get => _blinkRecalibrateShortcutEnabled;
            set { _blinkRecalibrateShortcutEnabled = value; OnPropertyChanged(); }
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

        private bool _allowDiscordDm = false;
        /// <summary>
        /// Allow other users to send Discord DMs via the leaderboard.
        /// When enabled, your Discord ID is shown on the leaderboard for direct messaging.
        /// </summary>
        public bool AllowDiscordDm
        {
            get => _allowDiscordDm;
            set { _allowDiscordDm = value; OnPropertyChanged(); }
        }

        private bool _shareProfilePicture = false;
        /// <summary>
        /// Share your Discord profile picture on the leaderboard and profile viewer.
        /// When enabled, other users can see your avatar when viewing your profile.
        /// </summary>
        public bool ShareProfilePicture
        {
            get => _shareProfilePicture;
            set { _shareProfilePicture = value; OnPropertyChanged(); }
        }

        private ProfileCosmetics _profileCosmetics = new();
        /// <summary>
        /// What this subject has equipped on their Trainer Card: banner, accent, worn title,
        /// pinned achievements (and, from Phase 3, avatar decoration + charms).
        ///
        /// Stored locally AND synced (<c>cosmetics</c> in the /user/sync payload) so the same look
        /// follows the account to a new machine and renders on other people's screens. Always run
        /// it through <see cref="Services.CosmeticsCatalog.SanitizeOwn"/> before sending or
        /// rendering - the settings file is user-editable and the ids in it may be from a build
        /// whose art this one does not ship.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ProfileCosmetics ProfileCosmetics
        {
            get => _profileCosmetics;
            set { _profileCosmetics = value ?? new ProfileCosmetics(); OnPropertyChanged(); }
        }

        private bool _showOnlineStatus = true;
        /// <summary>
        /// Show your online status on the leaderboard and profile viewer.
        /// When disabled, you appear offline to other users (invisible mode).
        /// </summary>
        public bool ShowOnlineStatus
        {
            get => _showOnlineStatus;
            set { _showOnlineStatus = value; OnPropertyChanged(); }
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

        private string _offlineUsername = "";
        /// <summary>
        /// Username used when in offline mode. This name is stored locally only
        /// and is never synced to the cloud or leaderboard.
        /// </summary>
        [JsonProperty("offline_username")]
        public string OfflineUsername
        {
            get => _offlineUsername;
            set { _offlineUsername = value ?? ""; OnPropertyChanged(); }
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
        private string _schedulerStartTime = "00:00";
        public string SchedulerStartTime
        {
            get => _schedulerStartTime;
            set { _schedulerStartTime = value ?? "00:00"; OnPropertyChanged(); }
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

        // Easing curve applied to the ramp's progress (suggestion #660). Stored by
        // ordinal like the other enum settings here; missing = Linear = unchanged
        // legacy behaviour. Applied to both ramp systems — see Helpers/RampCurves.cs.
        private RampCurve _rampCurve = RampCurve.Linear;
        public RampCurve RampCurve
        {
            get => _rampCurve;
            set { _rampCurve = value; OnPropertyChanged(); }
        }

        #endregion

        #region Spiral Overlay (Unlocks Lv.10)

        private bool _spiralEnabled = true;
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

        private bool _spiralRandomize = false;
        /// <summary>
        /// When enabled, each spiral overlay/session picks a random spiral from the pool
        /// (the folder of SpiralPath if set, else assets/spirals) at start. Falls back to
        /// the single spiral when the pool has fewer than two entries.
        /// </summary>
        public bool SpiralRandomize
        {
            get => _spiralRandomize;
            set { _spiralRandomize = value; OnPropertyChanged(); }
        }

        private int _spiralOpacity = 10; // 0-100%
        public int SpiralOpacity
        {
            get => _spiralOpacity;
            set { _spiralOpacity = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private bool _spiralLinkRamp = false;
        public bool SpiralLinkRamp
        {
            get => _spiralLinkRamp;
            set { _spiralLinkRamp = value; OnPropertyChanged(); }
        }

        // Standalone corner-GIF overlays (Spiral card -> "Corner GIFs" window). Independent of
        // sessions; driven app-wide by CornerGifService. Up to two slots (two screen corners).
        private List<CornerGifOverlaySetting> _cornerGifOverlays = new();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<CornerGifOverlaySetting> CornerGifOverlays
        {
            get => _cornerGifOverlays;
            set { _cornerGifOverlays = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region Audio Layers (suggestion #659) + Audio-Only sessions (#668)

        // User-maintained list of looping audio tracks mixed together through ONE output device
        // by Services.Audio.LayeredAudioService. Independent of any single feature.
        private List<AudioLayerTrack> _audioLayers = new();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<AudioLayerTrack> AudioLayers
        {
            get => _audioLayers;
            set { _audioLayers = value ?? new(); OnPropertyChanged(); }
        }

        // Master on/off for the layered audio player (also auto-started for audio-only sessions).
        private bool _audioLayersEnabled = false;
        public bool AudioLayersEnabled
        {
            get => _audioLayersEnabled;
            set { _audioLayersEnabled = value; OnPropertyChanged(); }
        }

        // Overall volume for the layered mix (0-100), multiplied with the app master volume.
        private int _audioLayersMasterVolume = 70;
        public int AudioLayersMasterVolume
        {
            get => _audioLayersMasterVolume;
            set { _audioLayersMasterVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        // #668 Audio-Only Hypno: when a session starts with this on, visual features
        // (flash/spiral/video/etc.) are suppressed and the layered audio player runs instead.
        private bool _audioOnlySession = false;
        public bool AudioOnlySession
        {
            get => _audioOnlySession;
            set { _audioOnlySession = value; OnPropertyChanged(); }
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
            set { _bubblesFrequency = Math.Clamp(value, 1, 60); OnPropertyChanged(); }
        }
        private bool _bubbleSharedHost = true;
        /// <summary>Render the ambient dashboard bubbles as visuals on ONE shared click-through host
        /// window (Canvas-positioned, pops via the global mouse hook) instead of one top-level layered
        /// Window per bubble — the same hyper-optimized path the chaos field uses (see
        /// <see cref="ChaosBubbleSharedHost"/>). The per-window path repositions every bubble via
        /// SetWindowPos each frame, which saturates the UI thread and makes clicks register late under a
        /// dense field (raised spawn rate / higher concurrent cap). Default ON since v6.2.5 (the chaos
        /// field proved the renderer); the "Solid mode" toggle remains as the opt-out back to the
        /// per-window path for setups where the global mouse hook or click-through host misbehave.</summary>
        public bool BubbleSharedHost
        {
            get => _bubbleSharedHost;
            set { _bubbleSharedHost = value; OnPropertyChanged(); }
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

        // ---- Trigger Bubbles (ambient bubbles that fire a Chaos effect on pop) ----
        private bool _bubbleTriggersEnabled = false;
        public bool BubbleTriggersEnabled
        {
            get => _bubbleTriggersEnabled;
            set { _bubbleTriggersEnabled = value; OnPropertyChanged(); }
        }
        private int _bubbleTriggerChance = 10;   // percent of spawns that carry an effect
        public int BubbleTriggerChance
        {
            get => _bubbleTriggerChance;
            set { _bubbleTriggerChance = Math.Clamp(value, 0, 50); OnPropertyChanged(); }
        }
        private int _bubbleSpeedBoost = 0;   // 0..500 % extra travel speed for on-screen bubbles
        public int BubbleSpeedBoost
        {
            get => _bubbleSpeedBoost;
            set { _bubbleSpeedBoost = Math.Clamp(value, 0, 500); OnPropertyChanged(); }
        }
        // Which effect types are in the pool (equal odds among the picked ids).
        // Ids map to ChaosBubbleVariants ("htlink" == Cascade/Gif Rain); "glitch" is the
        // full-screen GIF wash faced with glitch.png — built dashboard-side, not a chaos variant.
        private List<string> _bubbleTriggerVariants = new()
            { "flash", "subliminal", "pink", "spiral", "glitch", "htlink", "video" };
        public List<string> BubbleTriggerVariants
        {
            get => _bubbleTriggerVariants;
            set { _bubbleTriggerVariants = value ?? new List<string>(); OnPropertyChanged(); }
        }
        // Easter egg: when an effect bubble lingers >4s, a 10% roll sends the companion to glide over,
        // narrate the effect, and pop it for you (50% louder). Gated under BubbleTriggersEnabled.
        private bool _bubbleAvatarEggEnabled = true;
        public bool BubbleAvatarEggEnabled
        {
            get => _bubbleAvatarEggEnabled;
            set { _bubbleAvatarEggEnabled = value; OnPropertyChanged(); }
        }

        // ---- Chaos Mode (effect-bubbles roguelite, Lab) ----
        private bool _chaosModeEnabled = true;
        public bool ChaosModeEnabled
        {
            get => _chaosModeEnabled;
            set { _chaosModeEnabled = value; OnPropertyChanged(); }
        }
        private string _chaosDifficulty = "Easy";
        public string ChaosDifficulty
        {
            get => _chaosDifficulty;
            set { _chaosDifficulty = value; OnPropertyChanged(); }
        }
        private int _chaosRunDurationSec = 180;
        public int ChaosRunDurationSec
        {
            get => _chaosRunDurationSec;
            // Ceiling raised 60..900 -> 60..7200 (2026-07-17): the old 900 cap silently clamped
            // the 16/20-min portal chips down to 15 min, and The Hourglass unlock needs up to 2h.
            // Ownership gating for >20 min lives at the use sites (FromSettings / PersistRunSetup).
            set { _chaosRunDurationSec = Math.Clamp(value, 60, 7200); OnPropertyChanged(); }
        }
        // The Bottomless Fall unlock: last-chosen endless toggle (per-run, gated on owning
        // endless_mode at read time). Persisted so the portal remembers the choice.
        private bool _chaosEndless = false;
        public bool ChaosEndless
        {
            get => _chaosEndless;
            set { _chaosEndless = value; OnPropertyChanged(); }
        }
        // (ChaosLiveBubbleShare removed — the knob was inert; live/benign split is set by variant weights.)
        // Motion: "Mixed" (per-variant defaults), "FloatUp", "RainDown", "RoamBounce".
        private string _chaosMotionMode = "Mixed";
        public string ChaosMotionMode
        {
            get => _chaosMotionMode;
            set { _chaosMotionMode = value; OnPropertyChanged(); }
        }
        // (ChaosStartingShields removed 2026-06-12: orphan since the 2026-06-10 resistance
        //  redesign — base is 0, only the start_resistance charm grants any. Its stale
        //  default of 3 was one accidental UI binding away from undoing that redesign.)
        private int _chaosWaveCount = 5;
        public int ChaosWaveCount
        {
            get => _chaosWaveCount;
            set { _chaosWaveCount = Math.Clamp(value, 1, 12); OnPropertyChanged(); }
        }
        /// <summary>Enabled bubble-variant ids. Null = all variants enabled.</summary>
        private System.Collections.Generic.List<string>? _chaosEnabledVariants = null;
        public System.Collections.Generic.List<string>? ChaosEnabledVariants
        {
            get => _chaosEnabledVariants;
            set { _chaosEnabledVariants = value; OnPropertyChanged(); }
        }
        private bool _chaosScreenShakeEnabled = true;
        public bool ChaosScreenShakeEnabled
        {
            get => _chaosScreenShakeEnabled;
            set { _chaosScreenShakeEnabled = value; OnPropertyChanged(); }
        }
        private bool _chaosHudOnRight;
        /// <summary>Park the Rabbit Hole HUD sidebar on the RIGHT edge of the screen instead of the left.</summary>
        public bool ChaosHudOnRight
        {
            get => _chaosHudOnRight;
            set { _chaosHudOnRight = value; OnPropertyChanged(); }
        }
        private bool _chaosColorFlashesEnabled = true;
        public bool ChaosColorFlashesEnabled
        {
            get => _chaosColorFlashesEnabled;
            set { _chaosColorFlashesEnabled = value; OnPropertyChanged(); }
        }
        private bool _chaosSkiaFxEnabled = true;
        /// <summary>A/B flag for the Skia GPU-style FX prototype (ChaosSkiaFxOverlay): when on, the
        /// rabbit trail + Rabbit-Caller cursor glow render as an additive bloomed particle field
        /// instead of the legacy WPF ellipse pool. Off falls back to the old overlays.</summary>
        public bool ChaosSkiaFxEnabled
        {
            get => _chaosSkiaFxEnabled;
            set { _chaosSkiaFxEnabled = value; OnPropertyChanged(); }
        }
        private bool _chaosMenuMusicMuted;
        /// <summary>Persisted mute toggle for the Rabbit Hole main-menu soundtrack (menu_theme.mp3).</summary>
        public bool ChaosMenuMusicMuted
        {
            get => _chaosMenuMusicMuted;
            set { _chaosMenuMusicMuted = value; OnPropertyChanged(); }
        }
        private bool _chaosBubbleSharedHost = true;
        /// <summary>Default ON (proven win): render all chaos bubbles as visuals on ONE shared
        /// click-through host window (Canvas-positioned) instead of one top-level layered Window per
        /// bubble. The per-bubble-window model repositions every bubble via SetWindowPos each frame,
        /// which saturates the UI thread and makes clicks register late under a dense field. With the
        /// host on, pops are detected via the global mouse hook (swallow on hit) instead of WPF events,
        /// so they're immune to that starvation. Falls back to the proven per-window path when off.</summary>
        public bool ChaosBubbleSharedHost
        {
            get => _chaosBubbleSharedHost;
            set { _chaosBubbleSharedHost = value; OnPropertyChanged(); }
        }
        private bool _unifiedOverlayHost = true;
        /// <summary>Default ON (re-enabled for the 6.4 merge): render the fullscreen
        /// effects (pink filter, spiral, brain drain, subliminals, flash, bubbles, chaos FX) as
        /// z-ordered Skia layers inside ONE shared click-through compositor window per monitor
        /// (Services/Compositor/CompositorEngine) instead of one layered Window per effect.
        /// Concurrent fullscreen layered windows were the root cause of the session-lag /
        /// mouse-stutter cluster; this is the WPF twin of the Avalonia port's compositor and the
        /// end-state renderer. Was reverted to OFF once (2026-07-13, #550: unthrottled software
        /// SKElement raster saturated the UI thread) — since fixed by dirty-gated invalidation,
        /// so the compositor is the blessed path going forward. A Settings-tab toggle
        /// ("Unified overlay renderer") lets users fall back to the legacy per-effect windows.</summary>
        public bool UnifiedOverlayHost
        {
            get => _unifiedOverlayHost;
            set { _unifiedOverlayHost = value; OnPropertyChanged(); }
        }
        private bool _compositorOffThreadPresent = true;
        /// <summary>Default ON (#550 proper fix, promoted 6.4.1 after 6.4.0 shipped the compositor ON
        /// but this OFF — bugs #588/#586/#587: fullscreen spiral rastered on the UI thread and starved
        /// the dispatcher on high-res / multi-monitor machines, exactly the repro the flag was built for).
        /// When the unified overlay host is on, render each monitor's layers OFF the UI thread. The
        /// UI-thread tick still runs Update() and records the active layers into a cheap immutable
        /// SKPicture (draw-command capture, no raster); a dedicated per-monitor present thread then
        /// rasterizes that picture into a DIB-backed surface and pushes it with UpdateLayeredWindow(ULW_ALPHA).
        /// This removes the fullscreen software raster + layered composite from the UI thread while keeping
        /// per-pixel alpha, click-through and the layers' UI-thread contract intact (SKImage frees route
        /// through the engine's deferred-disposal so an image referenced by an in-flight picture is never
        /// freed under the present thread). No-op when the unified host is off. Falls back to the UI-thread
        /// SKElement host when off; there is no dedicated UI toggle — the user-facing escape hatch is the
        /// Settings > System "Unified overlay renderer" switch, which drops to the legacy per-effect windows
        /// entirely (that path never had the UI-thread spiral raster either).</summary>
        public bool CompositorOffThreadPresent
        {
            get => _compositorOffThreadPresent;
            set { _compositorOffThreadPresent = value; OnPropertyChanged(); }
        }
        private bool _chaosDvdSharedHost = true;
        /// <summary>Default ON (proven win): render the DVD bouncing-text logos (Porn DVD /
        /// Intrusive Thoughts / Casting Couch) as cheap Canvas children of ONE shared click-through host
        /// window instead of one top-level layered Window per logo. The per-logo-window model repositions
        /// every logo via SetWindowPos each frame; on a split (up to ~16 logos at once) that storm
        /// saturates the UI thread and freezes the companion avatar. With the host on, logos move via
        /// Canvas.SetLeft/Top (batched in one render pass). Spanker-clickable logos keep the per-window
        /// path so the smack still hit-tests. Falls back to the proven per-window path when off.</summary>
        public bool ChaosDvdSharedHost
        {
            get => _chaosDvdSharedHost;
            set { _chaosDvdSharedHost = value; OnPropertyChanged(); }
        }
        private bool _avatarOwnThread;
        /// <summary>EXPERIMENTAL A/B (default OFF): run the AI companion (AvatarTubeWindow) on its OWN
        /// dedicated UI thread + Dispatcher instead of sharing the main thread. Its float/breathing/
        /// typewriter/pose timers then can't be queued behind chaos's UI work, so the companion keeps
        /// animating + typing while a chaos run is busy (the "avatar stutters during chaos" symptom).
        /// Caveat: WPF's render thread is still process-wide, so it's smoother, not perfectly immune.
        /// Falls back to the proven same-thread path when off. Needs an attached-mode play-test.</summary>
        public bool AvatarOwnThread
        {
            get => _avatarOwnThread;
            set { _avatarOwnThread = value; OnPropertyChanged(); }
        }
        private bool _chaosMemTelemetry = true;
        /// <summary>Diagnostic: write a [CHAOSMEM] working-set / native-memory sample to the app log
        /// every ~15s during a run (plus run-start/run-end). Pairs with the dirty-shutdown sentinel to
        /// catch the random mid-play native crash on tester machines — the log tail shows whether native
        /// memory climbed run-over-run (OOM) or stayed flat (an access violation, e.g. the Skia layer).
        /// Default on while we hunt the crash; cheap (one line / 15s). Turn off once it's diagnosed.</summary>
        public bool ChaosMemTelemetry
        {
            get => _chaosMemTelemetry;
            set { _chaosMemTelemetry = value; OnPropertyChanged(); }
        }
        private bool _chaosPinOnTop = true;
        /// <summary>Pin the whole Rabbit Hole layer (HUD/sidebar, bubbles, overlays) topmost so it
        /// stays above other apps and never sinks when you click another window. Off restores the
        /// old Free Desktop behavior where the run yields to whatever you bring forward.</summary>
        public bool ChaosPinOnTop
        {
            get => _chaosPinOnTop;
            set { _chaosPinOnTop = value; OnPropertyChanged(); }
        }
        private double _chaosShakeIntensity = 0.8;
        public double ChaosShakeIntensity
        {
            get => _chaosShakeIntensity;
            set { _chaosShakeIntensity = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
        }
        private double _chaosEffectIntensity = 0.85;
        public double ChaosEffectIntensity
        {
            get => _chaosEffectIntensity;
            set { _chaosEffectIntensity = Math.Clamp(value, 0.2, 1.5); OnPropertyChanged(); }
        }
        private bool _chaosBoonDraftEnabled = true;
        public bool ChaosBoonDraftEnabled
        {
            get => _chaosBoonDraftEnabled;
            set { _chaosBoonDraftEnabled = value; OnPropertyChanged(); }
        }
        private bool _chaosAllowCurses = true;
        public bool ChaosAllowCurses
        {
            get => _chaosAllowCurses;
            set { _chaosAllowCurses = value; OnPropertyChanged(); }
        }
        private bool _chaosDartersEnabled = true;
        public bool ChaosDartersEnabled
        {
            get => _chaosDartersEnabled;
            set { _chaosDartersEnabled = value; OnPropertyChanged(); }
        }
        private bool _chaosAnnouncerEnabled = true;
        /// <summary>Show the on-screen subtitle announcer (mantra/temptation/willpower/depth/streak) during a Chaos run.</summary>
        public bool ChaosAnnouncerEnabled
        {
            get => _chaosAnnouncerEnabled;
            set { _chaosAnnouncerEnabled = value; OnPropertyChanged(); }
        }

        // ---- Narrative layer (the Madam) + per-zone backdrops ----
        private bool _narrativeModeEnabled = true;
        /// <summary>Master switch for the reactive narrator (voiced + text lines) during a Chaos run.</summary>
        public bool NarrativeModeEnabled
        {
            get => _narrativeModeEnabled;
            set { _narrativeModeEnabled = value; OnPropertyChanged(); }
        }

        private bool _backdropEnabled = true;
        /// <summary>Show per-zone backdrop plates under the chaos bubbles. When OFF, no backdrop window
        /// spawns and classic Chaos keeps its desktop click-through behavior exactly.</summary>
        public bool BackdropEnabled
        {
            get => _backdropEnabled;
            set { _backdropEnabled = value; OnPropertyChanged(); }
        }
        private double _backdropOpacity = 0.55;
        /// <summary>Backdrop window opacity (0 = invisible, 1 = fully covers desktop). Default 0.55 lets the desktop bleed through.</summary>
        public double BackdropOpacity
        {
            get => _backdropOpacity;
            set { _backdropOpacity = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
        }

        private bool _chaosTunnelEnabled = false;
        /// <summary>Endless 3D "rabbit hole" WebGL tunnel rendered behind the Chaos game (a non-topmost
        /// WebView2 window under every bubble/FX/video/HUD layer). Default OFF — it stacks GPU load on the
        /// already-heavy game, so it's opt-in from the Chaos hub.</summary>
        public bool ChaosTunnelEnabled
        {
            get => _chaosTunnelEnabled;
            set { _chaosTunnelEnabled = value; OnPropertyChanged(); }
        }

        private bool _chaosWebGameEnabled = true;
        /// <summary>DtRH browser game: the whole Rabbit Hole runs as a three.js game inside a
        /// fullscreen WebView2 window built on The Fall engine, instead of the WPF windows layer.
        /// Default ON since M6 (rollout flip); the classic WPF path stays intact behind the Lab
        /// toggle for machines where WebGL misbehaves - a boot-error also auto-falls back for
        /// the session. The legacy code retires one release after the flip.</summary>
        public bool ChaosWebGameEnabled
        {
            get => _chaosWebGameEnabled;
            set { _chaosWebGameEnabled = value; OnPropertyChanged(); }
        }

        private int _chaosActiveSlot = 1;
        /// <summary>Which of the 3 local save slots the Rabbit Hole is currently playing on
        /// (1-3). Chosen in the slot picker shown before the hole opens; persisted so Quick
        /// Start and the next session reuse the last pick. Backs
        /// <see cref="Services.Chaos.ChaosMeta.ActiveSlot"/> — each slot has its own
        /// chaos_meta.slotN.json.</summary>
        public int ChaosActiveSlot
        {
            get => _chaosActiveSlot;
            set { _chaosActiveSlot = value < 1 || value > 3 ? 1 : value; OnPropertyChanged(); }
        }

        private string _chaosAccessoryKey1 = "Q";
        /// <summary>Keybind for accessory pocket 1 (reserved: active-use accessories are a future system).</summary>
        public string ChaosAccessoryKey1
        {
            get => _chaosAccessoryKey1;
            set { _chaosAccessoryKey1 = value; OnPropertyChanged(); }
        }

        private string _chaosAccessoryKey2 = "E";
        /// <summary>Keybind for accessory pocket 2 (reserved: active-use accessories are a future system).</summary>
        public string ChaosAccessoryKey2
        {
            get => _chaosAccessoryKey2;
            set { _chaosAccessoryKey2 = value; OnPropertyChanged(); }
        }
        #endregion

        #region For You Feed (premium, WebView2)
        private string _fypLayout = "duo";
        /// <summary>Feed page layout: "duo" (landscape stacks two-up), "trio" (three-up) or
        /// "random" (irregular mosaic quilt). Mirrors the mobile reel's setting.</summary>
        public string FypLayout
        {
            get => _fypLayout;
            set { _fypLayout = value is "duo" or "trio" or "random" ? value : "duo"; OnPropertyChanged(); }
        }

        private bool _fypIncludeGifs = true;
        /// <summary>Mix animated GIFs from the images library into the feed.</summary>
        public bool FypIncludeGifs
        {
            get => _fypIncludeGifs;
            set { _fypIncludeGifs = value; OnPropertyChanged(); }
        }

        private bool _fypMosaicAutoChange = true;
        /// <summary>Mosaic layout re-composes itself on a timer (off = holds until swiped).</summary>
        public bool FypMosaicAutoChange
        {
            get => _fypMosaicAutoChange;
            set { _fypMosaicAutoChange = value; OnPropertyChanged(); }
        }

        private int _fypMosaicChangeSec = 10;
        /// <summary>Seconds between mosaic re-compositions. Floored at 3 - every morph
        /// mounts/releases up to 4 media elements, so a faster cadence churns decoders.</summary>
        public int FypMosaicChangeSec
        {
            get => _fypMosaicChangeSec;
            set { _fypMosaicChangeSec = Math.Clamp(value, 3, 60); OnPropertyChanged(); }
        }

        private bool _fypAutoAdvance = false;
        /// <summary>Scroll to the next page when a clip's window ends (off = loop forever).</summary>
        public bool FypAutoAdvance
        {
            get => _fypAutoAdvance;
            set { _fypAutoAdvance = value; OnPropertyChanged(); }
        }

        private bool _fypMuted = false;
        /// <summary>Feed audio muted.</summary>
        public bool FypMuted
        {
            get => _fypMuted;
            set { _fypMuted = value; OnPropertyChanged(); }
        }

        private double _fypWindowOpacity = 1.0;
        /// <summary>Ghost-mode translucency for the feed (0.01-1.0) - the DWM thumbnail opacity of
        /// the see-through mirror, never the real window's alpha (the WebView2 window must never be
        /// layered; see FypGhostOverlay). May go near-invisible: recovery is a single Esc/panic
        /// press, which restores the fully opaque real window regardless of this value.</summary>
        public double FypWindowOpacity
        {
            get => _fypWindowOpacity;
            set { _fypWindowOpacity = Math.Clamp(value, 0.01, 1.0); OnPropertyChanged(); }
        }

        private bool _fypAudioGlow = true;
        /// <summary>Page-side visual: the playing tile pulses with its own audio level. Persisted
        /// here and handed to the page in the init payload; the app itself does nothing with it.</summary>
        public bool FypAudioGlow
        {
            get => _fypAudioGlow;
            set { _fypAudioGlow = value; OnPropertyChanged(); }
        }

        private bool _fypEyeControl = false;
        /// <summary>Webcam eye control for the feed: a blink swaps one tile, holding the eyes
        /// shut for 2s changes the whole page. Off by default - it turns the camera on.</summary>
        public bool FypEyeControl
        {
            get => _fypEyeControl;
            set { _fypEyeControl = value; OnPropertyChanged(); }
        }

        private bool _fypEyeGaze = false;
        /// <summary>With eye control on, a blink swaps the tile the user is LOOKING at rather than
        /// a random one. Only meaningful once gaze is calibrated; ignored otherwise.</summary>
        public bool FypEyeGaze
        {
            get => _fypEyeGaze;
            set { _fypEyeGaze = value; OnPropertyChanged(); }
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

        private bool _lockCardVoiceMode = false; // Solve by speaking the phrase (offline mic) instead of typing
        /// <summary>
        /// When true, lock cards are solved by saying the phrase out loud (offline Vosk mic) rather
        /// than typing it. Falls back to typing automatically if speech isn't available or mic
        /// consent wasn't given, so the user is never trapped.
        /// </summary>
        public bool LockCardVoiceMode
        {
            get => _lockCardVoiceMode;
            set { _lockCardVoiceMode = value; OnPropertyChanged(); }
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

        #region Latest Quiz Result (for companion integration)

        private string _latestQuizArchetype = "";
        public string LatestQuizArchetype
        {
            get => _latestQuizArchetype;
            set { _latestQuizArchetype = value ?? ""; OnPropertyChanged(); }
        }

        private int _latestQuizScorePercentage = -1; // -1 = no quiz taken
        public int LatestQuizScorePercentage
        {
            get => _latestQuizScorePercentage;
            set { _latestQuizScorePercentage = value; OnPropertyChanged(); }
        }

        private string _latestQuizCategoryId = "";
        public string LatestQuizCategoryId
        {
            get => _latestQuizCategoryId;
            set { _latestQuizCategoryId = value ?? ""; OnPropertyChanged(); }
        }

        private string _latestQuizProfileText = "";
        public string LatestQuizProfileText
        {
            get => _latestQuizProfileText;
            set
            {
                // Truncate to 200 chars
                var truncated = value ?? "";
                if (truncated.Length > 200) truncated = truncated.Substring(0, 200);
                _latestQuizProfileText = truncated;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Graded Intake (web core window mode)

        private bool _intakeFullscreen = false;
        /// <summary>Launch the Graded Intake window borderless-fullscreen. The SINGLE source of
        /// truth for the mode: the page never stores it (a localStorage copy would disagree with
        /// the window the host had already built), it only mirrors what C# echoes back. Written
        /// by IntakeHostService whenever the page's toggle moves, so "how I left it" is how it
        /// comes back. Defaults off - a Lab tool opening windowed is the recoverable state.</summary>
        public bool IntakeFullscreen
        {
            get => _intakeFullscreen;
            set { _intakeFullscreen = value; OnPropertyChanged(); }
        }

        private bool _goonFullscreen = false;
        /// <summary>Launch the Goon Game (1v1 duel) web client borderless-fullscreen. Same contract
        /// as <see cref="IntakeFullscreen"/>: C# owns the window mode, the page only mirrors the
        /// state the host echoes back, and GoonHostService writes this whenever the page's toggle
        /// moves. Defaults off — and a recovery relaunch deliberately ignores it, so a wedged page
        /// always comes back in a titled window that Windows can still close.</summary>
        [JsonProperty]
        public bool GoonFullscreen
        {
            get => _goonFullscreen;
            set { _goonFullscreen = value; OnPropertyChanged(); }
        }

        // ---- Weekly Intake Pass (free-tier onboarding) ----------------------------
        // The Graded Intake is a premium Exclusive, but free users get ONE run a week so
        // the app has a front door: the intake drafts a session, and that session is the
        // first real thing a new user experiences. Premium is unchanged - unlimited runs,
        // none of this state is ever read for a patron.

        private string _intakePassSpentWeek = "";
        /// <summary>ISO week key ("2026-W31") of the week whose free pass has been SPENT.
        /// This - not a timestamp comparison - is the authority on whether the door is open:
        /// weeks are the unit the feature is sold in, so storing the week directly means a
        /// clock that drifts by hours can never half-open a pass. Empty = never spent.
        /// Written only on a COMPLETED intake (a quiz-result arrived), never on launch, so a
        /// crash or an abort cannot burn someone's week.</summary>
        public string IntakePassSpentWeek
        {
            get => _intakePassSpentWeek;
            set { _intakePassSpentWeek = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>UTC instant the pass above was spent. Not used for gating (the week key
        /// is), purely so a rolled-back clock is detectable: a spend stamped in the future
        /// means the machine's clock moved, and the pass service refuses to re-open on that
        /// evidence alone. Null = never spent.</summary>
        private DateTime? _intakePassSpentUtc = null;
        public DateTime? IntakePassSpentUtc
        {
            get => _intakePassSpentUtc;
            set { _intakePassSpentUtc = value; OnPropertyChanged(); }
        }

        // IntakePassCeremonyWeek was removed when the Dashboard tile stopped being a once-a-week
        // reveal and became a plate that alternates for as long as a pass is waiting. Existing
        // settings.json files may still carry the key; Newtonsoft ignores unknown properties on
        // load, so it simply falls away the next time settings are saved.

        /// <summary>ISO week the weekly nudge popup was dismissed for. Deliberately NOT the
        /// shared <see cref="DismissedAnnouncementId"/>: that slot belongs to server-triggered
        /// announcements, and a recurring local nudge writing into it would silently eat the
        /// next real announcement.</summary>
        private string _intakeNudgeDismissedWeek = "";
        public string IntakeNudgeDismissedWeek
        {
            get => _intakeNudgeDismissedWeek;
            set { _intakeNudgeDismissedWeek = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>Show the once-a-week "your intake pass is ready" popup. On by default -
        /// it is the feature's re-engagement hook - but a weekly popup with no off switch is
        /// a bug report waiting to happen, so it has one.</summary>
        private bool _intakeNudgeEnabled = true;
        public bool IntakeNudgeEnabled
        {
            get => _intakeNudgeEnabled;
            set { _intakeNudgeEnabled = value; OnPropertyChanged(); }
        }

        #endregion

        #region Pop Quiz (Session reinforcement questions)

        private bool _popQuizEnabled = false;
        public bool PopQuizEnabled
        {
            get => _popQuizEnabled;
            set { _popQuizEnabled = value; OnPropertyChanged(); }
        }

        private int _popQuizFrequency = 2; // Per hour (1-10)
        public int PopQuizFrequency
        {
            get => _popQuizFrequency;
            set { _popQuizFrequency = Math.Clamp(value, 1, 100); OnPropertyChanged(); }
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

        private bool _bouncingTextAlwaysOnTop = false;
        public bool BouncingTextAlwaysOnTop
        {
            get => _bouncingTextAlwaysOnTop;
            set { _bouncingTextAlwaysOnTop = value; OnPropertyChanged(); }
        }

        private int _bouncingTextColorMode = 0; // 0=Random (classic), 1=Fixed, 2=Rainbow cycle
        public int BouncingTextColorMode
        {
            get => _bouncingTextColorMode;
            set { _bouncingTextColorMode = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
        }

        private string _bouncingTextFixedColor = ""; // "#RRGGBB"; empty = hot pink
        public string BouncingTextFixedColor
        {
            get => _bouncingTextFixedColor;
            set { _bouncingTextFixedColor = value ?? ""; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxBreathing = false;
        public bool BouncingTextFxBreathing
        {
            get => _bouncingTextFxBreathing;
            set { _bouncingTextFxBreathing = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxWobble = false;
        public bool BouncingTextFxWobble
        {
            get => _bouncingTextFxWobble;
            set { _bouncingTextFxWobble = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxSpin = false;
        public bool BouncingTextFxSpin
        {
            get => _bouncingTextFxSpin;
            set { _bouncingTextFxSpin = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxVelocityTilt = false;
        public bool BouncingTextFxVelocityTilt
        {
            get => _bouncingTextFxVelocityTilt;
            set { _bouncingTextFxVelocityTilt = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxSquashStretch = true;
        public bool BouncingTextFxSquashStretch
        {
            get => _bouncingTextFxSquashStretch;
            set { _bouncingTextFxSquashStretch = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextFxCornerBurst = true;
        public bool BouncingTextFxCornerBurst
        {
            get => _bouncingTextFxCornerBurst;
            set { _bouncingTextFxCornerBurst = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextOutline = false;
        public bool BouncingTextOutline
        {
            get => _bouncingTextOutline;
            set { _bouncingTextOutline = value; OnPropertyChanged(); }
        }

        private bool _bouncingTextSecondText = false;
        public bool BouncingTextSecondText
        {
            get => _bouncingTextSecondText;
            set { _bouncingTextSecondText = value; OnPropertyChanged(); }
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

        // User-picked tint color as "#RRGGBB". Empty = use the default (mod/hot-pink)
        // color, preserving creator-mod retints until the user explicitly overrides.
        private string _pinkFilterColor = "";
        public string PinkFilterColor
        {
            get => _pinkFilterColor;
            set { _pinkFilterColor = value ?? ""; OnPropertyChanged(); }
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

        // Custom mind-wipe audio clip. When set to an existing file, it overrides the
        // built-in Resources/sounds/mindwipe folder (a short ~2s clip works best).
        // Empty => fall back to the built-in audio folder.
        private string _mindWipeAudioPath = "";
        public string MindWipeAudioPath
        {
            get => _mindWipeAudioPath;
            set { _mindWipeAudioPath = value ?? ""; OnPropertyChanged(); }
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

        #region Performance

        private bool _performanceMode = false;
        /// <summary>
        /// Master manual switch. When true, forces the Performance rendering tier everywhere
        /// (most aggressive downscaling / effect reduction) regardless of load.
        /// </summary>
        public bool PerformanceMode
        {
            get => _performanceMode;
            set { _performanceMode = value; OnPropertyChanged(); }
        }

        private bool _autoPerformanceMode = true;
        /// <summary>
        /// When true (and PerformanceMode is off), the effective rendering tier escalates
        /// automatically (Quality → Balanced → Performance) as more heavy on-screen elements
        /// (flashes/bubbles) become active. See Services/PerformanceProfile.cs.
        /// </summary>
        public bool AutoPerformanceMode
        {
            get => _autoPerformanceMode;
            set { _autoPerformanceMode = value; OnPropertyChanged(); }
        }

        private MotionLevel _motionLevel = MotionLevel.Full;
        /// <summary>
        /// How much UI motion is allowed. Full by default; Reduced keeps crossfades but kills
        /// ambient loops, particles and parallax; Off snaps everything. The effective level is
        /// additionally capped to Reduced when the OS animation-effects flag is off — read
        /// Services/MotionFx.Level rather than this property.
        /// </summary>
        [JsonProperty("MotionLevel")]
        public MotionLevel MotionLevel
        {
            get => _motionLevel;
            set { _motionLevel = value; OnPropertyChanged(); }
        }

        private bool _videoForceHardwareDecoding = false;
        /// <summary>
        /// Force GPU (DXVA) hardware decoding for mandatory videos. Default OFF — mandatory videos
        /// software-decode, because the LibVLC hardware path intermittently renders a white screen
        /// and wedges cleanup on Windows 11 (build 26200) and some Win10 machines (#533/#537/#540).
        /// These are short attention-check clips, so software decode costs little. This is an opt-in
        /// escape hatch for users on good hardware who want GPU decode back.
        /// NOTE: property was renamed from the old default-ON "VideoHardwareDecoding" precisely so
        /// existing users' persisted true value stops binding and everyone lands on software decode.
        /// </summary>
        public bool VideoForceHardwareDecoding
        {
            get => _videoForceHardwareDecoding;
            set { _videoForceHardwareDecoding = value; OnPropertyChanged(); }
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

        private bool _useCompanionBrain = true;
        /// <summary>
        /// Train 1 kill switch. True routes companion conversation through <c>CompanionBrain</c>
        /// (<c>App.Brain</c>) — one turn log shared by every provider, so cloud chat finally has
        /// memory of the current conversation and of previous launches.
        ///
        /// <para>False restores the pre-Train-1 behaviour exactly: each call site goes straight to
        /// <c>IAiService</c>'s stateless one-shot methods. Nothing else differs — the moderation
        /// spine, the pink AI badge semantics and the ChatMemoryEnabled toggle apply on both paths —
        /// so this is a safe switch to flip if the brain misbehaves in the field.</para>
        ///
        /// <para>Not a privacy control: conversation persistence is gated by
        /// <c>CompanionPrompt.ChatMemoryEnabled</c> on both paths.</para>
        /// </summary>
        [JsonProperty]
        public bool UseCompanionBrain
        {
            get => _useCompanionBrain;
            set { _useCompanionBrain = value; OnPropertyChanged(); }
        }

        private int _idleGiggleIntervalSeconds = 120; // 20-600 seconds; drives the idle BARK cadence (AvatarTubeWindow.OnIdleTick → BarkService.DispatchIdle)
        /// <summary>
        /// How often the companion speaks when idle (in seconds)
        /// </summary>
        public int IdleGiggleIntervalSeconds
        {
            get => _idleGiggleIntervalSeconds;
            set { _idleGiggleIntervalSeconds = Math.Clamp(value, 20, 600); OnPropertyChanged(); }
        }

        private double _bubbleDurationSeconds = 2.0;
        /// <summary>
        /// How long speech bubbles stay on screen (in seconds, 1-10). Default 2.
        /// </summary>
        public double BubbleDurationSeconds
        {
            get => _bubbleDurationSeconds;
            set { _bubbleDurationSeconds = Math.Clamp(value, 1.0, 10.0); OnPropertyChanged(); }
        }

        private bool _companionVoiceLinesMuted = false;
        /// <summary>
        /// Mute only the companion's spoken voicelines (#846): the bubble, its text, and the
        /// giggle/bubble sound cues all stay — the pre-recorded VO alone goes quiet. Distinct
        /// from AvatarMuted (which silences her outright) and from MasterVolume==0.
        /// </summary>
        [JsonProperty]
        public bool CompanionVoiceLinesMuted
        {
            get => _companionVoiceLinesMuted;
            set { _companionVoiceLinesMuted = value; OnPropertyChanged(); }
        }

        // Persisted avatar-tube (companion window) placement (#669). Restored on startup so a
        // detached, dragged, or rescaled companion comes back where the user left it. Left/Top use
        // NaN as the "unset" sentinel (no saved position yet -> fall back to the default anchor).
        private bool _avatarTubeDetached = false;
        /// <summary>Whether the companion window was detached from the main window at last exit.</summary>
        public bool AvatarTubeDetached
        {
            get => _avatarTubeDetached;
            set { _avatarTubeDetached = value; OnPropertyChanged(); }
        }

        private double _avatarTubeLeft = double.NaN;
        /// <summary>Saved detached companion X position (NaN = unset).</summary>
        public double AvatarTubeLeft
        {
            get => _avatarTubeLeft;
            set { _avatarTubeLeft = value; OnPropertyChanged(); }
        }

        private double _avatarTubeTop = double.NaN;
        /// <summary>Saved detached companion Y position (NaN = unset).</summary>
        public double AvatarTubeTop
        {
            get => _avatarTubeTop;
            set { _avatarTubeTop = value; OnPropertyChanged(); }
        }

        private double _avatarTubeScale = 1.0;
        /// <summary>Saved companion scale (Ctrl+scroll zoom). Default 1.0.</summary>
        public double AvatarTubeScale
        {
            get => _avatarTubeScale;
            set { _avatarTubeScale = value; OnPropertyChanged(); }
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

        private int _awarenessCooldownMaxSeconds = 0;
        /// <summary>
        /// Upper bound (seconds) for a randomized reaction cooldown. When set above
        /// AwarenessReactionCooldownSeconds, each reaction rolls a random cooldown in
        /// [base, max]; 0 (default) disables randomization so the fixed cooldown is used
        /// unchanged. Clamped to the same 10-600 range as the base cooldown (plus 0).
        /// </summary>
        public int AwarenessCooldownMaxSeconds
        {
            get => _awarenessCooldownMaxSeconds;
            set { _awarenessCooldownMaxSeconds = value <= 0 ? 0 : Math.Clamp(value, 10, 600); OnPropertyChanged(); }
        }

        // ---------- Awareness v2 (Train 2, "She notices") ----------

        private bool _useAwarenessV2 = true;
        /// <summary>
        /// Train 2 kill switch. True runs the v2 pipeline: <c>AwarenessObserver</c> with a dwell gate,
        /// the persisted <c>ActivityLedger</c> behind her callbacks, worthiness scoring, and one shared
        /// arbiter so a bark and an LLM quip can no longer both fire on the same window change.
        ///
        /// <para>False restores today's behaviour end to end — the legacy <c>WindowAwarenessService</c>
        /// poll, its cooldown helpers and the AvatarTube reaction path — with no ledger written and no
        /// v2 setting on this page having any effect.</para>
        ///
        /// <para>Not a privacy control. Recording is governed by <see cref="AwarenessModeEnabled"/> +
        /// <see cref="AwarenessConsentGiven"/>, the deny list and the adult-recording toggle, on both
        /// paths.</para>
        /// </summary>
        [JsonProperty]
        public bool UseAwarenessV2
        {
            get => _useAwarenessV2;
            set { _useAwarenessV2 = value; OnPropertyChanged(); }
        }

        private Services.Awareness.AwarenessIntensity _awarenessIntensity = Services.Awareness.AwarenessIntensity.Chatty;
        /// <summary>
        /// How talkative she is about what you are doing — the one dial that replaces the cooldown
        /// slider, the cooldown-max slider and the (dead) per-category toggles. Maps internally to a
        /// line budget per hour, the worthiness threshold and whether the Rare tier is armed
        /// (<c>AwarenessIntensityProfile</c>). Off silences awareness lines without losing any settings.
        /// </summary>
        [JsonProperty]
        public Services.Awareness.AwarenessIntensity AwarenessIntensity
        {
            get => _awarenessIntensity;
            set { _awarenessIntensity = value; OnPropertyChanged(); }
        }

        private List<string> _awarenessDenyList = new();
        /// <summary>
        /// Apps she must never see: matched as case-insensitive substrings against the resolved app id
        /// and display name. A deny-listed app produces no frame, no ledger entry and no reaction —
        /// ever.
        ///
        /// <para>Ships EMPTY. The privacy package seeds the recommended defaults (password managers,
        /// banking, mail clients, health portals) so the seeding is visible and editable rather than
        /// invisible and hard-coded. Entries are sanitised on the way in: length-capped, lowercased,
        /// wildcard characters removed, and anything that would collapse to "match everything"
        /// dropped.</para>
        /// </summary>
        [JsonProperty]
        public List<string> AwarenessDenyList
        {
            get => _awarenessDenyList;
            set { _awarenessDenyList = Services.Awareness.AwarenessText.SanitizeRuleList(value); OnPropertyChanged(); }
        }

        private List<string> _awarenessTitleAllowList = new();
        /// <summary>
        /// The only apps whose page/tab title may be included in what she is told —
        /// <c>ContextFrame.PageTitleSanitized</c> stays null for everything else.
        ///
        /// <para>Ships EMPTY, which inverts today's behaviour: page titles currently go to the cloud
        /// for every app. Same sanitising as the deny list, and for the same reason — an entry that
        /// silently meant "every app" here would leak titles rather than merely over-mute.</para>
        /// </summary>
        [JsonProperty]
        public List<string> AwarenessTitleAllowList
        {
            get => _awarenessTitleAllowList;
            set { _awarenessTitleAllowList = Services.Awareness.AwarenessText.SanitizeRuleList(value); OnPropertyChanged(); }
        }

        private int _awarenessRetentionDays = 30;
        /// <summary>
        /// How many days of activity counters the local ledger keeps (7-90, default 30). Pruning runs
        /// when the observer starts and on every day rollover — never only when a page is opened.
        /// </summary>
        [JsonProperty]
        public int AwarenessRetentionDays
        {
            get => _awarenessRetentionDays;
            set { _awarenessRetentionDays = Math.Clamp(value, 7, 90); OnPropertyChanged(); }
        }

        private bool _awarenessAdultReactionsEnabled = true;
        /// <summary>
        /// Whether she reacts at all to the adult-content cluster (doc 02 §6.1: on by default — it is
        /// the app's whole theme and the funniest material). Off means those frames are scored and
        /// recorded but never spoken about.
        ///
        /// <para>Independent of what crosses the wire: for that cluster only the cluster id is ever
        /// sent, never the site name or the title, regardless of this toggle or any allow list.</para>
        /// </summary>
        [JsonProperty]
        public bool AwarenessAdultReactionsEnabled
        {
            get => _awarenessAdultReactionsEnabled;
            set { _awarenessAdultReactionsEnabled = value; OnPropertyChanged(); }
        }

        private bool _awarenessAdultRecordingEnabled = true;
        /// <summary>
        /// Whether adult-cluster visits are written to the local ledger at all. Off means no counters,
        /// no streaks and no callbacks for that cluster — and those entries are the first thing the
        /// privacy panel's wipe button clears when it is on.
        /// </summary>
        [JsonProperty]
        public bool AwarenessAdultRecordingEnabled
        {
            get => _awarenessAdultRecordingEnabled;
            set { _awarenessAdultRecordingEnabled = value; OnPropertyChanged(); }
        }

        private bool _awarenessConsentShownV2 = false;
        /// <summary>
        /// Whether the plain-language awareness consent dialog has been shown and accepted at least once
        /// (doc 02 §6.3). False means the next attempt to open her eyes raises the dialog instead of
        /// switching silently; true means the toggle is one click, as it is for every other setting.
        ///
        /// <para>Separate from <see cref="AwarenessConsentGiven"/> on purpose:
        /// <c>AwarenessConsentGiven</c> is the live "is she allowed to watch" flag and follows the
        /// toggle, while this records that the explanation was actually read once. Upgraders who had the
        /// feature on before v2 land here as false and get the dialog the first time they touch it,
        /// which is the whole point — they never saw one.</para>
        /// </summary>
        [JsonProperty]
        public bool AwarenessConsentShownV2
        {
            get => _awarenessConsentShownV2;
            set { _awarenessConsentShownV2 = value; OnPropertyChanged(); }
        }

        private bool _awarenessDenySeeded = false;
        /// <summary>
        /// Whether the recommended deny groups (password managers, banking, email titles) have been
        /// written into <see cref="AwarenessDenyList"/>. Set by
        /// <c>AwarenessPrivacyRules.EnsureSeeded</c>, which runs once, from the consent flow.
        ///
        /// <para>Until it is true the privacy layer applies those groups anyway, so protection never
        /// depends on start-up ordering. After it is true the user's list is authoritative: removing a
        /// seeded chip removes the rule, and nothing puts it back.</para>
        /// </summary>
        [JsonProperty]
        public bool AwarenessDenySeeded
        {
            get => _awarenessDenySeeded;
            set { _awarenessDenySeeded = value; OnPropertyChanged(); }
        }

        private bool _awarenessIntensityMigrated = false;
        /// <summary>
        /// Whether <see cref="AwarenessReactionCooldownSeconds"/> has been mapped onto
        /// <see cref="AwarenessIntensity"/> (<c>AwarenessIntensityMigration</c>). Once only — a second
        /// run would overwrite whatever the user picked on the dial afterwards.
        /// </summary>
        [JsonProperty]
        public bool AwarenessIntensityMigrated
        {
            get => _awarenessIntensityMigrated;
            set { _awarenessIntensityMigrated = value; OnPropertyChanged(); }
        }

        private Dictionary<string, bool> _companionSectionOpen = new();
        /// <summary>
        /// Remembered open/collapsed state of the Companion tab's accordion sections, keyed by
        /// section name (Behaviour, Phrases, Content, Community). Absent key = collapsed (default).
        /// </summary>
        public Dictionary<string, bool> CompanionSectionOpen
        {
            get => _companionSectionOpen;
            set { _companionSectionOpen = value ?? new(); OnPropertyChanged(); }
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
            set { _activeCompanionId = Math.Clamp(value, 0, 4); OnPropertyChanged(); }
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

        /// <summary>
        /// OpenRouter API key for AI chat features.
        /// Stored in DPAPI-encrypted file, NOT in settings.json.
        /// </summary>
        [JsonIgnore]
        public string OpenRouterApiKey
        {
            get => Services.SecureApiKeyStore.Retrieve() ?? "";
            set { Services.SecureApiKeyStore.Store(string.IsNullOrEmpty(value) ? null : value); OnPropertyChanged(); }
        }

        /// <summary>
        /// Legacy plaintext key — only used for one-time migration to DPAPI.
        /// After migration this will be null in settings.json.
        /// </summary>
        [JsonProperty("OpenRouterApiKey")]
        public string? OpenRouterApiKeyLegacy
        {
            get => null; // Never write back to JSON
            set
            {
                // Migrate: if there's a plaintext key in settings.json, move it to DPAPI
                if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(Services.SecureApiKeyStore.Retrieve()))
                {
                    Services.SecureApiKeyStore.Store(value);
                }
            }
        }

        private bool _slutModeEnabled = false;
        /// <summary>
        /// When true, BambiSprite.GetSystemPrompt swaps the active preset's
        /// Personality text with its SlutModePersonality variant, giving a spicier
        /// version of the same persona. Available to all users.
        /// </summary>
        public bool SlutModeEnabled
        {
            get => _slutModeEnabled;
            set { _slutModeEnabled = value; OnPropertyChanged(); }
        }

        private bool _avatarMuted = false;
        public bool AvatarMuted
        {
            get => _avatarMuted;
            set { _avatarMuted = value; OnPropertyChanged(); }
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

        private string _activePersonalityPresetId = PersonalityPresets.BambiSpriteId;
        /// <summary>
        /// ID of the currently active personality preset.
        /// </summary>
        public string ActivePersonalityPresetId
        {
            get => _activePersonalityPresetId;
            set { _activePersonalityPresetId = value ?? PersonalityPresets.BambiSpriteId; OnPropertyChanged(); }
        }

        private List<PersonalityPreset> _userPersonalityPresets = new();
        /// <summary>
        /// User-created personality presets (customizations or copies of built-ins).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<PersonalityPreset> UserPersonalityPresets
        {
            get => _userPersonalityPresets;
            set { _userPersonalityPresets = value ?? new(); OnPropertyChanged(); }
        }

        private List<KnowledgeBaseLink> _globalKnowledgeBaseLinks = new();
        /// <summary>
        /// Global knowledge base links shared across ALL personality presets.
        /// These are appended to every AI prompt regardless of which personality is active.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<KnowledgeBaseLink> GlobalKnowledgeBaseLinks
        {
            get => _globalKnowledgeBaseLinks;
            set { _globalKnowledgeBaseLinks = value ?? new(); OnPropertyChanged(); }
        }

        private string _hypnotubeLinksBambiSleep = "";
        /// <summary>
        /// Comma-separated hypnotube links for Bambi Sleep content mode.
        /// </summary>
        [JsonProperty("hypnotube_links_bambi_sleep")]
        public string HypnotubeLinksBambiSleep
        {
            get => _hypnotubeLinksBambiSleep;
            set { _hypnotubeLinksBambiSleep = value ?? ""; OnPropertyChanged(); }
        }

        private string _hypnotubeLinksSissyHypno = "";
        /// <summary>
        /// Comma-separated hypnotube links for Sissy Hypno content mode.
        /// </summary>
        [JsonProperty("hypnotube_links_sissy_hypno")]
        public string HypnotubeLinksSissyHypno
        {
            get => _hypnotubeLinksSissyHypno;
            set { _hypnotubeLinksSissyHypno = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display name for current content mode.
        /// </summary>
        [JsonIgnore]
        public string ContentModeDisplay => App.Mods?.GetModeDisplayName() ?? "CCP Default";

        /// <summary>
        /// Gets/sets the hypnotube links for the currently active content mode.
        /// </summary>
        [JsonIgnore]
        public string ActiveHypnotubeLinks
        {
            get => IsBambiMode ? HypnotubeLinksBambiSleep : HypnotubeLinksSissyHypno;
            set
            {
                if (IsBambiMode)
                    HypnotubeLinksBambiSleep = value;
                else
                    HypnotubeLinksSissyHypno = value;
                OnPropertyChanged();
            }
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

        private bool _showTakeoverCountdownBar = true;
        /// <summary>
        /// Show a thin pink countdown bar under the avatar that drains toward the next
        /// random Takeover action. On by default; hidden via the Takeover tab toggle.
        /// </summary>
        public bool ShowTakeoverCountdownBar
        {
            get => _showTakeoverCountdownBar;
            set { _showTakeoverCountdownBar = value; OnPropertyChanged(); }
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

        private bool _autonomyCanTriggerWebVideo = false;
        /// <summary>
        /// Allow autonomous web video playback from HypnoTube (plays fullscreen in browser)
        /// </summary>
        [JsonProperty]
        public bool AutonomyCanTriggerWebVideo
        {
            get => _autonomyCanTriggerWebVideo;
            set { _autonomyCanTriggerWebVideo = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerWallpaper = false;
        [JsonProperty]
        public bool AutonomyCanTriggerWallpaper
        {
            get => _autonomyCanTriggerWallpaper;
            set { _autonomyCanTriggerWallpaper = value; OnPropertyChanged(); }
        }

        private bool _takeoverVideosStrict = false;
        /// <summary>
        /// RETIRED — no longer read or surfaced in the UI. Takeover videos are plain mandatory
        /// videos and follow the global StrictLockEnabled flag like every other one; having a
        /// second, independent notion of "strict" meant Takeover imposed unskippable videos (and
        /// its own consent dialog) regardless of the mandatory-video setting. Kept only so
        /// existing settings.json files continue to deserialize.
        /// </summary>
        [JsonProperty]
        public bool TakeoverVideosStrict
        {
            get => _takeoverVideosStrict;
            set { _takeoverVideosStrict = value; OnPropertyChanged(); }
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

        // ── Takeover start/stop + speech ("repeat after me") ──────────────────────

        private bool _autonomyResumeOnStartup = false;
        /// <summary>
        /// Opt-in: re-arm Takeover automatically on app launch. Default OFF — Takeover now
        /// always starts OFF and the user explicitly turns it on (fixes "it stays on after restart").
        /// </summary>
        [JsonProperty]
        public bool AutonomyResumeOnStartup
        {
            get => _autonomyResumeOnStartup;
            set { _autonomyResumeOnStartup = value; OnPropertyChanged(); }
        }

        private bool _autonomyCanTriggerVoiceCommand = true;
        /// <summary>
        /// Takeover "Surprise me with mantras": let the autonomy scheduler auto-prompt a spoken
        /// mantra during Takeover. Only ever fires when the speech engine is available (model + mic),
        /// mic consent is given, and the user isn't already driving the mic (wake/PTT). Self-disables
        /// otherwise. The on-demand mantra capability lives separately in <see cref="SpokenMantrasEnabled"/>.
        /// </summary>
        [JsonProperty]
        public bool AutonomyCanTriggerVoiceCommand
        {
            get => _autonomyCanTriggerVoiceCommand;
            set { _autonomyCanTriggerVoiceCommand = value; OnPropertyChanged(); }
        }

        private bool _spokenMantrasEnabled = false;
        /// <summary>
        /// "She's Listening" on-demand spoken mantras: when on, a wake-word / push-to-talk turn that
        /// doesn't match a voice command falls back to a mantra, and the Test affordance works. The
        /// Takeover *surprise* auto-trigger is the separate <see cref="AutonomyCanTriggerVoiceCommand"/>.
        /// Independent of Takeover — the mic features are decoupled from it.
        /// </summary>
        [JsonProperty]
        public bool SpokenMantrasEnabled
        {
            get => _spokenMantrasEnabled;
            set { _spokenMantrasEnabled = value; OnPropertyChanged(); }
        }

        private bool _micConsentGiven = false;
        /// <summary>
        /// Explicit consent to open the microphone for the offline "repeat after me" mechanic.
        /// Never implied — the mic stays closed until this is true.
        /// </summary>
        [JsonProperty]
        public bool MicConsentGiven
        {
            get => _micConsentGiven;
            set { _micConsentGiven = value; OnPropertyChanged(); }
        }

        private int _speechInputDeviceIndex = -1;
        /// <summary>WaveIn capture device index, or -1 for the Windows default device.</summary>
        [JsonProperty]
        public int SpeechInputDeviceIndex
        {
            get => _speechInputDeviceIndex;
            set { _speechInputDeviceIndex = value; OnPropertyChanged(); }
        }

        private string _speechInputDeviceName = "";
        /// <summary>WaveIn capture device NAME (ProductName) for the chosen mic. Preferred over the raw
        /// ordinal when reopening the mic, because NAudio device indices reshuffle when virtual audio
        /// devices come and go — a stale ordinal then silently points at a dead input ("voice worked
        /// yesterday, not today", #441b). Empty = fall back to the ordinal / system default.</summary>
        [JsonProperty]
        public string SpeechInputDeviceName
        {
            get => _speechInputDeviceName;
            set { _speechInputDeviceName = value ?? ""; OnPropertyChanged(); }
        }

        private double _speechMatchThreshold = 0.62;
        /// <summary>Minimum fuzzy similarity (0..1) for a spoken phrase to count as a match.</summary>
        [JsonProperty]
        public double SpeechMatchThreshold
        {
            get => _speechMatchThreshold;
            set { _speechMatchThreshold = Math.Clamp(value, 0.1, 1.0); OnPropertyChanged(); }
        }

        // Was 0.04, which proved too high: it rejected normal-volume speech that Vosk had ALREADY
        // recognized as "too quiet" (the avatar would ask you to be louder, or silently drop a matched
        // command). 0.010 (~-40 dBFS) still sits above typical room tone (~0.003-0.008) but lets a soft
        // speaking voice through. Users tune it live via the "Mic sensitivity" slider (She's Listening);
        // existing users at the old 0.04 default are relaxed by MigrateLoudnessThreshold() on load.
        private double _speechLoudnessThreshold = 0.010;
        /// <summary>Minimum peak RMS loudness (0..1) for a phrase to count as "said out loud".</summary>
        [JsonProperty]
        public double SpeechLoudnessThreshold
        {
            get => _speechLoudnessThreshold;
            set { _speechLoudnessThreshold = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(); }
        }

        private bool _loudnessThresholdRelaxed;
        /// <summary>One-shot guard for <see cref="MigrateLoudnessThreshold"/> so a future explicit choice sticks.</summary>
        [JsonProperty]
        public bool LoudnessThresholdRelaxed
        {
            get => _loudnessThresholdRelaxed;
            set { _loudnessThresholdRelaxed = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Relax the legacy 0.04 loudness gate to the gentler default for existing users. Nobody set
        /// 0.04 deliberately (there's no UI for it), so any value parked at the old default is bumped to
        /// 0.015. One-shot — once relaxed (or once a user picks their own value via a future UI), it
        /// never re-fires.
        /// </summary>
        internal void MigrateLoudnessThreshold()
        {
            if (_loudnessThresholdRelaxed) return;
            if (_speechLoudnessThreshold >= 0.035 && _speechLoudnessThreshold <= 0.045)
                _speechLoudnessThreshold = 0.015;
            _loudnessThresholdRelaxed = true;
        }

        private bool _migratedUnifiedOverlayHostOn;
        /// <summary>One-shot guard for <see cref="MigrateEnableUnifiedOverlayHost"/> so a user who
        /// turns the compositor toggle off afterwards isn't clobbered back on at the next launch.</summary>
        [JsonProperty]
        public bool MigratedUnifiedOverlayHostOn
        {
            get => _migratedUnifiedOverlayHostOn;
            set { _migratedUnifiedOverlayHostOn = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Force the unified overlay host ON once for users upgrading from 6.3.3/6.3.4. The
        /// 6.3.4 hotfix force-migrated everyone OFF (bug #550: the host's unthrottled software
        /// raster saturated the UI thread) and persisted "false" to settings.json, so the
        /// default flip back to ON wouldn't reach them. #550 is fixed (dirty-gated invalidation)
        /// and the compositor is now the blessed render path, so re-enable once; the
        /// Settings-tab toggle ("Unified overlay renderer") lets anyone opt back out and their
        /// choice sticks. Supersedes the retired MigrateDisableUnifiedOverlayHost — its
        /// MigratedUnifiedOverlayHostOff sentinel key is simply ignored in old settings files.
        /// </summary>
        internal void MigrateEnableUnifiedOverlayHost()
        {
            if (_migratedUnifiedOverlayHostOn) return;
            _unifiedOverlayHost = true;
            _migratedUnifiedOverlayHostOn = true;
        }

        private bool _migratedCompositorOffThreadOn;
        /// <summary>One-shot guard for <see cref="MigrateEnableCompositorOffThreadPresent"/> so a user who
        /// turns the off-thread present toggle off afterwards isn't clobbered back on at the next launch.</summary>
        [JsonProperty]
        public bool MigratedCompositorOffThreadOn
        {
            get => _migratedCompositorOffThreadOn;
            set { _migratedCompositorOffThreadOn = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Force the off-thread compositor present path ON once for users upgrading from 6.4.0 and
        /// earlier, which persisted "false" (the flag defaulted OFF while the compositor itself
        /// defaulted ON). That combo rastered the fullscreen spiral on the UI thread and starved the
        /// dispatcher on high-res / multi-monitor machines (bugs #588/#586/#587), so the field-default
        /// flip to ON wouldn't reach them without this. One-shot — turning the toggle off later sticks.
        /// No-op when the unified host is off (the present path only runs under the compositor).
        /// </summary>
        internal void MigrateEnableCompositorOffThreadPresent()
        {
            if (_migratedCompositorOffThreadOn) return;
            _compositorOffThreadPresent = true;
            _migratedCompositorOffThreadOn = true;
        }

        private double _speechWakeThreshold = 0.15;
        /// <summary>
        /// sherpa KWS trigger threshold (0..1) for the "Hey Bambi" wake word — the config-level
        /// KeywordsThreshold applied to every keyword line. Lower = wakes more easily (fewer misses,
        /// more false wakes). Default 0.15 is recall-biased; the in-app wake calibration overwrites this
        /// with a value tuned to the user's own voice + mic. Per-user, so it survives the keyword set.
        /// </summary>
        [JsonProperty]
        public double SpeechWakeThreshold
        {
            get => _speechWakeThreshold;
            set { _speechWakeThreshold = Math.Clamp(value, 0.02, 0.6); OnPropertyChanged(); }
        }

        private double _speechWakeBoost = 2.0;
        /// <summary>sherpa KWS keyword boost (KeywordsScore) for the wake word. Higher = easier to fire.</summary>
        [JsonProperty]
        public double SpeechWakeBoost
        {
            get => _speechWakeBoost;
            set { _speechWakeBoost = Math.Clamp(value, 0.0, 5.0); OnPropertyChanged(); }
        }

        private bool _speechWakeDiagnostics;
        /// <summary>
        /// Dev/diagnostic: when on, the sherpa wake spotter logs capture start/stop and a periodic mic
        /// level (peak RMS) + frame count, so we can tell from the log whether the mic is actually
        /// capturing and how loud speech is reaching it. Off by default (it's chatty).
        /// </summary>
        [JsonProperty]
        public bool SpeechWakeDiagnostics
        {
            get => _speechWakeDiagnostics;
            set { _speechWakeDiagnostics = value; OnPropertyChanged(); }
        }

        private bool _speechWakeWordEnabled = false;
        /// <summary>Opt-in always-on "Hey Bambi" wake-word listening (mic stays open). Pass-2 UI.</summary>
        [JsonProperty]
        public bool SpeechWakeWordEnabled
        {
            get => _speechWakeWordEnabled;
            set { _speechWakeWordEnabled = value; OnPropertyChanged(); }
        }

        private string _speechWakeWords = "hey bambi";
        /// <summary>Comma-separated wake phrases for the opt-in always-on path.</summary>
        [JsonProperty]
        public string SpeechWakeWords
        {
            get => _speechWakeWords;
            set { _speechWakeWords = value ?? ""; OnPropertyChanged(); }
        }

        private bool _speechPushToTalkEnabled = false;
        /// <summary>Opt-in push-to-talk (overrides auto-listen for noisy rooms). Pass-2 UI.</summary>
        [JsonProperty]
        public bool SpeechPushToTalkEnabled
        {
            get => _speechPushToTalkEnabled;
            set { _speechPushToTalkEnabled = value; OnPropertyChanged(); }
        }

        private string _speechPushToTalkKey = "F8";
        /// <summary>The key that summons a voice prompt when push-to-talk is on. Parsed as a <see cref="System.Windows.Input.Key"/>.</summary>
        [JsonProperty]
        public string SpeechPushToTalkKey
        {
            get => _speechPushToTalkKey;
            set { _speechPushToTalkKey = string.IsNullOrWhiteSpace(value) ? "F8" : value; OnPropertyChanged(); }
        }

        private double _speechWakeMatchThreshold = 0.6;
        /// <summary>
        /// Fuzzy-match strictness (0..1) for the "Hey Bambi" wake word. Lower = wakes more easily (good
        /// because "bambi" is out-of-vocabulary for the offline model, so it transcribes loosely); higher
        /// = fewer false wakes. Default 0.6 — was effectively 0.8, which missed ~half of real wakes.
        /// </summary>
        [JsonProperty]
        public double SpeechWakeMatchThreshold
        {
            get => _speechWakeMatchThreshold;
            set { _speechWakeMatchThreshold = Math.Clamp(value, 0.3, 0.95); OnPropertyChanged(); }
        }

        private bool _speechHeadphonesMode = false;
        /// <summary>
        /// "I use headphones" — when on, the avatar's own voice can't bleed into the mic, so the command
        /// listener allows barge-in: it skips the wait-until-she's-quiet echo guard and opens the mic even
        /// while she's still talking. Off (default, safe for speakers) keeps the half-duplex guard so the
        /// recognizer never hears her own voice as a bogus command.
        /// </summary>
        [JsonProperty]
        public bool SpeechHeadphonesMode
        {
            get => _speechHeadphonesMode;
            set { _speechHeadphonesMode = value; OnPropertyChanged(); }
        }

        private bool _speechNoiseSuppression = true;
        /// <summary>
        /// Mic noise front-end: strips low-frequency rumble (AC units, fans, mains hum) with a high-pass
        /// filter and gates onset on an ADAPTIVE noise floor instead of a fixed loudness threshold, so a
        /// steady room hum self-raises the trigger point rather than firing it. On by default; turn off to
        /// feed raw mic audio to the recognizers (the pre-6.2.x behaviour).
        /// </summary>
        [JsonProperty]
        public bool SpeechNoiseSuppression
        {
            get => _speechNoiseSuppression;
            set { _speechNoiseSuppression = value; OnPropertyChanged(); }
        }

        private double _speechNoiseGateFactor = 4.0;
        /// <summary>
        /// SNR margin for the adaptive noise gate: a frame counts as "voiced" when its RMS exceeds the
        /// tracked noise floor by this multiple (~+12 dB at 4.0). Higher = stricter (needs to be clearly
        /// louder than the room — good for noisy rooms); lower = more sensitive. Only used when
        /// <see cref="SpeechNoiseSuppression"/> is on.
        /// </summary>
        [JsonProperty]
        public double SpeechNoiseGateFactor
        {
            get => _speechNoiseGateFactor;
            set { _speechNoiseGateFactor = Math.Clamp(value, 1.5, 8.0); OnPropertyChanged(); }
        }

        #endregion

        #region Takeover — Wallpaper Override

        private bool _wallpaperEnabled = false;
        /// <summary>
        /// Keep her wallpaper changes on the desktop instead of reverting after
        /// <see cref="WallpaperPulseSeconds"/>. Still restored when the app closes. (#694)
        /// </summary>
        [JsonProperty]
        public bool WallpaperEnabled
        {
            get => _wallpaperEnabled;
            set { _wallpaperEnabled = value; OnPropertyChanged(); }
        }

        private int _wallpaperPulseSeconds = 30;
        /// <summary>
        /// How long a Takeover wallpaper change sticks around before the original comes back.
        /// Ignored while <see cref="WallpaperEnabled"/> is on.
        /// </summary>
        [JsonProperty]
        public int WallpaperPulseSeconds
        {
            get => _wallpaperPulseSeconds;
            set { _wallpaperPulseSeconds = Math.Clamp(value, 10, 600); OnPropertyChanged(); }
        }

        private string _wallpaperOriginalPath = "";
        /// <summary>
        /// The desktop wallpaper WallpaperService captured before overriding it. Written on
        /// activate and cleared on a successful restore, so a session that dies without
        /// restoring (crash / task-kill) can put it back on the next launch (#692).
        /// Not user-facing.
        /// </summary>
        [JsonProperty]
        public string WallpaperOriginalPath
        {
            get => _wallpaperOriginalPath;
            set { _wallpaperOriginalPath = value ?? ""; OnPropertyChanged(); }
        }

        private string _wallpaperSourceFolder = "";
        /// <summary>
        /// Folder the wallpaper takeover pulls images from. Empty = default to the
        /// assets/wallpapers folder under EffectiveAssetsPath.
        /// </summary>
        [JsonProperty]
        public string WallpaperSourceFolder
        {
            get => _wallpaperSourceFolder;
            set { _wallpaperSourceFolder = value; OnPropertyChanged(); }
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

        #region V5.5 Season System

        private string? _unifiedId = null;
        /// <summary>
        /// Unified user ID from v5.5+ server. Persists across logout to enable
        /// seamless re-login with any linked provider.
        /// </summary>
        public string? UnifiedId
        {
            get => _unifiedId;
            set { _unifiedId = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Server-issued auth token for V2 API requests. Rotated on each auth event.
        /// Stored in DPAPI-encrypted file, NOT in settings.json.
        /// </summary>
        [JsonIgnore]
        public string? AuthToken
        {
            get => Services.SecureAuthTokenStore.Retrieve();
            set { Services.SecureAuthTokenStore.Store(value); OnPropertyChanged(); }
        }

        private string? _userDisplayName = null;
        /// <summary>
        /// User's display name (synced with server). Used across all providers.
        /// </summary>
        public string? UserDisplayName
        {
            get => _userDisplayName;
            set { _userDisplayName = value; OnPropertyChanged(); }
        }

        private bool _isSeason0Og = false;
        /// <summary>
        /// Whether user is a Season 0 OG (had account before v5.5).
        /// Grants special badge and leaderboard flair.
        /// </summary>
        public bool IsSeason0Og
        {
            get => _isSeason0Og;
            set { _isSeason0Og = value; OnPropertyChanged(); }
        }

        private bool _ogLevelUnlockEnabled = false;
        /// <summary>
        /// Whether OG users have enabled the level unlock bypass.
        /// When true, OG users can access all level-gated features regardless of current level.
        /// </summary>
        public bool OgLevelUnlockEnabled
        {
            get => _ogLevelUnlockEnabled;
            set { _ogLevelUnlockEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Feature level gating has been removed — every feature is available from level 1.
        /// XP, levels, quests, achievements, and the skill tree still exist; they just no longer
        /// gate any features. Method stub preserved so existing call sites keep compiling.
        /// </summary>
        public bool IsLevelUnlocked(int requiredLevel)
        {
            return true;
        }

        private string? _currentSeason = null;
        /// <summary>
        /// Current season identifier (e.g., "2026-02").
        /// Used to detect season changes and trigger resets.
        /// </summary>
        public string? CurrentSeason
        {
            get => _currentSeason;
            set { _currentSeason = value; OnPropertyChanged(); }
        }

        private int _highestLevelEver = 0;
        /// <summary>
        /// Highest level ever achieved (persists across season resets).
        /// Used for determining permanent unlocks.
        /// </summary>
        public int HighestLevelEver
        {
            get => _highestLevelEver;
            set { _highestLevelEver = Math.Max(0, value); OnPropertyChanged(); }
        }

        #region Season Recap (local-only, per-device)

        // The Season Recap Card surfaces a snapshot of the just-ended season at rollover.
        // These counters are accumulated LOCALLY ONLY (no server, no new endpoints — locked
        // decision #2). They are scoped to SeasonStatsSeason; SeasonRecapService snapshots
        // them BEFORE rolling to a new season. None of these participate in the server-driven
        // level/XP reset, so the all-time figures they sit beside (TotalConditioningMinutes,
        // TotalSessionsStarted) are unaffected. First season after deploy will undercount
        // because tracking starts at install — by design.

        private string? _seasonStatsSeason = null;
        /// <summary>
        /// "YYYY-MM" the live season counters below currently belong to. Null until the first
        /// session/launch initializes it. Advanced only by SeasonRecapService at rollover
        /// (after the snapshot is written), never mid-increment.
        /// </summary>
        public string? SeasonStatsSeason
        {
            get => _seasonStatsSeason;
            set { _seasonStatsSeason = value; OnPropertyChanged(); }
        }

        private double _seasonConditioningMinutes = 0;
        /// <summary>Conditioning minutes accumulated during SeasonStatsSeason (resets each season).</summary>
        public double SeasonConditioningMinutes
        {
            get => _seasonConditioningMinutes;
            set { _seasonConditioningMinutes = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _seasonSessionsStarted = 0;
        /// <summary>Sessions started during SeasonStatsSeason (resets each season).</summary>
        public int SeasonSessionsStarted
        {
            get => _seasonSessionsStarted;
            set { _seasonSessionsStarted = Math.Max(0, value); OnPropertyChanged(); }
        }

        private List<string> _seasonActiveDays = new();
        /// <summary>
        /// Distinct "yyyy-MM-dd" dates the user was active this season (resets each season).
        /// Count gives "Days Active". Stored as strings for JSON friendliness.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SeasonActiveDays
        {
            get => _seasonActiveDays;
            set { _seasonActiveDays = value ?? new(); OnPropertyChanged(); }
        }

        private int _seasonPeakStreak = 0;
        /// <summary>
        /// Highest ConsecutiveDays streak reached during SeasonStatsSeason. Tracked separately
        /// from CurrentStreak because the server-driven reset can zero CurrentStreak before the
        /// snapshot runs — the peak must survive that.
        /// </summary>
        public int SeasonPeakStreak
        {
            get => _seasonPeakStreak;
            set { _seasonPeakStreak = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _seasonPeakRank = 0;
        /// <summary>
        /// Best (lowest) leaderboard rank sampled during SeasonStatsSeason while the app was
        /// open (decision #1: client-sampled, no server field). 0 = never sampled.
        /// </summary>
        public int SeasonPeakRank
        {
            get => _seasonPeakRank;
            set { _seasonPeakRank = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _seasonPeakRankTotal = 0;
        /// <summary>Total leaderboard users at the moment SeasonPeakRank was captured (for "of N").</summary>
        public int SeasonPeakRankTotal
        {
            get => _seasonPeakRankTotal;
            set { _seasonPeakRankTotal = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _seasonPeakLevel = 0;
        /// <summary>
        /// Highest PlayerLevel reached during SeasonStatsSeason (resets each season).
        /// Snapshot proxy for "how far did I get this season" since PlayerLevel itself
        /// is wiped by the server at rollover.
        /// </summary>
        public int SeasonPeakLevel
        {
            get => _seasonPeakLevel;
            set { _seasonPeakLevel = Math.Max(0, value); OnPropertyChanged(); }
        }

        private int _seasonPointsSpent = 0;
        /// <summary>
        /// Sparkle points spent on enhancements during SeasonStatsSeason (resets each season).
        /// Feeds the recap card's Prestige delta and the Season Rewind spend column.
        /// </summary>
        public int SeasonPointsSpent
        {
            get => _seasonPointsSpent;
            set { _seasonPointsSpent = Math.Max(0, value); OnPropertyChanged(); }
        }

        private Dictionary<string, int> _seasonFeatureUse = new();
        /// <summary>
        /// Per-feature engagement counts for SeasonStatsSeason, keyed by SeasonFeatureKeys.*.
        /// Counted once per session per enabled feature (plus standalone hooks). Top entries
        /// drive the card badge row. Lightest-touch ranking signal, not heavy analytics.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, int> SeasonFeatureUse
        {
            get => _seasonFeatureUse;
            set { _seasonFeatureUse = value ?? new(); OnPropertyChanged(); }
        }

        /// <summary>
        /// Increment the per-season engagement count for a feature key. No-op on null/empty key.
        /// Does not Save() — callers batch saves at natural points (session start, etc.).
        /// </summary>
        public void TrackSeasonFeature(string featureKey)
        {
            if (string.IsNullOrWhiteSpace(featureKey)) return;
            _seasonFeatureUse.TryGetValue(featureKey, out var n);
            _seasonFeatureUse[featureKey] = n + 1;
            OnPropertyChanged(nameof(SeasonFeatureUse));
        }

        #endregion

        private bool _hasAcceptedAgeVerification = false;
        /// <summary>
        /// Whether the user has accepted the 18+ age verification prompt.
        /// </summary>
        public bool HasAcceptedAgeVerification
        {
            get => _hasAcceptedAgeVerification;
            set { _hasAcceptedAgeVerification = value; OnPropertyChanged(); }
        }

        private bool _hasShownOgWelcome = false;
        /// <summary>
        /// Whether the OG welcome popup has been shown to this user.
        /// </summary>
        public bool HasShownOgWelcome
        {
            get => _hasShownOgWelcome;
            set { _hasShownOgWelcome = value; OnPropertyChanged(); }
        }

        private bool _hasLinkedDiscord = false;
        /// <summary>
        /// Whether a Discord account is linked to this unified user.
        /// </summary>
        public bool HasLinkedDiscord
        {
            get => _hasLinkedDiscord;
            set { _hasLinkedDiscord = value; OnPropertyChanged(); }
        }

        private bool _hasLinkedPatreon = false;
        /// <summary>
        /// Whether a Patreon account is linked to this unified user.
        /// </summary>
        public bool HasLinkedPatreon
        {
            get => _hasLinkedPatreon;
            set { _hasLinkedPatreon = value; OnPropertyChanged(); }
        }

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

        #region Keyword Triggers

        private bool _keywordTriggersEnabled = false;
        /// <summary>
        /// Enable keyword trigger system — intercepts typed text and fires multi-modal responses.
        /// Requires Patreon access. Not persisted — must be started each session.
        /// </summary>
        [JsonIgnore]
        public bool KeywordTriggersEnabled
        {
            get => _keywordTriggersEnabled;
            set { _keywordTriggersEnabled = value; OnPropertyChanged(); }
        }

        private int _keywordBufferTimeoutMs = 3000;
        /// <summary>
        /// Time in ms before the typed text buffer resets (1000-10000)
        /// </summary>
        public int KeywordBufferTimeoutMs
        {
            get => _keywordBufferTimeoutMs;
            set { _keywordBufferTimeoutMs = Math.Clamp(value, 1000, 10000); OnPropertyChanged(); }
        }

        private int _keywordGlobalCooldownSeconds = 10;
        /// <summary>
        /// Global cooldown between any trigger firing, in seconds (clamped 1-300).
        /// Enforced on all three match sources (OCR, keyboard, external text) —
        /// this is a hard ceiling on trigger frequency regardless of how many
        /// matches are on screen. Primarily prevents the OCR feedback loop
        /// (avatar speech bubble getting re-read on next scan) from spamming.
        /// Default raised to 10 per user preference — 10s minimum between any
        /// two reactions, paired with KeywordPerKeywordCooldownSeconds for the
        /// stricter 15s same-keyword hard cooldown.
        /// </summary>
        public int KeywordGlobalCooldownSeconds
        {
            get => _keywordGlobalCooldownSeconds;
            set { _keywordGlobalCooldownSeconds = Math.Clamp(value, 1, 300); OnPropertyChanged(); }
        }

        private int _keywordPerKeywordCooldownSeconds = 15;
        /// <summary>
        /// Hard minimum cooldown between two fires of the SAME keyword, in seconds
        /// (clamped 1-600). Enforced at RecordFire time via the _mutedKeywords
        /// dictionary independent of AwarenessLoopProtectionEnabled. Floor for
        /// the per-trigger <see cref="KeywordTrigger.CooldownSeconds"/> — presets
        /// that declare a lower cooldown will still be gated at this minimum.
        /// </summary>
        [JsonProperty]
        public int KeywordPerKeywordCooldownSeconds
        {
            get => _keywordPerKeywordCooldownSeconds;
            set { _keywordPerKeywordCooldownSeconds = Math.Clamp(value, 1, 600); OnPropertyChanged(); }
        }

        private double _keywordSessionMultiplier = 1.5;
        /// <summary>
        /// XP multiplier when a session is active (1.0-3.0)
        /// </summary>
        public double KeywordSessionMultiplier
        {
            get => _keywordSessionMultiplier;
            set { _keywordSessionMultiplier = Math.Clamp(value, 1.0, 3.0); OnPropertyChanged(); }
        }

        private AwarenessAppScope _keywordTriggerAppScope = AwarenessAppScope.Everywhere;
        /// <summary>
        /// Which applications triggers may fire in, judged by the foreground window's process.
        /// Defaults to <see cref="AwarenessAppScope.Everywhere"/>, i.e. the behaviour that shipped
        /// before this setting existed - turning app scoping on is an opt-in.
        /// </summary>
        [JsonProperty]
        public AwarenessAppScope KeywordTriggerAppScope
        {
            get => _keywordTriggerAppScope;
            set { _keywordTriggerAppScope = value; OnPropertyChanged(); }
        }

        private List<string> _keywordTriggerApps = new();
        /// <summary>
        /// The process names <see cref="KeywordTriggerAppScope"/> refers to - one list, read as a
        /// block list or an allow list depending on the mode, so there is never a second stale list
        /// sitting behind the one in use.
        ///
        /// Entries are process names, matched case-insensitively with an optional ".exe" that is
        /// stripped before comparing ("chrome", "Chrome", "chrome.exe" are the same entry). Empty
        /// while the mode is Everywhere.
        /// </summary>
        [JsonProperty]
        public List<string> KeywordTriggerApps
        {
            get => _keywordTriggerApps;
            set { _keywordTriggerApps = value ?? new(); OnPropertyChanged(); }
        }

        private bool _keywordTriggerIgnoreOwnFocus = false;
        /// <summary>
        /// Suppress every source while a Control Panel window itself holds focus - so typing a
        /// keyword INTO the trigger editor, or into the companion's chat box, does not fire it.
        ///
        /// Distinct from <see cref="AwarenessIgnoreOwnUi"/>, which drops OCR hits that land inside
        /// our own window RECTANGLES. That one cannot see the keyboard path at all; this one is
        /// about who has focus and applies to every source. Default off: someone typing to their
        /// companion may well want the reaction, so this is offered rather than assumed.
        /// </summary>
        [JsonProperty]
        public bool KeywordTriggerIgnoreOwnFocus
        {
            get => _keywordTriggerIgnoreOwnFocus;
            set { _keywordTriggerIgnoreOwnFocus = value; OnPropertyChanged(); }
        }

        private bool _screenOcrEnabled = false;
        public bool ScreenOcrEnabled
        {
            get => _screenOcrEnabled;
            set { _screenOcrEnabled = value; OnPropertyChanged(); }
        }

        private int _screenOcrIntervalMs = 3000;
        public int ScreenOcrIntervalMs
        {
            get => _screenOcrIntervalMs;
            set { _screenOcrIntervalMs = Math.Clamp(value, 2000, 10000); OnPropertyChanged(); }
        }

        private int _ocrConfirmationScans = 2;
        /// <summary>
        /// Number of consecutive scans a keyword must appear in (at the same on-screen
        /// position) before it is allowed to fire. Filters transient OCR ghosts from
        /// scrolling, tab switches, or a word that moved between frames — which used to
        /// leave a highlight box hanging over empty space. 1 = fire on first sighting
        /// (legacy behavior), 2 = double confirmation (default), 3 = triple.
        /// </summary>
        [JsonProperty]
        public int OcrConfirmationScans
        {
            get => _ocrConfirmationScans;
            set { _ocrConfirmationScans = Math.Clamp(value, 1, 5); OnPropertyChanged(); }
        }

        private bool _keywordHighlightEnabled = true;
        [JsonProperty]
        public bool KeywordHighlightEnabled
        {
            get => _keywordHighlightEnabled;
            set { _keywordHighlightEnabled = value; OnPropertyChanged(); }
        }

        private int _keywordHighlightDurationMs = 1500;
        [JsonProperty]
        public int KeywordHighlightDurationMs
        {
            get => _keywordHighlightDurationMs;
            set { _keywordHighlightDurationMs = Math.Clamp(value, 300, 5000); OnPropertyChanged(); }
        }

        private string _keywordHighlightColor = "#FF69B4";
        /// <summary>
        /// Hex color (<c>#RRGGBB</c>) used for the OCR keyword highlight overlay box,
        /// border, glow, and fill. Defaults to neon pink. Parsed at render time by
        /// <see cref="Services.KeywordHighlightService"/>; invalid values fall back
        /// to the default.
        /// </summary>
        [JsonProperty]
        public string KeywordHighlightColor
        {
            get => _keywordHighlightColor;
            set { _keywordHighlightColor = string.IsNullOrWhiteSpace(value) ? "#FF69B4" : value; OnPropertyChanged(); }
        }

        private bool _ocrHighlightAll = true;
        [JsonProperty("ocrHighlightAll")]
        public bool OcrHighlightAll
        {
            get => _ocrHighlightAll;
            set { _ocrHighlightAll = value; OnPropertyChanged(); }
        }

        private bool _ocrHighlightVisibleInCapture;
        [JsonProperty("ocrHighlightVisibleInCapture")]
        public bool OcrHighlightVisibleInCapture
        {
            get => _ocrHighlightVisibleInCapture;
            set { _ocrHighlightVisibleInCapture = value; OnPropertyChanged(); }
        }


        private List<KeywordTrigger> _keywordTriggers = new();
        /// <summary>
        /// Configured keyword triggers
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<KeywordTrigger> KeywordTriggers
        {
            get => _keywordTriggers;
            set { _keywordTriggers = value ?? new List<KeywordTrigger>(); OnPropertyChanged(); }
        }

        // --- Awareness Engine safety ---

        private bool _awarenessIgnoreOwnUi = true;
        /// <summary>
        /// When true, OCR word hits that fall inside any CCP window (MainWindow, avatar,
        /// subliminal flashes, highlight overlays, dialogs) are discarded before matching.
        /// Prevents the app from reacting to its own output.
        /// </summary>
        [JsonProperty("awarenessIgnoreOwnUi")]
        public bool AwarenessIgnoreOwnUi
        {
            get => _awarenessIgnoreOwnUi;
            set { _awarenessIgnoreOwnUi = value; OnPropertyChanged(); }
        }

        private bool _awarenessLoopProtectionEnabled = true;
        /// <summary>
        /// When true, a keyword that has just fired a trigger is temporarily muted
        /// across all sources so the trigger's own output cannot re-arm it.
        /// </summary>
        [JsonProperty("awarenessLoopProtectionEnabled")]
        public bool AwarenessLoopProtectionEnabled
        {
            get => _awarenessLoopProtectionEnabled;
            set { _awarenessLoopProtectionEnabled = value; OnPropertyChanged(); }
        }

        private int _awarenessLoopProtectionMs = 5000;
        /// <summary>
        /// Duration (ms) a keyword stays muted after firing, when loop protection is on.
        /// </summary>
        [JsonProperty("awarenessLoopProtectionMs")]
        public int AwarenessLoopProtectionMs
        {
            get => _awarenessLoopProtectionMs;
            set { _awarenessLoopProtectionMs = Math.Clamp(value, 500, 30000); OnPropertyChanged(); }
        }

        // --- Awareness preset packs ---

        private List<KeywordTriggerPreset> _keywordTriggerPresets = new();
        /// <summary>
        /// Known keyword trigger presets (built-in + user-created). Built-in presets
        /// are merged from Resources/AwarenessPresets/*.json on each load; their
        /// MasterEnabled state and Triggers are then stored here per-user.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<KeywordTriggerPreset> KeywordTriggerPresets
        {
            get => _keywordTriggerPresets;
            set { _keywordTriggerPresets = value ?? new List<KeywordTriggerPreset>(); OnPropertyChanged(); }
        }

        /// <summary>
        /// Ids of built-in presets the user has explicitly removed. Removed presets
        /// are skipped by the merge step so they don't reappear after uninstall.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> RemovedBuiltInPresetIds { get; set; } = new();

        #endregion

        #region Companion Phrase Manager

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> DisabledPhraseIds { get; set; } = new();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> RemovedPhraseIds { get; set; } = new();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<CustomCompanionPhrase> CustomCompanionPhrases { get; set; } = new();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, string> PhraseAudioOverrides { get; set; } = new();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<PhrasePreset> PhrasePresets { get; set; } = new();

        [JsonProperty]
        public string? CurrentPhrasePresetId { get; set; }

        #endregion

        #region Mantra Lab

        private List<string> _mantraPool = new()
        {
            "I am deeply relaxed",
            "My mind is open and receptive",
            "I feel calm and peaceful",
            "I surrender to the process",
            "Every breath takes me deeper"
        };
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> MantraPool
        {
            get => _mantraPool;
            set { _mantraPool = value ?? new(); OnPropertyChanged(); }
        }

        private int _mantraDefaultCount = 10;
        public int MantraDefaultCount
        {
            get => _mantraDefaultCount;
            set { _mantraDefaultCount = Math.Clamp(value, 1, 100); OnPropertyChanged(); }
        }

        private double _mantraDroneVolume = 30;
        public double MantraDroneVolume
        {
            get => _mantraDroneVolume;
            set { _mantraDroneVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        // ── Mantra Chant (ambient looped voiced mantras — see MantraChantService) ──

        private bool _mantraChantEnabled = false;
        /// <summary>
        /// When on, the active mod's VOICED mantra clips loop back-to-back as ambient audio. No-ops
        /// for mods that ship no voiced mantras. Distinct from the Mantra Lab drone/reps above.
        /// </summary>
        public bool MantraChantEnabled
        {
            get => _mantraChantEnabled;
            set { _mantraChantEnabled = value; OnPropertyChanged(); }
        }

        private double _mantraChantVolume = 50;
        public double MantraChantVolume
        {
            get => _mantraChantVolume;
            set { _mantraChantVolume = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private int _mantraChantGapSeconds = 5;
        public int MantraChantGapSeconds
        {
            get => _mantraChantGapSeconds;
            set { _mantraChantGapSeconds = Math.Clamp(value, 0, 60); OnPropertyChanged(); }
        }

        #endregion

        #region Remote Control

        private bool _stopEffectsOnRemoteDisconnect;
        /// <summary>
        /// When true, all effects started by a remote controller stop immediately
        /// when the controller disconnects. When false (default), effects continue
        /// running so a new controller can see the current state and the session
        /// doesn't snap to a halt. The sub can always hit stop/panic manually.
        /// </summary>
        public bool StopEffectsOnRemoteDisconnect
        {
            get => _stopEffectsOnRemoteDisconnect;
            set { _stopEffectsOnRemoteDisconnect = value; OnPropertyChanged(); }
        }

        // Subject-side opt-in for exposing the linked Discord avatar to whoever's
        // currently controlling the session. Default false — privacy fails closed;
        // controller sees a silhouette unless the user explicitly flips this on.
        // Patreon avatars are not surfaced anywhere in the app, so this is purely
        // about the Discord avatar URL. Distinct from `share_profile_picture`
        // (legacy field on profile:* records governing leaderboard / Subjects
        // directory display). Do not conflate; different audience, different
        // threat model.
        private bool _remoteShareAvatar = false;
        public bool RemoteShareAvatar
        {
            get => _remoteShareAvatar;
            set { _remoteShareAvatar = value; OnPropertyChanged(); }
        }

        // SP5 layer 3 — Available Subjects directory opt-in.
        //
        // The opt-in checkbox itself NEVER persists across sessions: the user
        // re-opts every time they start a remote-control session. Only the tag
        // selection + status_text are persisted, and only when the user
        // explicitly checks "Remember tags + status".
        private bool _rememberDirectoryDetails;
        public bool RememberDirectoryDetails
        {
            get => _rememberDirectoryDetails;
            set { _rememberDirectoryDetails = value; OnPropertyChanged(); }
        }

        private List<string> _savedDirectoryTags = new();
        /// <summary>
        /// Tag IDs the user picked last time they opted into the directory and
        /// chose "Remember". Used to pre-fill the tag selector on the next
        /// session-start configuration. Capped at 5 entries on save (the UI
        /// also caps selection at 5).
        /// </summary>
        public List<string> SavedDirectoryTags
        {
            get => _savedDirectoryTags;
            set { _savedDirectoryTags = value ?? new List<string>(); OnPropertyChanged(); }
        }

        private string _savedDirectoryStatusText = "";
        /// <summary>
        /// Free-text status the user wrote last time they opted into the
        /// directory and chose "Remember". 80 char max (UI-enforced + clamped
        /// here on set).
        /// </summary>
        public string SavedDirectoryStatusText
        {
            get => _savedDirectoryStatusText;
            set
            {
                var v = value ?? "";
                _savedDirectoryStatusText = v.Length > 80 ? v.Substring(0, 80) : v;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Goon Game (Discord sharing)

        // Goon Game opt-in Discord sharing. Sharer-only gating: each flag governs what
        // THIS user exposes to the opponent, never what they receive. All default false —
        // privacy fails closed. See docs/GOON_DISCORD_CONTRACT.md §1/§2.
        //
        // Distinct from RemoteShareAvatar (remote-control audience) and
        // ShareProfilePicture (leaderboard / Subjects directory audience). Do not conflate;
        // different audience, different threat model.

        private bool _goonShareAvatar = false;
        /// <summary>
        /// Show the linked Discord avatar to the Goon Game opponent (VS splash + HUD bubble).
        /// Pushed to the server as `goon_share_avatar` on change.
        /// </summary>
        [JsonProperty("goonShareAvatar")]
        public bool GoonShareAvatar
        {
            get => _goonShareAvatar;
            set { _goonShareAvatar = value; OnPropertyChanged(); }
        }

        private bool _goonShareDiscordDm = false;
        /// <summary>
        /// Let the Goon Game opponent open a Discord DM with this user (they get a Message
        /// button; the snowflake is only ever resolved server-side).
        /// Pushed to the server as `goon_share_dm` on change.
        /// </summary>
        [JsonProperty("goonShareDiscordDm")]
        public bool GoonShareDiscordDm
        {
            get => _goonShareDiscordDm;
            set { _goonShareDiscordDm = value; OnPropertyChanged(); }
        }

        private bool _goonRichPresence = false;
        /// <summary>
        /// Show Goon Game activity in Discord Rich Presence (fixed strings only — never the
        /// opponent's name, never free text). LOCAL-ONLY: never synced to the server.
        /// </summary>
        [JsonProperty("goonRichPresence")]
        public bool GoonRichPresence
        {
            get => _goonRichPresence;
            set { _goonRichPresence = value; OnPropertyChanged(); }
        }

        private bool _goonSeenSharePrompt = false;
        /// <summary>
        /// True once the one-time first-duel sharing confirm has been shown. Written by the
        /// page via the discord-prefs bridge verb, echoed back on the next `discord` message.
        /// </summary>
        [JsonProperty("goonSeenSharePrompt")]
        public bool GoonSeenSharePrompt
        {
            get => _goonSeenSharePrompt;
            set { _goonSeenSharePrompt = value; OnPropertyChanged(); }
        }

        private string _goonLastOpponentJson = "";
        /// <summary>
        /// Serialized { name, dmId, avatarFile, ts } for the MOST RECENT opponent only
        /// (overwrite semantics). avatarFile is a bare filename inside
        /// %LOCALAPPDATA%\ConditioningControlPanel\goon_avatars\ — never a full path.
        /// Written by GoonHostService only.
        /// </summary>
        [JsonProperty("goonLastOpponentJson")]
        public string GoonLastOpponentJson
        {
            get => _goonLastOpponentJson;
            set { _goonLastOpponentJson = value ?? ""; OnPropertyChanged(); }
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

        #region Webcam Tracking (Lab — Box 1 + Box 2)

        // Consent + calibration
        private bool _webcamConsentGiven;
        public bool WebcamConsentGiven
        {
            get => _webcamConsentGiven;
            set { _webcamConsentGiven = value; OnPropertyChanged(); }
        }

        private string _webcamConsentVersion = "";
        public string WebcamConsentVersion
        {
            get => _webcamConsentVersion;
            set { _webcamConsentVersion = value ?? ""; OnPropertyChanged(); }
        }

        private DateTime? _webcamConsentDate;
        public DateTime? WebcamConsentDate
        {
            get => _webcamConsentDate;
            set { _webcamConsentDate = value; OnPropertyChanged(); }
        }

        private bool _webcamCalibrated;
        public bool WebcamCalibrated
        {
            get => _webcamCalibrated;
            set { _webcamCalibrated = value; OnPropertyChanged(); }
        }

        private string _webcamCalibrationMode = "";
        public string WebcamCalibrationMode
        {
            get => _webcamCalibrationMode;
            set { _webcamCalibrationMode = value ?? ""; OnPropertyChanged(); }
        }

        // Which monitor the calibration / Quick Recal / Tracker Test windows
        // open on. "Primary" = follow the system primary; otherwise the
        // System.Windows.Forms.Screen.DeviceName (e.g. "\\.\DISPLAY2"). Stored
        // by device name (not index) so reordering monitors is non-destructive
        // when possible — when the named display is gone, the runtime falls
        // back to Primary silently.
        private string _webcamCalibrationScreen = "Primary";
        public string WebcamCalibrationScreen
        {
            get => _webcamCalibrationScreen;
            set { _webcamCalibrationScreen = string.IsNullOrWhiteSpace(value) ? "Primary" : value; OnPropertyChanged(); }
        }

        // Index passed to OpenCV's VideoCapture. -1 means "not yet chosen", which
        // the service treats as 0 (system default). Surfaced via the camera
        // selector in the Lab tab so users with virtual cameras (OBS, Snap, etc.)
        // can pick the physical webcam.
        private int _webcamDeviceIndex = -1;
        public int WebcamDeviceIndex
        {
            get => _webcamDeviceIndex;
            set { _webcamDeviceIndex = value; OnPropertyChanged(); }
        }

        // Friendly name remembered alongside the index — purely for UI display
        // and the "we picked the wrong one because the order shuffled" log line.
        private string _webcamDeviceName = "";
        public string WebcamDeviceName
        {
            get => _webcamDeviceName;
            set { _webcamDeviceName = value ?? ""; OnPropertyChanged(); }
        }

        // Box 1 — Webcam Triggers
        private bool _webcamTriggersEnabled;
        public bool WebcamTriggersEnabled
        {
            get => _webcamTriggersEnabled;
            set { _webcamTriggersEnabled = value; OnPropertyChanged(); }
        }

        private bool _webcamTriggerBlink = true;
        public bool WebcamTriggerBlink
        {
            get => _webcamTriggerBlink;
            set { _webcamTriggerBlink = value; OnPropertyChanged(); }
        }

        private bool _webcamTriggerLongStare = true;
        public bool WebcamTriggerLongStare
        {
            get => _webcamTriggerLongStare;
            set { _webcamTriggerLongStare = value; OnPropertyChanged(); }
        }

        private bool _webcamTriggerMouthOpen = true;
        public bool WebcamTriggerMouthOpen
        {
            get => _webcamTriggerMouthOpen;
            set { _webcamTriggerMouthOpen = value; OnPropertyChanged(); }
        }

        private bool _webcamTriggerBubbleStare;
        public bool WebcamTriggerBubbleStare
        {
            get => _webcamTriggerBubbleStare;
            set { _webcamTriggerBubbleStare = value; OnPropertyChanged(); }
        }

        private double _webcamSensitivity = 0.5;
        public double WebcamSensitivity
        {
            get => _webcamSensitivity;
            set { _webcamSensitivity = value; OnPropertyChanged(); }
        }

        // Click-driven implicit recalibration (GazeDriftCorrectionService).
        // While tracking runs with a calibration loaded, each left-click the
        // user makes near their fixated gaze point nudges the runtime offset
        // a little toward the click — posture drift self-corrects instead of
        // requiring Quick Recal. Default on; the toggle lives in the Lab
        // webcam debug card.
        private bool _webcamAutoDriftCorrection = true;
        public bool WebcamAutoDriftCorrection
        {
            get => _webcamAutoDriftCorrection;
            set { _webcamAutoDriftCorrection = value; OnPropertyChanged(); }
        }

        // Box 2 — Focus Training
        private bool _focusGameEnabled;
        public bool FocusGameEnabled
        {
            get => _focusGameEnabled;
            set { _focusGameEnabled = value; OnPropertyChanged(); }
        }

        private List<FocusGameBucket> _focusGameBuckets = new();
        public List<FocusGameBucket> FocusGameBuckets
        {
            get => _focusGameBuckets;
            set { _focusGameBuckets = value ?? new(); OnPropertyChanged(); }
        }

        private int _focusGameRoundCount = 10;
        public int FocusGameRoundCount
        {
            get => _focusGameRoundCount;
            set { _focusGameRoundCount = value; OnPropertyChanged(); }
        }

        private int _focusGameRoundDurationMs = 4000;
        public int FocusGameRoundDurationMs
        {
            get => _focusGameRoundDurationMs;
            set { _focusGameRoundDurationMs = value; OnPropertyChanged(); }
        }

        private string _focusGameMonitor = "Primary";
        public string FocusGameMonitor
        {
            get => _focusGameMonitor;
            set { _focusGameMonitor = value ?? "Primary"; OnPropertyChanged(); }
        }

        private int _focusGameCorrectXp = 30;
        public int FocusGameCorrectXp
        {
            get => _focusGameCorrectXp;
            set { _focusGameCorrectXp = value; OnPropertyChanged(); }
        }

        private int _focusGameSessionsPlayed;
        public int FocusGameSessionsPlayed
        {
            get => _focusGameSessionsPlayed;
            set { _focusGameSessionsPlayed = value; OnPropertyChanged(); }
        }

        private int _focusGameTotalCorrect;
        public int FocusGameTotalCorrect
        {
            get => _focusGameTotalCorrect;
            set { _focusGameTotalCorrect = value; OnPropertyChanged(); }
        }

        private int _focusGameTotalRounds;
        public int FocusGameTotalRounds
        {
            get => _focusGameTotalRounds;
            set { _focusGameTotalRounds = value; OnPropertyChanged(); }
        }

        #endregion

        #region Blink Trainer (Lab — Webcam Games)

        private List<string> _blinkTrainerFolders = new();
        public List<string> BlinkTrainerFolders
        {
            get => _blinkTrainerFolders;
            set { _blinkTrainerFolders = value ?? new(); OnPropertyChanged(); }
        }

        private int _blinkTrainerDurationMinutes = 10;
        public int BlinkTrainerDurationMinutes
        {
            get => _blinkTrainerDurationMinutes;
            set { _blinkTrainerDurationMinutes = Math.Clamp(value, 1, 180); OnPropertyChanged(); }
        }

        private int _blinkTrainerOpacity = 80;
        public int BlinkTrainerOpacity
        {
            get => _blinkTrainerOpacity;
            set { _blinkTrainerOpacity = Math.Clamp(value, 1, 100); OnPropertyChanged(); }
        }

        private bool _blinkTrainerIncludeVideos;
        public bool BlinkTrainerIncludeVideos
        {
            get => _blinkTrainerIncludeVideos;
            set { _blinkTrainerIncludeVideos = value; OnPropertyChanged(); }
        }

        private bool _blinkTrainerMixImages;
        public bool BlinkTrainerMixImages
        {
            get => _blinkTrainerMixImages;
            set { _blinkTrainerMixImages = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Set once the one-time asset migration (install-dir assets -> %APPDATA% user folder)
        /// has completed. Without this flag the migration re-copies the entire library on every
        /// launch: its only re-copy guard was a per-file "destination exists?" check, so a user
        /// who deleted the %APPDATA% copy to reclaim disk space got all ~10GB copied again next
        /// launch, repeatedly filling the system drive.
        /// </summary>
        public bool HasMigratedAssetsToUserFolder { get; set; }

        #endregion

        #region Training Programs

        /// <summary>
        /// Set the first time the user clicks the Programs tab button. Until then the tab button
        /// pulses once on startup to draw the eye to it, the same one-shot treatment the Deeper tab
        /// got when it shipped (see <see cref="HasSeenDeeperTab"/>). Never cleared: the pulse is an
        /// announcement, so a user who has already found the tab must not be nagged again.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenProgramsTab { get; set; }

        /// <summary>
        /// Set when the one-time "what Training Programs are" explainer has been shown. Kept
        /// separate from <see cref="HasSeenProgramsTab"/> on purpose: the pulse is spent the moment
        /// the tab is clicked, but the explainer has to survive that same click so it can open on
        /// top of the tab the user just landed on.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenProgramsIntro { get; set; }

        #endregion

        #region First-time experience

        // One-shot feature intro cards (Windows/FeatureIntroPopup). Each key is spent the
        // moment its card is about to open - same contract as HasSeenProgramsIntro - so a
        // card that fails to display burns nothing and one that displays never re-fires.
        private List<string> _seenFeatureIntros = new();
        [JsonProperty]
        public List<string> SeenFeatureIntros
        {
            get => _seenFeatureIntros;
            set { _seenFeatureIntros = value ?? new List<string>(); OnPropertyChanged(); }
        }

        #endregion

        #region Deeper

        private bool _enableDeeper = true;
        public bool EnableDeeper
        {
            get => _enableDeeper;
            set { _enableDeeper = value; OnPropertyChanged(); }
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenDeeperTab { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeededDeeperDemos { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenDeeperWelcome { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenDeeperEditorIntro { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasSeenDeeperHTInteractiveTutorial { get; set; }

        // Mission 1: editor sidebar restructure introduces a draggable splitter
        // between preview and the inspector panel; persist the user's chosen
        // width so it survives editor close + reopen. Clamped 320..520 by the
        // GridSplitter's column MinWidth/MaxWidth.
        private int _deeperEditorSidebarWidth = 380;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int DeeperEditorSidebarWidth
        {
            get => _deeperEditorSidebarWidth;
            set { _deeperEditorSidebarWidth = value; OnPropertyChanged(); }
        }

        private List<string> _deeperRecentFiles = new();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> DeeperRecentFiles
        {
            get => _deeperRecentFiles;
            set { _deeperRecentFiles = value ?? new(); OnPropertyChanged(); }
        }

        private string _deeperLastDirectory = "";
        public string DeeperLastDirectory
        {
            get => _deeperLastDirectory;
            set { _deeperLastDirectory = value ?? ""; OnPropertyChanged(); }
        }

        private bool _browserEnhanceIfPossible = true;
        public bool BrowserEnhanceIfPossible
        {
            get => _browserEnhanceIfPossible;
            set { _browserEnhanceIfPossible = value; OnPropertyChanged(); }
        }

        // Apply matching .ccpenh.json enhancements to mandatory + asset-folder
        // videos (the VideoService.PlayVideo path). Default OFF — opt-in, mirrors
        // BrowserEnhanceIfPossible but conservative since it drives effects over
        // mandatory video playback.
        private bool _videoEnhanceIfPossible = false;
        public bool VideoEnhanceIfPossible
        {
            get => _videoEnhanceIfPossible;
            set { _videoEnhanceIfPossible = value; OnPropertyChanged(); }
        }

        #endregion

        #region Migrations

        /// <summary>
        /// Phase 3.4: preserve "no interaction" intent for users who had
        /// FlashClickable=false before the decoupling. Pre-3.4, FlashClickable
        /// was a master switch for both mouse and gaze; Phase 3 split gaze-pop
        /// and stare-linger into their own toggles, both default ON. Without
        /// this migration, a hands-free / accessibility user upgrading from
        /// an older build would silently get gaze interaction enabled.
        ///
        /// One-shot via <see cref="MigratedFlashClickableDecoupling"/> — new
        /// installs run the same code path harmlessly (FlashClickable defaults
        /// to true, so the inner branch is a no-op), and a user who later
        /// configures the new toggles independently won't have them clobbered.
        /// Caller is responsible for persisting the settings file after this
        /// returns.
        /// </summary>
        public void RunFlashClickableDecouplingMigration()
        {
            if (MigratedFlashClickableDecoupling) return;

            if (!FlashClickable)
            {
                FlashGazePopEnabled = false;
                FlashGazeLingerEnabled = false;
                // Record that WE took the gaze toggles (not the user), so the
                // FlashClickable setter can restore them if clicking comes back on.
                // A heuristic re-enable ("clickable on + both toggles off") was tried
                // and rejected: it can't distinguish this stuck state from a user who
                // deliberately opted out of gaze interaction, and silently re-enabling
                // webcam-driven interaction against an explicit opt-out is worse than
                // asking the affected upgraders to flip one toggle.
                FlashGazeDisabledByDecoupling = true;
            }

            MigratedFlashClickableDecoupling = true;
        }

        #endregion
    }
}