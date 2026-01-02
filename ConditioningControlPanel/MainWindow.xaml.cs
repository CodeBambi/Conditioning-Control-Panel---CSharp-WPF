using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private bool _isLoading = true;
        private BrowserService? _browser;
        private bool _browserInitialized = false;
        private TrayIconService? _trayIcon;
        private GlobalKeyboardHook? _keyboardHook;
        private bool _isCapturingPanicKey = false;
        private bool _exitRequested = false;
        private int _panicPressCount = 0;
        private DateTime _lastPanicTime = DateTime.MinValue;
        
        // Session Engine
        private SessionEngine? _sessionEngine;
        
        // Avatar Tube Window
        private AvatarTubeWindow? _avatarTubeWindow;
        
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

        public MainWindow()
        {
            InitializeComponent();
            
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
                
                // Explicitly stop and dispose overlay to close all blur windows
                try
                {
                    App.Overlay?.Stop();
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
            
            // Initialize global keyboard hook
            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyPressed += OnGlobalKeyPressed;
            _keyboardHook.Start();
            
            // Subscribe to progression events for real-time XP updates
            App.Progression.XPChanged += OnXPChanged;
            App.Progression.LevelUp += OnLevelUp;
            
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
            
            // Ensure all services are stopped on startup (cleanup any leftover state)
            App.BouncingText.Stop();
            App.Overlay.Stop();
            
            // Show welcome dialog on first launch
            WelcomeDialog.ShowIfNeeded();
            
            // Initialize scheduler timer (checks every 30 seconds)
            _schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _schedulerTimer.Tick += SchedulerTimer_Tick;
            _schedulerTimer.Start();
            
            // Check scheduler immediately on startup
            CheckSchedulerOnStartup();
            
            // Initialize browser when window is loaded
            Loaded += MainWindow_Loaded;
        }

        private void OnXPChanged(object? sender, double xp)
        {
            Dispatcher.Invoke(() => UpdateLevelDisplay());
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
                        player.Volume = (App.Settings.Current.MasterVolume / 100.0);
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
                StopEngine();
                
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
            }
            else
            {
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
            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            hwndSource?.AddHook(WndProc);

            // Re-center after load in case DPI wasn't available in constructor
            CenterOnPrimaryScreen();

            // Update panic key button
            UpdatePanicKeyButton();

            // Handle start minimized (to tray)
            if (App.Settings.Current.StartMinimized)
            {
                _trayIcon?.MinimizeToTray();
            }

            // Handle auto-start engine
            if (App.Settings.Current.AutoStartEngine)
            {
                StartEngine();
            }

            // Auto-initialize browser on startup
            await InitializeBrowserAsync();

            // Initialize Avatar Tube Window
            InitializeAvatarTube();
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Intercept minimize command to hide avatar tube BEFORE minimize animation
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
            {
                HideAvatarTube();
            }
            return IntPtr.Zero;
        }

        #region Avatar Tube Window

        private void InitializeAvatarTube()
        {
            try
            {
                _avatarTubeWindow = new AvatarTubeWindow(this);

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
            _avatarTubeWindow?.ShowTube();
            _avatarTubeWindow?.StartPoseAnimation();
        }

        public void HideAvatarTube()
        {
            _avatarTubeWindow?.StopPoseAnimation();
            _avatarTubeWindow?.HideTube();
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

        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.gg/M6kpnrTPa9",
                    UseShellExecute = true
                });
                App.Logger?.Information("Opened Discord invite link");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Discord link");
            }
        }

        private void ShowTab(string tab)
        {
            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;

            // Reset button styles
            var pinkBrush = FindResource("PinkBrush") as SolidColorBrush;
            BtnSettings.Background = Brushes.Transparent;
            BtnSettings.Foreground = pinkBrush;
            BtnPresets.Background = Brushes.Transparent;
            BtnPresets.Foreground = pinkBrush;
            BtnProgression.Background = Brushes.Transparent;
            BtnProgression.Foreground = pinkBrush;
            BtnAchievements.Background = Brushes.Transparent;
            BtnAchievements.Foreground = pinkBrush;

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    BtnSettings.Background = pinkBrush;
                    BtnSettings.Foreground = Brushes.White;
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    BtnPresets.Background = pinkBrush;
                    BtnPresets.Foreground = Brushes.White;
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
                        // Optionally re-throw or show a message box, but for now, just log
                        throw; 
                    }
                    BtnProgression.Background = pinkBrush;
                    BtnProgression.Foreground = Brushes.White;
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    BtnAchievements.Background = pinkBrush;
                    BtnAchievements.Foreground = Brushes.White;
                    UpdateAchievementCount();
                    break;
            }
        }
        
        private void UpdateAchievementCount()
        {
            if (TxtAchievementCount != null && App.Achievements != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount();
                var total = App.Achievements.GetTotalCount();
                TxtAchievementCount.Text = $"{unlocked} / {total} Achievements Unlocked";
            }
        }
        
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
            
            // Add "Coming Soon" placeholders
            for (int i = 0; i < 2; i++)
            {
                var border = new Border { Style = tileStyle };
                var stack = new StackPanel 
                { 
                    VerticalAlignment = VerticalAlignment.Center, 
                    HorizontalAlignment = HorizontalAlignment.Center 
                };
                stack.Children.Add(new TextBlock 
                { 
                    Text = "❓", 
                    FontSize = 32, 
                    HorizontalAlignment = HorizontalAlignment.Center 
                });
                stack.Children.Add(new TextBlock 
                { 
                    Text = "Coming Soon", 
                    Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)), 
                    FontSize = 10, 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(0, 5, 0, 0) 
                });
                border.Child = stack;
                AchievementGrid.Children.Add(border);
            }
            
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
            
            // Add all presets - use black text for dropdown (white background)
            foreach (var preset in _allPresets)
            {
                CmbPresets.Items.Add(new ComboBoxItem 
                { 
                    Content = preset.Name, 
                    Tag = preset.Id,
                    Foreground = Brushes.Black
                });
            }
            
            // Add separator and "Save New" option
            CmbPresets.Items.Add(new Separator());
            CmbPresets.Items.Add(new ComboBoxItem 
            { 
                Content = "➕ Save as New Preset...", 
                Tag = "new",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 50, 150)) // Dark pink for visibility
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
                InitialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "images")
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

            // Live update during session
            if (_sessionEngine != null && _sessionEngine.IsRunning)
            {
                _sessionEngine.UpdateCornerGifSize((int)e.NewValue);
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
                Width = 400,
                Height = string.IsNullOrEmpty(noText) ? 260 : 280,
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
            
            var confirmed = ShowStyledDialog(
                $"🌅 Start {_selectedSession.Name}?",
                $"Duration: {_selectedSession.DurationMinutes} minutes\n\n" +
                "Your current settings will be temporarily replaced.\n" +
                "They will be restored when the session ends.\n\n" +
                "Ready to begin?",
                "▶ Start Session", "Not yet");
                
            if (confirmed)
            {
                StartSession(_selectedSession);
            }
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
            }
            
            // Initialize session engine if needed
            if (_sessionEngine == null)
            {
                _sessionEngine = new SessionEngine(this);
                _sessionEngine.SessionCompleted += OnSessionCompleted;
                _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
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
                
                // Update UI to show session is running
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Visibility = Visibility.Visible;
                    TxtPresetsStatus.Text = $"🎯 {session.Name} running... {session.DurationMinutes}:00 remaining";
                }
                
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
                App.Progression.AddXP(e.XPEarned);
                
                // Show completion window
                var completeWindow = new SessionCompleteWindow(e.Session, e.Duration, e.XPEarned);
                completeWindow.Owner = this;
                completeWindow.ShowDialog();
                
                // Update status
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Visibility = Visibility.Collapsed;
                    TxtPresetsStatus.Text = "";
                }
                
                App.Logger?.Information("Session {Name} completed, awarded {XP} XP", e.Session.Name, e.XPEarned);
            });
        }
        
        private void OnSessionProgressUpdated(object? sender, SessionProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_sessionEngine?.CurrentSession != null && TxtPresetsStatus != null)
                {
                    var remaining = e.Remaining;
                    TxtPresetsStatus.Visibility = Visibility.Visible;
                    TxtPresetsStatus.Text = $"🎯 {_sessionEngine.CurrentSession.Name} running... " +
                        $"{remaining.Minutes:D2}:{remaining.Seconds:D2} remaining ({e.ProgressPercent:F0}%)";
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
            TxtSessionXP.Text = $"+{session.BonusXP} XP";
            TxtSessionDifficulty.Text = session.GetDifficultyText();
            TxtSessionDescription.Text = session.Description;

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
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
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

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length != 1) return;

            var filePath = files[0];
            if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
            {
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
                ShowDropZoneStatus($"Session loaded: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via global drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
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
                        // Preserve original ID and save over existing file
                        editedSession.Id = session.Id;
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
                    
                    var confirmed = ShowStyledDialog(
                        "⚠ Stop Session?",
                        $"You're currently in a session:\n" +
                        $"{session?.Icon} {session?.Name}\n\n" +
                        $"Time elapsed: {elapsed.Minutes:D2}:{elapsed.Seconds:D2}\n\n" +
                        "If you stop now, you will NOT receive the XP reward.\n" +
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

        private void StartEngine()
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
            }

            // Start brain drain service (requires level 70)
            if (settings.PlayerLevel >= 70 && settings.BrainDrainEnabled)
            {
                App.BrainDrain.Start();
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

        private void StopEngine()
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
            App.Audio.Unduck();

            // Force close any open lock card windows (panic button should close them immediately)
            LockCardWindow.ForceCloseAll();

            // Stop ramp timer and reset sliders
            StopRampTimer();

            _isRunning = false;
            UpdateStartButton();

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
            
            // Calculate progress (0.0 to 1.0)
            var progress = Math.Min(elapsed / duration, 1.0);
            
            // Calculate current multiplier based on progress (linear interpolation from 1.0 to max)
            var currentMult = 1.0 + (multiplier - 1.0) * progress;
            
            // Update linked sliders and settings
            Dispatcher.Invoke(() =>
            {
                if (settings.RampLinkFlashOpacity && _rampBaseValues.TryGetValue("FlashOpacity", out var flashBase))
                {
                    var newVal = (int)Math.Min(flashBase * currentMult, 100);
                    SliderOpacity.Value = newVal;
                    TxtOpacity.Text = $"{newVal}%";
                    settings.FlashOpacity = newVal;
                }
                
                if (settings.RampLinkSpiralOpacity && _rampBaseValues.TryGetValue("SpiralOpacity", out var spiralBase))
                {
                    var newVal = (int)Math.Min(spiralBase * currentMult, 50);
                    SliderSpiralOpacity.Value = newVal;
                    TxtSpiralOpacity.Text = $"{newVal}%";
                    settings.SpiralOpacity = newVal;
                }
                
                if (settings.RampLinkPinkFilterOpacity && _rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkBase))
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
            
            if (!isDayActive) return false;
            
            // Parse start and end times
            if (!TimeSpan.TryParse(settings.SchedulerStartTime, out var startTime))
                startTime = new TimeSpan(16, 0, 0); // Default 16:00
            
            if (!TimeSpan.TryParse(settings.SchedulerEndTime, out var endTime))
                endTime = new TimeSpan(22, 0, 0); // Default 22:00
            
            var currentTime = now.TimeOfDay;
            
            // Handle case where end time is after midnight (e.g., 22:00 - 02:00)
            if (endTime < startTime)
            {
                // Overnight schedule
                return currentTime >= startTime || currentTime < endTime;
            }
            else
            {
                // Same-day schedule
                return currentTime >= startTime && currentTime < endTime;
            }
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
                    Children =
                    {
                        new TextBlock { Text = "⏹", Margin = new Thickness(0, 0, 10, 0) },
                        new TextBlock { Text = "STOP" }
                    }
                };
                
                // Also update Presets tab button using direct reference
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Visibility = Visibility.Visible;
                }
                
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "⏹", Margin = new Thickness(0, 0, 10, 0) },
                        new TextBlock { Text = "STOP" }
                    }
                };
            }
            else
            {
                BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "▶", Margin = new Thickness(0, 0, 10, 0) },
                        new TextBlock { Text = "START" }
                    }
                };
                
                // Also update Presets tab button
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Visibility = Visibility.Collapsed;
                    TxtPresetsStatus.Text = "";
                }
                
                BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "▶", Margin = new Thickness(0, 0, 10, 0) },
                        new TextBlock { Text = "START" }
                    }
                };
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

            // Audio
            SliderMaster.Value = s.MasterVolume;
            ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            SliderDuck.Value = s.DuckingLevel;

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

            // Update level display
            UpdateLevelDisplay();
            
            // Update all slider text displays
            UpdateSliderTexts();
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

            // Audio
            s.MasterVolume = (int)SliderMaster.Value;
            s.AudioDuckingEnabled = ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SliderDuck.Value;

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

            // Scheduler
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
            SaveSettings();
            MessageBox.Show("Settings saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var xpNeeded = 50 + (level * 20);

            TxtLevel.Text = $"Lvl {level}";
            TxtLevelLabel.Text = $"LVL {level}";
            TxtXP.Text = $"{(int)xp} / {xpNeeded} XP";

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
        private void SetFeatureImageBlur(Border? featureImageBorder, bool blur)
        {
            try
            {
                if (featureImageBorder == null)
                {
                    App.Logger?.Warning("SetFeatureImageBlur: featureImageBorder is null.");
                    return;
                }
                
                // Ensure the child is an Image before attempting to set its effect
                if (featureImageBorder.Child is Image image)
                {
                    if (blur)
                    {
                        image.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 };
                        App.Logger?.Debug("SetFeatureImageBlur: Applied blur to {ElementName}", featureImageBorder.Name);
                    }
                    else
                    {
                        image.Effect = null;
                        App.Logger?.Debug("SetFeatureImageBlur: Removed blur from {ElementName}", featureImageBorder.Name);
                    }
                }
                else
                {
                    App.Logger?.Warning("SetFeatureImageBlur: Child of featureImageBorder is not an Image. Child type: {ChildType}", featureImageBorder.Child?.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SetFeatureImageBlur: Error setting blur effect for {ElementName}: {Error}", featureImageBorder?.Name, ex.Message);
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
            
            // If flashes > 30, force audio OFF and disable checkbox
            if (flashFreq > 30)
            {
                ChkFlashAudio.IsChecked = false;
                ChkFlashAudio.IsEnabled = false;
                App.Settings.Current.FlashAudioEnabled = false;
                SliderFlashDuration.IsEnabled = true;
                SliderFlashDuration.Opacity = 1.0;
                TxtAudioWarning.Visibility = Visibility.Visible;
                TxtAudioWarning.Text = "⚠ Audio off >30/h";
            }
            else
            {
                ChkFlashAudio.IsEnabled = true;
                TxtAudioWarning.Text = "⚠ Max 30/h";
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
        }

        private void SliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtDuck == null) return;
            TxtDuck.Text = $"{(int)e.NewValue}%";
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
            ApplySettingsLive();
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
            ApplySettingsLive();
        }

        private void SliderLockCardRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtLockCardRepeats == null) return;
            TxtLockCardRepeats.Text = $"{(int)e.NewValue}x";
            ApplySettingsLive();
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
            App.BubbleCount.TriggerGame();
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

        private void BtnTestBrainDrain_Click(object sender, RoutedEventArgs e)
        {
            App.BrainDrain.Test();
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

        private void ShowEasterEgg()
        {
            var easterEggWindow = new EasterEggWindow();
            easterEggWindow.Owner = this;
            easterEggWindow.ShowDialog();
        }

        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            App.Video.TriggerVideo();
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
            
            // Show warning and minimize to tray
            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Strict Lock Card",
                "• You will NOT be able to escape lock cards with ESC\n" +
                "• You MUST type the phrase the required number of times\n" +
                "• The app will minimize to tray when this is enabled\n" +
                "• This can be very restrictive!");
            
            if (confirmed)
            {
                // Minimize to tray immediately
                _trayIcon?.MinimizeToTray();
            }
            else
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

        private void BtnOpenAssets_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
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
                "• The app will minimize to system tray\n" +
                "• This can be very intense and restrictive");
            
            if (confirmed)
            {
                // Minimize to tray immediately
                _trayIcon?.MinimizeToTray();
            }
            else
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
                "• The app will minimize to system tray\n" +
                "• Make sure you know what you're doing!");
            
            if (confirmed)
            {
                // Minimize to tray immediately
                _trayIcon?.MinimizeToTray();
            }
            else
            {
                ChkNoPanic.IsChecked = false;
            }
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
                // Actually closing - clean up
                SaveSettings();
                _schedulerTimer?.Stop();
                _rampTimer?.Stop();
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

                // Explicitly stop all overlay windows before app exits
                try
                {
                    App.Overlay?.Stop();
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
                
                // Show hint on first minimize
                _trayIcon?.ShowNotification("Still Running", 
                    "App minimized to tray. Use Exit button or double-tap panic key to quit.", 
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            
            // Minimize to tray when minimized
            if (WindowState == WindowState.Minimized)
            {
                _trayIcon?.MinimizeToTray();
                HideAvatarTube();
            }
            else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
            {
                ShowAvatarTube();
            }
        }

        #endregion
    }
}