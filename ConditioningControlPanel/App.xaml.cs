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

        protected override void OnStartup(StartupEventArgs e)
        {
            // Global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = (Exception)args.ExceptionObject;
                File.WriteAllText("crash_log.txt", $"FATAL: {ex.Message}\n\n{ex.StackTrace}");
                MessageBox.Show($"Fatal Error:\n{ex.Message}\n\n{ex.StackTrace}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                File.WriteAllText("crash_log.txt", $"UI ERROR: {args.Exception.Message}\n\n{args.Exception.StackTrace}");
                MessageBox.Show($"Error:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            try
            {
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
                Directory.CreateDirectory(Path.Combine(assetsPath, "spirals"));
                Directory.CreateDirectory(Path.Combine(assetsPath, "backgrounds"));
                Directory.CreateDirectory(Path.Combine(assetsPath, "sub_audio"));

                Logger.Information("Directories created");

                // Initialize services
                Settings = new SettingsService();
                Logger.Information("Settings initialized");
                
                Audio = new AudioService();
                Logger.Information("Audio initialized");
                
                Flash = new FlashService();
                Logger.Information("Flash initialized");
                
                Video = new VideoService();
                Logger.Information("Video initialized");
                
                Progression = new ProgressionService();
                Logger.Information("Progression initialized");
                
                Subliminal = new SubliminalService();
                Logger.Information("Subliminal initialized");
                
                Overlay = new OverlayService();
                Logger.Information("Overlay initialized");
                
                Bubbles = new BubbleService();
                Logger.Information("Bubbles initialized");
                
                LockCard = new LockCardService();
                Logger.Information("LockCard initialized");

                Logger.Information("All services initialized");

                // Show main window
                var mainWindow = new MainWindow();
                Logger.Information("MainWindow created");
                mainWindow.Show();
                Logger.Information("MainWindow shown");
            }
            catch (Exception ex)
            {
                File.WriteAllText("crash_log.txt", $"STARTUP ERROR: {ex.Message}\n\n{ex.StackTrace}\n\nInner: {ex.InnerException?.Message}\n{ex.InnerException?.StackTrace}");
                MessageBox.Show($"Startup Error:\n{ex.Message}\n\nInner: {ex.InnerException?.Message}", "Startup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger?.Information("Application shutting down...");
            
            Flash?.Dispose();
            Video?.Dispose();
            Subliminal?.Dispose();
            Overlay?.Dispose();
            Bubbles?.Dispose();
            LockCard?.Dispose();
            Audio?.Dispose();
            Settings?.Save();

            base.OnExit(e);
        }
    }
}
