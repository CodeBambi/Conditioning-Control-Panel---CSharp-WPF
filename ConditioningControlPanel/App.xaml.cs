using System;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
using Velopack;

// Alias to avoid ambiguity with Velopack.UpdateInfo
using AppUpdateInfo = ConditioningControlPanel.Models.UpdateInfo;

namespace ConditioningControlPanel
{
    public partial class App : Application
    {
        /// <summary>
        /// Custom entry point required for Velopack auto-updates.
        /// Must call VelopackApp.Build().Run() before WPF Application starts.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            // Velopack: Handle updates before anything else
            // This allows Velopack to process update commands (install, uninstall, etc.)
            VelopackApp.Build().Run();

            // Now start the WPF application normally
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        // Single instance mutex
        private static Mutex? _mutex;
        private const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";

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
        public static TutorialService Tutorial { get; private set; } = null!;
        public static AiService Ai { get; private set; } = null!;
        public static WindowAwarenessService WindowAwareness { get; private set; } = null!;
        public static PatreonService Patreon { get; private set; } = null!;
        public static UpdateService Update { get; private set; } = null!;
        public static ProfileSyncService ProfileSync { get; private set; } = null!;

        /// <summary>
        /// Flag to indicate if an update dialog is currently being shown.
        /// Used to delay tutorial until update is handled.
        /// </summary>
        public static bool IsUpdateDialogActive { get; set; } = false;

