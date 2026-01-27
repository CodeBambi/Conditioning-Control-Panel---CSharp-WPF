using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class MainWindow : Window
    {
        // DWM API for Windows 11 rounded corners
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        private bool _isRunning = false;
        private bool _isLoading = true;
        private BrowserService? _browser;
        private bool _browserInitialized = false;
        private List<Window> _browserFullscreenWindows = new();
        private Window? _browserPopoutWindow = null;
        private bool _isDualMonitorPlaybackActive = false;
        private TrayIconService? _trayIcon;
        private GlobalKeyboardHook? _keyboardHook;
        private bool _isCapturingPanicKey = false;
        private bool _exitRequested = false;
        private int _panicPressCount = 0;

        /// <summary>
        /// Fires when the engine is stopped (for avatar reactions)
        /// </summary>
        public event EventHandler? EngineStopped;
        private DateTime _lastPanicTime = DateTime.MinValue;

        /// <summary>
        /// Gets the browser WebView2 control for external access (e.g., avatar audio controls)
        /// </summary>
        public Microsoft.Web.WebView2.Wpf.WebView2? GetBrowserWebView() => _browser?.WebView;
        
        // Session Engine
        private SessionEngine? _sessionEngine;
        
        // Avatar Tube Window
        private AvatarTubeWindow? _avatarTubeWindow;
        private bool _avatarWasAttachedBeforeMaximize = false;

        // Auto-pause state when minimized with attached avatar
        private bool _autonomyWasPausedOnMinimize = false;
        private bool _avatarWasMutedOnMinimize = false;
        private bool _wasAutonomyRunningBeforeMinimize = false;
        private bool _wasAvatarUnmutedBeforeMinimize = false;

        // Achievement tracking
        private Dictionary<string, Image> _achievementImages = new();
        
        // Ramp tracking
        private DispatcherTimer? _rampTimer;
        private DateTime _rampStartTime;
        private Dictionary<string, double> _rampBaseValues = new();

        // Easter egg tracking (100 clicks in 60 seconds)
        private int _easterEggClickCount = 0;
        private DateTime _easterEggFirstClick = DateTime.MinValue;
        private bool _easterEggTriggered = false;
        
        // Scheduler tracking
        private DispatcherTimer? _schedulerTimer;
        private bool _schedulerAutoStarted = false;
        private bool _manuallyStoppedDuringSchedule = false;

        // Banner rotation (cycles through 3 messages: support, welcome, thanks)
        private DispatcherTimer? _bannerRotationTimer;
        private int _bannerCurrentIndex = 0; // 0=Primary (support), 1=Secondary (welcome), 2=Tertiary (thanks)
        private List<string> _bannerMessages = new();

        // Marquee animation
        private System.Windows.Media.Animation.Storyboard? _marqueeStoryboard;
        private DispatcherTimer? _marqueeRefreshTimer;
        private string _currentMarqueeMessage = "";

        // Content packs
        private ObservableCollection<ContentPack> _availablePacks = new();
        private DispatcherTimer? _packPreviewTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Set version dynamically from assembly
            var version = Services.UpdateService.GetCurrentVersion();
            TxtVersion.Text = $"Version {version}";
            Title = $"Conditioning Control Panel v{version}";
            TxtTitleBarVersion.Text = $"Conditioning Control Panel v{version}";
            TxtHeaderVersion.Text = $"v{version}";

            // Center on primary monitor
            CenterOnPrimaryScreen();
            
            // Load logo
            LoadLogo();
            
            // Initialize tray icon
            _trayIcon = new TrayIconService(this);
            _trayIcon.OnExitRequested += () =>
            {
                _exitRequested = true;
                if (_isRunning) StopEngine();

                // Kill all audio and effects - ensures clean exit with audio unducked
                App.KillAllAudio();

                // Explicitly dispose overlay
                try
                {
                    App.Overlay?.Dispose();
                }
                catch { }

                SaveSettings();
                Application.Current.Shutdown();
            };
            _trayIcon.OnShowRequested += () =>
            {
                ShowAvatarTube();
            };
            _trayIcon.OnWakeBambiRequested += () =>
            {
                WakeBambiUp();
            };

            // Initialize global keyboard hook (only if panic key is enabled)
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyPressed += OnGlobalKeyPressed;
            if (App.Settings.Current.PanicKeyEnabled)
            {
                _keyboardHook.Start();
            }
            
            // Subscribe to progression events for real-time XP updates
            App.Progression.XPChanged += OnXPChanged;
            App.Progression.LevelUp += OnLevelUp;

            // Subscribe to companion events for real-time UI updates (v5.3)
            if (App.Companion != null)
            {
                App.Companion.XPAwarded += OnCompanionXPAwarded;
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.XPDrained += OnCompanionXPDrained;
                App.Companion.CompanionSwitched += OnCompanionSwitched;
            }

            // Subscribe to cloud profile sync event to refresh UI when profile loads
            App.ProfileSync.ProfileLoaded += OnProfileLoaded;

            LoadSettings();
            InitializePresets();
            UpdateUI();

            // Sync startup registration with settings
            StartupManager.SyncWithSettings(App.Settings.Current.RunOnStartup);

            _isLoading = false;

            // Initialize achievement grid and subscribe to unlock events
            PopulateAchievementGrid();
            if (App.Achievements != null)
            {
                App.Achievements.AchievementUnlocked += OnAchievementUnlockedInMainWindow;
            }

            // Initialize Avatar tab settings
            InitializePatreonTab();

            // Initialize banner rotation
            InitializeBannerRotation();

            // Ensure all services are stopped on startup (cleanup any leftover state)
            App.BouncingText.Stop();
            App.Overlay.Stop();
            
            // Show welcome dialog on first launch, then start tutorial
            // But delay tutorial if update dialog is being shown
            if (WelcomeDialog.ShowIfNeeded())
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    // Wait for any update dialog to be dismissed first
                    // Check every 500ms for up to 30 seconds
                    for (int i = 0; i < 60 && App.IsUpdateDialogActive; i++)
                    {
                        await Task.Delay(500);
                    }

                    // Only start tutorial if update dialog is done
                    if (!App.IsUpdateDialogActive)
                    {
                        StartTutorial();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                // Not first launch - check if we need to show "What's New" after an update
                ShowWhatsNewIfNeeded();
            }

            // Initialize scheduler timer (checks every 30 seconds)
            _schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _schedulerTimer.Tick += SchedulerTimer_Tick;

            // Delay scheduler startup by 60 seconds to allow app to fully initialize
            // This prevents issues when restarting after an update while in a scheduled time window
            const int schedulerGracePeriodSeconds = 60;
            App.Logger?.Information("Scheduler will start after {Seconds}s grace period", schedulerGracePeriodSeconds);

            Task.Delay(TimeSpan.FromSeconds(schedulerGracePeriodSeconds)).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current == null) return;

                    _schedulerTimer.Start();
                    CheckSchedulerOnStartup();
                    App.Logger?.Information("Scheduler grace period complete - scheduler now active");
                });
            });
            
            // Initialize browser when window is loaded
            Loaded += MainWindow_Loaded;
        }

        private void OnXPChanged(object? sender, double xp)
        {
            Dispatcher.Invoke(() => UpdateLevelDisplay());
        }

        private void OnProfileLoaded(object? sender, EventArgs e)
        {
            // Cloud profile was loaded - refresh UI to show updated XP/level
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Cloud profile loaded, refreshing UI");
                UpdateLevelDisplay();
                // Also update avatar in case level changed significantly
                _avatarTubeWindow?.UpdateAvatarForLevel(App.Settings.Current.PlayerLevel);
            });
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateLevelDisplay();
                // Show level up notification
                _trayIcon?.ShowNotification("Level Up!", $"You reached Level {newLevel}!", System.Windows.Forms.ToolTipIcon.Info);
                // Play level up sound
                PlayLevelUpSound();
                // Update avatar if level threshold reached (20, 50, 100)
                _avatarTubeWindow?.UpdateAvatarForLevel(newLevel);
            });
        }

        #region Companion Events (v5.3)

        private void OnCompanionXPAwarded(object? sender, (Models.CompanionId Companion, double Amount, double Modifier) args)
        {
            // Update companion progress UI in real-time when XP is earned
            Dispatcher.Invoke(() =>
            {
                // Only update if we're on the companion tab to avoid unnecessary work
                if (CompanionTab.Visibility == Visibility.Visible)
                {
                    UpdateCompanionCardsUI();
                }
            });
        }

        private void OnCompanionLevelUp(object? sender, (Models.CompanionId Companion, int NewLevel) args)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCompanionCardsUI();

                var companionName = Models.CompanionDefinition.GetById(args.Companion).Name;

                // Show notification for companion level up
                if (args.NewLevel == Models.CompanionProgress.MaxLevel)
                {
                    _trayIcon?.ShowNotification("MAX LEVEL!",
                        $"{companionName} has reached maximum level!",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                else if (args.NewLevel % 10 == 0)
                {
                    _trayIcon?.ShowNotification("Companion Level Up!",
                        $"{companionName} reached Level {args.NewLevel}!",
                        System.Windows.Forms.ToolTipIcon.Info);
                }

                // Play level up sound for significant milestones
                if (args.NewLevel % 10 == 0 || args.NewLevel == Models.CompanionProgress.MaxLevel)
                {
                    PlayLevelUpSound();
                }
            });
        }

        private void OnCompanionXPDrained(object? sender, double amount)
        {
            // Update UI when Brain Parasite drains XP
            Dispatcher.Invoke(() =>
            {
                if (CompanionTab.Visibility == Visibility.Visible)
                {
                    UpdateCompanionCardsUI();
                }
            });
        }

        private void OnCompanionSwitched(object? sender, Models.CompanionId newCompanion)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCompanionCardsUI();
            });
        }

        #endregion

        private void PlayLevelUpSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                };

                foreach (var path in soundPaths)
                {
                    if (File.Exists(path))
                    {
                        var player = new System.Windows.Media.MediaPlayer();
                        player.Open(new Uri(path, UriKind.Absolute));
                        player.Volume = (App.Settings.Current.MasterVolume / 100.0) * 0.5; // 50% of master volume
                        player.Play();
                        App.Logger?.Debug("Level up sound played from: {Path}", path);
                        return;
                    }
                }
                App.Logger?.Debug("Level up sound not found in any of: {Paths}", string.Join(", ", soundPaths));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to play level up sound: {Error}", ex.Message);
            }
        }

        private void OnGlobalKeyPressed(Key key)
        {
            // Track Alt+Tab for achievement (Player 2 Disconnected)
            if (key == Key.Tab && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)))
            {
                if (_isRunning)
                {
                    App.Achievements?.TrackAltTab();
                    App.Logger?.Debug("Alt+Tab detected during session");
                }
            }
            
            // Handle panic key capture mode
            if (_isCapturingPanicKey)
            {
                Dispatcher.Invoke(() =>
                {
                    App.Settings.Current.PanicKey = key.ToString();
                    _isCapturingPanicKey = false;
                    UpdatePanicKeyButton();
                    App.Logger?.Information("Panic key changed to: {Key}", key);
                });
                return;
            }
            
            // Check if panic key is enabled and pressed
            var settings = App.Settings.Current;
            if (settings.PanicKeyEnabled)
            {
                var panicKey = settings.PanicKey;
                if (key.ToString() == panicKey)
                {
                    Dispatcher.Invoke(() => HandlePanicKeyPress());
                }
            }
        }

        private void HandlePanicKeyPress()
        {
            var now = DateTime.Now;
            var timeSinceLastPress = (now - _lastPanicTime).TotalMilliseconds;
            
            // Reset counter if more than 2 seconds since last press
            if (timeSinceLastPress > 2000)
            {
                _panicPressCount = 0;
            }
            
            _panicPressCount++;
            _lastPanicTime = now;
            
            if (_isRunning)
            {
                // First press while running: stop engine, show UI
                App.Logger?.Information("Panic key pressed! Stopping engine...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                // Cancel any active autonomy pulses (restore original settings)
                App.Autonomy?.CancelActivePulses();

                // Track panic press for Relapse achievement (must be before stopping session)
                App.Achievements?.TrackPanicPressed();

                // Stop session if one is running (this also tracks panic for relapse)
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    _sessionEngine.StopSession(completed: false);
                }

                StopEngine();

                // Reset interaction queue to clear any pending queued items
                App.InteractionQueue?.ForceReset();

                // Restore window
                if (!IsVisible)
                {
                    _trayIcon?.ShowWindow();
                }
                WindowState = WindowState.Normal;
                Activate();
                ShowAvatarTube();
                
                _trayIcon?.ShowNotification("Stopped", "Press panic key again within 2 seconds to exit completely.", System.Windows.Forms.ToolTipIcon.Info);
            }
            else if (_panicPressCount >= 2)
            {
                // Second press while stopped: exit application
                App.Logger?.Information("Double panic! Exiting application...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                _exitRequested = true;
                SaveSettings();
                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                Application.Current.Shutdown();
            }
        }

        private void UpdatePanicKeyButton()
        {
            if (BtnPanicKey != null)
            {
                var currentKey = App.Settings.Current.PanicKey;
                BtnPanicKey.Content = _isCapturingPanicKey ? "Press any key..." : $"🔑 {currentKey}";
            }
        }

        private void LoadLogo()
        {
            try
            {
                var resourceUri = new Uri("pack://application:,,,/Resources/logo.png", UriKind.Absolute);
                ImgLogo.Source = new System.Windows.Media.Imaging.BitmapImage(resourceUri);
                App.Logger?.Debug("Logo loaded from embedded resource");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load logo: {Error}", ex.Message);
            }
        }

        private void CenterOnPrimaryScreen()
        {
            // Get the primary screen
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            if (primaryScreen == null) return;
            
            // Get DPI scaling
            var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (dpiScale == 0) dpiScale = 1;
            
            // Calculate center position on primary screen
            var screenWidth = primaryScreen.WorkingArea.Width / dpiScale;
            var screenHeight = primaryScreen.WorkingArea.Height / dpiScale;
            var screenLeft = primaryScreen.WorkingArea.Left / dpiScale;
            var screenTop = primaryScreen.WorkingArea.Top / dpiScale;
            
            Left = screenLeft + (screenWidth - Width) / 2;
            Top = screenTop + (screenHeight - Height) / 2;
        }

        #region Custom Title Bar

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                BtnMaximize_Click(sender, e);
            }
            else
            {
                // Drag window
                if (WindowState == WindowState.Maximized)
                {
                    // Restore before dragging from maximized
                    var point = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    Left = point.X - (Width / 2);
                    Top = point.Y - 15;
                }
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // Hide avatar tube BEFORE minimizing to prevent visual artifacts
            HideAvatarTube();
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Content = "☐";

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }
            }
            else
            {
                // Detach avatar before maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                WindowState = WindowState.Maximized;
                BtnMaximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook window messages to intercept minimize BEFORE it happens
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(WndProc);

            // Enable Windows 11 rounded corners
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // Silently fail on Windows 10 or earlier - they don't support this API
            }

            // Re-center after load in case DPI wasn't available in constructor
            CenterOnPrimaryScreen();

            // Update panic key button
            UpdatePanicKeyButton();

            // Handle start minimized (to tray) - delay briefly to let window render properly first
            if (App.Settings.Current.StartMinimized)
            {
                // Let the window fully render before minimizing to avoid black window artifacts
                await Task.Delay(100);
                _trayIcon?.MinimizeToTray();
            }

            // Handle auto-start engine
            if (App.Settings.Current.AutoStartEngine)
            {
                StartEngine();
            }

            // Handle force video on launch (after a short delay to let things initialize)
            if (App.Settings.Current.ForceVideoOnLaunch)
            {
                await Task.Delay(1500); // Let engine and services initialize
                TriggerStartupVideo();
            }

            // Auto-initialize browser on startup
            await InitializeBrowserAsync();

            // Check if this is first run and prompt for assets folder
            await CheckFirstRunAssetsPromptAsync();

            // Initialize Avatar Tube Window
            InitializeAvatarTube();

            // Initialize Discord Rich Presence checkboxes (both locations)
            ChkDiscordRichPresence.IsChecked = App.Settings.Current.DiscordRichPresenceEnabled;
            ChkQuickDiscordRichPresence.IsChecked = App.Settings.Current.DiscordRichPresenceEnabled;

            // Initialize Quick Links login buttons
            UpdateQuickPatreonUI();
            UpdateQuickDiscordUI();

            // Initialize scrolling marquee banner
            InitializeMarqueeBanner();
        }

        /// <summary>
        /// Checks if this is a first run (no assets) and prompts user to choose a content folder.
        /// </summary>
        private async Task CheckFirstRunAssetsPromptAsync()
        {
            try
            {
                // Skip if custom assets path is already set
                if (!string.IsNullOrWhiteSpace(App.Settings?.Current?.CustomAssetsPath))
                    return;

                // Check if default assets folder has any content
                var defaultImagesPath = System.IO.Path.Combine(App.UserAssetsPath, "images");
                var defaultVideosPath = System.IO.Path.Combine(App.UserAssetsPath, "videos");

                int imageCount = 0;
                int videoCount = 0;

                if (System.IO.Directory.Exists(defaultImagesPath))
                {
                    imageCount = System.IO.Directory.GetFiles(defaultImagesPath, "*.*")
                        .Count(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                }

                if (System.IO.Directory.Exists(defaultVideosPath))
                {
                    videoCount = System.IO.Directory.GetFiles(defaultVideosPath, "*.*")
                        .Count(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
                }

                // If user has content, don't bother them
                if (imageCount > 5 || videoCount > 2)
                    return;

                // Check if there's a "first run shown" flag
                if (App.Settings?.Current?.FirstRunAssetsPromptShown == true)
                    return;

                // Show first-run prompt after a brief delay
                await Task.Delay(500);

                var result = MessageBox.Show(
                    "Welcome to Conditioning Control Panel!\n\n" +
                    "Would you like to choose a custom folder for your content?\n\n" +
                    "This folder will store:\n" +
                    "  • Your images and videos\n" +
                    "  • Downloaded content packs\n\n" +
                    "Choosing a custom folder is recommended if you want to:\n" +
                    "  • Keep content on a different drive\n" +
                    "  • Preserve content across reinstalls\n\n" +
                    "You can always change this later in Settings > Assets.",
                    "Choose Content Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                // Mark as shown regardless of choice
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.FirstRunAssetsPromptShown = true;
                    App.Settings.Save();
                }

                if (result == MessageBoxResult.Yes)
                {
                    // Open the assets folder selection dialog
                    BtnPickAssetsFolder_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error in first-run assets prompt");
            }
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int WM_GETMINMAXINFO = 0x0024;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Intercept minimize command to hide to tray instead
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
            {
                handled = true; // Mark as handled to prevent default minimize
                // Hide avatar tube FIRST to avoid event handler issues
                HideAvatarTube();
                _trayIcon?.MinimizeToTray();
            }
            // Fix maximized window extending behind taskbar (buttons cut off)
            else if (msg == WM_GETMINMAXINFO)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);

                // Get the monitor this window is on
                var monitor = System.Windows.Forms.Screen.FromHandle(hwnd);
                var workingArea = monitor.WorkingArea;

                // Constrain maximized size to working area (excludes taskbar)
                mmi.ptMaxPosition.X = workingArea.Left;
                mmi.ptMaxPosition.Y = workingArea.Top;
                mmi.ptMaxSize.X = workingArea.Width;
                mmi.ptMaxSize.Y = workingArea.Height;

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        #region Avatar Tube Window

        private void InitializeAvatarTube()
        {
            // Prevent duplicate initialization
            if (_avatarTubeWindow != null)
            {
                App.Logger?.Warning("InitializeAvatarTube called but window already exists");
                return;
            }

            try
            {
                _avatarTubeWindow = new AvatarTubeWindow(this);
                App.AvatarWindow = _avatarTubeWindow; // Set global reference for services

                // Only show if main window is visible and not minimized
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    _avatarTubeWindow.Show();
                    _avatarTubeWindow.StartPoseAnimation();
                }

                App.Logger?.Information("Avatar Tube Window initialized");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to initialize Avatar Tube Window: {Error}", ex.Message);
            }
        }

        public void ShowAvatarTube()
        {
            // Recreate if closed
            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
            }
            else
            {
                _avatarTubeWindow.ShowTube();
                _avatarTubeWindow.StartPoseAnimation();
            }
        }

        public void HideAvatarTube()
        {
            if (_avatarTubeWindow != null)
            {
                // Don't hide or close if the tube is detached - let it float independently
                if (_avatarTubeWindow.IsDetached)
                {
                    return;
                }

                _avatarTubeWindow.StopPoseAnimation();
                _avatarTubeWindow.HideTube();
            }
        }

        /// <summary>
        /// Shows only the avatar tube in detached mode (floating independently)
        /// Called from tray icon "Wake Bambi Up!" option
        /// </summary>
        public void WakeBambiUp()
        {
            // Create tube if needed
            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
            }

            if (_avatarTubeWindow != null)
            {
                // Show the tube
                _avatarTubeWindow.Show();
                _avatarTubeWindow.StartPoseAnimation();

                // Detach it so it floats independently
                if (!_avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Detach();
                }

                _avatarTubeWindow.Giggle("Good morning~!");
            }
        }

        public void SetAvatarPose(int poseNumber)
        {
            _avatarTubeWindow?.SetPose(poseNumber);
        }

        #endregion

        #region Tab Navigation

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("settings");
        }

        private void BtnPresets_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("presets");
            RefreshPresetsList();
        }

        private void BtnProgression_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("progression");
        }

        private void BtnAchievements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("achievements");
        }

        private void BtnCompanion_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("leaderboard");
        }

        private void BtnPatreonExclusives_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("patreon");
        }

        private async void BtnQuickPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleQuickPatreonLoginAsync();
        }

        private async Task HandleQuickPatreonLoginAsync()
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                App.Patreon.UnifiedUserId = null;
                App.UnifiedUserId = null;
                UpdateQuickPatreonUI();
                UpdatePatreonUI();
                UpdateBannerWelcomeMessage();
            }
            else
            {
                // Start OAuth flow
                BtnQuickPatreonLogin.IsEnabled = false;
                BtnQuickPatreonLogin.Content = "⭐ Connecting...";

                try
                {
                    await App.Patreon.StartOAuthFlowAsync();

                    // Use unified account flow - handles lookup, registration, and linking
                    var success = await AccountService.HandlePostAuthAsync(this, "patreon");

                    if (success)
                    {
                        UpdateQuickPatreonUI();
                        UpdatePatreonUI();
                        UpdateBannerWelcomeMessage();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Patreon login failed");
                    MessageBox.Show(
                        $"Failed to connect to Patreon.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    BtnQuickPatreonLogin.IsEnabled = true;
                    UpdateQuickPatreonUI();
                }
            }
        }

        private void UpdateQuickPatreonUI()
        {
            if (App.Patreon?.IsAuthenticated == true)
            {
                var name = App.Patreon.DisplayName ?? App.Patreon.PatronName ?? "Connected";
                BtnQuickPatreonLogin.Content = $"✓ {name}";
                BtnQuickPatreonLogin.ToolTip = "Click to disconnect Patreon";
            }
            else
            {
                BtnQuickPatreonLogin.Content = "⭐ Login with Patreon";
                BtnQuickPatreonLogin.ToolTip = "Login with Patreon for premium features";
            }
        }

        private async void BtnQuickDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            await HandleDiscordLoginAsync();
        }

        private async Task HandleDiscordLoginAsync()
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                App.Discord.UnifiedUserId = null;
                App.UnifiedUserId = null;
                UpdateQuickDiscordUI();
                UpdateBannerWelcomeMessage();
            }
            else
            {
                // Start OAuth flow
                SetDiscordButtonsEnabled(false);
                SetDiscordButtonsContent("Connecting...");

                try
                {
                    await App.Discord.StartOAuthFlowAsync();

                    // Use unified account flow - handles lookup, registration, and linking
                    var success = await AccountService.HandlePostAuthAsync(this, "discord");

                    if (success)
                    {
                        UpdateQuickDiscordUI();
                        UpdateBannerWelcomeMessage();

                        // Update bandwidth display (Discord users can inherit Patreon benefits via linked display name)
                        _ = UpdateBandwidthDisplayAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Discord login failed");
                    MessageBox.Show(
                        $"Failed to connect to Discord.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    SetDiscordButtonsEnabled(true);
                    UpdateQuickDiscordUI();
                }
            }
        }

        private void SetDiscordButtonsEnabled(bool enabled)
        {
            BtnQuickDiscordLogin.IsEnabled = enabled;
        }

        private void SetDiscordButtonsContent(string text)
        {
            BtnQuickDiscordLogin.Content = $"🎮 {text}";
        }

        private void UpdateQuickDiscordUI()
        {
            if (App.Discord?.IsAuthenticated == true)
            {
                BtnQuickDiscordLogin.Content = $"✓ {App.Discord.DisplayName ?? "Connected"}";
                BtnQuickDiscordLogin.ToolTip = "Click to logout";
            }
            else
            {
                BtnQuickDiscordLogin.Content = "🎮 Login with Discord";
                BtnQuickDiscordLogin.ToolTip = "Login with Discord for community features";
            }

            // Also update the Patreon tab Discord UI
            UpdateDiscordUI();
        }

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.gg/YxVAMt4qaZ",
                    UseShellExecute = true
                });
                App.Logger?.Information("Opened Discord invite link");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Discord link");
            }
        }

        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            // Get the state from whichever checkbox was clicked
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;

            // Sync both checkboxes
            ChkDiscordRichPresence.IsChecked = isEnabled;
            ChkQuickDiscordRichPresence.IsChecked = isEnabled;

            App.Settings.Current.DiscordRichPresenceEnabled = isEnabled;

            if (App.DiscordRpc != null)
            {
                App.DiscordRpc.IsEnabled = isEnabled;
                App.Logger?.Information("Discord Rich Presence {Status}", isEnabled ? "enabled" : "disabled");
            }
        }

        #region Expandable Icon Button Animation

        private readonly Dictionary<Button, double> _expandedWidths = new();

        private void ExpandableIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            // Animation disabled - was causing crashes
            // Just show the label text without animation
            try
            {
                if (sender is not Button btn) return;
                if (btn.Template == null || !btn.IsLoaded) return;

                var label = btn.Template.FindName("LabelText", btn) as TextBlock;
                if (label == null) return;

                label.Visibility = Visibility.Visible;
                label.Margin = new Thickness(6, 0, 0, 0);
            }
            catch
            {
                // Silently ignore animation errors
            }
        }

        private void ExpandableIcon_MouseLeave(object sender, MouseEventArgs e)
        {
            // Animation disabled - was causing crashes
            // Just hide the label text without animation
            try
            {
                if (sender is not Button btn) return;
                if (btn.Template == null || !btn.IsLoaded) return;

                var label = btn.Template.FindName("LabelText", btn) as TextBlock;
                if (label == null) return;

                label.Visibility = Visibility.Collapsed;
                label.Margin = new Thickness(0);
            }
            catch
            {
                // Silently ignore animation errors
            }
        }

        #endregion

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = "Checking...";

            try
            {
                await App.CheckForUpdatesManuallyAsync(this);
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
                BtnCheckUpdates.Content = "Check for Updates";
            }
        }

        private async void BtnUpdateAvailable_Click(object sender, RoutedEventArgs e)
        {
            // If server provided a URL, open it in browser instead of auto-updating
            if (!string.IsNullOrEmpty(_serverUpdateUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _serverUpdateUrl,
                        UseShellExecute = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to open update URL: {Error}", ex.Message);
                }
            }

            // Trigger the update installation
            await App.CheckForUpdatesManuallyAsync(this);
        }

        /// <summary>
        /// Sets the update button state in the tab bar.
        /// Called from App when an update is detected or after checking.
        /// </summary>
        public void ShowUpdateAvailableButton(bool updateAvailable)
        {
            Dispatcher.Invoke(() =>
            {
                BtnUpdateAvailable.Tag = updateAvailable ? "UpdateAvailable" : "NoUpdate";
                BtnUpdateAvailable.Content = updateAvailable ? "UPDATE" : "LATEST VERSION :3";
                BtnUpdateAvailable.ToolTip = updateAvailable
                    ? "Update Available - Click to install!"
                    : "You're on the latest version";
            });
        }

        private void ShowTab(string tab)
        {
            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;
            CompanionTab.Visibility = Visibility.Collapsed;
            PatreonTab.Visibility = Visibility.Collapsed;
            LeaderboardTab.Visibility = Visibility.Collapsed;
            AssetsTab.Visibility = Visibility.Collapsed;

            // Reset all button styles to inactive
            var inactiveStyle = FindResource("TabButton") as Style;
            var activeStyle = FindResource("TabButtonActive") as Style;
            BtnSettings.Style = inactiveStyle;
            BtnPresets.Style = inactiveStyle;
            BtnProgression.Style = inactiveStyle;
            BtnAchievements.Style = inactiveStyle;
            BtnCompanion.Style = inactiveStyle;
            BtnLeaderboard.Style = inactiveStyle;
            BtnOpenAssets.Style = inactiveStyle;
            // BtnPatreonExclusives keeps its inline Patreon red style defined in XAML

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    BtnSettings.Style = activeStyle;
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    BtnPresets.Style = activeStyle;
                    break;

                case "progression":
                    App.Logger?.Debug("ShowTab: Attempting to make ProgressionTab visible.");
                    try
                    {
                        ProgressionTab.Visibility = Visibility.Visible;
                        App.Logger?.Debug("ShowTab: ProgressionTab visibility set to Visible.");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error("ShowTab: Error making ProgressionTab visible: {Error}", ex.Message);
                        throw;
                    }
                    BtnProgression.Style = activeStyle;
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    BtnAchievements.Style = activeStyle;
                    RefreshAllAchievementTiles();
                    UpdateAchievementCount();
                    break;

                case "companion":
                    CompanionTab.Visibility = Visibility.Visible;
                    BtnCompanion.Style = activeStyle;
                    SyncCompanionTabUI();
                    break;

                case "patreon":
                    PatreonTab.Visibility = Visibility.Visible;
                    // Note: The main Discord login button isn't a tab button, so no style update needed
                    UpdatePatreonUI();
                    break;

                case "leaderboard":
                    LeaderboardTab.Visibility = Visibility.Visible;
                    BtnLeaderboard.Style = activeStyle;
                    _ = RefreshLeaderboardAsync(); // Load on first view
                    break;

                case "assets":
                    AssetsTab.Visibility = Visibility.Visible;
                    BtnOpenAssets.Style = activeStyle;
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    _ = RefreshPacksAsync();
                    break;
            }
        }

        #region Leaderboard

        private async void LeaderboardColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            // Ignore during window initialization
            if (_isLoading || TxtLeaderboardStatus == null || App.Leaderboard == null) return;

            if (e.OriginalSource is GridViewColumnHeader header && header.Content is string headerText)
            {
                // Map header text to sort field
                string? sortField = headerText switch
                {
                    "Rank" => "level",
                    "Level" => "level",
                    "XP" => "xp",
                    "Bubbles" => "total_bubbles_popped",
                    "GIFs" => "total_flashes",
                    "Video Min" => "total_video_minutes",
                    "Lock Cards" => "total_lock_cards_completed",
                    "Patreon" => "is_patreon",
                    "Name" => null, // Client-side sort
                    "Online" => null, // Client-side sort
                    "Achievements" => null, // Client-side sort
                    _ => null
                };

                if (sortField != null)
                {
                    // Server-side sort
                    await RefreshLeaderboardAsync(sortField);
                }
                else if (headerText == "Name")
                {
                    // Client-side alphabetical sort
                    TxtLeaderboardStatus.Text = "Sorting by name...";
                    var sorted = App.Leaderboard.Entries.OrderBy(x => x.DisplayName).ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = $"{App.Leaderboard.OnlineUsers} online / {App.Leaderboard.TotalUsers} users • Sorted by Name";
                }
                else if (headerText == "Online")
                {
                    // Client-side: online first, then by level descending
                    TxtLeaderboardStatus.Text = "Sorting by online status...";
                    var sorted = App.Leaderboard.Entries
                        .OrderByDescending(x => x.IsOnline)
                        .ThenByDescending(x => x.Level)
                        .ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = $"{App.Leaderboard.OnlineUsers} online / {App.Leaderboard.TotalUsers} users • Online first";
                }
                else if (headerText == "Achievements")
                {
                    // Client-side: by achievement count descending
                    TxtLeaderboardStatus.Text = "Sorting by achievements...";
                    var sorted = App.Leaderboard.Entries
                        .OrderByDescending(x => x.AchievementsCount)
                        .ToList();
                    LstLeaderboard.ItemsSource = sorted;
                    TxtLeaderboardStatus.Text = $"{App.Leaderboard.OnlineUsers} online / {App.Leaderboard.TotalUsers} users • Sorted by Achievements";
                }
            }
        }

        private async void BtnRefreshLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            await RefreshLeaderboardAsync();
        }

        private void BtnLeaderboardDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string discordId && !string.IsNullOrEmpty(discordId))
            {
                try
                {
                    // Open Discord user profile using the correct protocol format
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"discord://-/users/{discordId}",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open Discord profile for user {DiscordId}", discordId);
                }
            }
        }

        private async Task RefreshLeaderboardAsync(string? sortBy = null)
        {
            if (App.Leaderboard == null || TxtLeaderboardStatus == null || BtnRefreshLeaderboard == null) return;

            TxtLeaderboardStatus.Text = "Syncing...";
            BtnRefreshLeaderboard.IsEnabled = false;

            try
            {
                // Sync local stats to cloud first so leaderboard shows latest data
                var syncEnabled = App.ProfileSync?.IsSyncEnabled == true;
                App.Logger?.Information("Leaderboard refresh: IsSyncEnabled={SyncEnabled}, ProfileSync={HasProfileSync}, Patreon={HasPatreon}, Authenticated={IsAuth}",
                    syncEnabled,
                    App.ProfileSync != null,
                    App.Patreon != null,
                    App.Patreon?.IsAuthenticated);

                if (syncEnabled)
                {
                    App.Logger?.Information("Syncing profile before leaderboard refresh...");
                    App.Achievements?.Save(); // Save any pending achievements first
                    await App.ProfileSync.SyncProfileAsync();
                    App.Logger?.Information("Profile sync completed");
                }

                TxtLeaderboardStatus.Text = "Loading...";
                var success = await App.Leaderboard.RefreshAsync(sortBy);

                if (success)
                {
                    LstLeaderboard.ItemsSource = App.Leaderboard.Entries;
                    TxtLeaderboardStatus.Text = $"{App.Leaderboard.OnlineUsers} online / {App.Leaderboard.TotalUsers} users";
                }
                else
                {
                    TxtLeaderboardStatus.Text = App.Leaderboard.LastRefreshError ?? "Failed to load";
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error refreshing leaderboard");
                TxtLeaderboardStatus.Text = "Error loading leaderboard";
            }
            finally
            {
                BtnRefreshLeaderboard.IsEnabled = true;
            }
        }

        #endregion

        private void UpdateAchievementCount()
        {
            if (TxtAchievementCount != null && App.Achievements != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount();
                var total = App.Achievements.GetTotalCount();
                TxtAchievementCount.Text = $"{unlocked} / {total} Achievements Unlocked";
            }
        }

        /// <summary>
        /// Sync Companion tab UI controls with current state
        /// </summary>
        private void SyncCompanionTabUI()
        {
            _isLoading = true;
            try
            {
                // Sync avatar enabled
                ChkAvatarEnabledCompanion.IsChecked = _avatarTubeWindow?.IsVisible == true;

                // Sync trigger mode
                ChkTriggerModeCompanion.IsChecked = App.Settings?.Current?.TriggerModeEnabled == true;
                TriggerSettingsPanelCompanion.Visibility = ChkTriggerModeCompanion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

                // Sync trigger interval
                var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
                SliderTriggerIntervalCompanion.Value = interval;
                TxtTriggerIntervalCompanion.Text = $"{interval}s";

                // Sync idle interval
                var idleInterval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
                SliderIdleIntervalCompanion.Value = idleInterval;
                TxtIdleIntervalCompanion.Text = $"{idleInterval}s";

                // Sync detach status
                var isDetached = _avatarTubeWindow?.IsDetached == true;
                TxtDetachStatusCompanion.Text = isDetached ? "Floating freely" : "Anchored to window";
                BtnDetachCompanionTab.Content = isDetached ? "Attach" : "Detach";

                // Sync companion leveling UI (v5.3)
                UpdateCompanionCardsUI();
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Updates the companion selection cards UI with current progress and active state.
        /// </summary>
        private void UpdateCompanionCardsUI()
        {
            if (App.Companion == null || App.Settings?.Current == null) return;

            var activeId = App.Companion.ActiveCompanion;
            var playerLevel = App.Settings.Current.PlayerLevel;

            // Update each companion card
            var cards = new[] { CompanionCard0, CompanionCard1, CompanionCard2, CompanionCard3 };
            var levelTexts = new[] { TxtCompanion0Level, TxtCompanion1Level, TxtCompanion2Level, TxtCompanion3Level };
            var lockTexts = new[] { TxtCompanion0Lock, TxtCompanion1Lock, TxtCompanion2Lock, TxtCompanion3Lock };
            var colors = new[] { "#FF69B4", "#9370DB", "#50C878", "#FF6B6B" };

            for (int i = 0; i < 4; i++)
            {
                var companionId = (Models.CompanionId)i;
                var def = Models.CompanionDefinition.GetById(companionId);
                var progress = App.Companion.GetProgress(companionId);
                var isUnlocked = playerLevel >= def.RequiredLevel;

                // Update level text - show required level if locked
                if (isUnlocked)
                    levelTexts[i].Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";
                else
                    levelTexts[i].Text = $"Lv.{def.RequiredLevel}";

                // Highlight active companion with colored border
                var isActive = companionId == activeId;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
                cards[i].BorderBrush = isActive
                    ? new System.Windows.Media.SolidColorBrush(color)
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);

                // Update lock visibility based on player level
                lockTexts[i].Visibility = isUnlocked ? Visibility.Collapsed : Visibility.Visible;
                cards[i].Opacity = isUnlocked ? 1.0 : 0.5;
            }

            // Update active companion details
            var activeDef = Models.CompanionDefinition.GetById(activeId);
            var activeProgress = App.Companion.ActiveProgress;

            TxtActiveCompanionName.Text = activeDef.Name;
            TxtActiveCompanionLevel.Text = activeProgress.IsMaxLevel ? " · MAX LEVEL" : $" · Level {activeProgress.Level}";
            TxtActiveCompanionDesc.Text = activeDef.Description;
            TxtActiveCompanionXP.Text = activeProgress.IsMaxLevel
                ? "Complete!"
                : $"{activeProgress.CurrentXP:F0} / {activeProgress.XPForNextLevel:F0} XP";

            // Update main progress bar
            PrgCompanion0.Value = activeProgress.LevelProgress * 100;
            PrgCompanion0.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[(int)activeId]));

            // Update community prompts UI
            UpdateCommunityPromptsUI();

            // Update companion prompt labels
            UpdateCompanionPromptLabels();
        }

        /// <summary>
        /// Gets the display name for the currently active prompt.
        /// </summary>
        private string GetActivePromptDisplayName()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;

            if (!string.IsNullOrEmpty(activePromptId))
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                return prompt?.Name ?? "Unknown";
            }
            else if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
            {
                return "Custom";
            }
            return "Default";
        }

        /// <summary>
        /// Updates the community prompts section UI.
        /// </summary>
        private void UpdateCommunityPromptsUI()
        {
            var activePromptId = App.Settings?.Current?.ActiveCommunityPromptId;
            var installedIds = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

            // Update the Customize button prompt name
            TxtCustomizePromptName.Text = GetActivePromptDisplayName();

            // Update active prompt display
            if (string.IsNullOrEmpty(activePromptId))
            {
                if (App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
                {
                    TxtActivePromptName.Text = "Custom (Edited)";
                }
                else
                {
                    TxtActivePromptName.Text = "Default (Built-in)";
                }
                BtnDeactivatePrompt.Visibility = Visibility.Collapsed;
            }
            else
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(activePromptId);
                TxtActivePromptName.Text = prompt != null ? $"{prompt.Name} by {prompt.Author}" : "Custom";
                BtnDeactivatePrompt.Visibility = Visibility.Visible;
            }

            // Update installed prompts list
            InstalledPromptsPanel.Children.Clear();
            if (installedIds.Count == 0)
            {
                InstalledPromptsPanel.Children.Add(new TextBlock
                {
                    Text = "No prompts installed",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                foreach (var id in installedIds)
                {
                    var prompt = App.CommunityPrompts?.GetInstalledPrompt(id);
                    if (prompt == null) continue;

                    var isActive = id == activePromptId;
                    var row = CreatePromptRow(prompt, isActive);
                    InstalledPromptsPanel.Children.Add(row);
                }
            }
        }

        private FrameworkElement CreatePromptRow(Models.CommunityPrompt prompt, bool isActive)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Name + Author
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isActive)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "● ",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    FontSize = 10
                });
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = prompt.Name,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 10,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $" by {prompt.Author}",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 96, 96)),
                FontSize = 9
            });
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            // Action buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            if (!isActive)
            {
                var activateBtn = new Button
                {
                    Content = "Use",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 112, 219)),
                    BorderThickness = new Thickness(0),
                    FontSize = 9,
                    Padding = new Thickness(6, 2, 6, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = prompt.Id
                };
                activateBtn.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag is string promptId)
                    {
                        App.CommunityPrompts?.ActivatePrompt(promptId);
                        UpdateCommunityPromptsUI();
                    }
                };
                buttonPanel.Children.Add(activateBtn);
            }

            var removeBtn = new Button
            {
                Content = "×",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = prompt.Id,
                ToolTip = "Remove"
            };
            removeBtn.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is string promptId)
                {
                    App.CommunityPrompts?.RemovePrompt(promptId);
                    UpdateCommunityPromptsUI();
                }
            };
            buttonPanel.Children.Add(removeBtn);

            Grid.SetColumn(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        /// <summary>
        /// Handles clicking on a companion card to switch companions.
        /// Also switches the avatar to match the selected companion.
        /// </summary>
        private void CompanionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag == null) return;
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex)) return;

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);
            var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;

            // Check level requirement
            if (playerLevel < def.RequiredLevel)
            {
                System.Windows.MessageBox.Show(
                    $"{def.Name} unlocks at Level {def.RequiredLevel}.\n\nYou're currently Level {playerLevel}. Keep training to unlock!",
                    "Level Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Switch companion
            if (App.Companion?.SwitchCompanion(companionId) == true)
            {
                UpdateCompanionCardsUI();

                // Also switch the avatar to match the companion
                _avatarTubeWindow?.SwitchToCompanionAvatar(companionId);

                App.Logger?.Information("Switched to companion: {Name}", def.Name);
            }
        }

        /// <summary>
        /// Handles clicking the personality button on a companion card.
        /// Opens a dialog to assign a prompt JSON to this companion.
        /// </summary>
        private void BtnCompanionPersonality_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent card click from also triggering

            if (sender is not FrameworkElement element || element.Tag == null) return;
            if (!int.TryParse(element.Tag.ToString(), out int companionIndex)) return;

            var companionId = (Models.CompanionId)companionIndex;
            var def = Models.CompanionDefinition.GetById(companionId);

            // Check if companion is unlocked
            if (!(App.Companion?.IsCompanionUnlocked(companionId) ?? false))
            {
                ShowStyledDialog("Locked", $"{def.Name} is not unlocked yet.\nUnlock it first to assign a personality.", "OK", "");
                return;
            }

            // Show options: Import JSON, Choose from installed, or Clear
            var currentPromptId = App.Settings?.Current?.GetCompanionPromptId(companionIndex);
            var currentPromptName = Services.CompanionService.GetAssignedPromptName(companionId);
            var hasAssigned = !string.IsNullOrEmpty(currentPromptName);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select AI Personality for {def.Name}",
                Filter = "Prompt JSON files (*.json)|*.json",
                DefaultExt = ".json"
            };

            // Check for prompts folder
            var promptsFolder = System.IO.Path.Combine(App.EffectiveAssetsPath, "prompts");
            if (System.IO.Directory.Exists(promptsFolder))
            {
                dialog.InitialDirectory = promptsFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Import the prompt file if needed
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        // Assign to companion
                        App.Settings?.Current?.SetCompanionPromptId(companionIndex, prompt.Id);
                        App.Settings?.Save();

                        // Update UI
                        UpdateCompanionPromptLabels();

                        App.Logger?.Information("Assigned prompt '{Prompt}' to companion {Companion}",
                            prompt.Name, def.Name);

                        ShowStyledDialog("Personality Assigned",
                            $"{def.Name} will now use \"{prompt.Name}\" personality.\n\nThis will activate automatically when you switch to this companion.",
                            "OK", "");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to assign prompt to companion");
                    ShowStyledDialog("Error", $"Failed to import prompt: {ex.Message}", "OK", "");
                }
            }
        }

        /// <summary>
        /// Updates the prompt labels on all companion cards.
        /// </summary>
        private void UpdateCompanionPromptLabels()
        {
            var promptTexts = new[] { TxtCompanion0Prompt, TxtCompanion1Prompt, TxtCompanion2Prompt, TxtCompanion3Prompt };

            for (int i = 0; i < promptTexts.Length; i++)
            {
                var promptName = Services.CompanionService.GetAssignedPromptName((Models.CompanionId)i);
                promptTexts[i].Text = promptName ?? "";
                promptTexts[i].ToolTip = string.IsNullOrEmpty(promptName) ? null : $"AI Personality: {promptName}";
            }
        }

        #region Community Prompts

        private async void BtnRefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnRefreshPrompts.IsEnabled = false;
                BtnRefreshPrompts.Content = "...";
                await App.CommunityPrompts?.GetAvailablePromptsAsync(forceRefresh: true);
                UpdateCommunityPromptsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh prompts: {Error}", ex.Message);
            }
            finally
            {
                BtnRefreshPrompts.IsEnabled = true;
                BtnRefreshPrompts.Content = "Refresh";
            }
        }

        private void BtnDeactivatePrompt_Click(object sender, RoutedEventArgs e)
        {
            App.CommunityPrompts?.DeactivatePrompt();
            UpdateCommunityPromptsUI();
        }

        private async void BtnBrowsePrompts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fetch available prompts
                var available = await App.CommunityPrompts?.GetAvailablePromptsAsync();
                if (available == null || available.Count == 0)
                {
                    ShowStyledDialog("Community Prompts", "No community prompts available yet.\n\nCreate and export your own to share!", "OK", "");
                    return;
                }

                // Build selection list
                var installed = App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();
                var notInstalled = available.Where(p => !installed.Contains(p.Id)).ToList();

                if (notInstalled.Count == 0)
                {
                    ShowStyledDialog("Community Prompts", "You've installed all available prompts!", "OK", "");
                    return;
                }

                // Show simple selection (first 5)
                var message = "Available prompts:\n\n";
                for (int i = 0; i < Math.Min(5, notInstalled.Count); i++)
                {
                    var p = notInstalled[i];
                    message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
                }

                if (notInstalled.Count > 5)
                    message += $"...and {notInstalled.Count - 5} more\n\n";

                message += "Install the first one?";

                var result = ShowStyledDialog("Browse Community Prompts", message, "Install", "Cancel");
                if (result && notInstalled.Count > 0)
                {
                    var prompt = await App.CommunityPrompts?.InstallPromptAsync(notInstalled[0].Id);
                    if (prompt != null)
                    {
                        ShowStyledDialog("Installed!", $"'{prompt.Name}' has been installed.\n\nUse the 'Use' button to activate it.", "OK", "");
                        UpdateCommunityPromptsUI();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error browsing prompts");
                ShowStyledDialog("Error", $"Failed to browse prompts:\n{ex.Message}", "OK", "");
            }
        }

        private void BtnImportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Import Community Prompt"
                };

                if (dialog.ShowDialog() == true)
                {
                    var prompt = App.CommunityPrompts?.ImportFromFile(dialog.FileName);
                    if (prompt != null)
                    {
                        ShowStyledDialog("Imported!", $"'{prompt.Name}' by {prompt.Author} has been imported.", "OK", "");
                        UpdateCommunityPromptsUI();
                    }
                    else
                    {
                        ShowStyledDialog("Error", "Failed to import prompt. The file may be invalid.", "OK", "");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error importing prompt");
                ShowStyledDialog("Error", $"Failed to import prompt:\n{ex.Message}", "OK", "");
            }
        }

        private async void BtnExportPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create export dialog with name/author input
                var name = "My Custom Personality";
                var author = App.Patreon?.DisplayName ?? "Anonymous";

                var prompt = App.CommunityPrompts?.ExportCurrentSettings(name, author, "A custom AI personality.");
                if (prompt == null)
                {
                    ShowStyledDialog("Error", "Failed to export current settings.", "OK", "");
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = "Export Community Prompt",
                    FileName = $"{name.Replace(" ", "_")}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    await App.CommunityPrompts?.SavePromptToFileAsync(prompt, dialog.FileName);
                    ShowStyledDialog("Exported!", $"Prompt exported to:\n{dialog.FileName}\n\nShare this file with others!", "OK", "");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error exporting prompt");
                ShowStyledDialog("Error", $"Failed to export prompt:\n{ex.Message}", "OK", "");
            }
        }

        #endregion

        #region Patreon Exclusives Tab

        private void UpdatePatreonUI()
        {
            var tier = App.Patreon?.CurrentTier ?? PatreonTier.None;
            var isAuthenticated = App.Patreon?.IsAuthenticated ?? false;
            var isActivePatron = App.Patreon?.IsActivePatron ?? false;

            // Update login status
            if (isAuthenticated)
            {
                var patronName = App.Patreon?.PatronName;
                var patronEmail = App.Patreon?.PatronEmail;
                var displayName = App.Patreon?.DisplayName;
                var isWhitelisted = App.Patreon?.IsWhitelisted == true;

                // Debug: Log what we're getting from Patreon
                App.Logger?.Debug("Patreon UI Update: Name={Name}, Email={Email}, DisplayName={DisplayName}, Tier={Tier}, Whitelisted={Whitelisted}",
                    patronName, patronEmail, displayName, tier, isWhitelisted);

                // Show DisplayName if user chose one, otherwise fall back to PatronName
                var nameToShow = !string.IsNullOrEmpty(displayName) ? displayName : patronName;
                TxtPatreonStatus.Text = string.IsNullOrEmpty(nameToShow) ? "Connected to Patreon" : $"Welcome, {nameToShow}!";
                TxtPatreonTier.Text = tier switch
                {
                    PatreonTier.Level2 => "Level 2 Patron - All features unlocked!",
                    PatreonTier.Level1 => "Level 1 Patron - All features unlocked!",
                    _ when isWhitelisted => "Whitelisted - All features unlocked!",
                    _ => isActivePatron ? "Patron - Thank you for your support!" : "Connected - Subscribe to unlock features"
                };
                BtnPatreonLogin.Content = "Logout";
            }
            else
            {
                TxtPatreonStatus.Text = "Not Connected";
                TxtPatreonTier.Text = "Login to unlock exclusive features";
                BtnPatreonLogin.Content = "Login with Patreon";
            }

            // Update feature lockboxes
            // All features are now Tier 1 (or whitelisted)
            var hasPremiumAccess = App.Patreon?.HasPremiumAccess == true;
            var level1Unlocked = hasPremiumAccess;
            var level2Unlocked = hasPremiumAccess; // Same as Level 1 now - all features at Tier 1

            AiChatLocked.Visibility = level1Unlocked ? Visibility.Collapsed : Visibility.Visible;
            AiChatUnlocked.Visibility = level1Unlocked ? Visibility.Visible : Visibility.Collapsed;

            AwarenessLocked.Visibility = level2Unlocked ? Visibility.Collapsed : Visibility.Visible;
            AwarenessUnlocked.Visibility = level2Unlocked ? Visibility.Visible : Visibility.Collapsed;

            // Slut Mode lock overlay
            SlutModeLocked.Visibility = hasPremiumAccess ? Visibility.Collapsed : Visibility.Visible;
            ChkSlutMode.IsEnabled = hasPremiumAccess;

            // Haptics - unlock for all Patreon supporters
            var hasHapticsAccess = hasPremiumAccess;
            HapticsContentGrid.Opacity = hasHapticsAccess ? 1.0 : 0.3;
            HapticsContentGrid.IsHitTestVisible = hasHapticsAccess;
            HapticsConnectionLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsFeatureLock.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;
            HapticsConnectionBox.IsEnabled = hasHapticsAccess;
            HapticsFeatureBox.IsEnabled = hasHapticsAccess;

            // Hide "Coming Soon" overlay for Patreon supporters
            HapticsComingSoonOverlay.Visibility = hasHapticsAccess ? Visibility.Collapsed : Visibility.Visible;

            // Bambi Takeover (Autonomy) lock
            if (AutonomyLocked != null) AutonomyLocked.Visibility = hasPremiumAccess ? Visibility.Collapsed : Visibility.Visible;
            if (AutonomyUnlocked != null) AutonomyUnlocked.Visibility = hasPremiumAccess ? Visibility.Visible : Visibility.Collapsed;

            // Update connection status
            if (TxtAiStatus != null)
            {
                if (!isAuthenticated)
                {
                    TxtAiStatus.Text = "Login with Patreon to access AI features";
                }
                else if (hasPremiumAccess && App.Ai?.IsAvailable == true)
                {
                    TxtAiStatus.Text = $"AI Ready - {App.Ai.DailyRequestsRemaining} requests remaining today";
                }
                else if (hasPremiumAccess)
                {
                    TxtAiStatus.Text = "Patreon verified - AI service ready";
                }
                else
                {
                    TxtAiStatus.Text = "Subscribe to Level 1 ($5/mo) to unlock AI Chat";
                }
            }

            // Update XP bar login state when Patreon auth changes
            UpdateXPBarLoginState();
        }

        private async void BtnPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon == null) return;

            if (App.Patreon.IsAuthenticated)
            {
                // Logout
                App.ProfileSync?.StopHeartbeat();
                App.Patreon.Logout();
                App.Patreon.UnifiedUserId = null;
                App.UnifiedUserId = null;
                UpdatePatreonUI();
                UpdateBannerWelcomeMessage();
            }
            else
            {
                // Start OAuth flow
                BtnPatreonLogin.IsEnabled = false;
                BtnPatreonLogin.Content = "Connecting...";

                try
                {
                    await App.Patreon.StartOAuthFlowAsync();

                    // Use unified account flow - handles lookup, registration, and linking
                    var success = await AccountService.HandlePostAuthAsync(this, "patreon");

                    if (success)
                    {
                        // Update banner with welcome message
                        UpdateBannerWelcomeMessage();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Patreon login failed");
                    MessageBox.Show(
                        $"Failed to connect to Patreon.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    BtnPatreonLogin.IsEnabled = true;
                    UpdatePatreonUI();
                }
            }
        }

        private async void BtnDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            if (App.Discord == null) return;

            if (App.Discord.IsAuthenticated)
            {
                // Logout
                App.Discord.Logout();
                App.Discord.UnifiedUserId = null;
                App.UnifiedUserId = null;
                UpdateDiscordUI();
                UpdateBannerWelcomeMessage();
            }
            else
            {
                // Start OAuth flow
                BtnDiscordLogin.IsEnabled = false;
                BtnDiscordLogin.Content = "Connecting...";

                try
                {
                    await App.Discord.StartOAuthFlowAsync();

                    // Use unified account flow - handles lookup, registration, and linking
                    var success = await AccountService.HandlePostAuthAsync(this, "discord");

                    if (success)
                    {
                        UpdateDiscordUI();
                        UpdateBannerWelcomeMessage();

                        // Update bandwidth display (Discord users can inherit Patreon benefits via linked display name)
                        _ = UpdateBandwidthDisplayAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled - ignore
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Discord login failed");
                    MessageBox.Show(
                        $"Failed to connect to Discord.\n\n{ex.Message}",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                finally
                {
                    BtnDiscordLogin.IsEnabled = true;
                    UpdateDiscordUI();
                }
            }
        }

        private void UpdateDiscordUI()
        {
            if (App.Discord?.IsAuthenticated == true)
            {
                TxtDiscordStatus.Text = $"Connected as {App.Discord.DisplayName}";
                TxtDiscordInfo.Text = $"@{App.Discord.Username}";
                BtnDiscordLogin.Content = "Logout";
            }
            else
            {
                TxtDiscordStatus.Text = "Not Connected";
                TxtDiscordInfo.Text = "Link Discord for community features";
                BtnDiscordLogin.Content = "Login with Discord";
            }

            // Update XP bar login state when Discord auth changes
            UpdateXPBarLoginState();
        }

        private void ChkShareAchievements_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.DiscordShareAchievements = ChkShareAchievements.IsChecked == true;
            }
        }

        private void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.DiscordShareLevelUps = ChkShareLevelUps.IsChecked == true;
            }
        }

        private void ChkUseAnonymousName_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.DiscordUseAnonymousName = ChkUseAnonymousName.IsChecked == true;
            }
        }

        private void ChkShowLevelInPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.DiscordShowLevelInPresence = ChkShowLevelInPresence.IsChecked == true;
                // Update presence immediately to reflect change
                App.DiscordRpc?.UpdateLevel(App.Settings.Current.PlayerLevel);
            }
        }

        private async void ChkAllowDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AllowDiscordDm = ChkAllowDiscordDm.IsChecked == true;
                // Sync immediately so the setting takes effect on the leaderboard
                if (App.ProfileSync != null)
                {
                    await App.ProfileSync.SyncProfileAsync();
                }
            }
        }

        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Patreon page");
            }
        }

        private void OnPatreonTierChanged(object? sender, PatreonTier tier)
        {
            Dispatcher.Invoke(() => UpdatePatreonUI());
        }

        private void InitializePatreonTab()
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Subscribe to Patreon tier changes
            if (App.Patreon != null)
            {
                App.Patreon.TierChanged += OnPatreonTierChanged;
            }

            // Initialize companion settings
            ChkAvatarEnabledCompanion.IsChecked = settings.AvatarEnabled;
            ChkAiChat.IsChecked = settings.AiChatEnabled;
            SliderIdleIntervalCompanion.Value = settings.IdleGiggleIntervalSeconds;
            TxtIdleIntervalCompanion.Text = $"{settings.IdleGiggleIntervalSeconds}s";

            // Awareness Mode settings (now Tier 1 / whitelisted)
            var awarenessAvailable = App.Patreon?.HasPremiumAccess == true;
            ChkAwarenessMode.IsChecked = awarenessAvailable && settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            SliderAwarenessCooldown.Value = settings.AwarenessReactionCooldownSeconds;
            TxtAwarenessCooldown.Text = $"{settings.AwarenessReactionCooldownSeconds}s";

            // Show/hide awareness settings panel based on enabled state
            var awarenessEnabled = awarenessAvailable && settings.AwarenessModeEnabled && settings.AwarenessConsentGiven;
            AwarenessSettingsPanel.Visibility = awarenessEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Trigger Mode settings (free for all)
            ChkTriggerModeCompanion.IsChecked = settings.TriggerModeEnabled;
            SliderTriggerIntervalCompanion.Value = settings.TriggerIntervalSeconds;
            TxtTriggerIntervalCompanion.Text = $"{settings.TriggerIntervalSeconds}s";
            TriggerSettingsPanelCompanion.Visibility = settings.TriggerModeEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Slut Mode settings (Patreon only)
            var slutModeAvailable = App.Patreon?.HasPremiumAccess == true;
            ChkSlutMode.IsChecked = slutModeAvailable && settings.SlutModeEnabled;
            SlutModeLocked.Visibility = slutModeAvailable ? Visibility.Collapsed : Visibility.Visible;

            // Hide avatar if disabled
            if (!settings.AvatarEnabled)
            {
                HideAvatarTube();
            }

            UpdatePatreonUI();
        }

        private void ChkAvatarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            App.Settings.Current.AvatarEnabled = isEnabled;

            if (isEnabled)
            {
                ShowAvatarTube();
            }
            else
            {
                HideAvatarTube();
            }

            App.Settings.Save();
        }

        private void BtnDetachCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (_avatarTubeWindow == null) return;

            _avatarTubeWindow.ToggleDetached();

            // Update button and status text
            if (_avatarTubeWindow.IsDetached)
            {
                BtnDetachCompanionTab.Content = "Attach";
                TxtDetachStatusCompanion.Text = "Floating freely - drag to reposition";
            }
            else
            {
                BtnDetachCompanionTab.Content = "Detach";
                TxtDetachStatusCompanion.Text = "Anchored to window";
            }
        }

        private void BtnCustomizeCompanion_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CompanionPromptEditorDialog
            {
                Owner = this
            };
            dialog.ShowDialog();

            // Refresh UI to reflect any prompt changes
            UpdateCommunityPromptsUI();
        }

        private void ChkAiChat_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            App.Settings.Current.AiChatEnabled = ChkAiChat.IsChecked == true;
            App.Settings.Save();
        }

        private void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtIdleIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 120);
            TxtIdleIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.IdleGiggleIntervalSeconds = value;
            App.Settings.Save();
        }

        // ============================================================
        // TRIGGER MODE (Free for all)
        // ============================================================

        private void ChkTriggerMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            App.Settings.Current.TriggerModeEnabled = isEnabled;
            App.Settings.Save();

            // Restart trigger timer on avatar window
            _avatarTubeWindow?.RestartTriggerTimer();

            App.Logger?.Information("Trigger Mode {State}", isEnabled ? "enabled" : "disabled");
        }

        /// <summary>
        /// Sync the Trigger Mode UI when changed from avatar context menu
        /// </summary>
        public void SyncTriggerModeUI(bool isEnabled)
        {
            ChkTriggerModeCompanion.IsChecked = isEnabled;
            TriggerSettingsPanelCompanion.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SliderTriggerInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTriggerIntervalCompanion == null) return;

            var slider = sender as Slider;
            var value = (int)(slider?.Value ?? 60);
            TxtTriggerIntervalCompanion.Text = $"{value}s";
            App.Settings.Current.TriggerIntervalSeconds = value;

            // Restart trigger timer with new interval
            _avatarTubeWindow?.RestartTriggerTimer();
        }

        private void BtnEditTriggers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Convert List<string> to Dictionary<string, bool> for the editor
                // Use Distinct() to handle any duplicate triggers that could crash ToDictionary
                var triggers = App.Settings.Current.CustomTriggers ?? new List<string>();
                var triggerDict = triggers
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(t => t, _ => true);

                // Note: We no longer auto-populate defaults when empty.
                // Users can add triggers manually via the editor if they want them.
                // This fixes the bug where removed triggers would reappear.

                var dialog = new TextEditorDialog("Trigger Phrases", triggerDict);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.ResultData != null)
                {
                    // Get only enabled triggers
                    var newTriggers = dialog.ResultData
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    App.Settings.Current.CustomTriggers = newTriggers;
                    App.Settings.Save();
                    App.Logger?.Information("Updated {Count} custom triggers", newTriggers.Count);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open trigger editor");
                MessageBox.Show($"Error opening trigger editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        // SLUT MODE (Patreon only)
        // ============================================================

        private void ChkSlutMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkSlutMode.IsChecked == true;

            // Check Patreon access
            if (isEnabled && App.Patreon?.HasPremiumAccess != true)
            {
                ChkSlutMode.IsChecked = false;
                MessageBox.Show(
                    "Slut Mode requires Patreon subscription.",
                    "Patreon Only",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            App.Settings.Current.SlutModeEnabled = isEnabled;
            App.Settings.Save();

            App.Logger?.Information("Slut Mode {State}", isEnabled ? "enabled" : "disabled");
        }

        private void ChkAwarenessMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkAwarenessMode.IsChecked == true;

            // Check Patreon access (Tier 1+ or whitelisted)
            if (isEnabled && App.Patreon?.HasPremiumAccess != true)
            {
                ChkAwarenessMode.IsChecked = false;
                MessageBox.Show(
                    "Window Awareness requires Patreon subscription.",
                    "Patreon Only",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Show/hide awareness settings panel
            AwarenessSettingsPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Update settings
            App.Settings.Current.AwarenessModeEnabled = isEnabled;
            App.Settings.Current.AwarenessConsentGiven = isEnabled; // Auto-consent when enabling via UI
            App.Settings.Save();

            // Start or stop the awareness service
            if (isEnabled)
            {
                App.WindowAwareness?.Start();
                App.Logger?.Information("Awareness Mode enabled via UI");
            }
            else
            {
                App.WindowAwareness?.Stop();
                App.Logger?.Information("Awareness Mode disabled via UI");
            }
        }

        private void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (TxtPrivacyDetails.Visibility == Visibility.Collapsed)
            {
                TxtPrivacyDetails.Visibility = Visibility.Visible;
                BtnPrivacySpoiler.Content = "▼ Hide";
            }
            else
            {
                TxtPrivacyDetails.Visibility = Visibility.Collapsed;
                BtnPrivacySpoiler.Content = "▶ Click to reveal";
            }
        }

        private void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAwarenessCooldown == null) return;

            var value = (int)SliderAwarenessCooldown.Value;
            TxtAwarenessCooldown.Text = $"{value}s";
            App.Settings.Current.AwarenessReactionCooldownSeconds = value;
            App.Settings.Save();
        }

        #region Haptics Handlers

        private void ChkHapticsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = ChkHapticsEnabled.IsChecked == true;

            // Check Patreon access when enabling
            if (isEnabled && App.Patreon?.HasPremiumAccess != true)
            {
                ChkHapticsEnabled.IsChecked = false;
                MessageBox.Show(
                    "Haptic feedback is available for Patreon supporters.",
                    "Patreon Feature",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            App.Settings.Current.Haptics.Enabled = isEnabled;
            App.Settings.Save();
        }

        private void CmbHapticProvider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || CmbHapticProvider.SelectedItem == null) return;

            var item = CmbHapticProvider.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var tag = item?.Tag?.ToString();

            App.Settings.Current.Haptics.Provider = tag switch
            {
                "Mock" => Services.Haptics.HapticProviderType.Mock,
                "Lovense" => Services.Haptics.HapticProviderType.Lovense,
                "Buttplug" => Services.Haptics.HapticProviderType.Buttplug,
                _ => Services.Haptics.HapticProviderType.Mock
            };

            // Load the saved URL for the selected provider (or use default)
            if (TxtHapticUrl != null)
            {
                var url = tag switch
                {
                    "Lovense" => App.Settings.Current.Haptics.LovenseUrl,
                    "Buttplug" => App.Settings.Current.Haptics.ButtplugUrl,
                    _ => ""
                };
                TxtHapticUrl.Text = url;
            }

            // Update hint text based on provider
            if (TxtHapticUrlHint != null)
            {
                TxtHapticUrlHint.Text = tag switch
                {
                    "Lovense" => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)",
                    "Buttplug" => "Buttplug: Start Intiface Central, use default ws://localhost:12345",
                    _ => ""
                };
            }

            App.Settings.Save();
        }

        private void BtnHapticsHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HapticsSetupWindow
            {
                Owner = this
            };
            helpWindow.ShowDialog();
        }

        private async void BtnHapticConnect_Click(object sender, RoutedEventArgs e)
        {
            // Check Patreon access
            if (App.Patreon?.HasPremiumAccess != true)
            {
                MessageBox.Show(
                    "Haptic feedback is available for Patreon supporters.",
                    "Patreon Feature",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (App.Haptics == null) return;

            if (App.Haptics.IsConnected)
            {
                await App.Haptics.DisconnectAsync();
                BtnHapticConnect.Content = "Connect";
                TxtHapticStatus.Text = "Disconnected";
                TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
                TxtHapticDevices.Text = "No devices";
            }
            else
            {
                BtnHapticConnect.Content = "Connecting...";
                BtnHapticConnect.IsEnabled = false;

                try
                {
                    var success = await App.Haptics.ConnectAsync();

                    if (success)
                    {
                        BtnHapticConnect.Content = "Disconnect";
                        TxtHapticStatus.Text = "Connected";
                        TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));

                        var devices = App.Haptics.ConnectedDevices;
                        TxtHapticDevices.Text = devices.Count > 0
                            ? string.Join(", ", devices)
                            : "No devices found";
                    }
                    else
                    {
                        BtnHapticConnect.Content = "Connect";
                        TxtHapticStatus.Text = "Failed";
                        TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
                    }
                }
                catch (Exception ex)
                {
                    BtnHapticConnect.Content = "Connect";
                    TxtHapticStatus.Text = "Error";
                    TxtHapticDevices.Text = ex.Message;
                }
                finally
                {
                    BtnHapticConnect.IsEnabled = true;
                }
            }
        }

        private System.Threading.CancellationTokenSource? _hapticSliderCts;
        private System.Windows.Threading.DispatcherTimer? _hapticSliderDebounce;

        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtHapticIntensity == null) return;

            var value = (int)SliderHapticIntensity.Value;
            TxtHapticIntensity.Text = $"{value}%";
            App.Settings.Current.Haptics.GlobalIntensity = value / 100.0;

            // Debounce: wait 150ms after slider stops moving before sending command
            _hapticSliderDebounce?.Stop();
            _hapticSliderDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _hapticSliderDebounce.Tick += (s, args) =>
            {
                _hapticSliderDebounce?.Stop();
                if (App.Haptics != null && App.Haptics.IsConnected && App.Settings.Current.Haptics.Enabled)
                {
                    _ = App.Haptics.LiveIntensityUpdateAsync(value / 100.0);
                }
            };
            _hapticSliderDebounce.Start();
        }

        private async void BtnHapticTest_Click(object sender, RoutedEventArgs e)
        {
            if (App.Haptics == null) return;

            if (!App.Haptics.IsConnected)
            {
                MessageBox.Show("Connect to a device first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await App.Haptics.TestAsync();
        }
        private void TxtHapticUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || TxtHapticUrl == null) return;

            // Save to the appropriate URL based on current provider
            var provider = App.Settings.Current.Haptics.Provider;
            if (provider == Services.Haptics.HapticProviderType.Lovense)
                App.Settings.Current.Haptics.LovenseUrl = TxtHapticUrl.Text;
            else if (provider == Services.Haptics.HapticProviderType.Buttplug)
                App.Settings.Current.Haptics.ButtplugUrl = TxtHapticUrl.Text;
        }

        private void ChkHapticFeature_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            if (checkbox == null) return;

            var tag = checkbox.Tag?.ToString();
            var isEnabled = checkbox.IsChecked == true;
            var haptics = App.Settings.Current.Haptics;

            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopEnabled = isEnabled;
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayEnabled = isEnabled;
                    break;
                case "FlashClick":
                    haptics.FlashClickEnabled = isEnabled;
                    break;
                case "Video":
                    haptics.VideoEnabled = isEnabled;
                    break;
                case "TargetHit":
                    haptics.TargetHitEnabled = isEnabled;
                    break;
                case "Subliminal":
                    haptics.SubliminalEnabled = isEnabled;
                    break;
                case "LevelUp":
                    haptics.LevelUpEnabled = isEnabled;
                    break;
                case "Achievement":
                    haptics.AchievementEnabled = isEnabled;
                    break;
                case "BouncingText":
                    haptics.BouncingTextEnabled = isEnabled;
                    break;
            }
        }

        private System.Windows.Threading.DispatcherTimer? _hapticFeatureDebounce;

        private void SliderHapticFeature_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var slider = sender as Slider;
            if (slider == null) return;

            var tag = slider.Tag?.ToString();
            var value = slider.Value / 100.0;
            var haptics = App.Settings.Current.Haptics;

            // Update setting and text label
            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopIntensity = value;
                    if (TxtHapticBubble != null) TxtHapticBubble.Text = $"{(int)slider.Value}%";
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayIntensity = value;
                    if (TxtHapticFlashDisplay != null) TxtHapticFlashDisplay.Text = $"{(int)slider.Value}%";
                    break;
                case "FlashClick":
                    haptics.FlashClickIntensity = value;
                    if (TxtHapticFlashClick != null) TxtHapticFlashClick.Text = $"{(int)slider.Value}%";
                    break;
                case "Video":
                    haptics.VideoIntensity = value;
                    if (TxtHapticVideo != null) TxtHapticVideo.Text = $"{(int)slider.Value}%";
                    break;
                case "TargetHit":
                    haptics.TargetHitIntensity = value;
                    if (TxtHapticTargetHit != null) TxtHapticTargetHit.Text = $"{(int)slider.Value}%";
                    break;
                case "Subliminal":
                    haptics.SubliminalIntensity = value;
                    if (TxtHapticSubliminal != null) TxtHapticSubliminal.Text = $"{(int)slider.Value}%";
                    break;
                case "LevelUp":
                    haptics.LevelUpIntensity = value;
                    if (TxtHapticLevelUp != null) TxtHapticLevelUp.Text = $"{(int)slider.Value}%";
                    break;
                case "Achievement":
                    haptics.AchievementIntensity = value;
                    if (TxtHapticAchievement != null) TxtHapticAchievement.Text = $"{(int)slider.Value}%";
                    break;
                case "BouncingText":
                    haptics.BouncingTextIntensity = value;
                    if (TxtHapticBouncingText != null) TxtHapticBouncingText.Text = $"{(int)slider.Value}%";
                    break;
            }

            // Debounce: wait 150ms after slider stops moving before sending live vibration
            _hapticFeatureDebounce?.Stop();
            _hapticFeatureDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _hapticFeatureDebounce.Tick += (s, args) =>
            {
                _hapticFeatureDebounce?.Stop();
                if (App.Haptics != null && App.Haptics.IsConnected && App.Settings.Current.Haptics.Enabled)
                {
                    // Live preview at this intensity level
                    _ = App.Haptics.LiveIntensityUpdateAsync(value);
                }
            };
            _hapticFeatureDebounce.Start();
        }

        private void CmbHapticMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var combo = sender as ComboBox;
            if (combo == null) return;

            var tag = combo.Tag?.ToString();
            var mode = (Models.VibrationMode)combo.SelectedIndex;
            var haptics = App.Settings.Current.Haptics;

            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopMode = mode;
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayMode = mode;
                    break;
                case "FlashClick":
                    haptics.FlashClickMode = mode;
                    break;
                case "Video":
                    haptics.VideoMode = mode;
                    break;
                case "TargetHit":
                    haptics.TargetHitMode = mode;
                    break;
                case "Subliminal":
                    haptics.SubliminalMode = mode;
                    break;
                case "LevelUp":
                    haptics.LevelUpMode = mode;
                    break;
                case "Achievement":
                    haptics.AchievementMode = mode;
                    break;
                case "BouncingText":
                    haptics.BouncingTextMode = mode;
                    break;
            }
        }

        #endregion

        private void ChkMuteAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isEnabled = checkbox?.IsChecked == true;
            _avatarTubeWindow?.SetMuteAvatar(isEnabled);
        }

        private void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isMuted = checkbox?.IsChecked == true;

            // Toggle SubAudioEnabled (muted = disabled)
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !isMuted;
                App.Settings.Save();
            }

            // Sync Settings tab checkbox (inverted - it's "enabled" not "muted")
            _isLoading = true;
            ChkAudioWhispers.IsChecked = !isMuted;
            _isLoading = false;

            // Sync avatar menu
            _avatarTubeWindow?.UpdateQuickMenuState();
        }

        private async void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            var isPaused = checkbox?.IsChecked == true;
            await SetBrowserPaused(isPaused);
            _avatarTubeWindow?.SetBrowserPaused(isPaused);
        }

        private async Task SetBrowserPaused(bool isPaused)
        {
            try
            {
                var webView = GetBrowserWebView();
                if (webView?.CoreWebView2 != null)
                {
                    if (isPaused)
                    {
                        webView.CoreWebView2.IsMuted = true;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.pause());
                        ");
                    }
                    else
                    {
                        webView.CoreWebView2.IsMuted = false;
                        await webView.CoreWebView2.ExecuteScriptAsync(@"
                            document.querySelectorAll('audio, video').forEach(el => el.play());
                        ");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to toggle browser audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Sync Quick Controls UI from avatar context menu
        /// </summary>
        public void SyncQuickControlsUI(bool? muteAvatar = null, bool? muteWhispers = null, bool? pauseBrowser = null)
        {
            _isLoading = true;
            try
            {
                // Update Companion tab controls
                if (muteAvatar.HasValue) ChkMuteAvatarCompanion.IsChecked = muteAvatar.Value;
                if (muteWhispers.HasValue) ChkMuteWhispersCompanion.IsChecked = muteWhispers.Value;
                if (pauseBrowser.HasValue) ChkPauseBrowserCompanion.IsChecked = pauseBrowser.Value;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Sync whispers enabled state across all UI controls (Settings tab + Companion tab)
        /// </summary>
        public void SyncWhispersUI(bool enabled)
        {
            _isLoading = true;
            try
            {
                // Settings tab - ChkAudioWhispers represents "whispers enabled"
                ChkAudioWhispers.IsChecked = enabled;

                // Companion tab - ChkMuteWhispersCompanion represents "whispers muted" (inverted)
                ChkMuteWhispersCompanion.IsChecked = !enabled;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Sync slut mode state across all UI controls
        /// </summary>
        public void SyncSlutModeUI(bool enabled)
        {
            _isLoading = true;
            try
            {
                ChkSlutMode.IsChecked = enabled;
            }
            finally
            {
                _isLoading = false;
            }
        }

        #endregion

        #region Banner Rotation

        private void InitializeBannerRotation()
        {
            // Start the rotation timer (switches every 4 seconds)
            _bannerRotationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _bannerRotationTimer.Tick += BannerRotationTimer_Tick;

            // Update welcome message based on login status
            UpdateBannerWelcomeMessage();

            // Always start rotation now (we have 3 messages including the thanks message)
            _bannerRotationTimer.Start();
        }

        private void UpdateBannerWelcomeMessage()
        {
            // Check both Patreon and Discord for display name
            var displayName = App.Patreon?.DisplayName ?? App.Discord?.DisplayName;
            if (!string.IsNullOrEmpty(displayName))
            {
                TxtBannerSecondary.Text = $"Welcome back, {displayName}!";
            }
            else
            {
                // Not logged in - show generic welcome
                TxtBannerSecondary.Text = "Welcome! Consider logging in with Patreon for extra features.";
            }
        }

        /// <summary>
        /// Flag to indicate when a startup dialog (What's New) is showing.
        /// Used to prevent update dialog from showing behind it.
        /// </summary>
        public static bool IsStartupDialogShowing { get; set; } = false;

        /// <summary>
        /// Shows a "What's New" dialog if the app was updated since last launch
        /// </summary>
        private void ShowWhatsNewIfNeeded()
        {
            try
            {
                var currentVersion = Services.UpdateService.AppVersion;
                var lastSeenVersion = App.Settings?.Current?.LastSeenVersion ?? "";

                // If versions differ, show the patch notes
                if (lastSeenVersion != currentVersion)
                {
                    App.Logger?.Information("Version changed from {OldVersion} to {NewVersion}, showing What's New",
                        string.IsNullOrEmpty(lastSeenVersion) ? "(none)" : lastSeenVersion, currentVersion);

                    // Delay slightly to let the window fully load
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Set flag BEFORE showing MessageBox so update dialog knows to wait
                            IsStartupDialogShowing = true;
                            App.Logger?.Information("What's New dialog showing, setting IsStartupDialogShowing=true");

                            MessageBox.Show(
                                Services.UpdateService.CurrentPatchNotes,
                                $"What's New in v{currentVersion}",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            // Update the last seen version
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.LastSeenVersion = currentVersion;
                                App.Settings.Save();
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "Failed to show What's New dialog");
                        }
                        finally
                        {
                            // Clear flag AFTER MessageBox is dismissed
                            IsStartupDialogShowing = false;
                            App.Logger?.Information("What's New dialog dismissed, setting IsStartupDialogShowing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking for What's New");
            }
        }

        private void BannerRotationTimer_Tick(object? sender, EventArgs e)
        {
            // Get the 3 banner textblocks
            var banners = new[] { TxtBannerPrimary, TxtBannerSecondary, TxtBannerTertiary };

            // Determine which one to fade out and which to fade in
            var fadeOutTarget = banners[_bannerCurrentIndex];
            var nextIndex = (_bannerCurrentIndex + 1) % 3;
            var fadeInTarget = banners[nextIndex];

            // Create fade animations
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            // Apply animations
            fadeOutTarget.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fadeInTarget.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Disable hit testing on faded-out banner so hyperlinks don't capture clicks
            // (hyperlinks can still receive clicks even at Opacity=0)
            fadeOutTarget.IsHitTestVisible = false;
            fadeInTarget.IsHitTestVisible = true;

            _bannerCurrentIndex = nextIndex;
        }

        /// <summary>
        /// Set a temporary announcement message to display in the banner rotation
        /// </summary>
        public void SetBannerAnnouncement(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            TxtBannerSecondary.Text = message;

            // Ensure timer is running
            if (_bannerRotationTimer != null && !_bannerRotationTimer.IsEnabled)
            {
                _bannerRotationTimer.Start();
            }
        }

        #endregion

        private void PopulateAchievementGrid()
        {
            if (AchievementGrid == null) return;
            
            AchievementGrid.Children.Clear();
            _achievementImages.Clear();
            
            var tileStyle = FindResource("AchievementTile") as Style;
            
            // Add all achievements
            foreach (var kvp in Models.Achievement.All)
            {
                var achievement = kvp.Value;
                var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievement.Id) ?? false;
                
                var border = new Border { Style = tileStyle };
                border.ToolTip = isUnlocked
                    ? $"{achievement.Name}\n\n\"{achievement.FlavorText}\""
                    : $"???\n\nRequirement: {achievement.Requirement}";

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Source = LoadAchievementImage(achievement.ImageName)
                };

                // Apply blur if locked
                if (!isUnlocked)
                {
                    image.Effect = new BlurEffect { Radius = 15 };
                }

                border.Child = image;
                AchievementGrid.Children.Add(border);

                // Store reference for later updates
                _achievementImages[achievement.Id] = image;
            }
            
            // Note: All placeholders have been replaced with real achievements
            
            UpdateAchievementCount();
            App.Logger?.Information("Achievement grid populated with {Count} achievements", _achievementImages.Count);
        }
        
        private BitmapImage? LoadAchievementImage(string imageName)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute);
                return new BitmapImage(uri);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load achievement image {Name}: {Error}", imageName, ex.Message);
                return null;
            }
        }
        
        private void RefreshAchievementTile(string achievementId)
        {
            if (!_achievementImages.TryGetValue(achievementId, out var image)) return;

            var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievementId) ?? false;

            // Update blur
            image.Effect = isUnlocked ? null : new BlurEffect { Radius = 15 };

            // Update tooltip
            if (Models.Achievement.All.TryGetValue(achievementId, out var achievement))
            {
                var parent = image.Parent as Border;
                if (parent != null)
                {
                    parent.ToolTip = isUnlocked
                        ? $"{achievement.Name}\n\n\"{achievement.FlavorText}\""
                        : $"???\n\nRequirement: {achievement.Requirement}";
                }
            }

            UpdateAchievementCount();
        }

        private void RefreshAllAchievementTiles()
        {
            // Refresh all achievement tiles to reflect current unlock state
            foreach (var achievementId in _achievementImages.Keys.ToList())
            {
                RefreshAchievementTile(achievementId);
            }
            App.Logger?.Debug("All achievement tiles refreshed");
        }

        private void OnAchievementUnlockedInMainWindow(object? sender, Models.Achievement achievement)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAchievementTile(achievement.Id);
                App.Logger?.Information("Achievement tile refreshed: {Name}", achievement.Name);
            });
        }

        #endregion

        #region Presets

        private Models.Preset? _selectedPreset;
        private List<Models.Preset> _allPresets = new();

        private void InitializePresets()
        {
            // Load default presets + user presets
            _allPresets = Models.Preset.GetDefaultPresets();
            _allPresets.AddRange(App.Settings.Current.UserPresets);
            
            // Populate the header dropdown
            RefreshPresetsDropdown();
        }

        private void RefreshPresetsDropdown()
        {
            _isLoading = true;
            CmbPresets.Items.Clear();

            // Add all presets - use light text for dark dropdown background
            foreach (var preset in _allPresets)
            {
                CmbPresets.Items.Add(new ComboBoxItem
                {
                    Content = preset.Name,
                    Tag = preset.Id,
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)) // Light gray #E0E0E0
                });
            }

            // Add separator and "Save New" option
            CmbPresets.Items.Add(new Separator());
            CmbPresets.Items.Add(new ComboBoxItem
            {
                Content = "➕ Save as New Preset...",
                Tag = "new",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 180)) // Bright pink for visibility
            });

            // Select current preset
            var currentName = App.Settings.Current.CurrentPresetName;
            for (int i = 0; i < CmbPresets.Items.Count; i++)
            {
                if (CmbPresets.Items[i] is ComboBoxItem item && item.Content?.ToString() == currentName)
                {
                    CmbPresets.SelectedIndex = i;
                    break;
                }
            }

            _isLoading = false;
        }

        private void CmbPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbPresets.SelectedItem is not ComboBoxItem item) return;
            
            var tag = item.Tag?.ToString();
            
            if (tag == "new")
            {
                // Show save new preset dialog
                PromptSaveNewPreset();
                // Reset selection to current
                RefreshPresetsDropdown();
                return;
            }
            
            // Find and load the preset
            var preset = _allPresets.FirstOrDefault(p => p.Id == tag);
            if (preset != null)
            {
                var result = MessageBox.Show(
                    $"Load preset '{preset.Name}'?\n\nThis will replace your current settings.",
                    "Load Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                    
                if (result == MessageBoxResult.Yes)
                {
                    LoadPreset(preset);
                }
                else
                {
                    RefreshPresetsDropdown();
                }
            }
        }

        private void RefreshPresetsList()
        {
            PresetCardsPanel.Children.Clear();
            _allPresets = Models.Preset.GetDefaultPresets();
            _allPresets.AddRange(App.Settings.Current.UserPresets);
            
            foreach (var preset in _allPresets)
            {
                var card = CreatePresetCard(preset);
                PresetCardsPanel.Children.Add(card);
            }
        }

        private Border CreatePresetCard(Models.Preset preset)
        {
            var isSelected = _selectedPreset?.Id == preset.Id;
            var pinkBrush = FindResource("PinkBrush") as SolidColorBrush;
            
            var card = new Border
            {
                Background = new SolidColorBrush(isSelected ? Color.FromRgb(60, 60, 100) : Color.FromRgb(42, 42, 74)),
                BorderBrush = isSelected ? pinkBrush : new SolidColorBrush(Color.FromRgb(64, 64, 96)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 6, 0),
                Width = 100,
                Height = 70,
                Cursor = Cursors.Hand,
                Tag = preset.Id
            };
            
            card.MouseLeftButtonDown += (s, e) => SelectPreset(preset);
            card.MouseEnter += (s, e) => {
                if (_selectedPreset?.Id != preset.Id)
                    card.BorderBrush = pinkBrush;
            };
            card.MouseLeave += (s, e) => {
                if (_selectedPreset?.Id != preset.Id)
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 96));
            };
            
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            
            // Name
            var nameText = new TextBlock
            {
                Text = preset.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(nameText);
            
            // Badge
            var badge = new TextBlock
            {
                Text = preset.IsDefault ? "DEFAULT" : "CUSTOM",
                Foreground = preset.IsDefault ? pinkBrush : new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                FontSize = 7,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 1, 0, 0)
            };
            stack.Children.Add(badge);
            
            // Quick stats (icons only for compact view)
            var statsPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (preset.FlashEnabled) AddStatIcon(statsPanel, "⚡", 10);
            if (preset.MandatoryVideosEnabled) AddStatIcon(statsPanel, "🎬", 10);
            if (preset.SubliminalEnabled) AddStatIcon(statsPanel, "💭", 10);
            if (preset.SpiralEnabled) AddStatIcon(statsPanel, "🌀", 10);
            if (preset.LockCardEnabled) AddStatIcon(statsPanel, "🔒", 10);
            stack.Children.Add(statsPanel);
            
            card.Child = stack;
            return card;
        }
        
        private void AddStatIcon(WrapPanel panel, string icon, int size = 12)
        {
            panel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = size,
                Margin = new Thickness(0, 0, 2, 0)
            });
        }

        private string GetPresetQuickStats(Models.Preset preset)
        {
            var features = new List<string>();
            if (preset.FlashEnabled) features.Add("Flash");
            if (preset.MandatoryVideosEnabled) features.Add("Video");
            if (preset.SubliminalEnabled) features.Add("Subliminal");
            if (preset.SpiralEnabled) features.Add("Spiral");
            if (preset.PinkFilterEnabled) features.Add("Pink");
            if (preset.LockCardEnabled) features.Add("LockCard");
            
            return features.Count > 0 ? string.Join(" • ", features) : "Minimal";
        }

        private void SelectPreset(Models.Preset preset)
        {
            _selectedPreset = preset;
            _selectedSession = null;
            
            // Update cards UI
            RefreshPresetsList();
            
            // Show preset panel, hide session panel
            PresetDetailScroller.Visibility = Visibility.Visible;
            PresetButtonsPanel.Visibility = Visibility.Visible;
            SessionDetailScroller.Visibility = Visibility.Collapsed;
            SessionButtonsPanel.Visibility = Visibility.Collapsed;
            
            // Update detail panel
            TxtDetailTitle.Text = preset.Name;
            TxtDetailSubtitle.Text = preset.Description;
            
            TxtDetailFlash.Text = preset.FlashEnabled 
                ? $"Enabled | {preset.FlashFrequency}/hr | Opacity: {preset.FlashOpacity}%"
                : "Disabled";
                
            TxtDetailVideo.Text = preset.MandatoryVideosEnabled 
                ? $"Enabled | {preset.VideosPerHour}/hr | Strict: {(preset.StrictLockEnabled ? "Yes" : "No")}"
                : "Disabled";
                
            TxtDetailSubliminal.Text = preset.SubliminalEnabled 
                ? $"Enabled | {preset.SubliminalFrequency}/min | Opacity: {preset.SubliminalOpacity}%"
                : "Disabled";
                
            TxtDetailAudio.Text = $"Whispers: {(preset.SubAudioEnabled ? $"Yes ({preset.SubAudioVolume}%)" : "No")} | Master: {preset.MasterVolume}%";
            
            TxtDetailOverlays.Text = $"Spiral: {(preset.SpiralEnabled ? "Yes" : "No")} | Pink: {(preset.PinkFilterEnabled ? "Yes" : "No")}";
            
            TxtDetailAdvanced.Text = $"Bubbles: {(preset.BubblesEnabled ? "Yes" : "No")} | Lock Card: {(preset.LockCardEnabled ? "Yes" : "No")}";
            
            // Enable buttons
            BtnLoadPreset.IsEnabled = true;
            BtnSaveOverPreset.IsEnabled = !preset.IsDefault;
            BtnDeletePreset.IsEnabled = !preset.IsDefault;
        }
        
        private void SessionCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string sessionType)
            {
                // Find the session
                var session = GetSessionById(sessionType);

                if (session != null)
                {
                    SelectSession(session);

                    // Show corner GIF option if applicable
                    if (session.HasCornerGifOption)
                    {
                        TxtCornerGifDesc.Text = session.CornerGifDescription;
                        ChkCornerGifEnabled.IsChecked = false;
                        CornerGifSettings.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private Models.Session? _selectedSession;
        
        private void ChkCornerGifEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkCornerGifEnabled.IsChecked == true)
            {
                CornerGifSettings.Visibility = Visibility.Visible;
            }
            else
            {
                CornerGifSettings.Visibility = Visibility.Collapsed;
            }
        }
        
        private void BtnSelectCornerGif_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Corner GIF",
                Filter = "GIF files (*.gif)|*.gif|All files (*.*)|*.*",
                InitialDirectory = System.IO.Path.Combine(App.EffectiveAssetsPath, "images")
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedCornerGifPath = dialog.FileName;
                BtnSelectCornerGif.Content = $"📁 {System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }

        private void SliderCornerGifSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtCornerGifSize != null)
            {
                TxtCornerGifSize.Text = $"{(int)e.NewValue}px";
            }

            // Don't live update during session - causes crashes with animated GIFs
            // Size will be applied when session starts or restarts
        }

        private void SliderCornerGifOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtCornerGifOpacity != null)
            {
                TxtCornerGifOpacity.Text = $"{(int)e.NewValue}%";
            }

            // Live update during session
            if (_sessionEngine != null && _sessionEngine.IsRunning)
            {
                _sessionEngine.UpdateCornerGifOpacity((int)e.NewValue);
            }
        }

        private string _selectedCornerGifPath = "";
        
        private Models.CornerPosition GetSelectedCornerPosition()
        {
            if (RbCornerTL.IsChecked == true) return Models.CornerPosition.TopLeft;
            if (RbCornerTR.IsChecked == true) return Models.CornerPosition.TopRight;
            if (RbCornerBR.IsChecked == true) return Models.CornerPosition.BottomRight;
            return Models.CornerPosition.BottomLeft;
        }
        
        private void BtnRevealSpoilers_Click(object sender, RoutedEventArgs e)
        {
            if (SessionSpoilerPanel.Visibility == Visibility.Visible)
            {
                // Hide spoilers
                SessionSpoilerPanel.Visibility = Visibility.Collapsed;
                BtnRevealSpoilers.Content = "👁 Reveal Details";
                return;
            }
            
            // Sequential warnings
            var warning1 = ShowStyledDialog(
                "⚠ Spoiler Warning",
                "Are you sure you want to see the session details?\n\n" +
                "Part of the magic is not knowing what's coming...\n" +
                "The experience works best when you surrender to the unknown.\n\n" +
                "Do you really want to spoil the surprise?",
                "Yes, show me", "No, keep the mystery");
                
            if (!warning1) return;
            
            var warning2 = ShowStyledDialog(
                "💗 Second Warning",
                "Good girls trust the process...\n\n" +
                "You're about to see exactly what will happen.\n" +
                "Once you know, you can't un-know.\n\n" +
                "Last chance to keep the mystery alive.",
                "Continue anyway", "You're right, nevermind");
                
            if (!warning2) return;
            
            var warning3 = ShowStyledDialog(
                "🏁 Final Confirmation",
                "You're choosing to see the details.\n" +
                "That's okay - some girls like to know.\n\n" +
                "Show the spoilers?",
                "Show spoilers", "Keep it secret");
                
            if (warning3)
            {
                SessionSpoilerPanel.Visibility = Visibility.Visible;
                BtnRevealSpoilers.Content = "😎 Hide Details";
            }
        }
        
        private bool ShowStyledDialog(string title, string message, string yesText, string noText)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                MinHeight = 200,
                MaxHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };
            
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 46)),
                BorderBrush = FindResource("PinkBrush") as SolidColorBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };
            
            var mainStack = new StackPanel();
            
            // Title
            mainStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = FindResource("PinkBrush") as SolidColorBrush,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            });
            
            // Message
            mainStack.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 20)
            });
            
            // Buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            bool result = false;
            
            var yesBtn = new Button
            {
                Content = yesText,
                Background = FindResource("PinkBrush") as SolidColorBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 0, string.IsNullOrEmpty(noText) ? 0 : 10, 0),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            yesBtn.Click += (s, ev) => { result = true; dialog.Close(); };
            buttonPanel.Children.Add(yesBtn);
            
            // Only add cancel button if noText is provided
            if (!string.IsNullOrEmpty(noText))
            {
                var noBtn = new Button
                {
                    Content = noText,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 10, 20, 10),
                    FontSize = 12,
                    Cursor = Cursors.Hand
                };
                noBtn.Click += (s, ev) => { result = false; dialog.Close(); };
                buttonPanel.Children.Add(noBtn);
            }
            
            mainStack.Children.Add(buttonPanel);
            
            border.Child = mainStack;
            dialog.Content = border;
            dialog.ShowDialog();
            
            return result;
        }
        
        private void BtnStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSession == null || !_selectedSession.IsAvailable) return;

            // Check for locked features in this session
            var lockedFeatures = GetLockedFeaturesForSession(_selectedSession);
            string lockedFeaturesMsg = "";
            if (lockedFeatures.Count > 0)
            {
                lockedFeaturesMsg = $"\n\n⚠️ Features you haven't unlocked yet:\n• {string.Join("\n• ", lockedFeatures)}\n\n(These will be skipped during the session)";
            }

            var confirmed = ShowStyledDialog(
                $"🌅 Start {_selectedSession.Name}?",
                $"Duration: {_selectedSession.DurationMinutes} minutes\n\n" +
                "Your current settings will be temporarily replaced.\n" +
                "They will be restored when the session ends." +
                lockedFeaturesMsg +
                "\n\nReady to begin?",
                "▶ Start Session", "Not yet");

            if (confirmed)
            {
                StartSession(_selectedSession);
            }
        }

        /// <summary>
        /// Get a list of features used by a session that the player hasn't unlocked yet
        /// </summary>
        private List<string> GetLockedFeaturesForSession(Models.Session session)
        {
            var locked = new List<string>();
            int level = App.Settings.Current.PlayerLevel;
            var settings = session.Settings;

            // Level 10: Spiral, Pink Filter
            if (level < 10)
            {
                if (settings.SpiralEnabled) locked.Add("Spiral Overlay (Lv.10)");
                if (settings.PinkFilterEnabled) locked.Add("Pink Filter (Lv.10)");
            }

            // Level 20: Bubbles
            if (level < 20 && settings.BubblesEnabled)
                locked.Add("Bubbles (Lv.20)");

            // Level 35: Lock Cards
            if (level < 35 && settings.LockCardEnabled)
                locked.Add("Lock Cards (Lv.35)");

            // Level 50: Bubble Count
            if (level < 50 && settings.BubbleCountEnabled)
                locked.Add("Bubble Count Game (Lv.50)");

            // Level 60: Bouncing Text
            if (level < 60 && settings.BouncingTextEnabled)
                locked.Add("Bouncing Text (Lv.60)");

            // Level 70: Brain Drain
            if (level < 70 && settings.BrainDrainEnabled)
                locked.Add("Brain Drain (Lv.70)");

            // Level 75: Mind Wipe
            if (level < 75 && settings.MindWipeEnabled)
                locked.Add("Mind Wipe (Lv.75)");

            return locked;
        }
        
        private async void StartSession(Models.Session session)
        {
            // Apply corner GIF settings if enabled
            if (session.HasCornerGifOption && ChkCornerGifEnabled.IsChecked == true)
            {
                session.Settings.CornerGifEnabled = true;
                session.Settings.CornerGifPath = _selectedCornerGifPath;
                session.Settings.CornerGifPosition = GetSelectedCornerPosition();
                session.Settings.CornerGifSize = (int)SliderCornerGifSize.Value;
                session.Settings.CornerGifOpacity = (int)SliderCornerGifOpacity.Value;
            }
            
            // Initialize session engine if needed
            if (_sessionEngine == null)
            {
                _sessionEngine = new SessionEngine(this);
                _sessionEngine.SessionCompleted += OnSessionCompleted;
                _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                _sessionEngine.SessionStarted += OnSessionStarted;
                _sessionEngine.SessionStopped += OnSessionStopped;
            }
            
            try
            {
                // Start the engine if not already running
                if (!_isRunning)
                {
                    BtnStart_Click(this, new RoutedEventArgs());
                }
                
                // Start the session
                await _sessionEngine.StartSessionAsync(session);
                
                
                App.Logger?.Information("Started session: {Name} ({Difficulty}, +{XP} XP)", 
                    session.Name, session.Difficulty, session.BonusXP);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to start session");
                ShowStyledDialog("Error", $"Failed to start session:\n{ex.Message}", "OK", "");
            }
        }
        
        private void OnSessionCompleted(object? sender, SessionCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Award XP
                App.Progression?.AddXP(e.XPEarned, XPSource.Session);

                // Show completion window
                var completeWindow = new SessionCompleteWindow(e.Session, e.Duration, e.XPEarned);
                completeWindow.Owner = this;
                completeWindow.ShowDialog();

                App.Logger?.Information("Session {Name} completed, awarded {XP} XP", e.Session.Name, e.XPEarned);

                // Sync progress to cloud after session (fire and forget)
                if (App.ProfileSync?.IsSyncEnabled == true)
                {
                    _ = App.ProfileSync.SyncProfileAsync();
                }
            });
        }
        
        private void OnSessionProgressUpdated(object? sender, SessionProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_sessionEngine?.CurrentSession != null)
                {
                    var remaining = e.Remaining;
                    var session = _sessionEngine.CurrentSession;

                    // Update session button with remaining time
                    BtnStartSession.Content = $"STOP SESSION ({remaining.Minutes:D2}:{remaining.Seconds:D2})";

                    // Update Start button label with session name + timer
                    var name = session.Name.Length > 14
                        ? session.Name.Substring(0, 11) + "..."
                        : session.Name;
                    var pauseIndicator = _sessionEngine.IsPaused ? " [PAUSED]" : "";
                    TxtStartLabel.Text = $"{name} {remaining.Minutes:D2}:{remaining.Seconds:D2}{pauseIndicator}";
                }
            });
        }
        
        private void OnSessionPhaseChanged(object? sender, SessionPhaseChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session phase: {Phase} - {Description}", e.Phase.Name, e.Phase.Description);
            });
        }

        private void OnSessionStarted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                BtnStartSession.Content = "STOP SESSION";
                BtnStartSession.Click -= BtnStartSession_Click;
                BtnStartSession.Click += BtnStopSession_Click;

                // Update Start button to show session info
                var session = _sessionEngine?.CurrentSession;
                if (session != null)
                {
                    // Abbreviate name if over 22 chars
                    var name = session.Name.Length > 22
                        ? session.Name.Substring(0, 19) + "..."
                        : session.Name;

                    TxtStartIcon.Text = "⏹";
                    TxtStartLabel.Text = name;

                    // Make button red during session
                    BtnStart.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(220, 53, 69)); // Bootstrap danger red

                    // Show pause button
                    BtnPauseSession.Visibility = Visibility.Visible;
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "⏸";
                }
            });
        }

        private void OnSessionStopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Stop the engine when session stops
                StopEngine();

                BtnStartSession.Content = "▶ Start Session";
                BtnStartSession.Click -= BtnStopSession_Click;
                BtnStartSession.Click += BtnStartSession_Click;

                // Reset Start button to normal state
                TxtStartIcon.Text = "▶";
                TxtStartLabel.Text = "START";

                // Restore pink color
                BtnStart.ClearValue(System.Windows.Controls.Control.BackgroundProperty);

                // Hide pause button
                BtnPauseSession.Visibility = Visibility.Collapsed;
            });
        }

        private void BtnStopSession_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionEngine == null || !_sessionEngine.IsRunning) return;

            var session = _sessionEngine.CurrentSession;
            var elapsed = _sessionEngine.ElapsedTime;
            var remaining = _sessionEngine.RemainingTime;

            // Apply level-based XP multiplier
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
            var potentialXP = (int)Math.Round((session?.BonusXP ?? 0) * multiplier);

            var penaltyText = _sessionEngine.PauseCount > 0
                ? $"\n(Plus {_sessionEngine.XPPenalty} XP pause penalty)"
                : "";

            var confirmed = ShowStyledDialog(
                "⚠ Stop Session?",
                $"You're currently in a session:\n" +
                $"{session?.Icon} {session?.Name}\n\n" +
                $"Time elapsed: {elapsed.Minutes:D2}:{elapsed.Seconds:D2}\n" +
                $"Time remaining: {remaining.Minutes:D2}:{remaining.Seconds:D2}\n\n" +
                $"If you stop now, you will lose ALL {potentialXP} XP.{penaltyText}\n\n" +
                "Are you sure you want to quit?",
                "Yes, stop session", "Keep going");

            if (confirmed)
            {
                _sessionEngine.StopSession(completed: false);
            }
        }

        private void BtnPauseSession_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionEngine == null || !_sessionEngine.IsRunning) return;

            if (_sessionEngine.IsPaused)
            {
                // Resume
                _sessionEngine.ResumeSession();
                if (TxtPauseIcon != null) TxtPauseIcon.Text = "⏸";
                BtnPauseSession.ToolTip = $"Pause session (-100 XP penalty per pause)\nPaused {_sessionEngine.PauseCount}x so far";
            }
            else
            {
                // Confirm pause (costs XP)
                var confirmed = ShowStyledDialog(
                    "⏸ Pause Session?",
                    "Pausing will cost you 100 XP from your session reward.\n\n" +
                    $"Current penalty: -{_sessionEngine.XPPenalty} XP\n" +
                    $"After this pause: -{_sessionEngine.XPPenalty + 100} XP\n\n" +
                    "Are you sure?",
                    "Yes, pause", "Keep going");

                if (confirmed)
                {
                    _sessionEngine.PauseSession();
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    BtnPauseSession.ToolTip = "Resume session";
                }
            }
        }
        
        // Methods called by SessionEngine to control features
        public void ApplySessionSettings()
        {
            _isLoading = true;
            LoadSettings();
            _isLoading = false;
        }
        
        public void UpdateSpiralOpacity(int opacity)
        {
            App.Settings.Current.SpiralOpacity = opacity;
            Dispatcher.Invoke(() =>
            {
                if (SliderSpiralOpacity != null && !_isLoading)
                {
                    _isLoading = true;
                    SliderSpiralOpacity.Value = opacity;
                    if (TxtSpiralOpacity != null) TxtSpiralOpacity.Text = $"{opacity}%";
                    _isLoading = false;
                }
            });
        }
        
        public void EnablePinkFilter(bool enabled)
        {
            App.Settings.Current.PinkFilterEnabled = enabled;
            Dispatcher.Invoke(() =>
            {
                if (ChkPinkFilterEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ChkPinkFilterEnabled.IsChecked = enabled;
                    _isLoading = false;
                }
            });
        }
        
        public void EnableSpiral(bool enabled)
        {
            App.Settings.Current.SpiralEnabled = enabled;
            Dispatcher.Invoke(() =>
            {
                if (ChkSpiralEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ChkSpiralEnabled.IsChecked = enabled;
                    _isLoading = false;
                }
            });
        }
        
        public void UpdatePinkFilterOpacity(int opacity)
        {
            App.Settings.Current.PinkFilterOpacity = opacity;
            Dispatcher.Invoke(() =>
            {
                if (SliderPinkOpacity != null && !_isLoading)
                {
                    _isLoading = true;
                    SliderPinkOpacity.Value = opacity;
                    if (TxtPinkOpacity != null) TxtPinkOpacity.Text = $"{opacity}%";
                    _isLoading = false;
                }
            });
        }

        public void EnableBrainDrain(bool enabled, int intensity = 5)
        {
            App.Settings.Current.BrainDrainEnabled = enabled;
            App.Settings.Current.BrainDrainIntensity = intensity;

            if (enabled)
            {
                App.BrainDrain.Start(bypassLevelCheck: true);
            }
            else
            {
                App.BrainDrain.Stop();
            }

            Dispatcher.Invoke(() =>
            {
                if (ChkBrainDrainEnabled != null && !_isLoading)
                {
                    _isLoading = true;
                    ChkBrainDrainEnabled.IsChecked = enabled;
                    if (SliderBrainDrainIntensity != null) SliderBrainDrainIntensity.Value = intensity;
                    if (TxtBrainDrainIntensity != null) TxtBrainDrainIntensity.Text = $"{intensity}%";
                    _isLoading = false;
                }
            });
        }

        public void UpdateBrainDrainIntensity(int intensity)
        {
            App.Settings.Current.BrainDrainIntensity = intensity;
            App.BrainDrain.UpdateSettings();

            Dispatcher.Invoke(() =>
            {
                if (SliderBrainDrainIntensity != null && !_isLoading)
                {
                    _isLoading = true;
                    SliderBrainDrainIntensity.Value = intensity;
                    if (TxtBrainDrainIntensity != null) TxtBrainDrainIntensity.Text = $"{intensity}%";
                    _isLoading = false;
                }
            });
        }

        public void SetBubblesActive(bool active, int bubblesPerBurst = 5)
        {
            // Bubbles are handled by BubbleService through the settings
            // Toggle the enabled state and actually start/stop the service
            if (active)
            {
                App.Settings.Current.BubblesEnabled = true;
                App.Settings.Current.BubblesFrequency = bubblesPerBurst * 2; // Higher frequency during burst

                // Actually start the bubble service if not running (bypass level check for sessions)
                if (!App.Bubbles.IsRunning)
                {
                    App.Bubbles.Start(bypassLevelCheck: true);
                    App.Logger?.Information("Bubble burst started via SetBubblesActive");
                }
            }
            else
            {
                // Stop bubbles when burst ends
                App.Bubbles.Stop();
                App.Settings.Current.BubblesEnabled = false;
                App.Logger?.Information("Bubble burst ended via SetBubblesActive");
            }
        }

        private void HandleHyperlinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to open hyperlink: {Uri} - {Error}", e.Uri.AbsoluteUri, ex.Message);
            }
        }

        private void LoadPreset(Models.Preset preset)
        {
            preset.ApplyTo(App.Settings.Current);
            App.Settings.Save();
            
            _isLoading = true;
            LoadSettings();
            _isLoading = false;
            
            RefreshPresetsDropdown();
            
            App.Logger?.Information("Loaded preset: {Name}", preset.Name);
            MessageBox.Show($"Preset '{preset.Name}' loaded!", "Preset Loaded", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null) return;
            
            var result = MessageBox.Show(
                $"Load preset '{_selectedPreset.Name}'?\n\nThis will replace your current settings.",
                "Load Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                LoadPreset(_selectedPreset);
            }
        }

        private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
        {
            PromptSaveNewPreset();
        }

        private void PromptSaveNewPreset()
        {
            var dialog = new InputDialog("New Preset", "Enter a name for your preset:", "My Custom Preset");
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultText))
            {
                var name = dialog.ResultText.Trim();
                
                // Check if name already exists
                if (_allPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("A preset with this name already exists.", "Name Taken", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var preset = Models.Preset.FromSettings(App.Settings.Current, name, "Custom preset created by user");
                App.Settings.Current.UserPresets.Add(preset);
                App.Settings.Current.CurrentPresetName = name;
                App.Settings.Save();
                
                RefreshPresetsList();
                RefreshPresetsDropdown();
                SelectPreset(preset);
                
                App.Logger?.Information("Created new preset: {Name}", name);
                MessageBox.Show($"Preset '{name}' saved!", "Preset Saved", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSaveOverPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null || _selectedPreset.IsDefault) return;
            
            var result = MessageBox.Show(
                $"Save current settings over preset '{_selectedPreset.Name}'?",
                "Overwrite Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Update the preset with current settings
                var updated = Models.Preset.FromSettings(App.Settings.Current, _selectedPreset.Name, _selectedPreset.Description);
                updated.Id = _selectedPreset.Id;
                updated.CreatedAt = _selectedPreset.CreatedAt;
                
                // Find and replace in user presets
                var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == _selectedPreset.Id);
                if (index >= 0)
                {
                    App.Settings.Current.UserPresets[index] = updated;
                    App.Settings.Save();
                    
                    RefreshPresetsList();
                    SelectPreset(updated);
                    
                    App.Logger?.Information("Updated preset: {Name}", updated.Name);
                    MessageBox.Show($"Preset '{updated.Name}' updated!", "Preset Updated", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null || _selectedPreset.IsDefault) return;
            
            var result = MessageBox.Show(
                $"Delete preset '{_selectedPreset.Name}'?\n\nThis cannot be undone.",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                App.Settings.Current.UserPresets.RemoveAll(p => p.Id == _selectedPreset.Id);
                App.Settings.Save();
                
                _selectedPreset = null;
                
                RefreshPresetsList();
                RefreshPresetsDropdown();
                
                App.Logger?.Information("Deleted preset");
            }
        }

        #endregion

        #region Session Import/Export

        private Services.SessionManager? _sessionManager;
        private Services.SessionFileService? _sessionFileService;
        private Services.AssetImportService? _assetImportService;

        private void InitializeSessionManager()
        {
            _sessionFileService = new Services.SessionFileService();
            _sessionManager = new Services.SessionManager();
            _sessionManager.SessionAdded += OnSessionAdded;
            _sessionManager.SessionRemoved += OnSessionRemoved;
            _sessionManager.LoadAllSessions();
        }

        private void OnSessionAdded(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session imported: {Name}", session.Name);
                AddCustomSessionCard(session);

                // Show "Session loaded!" notification
                ShowDropZoneStatus($"Session loaded: {session.Name}", isError: false);

                // Auto-select the new session
                SelectSession(session);
            });
        }

        private void OnSessionRemoved(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session removed: {Name}", session.Name);
                RemoveCustomSessionCard(session);
            });
        }

        private void AddCustomSessionCard(Models.Session session)
        {
            // Show the "Your Sessions" header
            TxtCustomSessionsHeader.Visibility = Visibility.Visible;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)), // #2A2A4A
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = session.Id
            };

            // Style with border
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(64, 64, 96)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(2));

            border.MouseEnter += (s, e) => border.BorderBrush = FindResource("PinkBrush") as SolidColorBrush;
            border.MouseLeave += (s, e) => border.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 96));
            border.MouseLeftButtonUp += SessionCard_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left side: Session info
            var infoPanel = new StackPanel();
            Grid.SetColumn(infoPanel, 0);

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var nameText = new TextBlock
            {
                Text = $"{session.Icon} {session.Name}",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 15
            };
            headerPanel.Children.Add(nameText);

            // Duration badge
            var durationBadge = new Border
            {
                Background = FindResource("PinkBrush") as SolidColorBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(10, 0, 0, 0)
            };
            durationBadge.Child = new TextBlock
            {
                Text = $"{session.DurationMinutes} MIN",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(durationBadge);

            // Difficulty badge
            var (diffBg, diffFg) = session.Difficulty switch
            {
                Models.SessionDifficulty.Easy => ("#2A3A2A", "#90EE90"),
                Models.SessionDifficulty.Medium => ("#3A3A2A", "#FFD700"),
                Models.SessionDifficulty.Hard => ("#4A3A2A", "#FFA500"),
                Models.SessionDifficulty.Extreme => ("#4A2A2A", "#FF6347"),
                _ => ("#2A3A2A", "#90EE90")
            };
            var diffBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffBg)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0)
            };
            diffBadge.Child = new TextBlock
            {
                Text = session.GetDifficultyText(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffFg)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(diffBadge);

            // Custom badge
            var customBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(106, 90, 205)), // Purple
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0)
            };
            customBadge.Child = new TextBlock
            {
                Text = "CUSTOM",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(customBadge);

            infoPanel.Children.Add(headerPanel);

            // Description
            var descText = new TextBlock
            {
                Text = string.IsNullOrEmpty(session.Description)
                    ? "Custom session"
                    : session.Description.Split('\n')[0].Substring(0, Math.Min(60, session.Description.Split('\n')[0].Length)) + "...",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0)
            };
            infoPanel.Children.Add(descText);

            grid.Children.Add(infoPanel);

            // Right side: Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(buttonPanel, 1);

            var editBtn = CreateSessionActionButton("✏", "Edit Session", session.Id, SessionBtn_Edit);
            var exportBtn = CreateSessionActionButton("📤", "Export Session", session.Id, SessionBtn_Export);
            var deleteBtn = CreateSessionDeleteButton("🗑", "Delete Session", session.Id, SessionBtn_Delete);

            buttonPanel.Children.Add(editBtn);
            buttonPanel.Children.Add(exportBtn);
            buttonPanel.Children.Add(deleteBtn);

            grid.Children.Add(buttonPanel);
            border.Child = grid;

            CustomSessionsPanel.Children.Add(border);
        }

        private Button CreateSessionActionButton(string content, string tooltip, string tag, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Tag = tag,
                Width = 26,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
                Foreground = new SolidColorBrush(Color.FromRgb(144, 144, 144)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0)
            };
            btn.Click += handler;

            // Create template for rounded corners and hover effect
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("PinkBrush")));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.White)));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private Button CreateSessionDeleteButton(string content, string tooltip, string tag, RoutedEventHandler handler)
        {
            var btn = CreateSessionActionButton(content, tooltip, tag, handler);

            // Update hover to red
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 17, 35))));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.White)));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private void RemoveCustomSessionCard(Models.Session session)
        {
            var cardToRemove = CustomSessionsPanel.Children
                .OfType<Border>()
                .FirstOrDefault(b => b.Tag as string == session.Id);

            if (cardToRemove != null)
            {
                CustomSessionsPanel.Children.Remove(cardToRemove);
            }

            // Hide header if no more custom sessions
            if (CustomSessionsPanel.Children.Count == 0)
            {
                TxtCustomSessionsHeader.Visibility = Visibility.Collapsed;
            }
        }

        private void SelectSession(Models.Session session)
        {
            _selectedSession = session;

            // Clear preset selection
            _selectedPreset = null;
            RefreshPresetsList();

            // Hide preset panel, show session panel
            PresetDetailScroller.Visibility = Visibility.Collapsed;
            PresetButtonsPanel.Visibility = Visibility.Collapsed;
            SessionDetailScroller.Visibility = Visibility.Visible;
            SessionButtonsPanel.Visibility = Visibility.Visible;
            SessionSpoilerPanel.Visibility = Visibility.Collapsed;
            BtnRevealSpoilers.Content = "👁 Reveal Details";

            TxtDetailTitle.Text = $"{session.Icon} {session.Name}";
            TxtDetailSubtitle.Text = GenerateSessionTimelineDescription(session);
            TxtSessionDuration.Text = $"{session.DurationMinutes} minutes";

            // Apply level-based XP multiplier for display
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
            var scaledXP = (int)Math.Round(session.BonusXP * multiplier);
            if (multiplier > 1.0)
                TxtSessionXP.Text = $"+{scaledXP} XP ({multiplier:F1}x)";
            else
                TxtSessionXP.Text = $"+{scaledXP} XP";

            TxtSessionDifficulty.Text = session.GetDifficultyText();

            // Show manual description + auto-generated feature summary
            var description = session.Description ?? "";
            var featureSummary = session.GenerateFeatureDescription();
            if (!string.IsNullOrWhiteSpace(description))
                TxtSessionDescription.Text = description + "\n\n─────────────────\n\n" + featureSummary;
            else
                TxtSessionDescription.Text = featureSummary;

            // Update XP color based on difficulty
            TxtSessionXP.Foreground = session.Difficulty switch
            {
                Models.SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                Models.SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                Models.SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                Models.SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Hide corner GIF option for custom sessions
            CornerGifOptionPanel.Visibility = session.HasCornerGifOption ? Visibility.Visible : Visibility.Collapsed;

            // Populate spoiler details
            TxtSessionFlash.Text = session.GetSpoilerFlash();
            TxtSessionSubliminal.Text = session.GetSpoilerSubliminal();
            TxtSessionAudio.Text = session.GetSpoilerAudio();
            TxtSessionOverlays.Text = session.GetSpoilerOverlays();
            TxtSessionExtras.Text = session.GetSpoilerInteractive();
            TxtSessionTimeline.Text = session.GetSpoilerTimeline();

            BtnStartSession.IsEnabled = session.IsAvailable;
            BtnStartSession.Content = session.IsAvailable ? "▶ Start Session" : "🔒 Coming Soon";
            BtnExportSession.IsEnabled = true;
        }

        private string GenerateSessionTimelineDescription(Models.Session session)
        {
            var parts = new List<string>();

            if (session.Settings.FlashEnabled)
                parts.Add($"⚡ Flashes ({session.Settings.FlashPerHour}/hr)");
            if (session.Settings.SubliminalEnabled)
                parts.Add($"💭 Subliminals ({session.Settings.SubliminalPerMin}/min)");
            if (session.Settings.AudioWhispersEnabled)
                parts.Add("🔊 Audio Whispers");
            if (session.Settings.PinkFilterEnabled)
                parts.Add("💗 Pink Filter");
            if (session.Settings.SpiralEnabled)
                parts.Add("🌀 Spiral");
            if (session.Settings.BouncingTextEnabled)
                parts.Add("📝 Bouncing Text");
            if (session.Settings.BubblesEnabled)
                parts.Add("🫧 Bubbles");
            if (session.Settings.LockCardEnabled)
                parts.Add("🔒 Lock Cards");
            if (session.Settings.MandatoryVideosEnabled)
                parts.Add("🎬 Videos");
            if (session.Settings.MindWipeEnabled)
                parts.Add("🧠 Mind Wipe");

            if (parts.Count == 0)
                return "";

            return string.Join(" • ", parts);
        }

        private void SessionDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                    SessionDropZone.BorderBrush = FindResource("PinkBrush") as SolidColorBrush;
                    DropZoneIcon.Text = "📥";
                    DropZoneIcon.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                    SessionDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    DropZoneIcon.Text = "❌";
                    DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void SessionDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void SessionDropZone_DragLeave(object sender, DragEventArgs e)
        {
            SessionDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 96));
            DropZoneIcon.Text = "📂";
            DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));
            DropZoneStatus.Visibility = Visibility.Collapsed;
        }

        // Global window drag-drop handlers
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);

                if (dropType != DropType.None)
                {
                    e.Effects = DragDropEffects.Copy;
                    UpdateDropOverlay(dropType, files);
                    // Hide browser to avoid WebView2 airspace issue (renders on top of WPF)
                    BrowserContainer.Visibility = Visibility.Hidden;
                    GlobalDropOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);
                e.Effects = dropType != DropType.None ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            BrowserContainer.Visibility = Visibility.Visible;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            BrowserContainer.Visibility = Visibility.Visible;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var dropType = DetectDropType(files);

            switch (dropType)
            {
                case DropType.Session:
                    HandleSessionDrop(files[0]);
                    break;

                case DropType.Assets:
                case DropType.Zip:
                case DropType.Folder:
                    await HandleAssetDropAsync(files);
                    break;
            }
        }

        private enum DropType { None, Session, Assets, Zip, Folder }

        private static readonly HashSet<string> AssetVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp"
        };

        private static readonly HashSet<string> AssetImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
        };

        private static DropType DetectDropType(string[] files)
        {
            if (files.Length == 0) return DropType.None;

            // Single session file
            if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Session;

            // Single folder
            if (files.Length == 1 && Directory.Exists(files[0]))
                return DropType.Folder;

            // Check for ZIP files or asset files
            var hasZip = false;
            var hasAssets = false;

            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    hasAssets = true;
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    hasZip = true;
                else if (AssetVideoExtensions.Contains(ext) || AssetImageExtensions.Contains(ext))
                    hasAssets = true;
            }

            if (hasZip) return DropType.Zip;
            if (hasAssets) return DropType.Assets;

            return DropType.None;
        }

        private void UpdateDropOverlay(DropType dropType, string[] files)
        {
            switch (dropType)
            {
                case DropType.Session:
                    DropOverlayIcon.Text = "📋";
                    DropOverlayTitle.Text = "Drop to Import Session";
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Zip:
                    DropOverlayIcon.Text = "📦";
                    DropOverlayTitle.Text = "Drop to Extract Assets";
                    var zipCount = files.Count(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase));
                    DropOverlaySubtitle.Text = zipCount == 1
                        ? Path.GetFileName(files.First(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                        : $"{zipCount} ZIP files";
                    break;

                case DropType.Folder:
                    DropOverlayIcon.Text = "📁";
                    DropOverlayTitle.Text = "Drop to Import Folder";
                    DropOverlaySubtitle.Text = $"Scan for images & videos";
                    break;

                case DropType.Assets:
                    DropOverlayIcon.Text = "🖼️";
                    DropOverlayTitle.Text = "Drop to Import Assets";
                    DropOverlaySubtitle.Text = files.Length == 1
                        ? Path.GetFileName(files[0])
                        : $"{files.Length} files";
                    break;
            }
        }

        private void HandleSessionDrop(string filePath)
        {
            // Validate and import session
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Session loaded: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private async Task HandleAssetDropAsync(string[] paths)
        {
            try
            {
                _assetImportService ??= new Services.AssetImportService();

                var progress = new Progress<Services.ImportProgress>(p =>
                {
                    // Could update a progress indicator here if needed
                    App.Logger?.Debug("Import progress: {Current}/{Total} - {File}", p.Current, p.Total, p.CurrentFile);
                });

                var result = await Task.Run(() => _assetImportService.ImportAsync(paths, progress));

                // Show result notification
                ShowDropZoneStatus(result.GetSummary(), isError: result.TotalImported == 0 && !result.HasErrors);

                // Refresh the asset lists if any were imported
                if (result.ImagesImported > 0)
                {
                    App.Flash?.RefreshImagesPath();
                    RefreshImagesList();
                }

                if (result.VideosImported > 0)
                {
                    App.Video?.RefreshVideosPath();
                    RefreshVideosList();
                }

                App.Logger?.Information("Asset import complete: {Summary}", result.GetSummary());
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Asset import failed");
                ShowDropZoneStatus($"Import failed: {ex.Message}", isError: true);
            }
        }

        private void RefreshImagesList()
        {
            // The FlashService manages its own file list internally
            // RefreshImagesPath() already clears and reloads the cache
            App.Logger?.Debug("Images list refreshed after import");
        }

        private void RefreshVideosList()
        {
            // The VideoService manages its own file list internally
            // RefreshVideosPath() already clears and reloads the cache
            App.Logger?.Debug("Videos list refreshed after import");
        }

        // Session action button handlers
        private void SessionBtn_Edit(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                var editor = new SessionEditorWindow(session);
                editor.Owner = this;
                if (editor.ShowDialog() == true && editor.ResultSession != null)
                {
                    if (_sessionFileService == null) _sessionFileService = new Services.SessionFileService();
                    if (_sessionManager == null) InitializeSessionManager();

                    var editedSession = editor.ResultSession;

                    if (session.Source == Models.SessionSource.BuiltIn)
                    {
                        // Editing a built-in session creates a new custom session
                        editedSession.Id = Guid.NewGuid().ToString(); // New ID

                        var dialog = new Microsoft.Win32.SaveFileDialog
                        {
                            Filter = "Session Files (*.session.json)|*.session.json",
                            Title = "Save as New Custom Session",
                            InitialDirectory = SessionFileService.CustomSessionsFolder,
                            FileName = SessionFileService.GetExportFileName(editedSession)
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            _sessionManager.AddNewSession(editedSession, dialog.FileName);
                            MessageBox.Show("Built-in session saved as a new custom session!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else // Custom session
                    {
                        // Preserve original ID, source, and file path to save over existing file
                        editedSession.Id = session.Id;
                        editedSession.Source = session.Source;
                        editedSession.SourceFilePath = session.SourceFilePath;
                        _sessionManager.UpdateCustomSession(editedSession);
                        
                        SelectSession(editedSession);
                        ShowDropZoneStatus($"Session updated: {editedSession.Name}", isError: false);
                    }
                }
            }
            e.Handled = true;
        }

        private void SessionBtn_Export(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
            e.Handled = true;
        }

        private void SessionBtn_Delete(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                // Confirm deletion
                var result = ShowStyledDialog(
                    "Delete Session",
                    $"Are you sure you want to delete '{session.Name}'?\n\nThis cannot be undone.",
                    "Delete", "Cancel");

                if (result && _sessionManager != null)
                {
                    _sessionManager.DeleteSession(session);
                    ShowDropZoneStatus($"Deleted: {session.Name}", isError: false);

                    // Clear selection if this was selected
                    if (_selectedSession?.Id == sessionId)
                    {
                        _selectedSession = null;
                        TxtDetailTitle.Text = "Select a Session";
                        TxtDetailSubtitle.Text = "Click on a session to see details";
                    }
                }
            }
            e.Handled = true;
        }

        private Models.Session? GetSessionById(string sessionId)
        {
            // Check session manager first
            if (_sessionManager != null)
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session != null) return session;
            }

            // Fall back to hardcoded sessions
            return Models.Session.GetAllSessions().FirstOrDefault(s => s.Id == sessionId);
        }

        private void SessionDropZone_Drop(object sender, DragEventArgs e)
        {
            // Reset visual state
            SessionDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 96));
            DropZoneIcon.Text = "📂";
            DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length != 1) return;

            var filePath = files[0];
            if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
            {
                ShowDropZoneStatus("Only .session.json files allowed", isError: true);
                return;
            }

            // Validate and import
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Imported: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private void ShowDropZoneStatus(string message, bool isError)
        {
            DropZoneStatus.Text = message;
            DropZoneStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(255, 100, 100))
                : FindResource("PinkBrush") as SolidColorBrush;
            DropZoneStatus.Visibility = Visibility.Visible;

            // Auto-hide after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                DropZoneStatus.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        private void BtnExportSession_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSession == null) return;
            ExportSessionToFile(_selectedSession);
        }

        private void BtnCreateSession_Click(object sender, RoutedEventArgs e)
        {
            var editor = new SessionEditorWindow();
            editor.Owner = this;
            if (editor.ShowDialog() == true && editor.ResultSession != null)
            {
                if (_sessionFileService == null)
                {
                    _sessionFileService = new Services.SessionFileService();
                }

                var session = editor.ResultSession;

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Session Files (*.session.json)|*.session.json",
                    Title = "Save New Session",
                    InitialDirectory = SessionFileService.CustomSessionsFolder,
                    FileName = SessionFileService.GetExportFileName(session)
                };

                if (dialog.ShowDialog() == true)
                {
                    if (_sessionManager == null) InitializeSessionManager();
                    _sessionManager.AddNewSession(session, dialog.FileName);

                    // The OnSessionAdded event will handle UI updates
                    MessageBox.Show("New session saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    App.Logger?.Information("Session created: {Name} at {Path}", session.Name, dialog.FileName);
                }
            }
        }

        private void SessionContextMenu_Export(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string sessionId)
            {
                var sessions = Models.Session.GetAllSessions();
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
        }

        private void ExportSessionToFile(Models.Session session)
        {
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Session",
                Filter = "Session files (*.session.json)|*.session.json",
                FileName = Services.SessionFileService.GetExportFileName(session),
                DefaultExt = ".session.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _sessionFileService.ExportSession(session, dialog.FileName);
                    ShowStyledDialog("Export Complete", $"Session exported to:\n{dialog.FileName}", "OK", "");
                    App.Logger?.Information("Session exported: {Name} to {Path}", session.Name, dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowStyledDialog("Export Failed", $"Failed to export session:\n{ex.Message}", "OK", "");
                    App.Logger?.Error(ex, "Failed to export session");
                }
            }
        }

        #endregion

        #region Browser

        private async System.Threading.Tasks.Task InitializeBrowserAsync()
        {
            if (_browserInitialized) return;

            try
            {
                TxtBrowserStatus.Text = "● Loading...";
                TxtBrowserStatus.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                BrowserLoadingText.Text = "🌐 Initializing WebView2...";
                
                _browser = new BrowserService();
                
                _browser.BrowserReady += (s, e) =>
                {
                    Dispatcher.Invoke(() => 
                    {
                        TxtBrowserStatus.Text = "● Connected";
                        TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
                    });
                };
                
                _browser.NavigationCompleted += (s, url) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtBrowserStatus.Text = "● Connected";
                        TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
                    });
                };

                _browser.FullscreenChanged += (s, isFullscreen) =>
                {
                    Dispatcher.Invoke(() => HandleBrowserFullscreenChanged(isFullscreen));
                };

                BrowserLoadingText.Text = "🌐 Creating browser...";
                
                // Navigate directly to Bambi Cloud
                var webView = await _browser.CreateBrowserAsync("https://bambicloud.com/");
                
                if (webView != null)
                {
                    BrowserLoadingText.Visibility = Visibility.Collapsed;
                    BrowserContainer.Children.Add(webView);
                    _browserInitialized = true;
                    
                    App.Logger?.Information("Browser initialized - Bambi Cloud loaded");
                }
                else
                {
                    var errorMsg = "WebView2 returned null - unknown error";
                    BrowserLoadingText.Text = $"❌ {errorMsg}\n\nInstall WebView2 Runtime:\ngo.microsoft.com/fwlink/p/?LinkId=2124703";
                    TxtBrowserStatus.Text = "● Error";
                    TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                    MessageBox.Show(errorMsg, "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (InvalidOperationException invEx)
            {
                BrowserLoadingText.Text = $"❌ {invEx.Message}";
                TxtBrowserStatus.Text = "● Not Installed";
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(invEx.Message, "WebView2 Not Installed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                var errorMsg = $"WebView2 COM Error:\n{comEx.Message}\n\nError Code: {comEx.HResult}";
                BrowserLoadingText.Text = $"❌ COM Error\n\nInstall WebView2:\ngo.microsoft.com/fwlink/p/?LinkId=2124703";
                TxtBrowserStatus.Text = "● COM Error";
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.DllNotFoundException dllEx)
            {
                var errorMsg = $"WebView2 DLL not found:\n{dllEx.Message}";
                BrowserLoadingText.Text = $"❌ Missing DLL\n\nInstall WebView2:\ngo.microsoft.com/fwlink/p/?LinkId=2124703";
                TxtBrowserStatus.Text = "● Missing DLL";
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, "Missing DLL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Browser Error:\n\nType: {ex.GetType().Name}\n\nMessage: {ex.Message}\n\nStack: {ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))}";
                BrowserLoadingText.Text = $"❌ {ex.GetType().Name}\n{ex.Message}";
                TxtBrowserStatus.Text = "● Error";
                TxtBrowserStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107));
                MessageBox.Show(errorMsg, "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowserSiteToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_browser == null || !_browserInitialized) return;

            var isBambiCloud = RbBambiCloud.IsChecked == true;
            var url = isBambiCloud
                ? "https://bambicloud.com/"
                : "https://hypnotube.com/";

            // Set zoom: 50% for both sites
            _browser.ZoomFactor = 0.5;

            _browser.Navigate(url);
            App.Logger?.Information("Browser navigated to {Site} (zoom: 50%)",
                isBambiCloud ? "BambiCloud" : "HypnoTube");
        }

        private void BtnPopOutBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (_browser?.WebView == null) return;

            // If already popped out, bring the window to front
            if (_browserPopoutWindow != null)
            {
                _browserPopoutWindow.Activate();
                return;
            }

            try
            {
                // Remove WebView from embedded container
                if (BrowserContainer.Children.Contains(_browser.WebView))
                {
                    BrowserContainer.Children.Remove(_browser.WebView);
                }

                // Show placeholder in the embedded container
                BrowserLoadingText.Text = "🌐 Browser popped out\nClick ⧉ to focus window";
                BrowserLoadingText.Visibility = Visibility.Visible;

                // Create popup window
                _browserPopoutWindow = new Window
                {
                    Title = "Conditioning Control Panel - Browser",
                    Width = 1024,
                    Height = 768,
                    MinWidth = 400,
                    MinHeight = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Content = _browser.WebView
                };

                // Handle window CLOSING (before close) - detach WebView to prevent parent/child errors
                _browserPopoutWindow.Closing += (s, args) =>
                {
                    if (_browserPopoutWindow != null)
                    {
                        // CRITICAL: Remove WebView from window content BEFORE closing
                        // This prevents "window is a parent/child of another" errors
                        _browserPopoutWindow.Content = null;
                    }
                };

                // Handle window CLOSED (after close) - return browser to embedded container
                _browserPopoutWindow.Closed += (s, args) =>
                {
                    if (_browser?.WebView != null)
                    {
                        // Add back to embedded container
                        if (!BrowserContainer.Children.Contains(_browser.WebView))
                        {
                            BrowserContainer.Children.Add(_browser.WebView);
                        }
                        BrowserLoadingText.Visibility = Visibility.Collapsed;
                    }
                    _browserPopoutWindow = null;
                    BtnPopOutBrowser.Content = "⧉ Pop Out";
                    BtnPopOutBrowser.ToolTip = "Pop out browser to resizable window";
                };

                // Update button to show it's popped out
                BtnPopOutBrowser.Content = "◱ Focus";
                BtnPopOutBrowser.ToolTip = "Browser is popped out - click to focus";

                _browserPopoutWindow.Show();
                App.Logger?.Information("Browser popped out to separate window");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to pop out browser");
                // Try to restore browser to container
                if (_browser?.WebView != null && !BrowserContainer.Children.Contains(_browser.WebView))
                {
                    BrowserContainer.Children.Add(_browser.WebView);
                    BrowserLoadingText.Visibility = Visibility.Collapsed;
                }
                _browserPopoutWindow = null;
            }
        }

        private void HandleBrowserFullscreenChanged(bool isFullscreen)
        {
            if (_browser?.WebView == null) return;

            if (isFullscreen)
            {
                // Check if dual monitor mode should be used (screen mirroring)
                var screens = App.GetAllScreensCached();
                var useDualMonitor = App.Settings.Current.DualMonitorEnabled && screens.Length > 1;

                if (useDualMonitor)
                {
                    // Enable screen mirroring - clones primary to all monitors
                    _isDualMonitorPlaybackActive = App.ScreenMirror.EnableMirror();
                    if (_isDualMonitorPlaybackActive)
                    {
                        App.Logger?.Information("Screen mirroring enabled for fullscreen video");
                    }
                }

                EnterBrowserFullscreen();
            }
            else
            {
                // Disable screen mirroring if it was active
                if (_isDualMonitorPlaybackActive)
                {
                    App.ScreenMirror.DisableMirror();
                    _isDualMonitorPlaybackActive = false;
                    App.Logger?.Information("Screen mirroring disabled");
                }

                ExitBrowserFullscreen();
            }
        }

        private void EnterBrowserFullscreen()
        {
            if (_browser?.WebView == null) return;

            try
            {
                var allScreens = System.Windows.Forms.Screen.AllScreens.ToList();
                if (allScreens.Count == 0)
                {
                    App.Logger?.Warning("No screens available for browser fullscreen");
                    return;
                }

                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];

                // Remove WebView from its current container (could be embedded or popup)
                if (BrowserContainer.Children.Contains(_browser.WebView))
                {
                    BrowserContainer.Children.Remove(_browser.WebView);
                }
                else if (_browserPopoutWindow != null && _browserPopoutWindow.Content == _browser.WebView)
                {
                    _browserPopoutWindow.Content = null;
                }

                // Create primary fullscreen window with WebView
                var primaryWin = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Background = System.Windows.Media.Brushes.Black,
                    Left = primary.Bounds.Left,
                    Top = primary.Bounds.Top,
                    Width = primary.Bounds.Width,
                    Height = primary.Bounds.Height,
                    Content = _browser.WebView
                };

                _browser.ZoomFactor = 1.0;

                primaryWin.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        _browser?.WebView?.CoreWebView2?.ExecuteScriptAsync("document.exitFullscreen()");
                    }
                };

                primaryWin.Show();
                _browserFullscreenWindows.Add(primaryWin);

                // Note: WebView2 can't be mirrored to secondary screens due to DirectX rendering
                // Browser fullscreen only displays on primary monitor

                App.Logger?.Information("Browser entered fullscreen on primary monitor");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to enter browser fullscreen");
                ExitBrowserFullscreen();
            }
        }

        private void ExitBrowserFullscreen()
        {
            try
            {
                // Close all fullscreen windows
                foreach (var win in _browserFullscreenWindows.ToList())
                {
                    try
                    {
                        // Get the WebView before closing if it's the primary
                        if (win.Content == _browser?.WebView)
                        {
                            win.Content = null;
                        }
                        win.Close();
                    }
                    catch { }
                }
                _browserFullscreenWindows.Clear();

                // Restore WebView to correct container (popup if popped out, otherwise embedded)
                if (_browser?.WebView != null)
                {
                    if (_browserPopoutWindow != null)
                    {
                        // Browser was popped out - return to popup window
                        if (_browserPopoutWindow.Content != _browser.WebView)
                        {
                            _browserPopoutWindow.Content = _browser.WebView;
                        }
                        // Restore zoom to 50%
                        _browser.ZoomFactor = 0.5;
                    }
                    else if (!BrowserContainer.Children.Contains(_browser.WebView))
                    {
                        // Return to embedded container
                        BrowserContainer.Children.Add(_browser.WebView);
                        // Restore zoom to 50%
                        _browser.ZoomFactor = 0.5;
                    }
                }

                App.Logger?.Information("Browser exited fullscreen");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to exit browser fullscreen");
            }
        }

        #endregion

        #region Start/Stop

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                // Check if a session is running
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    var session = _sessionEngine.CurrentSession;
                    var elapsed = _sessionEngine.ElapsedTime;
                    var remaining = _sessionEngine.RemainingTime;

                    // Apply level-based XP multiplier
                    var level = App.Settings?.Current?.PlayerLevel ?? 1;
                    var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
                    var potentialXP = (int)Math.Round((session?.BonusXP ?? 0) * multiplier);

                    var penalty = _sessionEngine.XPPenalty;
                    var finalXP = Math.Max(0, potentialXP - penalty);

                    var penaltyText = penalty > 0
                        ? $"\n(Pause penalty: -{penalty} XP, would earn: {finalXP} XP)"
                        : "";

                    var confirmed = ShowStyledDialog(
                        "⚠ Stop Session?",
                        $"You're currently in a session:\n" +
                        $"{session?.Icon} {session?.Name}\n\n" +
                        $"Time elapsed: {elapsed.Minutes:D2}:{elapsed.Seconds:D2}\n" +
                        $"Time remaining: {remaining.Minutes:D2}:{remaining.Seconds:D2}\n\n" +
                        $"If you stop now, you will lose ALL {potentialXP} XP.{penaltyText}\n\n" +
                        "Are you sure you want to quit?",
                        "Yes, stop session", "Keep going");

                    if (!confirmed) return;

                    // Stop the session without completing it
                    _sessionEngine.StopSession(completed: false);
                    if (TxtPresetsStatus != null)
                    {
                        TxtPresetsStatus.Visibility = Visibility.Collapsed;
                        TxtPresetsStatus.Text = "";
                    }
                }
                
                // User manually stopping
                if (App.Settings.Current.SchedulerEnabled && IsInScheduledTimeWindow())
                {
                    _manuallyStoppedDuringSchedule = true;
                }
                StopEngine();
            }
            else
            {
                // User manually starting - clear manual stop flag
                _manuallyStoppedDuringSchedule = false;
                StartEngine();
            }
        }

        public void StartEngine()
        {
            SaveSettings();
            
            var settings = App.Settings.Current;
            
            App.Flash.Start();
            
            if (settings.MandatoryVideosEnabled)
                App.Video.Start();
            
            if (settings.SubliminalEnabled)
                App.Subliminal.Start();
            
            // Always start overlay service if level >= 10 (handles spiral and pink filter)
            // This allows toggling overlays on/off while engine is running
            if (settings.PlayerLevel >= 10)
            {
                App.Overlay.Start();
            }
            
            // Start bubble service (requires level 20)
            if (settings.PlayerLevel >= 20 && settings.BubblesEnabled)
            {
                App.Bubbles.Start();
            }
            
            // Start lock card service (requires level 35)
            if (settings.PlayerLevel >= 35 && settings.LockCardEnabled)
            {
                App.LockCard.Start();
            }
            
            // Start bubble count game service (requires level 50)
            if (settings.PlayerLevel >= 50 && settings.BubbleCountEnabled)
            {
                App.BubbleCount.Start();
            }
            
            // Start bouncing text service (requires level 60)
            if (settings.PlayerLevel >= 60 && settings.BouncingTextEnabled)
            {
                App.BouncingText.Start();
            }
            else
            {
                // Ensure bouncing text is stopped if disabled (cleanup any leftover state)
                App.BouncingText.Stop();
            }
            
            // Start mind wipe service (requires level 75)
            if (settings.PlayerLevel >= 75 && settings.MindWipeEnabled)
            {
                App.MindWipe.Start(settings.MindWipeFrequency, settings.MindWipeVolume / 100.0);

                // Start loop mode if enabled in settings
                if (settings.MindWipeLoop)
                {
                    App.MindWipe.StartLoop(settings.MindWipeVolume / 100.0);
                }
            }

            // Start brain drain service (requires level 70)
            if (settings.PlayerLevel >= 70 && settings.BrainDrainEnabled)
            {
                App.BrainDrain.Start();
            }

            // Start autonomy service (requires Patreon + level 100)
            var hasPatreonAccess = settings.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && settings.PlayerLevel >= 100 && settings.AutonomyModeEnabled && settings.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
            }

            // Start ramp timer if enabled
            if (settings.IntensityRampEnabled)
            {
                StartRampTimer();
            }
            
            // Browser audio serves as background - no need to play separate music
            
            _isRunning = true;
            UpdateStartButton();
            
            App.Logger?.Information("Engine started - Overlay: {Overlay}, Bubbles: {Bubbles}, LockCard: {LockCard}, BubbleCount: {BubbleCount}, MindWipe: {MindWipe}, BrainDrain: {BrainDrain}", 
                App.Overlay.IsRunning, App.Bubbles.IsRunning, App.LockCard.IsRunning, App.BubbleCount.IsRunning, App.MindWipe.IsRunning, App.BrainDrain.IsRunning);
        }

        public void StopEngine()
        {
            App.Flash.Stop();
            App.Video.Stop();
            App.Subliminal.Stop();
            App.Overlay.Stop();
            App.Bubbles.Stop();
            App.LockCard.Stop();
            App.BubbleCount.Stop();
            App.BouncingText.Stop();
            App.MindWipe.Stop();
            App.BrainDrain.Stop();
            App.Autonomy?.Stop();
            App.Audio.Unduck();

            // Force close any open lock card windows (panic button should close them immediately)
            LockCardWindow.ForceCloseAll();
            BubbleCountWindow.ForceCloseAll();

            // Stop ramp timer and reset sliders
            StopRampTimer();

            _isRunning = false;
            UpdateStartButton();

            // Fire event for avatar reaction
            EngineStopped?.Invoke(this, EventArgs.Empty);

            App.Logger?.Information("Engine stopped");
        }

        private void StartRampTimer()
        {
            var settings = App.Settings.Current;
            
            // Store base values
            _rampBaseValues["FlashOpacity"] = settings.FlashOpacity;
            _rampBaseValues["SpiralOpacity"] = settings.SpiralOpacity;
            _rampBaseValues["PinkFilterOpacity"] = settings.PinkFilterOpacity;
            _rampBaseValues["MasterVolume"] = settings.MasterVolume;
            _rampBaseValues["SubAudioVolume"] = settings.SubAudioVolume;
            
            _rampStartTime = DateTime.Now;
            
            _rampTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Update every 2 seconds
            };
            _rampTimer.Tick += RampTimer_Tick;
            _rampTimer.Start();
            
            App.Logger?.Information("Ramp timer started - Duration: {Duration}min, Multiplier: {Mult}x", 
                settings.RampDurationMinutes, settings.SchedulerMultiplier);
        }

        private void StopRampTimer()
        {
            _rampTimer?.Stop();
            _rampTimer = null;
            
            // Reset sliders and settings to base values
            if (_rampBaseValues.Count > 0)
            {
                var settings = App.Settings.Current;
                
                if (_rampBaseValues.TryGetValue("FlashOpacity", out var flashOp))
                {
                    SliderOpacity.Value = flashOp;
                    TxtOpacity.Text = $"{(int)flashOp}%";
                    settings.FlashOpacity = (int)flashOp;
                }
                if (_rampBaseValues.TryGetValue("SpiralOpacity", out var spiralOp))
                {
                    SliderSpiralOpacity.Value = spiralOp;
                    TxtSpiralOpacity.Text = $"{(int)spiralOp}%";
                    settings.SpiralOpacity = (int)spiralOp;
                }
                if (_rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkOp))
                {
                    SliderPinkOpacity.Value = pinkOp;
                    TxtPinkOpacity.Text = $"{(int)pinkOp}%";
                    settings.PinkFilterOpacity = (int)pinkOp;
                }
                if (_rampBaseValues.TryGetValue("MasterVolume", out var masterVol))
                {
                    SliderMaster.Value = masterVol;
                    TxtMaster.Text = $"{(int)masterVol}%";
                    settings.MasterVolume = (int)masterVol;
                }
                if (_rampBaseValues.TryGetValue("SubAudioVolume", out var subVol))
                {
                    SliderWhisperVol.Value = subVol;
                    TxtWhisperVol.Text = $"{(int)subVol}%";
                    settings.SubAudioVolume = (int)subVol;
                }
                
                _rampBaseValues.Clear();
                App.Logger?.Information("Ramp timer stopped - values reset to base");
            }
        }

        private void RampTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            var elapsed = (DateTime.Now - _rampStartTime).TotalMinutes;
            var duration = settings.RampDurationMinutes;
            var multiplier = settings.SchedulerMultiplier;

            // Skip visual effect ramping if a session is active - sessions have their own built-in ramping
            // This prevents the two systems from fighting and causing values to jump around
            var sessionActive = _sessionEngine?.IsRunning == true;

            // Calculate progress (0.0 to 1.0)
            var progress = Math.Min(elapsed / duration, 1.0);

            // Calculate current multiplier based on progress (linear interpolation from 1.0 to max)
            var currentMult = 1.0 + (multiplier - 1.0) * progress;

            // Update linked sliders and settings
            Dispatcher.Invoke(() =>
            {
                // Only apply visual effect ramps when no session is active
                if (!sessionActive && settings.RampLinkFlashOpacity && _rampBaseValues.TryGetValue("FlashOpacity", out var flashBase))
                {
                    var newVal = (int)Math.Min(flashBase * currentMult, 100);
                    SliderOpacity.Value = newVal;
                    TxtOpacity.Text = $"{newVal}%";
                    settings.FlashOpacity = newVal;
                }

                if (!sessionActive && settings.RampLinkSpiralOpacity && _rampBaseValues.TryGetValue("SpiralOpacity", out var spiralBase))
                {
                    var newVal = (int)Math.Min(spiralBase * currentMult, 50);
                    SliderSpiralOpacity.Value = newVal;
                    TxtSpiralOpacity.Text = $"{newVal}%";
                    settings.SpiralOpacity = newVal;
                }
                
                if (!sessionActive && settings.RampLinkPinkFilterOpacity && _rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkBase))
                {
                    var newVal = (int)Math.Min(pinkBase * currentMult, 50);
                    SliderPinkOpacity.Value = newVal;
                    TxtPinkOpacity.Text = $"{newVal}%";
                    settings.PinkFilterOpacity = newVal;
                }
                
                if (settings.RampLinkMasterAudio && _rampBaseValues.TryGetValue("MasterVolume", out var masterBase))
                {
                    var newVal = (int)Math.Min(masterBase * currentMult, 100);
                    SliderMaster.Value = newVal;
                    TxtMaster.Text = $"{newVal}%";
                    settings.MasterVolume = newVal;
                }
                
                if (settings.RampLinkSubliminalAudio && _rampBaseValues.TryGetValue("SubAudioVolume", out var subBase))
                {
                    var newVal = (int)Math.Min(subBase * currentMult, 100);
                    SliderWhisperVol.Value = newVal;
                    TxtWhisperVol.Text = $"{newVal}%";
                    settings.SubAudioVolume = newVal;
                }
            });
            
            // Check if ramp is complete and should end session
            if (progress >= 1.0 && settings.EndSessionOnRampComplete)
            {
                App.Logger?.Information("Ramp complete - ending session");
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.ShowNotification("Session Complete", "Intensity ramp finished. Stopping...", System.Windows.Forms.ToolTipIcon.Info);
                    StopEngine();
                });
            }
        }

        #endregion

        #region Scheduler

        private void CheckSchedulerOnStartup()
        {
            var settings = App.Settings.Current;
            App.Logger?.Information("Scheduler startup check: Enabled={Enabled}, InWindow={InWindow}",
                settings.SchedulerEnabled, IsInScheduledTimeWindow());

            if (!settings.SchedulerEnabled) return;

            if (IsInScheduledTimeWindow())
            {
                App.Logger?.Information("Scheduler: App started within scheduled time window - auto-starting");

                // Minimize to tray and start engine
                WindowState = WindowState.Minimized;
                Hide();
                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void CheckSchedulerAfterSettingsChange()
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;

            App.Logger?.Information("Scheduler settings changed - checking time window");

            if (IsInScheduledTimeWindow() && !_isRunning)
            {
                App.Logger?.Information("Scheduler: In time window after settings change - auto-starting");

                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;
            
            bool inWindow = IsInScheduledTimeWindow();
            
            if (inWindow && !_isRunning && !_schedulerAutoStarted && !_manuallyStoppedDuringSchedule)
            {
                // Time to start!
                App.Logger?.Information("Scheduler: Entering scheduled time window - auto-starting");
                
                Dispatcher.Invoke(() =>
                {
                    WindowState = WindowState.Minimized;
                    Hide();
                    _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);
                    
                    StartEngine();
                    _schedulerAutoStarted = true;
                });
            }
            else if (!inWindow && _isRunning && _schedulerAutoStarted)
            {
                // Time to stop!
                App.Logger?.Information("Scheduler: Exiting scheduled time window - auto-stopping");
                
                Dispatcher.Invoke(() =>
                {
                    StopEngine();
                    _schedulerAutoStarted = false;
                    _trayIcon?.ShowNotification("Scheduler", "Scheduled session ended.", System.Windows.Forms.ToolTipIcon.Info);
                });
            }
            else if (!inWindow)
            {
                // Outside window - reset flags for next window
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
            }
        }

        private bool IsInScheduledTimeWindow()
        {
            var settings = App.Settings.Current;
            var now = DateTime.Now;

            // Check if today is an active day
            bool isDayActive = now.DayOfWeek switch
            {
                DayOfWeek.Monday => settings.SchedulerMonday,
                DayOfWeek.Tuesday => settings.SchedulerTuesday,
                DayOfWeek.Wednesday => settings.SchedulerWednesday,
                DayOfWeek.Thursday => settings.SchedulerThursday,
                DayOfWeek.Friday => settings.SchedulerFriday,
                DayOfWeek.Saturday => settings.SchedulerSaturday,
                DayOfWeek.Sunday => settings.SchedulerSunday,
                _ => false
            };

            if (!isDayActive)
            {
                App.Logger?.Debug("Scheduler: {Day} is not an active day", now.DayOfWeek);
                return false;
            }

            // Parse start and end times
            if (!TimeSpan.TryParse(settings.SchedulerStartTime, out var startTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", settings.SchedulerStartTime);
                startTime = new TimeSpan(16, 0, 0);
            }

            if (!TimeSpan.TryParse(settings.SchedulerEndTime, out var endTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", settings.SchedulerEndTime);
                endTime = new TimeSpan(22, 0, 0);
            }

            var currentTime = now.TimeOfDay;

            bool inWindow;
            // Handle case where end time is after midnight (e.g., 22:00 - 02:00)
            if (endTime < startTime)
            {
                // Overnight schedule
                inWindow = currentTime >= startTime || currentTime < endTime;
            }
            else
            {
                // Same-day schedule
                inWindow = currentTime >= startTime && currentTime < endTime;
            }

            App.Logger?.Debug("Scheduler: Current={Current}, Start={Start}, End={End}, InWindow={InWindow}",
                currentTime.ToString(@"hh\:mm"), startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm"), inWindow);

            return inWindow;
        }

        #endregion

        #region Engine Helpers

        /// <summary>
        /// Apply current UI values to settings immediately (for live updates)
        /// </summary>
        private void ApplySettingsLive()
        {
            if (_isLoading) return;
            
            var s = App.Settings.Current;
            
            // Track if flash frequency changed
            var oldFlashFreq = s.FlashFrequency;
            
            // Flash settings
            s.FlashEnabled = ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = ChkClickable.IsChecked ?? true;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            s.FlashFrequency = (int)SliderPerMin.Value;
            s.SimultaneousImages = (int)SliderImages.Value;
            s.HydraLimit = (int)SliderMaxOnScreen.Value;
            s.ImageScale = (int)SliderSize.Value;
            s.FlashOpacity = (int)SliderOpacity.Value;
            s.FadeDuration = (int)SliderFade.Value;
            
            // Video settings
            s.MandatoryVideosEnabled = ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SliderPerHour.Value;
            s.StrictLockEnabled = ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SliderTargets.Value;
            s.RandomizeAttentionTargets = ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SliderDuration.Value;
            s.AttentionSize = (int)SliderTargetSize.Value;

            // Subliminal settings
            s.SubliminalEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SliderFrames.Value;
            s.SubliminalOpacity = (int)SliderSubOpacity.Value;
            s.SubAudioEnabled = ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SliderWhisperVol.Value;
            
            // Audio settings  
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Overlay settings
            s.SpiralOpacity = (int)SliderSpiralOpacity.Value;
            s.PinkFilterOpacity = (int)SliderPinkOpacity.Value;
            
            // Refresh services if running
            if (_isRunning)
            {
                // Reschedule flash timer if frequency changed
                if (s.FlashFrequency != oldFlashFreq)
                {
                    App.Flash.RefreshSchedule();
                }
                
                // Refresh overlays (spiral, pink filter)
                App.Overlay.RefreshOverlays();
            }
            
            // Save settings to disk
            App.Settings.Save();
        }

        private void UpdateStartButton()
        {
            if (_isRunning)
            {
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "■", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "STOP", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button using direct reference
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = "Running...";
                }
            }
            else
            {
                BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "▶", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "START", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = "";
                }
            }
        }
        
        /// <summary>
        /// Find a visual child by name in the visual tree
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region Settings Load/Save

        private void LoadSettings()
        {
            var s = App.Settings.Current;

            // Flash
            ChkFlashEnabled.IsChecked = s.FlashEnabled;
            ChkClickable.IsChecked = s.FlashClickable;
            ChkCorruption.IsChecked = s.CorruptionMode;
            SliderPerMin.Value = s.FlashFrequency;
            SliderImages.Value = s.SimultaneousImages;
            SliderMaxOnScreen.Value = s.HydraLimit;

            // Visuals
            SliderSize.Value = s.ImageScale;
            SliderOpacity.Value = s.FlashOpacity;
            SliderFade.Value = s.FadeDuration;
            SliderFlashDuration.Value = s.FlashDuration;
            ChkFlashAudio.IsChecked = s.FlashAudioEnabled;
            SliderFlashDuration.IsEnabled = !s.FlashAudioEnabled;
            SliderFlashDuration.Opacity = s.FlashAudioEnabled ? 0.5 : 1.0;
            
            // Set audio link state based on frequency
            _isLoading = false;
            UpdateAudioLinkState();
            _isLoading = true;

            // Video
            ChkVideoEnabled.IsChecked = s.MandatoryVideosEnabled;
            SliderPerHour.Value = s.VideosPerHour;
            ChkStrictLock.IsChecked = s.StrictLockEnabled;
            ChkMiniGameEnabled.IsChecked = s.AttentionChecksEnabled;
            SliderTargets.Value = s.AttentionDensity;
            ChkRandomizeTargets.IsChecked = s.RandomizeAttentionTargets;
            SliderDuration.Value = s.AttentionLifespan;
            SliderTargetSize.Value = s.AttentionSize;

            // Subliminals
            ChkSubliminalEnabled.IsChecked = s.SubliminalEnabled;
            SliderSubPerMin.Value = s.SubliminalFrequency;
            SliderFrames.Value = s.SubliminalDuration;
            SliderSubOpacity.Value = s.SubliminalOpacity;
            ChkAudioWhispers.IsChecked = s.SubAudioEnabled;
            SliderWhisperVol.Value = s.SubAudioVolume;

            // System
            ChkDualMon.IsChecked = s.DualMonitorEnabled;
            ChkWinStart.IsChecked = s.RunOnStartup;
            ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            ChkAutoRun.IsChecked = s.AutoStartEngine;
            ChkStartHidden.IsChecked = s.StartMinimized;
            ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            ChkOfflineMode.IsChecked = s.OfflineMode;

            // Startup video display
            if (!string.IsNullOrEmpty(s.StartupVideoPath) && System.IO.File.Exists(s.StartupVideoPath))
            {
                TxtStartupVideo.Text = System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            else
            {
                TxtStartupVideo.Text = "(Random)";
            }

            // Audio
            SliderMaster.Value = s.MasterVolume;
            SliderVideoVolume.Value = s.VideoVolume;
            ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            SliderDuck.Value = s.DuckingLevel;
            ChkExcludeBambiCloudDucking.IsChecked = s.ExcludeBambiCloudFromDucking;

            // Progression
            ChkSpiralEnabled.IsChecked = s.SpiralEnabled;
            SliderSpiralOpacity.Value = s.SpiralOpacity;
            ChkPinkFilterEnabled.IsChecked = s.PinkFilterEnabled;
            SliderPinkOpacity.Value = s.PinkFilterOpacity;
            ChkBubblesEnabled.IsChecked = s.BubblesEnabled;
            SliderBubbleFreq.Value = s.BubblesFrequency;
            ChkLockCardEnabled.IsChecked = s.LockCardEnabled;
            SliderLockCardFreq.Value = s.LockCardFrequency;
            SliderLockCardRepeats.Value = s.LockCardRepeats;
            ChkLockCardStrict.IsChecked = s.LockCardStrict;
            
            // Mind Wipe
            ChkMindWipeEnabled.IsChecked = s.MindWipeEnabled;
            SliderMindWipeFreq.Value = s.MindWipeFrequency;
            SliderMindWipeVolume.Value = s.MindWipeVolume;
            ChkMindWipeLoop.IsChecked = s.MindWipeLoop;

            // Brain Drain
            ChkBrainDrainEnabled.IsChecked = s.BrainDrainEnabled;
            SliderBrainDrainIntensity.Value = s.BrainDrainIntensity;
            ChkBrainDrainHighRefresh.IsChecked = s.BrainDrainHighRefresh;

            // Autonomy Mode
            ChkAutonomyEnabled.IsChecked = s.AutonomyModeEnabled;
            UpdateAutonomyButtonState(s.AutonomyModeEnabled);
            SliderAutonomyIntensity.Value = s.AutonomyIntensity;
            SliderAutonomyCooldown.Value = s.AutonomyCooldownSeconds;
            SliderAutonomyInterval.Value = s.AutonomyRandomIntervalSeconds;
            ChkAutonomyIdle.IsChecked = s.AutonomyIdleTriggerEnabled;
            ChkAutonomyRandom.IsChecked = s.AutonomyRandomTriggerEnabled;
            ChkAutonomyTimeAware.IsChecked = s.AutonomyTimeAwareEnabled;
            ChkAutonomyFlash.IsChecked = s.AutonomyCanTriggerFlash;
            ChkAutonomyVideo.IsChecked = s.AutonomyCanTriggerVideo;
            ChkAutonomySubliminal.IsChecked = s.AutonomyCanTriggerSubliminal;
            ChkAutonomyBubbles.IsChecked = s.AutonomyCanTriggerBubbles;
            ChkAutonomyComment.IsChecked = s.AutonomyCanComment;
            ChkAutonomyMindWipe.IsChecked = s.AutonomyCanTriggerMindWipe;
            ChkAutonomyLockCard.IsChecked = s.AutonomyCanTriggerLockCard;
            ChkAutonomySpiral.IsChecked = s.AutonomyCanTriggerSpiral;
            ChkAutonomyPinkFilter.IsChecked = s.AutonomyCanTriggerPinkFilter;
            ChkAutonomyBouncingText.IsChecked = s.AutonomyCanTriggerBouncingText;
            ChkAutonomyBubbleCount.IsChecked = s.AutonomyCanTriggerBubbleCount;
            SliderAutonomyAnnounce.Value = s.AutonomyAnnouncementChance;

            // Bouncing Text Size (add if not already loaded above)
            SliderBouncingTextSize.Value = s.BouncingTextSize;

            // Scheduler
            ChkSchedulerEnabled.IsChecked = s.SchedulerEnabled;
            TxtStartTime.Text = s.SchedulerStartTime;
            TxtEndTime.Text = s.SchedulerEndTime;
            ChkMon.IsChecked = s.SchedulerMonday;
            ChkTue.IsChecked = s.SchedulerTuesday;
            ChkWed.IsChecked = s.SchedulerWednesday;
            ChkThu.IsChecked = s.SchedulerThursday;
            ChkFri.IsChecked = s.SchedulerFriday;
            ChkSat.IsChecked = s.SchedulerSaturday;
            ChkSun.IsChecked = s.SchedulerSunday;
            ChkRampEnabled.IsChecked = s.IntensityRampEnabled;
            SliderRampDuration.Value = s.RampDurationMinutes;
            SliderMultiplier.Value = s.SchedulerMultiplier;
            
            // Ramp Links
            ChkRampLinkFlash.IsChecked = s.RampLinkFlashOpacity;
            ChkRampLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
            ChkRampLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
            ChkRampLinkMaster.IsChecked = s.RampLinkMasterAudio;
            ChkRampLinkSubAudio.IsChecked = s.RampLinkSubliminalAudio;
            ChkEndAtRamp.IsChecked = s.EndSessionOnRampComplete;

            // Haptics
            ChkHapticsEnabled.IsChecked = s.Haptics.Enabled;
            SliderHapticIntensity.Value = s.Haptics.GlobalIntensity * 100;

            // Set provider combo box first
            foreach (System.Windows.Controls.ComboBoxItem item in CmbHapticProvider.Items)
            {
                if (item.Tag?.ToString() == s.Haptics.Provider.ToString())
                {
                    CmbHapticProvider.SelectedItem = item;
                    break;
                }
            }

            // Then set URL based on provider
            TxtHapticUrl.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => s.Haptics.LovenseUrl,
                Services.Haptics.HapticProviderType.Buttplug => s.Haptics.ButtplugUrl,
                _ => s.Haptics.LovenseUrl
            };

            // Set hint text based on provider
            TxtHapticUrlHint.Text = s.Haptics.Provider switch
            {
                Services.Haptics.HapticProviderType.Lovense => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)",
                Services.Haptics.HapticProviderType.Buttplug => "Buttplug: Start Intiface Central, use default ws://localhost:12345",
                _ => "Lovense: Enter IP from Lovense Remote → Settings → Game Mode (http://IP:30010)"
            };

            // Per-feature haptic settings
            ChkHapticBubble.IsChecked = s.Haptics.BubblePopEnabled;
            SliderHapticBubble.Value = s.Haptics.BubblePopIntensity * 100;
            ChkHapticFlashDisplay.IsChecked = s.Haptics.FlashDisplayEnabled;
            SliderHapticFlashDisplay.Value = s.Haptics.FlashDisplayIntensity * 100;
            ChkHapticFlashClick.IsChecked = s.Haptics.FlashClickEnabled;
            SliderHapticFlashClick.Value = s.Haptics.FlashClickIntensity * 100;
            ChkHapticVideo.IsChecked = s.Haptics.VideoEnabled;
            SliderHapticVideo.Value = s.Haptics.VideoIntensity * 100;
            ChkHapticTargetHit.IsChecked = s.Haptics.TargetHitEnabled;
            SliderHapticTargetHit.Value = s.Haptics.TargetHitIntensity * 100;
            ChkHapticSubliminal.IsChecked = s.Haptics.SubliminalEnabled;
            SliderHapticSubliminal.Value = s.Haptics.SubliminalIntensity * 100;
            ChkHapticLevelUp.IsChecked = s.Haptics.LevelUpEnabled;
            SliderHapticLevelUp.Value = s.Haptics.LevelUpIntensity * 100;
            ChkHapticAchievement.IsChecked = s.Haptics.AchievementEnabled;
            SliderHapticAchievement.Value = s.Haptics.AchievementIntensity * 100;
            ChkHapticBouncingText.IsChecked = s.Haptics.BouncingTextEnabled;
            SliderHapticBouncingText.Value = s.Haptics.BouncingTextIntensity * 100;

            // Per-feature haptic mode dropdowns
            CmbHapticBubbleMode.SelectedIndex = (int)s.Haptics.BubblePopMode;
            CmbHapticFlashDisplayMode.SelectedIndex = (int)s.Haptics.FlashDisplayMode;
            CmbHapticFlashClickMode.SelectedIndex = (int)s.Haptics.FlashClickMode;
            CmbHapticVideoMode.SelectedIndex = (int)s.Haptics.VideoMode;
            CmbHapticTargetHitMode.SelectedIndex = (int)s.Haptics.TargetHitMode;
            CmbHapticSubliminalMode.SelectedIndex = (int)s.Haptics.SubliminalMode;
            CmbHapticLevelUpMode.SelectedIndex = (int)s.Haptics.LevelUpMode;
            CmbHapticAchievementMode.SelectedIndex = (int)s.Haptics.AchievementMode;
            CmbHapticBouncingTextMode.SelectedIndex = (int)s.Haptics.BouncingTextMode;

            // Discord Sharing Settings
            ChkShareAchievements.IsChecked = s.DiscordShareAchievements;
            ChkShareLevelUps.IsChecked = s.DiscordShareLevelUps;
            ChkUseAnonymousName.IsChecked = s.DiscordUseAnonymousName;
            ChkShowLevelInPresence.IsChecked = s.DiscordShowLevelInPresence;
            ChkAllowDiscordDm.IsChecked = s.AllowDiscordDm;

            // Update Discord UI (both main tab and Patreon tab)
            UpdateQuickDiscordUI();

            // Update level display
            UpdateLevelDisplay();

            // Update all slider text displays
            UpdateSliderTexts();

            // Start autonomy service if it was enabled (works independently of engine)
            var hasPatreonAccess = s.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && s.PlayerLevel >= 100 && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
                App.Logger?.Debug("MainWindow: Started autonomy service on settings load");
            }
        }

        /// <summary>
        /// Updates all slider text displays to match current slider values
        /// Called after loading settings since the value changed events are suppressed during load
        /// </summary>
        private void UpdateSliderTexts()
        {
            // Flash sliders
            if (TxtPerMin != null) TxtPerMin.Text = ((int)SliderPerMin.Value).ToString();
            if (TxtImages != null) TxtImages.Text = ((int)SliderImages.Value).ToString();
            if (TxtMaxOnScreen != null) TxtMaxOnScreen.Text = ((int)SliderMaxOnScreen.Value).ToString();
            if (TxtSize != null) TxtSize.Text = $"{(int)SliderSize.Value}%";
            if (TxtOpacity != null) TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
            if (TxtFade != null) TxtFade.Text = $"{(int)SliderFade.Value}%";
            
            // Video sliders
            if (TxtPerHour != null) TxtPerHour.Text = ((int)SliderPerHour.Value).ToString();
            if (TxtTargets != null) TxtTargets.Text = ((int)SliderTargets.Value).ToString();
            if (TxtDuration != null) TxtDuration.Text = $"{(int)SliderDuration.Value}s";
            if (TxtTargetSize != null) TxtTargetSize.Text = $"{(int)SliderTargetSize.Value}px";
            
            // Subliminal sliders
            if (TxtSubPerMin != null) TxtSubPerMin.Text = ((int)SliderSubPerMin.Value).ToString();
            if (TxtFrames != null) TxtFrames.Text = ((int)SliderFrames.Value).ToString();
            if (TxtSubOpacity != null) TxtSubOpacity.Text = $"{(int)SliderSubOpacity.Value}%";
            if (TxtWhisperVol != null) TxtWhisperVol.Text = $"{(int)SliderWhisperVol.Value}%";
            
            // Audio sliders
            if (TxtMaster != null) TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            if (TxtVideoVolume != null) TxtVideoVolume.Text = $"{(int)SliderVideoVolume.Value}%";
            if (TxtDuck != null) TxtDuck.Text = $"{(int)SliderDuck.Value}%";
            
            // Progression sliders
            if (TxtSpiralOpacity != null) TxtSpiralOpacity.Text = $"{(int)SliderSpiralOpacity.Value}%";
            if (TxtPinkOpacity != null) TxtPinkOpacity.Text = $"{(int)SliderPinkOpacity.Value}%";
            if (TxtBubbleFreq != null) TxtBubbleFreq.Text = ((int)SliderBubbleFreq.Value).ToString();
            if (TxtLockCardFreq != null) TxtLockCardFreq.Text = ((int)SliderLockCardFreq.Value).ToString();
            if (TxtLockCardRepeats != null) TxtLockCardRepeats.Text = $"{(int)SliderLockCardRepeats.Value}x";
            if (TxtBouncingTextSize != null) TxtBouncingTextSize.Text = $"{(int)SliderBouncingTextSize.Value}%";
            if (TxtMindWipeFreq != null) TxtMindWipeFreq.Text = $"{(int)SliderMindWipeFreq.Value}/h";
            if (TxtMindWipeVolume != null) TxtMindWipeVolume.Text = $"{(int)SliderMindWipeVolume.Value}%";
            if (TxtBrainDrainIntensity != null) TxtBrainDrainIntensity.Text = $"{(int)SliderBrainDrainIntensity.Value}%";
            
            // Scheduler sliders
            if (TxtRampDuration != null) TxtRampDuration.Text = $"{(int)SliderRampDuration.Value} min";
            if (TxtMultiplier != null) TxtMultiplier.Text = $"{SliderMultiplier.Value:F1}x";

            // Haptic sliders
            if (TxtHapticIntensity != null) TxtHapticIntensity.Text = $"{(int)SliderHapticIntensity.Value}%";
            if (TxtHapticBubble != null) TxtHapticBubble.Text = $"{(int)SliderHapticBubble.Value}%";
            if (TxtHapticFlashDisplay != null) TxtHapticFlashDisplay.Text = $"{(int)SliderHapticFlashDisplay.Value}%";
            if (TxtHapticFlashClick != null) TxtHapticFlashClick.Text = $"{(int)SliderHapticFlashClick.Value}%";
            if (TxtHapticVideo != null) TxtHapticVideo.Text = $"{(int)SliderHapticVideo.Value}%";
            if (TxtHapticTargetHit != null) TxtHapticTargetHit.Text = $"{(int)SliderHapticTargetHit.Value}%";
            if (TxtHapticSubliminal != null) TxtHapticSubliminal.Text = $"{(int)SliderHapticSubliminal.Value}%";
            if (TxtHapticLevelUp != null) TxtHapticLevelUp.Text = $"{(int)SliderHapticLevelUp.Value}%";
            if (TxtHapticAchievement != null) TxtHapticAchievement.Text = $"{(int)SliderHapticAchievement.Value}%";
        }

        private void SaveSettings()
        {
            var s = App.Settings.Current;

            // Flash
            s.FlashEnabled = ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = ChkClickable.IsChecked ?? true;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            s.FlashFrequency = (int)SliderPerMin.Value;
            s.SimultaneousImages = (int)SliderImages.Value;
            s.HydraLimit = (int)SliderMaxOnScreen.Value;

            // Visuals
            s.ImageScale = (int)SliderSize.Value;
            s.FlashOpacity = (int)SliderOpacity.Value;
            s.FadeDuration = (int)SliderFade.Value;

            // Video
            s.MandatoryVideosEnabled = ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SliderPerHour.Value;
            s.StrictLockEnabled = ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SliderTargets.Value;
            s.RandomizeAttentionTargets = ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SliderDuration.Value;
            s.AttentionSize = (int)SliderTargetSize.Value;

            // Subliminals
            s.SubliminalEnabled = ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SliderFrames.Value;
            s.SubliminalOpacity = (int)SliderSubOpacity.Value;
            s.SubAudioEnabled = ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SliderWhisperVol.Value;

            // System
            s.DualMonitorEnabled = ChkDualMon.IsChecked ?? true;
            s.RunOnStartup = ChkWinStart.IsChecked ?? false;
            s.ForceVideoOnLaunch = ChkVidLaunch.IsChecked ?? false;
            s.AutoStartEngine = ChkAutoRun.IsChecked ?? false;
            s.StartMinimized = ChkStartHidden.IsChecked ?? false;
            s.PanicKeyEnabled = !(ChkNoPanic.IsChecked ?? false);
            s.OfflineMode = ChkOfflineMode.IsChecked ?? false;

            // Audio
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Progression
            s.SpiralEnabled = ChkSpiralEnabled.IsChecked ?? false;
            s.SpiralOpacity = (int)SliderSpiralOpacity.Value;
            s.PinkFilterEnabled = ChkPinkFilterEnabled.IsChecked ?? false;
            s.PinkFilterOpacity = (int)SliderPinkOpacity.Value;
            s.BubblesEnabled = ChkBubblesEnabled.IsChecked ?? false;
            s.BubblesFrequency = (int)SliderBubbleFreq.Value;
            s.LockCardEnabled = ChkLockCardEnabled.IsChecked ?? false;
            s.LockCardFrequency = (int)SliderLockCardFreq.Value;
            s.LockCardRepeats = (int)SliderLockCardRepeats.Value;
            s.LockCardStrict = ChkLockCardStrict.IsChecked ?? false;

            // Brain Drain
            s.BrainDrainEnabled = ChkBrainDrainEnabled.IsChecked ?? false;
            s.BrainDrainIntensity = (int)SliderBrainDrainIntensity.Value;
            s.BrainDrainHighRefresh = ChkBrainDrainHighRefresh.IsChecked ?? false;

            // Scheduler - track if settings changed
            var schedulerWasEnabled = s.SchedulerEnabled;
            s.SchedulerEnabled = ChkSchedulerEnabled.IsChecked ?? false;
            s.SchedulerStartTime = TxtStartTime.Text;
            s.SchedulerEndTime = TxtEndTime.Text;
            s.SchedulerMonday = ChkMon.IsChecked ?? true;
            s.SchedulerTuesday = ChkTue.IsChecked ?? true;
            s.SchedulerWednesday = ChkWed.IsChecked ?? true;
            s.SchedulerThursday = ChkThu.IsChecked ?? true;
            s.SchedulerFriday = ChkFri.IsChecked ?? true;
            s.SchedulerSaturday = ChkSat.IsChecked ?? true;
            s.SchedulerSunday = ChkSun.IsChecked ?? true;

            // If scheduler was just enabled or settings changed, reset flags and check immediately
            if (s.SchedulerEnabled && !schedulerWasEnabled)
            {
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
                // Check scheduler immediately after save completes
                Dispatcher.BeginInvoke(new Action(() => CheckSchedulerAfterSettingsChange()), System.Windows.Threading.DispatcherPriority.Background);
            }
            s.IntensityRampEnabled = ChkRampEnabled.IsChecked ?? false;
            s.RampDurationMinutes = (int)SliderRampDuration.Value;
            s.SchedulerMultiplier = SliderMultiplier.Value;
            
            // Ramp Links
            s.RampLinkFlashOpacity = ChkRampLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ChkRampLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ChkRampLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ChkRampLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ChkRampLinkSubAudio.IsChecked ?? false;
            s.EndSessionOnRampComplete = ChkEndAtRamp.IsChecked ?? false;

            App.Settings.Save();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // First, apply current settings to the settings object
            SaveSettings();

            // Find current preset
            var currentPresetName = App.Settings.Current.CurrentPresetName;
            var currentPreset = _allPresets.FirstOrDefault(p => p.Name == currentPresetName);

            // Determine if we should create new or overwrite
            if (currentPreset == null || currentPreset.IsDefault || string.IsNullOrEmpty(currentPresetName))
            {
                // No preset, default preset, or unknown - ask to create new
                var result = MessageBox.Show(
                    "Would you like to save your current settings as a new preset?\n\n" +
                    "This will create a custom preset that you can load later.",
                    "Save as Preset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    PromptSaveNewPreset();
                }
                else
                {
                    MessageBox.Show("Settings saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Custom user preset - ask to overwrite
                var result = MessageBox.Show(
                    $"Do you want to overwrite preset '{currentPreset.Name}' with your current settings?\n\n" +
                    "Click 'Yes' to overwrite, 'No' to save as new preset, or 'Cancel' to just save settings.",
                    "Overwrite Preset?",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Overwrite existing preset
                    var updated = Models.Preset.FromSettings(App.Settings.Current, currentPreset.Name, currentPreset.Description);
                    updated.Id = currentPreset.Id;
                    updated.CreatedAt = currentPreset.CreatedAt;

                    var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == currentPreset.Id);
                    if (index >= 0)
                    {
                        App.Settings.Current.UserPresets[index] = updated;
                        App.Settings.Save();
                        RefreshPresetsList();

                        App.Logger?.Information("Overwritten preset: {Name}", updated.Name);
                        MessageBox.Show($"Preset '{updated.Name}' updated!", "Preset Saved",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    // Save as new preset
                    PromptSaveNewPreset();
                }
                else
                {
                    // Cancel - just show settings saved message
                    MessageBox.Show("Settings saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show("Engine is running. Stop and exit?", "Confirm Exit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
                StopEngine();
            }
            _exitRequested = true;
            SaveSettings();
            Close(); // This will now actually close since _exitRequested is true
        }

        private void BtnMainHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hide browser (WebView2 doesn't respect WPF z-order)
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;
            MainTutorialOverlay.Visibility = Visibility.Visible;
        }

        private void MainTutorial_Close(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_Close(object sender, MouseButtonEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_ContentClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on the content
            e.Handled = true;
        }

        private TutorialOverlay? _tutorialOverlay;

        private void BtnStartTutorial_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial();
        }

        public void StartTutorial(TutorialType type = TutorialType.FullTour)
        {
            if (_tutorialOverlay != null) return;

            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Hidden;

            // Configure tutorial callbacks for tab switching
            App.Tutorial.ConfigureCallbacks(
                showSettings: () => ShowTab("settings"),
                showPresets: () => { ShowTab("presets"); RefreshPresetsList(); },
                showProgression: () => ShowTab("progression"),
                showAchievements: () => ShowTab("achievements"),
                showCompanion: () => ShowTab("companion"),
                showPatreon: () => ShowTab("patreon")
            );

            App.Tutorial.Start(type);
            _tutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _tutorialOverlay.Closed += (s, e) =>
            {
                _tutorialOverlay = null;
                if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            };
            _tutorialOverlay.Show();
        }

        #region Feature Tutorial Button Handlers

        private void BtnTutorialGettingStarted_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.GettingStarted);
        }

        private void BtnTutorialSettings_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Settings);
        }

        private void BtnTutorialPresets_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Presets);
        }

        private void BtnTutorialProgression_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Progression);
        }

        private void BtnTutorialAchievements_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Achievements);
        }

        private void BtnTutorialCompanion_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Companion);
        }

        private void BtnTutorialPatreon_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Patreon);
        }

        private void BtnTutorialAvatar_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (BrowserContainer != null) BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Avatar);
        }

        #endregion

        private void OpenLinktree()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            // Update all value labels
            TxtPerMin.Text = ((int)SliderPerMin.Value).ToString();
            TxtImages.Text = ((int)SliderImages.Value).ToString();
            TxtMaxOnScreen.Text = ((int)SliderMaxOnScreen.Value).ToString();
            TxtSize.Text = $"{(int)SliderSize.Value}%";
            TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
            TxtFade.Text = $"{(int)SliderFade.Value}%";
            TxtPerHour.Text = ((int)SliderPerHour.Value).ToString();
            TxtTargets.Text = ((int)SliderTargets.Value).ToString();
            TxtDuration.Text = ((int)SliderDuration.Value).ToString();
            TxtTargetSize.Text = ((int)SliderTargetSize.Value).ToString();
            TxtSubPerMin.Text = ((int)SliderSubPerMin.Value).ToString();
            TxtFrames.Text = ((int)SliderFrames.Value).ToString();
            TxtSubOpacity.Text = $"{(int)SliderSubOpacity.Value}%";
            TxtWhisperVol.Text = $"{(int)SliderWhisperVol.Value}%";
            TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            TxtDuck.Text = $"{(int)SliderDuck.Value}%";
            TxtSpiralOpacity.Text = $"{(int)SliderSpiralOpacity.Value}%";
            TxtPinkOpacity.Text = $"{(int)SliderPinkOpacity.Value}%";
            TxtBubbleFreq.Text = ((int)SliderBubbleFreq.Value).ToString();
            TxtRampDuration.Text = $"{(int)SliderRampDuration.Value} min";
            TxtMultiplier.Text = $"{SliderMultiplier.Value:F1}x";
        }

        private void UpdateLevelDisplay()
        {
            var s = App.Settings.Current;
            var level = s.PlayerLevel;
            var xp = s.PlayerXP;
            var xpNeeded = App.Progression.GetXPForLevel(level);

            TxtLevel.Text = $"Lvl {level}";
            TxtLevelLabel.Text = $"LVL {level}";
            TxtXP.Text = $"{(int)xp} / {(int)xpNeeded} XP";

            // Update XP bar width
            var progress = Math.Min(1.0, xp / xpNeeded);
            XPBar.Width = progress * (XPBar.Parent as Border)?.ActualWidth ?? 100;

            // Update title based on level
            TxtPlayerTitle.Text = level switch
            {
                < 20 => "BASIC BIMBO",
                < 50 => "DUMB AIRHEAD",
                < 100 => "SYNTHETIC BLOWDOLL",
                _ => "PERFECT FUCKPUPPET"
            };

            // Update unlockables visibility based on level
            UpdateUnlockablesVisibility(level);

            // Update XP bar login state
            UpdateXPBarLoginState();
        }

        /// <summary>
        /// Updates the XP bar visibility based on login status.
        /// Shows a login prompt overlay when user is not logged in.
        /// </summary>
        private void UpdateXPBarLoginState()
        {
            var isLoggedIn = (App.Discord?.IsAuthenticated == true) || (App.Patreon?.IsAuthenticated == true);

            if (XPBarLoginOverlay != null && XPBarContent != null)
            {
                if (isLoggedIn)
                {
                    // User is logged in - show normal XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Collapsed;
                    XPBarContent.Opacity = 1.0;
                }
                else
                {
                    // User is not logged in - show overlay and gray out XP bar
                    XPBarLoginOverlay.Visibility = Visibility.Visible;
                    XPBarContent.Opacity = 0.3;
                }
            }
        }

        private void UpdateUnlockablesVisibility(int level)
        {
            try
            {
                App.Logger?.Debug("UpdateUnlockablesVisibility: Updating visibility for level {Level}", level);

                // Level 10 unlocks: Spiral Overlay, Pink Filter
                var level10Unlocked = level >= 10;
                if (SpiralLocked != null) SpiralLocked.Visibility = level10Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (SpiralUnlocked != null) SpiralUnlocked.Visibility = level10Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (PinkFilterLocked != null) PinkFilterLocked.Visibility = level10Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (PinkFilterUnlocked != null) PinkFilterUnlocked.Visibility = level10Unlocked ? Visibility.Visible : Visibility.Collapsed;
                
                if (SpiralFeatureImage != null) SetFeatureImageBlur(SpiralFeatureImage, !level10Unlocked);
                if (PinkFilterFeatureImage != null) SetFeatureImageBlur(PinkFilterFeatureImage, !level10Unlocked);
                
                // Level 20 unlocks: Bubbles
                var level20Unlocked = level >= 20;
                if (BubblesLocked != null) BubblesLocked.Visibility = level20Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (BubblesUnlocked != null) BubblesUnlocked.Visibility = level20Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (BubblePopFeatureImage != null) SetFeatureImageBlur(BubblePopFeatureImage, !level20Unlocked);
                
                // Level 35 unlocks: Lock Card
                var level35Unlocked = level >= 35;
                if (LockCardLocked != null) LockCardLocked.Visibility = level35Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (LockCardUnlocked != null) LockCardUnlocked.Visibility = level35Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (LockCardFeatureImage != null) SetFeatureImageBlur(LockCardFeatureImage, !level35Unlocked);
                
                // Level 50 unlocks: Bubble Count Game
                var level50Unlocked = level >= 50;
                if (Level50Locked != null) Level50Locked.Visibility = level50Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (Level50Unlocked != null) Level50Unlocked.Visibility = level50Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (BubbleCountFeatureImage != null) SetFeatureImageBlur(BubbleCountFeatureImage, !level50Unlocked);
                
                // Level 60 unlocks: Bouncing Text
                var level60Unlocked = level >= 60;
                if (Level60Locked != null) Level60Locked.Visibility = level60Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (Level60Unlocked != null) Level60Unlocked.Visibility = level60Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (BouncingTextFeatureImage != null) SetFeatureImageBlur(BouncingTextFeatureImage, !level60Unlocked);
                
                // Level 75 unlocks: Mind Wipe
                var level75Unlocked = level >= 75;
                if (MindWipeLocked != null) MindWipeLocked.Visibility = level75Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (MindWipeUnlocked != null) MindWipeUnlocked.Visibility = level75Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (MindWipeFeatureImage != null) SetFeatureImageBlur(MindWipeFeatureImage, !level75Unlocked);

                // Level 70 unlocks: Brain Drain
                var level70Unlocked = level >= 70;
                if (BrainDrainLocked != null) BrainDrainLocked.Visibility = level70Unlocked ? Visibility.Collapsed : Visibility.Visible;
                if (BrainDrainUnlocked != null) BrainDrainUnlocked.Visibility = level70Unlocked ? Visibility.Visible : Visibility.Collapsed;
                if (BrainDrainFeatureImage != null) SetFeatureImageBlur(BrainDrainFeatureImage, !level70Unlocked);

                // Bambi Takeover: Requires Patreon (any tier)
                var autonomyUnlocked = App.Patreon?.HasPremiumAccess == true;
                if (AutonomyLocked != null) AutonomyLocked.Visibility = autonomyUnlocked ? Visibility.Collapsed : Visibility.Visible;
                if (AutonomyUnlocked != null) AutonomyUnlocked.Visibility = autonomyUnlocked ? Visibility.Visible : Visibility.Collapsed;

                // Update lock message
                if (TxtAutonomyLockStatus != null && TxtAutonomyLockMessage != null)
                {
                    TxtAutonomyLockStatus.Text = "🔒 Patreon Only";
                    TxtAutonomyLockMessage.Text = "Support on Patreon to unlock";
                }

                App.Logger?.Debug("UpdateUnlockablesVisibility: Completed successfully.");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("UpdateUnlockablesVisibility: Error updating unlockables visibility: {Error}", ex.Message);
            }
        }
        
        /// <summary>
        /// Applies or removes blur effect on feature images based on lock state
        /// </summary>
        private void SetFeatureImageBlur(Rectangle? featureImageRect, bool blur)
        {
            try
            {
                if (featureImageRect == null)
                {
                    App.Logger?.Warning("SetFeatureImageBlur: featureImageRect is null.");
                    return;
                }

                if (blur)
                {
                    featureImageRect.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 };
                    App.Logger?.Debug("SetFeatureImageBlur: Applied blur to {ElementName}", featureImageRect.Name);
                }
                else
                {
                    featureImageRect.Effect = null;
                    App.Logger?.Debug("SetFeatureImageBlur: Removed blur from {ElementName}", featureImageRect.Name);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SetFeatureImageBlur: Error setting blur effect for {ElementName}: {Error}", featureImageRect?.Name, ex.Message);
            }
        }

        #endregion

        #region Slider Events

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPerMin == null) return;
            TxtPerMin.Text = ((int)e.NewValue).ToString();
            UpdateAudioLinkState();
            ApplySettingsLive();
        }

        private void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtImages == null) return;
            TxtImages.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMaxOnScreen == null) return;
            TxtMaxOnScreen.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSize == null) return;
            TxtSize.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtOpacity == null) return;
            TxtOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderFade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFade == null) return;
            TxtFade.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderFlashDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFlashDuration == null) return;
            TxtFlashDuration.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.FlashDuration = (int)e.NewValue;
        }

        private void ChkFlashAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkFlashAudio.IsChecked ?? true;
            App.Settings.Current.FlashAudioEnabled = isEnabled;
            
            // Enable/disable duration slider based on audio link
            SliderFlashDuration.IsEnabled = !isEnabled;
            SliderFlashDuration.Opacity = isEnabled ? 0.5 : 1.0;
            
            // Show/hide warning
            TxtAudioWarning.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateAudioLinkState()
        {
            if (_isLoading) return;
            
            var flashFreq = (int)SliderPerMin.Value;
            
            // If flashes > 60, force audio OFF and disable checkbox
            if (flashFreq > 60)
            {
                ChkFlashAudio.IsChecked = false;
                ChkFlashAudio.IsEnabled = false;
                App.Settings.Current.FlashAudioEnabled = false;
                SliderFlashDuration.IsEnabled = true;
                SliderFlashDuration.Opacity = 1.0;
                TxtAudioWarning.Visibility = Visibility.Visible;
                TxtAudioWarning.Text = "⚠ Audio off >60/h";
            }
            else
            {
                ChkFlashAudio.IsEnabled = true;
                TxtAudioWarning.Text = "⚠ Max 60/h";
                TxtAudioWarning.Visibility = (ChkFlashAudio.IsChecked ?? true) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPerHour == null) return;
            TxtPerHour.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTargets == null) return;
            TxtTargets.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void ChkRandomizeTargets_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtDuration == null) return;
            TxtDuration.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtTargetSize == null) return;
            TxtTargetSize.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSubPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSubPerMin == null) return;
            TxtSubPerMin.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtFrames == null) return;
            TxtFrames.Text = ((int)e.NewValue).ToString();
            ApplySettingsLive();
        }

        private void SliderSubOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSubOpacity == null) return;
            TxtSubOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtWhisperVol == null) return;
            TxtWhisperVol.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMaster == null) return;
            TxtMaster.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();

            // Update volume on all currently playing audio
            var volume = (int)e.NewValue;
            App.Video?.UpdateMasterVolume(volume);
            App.BrainDrain?.UpdateMasterVolume(volume);
        }

        private void SliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtVideoVolume == null) return;
            TxtVideoVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.VideoVolume = (int)e.NewValue;
            App.Video?.UpdateVideoVolume((int)e.NewValue);
        }

        private void SliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtDuck == null) return;
            TxtDuck.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void ChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // If ducking was just disabled, immediately restore audio for any ducked sessions
            if (ChkAudioDuck.IsChecked == false)
            {
                App.Audio?.Unduck();
            }

            ApplySettingsLive();
        }

        private void ChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplySettingsLive();
        }

        private void SliderSpiralOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtSpiralOpacity == null) return;
            TxtSpiralOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderPinkOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPinkOpacity == null) return;
            TxtPinkOpacity.Text = $"{(int)e.NewValue}%";
            ApplySettingsLive();
        }

        private void SliderBubbleFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBubbleFreq == null) return;
            TxtBubbleFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BubblesFrequency = (int)e.NewValue;

            if (_isRunning)
            {
                App.Bubbles.RefreshFrequency();
            }

            App.Settings.Save();
        }

        private void ChkSpiralEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkSpiralEnabled.IsChecked ?? false;
            App.Settings.Current.SpiralEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Spiral overlay toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkPinkFilterEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkPinkFilterEnabled.IsChecked ?? false;
            App.Settings.Current.PinkFilterEnabled = isEnabled;
            
            // Immediately update overlay if engine is running
            if (_isRunning)
            {
                App.Overlay.RefreshOverlays();
                App.Logger?.Information("Pink filter toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkBubblesEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkBubblesEnabled.IsChecked ?? false;
            App.Settings.Current.BubblesEnabled = isEnabled;
            
            // Immediately update bubbles if engine is running
            if (_isRunning)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 20)
                {
                    App.Bubbles.Start();
                }
                else
                {
                    App.Bubbles.Stop();
                }
                App.Logger?.Information("Bubbles toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void ChkLockCardEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkLockCardEnabled.IsChecked ?? false;
            App.Settings.Current.LockCardEnabled = isEnabled;
            
            // Immediately update lock card service if engine is running
            if (_isRunning)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 35)
                {
                    App.LockCard.Start();
                }
                else
                {
                    App.LockCard.Stop();
                }
                App.Logger?.Information("Lock Card toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void SliderLockCardFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtLockCardFreq == null) return;
            TxtLockCardFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.LockCardFrequency = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderLockCardRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtLockCardRepeats == null) return;
            TxtLockCardRepeats.Text = $"{(int)e.NewValue}x";
            App.Settings.Current.LockCardRepeats = (int)e.NewValue;
            App.Settings.Save();
        }

        #region Bubble Count (Level 50)

        private void ChkBubbleCountEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkBubbleCountEnabled.IsChecked ?? false;
            App.Settings.Current.BubbleCountEnabled = isEnabled;
            
            // Immediately update service if engine is running
            if (_isRunning)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 50)
                {
                    App.BubbleCount.Start();
                }
                else
                {
                    App.BubbleCount.Stop();
                }
                App.Logger?.Information("Bubble Count toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void SliderBubbleCountFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBubbleCountFreq == null) return;
            TxtBubbleCountFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BubbleCountFrequency = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BubbleCount.RefreshSchedule();
            }
            
            App.Settings.Save();
        }

        private void CmbBubbleCountDifficulty_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || CmbBubbleCountDifficulty.SelectedItem == null) return;
            
            var item = CmbBubbleCountDifficulty.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item?.Tag != null && int.TryParse(item.Tag.ToString(), out int difficulty))
            {
                App.Settings.Current.BubbleCountDifficulty = difficulty;
                App.Settings.Save();
            }
        }

        private void ChkBubbleCountStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkBubbleCountStrict.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• After 3 wrong attempts, a mercy lock card appears\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    _isLoading = true;
                    ChkBubbleCountStrict.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }

            App.Settings.Current.BubbleCountStrictLock = isEnabled;
            App.Settings.Save();
        }

        private void BtnTestBubbleCount_Click(object sender, RoutedEventArgs e)
        {
            App.BubbleCount.TriggerGame(forceTest: true);
        }

        #endregion

        #region Bouncing Text (Level 60)

        private void ChkBouncingTextEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkBouncingTextEnabled.IsChecked ?? false;
            App.Settings.Current.BouncingTextEnabled = isEnabled;
            
            // Immediately update service if engine is running
            if (_isRunning)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 60)
                {
                    App.BouncingText.Start();
                }
                else
                {
                    App.BouncingText.Stop();
                }
                App.Logger?.Information("Bouncing Text toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        private void SliderBouncingTextSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBouncingTextSpeed == null) return;
            TxtBouncingTextSpeed.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BouncingTextSpeed = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BouncingText.Refresh();
            }
            App.Settings.Save();
        }

        private void SliderBouncingTextSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBouncingTextSize == null) return;
            TxtBouncingTextSize.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BouncingTextSize = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BouncingText.Refresh();
            }
            App.Settings.Save();
        }

        private void BtnEditBouncingText_Click(object sender, RoutedEventArgs e)
        {
            var editor = new TextEditorDialog("Bouncing Text Phrases", App.Settings.Current.BouncingTextPool);
            editor.Owner = this;
            
            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                App.Settings.Current.BouncingTextPool = editor.ResultData;
                App.Logger?.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
                App.Settings.Save();
            }
        }

        #endregion

        #region Mind Wipe (Lvl 75)

        private void ChkMindWipeEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ChkMindWipeEnabled.IsChecked ?? false;
            App.Settings.Current.MindWipeEnabled = isEnabled;
            
            // Immediately update service if engine is running (non-session mode)
            if (_isRunning && _sessionEngine?.CurrentSession == null)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 75)
                {
                    App.MindWipe.Start(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
                }
                else
                {
                    App.MindWipe.Stop();
                }
                App.Logger?.Information("Mind Wipe toggled: {Enabled}", isEnabled);
            }
            App.Settings.Save();
        }

        private void SliderMindWipeFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMindWipeFreq == null) return;
            TxtMindWipeFreq.Text = $"{(int)e.NewValue}/h";
            App.Settings.Current.MindWipeFrequency = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.MindWipe.UpdateSettings(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
            }
            App.Settings.Save();
        }

        private void SliderMindWipeVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMindWipeVolume == null) return;
            TxtMindWipeVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.MindWipeVolume = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.MindWipe.UpdateSettings(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
            }
            App.Settings.Save();
        }

        private void ChkMindWipeLoop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isLooping = ChkMindWipeLoop.IsChecked ?? false;
            App.Settings.Current.MindWipeLoop = isLooping;
            
            // Start/stop loop immediately
            if (isLooping)
            {
                App.MindWipe.StartLoop(App.Settings.Current.MindWipeVolume / 100.0);
            }
            else
            {
                App.MindWipe.StopLoop();
            }
            
            App.Settings.Save();
            App.Logger?.Information("Mind Wipe loop toggled: {Looping}", isLooping);
        }

        private void BtnTestMindWipe_Click(object sender, RoutedEventArgs e)
        {
            App.MindWipe.TriggerOnce();
        }

        #endregion

        #region Brain Drain (Lvl 70)

        private void ChkBrainDrainEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkBrainDrainEnabled.IsChecked ?? false;
            App.Settings.Current.BrainDrainEnabled = isEnabled;

            if (_isRunning)
            {
                if (isEnabled && App.Settings.Current.PlayerLevel >= 70)
                {
                    App.BrainDrain.Start();
                }
                else
                {
                    App.BrainDrain.Stop();
                }
                App.Logger?.Information("Brain Drain toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        private void SliderBrainDrainIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtBrainDrainIntensity == null) return;
            TxtBrainDrainIntensity.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BrainDrainIntensity = (int)e.NewValue;

            if (_isRunning)
            {
                App.BrainDrain.UpdateSettings();
            }
            App.Settings.Save();
        }

        private void ChkBrainDrainHighRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isHighRefresh = ChkBrainDrainHighRefresh.IsChecked ?? false;
            App.Settings.Current.BrainDrainHighRefresh = isHighRefresh;

            // If brain drain is running, restart it to apply new interval
            if (_isRunning && App.BrainDrain.IsRunning)
            {
                App.BrainDrain.Stop();
                App.BrainDrain.Start();
            }

            App.Logger?.Information("Brain Drain High Refresh toggled: {Enabled}", isHighRefresh);
            App.Settings.Save();
        }

        #endregion

        #region Autonomy Mode (Lvl 100)

        private void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkAutonomyEnabled.IsChecked ?? false;

            // If enabling for the first time, show consent dialog
            if (isEnabled && !App.Settings.Current.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own within your configured intensity settings.\n\n" +
                    "You can disable this at any time. Videos triggered autonomously will NEVER use strict mode.\n\n" +
                    "Do you consent to enable Autonomy Mode?",
                    "Enable Autonomy Mode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    App.Settings.Current.AutonomyConsentGiven = true;
                }
                else
                {
                    ChkAutonomyEnabled.IsChecked = false;
                    return;
                }
            }

            App.Settings.Current.AutonomyModeEnabled = isEnabled;

            // Start/stop autonomy service (works independently of engine!)
            // Requires Patreon + Consent
            var hasPatreon = App.Settings.Current.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;

            if (isEnabled)
            {
                if (!hasPatreon)
                {
                    App.Logger?.Warning("Autonomy Mode enabled but Patreon access missing - service will not start");
                    MessageBox.Show(
                        "Autonomy Mode requires Patreon access.\n\n" +
                        "The setting has been saved, but the feature will not activate until you have Patreon access.",
                        "Patreon Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    App.Autonomy?.Stop();
                }
                else if (App.Settings.Current.AutonomyConsentGiven)
                {
                    App.Autonomy?.Start();
                }
            }
            else
            {
                App.Autonomy?.Stop();
            }
            App.Logger?.Information("Autonomy Mode toggled: {Enabled} (Engine running: {EngineRunning}, Patreon: {Patreon})",
                isEnabled, _isRunning, hasPatreon);

            App.Settings.Save();

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        private void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var isCurrentlyEnabled = settings.AutonomyModeEnabled;

            // If starting for the first time, show consent dialog
            if (!isCurrentlyEnabled && !settings.AutonomyConsentGiven)
            {
                var result = MessageBox.Show(
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos (without strict mode)\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own schedule based on your intensity setting.\n" +
                    "You can stop her at any time by clicking the Stop button.\n\n" +
                    "Do you consent to enabling Autonomy Mode?",
                    "Autonomy Mode Consent",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                settings.AutonomyConsentGiven = true;
            }

            // Toggle the mode
            settings.AutonomyModeEnabled = !isCurrentlyEnabled;
            App.Settings.Save();

            // Update button appearance
            UpdateAutonomyButtonState(!isCurrentlyEnabled);

            // Start/stop autonomy service
            if (!isCurrentlyEnabled)
            {
                App.Autonomy?.Start();
            }
            else
            {
                App.Autonomy?.Stop();
            }

            App.Logger?.Information("Autonomy Mode button toggled: {Enabled}", !isCurrentlyEnabled);

            // Sync avatar menu state
            Dispatcher.BeginInvoke(() => _avatarTubeWindow?.UpdateQuickMenuState());
        }

        private void UpdateAutonomyButtonState(bool isEnabled)
        {
            if (BtnAutonomyStartStop == null) return;

            if (isEnabled)
            {
                BtnAutonomyStartStop.Content = "■ Stop";
                BtnAutonomyStartStop.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // Pink
            }
            else
            {
                BtnAutonomyStartStop.Content = "▶ Start";
                BtnAutonomyStartStop.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // Light green
            }
        }

        /// <summary>
        /// Called from AvatarTubeWindow to sync the button/checkbox state when toggled from avatar menu
        /// </summary>
        public void SyncAutonomyCheckbox(bool isEnabled)
        {
            // Read the actual setting value to ensure consistency
            var actualValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;
            App.Logger?.Information("MainWindow.SyncAutonomyCheckbox called with isEnabled={IsEnabled}, actualSetting={Actual}", isEnabled, actualValue);

            // Use BeginInvoke to ensure UI update happens after current operation completes
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    // Re-read the setting inside dispatcher to get the latest value
                    var settingValue = App.Settings?.Current?.AutonomyModeEnabled ?? false;

                    // Update button state
                    UpdateAutonomyButtonState(settingValue);

                    // Also update hidden checkbox for backwards compatibility
                    if (ChkAutonomyEnabled != null)
                    {
                        var wasLoading = _isLoading;
                        _isLoading = true;
                        ChkAutonomyEnabled.IsChecked = settingValue;
                        _isLoading = wasLoading;
                    }

                    App.Logger?.Information("MainWindow.SyncAutonomyCheckbox synced to {Value}", settingValue);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "MainWindow.SyncAutonomyCheckbox failed");
                }
            }));
        }

        private void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyIntensity == null) return;
            TxtAutonomyIntensity.Text = $"{(int)e.NewValue}";
            App.Settings.Current.AutonomyIntensity = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyCooldown == null) return;
            TxtAutonomyCooldown.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyCooldownSeconds = (int)e.NewValue;
            App.Settings.Save();
        }

        private void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyInterval == null) return;
            TxtAutonomyInterval.Text = $"{(int)e.NewValue}s";
            App.Settings.Current.AutonomyRandomIntervalSeconds = (int)e.NewValue;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyIdleTriggerEnabled = ChkAutonomyIdle.IsChecked ?? false;
            App.Autonomy?.RefreshIdleTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyRandomTriggerEnabled = ChkAutonomyRandom.IsChecked ?? false;
            App.Autonomy?.RefreshRandomTimer();
            App.Settings.Save();
        }

        private void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyTimeAwareEnabled = ChkAutonomyTimeAware.IsChecked ?? false;
            App.Settings.Save();
        }

        private void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.AutonomyCanTriggerFlash = ChkAutonomyFlash.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerVideo = ChkAutonomyVideo.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSubliminal = ChkAutonomySubliminal.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbles = ChkAutonomyBubbles.IsChecked ?? false;
            App.Settings.Current.AutonomyCanComment = ChkAutonomyComment.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerMindWipe = ChkAutonomyMindWipe.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerLockCard = ChkAutonomyLockCard.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerSpiral = ChkAutonomySpiral.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerPinkFilter = ChkAutonomyPinkFilter.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBouncingText = ChkAutonomyBouncingText.IsChecked ?? false;
            App.Settings.Current.AutonomyCanTriggerBubbleCount = ChkAutonomyBubbleCount.IsChecked ?? false;
            App.Settings.Save();
        }

        private void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtAutonomyAnnounce == null) return;
            TxtAutonomyAnnounce.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.AutonomyAnnouncementChance = (int)e.NewValue;
            App.Settings.Save();
        }

        private void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.TestTrigger();
        }

        private void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            App.Autonomy?.ForceStart();
        }

        #endregion

        private void SliderRampDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtRampDuration == null) return;
            TxtRampDuration.Text = $"{(int)e.NewValue} min";
            ApplySettingsLive();
        }

        private void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtMultiplier == null) return;
            TxtMultiplier.Text = $"{e.NewValue:F1}x";
            ApplySettingsLive();
        }

        #endregion

        #region Button Events
        
        private void ImgLogo_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Track for Neon Obsession achievement (20 rapid clicks on the avatar/logo)
            App.Achievements?.TrackAvatarClick();

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Logo/Avatar clicked! Count: {Count}/20", clickCount);

            // Easter egg tracking (100 clicks in 60 seconds)
            if (!_easterEggTriggered)
            {
                var now = DateTime.Now;
                if (_easterEggFirstClick == DateTime.MinValue || (now - _easterEggFirstClick).TotalSeconds > 60)
                {
                    // Reset if more than 60 seconds passed
                    _easterEggFirstClick = now;
                    _easterEggClickCount = 1;
                }
                else
                {
                    _easterEggClickCount++;
                    if (_easterEggClickCount >= 100)
                    {
                        _easterEggTriggered = true;
                        ShowEasterEgg();
                    }
                }
            }

            // Visual feedback - quick pulse effect
            if (ImgLogo != null)
            {
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 1.05,
                    Duration = TimeSpan.FromMilliseconds(80),
                    AutoReverse = true
                };

                var scaleTransform = ImgLogo.RenderTransform as System.Windows.Media.ScaleTransform;
                if (scaleTransform == null)
                {
                    scaleTransform = new System.Windows.Media.ScaleTransform(1, 1);
                    ImgLogo.RenderTransformOrigin = new Point(0.5, 0.5);
                    ImgLogo.RenderTransform = scaleTransform;
                }

                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
                scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
            }
        }

        #region Marquee Banner

        private void InitializeMarqueeBanner()
        {
            try
            {
                // Migrate old message to new default if needed
                var currentSaved = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(currentSaved) ||
                    currentSaved.Contains("WELCOME TO YOUR CONDITIONING") ||
                    currentSaved.Contains("RELAX AND SUBMIT"))
                {
                    App.Settings.Current.MarqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }

                // Need to wait for layout to measure text width
                MarqueeText.Loaded += (s, e) => StartMarqueeAnimation();
                MarqueeCanvas.SizeChanged += (s, e) => StartMarqueeAnimation();

                // Start immediately if already loaded
                if (MarqueeText.IsLoaded)
                {
                    Dispatcher.BeginInvoke(new Action(StartMarqueeAnimation), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                // Fetch from server on startup (with short delay)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(RefreshMarqueeFromSettings));
                }));

                // Check for server-controlled update banner (fallback for when auto-update fails)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(5000).ContinueWith(_ => Dispatcher.Invoke(CheckServerUpdateBanner));
                }));

                // Start 5-minute refresh timer to check for server-side message updates
                _marqueeRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(5)
                };
                _marqueeRefreshTimer.Tick += (s, e) => RefreshMarqueeFromSettings();
                _marqueeRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to initialize marquee banner: {Error}", ex.Message);
            }
        }

        private async void RefreshMarqueeFromSettings()
        {
            try
            {
                // Fetch marquee message from server
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/marquee");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<MarqueeResponse>(json);
                    var newMessage = result?.message;

                    if (!string.IsNullOrWhiteSpace(newMessage) && newMessage != _currentMarqueeMessage)
                    {
                        App.Logger?.Information("Marquee message updated from server: {Message}", newMessage);
                        App.Settings.Current.MarqueeMessage = newMessage;
                        Dispatcher.Invoke(() => StartMarqueeAnimation());
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh marquee from server: {Error}", ex.Message);
            }
        }

        private class MarqueeResponse
        {
            public string? message { get; set; }
        }

        #endregion

        #region Server-Controlled Update Banner

        private class UpdateBannerResponse
        {
            public bool enabled { get; set; }
            public string? version { get; set; }
            public string? message { get; set; }
            public string? url { get; set; }
        }

        // Store the server-provided update URL for redirect
        private string? _serverUpdateUrl;

        /// <summary>
        /// Check server for forced update banner configuration.
        /// This is a fallback when automatic update detection fails.
        /// </summary>
        private async void CheckServerUpdateBanner()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/update-banner");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<UpdateBannerResponse>(json);

                    if (result?.enabled == true && !string.IsNullOrWhiteSpace(result.version))
                    {
                        // Check if user is on an older version than the one in the banner
                        var currentVersion = Services.UpdateService.GetCurrentVersion();
                        if (Version.TryParse(result.version, out var bannerVersion) && bannerVersion > currentVersion)
                        {
                            App.Logger?.Information("Server update banner enabled: version={Version}, message={Message}",
                                result.version, result.message);

                            // Store the URL if provided
                            _serverUpdateUrl = result.url;

                            // Update the button on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (BtnUpdateAvailable != null)
                                {
                                    BtnUpdateAvailable.Tag = "UrgentUpdate";
                                    BtnUpdateAvailable.Content = $"UPDATE AVAILABLE v{result.version}";
                                    BtnUpdateAvailable.ToolTip = !string.IsNullOrEmpty(result.url)
                                        ? $"Version {result.version} is available - Click to visit download page!"
                                        : $"Version {result.version} is available - Click to update!";
                                }
                            });
                        }
                        else
                        {
                            App.Logger?.Debug("Server update banner: user already on version {Current}, banner is for {Banner}",
                                currentVersion, result.version);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to check server update banner: {Error}", ex.Message);
            }
        }

        #endregion

        #region Marquee Animation

        private void StartMarqueeAnimation()
        {
            try
            {
                // Stop existing animation
                _marqueeStoryboard?.Stop();

                var canvasWidth = MarqueeCanvas.ActualWidth;
                if (canvasWidth <= 0) return;

                // Get the original message
                var message = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }
                message = message.ToUpperInvariant();

                // Track current message for refresh detection
                _currentMarqueeMessage = message;

                // Create single segment with separator (doubled message + spacing)
                var separator = "          "; // 10 spaces between repetitions
                var singleSegment = message + separator + message + separator;

                // Measure single segment width
                var tempBlock = new TextBlock
                {
                    Text = singleSegment,
                    FontFamily = MarqueeText.FontFamily,
                    FontSize = MarqueeText.FontSize,
                    FontWeight = MarqueeText.FontWeight
                };
                tempBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var segmentWidth = tempBlock.DesiredSize.Width;

                if (segmentWidth <= 0) return;

                // Calculate how many segments needed to fill canvas + one extra for seamless loop
                var segmentsNeeded = (int)Math.Ceiling(canvasWidth / segmentWidth) + 2;
                var fullText = string.Concat(Enumerable.Repeat(singleSegment, segmentsNeeded));
                MarqueeText.Text = fullText;

                // Animation: scroll exactly one segment width, then loop back seamlessly
                // From 0 to -segmentWidth creates perfect loop since next segment is identical
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = -segmentWidth,
                    Duration = TimeSpan.FromSeconds(segmentWidth / 80), // Speed: 80 pixels per second
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                _marqueeStoryboard = new System.Windows.Media.Animation.Storyboard();
                _marqueeStoryboard.Children.Add(animation);
                System.Windows.Media.Animation.Storyboard.SetTarget(animation, MarqueeText);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                _marqueeStoryboard.Begin();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to start marquee animation: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Updates the marquee message from server/external source.
        /// Call this method when receiving a new message from the server.
        /// </summary>
        public void UpdateMarqueeMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                var newMessage = message.Trim().ToUpperInvariant();
                if (!newMessage.EndsWith("•") && !newMessage.EndsWith(" "))
                {
                    newMessage += " • ";
                }

                App.Settings.Current.MarqueeMessage = newMessage;
                Dispatcher.Invoke(() =>
                {
                    MarqueeText.Text = newMessage;
                    StartMarqueeAnimation();
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to update marquee message: {Error}", ex.Message);
            }
        }

        #endregion

        private void ShowEasterEgg()
        {
            var easterEggWindow = new EasterEggWindow();
            easterEggWindow.Owner = this;
            easterEggWindow.ShowDialog();
        }

        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if video is already playing - offer force reset if stuck
                if (App.Video.IsPlaying)
                {
                    var result = MessageBox.Show(
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        "Video Playing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck video state");
                        App.Video.ForceCleanup();
                        App.InteractionQueue?.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                // Check if another interaction is blocking - offer force reset if stuck
                if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    var result = MessageBox.Show(
                        $"Another interaction is in progress ({App.InteractionQueue.CurrentInteraction}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        "Please Wait",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck interaction queue");
                        App.Video.ForceCleanup();
                        App.InteractionQueue.ForceReset();
                        // Continue to trigger video below
                    }
                    else
                    {
                        return;
                    }
                }

                App.Video.TriggerVideo();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
                MessageBox.Show($"Error triggering video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TriggerStartupVideo()
        {
            var startupPath = App.Settings.Current.StartupVideoPath;

            // If a specific video is configured, play that one
            if (!string.IsNullOrEmpty(startupPath) && System.IO.File.Exists(startupPath))
            {
                App.Logger?.Information("Playing startup video: {Path}", startupPath);
                App.Video.PlaySpecificVideo(startupPath, App.Settings.Current.StrictLockEnabled);
            }
            else
            {
                // Play a random video
                App.Logger?.Information("Playing random startup video");
                App.Video.TriggerVideo();
            }
        }

        private void BtnSelectStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Startup Video",
                Filter = "Video Files|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm|All Files|*.*",
                InitialDirectory = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos")
            };

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.StartupVideoPath = dialog.FileName;
                TxtStartupVideo.Text = System.IO.Path.GetFileName(dialog.FileName);
                App.Settings.Save();
                App.Logger?.Information("Startup video set to: {Path}", dialog.FileName);
            }
        }

        private void BtnClearStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Current.StartupVideoPath = null;
            TxtStartupVideo.Text = "(Random)";
            App.Settings.Save();
            App.Logger?.Information("Startup video cleared - will use random");
        }

        private void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Attention Targets", App.Settings.Current.AttentionPool);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.AttentionPool = dialog.ResultData;
                App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AttentionTargetEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void BtnSubliminalSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Subliminal Messages", App.Settings.Current.SubliminalPool);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.SubliminalPool = dialog.ResultData;
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnManageLockCardPhrases_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextEditorDialog("Lock Card Phrases", App.Settings.Current.LockCardPhrases);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                App.Settings.Current.LockCardPhrases = dialog.ResultData;
                App.Logger?.Information("Lock card phrases updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnTestLockCard_Click(object sender, RoutedEventArgs e)
        {
            var phrases = App.Settings.Current.LockCardPhrases;
            var enabledPhrases = phrases.Where(p => p.Value).Select(p => p.Key).ToList();
            
            if (enabledPhrases.Count == 0)
            {
                MessageBox.Show("No phrases enabled! Add some phrases first.", "No Phrases", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Show the actual lock card
            App.LockCard.TestLockCard();
        }

        private void BtnLockCardSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LockCardColorDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void ChkLockCardStrict_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Show warning
            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Strict Lock Card",
                "• You will NOT be able to escape lock cards with ESC\n" +
                "• You MUST type the phrase the required number of times\n" +
                "• This can be very restrictive!");

            if (!confirmed)
            {
                ChkLockCardStrict.IsChecked = false;
            }
        }

        private void BtnSelectSpiral_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GIF Files (*.gif)|*.gif|All Image Files|*.gif;*.png;*.jpg;*.jpeg",
                Title = "Select Spiral GIF"
            };
            
            // Start in last used directory if available
            var currentPath = App.Settings.Current.SpiralPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            }

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.SpiralPath = dialog.FileName;
                App.Settings.Save();
                
                // Refresh overlays if running
                if (_isRunning)
                {
                    App.Overlay.RefreshOverlays();
                }
                
                MessageBox.Show($"Selected: {Path.GetFileName(dialog.FileName)}", "Spiral Selected");
            }
        }

        private void BtnPrevImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }

        private void BtnNextImage_Click(object sender, RoutedEventArgs e)
        {
            // Image carousel navigation
        }

        #region Assets & Packs Tab

        private ObservableCollection<AssetTreeItem> _assetTree = new();
        private ObservableCollection<AssetFileItem> _currentFolderFiles = new();
        private AssetTreeItem? _selectedFolder;

        private void BtnAssets_Click(object sender, RoutedEventArgs e) => ShowTab("assets");

        private void BtnOpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            var assetsPath = App.EffectiveAssetsPath;
            Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "videos"));
            Process.Start("explorer.exe", assetsPath);
        }

        private async Task RefreshPacksAsync()
        {
            try
            {
                // Fetch packs from server (with fallback to built-in)
                var packs = await App.ContentPacks?.GetAvailablePacksAsync() ?? new List<ContentPack>();

                // Set static preview images for original packs (always use embedded resources)
                foreach (var pack in packs)
                {
                    if (pack.Id == "basic-bimbo-starter")
                        pack.PreviewImageUrl = "pack://application:,,,/Resources/pack1.png";
                    else if (pack.Id == "enhanced-bimbodoll-video")
                        pack.PreviewImageUrl = "pack://application:,,,/Resources/pack2.png";
                }

                // Update the observable collection
                _availablePacks.Clear();
                foreach (var pack in packs)
                {
                    _availablePacks.Add(pack);
                }

                // Bind to ItemsControl
                PackCardsItemsControl.ItemsSource = _availablePacks;

                // Load preview images for all packs
                var loadTasks = new List<Task>();
                foreach (var pack in packs)
                {
                    if (pack.IsDownloaded)
                    {
                        // Load from local encrypted files for installed packs
                        loadTasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                var previewImages = App.ContentPacks?.GetPackPreviewImages(pack.Id, 10, 240, 100);
                                if (previewImages != null && previewImages.Count > 0)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        pack.PreviewImages = previewImages;
                                        pack.CurrentPreviewIndex = 0;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("Failed to load preview images for {PackId}: {Error}", pack.Id, ex.Message);
                            }
                        }));
                    }
                    else if (pack.PreviewUrls?.Count > 0)
                    {
                        // Load from server URLs for non-installed packs
                        loadTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var previewImages = await LoadPreviewImagesFromUrlsAsync(pack.PreviewUrls);
                                if (previewImages.Count > 0)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        pack.PreviewImages = previewImages;
                                        pack.CurrentPreviewIndex = 0;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("Failed to load preview images from URLs for {PackId}: {Error}", pack.Id, ex.Message);
                            }
                        }));
                    }
                }
                await Task.WhenAll(loadTasks);

                // Start preview rotation timer
                StartPackPreviewRotation();

                // Fetch and update bandwidth usage
                await UpdateBandwidthDisplayAsync();

                // Subscribe to pack events for progress updates
                if (App.ContentPacks != null)
                {
                    App.ContentPacks.PackDownloadProgress -= OnPackDownloadProgress;
                    App.ContentPacks.PackDownloadProgress += OnPackDownloadProgress;
                    App.ContentPacks.PackDownloadCompleted -= OnPackDownloadCompleted;
                    App.ContentPacks.PackDownloadCompleted += OnPackDownloadCompleted;
                    App.ContentPacks.AuthenticationRequired -= OnPackAuthenticationRequired;
                    App.ContentPacks.AuthenticationRequired += OnPackAuthenticationRequired;
                    App.ContentPacks.RateLimitExceeded -= OnPackRateLimitExceeded;
                    App.ContentPacks.RateLimitExceeded += OnPackRateLimitExceeded;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh packs");
            }
        }

        private async Task UpdateBandwidthDisplayAsync()
        {
            try
            {
                App.Logger?.Information("UpdateBandwidthDisplayAsync: Starting update");

                // Show default state if not logged in with Patreon or Discord
                var isPatreonAuth = App.Patreon?.IsAuthenticated == true;
                var isDiscordAuth = App.Discord?.IsAuthenticated == true;

                if (App.ContentPacks == null || (!isPatreonAuth && !isDiscordAuth))
                {
                    App.Logger?.Information("UpdateBandwidthDisplayAsync: Not authenticated, showing default");
                    // Show default bar for non-authenticated users
                    BandwidthProgressBar.Value = 0;
                    TxtBandwidthUsage.Text = "0 / 10 GB";
                    TxtBandwidthLabel.Text = "Bandwidth (Free):";
                    BandwidthProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                    BandwidthPanel.Visibility = Visibility.Visible;
                    return;
                }

                var status = await App.ContentPacks.GetFullPackStatusAsync();
                if (status?.Bandwidth == null)
                {
                    App.Logger?.Information("UpdateBandwidthDisplayAsync: Server returned no bandwidth data");
                    // Server didn't return bandwidth - show default
                    BandwidthProgressBar.Value = 0;
                    var isPremium = App.Patreon?.HasPremiumAccess == true;
                    TxtBandwidthUsage.Text = isPremium ? "0 / 100 GB" : "0 / 10 GB";
                    TxtBandwidthLabel.Text = isPremium ? "Bandwidth (Patreon):" : "Bandwidth (Free):";
                    BandwidthProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                    BandwidthPanel.Visibility = Visibility.Visible;
                    return;
                }

                App.Logger?.Information("UpdateBandwidthDisplayAsync: Got bandwidth - UsedBytes={Used}, LimitBytes={Limit}, UsedGB={UsedGB}",
                    status.Bandwidth.UsedBytes, status.Bandwidth.LimitBytes, status.Bandwidth.UsedGB);

                var bandwidth = status.Bandwidth;
                var usedGB = double.TryParse(bandwidth.UsedGB, out var used) ? used : 0;
                var limitGB = bandwidth.LimitGB;
                var percentage = limitGB > 0 ? (usedGB / limitGB) * 100 : 0;

                BandwidthProgressBar.Value = Math.Min(100, percentage);
                TxtBandwidthUsage.Text = $"{usedGB:F1} / {limitGB:F0} GB";

                // Change color based on usage
                if (percentage >= 90)
                    BandwidthProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)); // Red
                else if (percentage >= 70)
                    BandwidthProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)); // Orange
                else
                    BandwidthProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)); // Pink

                // Update label to show if Patreon or free
                TxtBandwidthLabel.Text = bandwidth.IsPatreon ? "Bandwidth (Patreon):" : "Bandwidth (Free):";

                BandwidthPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to update bandwidth display: {Error}", ex.Message);
                // Show default on error
                BandwidthProgressBar.Value = 0;
                TxtBandwidthUsage.Text = "0 / 10 GB";
                TxtBandwidthLabel.Text = "Bandwidth:";
                BandwidthPanel.Visibility = Visibility.Visible;
            }
        }

        private void StartPackPreviewRotation()
        {
            // Stop existing timer if running
            _packPreviewTimer?.Stop();

            // Create timer to rotate preview images every 1 second
            _packPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _packPreviewTimer.Tick += (s, e) =>
            {
                try
                {
                    foreach (var pack in _availablePacks.Where(p => p.HasPreviewImages))
                    {
                        pack.AdvancePreviewImage();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Error rotating pack previews: {Error}", ex.Message);
                }
            };
            _packPreviewTimer.Start();
        }

        private void StopPackPreviewRotation()
        {
            _packPreviewTimer?.Stop();
            _packPreviewTimer = null;
        }

        private async Task<List<BitmapImage>> LoadPreviewImagesFromUrlsAsync(List<string> urls)
        {
            var images = new List<BitmapImage>();
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            foreach (var url in urls)
            {
                try
                {
                    var bytes = await httpClient.GetByteArrayAsync(url);
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(bytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze(); // Required for cross-thread access
                    images.Add(bitmap);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to load preview image from {Url}: {Error}", url, ex.Message);
                }
            }

            return images;
        }

        private void OnPackDownloadProgress(object? sender, (ContentPack Pack, int Progress) e)
        {
            // Progress is bound via INotifyPropertyChanged, no manual UI update needed
            // Just update the pack's download progress property
            Dispatcher.Invoke(() =>
            {
                e.Pack.DownloadProgress = e.Progress;
            });
        }

        private void OnPackDownloadCompleted(object? sender, ContentPack pack)
        {
            Dispatcher.Invoke(async () =>
            {
                // Properties are bound, just ensure state is correct
                pack.IsDownloaded = true;
                pack.IsDownloading = false;

                // Load preview images for the newly installed pack
                try
                {
                    var previewImages = await Task.Run(() =>
                        App.ContentPacks?.GetPackPreviewImages(pack.Id, 10, 240, 100));
                    if (previewImages != null && previewImages.Count > 0)
                    {
                        pack.PreviewImages = previewImages;
                        pack.CurrentPreviewIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to load preview images after install: {Error}", ex.Message);
                }

                // Update bandwidth display after download
                await UpdateBandwidthDisplayAsync();

                RefreshAssetTree();
            });
        }

        private void OnPackAuthenticationRequired(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Show login prompt
                var result = MessageBox.Show(
                    $"{message}\n\nWould you like to log in with Patreon now?",
                    "Login Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // Trigger Patreon login
                    _ = App.Patreon?.StartOAuthFlowAsync();
                }
            });
        }

        private void OnPackRateLimitExceeded(object? sender, (ContentPack Pack, string Message, DateTime ResetTime) e)
        {
            Dispatcher.Invoke(() =>
            {
                // Reset pack state (bound via INotifyPropertyChanged)
                e.Pack.IsDownloading = false;

                // Calculate time until reset
                var timeUntilReset = e.ResetTime - DateTime.UtcNow;
                var hoursText = timeUntilReset.TotalHours > 1
                    ? $"{(int)timeUntilReset.TotalHours} hours"
                    : $"{(int)timeUntilReset.TotalMinutes} minutes";

                MessageBox.Show(
                    $"{e.Message}\n\nYou can download again in approximately {hoursText}.",
                    "Download Limit Reached",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        private void BtnCreatorDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://discord.gg/YxVAMt4qaZ") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Discord link");
            }
        }

        private void PacksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Enable horizontal scrolling with mouse wheel
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void BtnRefreshPacks_Click(object sender, RoutedEventArgs e) => _ = RefreshPacksAsync();

        private void RefreshAssetTree()
        {
            _assetTree.Clear();
            var assetsPath = App.EffectiveAssetsPath;

            // Build tree for images folder
            var imagesFolder = Path.Combine(assetsPath, "images");
            if (Directory.Exists(imagesFolder))
            {
                var imagesNode = BuildFolderTree(imagesFolder, "images");
                imagesNode.IsExpanded = true;
                _assetTree.Add(imagesNode);
            }

            // Build tree for videos folder
            var videosFolder = Path.Combine(assetsPath, "videos");
            if (Directory.Exists(videosFolder))
            {
                var videosNode = BuildFolderTree(videosFolder, "videos");
                videosNode.IsExpanded = true;
                _assetTree.Add(videosNode);
            }

            // Add content pack virtual folders for active packs
            var activePackIds = App.ContentPacks?.GetActivePackIds() ?? new List<string>();
            if (activePackIds.Count > 0)
            {
                var packsNode = new AssetTreeItem
                {
                    Name = "📦 Content Packs",
                    FullPath = "",
                    IsChecked = true,
                    IsPackFolder = true,
                    IsExpanded = true
                };

                foreach (var packId in activePackIds)
                {
                    var packNode = BuildPackTree(packId);
                    if (packNode != null)
                    {
                        packNode.Parent = packsNode;
                        packsNode.Children.Add(packNode);
                    }
                }

                if (packsNode.Children.Count > 0)
                {
                    packsNode.FileCount = packsNode.Children.Sum(c => c.FileCount);
                    _assetTree.Add(packsNode);
                }
            }

            AssetTreeView.ItemsSource = _assetTree;
            UpdateAssetCounts();
        }

        private AssetTreeItem? BuildPackTree(string packId)
        {
            var packFiles = App.ContentPacks?.GetPackFiles(packId);
            if (packFiles == null || packFiles.Count == 0)
                return null;

            // Get pack name from built-in packs
            var packs = App.ContentPacks?.GetBuiltInPacks();
            var packInfo = packs?.FirstOrDefault(p => p.Id == packId);
            var packName = packInfo?.Name ?? packId;

            var packNode = new AssetTreeItem
            {
                Name = packName,
                FullPath = "",
                IsPackFolder = true,
                PackId = packId,
                IsChecked = true,
                IsExpanded = false
            };

            // Images subfolder
            var imageFiles = packFiles.Where(f => f.FileType == "image").ToList();
            if (imageFiles.Count > 0)
            {
                var imagesNode = new AssetTreeItem
                {
                    Name = "images",
                    FullPath = "",
                    IsPackFolder = true,
                    PackId = packId,
                    PackFileType = "image",
                    IsChecked = true,
                    FileCount = imageFiles.Count,
                    CheckedFileCount = imageFiles.Count,
                    Parent = packNode
                };
                packNode.Children.Add(imagesNode);
            }

            // Videos subfolder
            var videoFiles = packFiles.Where(f => f.FileType == "video").ToList();
            if (videoFiles.Count > 0)
            {
                var videosNode = new AssetTreeItem
                {
                    Name = "videos",
                    FullPath = "",
                    IsPackFolder = true,
                    PackId = packId,
                    PackFileType = "video",
                    IsChecked = true,
                    FileCount = videoFiles.Count,
                    CheckedFileCount = videoFiles.Count,
                    Parent = packNode
                };
                packNode.Children.Add(videosNode);
            }

            packNode.FileCount = packFiles.Count;
            return packNode;
        }

        private AssetTreeItem BuildFolderTree(string path, string name)
        {
            var node = new AssetTreeItem
            {
                Name = name,
                FullPath = path,
                IsChecked = true // Will be recalculated based on DisabledAssetPaths
            };

            // Count files in this folder
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            var files = Directory.GetFiles(path)
                .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            node.FileCount = files.Count;

            // Count checked files using blacklist (files NOT in DisabledAssetPaths are active)
            var basePath = App.EffectiveAssetsPath;
            node.CheckedFileCount = files.Count(f =>
            {
                var relativePath = Path.GetRelativePath(basePath, f);
                return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
            });

            // Add subfolders
            foreach (var dir in Directory.GetDirectories(path))
            {
                var child = BuildFolderTree(dir, Path.GetFileName(dir));
                child.Parent = node;
                node.Children.Add(child);
            }

            // Update check state based on children and files
            node.UpdateCheckState();

            return node;
        }

        private void AssetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is AssetTreeItem folder)
            {
                _selectedFolder = folder;

                // Handle pack virtual folders differently
                if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
                {
                    LoadPackFolderThumbnails(folder.PackId, folder.PackFileType);
                }
                else if (!string.IsNullOrEmpty(folder.FullPath))
                {
                    LoadFolderThumbnails(folder.FullPath);
                    // Recalculate folder's checked state from actual data
                    RecalculateFolderCheckState(folder);
                }
                else
                {
                    // Parent pack folder or root - show empty
                    _currentFolderFiles.Clear();
                    TxtThumbnailsEmpty.Text = "Select a subfolder to view files";
                    TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                    ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                }
            }
        }

        /// <summary>
        /// Recalculate a folder's CheckedFileCount and IsChecked from DisabledAssetPaths
        /// </summary>
        private void RecalculateFolderCheckState(AssetTreeItem folder)
        {
            var basePath = App.EffectiveAssetsPath;
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            // Handle pack virtual folders
            if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
            {
                var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                if (packFiles != null)
                {
                    folder.FileCount = packFiles.Count();
                    folder.CheckedFileCount = packFiles.Count(f =>
                    {
                        var packPath = $"pack:{folder.PackId}/{f.OriginalName}";
                        return !App.Settings.Current.DisabledAssetPaths.Contains(packPath);
                    });
                }
            }
            // Handle local folders
            else if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
            {
                var files = Directory.GetFiles(folder.FullPath)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                folder.CheckedFileCount = files.Count(f =>
                {
                    var relativePath = Path.GetRelativePath(basePath, f);
                    return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
                });
            }

            // Recalculate children too
            foreach (var child in folder.Children)
            {
                RecalculateFolderCheckState(child);
            }

            // Update visual state
            folder.UpdateCheckState();
        }

        /// <summary>
        /// Recalculate all folder check states in the tree from DisabledAssetPaths
        /// </summary>
        private void RecalculateAllFolderCheckStates()
        {
            foreach (var root in _assetTree)
            {
                RecalculateFolderCheckState(root);
            }
        }

        private void LoadPackFolderThumbnails(string packId, string fileType)
        {
            _currentFolderFiles.Clear();
            TxtThumbnailsEmpty.Visibility = Visibility.Collapsed;

            var packFiles = App.ContentPacks?.GetPackFiles(packId, fileType);
            if (packFiles == null || packFiles.Count == 0)
            {
                TxtThumbnailsEmpty.Text = "No files in this pack folder";
                TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                return;
            }

            foreach (var file in packFiles.OrderBy(f => f.OriginalName))
            {
                var packPath = $"pack:{packId}/{file.OriginalName}";
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(packPath);

                var item = new AssetFileItem
                {
                    RelativePath = packPath,
                    IsChecked = isActive,
                    IsPackFile = true,
                    PackId = packId,
                    PackFileEntry = file
                };

                // Set properties manually for pack files (don't use FullPath setter)
                item.Name = file.OriginalName;
                item.Extension = file.Extension;
                item.IsVideo = file.FileType == "video";

                _currentFolderFiles.Add(item);

                // Load thumbnail from encrypted pack
                _ = LoadPackThumbnailAsync(item, packId, file);
            }

            ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
        }

        // Thumbnail cache for pack files (keyed by packId + obfuscatedName)
        private static readonly Dictionary<string, ImageSource> _packThumbnailCache = new();
        private static readonly SemaphoreSlim _thumbnailSemaphore = new(4); // Limit concurrent loads

        private async Task LoadPackThumbnailAsync(AssetFileItem item, string packId, Services.PackFileEntry file)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                // Check cache first
                var cacheKey = $"{packId}:{file.ObfuscatedName}";
                if (_packThumbnailCache.TryGetValue(cacheKey, out var cached))
                {
                    Dispatcher.Invoke(() => item.Thumbnail = cached);
                    return;
                }

                // Limit concurrent thumbnail loads (videos are slow, so limit helps)
                await _thumbnailSemaphore.WaitAsync();
                try
                {
                    // Double-check cache after acquiring semaphore
                    if (_packThumbnailCache.TryGetValue(cacheKey, out cached))
                    {
                        Dispatcher.Invoke(() => item.Thumbnail = cached);
                        return;
                    }

                    await Task.Run(() =>
                    {
                        try
                        {
                            ImageSource? thumbnail = null;

                            if (file.FileType == "image")
                            {
                                // For images, get decrypted thumbnail directly
                                thumbnail = App.ContentPacks?.GetPackFileThumbnail(packId, file, 100, 100);
                            }
                            else if (file.FileType == "video")
                            {
                                // For videos, decrypt to temp file and get shell thumbnail
                                var tempPath = App.ContentPacks?.GetPackFileTempPath(packId, file);
                                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                                {
                                    thumbnail = Helpers.ShellThumbnailHelper.GetThumbnail(tempPath, 100, 100);
                                    // Clean up temp file
                                    try { File.Delete(tempPath); } catch { }
                                }
                            }

                            if (thumbnail != null)
                            {
                                _packThumbnailCache[cacheKey] = thumbnail;
                                Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Failed to load pack thumbnail: {Error}", ex.Message);
                        }
                    });
                }
                finally
                {
                    _thumbnailSemaphore.Release();
                }
            }
            finally
            {
                Dispatcher.Invoke(() => item.IsLoadingThumbnail = false);
            }
        }

        private void LoadFolderThumbnails(string folderPath)
        {
            _currentFolderFiles.Clear();
            TxtThumbnailsEmpty.Visibility = Visibility.Collapsed;

            if (!Directory.Exists(folderPath))
            {
                TxtThumbnailsEmpty.Text = "Folder does not exist";
                TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                return;
            }

            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            var files = Directory.GetFiles(folderPath)
                .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (files.Count == 0)
            {
                TxtThumbnailsEmpty.Text = "No media files in this folder";
                TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                return;
            }

            var basePath = App.EffectiveAssetsPath;

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(basePath, file);
                // Item is checked if NOT in DisabledAssetPaths (blacklist approach)
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);

                var item = new AssetFileItem
                {
                    FullPath = file,
                    RelativePath = relativePath,
                    IsChecked = isActive
                };

                // Get file size
                try { item.SizeBytes = new FileInfo(file).Length; } catch { }

                _currentFolderFiles.Add(item);

                // Load thumbnail asynchronously
                _ = LoadThumbnailAsync(item);
            }

            ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
        }

        private async Task LoadThumbnailAsync(AssetFileItem item)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // Use Windows Shell API for thumbnails - works for both images and videos
                        // This gives us the same thumbnails Windows Explorer shows
                        var thumbnail = Helpers.ShellThumbnailHelper.GetThumbnail(item.FullPath, 100, 100);

                        if (thumbnail != null)
                        {
                            Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
                        }
                        else if (!item.IsVideo)
                        {
                            // Fallback for images: load directly
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(item.FullPath, UriKind.Absolute);
                            bitmap.DecodePixelWidth = 100;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            Dispatcher.Invoke(() => item.Thumbnail = bitmap);
                        }
                    }
                    catch
                    {
                        // Ignore thumbnail load errors
                    }
                });
            }
            finally
            {
                item.IsLoadingThumbnail = false;
            }
        }

        private bool _isUpdatingFolderCheckState = false;

        private void FolderCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Prevent recursive triggering when programmatically updating parent states
            if (_isUpdatingFolderCheckState) return;

            if (sender is CheckBox cb && cb.DataContext is AssetTreeItem folder)
            {
                _isUpdatingFolderCheckState = true;
                try
                {
                    // Get the target state from the checkbox
                    bool targetState = folder.IsChecked;

                    // FIRST: Visually update this folder and ALL subfolders immediately
                    // This gives instant feedback to the user
                    SetFolderAndChildrenChecked(folder, targetState);

                    // SECOND: Update the source of truth (DisabledAssetPaths)
                    UpdateFolderFilesCheckState(folder, targetState);

                    // THIRD: Update parent folder states (they may become partially checked)
                    folder.Parent?.UpdateCheckStateFromChildren();

                    UpdateAssetCounts();

                    // Sync thumbnail checkboxes with current DisabledAssetPaths state
                    RefreshThumbnailCheckboxes();
                }
                finally
                {
                    _isUpdatingFolderCheckState = false;
                }
            }
        }

        /// <summary>
        /// Set IsChecked and CheckedFileCount for a folder and all its children recursively.
        /// This provides immediate visual feedback when user clicks a folder checkbox.
        /// </summary>
        private void SetFolderAndChildrenChecked(AssetTreeItem folder, bool isChecked)
        {
            folder.IsChecked = isChecked;
            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;

            foreach (var child in folder.Children)
            {
                SetFolderAndChildrenChecked(child, isChecked);
            }
        }

        /// <summary>
        /// Refresh the IsChecked state of thumbnail items based on DisabledAssetPaths
        /// </summary>
        private void RefreshThumbnailCheckboxes()
        {
            foreach (var item in _currentFolderFiles)
            {
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(item.RelativePath);
                if (item.IsChecked != isActive)
                {
                    item.IsChecked = isActive;
                }
            }
        }

        private void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
        {
            var basePath = App.EffectiveAssetsPath;
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            // Handle pack virtual folders
            if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
            {
                var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                if (packFiles != null)
                {
                    foreach (var packFile in packFiles)
                    {
                        // Pack file paths use format: pack:{packId}/{filename}
                        var packPath = $"pack:{folder.PackId}/{packFile.OriginalName}";
                        if (isChecked)
                        {
                            App.Settings.Current.DisabledAssetPaths.Remove(packPath);
                        }
                        else
                        {
                            App.Settings.Current.DisabledAssetPaths.Add(packPath);
                        }
                    }
                }
            }
            // Handle local folders
            else if (Directory.Exists(folder.FullPath))
            {
                var files = Directory.GetFiles(folder.FullPath)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(basePath, file);
                    // Use DisabledAssetPaths (blacklist): unchecked items are in the set
                    if (isChecked)
                    {
                        App.Settings.Current.DisabledAssetPaths.Remove(relativePath);
                    }
                    else
                    {
                        App.Settings.Current.DisabledAssetPaths.Add(relativePath);
                    }
                }
            }

            // Recurse into subfolders
            foreach (var child in folder.Children)
            {
                UpdateFolderFilesCheckState(child, isChecked);
            }

            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        }

        private void ThumbnailCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AssetFileItem file)
            {
                // Use DisabledAssetPaths (blacklist): unchecked items are in the set
                if (file.IsChecked)
                {
                    App.Settings.Current.DisabledAssetPaths.Remove(file.RelativePath);
                }
                else
                {
                    App.Settings.Current.DisabledAssetPaths.Add(file.RelativePath);
                }

                // Update parent folder state
                UpdateParentFolderCheckState();
                UpdateAssetCounts();
            }
        }

        private void ThumbnailItem_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle selection on click
            if (sender is Border border && border.DataContext is AssetFileItem file)
            {
                file.IsChecked = !file.IsChecked;
                ThumbnailCheckBox_Changed(sender, e);
            }
        }

        private void UpdateParentFolderCheckState()
        {
            if (_selectedFolder == null) return;

            // Count checked files
            var checkedCount = _currentFolderFiles.Count(f => f.IsChecked);
            _selectedFolder.CheckedFileCount = checkedCount;
            _selectedFolder.UpdateCheckState();
        }

        private void BtnSelectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            // Update DisabledAssetPaths (clear all = everything enabled)
            foreach (var folder in _assetTree)
            {
                UpdateFolderFilesCheckState(folder, true);
            }

            // Recalculate all folder check states from DisabledAssetPaths
            RecalculateAllFolderCheckStates();

            // Sync thumbnail checkboxes
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
        }

        private void BtnDeselectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            // Update DisabledAssetPaths (add all = everything disabled)
            foreach (var folder in _assetTree)
            {
                UpdateFolderFilesCheckState(folder, false);
            }

            // Recalculate all folder check states from DisabledAssetPaths
            RecalculateAllFolderCheckStates();

            // Sync thumbnail checkboxes
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
        }

        private void BtnSaveAssetSelection_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Save();

            // Clear caches so services pick up new selection
            App.Flash?.ClearFileCache();
            App.Video?.RefreshVideosPath();

            var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
            var message = disabledCount > 0
                ? $"Selection saved!\n\n{disabledCount} assets are disabled.\n\nThe changes will take effect on the next flash/video."
                : "Selection saved!\n\nAll assets are active.\n\nThe changes will take effect on the next flash/video.";
            MessageBox.Show(
                message,
                "Selection Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #region Asset Presets

        private bool _isLoadingPreset = false;

        private void InitializeAssetPresets()
        {
            // Ensure default preset exists
            if (!App.Settings.Current.AssetPresets.Any(p => p.IsDefault))
            {
                App.Settings.Current.AssetPresets.Insert(0, Models.AssetPreset.CreateDefault());
            }

            // Update default preset counts (it should show all assets including packs)
            var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
            if (defaultPreset != null)
            {
                // Use the same counting logic as asset counts display
                var totalImages = 0;
                var totalVideos = 0;
                var activeImages = 0;
                var activeVideos = 0;
                CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
                defaultPreset.EnabledImageCount = totalImages;
                defaultPreset.EnabledVideoCount = totalVideos;
            }

            // Refresh the ComboBox
            RefreshAssetPresetsComboBox();

            // Update existing preset counts to match current file counts
            // (in case files were added/removed since preset was saved)
            UpdatePresetCountsFromCurrentState();
        }

        /// <summary>
        /// Recalculates the enabled counts for all non-default presets based on current files.
        /// This ensures preset displays are accurate even if files were added/removed.
        /// </summary>
        private void UpdatePresetCountsFromCurrentState()
        {
            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            var basePath = App.EffectiveAssetsPath;

            foreach (var preset in App.Settings.Current.AssetPresets.Where(p => !p.IsDefault))
            {
                var enabledImages = 0;
                var enabledVideos = 0;

                // Count files in images folder that are NOT in this preset's disabled list
                var imagesPath = Path.Combine(basePath, "images");
                if (Directory.Exists(imagesPath))
                {
                    CountEnabledFilesRecursive(imagesPath, basePath, preset.DisabledAssetPaths, imageExts, ref enabledImages);
                }

                // Count files in videos folder that are NOT in this preset's disabled list
                var videosPath = Path.Combine(basePath, "videos");
                if (Directory.Exists(videosPath))
                {
                    CountEnabledFilesRecursive(videosPath, basePath, preset.DisabledAssetPaths, videoExts, ref enabledVideos);
                }

                // Add pack files (check if disabled in preset)
                var activePackIds = App.ContentPacks?.GetActivePackIds() ?? new List<string>();
                foreach (var packId in activePackIds)
                {
                    var packImages = App.ContentPacks?.GetPackFiles(packId, "image");
                    var packVideos = App.ContentPacks?.GetPackFiles(packId, "video");

                    if (packImages != null)
                    {
                        foreach (var packFile in packImages)
                        {
                            var packPath = $"pack:{packId}/{packFile.OriginalName}";
                            if (preset.DisabledAssetPaths == null || !preset.DisabledAssetPaths.Contains(packPath))
                            {
                                enabledImages++;
                            }
                        }
                    }

                    if (packVideos != null)
                    {
                        foreach (var packFile in packVideos)
                        {
                            var packPath = $"pack:{packId}/{packFile.OriginalName}";
                            if (preset.DisabledAssetPaths == null || !preset.DisabledAssetPaths.Contains(packPath))
                            {
                                enabledVideos++;
                            }
                        }
                    }
                }

                // Update preset if counts changed
                if (preset.EnabledImageCount != enabledImages || preset.EnabledVideoCount != enabledVideos)
                {
                    preset.EnabledImageCount = enabledImages;
                    preset.EnabledVideoCount = enabledVideos;
                }
            }
        }

        private void CountEnabledFilesRecursive(string path, string basePath, HashSet<string>? disabledPaths, string[] validExts, ref int count)
        {
            if (!Directory.Exists(path)) return;

            var files = Directory.GetFiles(path);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!validExts.Contains(ext)) continue;

                var relativePath = Path.GetRelativePath(basePath, file);
                if (disabledPaths == null || !disabledPaths.Contains(relativePath))
                {
                    count++;
                }
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                CountEnabledFilesRecursive(dir, basePath, disabledPaths, validExts, ref count);
            }
        }

        private void RefreshAssetPresetsComboBox()
        {
            _isLoadingPreset = true;
            CmbAssetPresets.ItemsSource = null;
            CmbAssetPresets.ItemsSource = App.Settings.Current.AssetPresets;

            // Select current preset if set
            if (!string.IsNullOrEmpty(App.Settings.Current.CurrentAssetPresetId))
            {
                CmbAssetPresets.SelectedValue = App.Settings.Current.CurrentAssetPresetId;
            }
            else
            {
                // Default to "All Assets" preset
                var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
                if (defaultPreset != null)
                {
                    CmbAssetPresets.SelectedValue = defaultPreset.Id;
                }
            }
            _isLoadingPreset = false;
        }

        private void CmbAssetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset) return;
            if (CmbAssetPresets.SelectedItem is not Models.AssetPreset preset) return;

            // Apply preset's disabled paths
            var presetDisabledCount = preset.DisabledAssetPaths?.Count ?? 0;
            preset.ApplyToSettings();
            App.Settings.Current.CurrentAssetPresetId = preset.Id;

            // Refresh tree to show new state
            RefreshAssetTree();
            UpdateAssetCounts();

            // Sync thumbnail checkboxes with new preset state
            RefreshThumbnailCheckboxes();

            // Clear caches so services pick up new selection
            App.Flash?.ClearFileCache();
            App.Video?.RefreshVideosPath();

            // Get actual counts after applying
            var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
            App.Logger?.Information("Loaded asset preset: {Name} - Preset had {PresetDisabled} disabled paths, now {ActiveImages} images and {ActiveVideos} videos active",
                preset.Name, presetDisabledCount, activeImages, activeVideos);
        }

        private void BtnSaveAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            // Get current counts
            var (imageCount, videoCount) = GetCurrentActiveAssetCounts();

            // Simple input dialog using WPF
            var dialog = new System.Windows.Window
            {
                Title = "Save Asset Preset",
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A2E")),
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Enter a name for this preset:",
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = $"Preset {App.Settings.Current.AssetPresets.Count}",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252542")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 15)
            };
            textBox.SelectAll();
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Save",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404060")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };

            btnOk.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
            btnCancel.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dialog.Content = grid;
            textBox.Focus();

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
                var preset = Models.AssetPreset.FromCurrentSettings(textBox.Text.Trim(), imageCount, videoCount);
                App.Settings.Current.AssetPresets.Add(preset);
                App.Settings.Current.CurrentAssetPresetId = preset.Id;
                App.Settings.Save();

                RefreshAssetPresetsComboBox();
                CmbAssetPresets.SelectedValue = preset.Id;

                App.Logger?.Information("Saved asset preset: {Name} with {Images} images, {Videos} videos, {Disabled} disabled paths",
                    preset.Name, imageCount, videoCount, disabledCount);

                MessageBox.Show(
                    $"Preset '{preset.Name}' saved!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.",
                    "Preset Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void BtnUpdateAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAssetPresets.SelectedItem is not Models.AssetPreset preset)
            {
                MessageBox.Show("Please select a preset to update.", "No Preset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (preset.IsDefault)
            {
                MessageBox.Show("Cannot update the default 'All Assets' preset.\nUse 'Save As' to create a new preset.", "Cannot Update Default", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Update preset '{preset.Name}' with the current selection?",
                "Update Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var (imageCount, videoCount) = GetCurrentActiveAssetCounts();
                var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
                preset.UpdateFromCurrentSettings(imageCount, videoCount);
                App.Settings.Save();

                // Refresh display
                RefreshAssetPresetsComboBox();
                CmbAssetPresets.SelectedValue = preset.Id;

                App.Logger?.Information("Updated asset preset: {Name} with {Images} images, {Videos} videos, {Disabled} disabled paths",
                    preset.Name, imageCount, videoCount, disabledCount);

                MessageBox.Show(
                    $"Preset '{preset.Name}' updated!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.",
                    "Preset Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void BtnDeleteAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAssetPresets.SelectedItem is not Models.AssetPreset preset)
            {
                MessageBox.Show("Please select a preset to delete.", "No Preset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (preset.IsDefault)
            {
                MessageBox.Show("Cannot delete the default 'All Assets' preset.", "Cannot Delete Default", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Delete preset '{preset.Name}'?\n\nThis cannot be undone.",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.Settings.Current.AssetPresets.Remove(preset);

                // Select default preset
                var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
                if (defaultPreset != null)
                {
                    App.Settings.Current.CurrentAssetPresetId = defaultPreset.Id;
                }
                else
                {
                    App.Settings.Current.CurrentAssetPresetId = null;
                }

                App.Settings.Save();
                RefreshAssetPresetsComboBox();

                App.Logger?.Information("Deleted asset preset: {Name}", preset.Name);
            }
        }

        private (int imageCount, int videoCount) GetCurrentActiveAssetCounts()
        {
            var totalImages = 0;
            var totalVideos = 0;
            var activeImages = 0;
            var activeVideos = 0;
            CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
            return (activeImages, activeVideos);
        }

        #endregion

        private void UpdateAssetCounts()
        {
            var totalImages = 0;
            var totalVideos = 0;
            var activeImages = 0;
            var activeVideos = 0;

            CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);

            // Always show active counts (blacklist system: files NOT in DisabledAssetPaths are active)
            TxtAssetCounts.Text = $"{activeImages} images, {activeVideos} videos active";
        }

        private void CountAssetsRecursive(IEnumerable<AssetTreeItem> items, ref int totalImages, ref int totalVideos, ref int activeImages, ref int activeVideos)
        {
            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            foreach (var folder in items)
            {
                // Handle pack virtual folders
                if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
                {
                    var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                    if (packFiles != null)
                    {
                        foreach (var packFile in packFiles)
                        {
                            // Pack file paths use format: pack:{packId}/{filename}
                            var packPath = $"pack:{folder.PackId}/{packFile.OriginalName}";
                            var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(packPath);

                            if (folder.PackFileType == "image")
                            {
                                totalImages++;
                                if (isActive) activeImages++;
                            }
                            else if (folder.PackFileType == "video")
                            {
                                totalVideos++;
                                if (isActive) activeVideos++;
                            }
                        }
                    }
                }
                // Handle local folders
                else if (Directory.Exists(folder.FullPath))
                {
                    var files = Directory.GetFiles(folder.FullPath);
                    var basePath = App.EffectiveAssetsPath;

                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        var isImage = imageExts.Contains(ext);
                        var isVideo = videoExts.Contains(ext);

                        if (isImage) totalImages++;
                        if (isVideo) totalVideos++;

                        // Use blacklist: files NOT in DisabledAssetPaths are active
                        var relativePath = Path.GetRelativePath(basePath, file);
                        var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);

                        if (isActive && isImage) activeImages++;
                        if (isActive && isVideo) activeVideos++;
                    }
                }

                CountAssetsRecursive(folder.Children, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
            }
        }

        private async void BtnPackDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack)
            {
                if (pack.IsDownloaded)
                {
                    // Ask for confirmation to uninstall
                    var sizeStr = pack.SizeBytes > 0 ? $"{pack.SizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB" : "";
                    var result = MessageBox.Show(
                        $"Uninstall '{pack.Name}'?\n\nThis will delete {sizeStr} of downloaded content from your computer.\n\nYou can reinstall it later if needed.",
                        "Uninstall Content Pack",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        // Uninstall pack (deactivate + delete files)
                        App.ContentPacks?.UninstallPack(pack.Id);
                        pack.IsDownloaded = false;
                        pack.IsActive = false;
                        pack.PreviewImages.Clear(); // Clear preview images

                        // UI updates automatically via data binding

                        RefreshAssetTree();
                        MessageBox.Show($"'{pack.Name}' has been uninstalled.", "Uninstalled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to uninstall pack: {Name}", pack.Name);
                        MessageBox.Show($"Failed to uninstall pack: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // Show confirmation dialog before download
                    var sizeStr = pack.SizeBytes > 0 ? $" ({pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
                    var result = MessageBox.Show(
                        $"Download and install '{pack.Name}'?{sizeStr}\n\nThis will download encrypted content to a secure folder on your computer.",
                        "Install Content Pack",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    // Download and install - UI updates automatically via data binding
                    pack.IsDownloading = true;

                    try
                    {
                        var progress = new Progress<int>(p => pack.DownloadProgress = p);
                        await App.ContentPacks!.InstallPackAsync(pack, progress);
                        App.ContentPacks.ActivatePack(pack.Id);
                        pack.IsActive = true;

                        // UI updates automatically via data binding
                        // Preview images are loaded by OnPackDownloadCompleted event handler

                        RefreshAssetTree();
                        MessageBox.Show($"'{pack.Name}' installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Auth error - already handled by OnPackAuthenticationRequired event
                        App.Logger?.Debug("Pack install cancelled - authentication required");
                    }
                    catch (Services.PackRateLimitException)
                    {
                        // Rate limit error - already handled by OnPackRateLimitExceeded event
                        App.Logger?.Debug("Pack install cancelled - rate limit exceeded");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to install pack: {Name}", pack.Name);
                        MessageBox.Show($"Installation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        // UI updates automatically via data binding
                        pack.IsDownloading = false;
                        pack.DownloadProgress = 0;
                    }
                }
            }
        }

        private void BtnPackUpgrade_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack && !string.IsNullOrEmpty(pack.UpgradeUrl))
            {
                Process.Start(new ProcessStartInfo(pack.UpgradeUrl) { UseShellExecute = true });
            }
        }

        private void BtnPackPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack && !string.IsNullOrEmpty(pack.PatreonUrl))
            {
                Process.Start(new ProcessStartInfo(pack.PatreonUrl) { UseShellExecute = true });
            }
        }

        #endregion

        private void BtnPickAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder for your custom assets (images and videos).\nTwo subfolders 'images' and 'videos' will be created.",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            // Start from current custom path if set, otherwise default
            var currentPath = App.Settings?.Current?.CustomAssetsPath;
            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.SelectedPath = currentPath;
            }
            else
            {
                dialog.SelectedPath = App.UserAssetsPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = dialog.SelectedPath;

                // Create subfolders
                Directory.CreateDirectory(Path.Combine(selectedPath, "images"));
                Directory.CreateDirectory(Path.Combine(selectedPath, "videos"));

                // Save to settings
                App.Settings.Current.CustomAssetsPath = selectedPath;
                App.Settings.Save();

                // Refresh services to use new path
                App.Flash?.RefreshImagesPath();
                App.Video?.RefreshVideosPath();
                App.BubbleCount?.RefreshVideosPath();

                MessageBox.Show(
                    $"Custom assets folder set to:\n{selectedPath}\n\nSubfolders 'images' and 'videos' have been created.",
                    "Assets Folder Set",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                App.Logger?.Information("Custom assets path set to: {Path}", selectedPath);
            }
        }

        private void BtnRefreshAssets_Click(object sender, RoutedEventArgs e)
        {
            App.Flash.LoadAssets();
            MessageBox.Show("Assets refreshed!", "Success");
        }

        private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
            }
            else
            {
                MessageBox.Show("No logs found.", "Info");
            }
        }

        private void BtnPanicKey_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingPanicKey = true;
            UpdatePanicKeyButton();
            MessageBox.Show("Press any key to set as the new panic key...", "Change Panic Key", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ChkStrictLock_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Show double warning
            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Strict Lock",
                "• You will NOT be able to skip or close videos\n" +
                "• Videos MUST be watched to completion\n" +
                "• The only way out is the panic key (if enabled)\n" +
                "• This can be very intense and restrictive");

            if (!confirmed)
            {
                ChkStrictLock.IsChecked = false;
            }
        }

        private void ChkNoPanic_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Show double warning
            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Disable Panic Key",
                "• You will have NO emergency escape option\n" +
                "• The ONLY way to exit will be the Exit button\n" +
                "• Combined with Strict Lock, this is VERY restrictive\n" +
                "• Make sure you know what you're doing!");

            if (!confirmed)
            {
                ChkNoPanic.IsChecked = false;
            }
            else
            {
                // Stop keyboard hook when panic key is disabled (privacy improvement)
                _keyboardHook?.Stop();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }
        }

        private void ChkNoPanic_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Start keyboard hook when panic key is re-enabled
            _keyboardHook?.Start();
            App.Logger?.Information("Keyboard hook started - panic key enabled");
        }

        private void ChkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkOfflineMode.IsChecked ?? false;
            App.Settings.Current.OfflineMode = isEnabled;
            App.Logger?.Information("Offline mode {Status}", isEnabled ? "enabled" : "disabled");
        }

        private void ChkDualMon_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkDualMon.IsChecked ?? true;
            App.Settings.Current.DualMonitorEnabled = isEnabled;

            // Refresh all services if engine is running
            if (_isRunning)
            {
                // Refresh overlays (pink filter, spiral, brain drain) - restart to add/remove monitor windows
                App.Overlay.RefreshForDualMonitorChange();

                // Bouncing text needs restart
                App.BouncingText.Stop();
                if (App.Settings.Current.BouncingTextEnabled && App.Settings.Current.PlayerLevel >= 60)
                {
                    App.BouncingText.Start();
                }

                App.Logger?.Information("Dual monitor toggled: {Enabled} - services refreshed", isEnabled);
            }

            App.Settings.Save();
        }

        private void ChkWinStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkWinStart.IsChecked ?? false;
            var isHidden = ChkStartHidden.IsChecked ?? false;

            if (isEnabled && isHidden)
            {
                // Show warning when both startup and hidden are enabled
                var result = MessageBox.Show(this,
                    "The app will launch minimized to system tray on startup.\n\n" +
                    "You will need to click the tray icon to show the main window.\n\n" +
                    "Are you sure you want to enable this?",
                    "Startup Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    ChkWinStart.IsChecked = false;
                    return;
                }
            }

            // Apply the startup setting
            if (!StartupManager.SetStartupState(isEnabled))
            {
                MessageBox.Show(this,
                    "Failed to update Windows startup setting.\nPlease check your permissions.",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ChkWinStart.IsChecked = StartupManager.IsRegistered();
            }
        }

        private void ChkStartHidden_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isStartup = ChkWinStart.IsChecked ?? false;
            var isHidden = ChkStartHidden.IsChecked ?? false;

            if (isStartup && isHidden)
            {
                // Show warning when enabling hidden while startup is already enabled
                var result = MessageBox.Show(this,
                    "The app will launch minimized to system tray on startup.\n\n" +
                    "You will need to click the tray icon to show the main window.\n\n" +
                    "Are you sure you want to enable this?",
                    "Startup Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    ChkStartHidden.IsChecked = false;
                }
            }
        }

        #endregion

        #region Window Events

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Only allow actual close if exit was explicitly requested
            if (_exitRequested)
            {
                // Kill all audio and effects first - ensures clean exit
                App.KillAllAudio();

                // Actually closing - clean up
                SaveSettings();
                _schedulerTimer?.Stop();
                _rampTimer?.Stop();
                _packPreviewTimer?.Stop();
                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                _avatarTubeWindow?.Close();

                // Stop and dispose session engine (closes corner GIF window)
                try
                {
                    _sessionEngine?.Dispose();
                }
                catch { }

                // Explicitly dispose overlay service
                try
                {
                    App.Overlay?.Dispose();
                }
                catch { }
            }
            else
            {
                // Always minimize to tray instead of closing
                e.Cancel = true;
                _trayIcon?.MinimizeToTray();
                HideAvatarTube();

                // Stop bouncing text when minimizing to tray (user expects app to be "closed")
                App.BouncingText?.Stop();
            }
            base.OnClosing(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            // Handle restoring from tray or maximizing
            if (WindowState == WindowState.Normal)
            {
                ShowAvatarTube();

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }

                // Restore autonomy and avatar mute state if we paused them on minimize
                if (_autonomyWasPausedOnMinimize && _wasAutonomyRunningBeforeMinimize)
                {
                    App.Autonomy?.Start();
                    _autonomyWasPausedOnMinimize = false;
                    _wasAutonomyRunningBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored autonomy mode after restore from minimize");
                }

                if (_avatarWasMutedOnMinimize && _wasAvatarUnmutedBeforeMinimize)
                {
                    _avatarTubeWindow?.SetMuteAvatar(false);
                    _avatarWasMutedOnMinimize = false;
                    _wasAvatarUnmutedBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored avatar unmuted state after restore from minimize");
                }

                // Update maximize button icon
                BtnMaximize.Content = "☐";
            }
            else if (WindowState == WindowState.Maximized)
            {
                // Detach avatar when maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                ShowAvatarTube();

                // Update maximize button icon
                BtnMaximize.Content = "❐";
            }
            else if (WindowState == WindowState.Minimized)
            {
                // Auto-pause autonomy and mute avatar when minimized with attached avatar
                // (no point running effects when user can't see them)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    // Pause autonomy if it's running
                    if (App.Autonomy?.IsEnabled == true)
                    {
                        _wasAutonomyRunningBeforeMinimize = true;
                        _autonomyWasPausedOnMinimize = true;
                        App.Autonomy.Stop();
                        App.Logger?.Debug("MainWindow: Auto-paused autonomy mode on minimize (attached avatar)");
                    }

                    // Mute avatar if it's not already muted
                    if (_avatarTubeWindow.IsMuted == false)
                    {
                        _wasAvatarUnmutedBeforeMinimize = true;
                        _avatarWasMutedOnMinimize = true;
                        _avatarTubeWindow.SetMuteAvatar(true);
                        App.Logger?.Debug("MainWindow: Auto-muted avatar on minimize (attached avatar)");
                    }
                }
            }
        }

        #endregion
    }
}