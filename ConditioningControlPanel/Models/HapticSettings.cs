using System.ComponentModel;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Haptics;

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

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
