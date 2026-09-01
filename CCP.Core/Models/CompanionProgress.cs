using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Tracks the progression state for a single companion.
    /// Each companion has their own independent level and XP.
    /// </summary>
    public class CompanionProgress : INotifyPropertyChanged
    {
        /// <summary>
        /// Maximum level a companion can reach.
        /// </summary>
        public const int MaxLevel = 100;

        /// <summary>
        /// Base XP required for level 1.
        /// </summary>
        public const double BaseXPPerLevel = 500;

        /// <summary>
        /// Additional XP per level (linear scaling).
        /// </summary>
        public const double XPPerLevelGrowth = 50;

        private CompanionId _companionId;
        private int _level = 1;
        private double _currentXP = 0;
        private double _totalXPEarned = 0;
        private DateTime _firstActivated = DateTime.MinValue;
        private TimeSpan _totalActiveTime = TimeSpan.Zero;

        /// <summary>
        /// Which companion this progress belongs to.
        /// </summary>
        public CompanionId CompanionId
        {
            get => _companionId;
            set { _companionId = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Current level (1 to MaxLevel).
        /// </summary>
        public int Level
        {
            get => _level;
            set
            {
                _level = Math.Clamp(value, 1, MaxLevel);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMaxLevel));
                OnPropertyChanged(nameof(XPForNextLevel));
                OnPropertyChanged(nameof(LevelProgress));
            }
        }

        /// <summary>
        /// XP accumulated towards the next level.
        /// </summary>
        public double CurrentXP
        {
            get => _currentXP;
            set
            {
                _currentXP = Math.Max(0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LevelProgress));
            }
        }

        /// <summary>
        /// Lifetime total XP earned by this companion.
        /// </summary>
        public double TotalXPEarned
        {
            get => _totalXPEarned;
            set { _totalXPEarned = Math.Max(0, value); OnPropertyChanged(); }
        }

        /// <summary>
        /// When this companion was first activated.
        /// </summary>
        public DateTime FirstActivated
        {
            get => _firstActivated;
            set { _firstActivated = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Total time this companion has been the active companion.
        /// </summary>
        public TimeSpan TotalActiveTime
        {
            get => _totalActiveTime;
            set { _totalActiveTime = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this companion has reached max level.
        /// </summary>
        [JsonIgnore]
        public bool IsMaxLevel => Level >= MaxLevel;

        /// <summary>
        /// XP required to advance from current level to next.
        /// </summary>
        [JsonIgnore]
        public double XPForNextLevel => GetXPForLevel(Level);

        /// <summary>
        /// Progress towards next level as 0.0 to 1.0.
        /// </summary>
        [JsonIgnore]
        public double LevelProgress
        {
            get
            {
                if (IsMaxLevel) return 1.0;
                var required = XPForNextLevel;
                if (required <= 0) return 0;
                return Math.Clamp(CurrentXP / required, 0, 1);
            }
        }

        /// <summary>
        /// Gets the XP required to advance from a given level to the next.
        /// Formula: 500 base + 50 per level
        /// Level 1: 550 XP, Level 50: 3000 XP, Level 99: 5450 XP
        /// </summary>
        public static double GetXPForLevel(int level)
        {
            if (level >= MaxLevel) return 0; // Max level, no more XP needed
            return BaseXPPerLevel + (level * XPPerLevelGrowth);
        }

        /// <summary>
        /// Gets the cumulative XP needed to reach a given level from level 1.
        /// </summary>
        public static double GetCumulativeXPForLevel(int targetLevel)
        {
            if (targetLevel <= 1) return 0;

            double total = 0;
            for (int i = 1; i < targetLevel; i++)
            {
                total += GetXPForLevel(i);
            }
            return total;
        }

        /// <summary>
        /// Creates a new progress instance for the specified companion.
        /// </summary>
        public static CompanionProgress CreateNew(CompanionId id)
        {
            return new CompanionProgress
            {
                CompanionId = id,
                Level = 1,
                CurrentXP = 0,
                TotalXPEarned = 0,
                FirstActivated = DateTime.MinValue,
                TotalActiveTime = TimeSpan.Zero
            };
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