        /// <summary>
        /// Flag to prevent concurrent update checks
        /// </summary>
        private static bool _isCheckingForUpdates = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for single instance
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running
                MessageBox.Show(
                    "Conditioning Control Panel is already running.\n\nCheck your system tray if the window is minimized.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

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
            Tutorial = new TutorialService();
            Ai = new AiService();
            WindowAwareness = new WindowAwarenessService();
            Patreon = new PatreonService();
            ProfileSync = new ProfileSyncService();

            // Initialize Patreon (validate subscription in background)
            // Then load cloud profile if authenticated
            _ = InitializePatreonAndSyncAsync();

            // Initialize Update service and check for updates in background
            Update = new UpdateService();
            _ = CheckForUpdatesInBackgroundAsync();

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
        /// Initialize Patreon and load cloud profile if authenticated
        /// </summary>
        private async Task InitializePatreonAndSyncAsync()
        {
            try
            {
                // Initialize Patreon authentication
                await Patreon.InitializeAsync();

                // If authenticated, load cloud profile
                if (Patreon.IsAuthenticated)
                {
                    Logger?.Information("Patreon authenticated, loading cloud profile...");
                    await ProfileSync.LoadProfileAsync();
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize Patreon and sync profile");
            }
        }

        /// <summary>
        /// Check for updates in the background after a short delay
        /// </summary>
        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                // Delay update check to let app fully load
                await Task.Delay(3000);

                var updateInfo = await Update.CheckForUpdatesAsync();

                if (updateInfo?.IsNewer == true)
                {
                    // Show notification and update button via dispatcher
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var mainWindow = Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            // Show the update button in tab bar
                            mainWindow.ShowUpdateAvailableButton(true);

                            // Show the update notification dialog
                            ShowUpdateNotification(updateInfo, mainWindow);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Background update check failed");
                // Silently fail - don't disrupt user
            }
        }

        /// <summary>
        /// Show update notification dialog and handle user response
        /// </summary>
        private void ShowUpdateNotification(AppUpdateInfo updateInfo, Window owner)
        {
            try
            {
                Logger?.Information("Showing update notification dialog for version {Version}", updateInfo.Version);
                IsUpdateDialogActive = true;

                var dialog = new UpdateNotificationDialog(updateInfo)
                {
                    Owner = owner,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;
                Logger?.Information("Update dialog closed, install requested: {InstallRequested}", installRequested);

                if (installRequested)
                {
                    // Keep flag active during download
                    DownloadAndInstallUpdateAsync(owner);
                }
                else
                {
                    // User declined or closed dialog
                    IsUpdateDialogActive = false;
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error showing update notification dialog");
                IsUpdateDialogActive = false;
            }
        }

        /// <summary>
        /// Download and install the update with progress dialog
        /// </summary>
        private async void DownloadAndInstallUpdateAsync(Window owner)
        {
            UpdateProgressDialog? progressDialog = null;
            EventHandler<int>? progressHandler = null;

            try
            {
                // Create and show dialog directly (we're already on UI thread)
                Logger?.Information("Creating progress dialog...");
                progressDialog = new UpdateProgressDialog();
                progressDialog.Topmost = true;
                Logger?.Information("Showing progress dialog...");
                progressDialog.Show();
                Logger?.Information("Progress dialog shown");

                // Allow UI to update
                await Task.Delay(100);

                Logger?.Information("Starting update download...");

                // Create progress handler that safely updates the dialog
                progressHandler = (s, progress) =>
                {
                    try
                    {
                        progressDialog?.Dispatcher.BeginInvoke(() =>
                        {
                            if (progressDialog.IsVisible)
                            {
                                progressDialog.SetProgress(progress);
                            }
                        });
                    }
                    catch
                    {
                        // Ignore if dialog was closed
                    }
                };

                Update.DownloadProgressChanged += progressHandler;

                await Update.DownloadUpdateAsync();

                progressDialog.Close();
                progressDialog = null;

                // Ask user to restart
                var result = MessageBox.Show(
                    owner,
                    "Update downloaded successfully. Restart now to apply the update?",
                    "Update Ready",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Update.ApplyUpdateAndRestart();
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to download update");

                try
                {
                    progressDialog?.Close();
                }
                catch
                {
                    // Ignore close errors
                }

                MessageBox.Show(
                    owner,
                    $"Failed to download update: {ex.Message}",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Always unsubscribe the event handler
                if (progressHandler != null)
                {
                    Update.DownloadProgressChanged -= progressHandler;
                }

                IsUpdateDialogActive = false;
            }
        }

        /// <summary>
        /// Manually check for updates (called from MainWindow)
        /// </summary>
        public static async Task<bool> CheckForUpdatesManuallyAsync(Window owner)
        {
            // Prevent concurrent update checks
            if (_isCheckingForUpdates || IsUpdateDialogActive)
            {
                Logger?.Information("Update check already in progress, skipping");
                return false;
            }

            _isCheckingForUpdates = true;

            try
            {
                var updateInfo = await Update.CheckForUpdatesAsync();

                if (updateInfo?.IsNewer == true)
                {
                    IsUpdateDialogActive = true;

                    var dialog = new UpdateNotificationDialog(updateInfo)
                    {
                        Owner = owner,
                        Topmost = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    var installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;

                    if (installRequested)
                    {
                        ((App)Current).DownloadAndInstallUpdateAsync(owner);
                    }
                    else
                    {
                        IsUpdateDialogActive = false;
                    }
                    return true;
                }
                else
                {
                    // Hide the update button since we're on latest
                    (owner as MainWindow)?.ShowUpdateAvailableButton(false);

                    MessageBox.Show(
                        owner,
                        $"You're running the latest version ({UpdateService.GetCurrentVersion()}).",
                        "No Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Manual update check failed");
                MessageBox.Show(
                    owner,
                    $"Failed to check for updates: {ex.Message}",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
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
            Logger?.Information("Application shutting down...");

            // Sync profile to cloud on exit (fire and forget, don't block shutdown)
            if (ProfileSync?.IsSyncEnabled == true)
            {
                try
                {
                    Logger?.Information("Syncing profile to cloud before exit...");
                    ProfileSync.SyncProfileAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Failed to sync profile on exit");
                }
            }

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
            WindowAwareness?.Dispose();
            Ai?.Dispose();
            Patreon?.Dispose();
            Update?.Dispose();
            ProfileSync?.Dispose();
            Audio?.Dispose();
            Settings?.Save();

            // Close and flush the logger
            Log.CloseAndFlush();

            // Release single instance mutex
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();

            base.OnExit(e);

            // Force exit to ensure no background threads keep process alive
            Environment.Exit(0);
        }
    }
}
