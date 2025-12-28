using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a saved preset configuration
    /// </summary>
    public class Preset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _name = "New Preset";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private bool _isDefault = false;
        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(); }
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        // Flash Settings
        public bool FlashEnabled { get; set; } = true;
        public int FlashFrequency { get; set; } = 2;
        public int SimultaneousImages { get; set; } = 5;
        public int HydraLimit { get; set; } = 20;
        public int ImageScale { get; set; } = 100;
        public int FlashOpacity { get; set; } = 100;
        public int FadeDuration { get; set; } = 40;
        public bool FlashClickable { get; set; } = true;
        public bool CorruptionMode { get; set; } = false;

        // Video Settings
        public bool MandatoryVideosEnabled { get; set; } = false;
        public int VideosPerHour { get; set; } = 6;
        public bool StrictLockEnabled { get; set; } = false;

        // Attention Check (Mini-Game) Settings
        public bool AttentionChecksEnabled { get; set; } = false;
        public int AttentionDensity { get; set; } = 3;
        public int AttentionLifespan { get; set; } = 5;
        public int AttentionSize { get; set; } = 70;

        // Subliminal Settings
        public bool SubliminalEnabled { get; set; } = false;
        public int SubliminalFrequency { get; set; } = 5;
        public int SubliminalDuration { get; set; } = 2;
        public int SubliminalOpacity { get; set; } = 80;

        // Audio Settings
        public bool SubAudioEnabled { get; set; } = false;
        public int SubAudioVolume { get; set; } = 50;
        public int MasterVolume { get; set; } = 32;
        public bool AudioDuckingEnabled { get; set; } = true;
        public int DuckingLevel { get; set; } = 100;

        // Overlay Settings (Level 10+)
        public bool SpiralEnabled { get; set; } = false;
        public int SpiralOpacity { get; set; } = 15;
        public bool PinkFilterEnabled { get; set; } = false;
        public int PinkFilterOpacity { get; set; } = 10;

        // Bubbles Settings (Level 20+)
        public bool BubblesEnabled { get; set; } = false;
        public int BubblesFrequency { get; set; } = 5;

        // Lock Card Settings (Level 35+)
        public bool LockCardEnabled { get; set; } = false;
        public int LockCardFrequency { get; set; } = 2;
        public int LockCardRepeats { get; set; } = 3;
        public bool LockCardStrict { get; set; } = false;

        // System Settings
        public bool DualMonitorEnabled { get; set; } = true;
        public bool PanicKeyEnabled { get; set; } = true;

        /// <summary>
        /// Creates the 5 default presets
        /// </summary>
        public static List<Preset> GetDefaultPresets()
        {
            return new List<Preset>
            {
                new Preset
                {
                    Id = "default-gentle",
                    Name = "Gentle Introduction",
                    Description = "Very mild settings for beginners. Relaxed pace with minimal intensity.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 1,
                    SimultaneousImages = 3,
                    FlashOpacity = 60,
                    FadeDuration = 60,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 1,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 2,
                    SubliminalOpacity = 50,
                    SubAudioEnabled = false,
                    MasterVolume = 30,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-basics",
                    Name = "Bimbo Basics",
                    Description = "Light conditioning with occasional flashes. Good for daily use.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 2,
                    SimultaneousImages = 5,
                    FlashOpacity = 80,
                    FadeDuration = 40,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 2,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 3,
                    SubliminalOpacity = 60,
                    SubAudioEnabled = false,
                    MasterVolume = 35,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-pink-cloud",
                    Name = "Pink Cloud",
                    Description = "Medium intensity with visual focus. Dreamy pink aesthetic.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 2,
                    SimultaneousImages = 6,
                    FlashOpacity = 90,
                    FadeDuration = 30,
                    FlashClickable = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 3,
                    StrictLockEnabled = false,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 5,
                    SubliminalOpacity = 70,
                    SubAudioEnabled = true,
                    SubAudioVolume = 40,
                    MasterVolume = 40,
                    SpiralEnabled = false,
                    PinkFilterEnabled = false,
                    BubblesEnabled = true,
                    BubblesFrequency = 3,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-deep",
                    Name = "Deep Conditioning",
                    Description = "Higher intensity training. More frequent triggers and reinforcement.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 4,
                    SimultaneousImages = 8,
                    FlashOpacity = 100,
                    FadeDuration = 25,
                    FlashClickable = true,
                    CorruptionMode = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 5,
                    StrictLockEnabled = false,
                    AttentionChecksEnabled = true,
                    AttentionDensity = 3,
                    AttentionLifespan = 5,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 7,
                    SubliminalOpacity = 80,
                    SubAudioEnabled = true,
                    SubAudioVolume = 50,
                    MasterVolume = 45,
                    AudioDuckingEnabled = true,
                    DuckingLevel = 70,
                    SpiralEnabled = true,
                    SpiralOpacity = 5,
                    PinkFilterEnabled = true,
                    PinkFilterOpacity = 5,
                    BubblesEnabled = true,
                    BubblesFrequency = 6,
                    LockCardEnabled = true,
                    LockCardFrequency = 2,
                    LockCardRepeats = 3,
                    PanicKeyEnabled = true
                },
                new Preset
                {
                    Id = "default-surrender",
                    Name = "Total Surrender",
                    Description = "Maximum intensity preset. For experienced users seeking deeper conditioning.",
                    IsDefault = true,
                    FlashEnabled = true,
                    FlashFrequency = 5,
                    SimultaneousImages = 10,
                    HydraLimit = 15,
                    FlashOpacity = 100,
                    FadeDuration = 20,
                    FlashClickable = true,
                    CorruptionMode = true,
                    MandatoryVideosEnabled = true,
                    VideosPerHour = 8,
                    StrictLockEnabled = false,
                    AttentionChecksEnabled = true,
                    AttentionDensity = 4,
                    AttentionLifespan = 6,
                    AttentionSize = 60,
                    SubliminalEnabled = true,
                    SubliminalFrequency = 10,
                    SubliminalDuration = 3,
                    SubliminalOpacity = 90,
                    SubAudioEnabled = true,
                    SubAudioVolume = 60,
                    MasterVolume = 50,
                    AudioDuckingEnabled = true,
                    DuckingLevel = 80,
                    SpiralEnabled = true,
                    SpiralOpacity = 10,
                    PinkFilterEnabled = true,
                    PinkFilterOpacity = 10,
                    BubblesEnabled = true,
                    BubblesFrequency = 10,
                    LockCardEnabled = true,
                    LockCardFrequency = 4,
                    LockCardRepeats = 5,
                    LockCardStrict = false,
                    PanicKeyEnabled = true
                }
            };
        }

        /// <summary>
        /// Apply this preset to the given settings
        /// </summary>
        public void ApplyTo(AppSettings settings)
        {
            // Flash
            settings.FlashEnabled = FlashEnabled;
            settings.FlashFrequency = FlashFrequency;
            settings.SimultaneousImages = SimultaneousImages;
            settings.HydraLimit = HydraLimit;
            settings.ImageScale = ImageScale;
            settings.FlashOpacity = FlashOpacity;
            settings.FadeDuration = FadeDuration;
            settings.FlashClickable = FlashClickable;
            settings.CorruptionMode = CorruptionMode;

            // Video
            settings.MandatoryVideosEnabled = MandatoryVideosEnabled;
            settings.VideosPerHour = VideosPerHour;
            settings.StrictLockEnabled = StrictLockEnabled;

            // Attention Checks
            settings.AttentionChecksEnabled = AttentionChecksEnabled;
            settings.AttentionDensity = AttentionDensity;
            settings.AttentionLifespan = AttentionLifespan;
            settings.AttentionSize = AttentionSize;

            // Subliminal
            settings.SubliminalEnabled = SubliminalEnabled;
            settings.SubliminalFrequency = SubliminalFrequency;
            settings.SubliminalDuration = SubliminalDuration;
            settings.SubliminalOpacity = SubliminalOpacity;

            // Audio
            settings.SubAudioEnabled = SubAudioEnabled;
            settings.SubAudioVolume = SubAudioVolume;
            settings.MasterVolume = MasterVolume;
            settings.AudioDuckingEnabled = AudioDuckingEnabled;
            settings.DuckingLevel = DuckingLevel;

            // Overlays
            settings.SpiralEnabled = SpiralEnabled;
            settings.SpiralOpacity = SpiralOpacity;
            settings.PinkFilterEnabled = PinkFilterEnabled;
            settings.PinkFilterOpacity = PinkFilterOpacity;

            // Bubbles
            settings.BubblesEnabled = BubblesEnabled;
            settings.BubblesFrequency = BubblesFrequency;

            // Lock Card
            settings.LockCardEnabled = LockCardEnabled;
            settings.LockCardFrequency = LockCardFrequency;
            settings.LockCardRepeats = LockCardRepeats;
            settings.LockCardStrict = LockCardStrict;

            // System
            settings.DualMonitorEnabled = DualMonitorEnabled;
            settings.PanicKeyEnabled = PanicKeyEnabled;

            // Update current preset name
            settings.CurrentPresetName = Name;
        }

        /// <summary>
        /// Create a preset from current settings
        /// </summary>
        public static Preset FromSettings(AppSettings settings, string name, string description = "")
        {
            return new Preset
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                IsDefault = false,
                CreatedAt = DateTime.Now,

                // Flash
                FlashEnabled = settings.FlashEnabled,
                FlashFrequency = settings.FlashFrequency,
                SimultaneousImages = settings.SimultaneousImages,
                HydraLimit = settings.HydraLimit,
                ImageScale = settings.ImageScale,
                FlashOpacity = settings.FlashOpacity,
                FadeDuration = settings.FadeDuration,
                FlashClickable = settings.FlashClickable,
                CorruptionMode = settings.CorruptionMode,

                // Video
                MandatoryVideosEnabled = settings.MandatoryVideosEnabled,
                VideosPerHour = settings.VideosPerHour,
                StrictLockEnabled = settings.StrictLockEnabled,

                // Attention Checks
                AttentionChecksEnabled = settings.AttentionChecksEnabled,
                AttentionDensity = settings.AttentionDensity,
                AttentionLifespan = settings.AttentionLifespan,
                AttentionSize = settings.AttentionSize,

                // Subliminal
                SubliminalEnabled = settings.SubliminalEnabled,
                SubliminalFrequency = settings.SubliminalFrequency,
                SubliminalDuration = settings.SubliminalDuration,
                SubliminalOpacity = settings.SubliminalOpacity,

                // Audio
                SubAudioEnabled = settings.SubAudioEnabled,
                SubAudioVolume = settings.SubAudioVolume,
                MasterVolume = settings.MasterVolume,
                AudioDuckingEnabled = settings.AudioDuckingEnabled,
                DuckingLevel = settings.DuckingLevel,

                // Overlays
                SpiralEnabled = settings.SpiralEnabled,
                SpiralOpacity = settings.SpiralOpacity,
                PinkFilterEnabled = settings.PinkFilterEnabled,
                PinkFilterOpacity = settings.PinkFilterOpacity,

                // Bubbles
                BubblesEnabled = settings.BubblesEnabled,
                BubblesFrequency = settings.BubblesFrequency,

                // Lock Card
                LockCardEnabled = settings.LockCardEnabled,
                LockCardFrequency = settings.LockCardFrequency,
                LockCardRepeats = settings.LockCardRepeats,
                LockCardStrict = settings.LockCardStrict,

                // System
                DualMonitorEnabled = settings.DualMonitorEnabled,
                PanicKeyEnabled = settings.PanicKeyEnabled
            };
        }
    }
}
