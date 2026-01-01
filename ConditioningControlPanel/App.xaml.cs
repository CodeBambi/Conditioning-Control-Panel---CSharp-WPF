using System;
using System.IO;
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
        public static AchievementService Achievements { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup logging
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logPath);
            
            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(logPath, "app-.log"), 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            Logger.Information("Application starting...");

            // Create assets directories
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "sounds"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "startle_videos"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "backgrounds"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "sub_audio"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "mindwipe"));
            
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
            Achievements = new AchievementService();
            
            // Wire up achievement popup BEFORE checking initial achievements
            Achievements.AchievementUnlocked += OnAchievementUnlocked;
            
            // Now check level achievements (so popup can show)
            Achievements.CheckLevelAchievements(Settings.Current.PlayerLevel);
            Logger.Information("Checked level achievements for level {Level}", Settings.Current.PlayerLevel);

            Logger.Information("Services initialized");

            // Show main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        
        private void OnAchievementUnlocked(object? sender, Models.Achievement achievement)
        {
            Logger.Information("OnAchievementUnlocked handler called for: {Name}", achievement.Name);
            
            // Show achievement popup
            var popup = new AchievementPopup(achievement);
            popup.Show();
            
            Logger.Information("Achievement popup shown for: {Name}", achievement.Name);
            
            // Play achievement sound if available
            try
            {
                var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "achievement.wav");
                if (File.Exists(soundPath))
                {
                    var player = new System.Media.SoundPlayer(soundPath);
                    player.Play();
                }
            }
            catch { /* Ignore sound errors */ }
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
            Achievements?.Dispose();
            Audio?.Dispose();
            Settings?.Save();

            base.OnExit(e);
        }
    }
}
