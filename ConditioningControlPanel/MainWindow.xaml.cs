using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        
        // Ramp tracking
        private DispatcherTimer? _rampTimer;
        private DateTime _rampStartTime;
        private Dictionary<string, double> _rampBaseValues = new();
        
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
                SaveSettings();
                Application.Current.Shutdown();
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
            _isLoading = false;
            
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
            });
        }

        private void PlayLevelUpSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Audio", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lvlup.mp3"),
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
                // Try multiple paths for the logo
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Resources", "logo.png"),
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ImgLogo.Source = bitmap;
                        App.Logger?.Debug("Logo loaded from: {Path}", path);
                        return;
                    }
                }

                // Try embedded resource as fallback
                try
                {
                    var resourceUri = new Uri("pack://application:,,,/Resources/logo.png", UriKind.Absolute);
                    ImgLogo.Source = new System.Windows.Media.Imaging.BitmapImage(resourceUri);
                    App.Logger?.Debug("Logo loaded from embedded resource");
                }
                catch
                {
                    App.Logger?.Warning("Could not load logo from any location");
                }
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
        }

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

        private void ShowTab(string tab)
        {
            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;

            // Reset button styles
            var pinkBrush = FindResource("PinkBrush") as SolidColorBrush;
            BtnSettings.Background = Brushes.Transparent;
            BtnSettings.Foreground = pinkBrush;
            BtnPresets.Background = Brushes.Transparent;
            BtnPresets.Foreground = pinkBrush;
            BtnProgression.Background = Brushes.Transparent;
            BtnProgression.Foreground = pinkBrush;

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
                    ProgressionTab.Visibility = Visibility.Visible;
                    BtnProgression.Background = pinkBrush;
                    BtnProgression.Foreground = Brushes.White;
                    break;
            }
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
            PresetsList.Children.Clear();
            _allPresets = Models.Preset.GetDefaultPresets();
            _allPresets.AddRange(App.Settings.Current.UserPresets);
            
            foreach (var preset in _allPresets)
            {
                var card = CreatePresetCard(preset);
                PresetsList.Children.Add(card);
            }
        }

        private Border CreatePresetCard(Models.Preset preset)
        {
            var isSelected = _selectedPreset?.Id == preset.Id;
            var card = new Border
            {
                Background = new SolidColorBrush(isSelected ? Color.FromRgb(60, 60, 100) : Color.FromRgb(30, 30, 58)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Tag = preset.Id
            };
            
            card.MouseLeftButtonDown += (s, e) => SelectPreset(preset);
            
            var stack = new StackPanel();
            
            // Header with name and badge
            var header = new Grid();
            var nameText = new TextBlock
            {
                Text = preset.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14
            };
            header.Children.Add(nameText);
            
            if (preset.IsDefault)
            {
                var badge = new Border
                {
                    Background = FindResource("PinkBrush") as SolidColorBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                badge.Child = new TextBlock
                {
                    Text = "DEFAULT",
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                };
                header.Children.Add(badge);
            }
            else
            {
                var badge = new TextBlock
                {
                    Text = "CUSTOM",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                header.Children.Add(badge);
            }
            
            stack.Children.Add(header);
            
            // Description
            var desc = new TextBlock
            {
                Text = preset.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            stack.Children.Add(desc);
            
            // Quick stats
            var stats = new TextBlock
            {
                Text = GetPresetQuickStats(preset),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                FontSize = 10,
                Margin = new Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(stats);
            
            card.Child = stack;
            return card;
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
            
            // Update UI
            RefreshPresetsList();
            UpdatePresetPreview(preset);
            
            // Enable buttons
            BtnLoadPreset.IsEnabled = true;
            BtnSaveOverPreset.IsEnabled = !preset.IsDefault;
            BtnDeletePreset.IsEnabled = !preset.IsDefault;
        }

        private void UpdatePresetPreview(Models.Preset preset)
        {
            TxtPresetDetailName.Text = preset.Name;
            TxtPresetDetailDesc.Text = preset.Description;
            
            TxtPreviewFlash.Text = preset.FlashEnabled 
                ? $"Enabled | {preset.FlashFrequency}/min | Opacity: {preset.FlashOpacity}% | Fade: {preset.FadeDuration}%"
                : "Disabled";
                
            TxtPreviewVideo.Text = preset.MandatoryVideosEnabled 
                ? $"Enabled | {preset.VideosPerHour}/hour | Strict: {(preset.StrictLockEnabled ? "Yes" : "No")}"
                : "Disabled";
                
            TxtPreviewSubliminal.Text = preset.SubliminalEnabled 
                ? $"Enabled | {preset.SubliminalFrequency}/min | Opacity: {preset.SubliminalOpacity}%"
                : "Disabled";
                
            TxtPreviewAudio.Text = $"Whispers: {(preset.SubAudioEnabled ? $"Yes ({preset.SubAudioVolume}%)" : "No")} | Master: {preset.MasterVolume}%";
            
            TxtPreviewOverlays.Text = $"Spiral: {(preset.SpiralEnabled ? $"Yes ({preset.SpiralOpacity}%)" : "No")} | Pink: {(preset.PinkFilterEnabled ? $"Yes ({preset.PinkFilterOpacity}%)" : "No")}";
            
            TxtPreviewAdvanced.Text = $"Bubbles: {(preset.BubblesEnabled ? "Yes" : "No")} | Lock Card: {(preset.LockCardEnabled ? $"Yes ({preset.LockCardRepeats}x)" : "No")}";
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
                BtnLoadPreset.IsEnabled = false;
                BtnSaveOverPreset.IsEnabled = false;
                BtnDeletePreset.IsEnabled = false;
                
                TxtPresetDetailName.Text = "Select a Preset";
                TxtPresetDetailDesc.Text = "Click on a preset to see details";
                
                RefreshPresetsList();
                RefreshPresetsDropdown();
                
                App.Logger?.Information("Deleted preset");
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
            
            // Start overlay service (handles spiral and pink filter based on level)
            if (settings.PlayerLevel >= 10 && (settings.SpiralEnabled || settings.PinkFilterEnabled))
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
            
            // Start ramp timer if enabled
            if (settings.IntensityRampEnabled)
            {
                StartRampTimer();
            }
            
            // Browser audio serves as background - no need to play separate music
            
            _isRunning = true;
            UpdateStartButton();
            
            App.Logger?.Information("Engine started - Overlay: {Overlay}, Bubbles: {Bubbles}, LockCard: {LockCard}, BubbleCount: {BubbleCount}", 
                App.Overlay.IsRunning, App.Bubbles.IsRunning, App.LockCard.IsRunning, App.BubbleCount.IsRunning);
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
            App.Audio.Unduck();
            
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
            }
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
                < 5 => "Beginner Bimbo",
                < 10 => "Training Bimbo",
                < 20 => "Eager Bimbo",
                < 30 => "Devoted Bimbo",
                < 50 => "Advanced Bimbo",
                _ => "Perfect Bimbo"
            };
            
            // Update unlockables visibility based on level
            UpdateUnlockablesVisibility(level);
        }

        private void UpdateUnlockablesVisibility(int level)
        {
            // Level 10 unlocks: Spiral Overlay, Pink Filter
            var level10Unlocked = level >= 10;
            SpiralLocked.Visibility = level10Unlocked ? Visibility.Collapsed : Visibility.Visible;
            SpiralUnlocked.Visibility = level10Unlocked ? Visibility.Visible : Visibility.Collapsed;
            PinkFilterLocked.Visibility = level10Unlocked ? Visibility.Collapsed : Visibility.Visible;
            PinkFilterUnlocked.Visibility = level10Unlocked ? Visibility.Visible : Visibility.Collapsed;
            
            // Level 20 unlocks: Bubbles
            var level20Unlocked = level >= 20;
            BubblesLocked.Visibility = level20Unlocked ? Visibility.Collapsed : Visibility.Visible;
            BubblesUnlocked.Visibility = level20Unlocked ? Visibility.Visible : Visibility.Collapsed;
            
            // Level 35 unlocks: Lock Card
            var level35Unlocked = level >= 35;
            LockCardLocked.Visibility = level35Unlocked ? Visibility.Collapsed : Visibility.Visible;
            LockCardUnlocked.Visibility = level35Unlocked ? Visibility.Visible : Visibility.Collapsed;
            
            // Level 50 unlocks: Bubble Count Game
            var level50Unlocked = level >= 50;
            Level50Locked.Visibility = level50Unlocked ? Visibility.Collapsed : Visibility.Visible;
            Level50Unlocked.Visibility = level50Unlocked ? Visibility.Visible : Visibility.Collapsed;
            
            // Level 60 unlocks: Bouncing Text
            var level60Unlocked = level >= 60;
            Level60Locked.Visibility = level60Unlocked ? Visibility.Collapsed : Visibility.Visible;
            Level60Unlocked.Visibility = level60Unlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Slider Events

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || TxtPerMin == null) return;
            TxtPerMin.Text = ((int)e.NewValue).ToString();
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
        }

        private void CmbBubbleCountDifficulty_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || CmbBubbleCountDifficulty.SelectedItem == null) return;
            
            var item = CmbBubbleCountDifficulty.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item?.Tag != null && int.TryParse(item.Tag.ToString(), out int difficulty))
            {
                App.Settings.Current.BubbleCountDifficulty = difficulty;
            }
        }

        private void ChkBubbleCountStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.BubbleCountStrictLock = ChkBubbleCountStrict.IsChecked ?? false;
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
        }

        private void BtnEditBouncingText_Click(object sender, RoutedEventArgs e)
        {
            var editor = new TextEditorDialog("Bouncing Text Phrases", App.Settings.Current.BouncingTextPool);
            editor.Owner = this;
            
            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                App.Settings.Current.BouncingTextPool = editor.ResultData;
                App.Logger?.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
            }
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
                Filter = "GIF Files (*.gif)|*.gif",
                Title = "Select Spiral GIF"
            };

            if (dialog.ShowDialog() == true)
            {
                App.Settings.Current.SpiralPath = dialog.FileName;
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
            }
            else
            {
                // Always minimize to tray instead of closing
                e.Cancel = true;
                _trayIcon?.MinimizeToTray();
                
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
            }
        }

        #endregion
    }
}
