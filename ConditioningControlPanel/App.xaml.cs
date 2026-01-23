using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
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
        private static bool _mutexOwned = false;
        private const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";

        /// <summary>
        /// User data folder path in LocalAppData - persists across updates
        /// </summary>
        public static string UserDataPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel");

        /// <summary>
        /// User assets folder path - for user-added content that persists across updates
        /// </summary>
        public static string UserAssetsPath => Path.Combine(UserDataPath, "assets");

        /// <summary>
        /// Effective assets path - returns custom path if set, otherwise default UserAssetsPath.
        /// Use this for all asset loading (images, videos).
        /// </summary>
        public static string EffectiveAssetsPath
        {
            get
            {
                var customPath = Settings?.Current?.CustomAssetsPath;
                if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                {
                    return customPath;
                }
                return UserAssetsPath;
            }
        }

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
        public static LeaderboardService Leaderboard { get; private set; } = null!;
        public static HapticService Haptics { get; private set; } = null!;
        public static DiscordRichPresenceService DiscordRpc { get; private set; } = null!;
        public static DiscordService Discord { get; private set; } = null!;
        public static DualMonitorVideoService DualMonitorVideo { get; private set; } = null!;
        public static ScreenMirrorService ScreenMirror { get; private set; } = null!;
        public static AutonomyService Autonomy { get; private set; } = null!;
        public static InteractionQueueService InteractionQueue { get; private set; } = null!;
        public static ContentPackService ContentPacks { get; private set; } = null!;
        public static CompanionService Companion { get; private set; } = null!;
        public static CommunityPromptService CommunityPrompts { get; private set; } = null!;

        /// <summary>
        /// Whether user is logged in with either Patreon or Discord (required for progression tracking)
        /// </summary>
        public static bool IsLoggedIn => (Patreon?.IsAuthenticated == true) || (Discord?.IsAuthenticated == true);

        /// <summary>
        /// Get the user's display name (from Patreon or Discord)
        /// </summary>
        public static string? UserDisplayName =>
            Patreon?.DisplayName ?? Discord?.CustomDisplayName ?? Discord?.DisplayName;

        /// <summary>
        /// Reference to the avatar companion window (set by MainWindow)
        /// </summary>
        public static AvatarTubeWindow? AvatarWindow { get; set; }

        // Screen enumeration cache
        private static System.Windows.Forms.Screen[]? _cachedScreens;
        private static DateTime _screenCacheTime = DateTime.MinValue;
        private static readonly TimeSpan ScreenCacheDuration = TimeSpan.FromSeconds(5);
        private static readonly object _screenCacheLock = new();

        /// <summary>
        /// Gets all screens with caching to reduce expensive Win32 calls.
        /// Cache is valid for 5 seconds - long enough to avoid repeated calls in tight loops,
        /// short enough to detect monitor changes.
        /// </summary>
        public static System.Windows.Forms.Screen[] GetAllScreensCached()
        {
            lock (_screenCacheLock)
            {
                if (_cachedScreens == null || DateTime.Now - _screenCacheTime > ScreenCacheDuration)
                {
                    try
                    {
                        _cachedScreens = System.Windows.Forms.Screen.AllScreens;
                        _screenCacheTime = DateTime.Now;
                        Logger?.Debug("Screen enumeration: {Count} monitors detected: {Names}",
                            _cachedScreens.Length,
                            string.Join(", ", _cachedScreens.Select(s => $"{s.DeviceName} ({s.Bounds.Width}x{s.Bounds.Height})")));
                    }
                    catch (Exception ex)
                    {
                        Logger?.Debug("Failed to enumerate screens: {Error}", ex.Message);
                        // Return empty array if enumeration fails (can happen during certain system states)
                        return _cachedScreens ?? Array.Empty<System.Windows.Forms.Screen>();
                    }
                }
                return _cachedScreens ?? Array.Empty<System.Windows.Forms.Screen>();
            }
        }

        /// <summary>
        /// Invalidates the screen cache, forcing the next call to re-enumerate.
        /// Call this when monitor configuration might have changed.
        /// </summary>
        public static void InvalidateScreenCache()
        {
            lock (_screenCacheLock)
            {
                _cachedScreens = null;
                _screenCacheTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Flag to indicate if an update dialog is currently being shown.
        /// Used to delay tutorial until update is handled.
        /// </summary>
        public static bool IsUpdateDialogActive { get; set; } = false;

        /// <summary>
        /// Flag to prevent concurrent update checks
        /// </summary>
        private static bool _isCheckingForUpdates = false;

        /// <summary>
        /// Immediately kills ALL audio and visual effects across all services.
        /// Used for panic exit and application shutdown to ensure clean state.
        /// </summary>
        public static void KillAllAudio()
        {
            try
            {
                // Stop subliminal whispers
                Subliminal?.Stop();

                // Stop flash sounds and images
                Flash?.Stop();

                // Stop mind wipe audio
                MindWipe?.Stop();

                // Stop brain drain audio
                BrainDrain?.Stop();

                // Stop video audio (closes video windows)
                Video?.Stop();

                // Stop bubble pop sounds and visuals
                Bubbles?.Stop();

                // Stop bubble count game
                BubbleCount?.Stop();

                // Stop bouncing text overlay
                BouncingText?.Stop();

                // Stop all visual overlays (spiral, pink filter, etc.)
                Overlay?.Stop();

                // Stop lock card if active
                LockCard?.Stop();

                // Stop autonomy mode
                Autonomy?.Stop();

                // Reset audio ducking - CRITICAL for clean exit
                Audio?.Unduck();

                Logger?.Debug("KillAllAudio: All audio and effects stopped");
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error in KillAllAudio");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for single instance
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _mutexOwned = createdNew; // Track if we actually own the mutex
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

            // Show splash screen for loading progress
            var splash = new SplashScreen();
            splash.Show();
            splash.SetProgress(0.05, "Initializing...");

            // Force the splash to render before continuing with heavy initialization
            // Without this, the UI thread may be blocked before the splash actually displays
            splash.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

            // Global exception handlers to catch and log crashes instead of hard crashing
            bool errorDialogShown = false;
            DispatcherUnhandledException += (s, args) =>
            {
                LogCrashDetails("DISPATCHER", args.Exception);

                // Check for rendering thread failure - this is unrecoverable and can cause dialog loops
                var isRenderFailure = args.Exception.Message.Contains("RENDER") ||
                                      args.Exception.Message.Contains("0x88980406") ||
                                      args.Exception.HResult == unchecked((int)0x88980406);

                // Only show error dialog once to prevent multiplying dialogs
                if (!errorDialogShown)
                {
                    errorDialogShown = true;
                    try
                    {
                        MessageBox.Show($"An error occurred:\n\n{args.Exception.Message}\n\nDetails logged to crash log.",
                            "Error - Please report this", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { /* MessageBox may fail during shutdown */ }

                    // For render failures, exit gracefully after showing error once
                    if (isRenderFailure)
                    {
                        Logger?.Error("Render thread failure detected - shutting down to prevent cascading errors");
                        Environment.Exit(1);
                    }
                }

                args.Handled = true; // Prevent crash, just log
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogCrashDetails("DOMAIN", ex);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogCrashDetails("TASK", args.Exception);
                args.SetObserved();
            };

            // Clean up old update packages in background (don't block startup)
            _ = Task.Run(() =>
            {
                try
                {
                    UpdateService.CleanupOldPackages();
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Background cleanup of old packages failed");
                }
            });

            splash.SetProgress(0.1, "Creating directories...");

            // Create user assets directories in LocalAppData (persists across updates)
            Directory.CreateDirectory(Path.Combine(UserAssetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(UserAssetsPath, "videos"));
            Directory.CreateDirectory(Path.Combine(UserDataPath, "Spirals"));

            // Migrate assets from old location (install dir) to new location (user data)
            MigrateAssetsToUserFolder();

            // Create Resources directories (these are bundled with app, not user content)
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            Directory.CreateDirectory(resourcesPath);
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sub_audio"));
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sounds", "mindwipe"));

            splash.SetProgress(0.2, "Loading settings...");

            // Initialize services
            Settings = new SettingsService();

            // Check if installer set an assets path in registry
            ApplyInstallerAssetsPath();

            splash.SetProgress(0.3, "Initializing audio...");
            Audio = new AudioService();

            splash.SetProgress(0.4, "Initializing flash service...");
            Flash = new FlashService();

            splash.SetProgress(0.5, "Initializing video service...");
            Video = new VideoService();

            splash.SetProgress(0.6, "Initializing effects...");
            Progression = new ProgressionService();

            // Initialize companion leveling system (v5.3) - migrate existing users if needed
            CompanionService.MigrateFromLegacy(Settings.Current);
            Companion = new CompanionService();
            CommunityPrompts = new CommunityPromptService();

            Subliminal = new SubliminalService();
            Overlay = new OverlayService();
            Bubbles = new BubbleService();
            InteractionQueue = new InteractionQueueService();
            LockCard = new LockCardService();
            BubbleCount = new BubbleCountService();
            BouncingText = new BouncingTextService();
            MindWipe = new MindWipeService();
            BrainDrain = new BrainDrainService();

            splash.SetProgress(0.75, "Loading achievements...");
            Achievements = new AchievementService();
            Tutorial = new TutorialService();

            splash.SetProgress(0.85, "Initializing companion...");
            Ai = new AiService();
            WindowAwareness = new WindowAwarenessService();
            Patreon = new PatreonService();
            ProfileSync = new ProfileSyncService();
            Leaderboard = new LeaderboardService();
            Haptics = new HapticService(Settings.Current.Haptics);

            // Initialize Discord Rich Presence
            DiscordRpc = new DiscordRichPresenceService();
            if (Settings.Current.DiscordRichPresenceEnabled)
            {
                DiscordRpc.IsEnabled = true;
            }

            // Initialize Discord OAuth service
            Discord = new DiscordService();

            // Initialize dual monitor video service for Hypnotube playback
            DualMonitorVideo = new DualMonitorVideoService();
            ScreenMirror = new ScreenMirrorService();

            // Initialize autonomy service (companion autonomous behavior - Level 100+)
            Autonomy = new AutonomyService();

            // Initialize content packs service
            ContentPacks = new ContentPackService();

            // Initialize Patreon (validate subscription in background)
            // Then load cloud profile if authenticated
            _ = InitializePatreonAndSyncAsync();

            // Initialize Discord OAuth (validate session in background)
            _ = InitializeDiscordAsync();

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

            splash.SetProgress(0.95, "Opening main window...");

            // Show main window
            var mainWindow = new MainWindow();
            mainWindow.Show();

            // Close splash screen with fade animation
            splash.SetProgress(1.0, "Ready!");
            splash.FadeOutAndClose();
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

            // Send Discord webhook if enabled (fire and forget)
            if (Settings?.Current?.DiscordShareAchievements == true)
            {
                var displayName = Settings.Current.DiscordUseAnonymousName
                    ? (Patreon?.DisplayName ?? Discord?.DisplayName ?? "Someone")
                    : (Discord?.Username ?? Patreon?.DisplayName ?? "Someone");
                _ = Discord?.SendAchievementWebhookAsync(achievement, displayName);
            }
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

                // If authenticated, load cloud profile and start heartbeat
                if (Patreon.IsAuthenticated)
                {
                    Logger?.Information("Patreon authenticated, loading cloud profile...");
                    await ProfileSync.LoadProfileAsync();
                    ProfileSync.StartHeartbeat();
                }

                // Start autonomy service if it should be enabled
                // (might have been skipped during LoadSettings if whitelist wasn't loaded yet)
                var s = Settings?.Current;
                if (s != null && s.AutonomyModeEnabled && s.AutonomyConsentGiven && s.PlayerLevel >= 100)
                {
                    var hasPatreonAccess = s.PatreonTier >= 1 || Patreon?.IsWhitelisted == true;
                    if (hasPatreonAccess && Autonomy?.IsEnabled != true)
                    {
                        Autonomy?.Start();
                        Logger?.Information("Started autonomy service after Patreon validation");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize Patreon and sync profile");
            }
        }

        /// <summary>
        /// Initialize Discord OAuth and validate session
        /// </summary>
        private async Task InitializeDiscordAsync()
        {
            try
            {
                await Discord.InitializeAsync();

                if (Discord.IsAuthenticated)
                {
                    Logger?.Information("Discord authenticated: {Username} ({Id})", Discord.Username, Discord.UserId);
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize Discord");
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

                Logger?.Information("Background update check starting...");
                var updateInfo = await Update.CheckForUpdatesAsync();
                Logger?.Information("Background update check completed, IsNewer={IsNewer}", updateInfo?.IsNewer);

                if (updateInfo?.IsNewer == true)
                {
                    // First, show the update button immediately (this always works)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var mainWindow = Application.Current.MainWindow as MainWindow;
                            if (mainWindow != null)
                            {
                                var btn = mainWindow.FindName("BtnUpdateAvailable") as System.Windows.Controls.Button;
                                if (btn != null)
                                {
                                    btn.Tag = "UpdateAvailable";
                                    btn.Content = "UPDATE";
                                    btn.ToolTip = "Update Available - Click to install!";
                                    Logger?.Information("Update button configured successfully");
                                }
                            }
                        }
                        catch (Exception btnEx)
                        {
                            Logger?.Warning(btnEx, "Failed to configure update button");
                        }
                    });

                    // Wait for any startup dialogs (What's New) to be dismissed
                    // Check every 500ms for up to 30 seconds
                    Logger?.Information("Waiting for startup dialogs to close before showing update popup...");
                    for (int i = 0; i < 60; i++)
                    {
                        if (!ConditioningControlPanel.MainWindow.IsStartupDialogShowing)
                        {
                            Logger?.Information("No startup dialog showing, proceeding with update popup");
                            break;
                        }
                        Logger?.Information("Startup dialog still showing, waiting... ({Attempt}/60)", i + 1);
                        await Task.Delay(500);
                    }

                    // Additional small delay after dialog closes to let UI settle
                    await Task.Delay(500);

                    // Now show the update dialog on UI thread
                    Logger?.Information("Attempting to show update dialog on UI thread...");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Double-check no modal dialog is showing
                            if (ConditioningControlPanel.MainWindow.IsStartupDialogShowing)
                            {
                                Logger?.Warning("Startup dialog still showing after wait, skipping auto-popup");
                                return;
                            }

                            Logger?.Information("Inside Dispatcher.Invoke - getting MainWindow");
                            var mainWindow = Application.Current.MainWindow as MainWindow;

                            if (mainWindow == null)
                            {
                                Logger?.Warning("MainWindow is null, cannot show update dialog");
                                return;
                            }

                            Logger?.Information("MainWindow found, IsLoaded={IsLoaded}, IsVisible={IsVisible}",
                                mainWindow.IsLoaded, mainWindow.IsVisible);

                            // Show the update notification dialog
                            Logger?.Information("Calling ShowUpdateNotification...");
                            ShowUpdateNotification(updateInfo, mainWindow);
                            Logger?.Information("ShowUpdateNotification returned");
                        }
                        catch (Exception innerEx)
                        {
                            Logger?.Error(innerEx, "Exception inside Dispatcher.Invoke for update dialog");
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

                // Check if this is a major update requiring fresh install
                var requiresFreshInstall = Update?.RequiresFreshInstall == true;
                Logger?.Information("Update requires fresh install: {RequiresFreshInstall}", requiresFreshInstall);

                // Ensure owner window is active and in foreground
                owner.Activate();
                owner.Focus();

                bool installRequested;

                if (requiresFreshInstall)
                {
                    // Show special warning for fresh install updates
                    var result = MessageBox.Show(
                        owner,
                        $"Version {updateInfo.Version} is a major update that requires a fresh installation.\n\n" +
                        "What this means:\n" +
                        "• The installer will run and let you choose your install location\n" +
                        "• Your assets folder (images, videos) will NOT be touched\n" +
                        "• Your settings and progress will be preserved\n\n" +
                        "The app will close and the installer will start automatically.\n\n" +
                        "Would you like to update now?",
                        "Major Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    installRequested = result == MessageBoxResult.Yes;
                }
                else
                {
                    var dialog = new UpdateNotificationDialog(updateInfo)
                    {
                        Owner = owner,
                        Topmost = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    // Activate dialog when it's loaded to ensure it's visible
                    dialog.Loaded += (s, e) =>
                    {
                        dialog.Activate();
                        dialog.Focus();
                    };

                    installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;
                }

                Logger?.Information("Update dialog closed, install requested: {InstallRequested}", installRequested);

                if (installRequested)
                {
                    // Keep flag active during download
                    if (requiresFreshInstall)
                    {
                        DownloadAndRunInstallerAsync(owner);
                    }
                    else
                    {
                        DownloadAndInstallUpdateAsync(owner);
                    }
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
        /// Download the installer and run it for fresh install updates (5.1+)
        /// </summary>
        private async void DownloadAndRunInstallerAsync(Window owner)
        {
            UpdateProgressDialog? progressDialog = null;
            EventHandler<int>? progressHandler = null;

            try
            {
                Logger?.Information("Starting fresh install update - downloading installer...");

                // Hide the main window during update for cleaner experience
                var mainWindow = Current.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Hide();
                    Logger?.Information("Main window hidden for update");
                }

                // Also hide the avatar tube window if it exists
                try
                {
                    var avatarWindow = Current.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "AvatarTubeWindow");
                    avatarWindow?.Hide();
                }
                catch { }

                progressDialog = new UpdateProgressDialog();
                progressDialog.Topmost = true;
                progressDialog.Show();

                await Task.Delay(100);

                progressHandler = (s, progress) =>
                {
                    try
                    {
                        var dialog = progressDialog;
                        if (dialog == null) return;

                        dialog.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                if (dialog.IsVisible)
                                {
                                    dialog.SetProgress(progress);
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                };

                Update.DownloadProgressChanged += progressHandler;

                var installerPath = await Update.DownloadInstallerAsync();

                progressDialog.Close();
                progressDialog = null;

                if (string.IsNullOrEmpty(installerPath))
                {
                    throw new InvalidOperationException("Failed to download installer");
                }

                // Check if this is an Inno Setup installation - if so, use silent update
                var isInnoSetupInstall = UpdateService.IsInstalledViaInstaller;

                if (isInnoSetupInstall)
                {
                    // Silent update for Inno Setup installations
                    var result = MessageBox.Show(
                        owner,
                        "Update downloaded successfully!\n\n" +
                        "The app will now close and update automatically.\n" +
                        "It will restart when complete.\n\n" +
                        "Continue?",
                        "Ready to Update",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Logger?.Information("Starting silent update for Inno Setup installation");
                        Update.RunInstallerSilentlyAndExit(installerPath);
                    }
                    else
                    {
                        // User cancelled - restore windows
                        RestoreHiddenWindows();
                    }
                }
                else
                {
                    // Fresh install flow - show installer UI
                    var result = MessageBox.Show(
                        owner,
                        "Installer downloaded successfully.\n\n" +
                        "The app will now close and the installer will start.\n" +
                        "Please follow the installer prompts to complete the update.\n\n" +
                        "Continue?",
                        "Ready to Install",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Update.RunInstallerAndExit(installerPath);
                    }
                    else
                    {
                        // User cancelled - restore windows
                        RestoreHiddenWindows();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to download installer for fresh install");

                try { progressDialog?.Close(); } catch { }

                // Restore the main window if update failed
                RestoreHiddenWindows();

                // Show error message - handle null owner
                if (owner != null && owner.IsLoaded)
                {
                    MessageBox.Show(
                        owner,
                        $"Failed to download installer: {ex.Message}",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to download installer: {ex.Message}",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                if (progressHandler != null)
                {
                    Update.DownloadProgressChanged -= progressHandler;
                }
                IsUpdateDialogActive = false;
            }
        }

        /// <summary>
        /// Restores windows that were hidden during the update process.
        /// </summary>
        private void RestoreHiddenWindows()
        {
            try
            {
                var mainWindow = Current.MainWindow;
                if (mainWindow != null && !mainWindow.IsVisible)
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                }
                var avatarWindow = Current.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "AvatarTubeWindow");
                avatarWindow?.Show();
                Logger?.Information("Restored hidden windows after update cancelled/failed");
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Failed to restore hidden windows");
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
                // Force check bypasses the 24-hour skip logic since user manually requested
                var updateInfo = await Update.CheckForUpdatesAsync(forceCheck: true);

                if (updateInfo?.IsNewer == true)
                {
                    IsUpdateDialogActive = true;

                    // Check if this is a major update requiring fresh install
                    var requiresFreshInstall = Update?.RequiresFreshInstall == true;

                    // Ensure owner window is active and in foreground
                    owner.Activate();
                    owner.Focus();

                    bool installRequested;

                    if (requiresFreshInstall)
                    {
                        // Show special warning for fresh install updates
                        var result = MessageBox.Show(
                            owner,
                            $"Version {updateInfo.Version} is a major update that requires a fresh installation.\n\n" +
                            "What this means:\n" +
                            "• The installer will run and let you choose your install location\n" +
                            "• Your assets folder (images, videos) will NOT be touched\n" +
                            "• Your settings and progress will be preserved\n\n" +
                            "The app will close and the installer will start automatically.\n\n" +
                            "Would you like to update now?",
                            "Major Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        installRequested = result == MessageBoxResult.Yes;
                    }
                    else
                    {
                        var dialog = new UpdateNotificationDialog(updateInfo)
                        {
                            Owner = owner,
                            Topmost = true,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        // Activate dialog when it's loaded to ensure it's visible
                        dialog.Loaded += (s, e) =>
                        {
                            dialog.Activate();
                            dialog.Focus();
                        };

                        installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;
                    }

                    if (installRequested)
                    {
                        // Use installer download for:
                        // 1. Major updates requiring fresh install (pre-5.1 to 5.1+)
                        // 2. Updates detected via GitHub API fallback (Velopack couldn't find them)
                        // 3. Inno Setup installations (Velopack not available)
                        var useInstallerDownload = requiresFreshInstall ||
                                                   updateInfo.IsGitHubFallback ||
                                                   (UpdateService.IsInstalledViaInstaller && Update?.IsInstalled == false);

                        if (useInstallerDownload)
                        {
                            Logger?.Information("Using installer download flow (requiresFreshInstall={Fresh}, isGitHubFallback={Fallback}, isInnoSetup={Inno})",
                                requiresFreshInstall, updateInfo.IsGitHubFallback, UpdateService.IsInstalledViaInstaller && Update?.IsInstalled == false);
                            ((App)Current).DownloadAndRunInstallerAsync(owner);
                        }
                        else
                        {
                            ((App)Current).DownloadAndInstallUpdateAsync(owner);
                        }
                    }
                    else
                    {
                        IsUpdateDialogActive = false;
                    }
                    return true;
                }
                else
                {
                    // Check if server banner indicated an update but our check failed
                    // This can happen with Inno Setup installations or network issues
                    var mainWindow = owner as MainWindow;
                    var serverIndicatedUpdate = mainWindow?.BtnUpdateAvailable?.Tag?.ToString() == "UrgentUpdate";

                    if (serverIndicatedUpdate)
                    {
                        // Offer to open releases page as fallback
                        Logger?.Warning("Update check returned no update, but server banner indicated update available. Offering browser fallback.");
                        var result = MessageBox.Show(
                            owner,
                            "The automatic update check couldn't find the update, but our server indicates a new version is available.\n\n" +
                            "This can happen with certain installation types. Would you like to open the releases page to download manually?\n\n" +
                            "After this update, automatic updates should work normally.",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger?.Error(ex, "Failed to open releases page");
                            }
                        }
                        return false;
                    }

                    // Hide the update button since we're on latest
                    mainWindow?.ShowUpdateAvailableButton(false);

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

                // Even on error, check if server indicated an update and offer browser fallback
                var mainWindow = owner as MainWindow;
                var serverIndicatedUpdate = mainWindow?.BtnUpdateAvailable?.Tag?.ToString() == "UrgentUpdate";

                if (serverIndicatedUpdate)
                {
                    var result = MessageBox.Show(
                        owner,
                        $"Update check failed: {ex.Message}\n\n" +
                        "However, our server indicates a new version is available. Would you like to open the releases page to download manually?",
                        "Update Check Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                    return false;
                }

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
                SystemSounds.Asterisk.Play();
            }
            catch
            {
                // Ignore if sound fails
            }
        }

        /// <summary>
        /// Log detailed crash information to both main log and a dedicated crash log file.
        /// This helps debug random crashes by capturing full context.
        /// </summary>
        private static void LogCrashDetails(string source, Exception? ex)
        {
            if (ex == null) return;

            try
            {
                // Log to main logger
                Logger?.Error(ex, "UNHANDLED {Source} EXCEPTION: {Message}", source, ex.Message);

                // Also write to dedicated crash log with full details
                var crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "crash.log");
                var crashInfo = $@"
================================================================================
CRASH REPORT - {DateTime.Now:yyyy-MM-dd HH:mm:ss}
================================================================================
Source: {source}
Exception Type: {ex.GetType().FullName}
Message: {ex.Message}

Stack Trace:
{ex.StackTrace}

Inner Exception: {(ex.InnerException != null ? ex.InnerException.Message : "None")}
{(ex.InnerException?.StackTrace != null ? $"Inner Stack Trace:\n{ex.InnerException.StackTrace}" : "")}

Application State:
- IsRunning: {Current != null}
- Dispatcher Shutdown: {(Current?.Dispatcher?.HasShutdownStarted ?? true)}
================================================================================
";
                File.AppendAllText(crashLogPath, crashInfo);
            }
            catch
            {
                // Can't log the crash - last resort
            }
        }

        /// <summary>
        /// Migrate user assets from old install directory location to persistent user data folder.
        /// This ensures user content survives app updates.
        /// </summary>
        private static void MigrateAssetsToUserFolder()
        {
            try
            {
                var migratedCount = 0;

                // 1. Migrate from current app directory (standard migration)
                migratedCount += MigrateAssetsFromPath(AppDomain.CurrentDomain.BaseDirectory);

                // 2. Also check old version folders in the Velopack app root
                // This rescues assets from old app-X.X.X folders that might still exist
                // Critical for users updating from versions that stored assets in the app folder
                try
                {
                    var appRoot = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    if (!string.IsNullOrEmpty(appRoot))
                    {
                        foreach (var dir in Directory.GetDirectories(appRoot, "app-*"))
                        {
                            // Skip current directory to avoid double-processing
                            if (dir.Equals(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                                continue;

                            migratedCount += MigrateAssetsFromPath(dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Debug("Could not check old version folders: {Error}", ex.Message);
                }

                if (migratedCount > 0)
                {
                    Logger?.Information("Migrated {Count} asset files to user data folder", migratedCount);
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Asset migration failed");
            }
        }

        /// <summary>
        /// Migrates assets from a specific path (old app directory or old version folder).
        /// Returns the number of files migrated.
        /// </summary>
        private static int MigrateAssetsFromPath(string basePath)
        {
            var migratedCount = 0;

            try
            {
                var oldAssetsPath = Path.Combine(basePath, "assets");

                // Map old folder names to new folder names (startle_videos -> videos)
                var foldersToMigrate = new[] { ("images", "images"), ("startle_videos", "videos"), ("videos", "videos") };

                if (Directory.Exists(oldAssetsPath))
                {
                    foreach (var (oldName, newName) in foldersToMigrate)
                    {
                        var oldFolder = Path.Combine(oldAssetsPath, oldName);
                        var newFolder = Path.Combine(UserAssetsPath, newName);

                        if (!Directory.Exists(oldFolder)) continue;

                        Directory.CreateDirectory(newFolder);

                        foreach (var file in Directory.GetFiles(oldFolder))
                        {
                            var fileName = Path.GetFileName(file);
                            var destFile = Path.Combine(newFolder, fileName);

                            // Don't overwrite existing files in user folder
                            if (File.Exists(destFile)) continue;

                            try
                            {
                                File.Copy(file, destFile);
                                migratedCount++;
                                Logger?.Debug("Migrated asset: {File} from {Source}", fileName, basePath);
                            }
                            catch (Exception ex)
                            {
                                Logger?.Warning("Failed to migrate {File}: {Error}", fileName, ex.Message);
                            }
                        }
                    }
                }

                // Also migrate Spirals folder
                var oldSpirals = Path.Combine(basePath, "Spirals");
                var newSpirals = Path.Combine(UserDataPath, "Spirals");
                if (Directory.Exists(oldSpirals))
                {
                    Directory.CreateDirectory(newSpirals);

                    foreach (var file in Directory.GetFiles(oldSpirals))
                    {
                        var fileName = Path.GetFileName(file);
                        var destFile = Path.Combine(newSpirals, fileName);
                        if (!File.Exists(destFile))
                        {
                            try
                            {
                                File.Copy(file, destFile);
                                migratedCount++;
                                Logger?.Debug("Migrated spiral: {File} from {Source}", fileName, basePath);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Debug("Could not migrate from {Path}: {Error}", basePath, ex.Message);
            }

            return migratedCount;
        }

        /// <summary>
        /// Check if the installer set a custom assets path in the registry and apply it.
        /// This allows users to confirm/change their assets folder during installation.
        /// </summary>
        private static void ApplyInstallerAssetsPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel", writable: true);
                if (key == null) return;

                var installerAssetsPath = key.GetValue("AssetsPath") as string;
                if (string.IsNullOrWhiteSpace(installerAssetsPath)) return;

                // Check if this path differs from default and exists
                if (Directory.Exists(installerAssetsPath))
                {
                    var defaultPath = UserAssetsPath;

                    // If the installer-selected path is different from default, apply it
                    // But only if settings don't already have a custom path set
                    if (!string.Equals(installerAssetsPath, defaultPath, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(Settings?.Current?.CustomAssetsPath))
                    {
                        if (Settings?.Current != null)
                        {
                            Settings.Current.CustomAssetsPath = installerAssetsPath;
                            Settings.Save();
                            Logger?.Information("Applied installer assets path: {Path}", installerAssetsPath);
                        }
                    }
                }

                // Remove the registry value after processing (one-time operation)
                key.DeleteValue("AssetsPath", throwOnMissingValue: false);
                Logger?.Debug("Cleared installer AssetsPath registry value");
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Failed to apply installer assets path from registry");
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
            Leaderboard?.Dispose();
            DiscordRpc?.Dispose();
            Discord?.Dispose();
            DualMonitorVideo?.Dispose();
            ScreenMirror?.Dispose();
            Autonomy?.Dispose();
            ContentPacks?.Dispose();
            Audio?.Dispose();
            Settings?.Save();

            // Close and flush the logger
            Log.CloseAndFlush();

            // Release single instance mutex (only if we own it)
            if (_mutexOwned && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by this thread - ignore
                }
            }
            _mutex?.Dispose();

            base.OnExit(e);

            // Force exit to ensure no background threads keep process alive
            Environment.Exit(0);
        }
    }
}
