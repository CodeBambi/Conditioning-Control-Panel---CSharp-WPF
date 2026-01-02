using System;
using System.IO;
using System.Media;
using System.Windows;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    public partial class App : Application
    {
        // Static service references
        public static ILogger Logger { get; private set; } = null!;
        public static SettingsService Settings { get; private set; } = null!;
        public static FlashService Flash { get; private set; } = null!;
        public static VideoService Video { get; private set; } = null!;
        public static AudioService Audio { get; private set; } = null!;
        public static ProgressionService Progression { get; private set; } = null!;
        public static SubliminalService Subliminal { get; private set; } = null!;
        public static OverlayService Overlay { get; private set; } = null!;
        public static BubbleService Bubbles { get; private set; } = null!;
        public static LockCardService LockCard { get; private set; } = null!;
        public static BubbleCountService BubbleCount { get; private set; } = null!;
        public static BouncingTextService BouncingText { get; private set; } = null!;
        public static MindWipeService MindWipe { get; private set; } = null!;
        public static BrainDrainService BrainDrain { get; private set; } = null!;
        public static AchievementService Achievements { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup logging
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logPath);
            
            Logger = new LoggerConfiguration()
                .MinimumLevel.Information() // Security: Changed from Debug to avoid exposing sensitive data in logs
                .WriteTo.File(Path.Combine(logPath, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            Logger.Information("Application starting...");

            // Create assets directories
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            Directory.CreateDirectory(resourcesPath); // Ensure Resources folder exists

            Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "sounds"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "startle_videos"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "backgrounds"));
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sub_audio"));
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sounds", "mindwipe"));
            
            // Create Spirals directory for random spiral selection
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Spirals"));

            // Initialize services
            Settings = new SettingsService();
            Audio = new AudioService();
            Flash = new FlashService();
            Video = new VideoService();
            Progression = new ProgressionService();
            Subliminal = new SubliminalService();
            Overlay = new OverlayService();
            Bubbles = new BubbleService();
            LockCard = new LockCardService();
            BubbleCount = new BubbleCountService();
            BouncingText = new BouncingTextService();
            MindWipe = new MindWipeService();
            BrainDrain = new BrainDrainService();
            Achievements = new AchievementService();
            
            // Wire up achievement popup BEFORE checking any achievements
            Achievements.AchievementUnlocked += OnAchievementUnlocked;
            
            // Now check initial achievements (so popup can show)
            Achievements.CheckLevelAchievements(Settings.Current.PlayerLevel);
            Logger.Information("Checked level achievements for level {Level}", Settings.Current.PlayerLevel);
            
            // Check daily maintenance achievement (7 days streak)
            Achievements.CheckDailyMaintenance();
            Logger.Information("Checked daily maintenance achievement");

            Logger.Information("Services initialized");

            // Show main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        
        private void OnAchievementUnlocked(object? sender, Models.Achievement achievement)
        {
            Logger.Information("OnAchievementUnlocked handler called for: {Name}", achievement.Name);
            
            // Show achievement popup
            try
            {
                var popup = new AchievementPopup(achievement);
                popup.Show();
                Logger.Information("Achievement popup shown for: {Name}", achievement.Name);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to show achievement popup for: {Name}", achievement.Name);
            }
            
            // Play achievement sound
            PlayAchievementSound();
        }
        
        /// <summary>
        /// Play the achievement notification sound
        /// </summary>
        private void PlayAchievementSound()
        {
            try
            {
                // First try custom achievement sound
                var customSoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "achievement.wav");
                if (File.Exists(customSoundPath))
                {
                    var player = new SoundPlayer(customSoundPath);
                    player.Play();
                    Logger.Debug("Played custom achievement sound");
                }
                else
                {
                    // Fall back to Windows notification sound (Asterisk = the classic notification "ding")
                    SystemSounds.Asterisk.Play();
                    Logger.Debug("Played Windows notification sound");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to play achievement sound, trying fallback");
                try
                {
                    // Ultimate fallback - Windows exclamation sound
                    SystemSounds.Exclamation.Play();
                }
                catch
                {
                    // Ignore if even fallback fails
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Information("Application shutting down...");
            
            Flash?.Dispose();
            Video?.Dispose();
            Subliminal?.Dispose();
            Overlay?.Dispose();
            Bubbles?.Dispose();
            LockCard?.Dispose();
            BubbleCount?.Dispose();
            BouncingText?.Dispose();
            MindWipe?.Dispose();
            BrainDrain?.Dispose();
            Achievements?.Dispose();
            Audio?.Dispose();
            Settings?.Save();

            base.OnExit(e);
        }
    }
}
