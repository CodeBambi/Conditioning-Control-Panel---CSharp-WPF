using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using XamlAnimatedGif;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly Window _parentWindow;
        private readonly DispatcherTimer _poseTimer;
        private BitmapImage[] _avatarPoses;
        private int _currentPoseIndex = 0;
        private bool _isAttached = true;
        private Storyboard? _floatStoryboard;
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;
        private int _currentAvatarSet = 1; // Track which avatar set is loaded
        private int _selectedAvatarSet = 1; // User's manually selected avatar (can be lower than max unlocked)
        private int _maxUnlockedSet = 1; // Highest avatar set unlocked based on level
        private bool _useAnimatedAvatar = false; // Whether to use animated GIF

        // Avatar set titles
        private static readonly string[] AvatarTitles = new[]
        {
            "BASIC BIMBO",      // Set 1: Level 1-19
            "DUMB AIRHEAD",     // Set 2: Level 20-49
            "SYNTHETIC BLOWDOLL", // Set 3: Level 50-99
            "PERFECT FUCKPUPPET"  // Set 4: Level 100+
        };

        // Companion speech and chat
        private readonly Queue<string> _speechQueue = new();
        private bool _isGiggling = false;
        private bool _isWaitingForAi = false; // Blocks other giggles while waiting for AI
        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _triggerTimer; // Random trigger phrases
        private DateTime _lastClickTime = DateTime.MinValue;
        private bool _isInputVisible = false;
        private readonly Random _random = new();
        private bool _mainWindowClosed = false;

        // ============================================================
        // POSITIONING & SCALING - ADJUST THESE VALUES AS NEEDED
        // ============================================================

        // Design reference size (what the XAML is designed for)
        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;

        // Gap between tube window and main window (negative = overlap)
        // This will be scaled based on actual window size
        private const double BaseOffsetFromParent = -350;

        // Vertical offset from center (positive = lower, negative = higher)
        private const double VerticalOffset = 20;

        // Floating animation settings
        private const double FloatDistance = 8;
        private const double FloatDuration = 2.0;

        // Current scale factor
        private double _scaleFactor = 1.0;

        // Current avatar scale (for Ctrl+scroll/arrow key/menu resizing when detached)
        private double _currentScale = 1.0;
        private const double MinScale = 0.5;   // 50% - can shrink twice from 100%
        private const double MaxScale = 1.5;   // 150% - can grow twice from 100%
        private const double ScaleStep = 0.25; // 25% per step

        // Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // Window message hook for maintaining topmost during drag
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private HwndSource? _hwndSource;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        public AvatarTubeWindow(Window parentWindow)
        {
            InitializeComponent();

            _parentWindow = parentWindow;
            // Don't set Owner - it causes black window artifacts during minimize
            // We manage visibility manually via event handlers instead

            // Determine which avatar set to load based on player level
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            _maxUnlockedSet = GetAvatarSetForLevel(playerLevel);

            // Load user's saved avatar selection, or use max unlocked
            _selectedAvatarSet = App.Settings?.Current?.SelectedAvatarSet ?? _maxUnlockedSet;
            // Clamp to valid range (1 to max unlocked)
            _selectedAvatarSet = Math.Clamp(_selectedAvatarSet, 1, _maxUnlockedSet);
            _currentAvatarSet = _selectedAvatarSet;

            // Check if this avatar set has an animated version available
            _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

            // Load avatar poses for the appropriate set
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);

            // Set initial avatar (animated or static)
            if (_useAnimatedAvatar)
            {
                LoadAnimatedAvatar(_currentAvatarSet);
            }
            else if (_avatarPoses.Length > 0)
            {
                ImgAvatar.Source = _avatarPoses[0];
            }

            // Apply size/position adjustments for non-basic avatars
            ApplyAvatarTransform(_currentAvatarSet);

            // Initialize title box display
            UpdateTitleDisplay(playerLevel);
            UpdateNavigationArrows();

            // Setup pose switching timer (only for static avatars)
            _poseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _poseTimer.Tick += PoseTimer_Tick;
            
            // Subscribe to parent window events
            _parentWindow.LocationChanged += ParentWindow_PositionChanged;
            _parentWindow.SizeChanged += ParentWindow_PositionChanged;
            _parentWindow.StateChanged += ParentWindow_StateChanged;
            _parentWindow.IsVisibleChanged += ParentWindow_IsVisibleChanged;
            _parentWindow.Activated += ParentWindow_Activated;
            _parentWindow.Closed += ParentWindow_Closed;
            
            // Get handles when loaded
            Loaded += OnLoaded;

            // Initialize context menu state
            UpdateQuickMenuState();

            // Subscribe to mouse wheel and keyboard for resizing when detached
            PreviewMouseWheel += Window_PreviewMouseWheel;
            PreviewKeyDown += Window_PreviewKeyDown;

            // Keep tube in front during position changes when attached
            LocationChanged += (s, e) => { if (_isAttached) BringToFrontTemporarily(); };

            // Wire up video service events for companion speech
            if (App.Video != null)
            {
                App.Video.VideoStarted += OnVideoStarted;
                App.Video.VideoEnded += OnVideoEnded;
            }

            // Wire up game completion events
            if (App.BubbleCount != null)
            {
                App.BubbleCount.GameCompleted += OnGameCompleted;
            }

            // Wire up flash service events for pre-announcement
            if (App.Flash != null)
            {
                App.Flash.FlashAboutToDisplay += OnFlashAboutToDisplay;
            }

            // Wire up subliminal service events for acknowledgment
            if (App.Subliminal != null)
            {
                App.Subliminal.SubliminalDisplayed += OnSubliminalDisplayed;
            }

            // Wire up bubble service events for occasional pop acknowledgment
            if (App.Bubbles != null)
            {
                App.Bubbles.OnBubblePopped += OnBubblePopped;
            }

            // Wire up window awareness events (opt-in feature)
            if (App.WindowAwareness != null)
            {
                App.WindowAwareness.ActivityChanged += OnActivityChanged;
                // Start awareness if enabled
                App.WindowAwareness.Start();
            }

            // Start idle timer for random giggles
            StartIdleTimer();

            // Start trigger timer if enabled
            StartTriggerTimer();

            // Handle clicks outside the input panel to close it
            PreviewMouseDown += Window_PreviewMouseDown;

            App.Logger?.Information("AvatarTubeWindow initialized with avatar set {Set} for level {Level}",
                _currentAvatarSet, playerLevel);
        }

        /// <summary>
        /// Determines which avatar set to use based on player level
        /// </summary>
        /// <param name="level">Player's current level</param>
        /// <returns>Avatar set number (1-4)</returns>
        public static int GetAvatarSetForLevel(int level)
        {
            // Avatar Set 4: Level 100+
            if (level >= 100) return 4;
            // Avatar Set 3: Level 50-99
            if (level >= 50) return 3;
            // Avatar Set 2: Level 20-49
            if (level >= 20) return 2;
            // Avatar Set 1: Level 1-19 (default)
            return 1;
        }

        /// <summary>
        /// Updates the avatar to match the current player level
        /// Call this when the player levels up
        /// </summary>
        public void UpdateAvatarForLevel(int newLevel)
        {
            int newMaxSet = GetAvatarSetForLevel(newLevel);

            // Update max unlocked (user may have unlocked a new avatar)
            if (newMaxSet > _maxUnlockedSet)
            {
                App.Logger?.Information("New avatar unlocked! Set {NewSet} at level {Level}", newMaxSet, newLevel);
                _maxUnlockedSet = newMaxSet;

                // Auto-switch to newly unlocked avatar
                _selectedAvatarSet = newMaxSet;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                    App.Settings.Save();
                }

                SwitchToAvatarSet(newMaxSet, animate: true);
            }

            // Update title display
            UpdateTitleDisplay(newLevel);
            UpdateNavigationArrows();
        }

        /// <summary>
        /// Check if an avatar set has animated GIF version available
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private bool HasAnimatedAvatar(int setNumber)
        {
            try
            {
                // Try to load the animated resource to verify it exists
                // Naming pattern: animated1_1.gif, animated2_1.gif, etc.
                var uri = new Uri($"pack://application:,,,/Resources/animated{setNumber}_1.gif", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                return info != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Load animated GIF avatar using XamlAnimatedGif
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private void LoadAnimatedAvatar(int setNumber)
        {
            try
            {
                // Naming pattern: animated1_1.gif, animated2_1.gif, etc.
                var gifUri = new Uri($"pack://application:,,,/Resources/animated{setNumber}_1.gif", UriKind.Absolute);

                // Hide static avatar, show animated
                ImgAvatar.Visibility = Visibility.Collapsed;
                ImgAvatarAnimated.Visibility = Visibility.Visible;

                // Set the animated GIF source
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, gifUri);
                AnimationBehavior.SetAutoStart(ImgAvatarAnimated, true);
                AnimationBehavior.SetRepeatBehavior(ImgAvatarAnimated, RepeatBehavior.Forever);

                // Stop pose timer (not needed for animated)
                _poseTimer.Stop();

                App.Logger?.Information("Loaded animated avatar: animated{Set}_1.gif", setNumber);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load animated avatar {Set}: {Error}", setNumber, ex.Message);
                // Fall back to static
                _useAnimatedAvatar = false;
                ImgAvatar.Visibility = Visibility.Visible;
                ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                if (_avatarPoses.Length > 0)
                {
                    ImgAvatar.Source = _avatarPoses[0];
                }
            }
        }

        /// <summary>
        /// Switch to a specific avatar set (with optional animation)
        /// </summary>
        private void SwitchToAvatarSet(int setNumber, bool animate = true)
        {
            if (setNumber < 1 || setNumber > _maxUnlockedSet) return;

            _currentAvatarSet = setNumber;
            _selectedAvatarSet = setNumber;
            _useAnimatedAvatar = HasAnimatedAvatar(setNumber);

            // Save selection
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SelectedAvatarSet = setNumber;
                App.Settings.Save();
            }

            Action switchAction = () =>
            {
                if (_useAnimatedAvatar)
                {
                    LoadAnimatedAvatar(setNumber);
                }
                else
                {
                    // Hide animated, show static
                    ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                    AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                    ImgAvatar.Visibility = Visibility.Visible;

                    _avatarPoses = LoadAvatarPoses(setNumber);
                    _currentPoseIndex = 0;
                    if (_avatarPoses.Length > 0)
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                    }

                    // Restart pose timer for static avatars
                    _poseTimer.Start();
                }

                // Update UI
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();
                ApplyAvatarTransform(setNumber);
            };

            if (animate)
            {
                // Fade transition
                var target = _useAnimatedAvatar ? (UIElement)ImgAvatarAnimated : ImgAvatar;
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, args) =>
                {
                    switchAction();
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    AvatarBorder.BeginAnimation(OpacityProperty, fadeIn);
                };
                AvatarBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                switchAction();
            }

            App.Logger?.Information("Switched to avatar set {Set} (animated: {Animated})", setNumber, _useAnimatedAvatar);
        }

        /// <summary>
        /// Update the title and level display
        /// </summary>
        private void UpdateTitleDisplay(int level)
        {
            // Get title for currently displayed avatar set
            int titleIndex = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitles.Length - 1);
            TxtAvatarTitle.Text = AvatarTitles[titleIndex];
            TxtAvatarLevel.Text = $"Lv. {level}";
        }

        /// <summary>
        /// Update navigation arrow visibility based on unlocked avatars
        /// </summary>
        private void UpdateNavigationArrows()
        {
            // Show arrows only if user has multiple avatars unlocked
            bool hasMultiple = _maxUnlockedSet > 1;

            // Previous arrow: show if not at set 1
            BtnPrevAvatar.Visibility = hasMultiple && _currentAvatarSet > 1
                ? Visibility.Visible : Visibility.Collapsed;

            // Next arrow: show if not at max unlocked
            BtnNextAvatar.Visibility = hasMultiple && _currentAvatarSet < _maxUnlockedSet
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Apply size and position transforms for different avatar sets
        /// Sets 2, 3, 4 are 12% bigger and 10px to the right
        /// </summary>
        private void ApplyAvatarTransform(int setNumber)
        {
            if (setNumber > 1)
            {
                // Sets 2, 3, 4: 12% bigger, 10px to the right
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1.12, 1.12));
                transformGroup.Children.Add(new TranslateTransform(10, 0));
                AvatarBorder.RenderTransform = transformGroup;
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                // Set 1 (Basic Bimbo): no transform
                AvatarBorder.RenderTransform = null;
            }
        }

        /// <summary>
        /// Navigate to previous avatar set
        /// </summary>
        private void BtnPrevAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            if (_currentAvatarSet > 1)
            {
                SwitchToAvatarSet(_currentAvatarSet - 1);
            }
        }

        /// <summary>
        /// Navigate to next avatar set
        /// </summary>
        private void BtnNextAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            if (_currentAvatarSet < _maxUnlockedSet)
            {
                SwitchToAvatarSet(_currentAvatarSet + 1);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tubeHandle = new WindowInteropHelper(this).Handle;
            _parentHandle = new WindowInteropHelper(_parentWindow).Handle;

            // Hook window messages (minimal hook, no z-order forcing)
            _hwndSource = HwndSource.FromHwnd(_tubeHandle);
            _hwndSource?.AddHook(WndProc);

            // Hide from Alt+Tab by adding WS_EX_TOOLWINDOW style
            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // Ensure NOT topmost when attached (starts attached)
            Topmost = false;

            // Calculate scale factor based on screen size and DPI
            CalculateScaleFactor();

            // Defer position update to ensure layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    BringToFrontTemporarily();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Window procedure hook (minimal - no longer forcing z-order to allow normal window switching)
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // No longer intercepting z-order changes - let Windows handle it normally
            return IntPtr.Zero;
        }

        private void CalculateScaleFactor()
        {
            try
            {
                // Get DPI scaling
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                // Get primary screen working area
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                double screenHeight = screen.WorkingArea.Height / dpiScale;
                double screenWidth = screen.WorkingArea.Width / dpiScale;

                // Calculate max scale that fits on screen (leave some margin)
                double maxHeightScale = (screenHeight * 0.85) / DesignHeight;
                double maxWidthScale = (screenWidth * 0.3) / DesignWidth; // Tube shouldn't be more than 30% of screen width

                _scaleFactor = Math.Min(maxHeightScale, maxWidthScale);
                _scaleFactor = Math.Max(0.4, Math.Min(1.0, _scaleFactor)); // Clamp between 40% and 100%

                // Apply scale to viewbox
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;

                App.Logger?.Information("AvatarTube scale factor: {Scale:F2} (Screen: {W}x{H}, DPI: {DPI:F2})",
                    _scaleFactor, screenWidth, screenHeight, dpiScale);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to calculate scale factor: {Error}", ex.Message);
                _scaleFactor = 0.7; // Safe default for smaller screens
                ContentViewbox.Width = DesignWidth * _scaleFactor;
                ContentViewbox.Height = DesignHeight * _scaleFactor;
            }
        }

        /// <summary>
        /// Ensure the window is visible when detached
        /// </summary>
        private void EnsureVisibleWhenDetached()
        {
            if (!_isAttached)
            {
                Show();
                // Don't activate or force topmost - let Windows handle focus normally
            }
        }

        /// <summary>
        /// Toggle the WS_EX_TOOLWINDOW style (controls Alt+Tab visibility)
        /// </summary>
        private void SetToolWindowStyle(bool isToolWindow)
        {
            if (_tubeHandle == IntPtr.Zero) return;

            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            if (isToolWindow)
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            else
            {
                SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW);
            }
        }

        private void StartFloatingAnimation()
        {
            var animation = new DoubleAnimation
            {
                From = -FloatDistance,
                To = FloatDistance,
                Duration = TimeSpan.FromSeconds(FloatDuration),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            
            _floatStoryboard = new Storyboard();
            _floatStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, ImgAvatar);
            Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.Y"));
            _floatStoryboard.Begin();
        }

        private void StopFloatingAnimation()
        {
            _floatStoryboard?.Stop();
        }

        /// <summary>
        /// Load avatar poses for a specific set
        /// </summary>
        /// <param name="setNumber">1 = default, 2 = level 20, 3 = level 50, 4 = level 100</param>
        private BitmapImage[] LoadAvatarPoses(int setNumber = 1)
        {
            var poses = new BitmapImage[4];
            
            // Determine the resource path based on set number
            // Set 1: avatar_pose1.png - avatar_pose4.png (original)
            // Set 2: avatar2_pose1.png - avatar2_pose4.png (level 20)
            // Set 3: avatar3_pose1.png - avatar3_pose4.png (level 50)
            // Set 4: avatar4_pose1.png - avatar4_pose4.png (level 100)
            string prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
            
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Resources/{prefix}{i + 1}.png", UriKind.Absolute);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    poses[i] = bitmap;
                    
                    App.Logger?.Debug("Loaded avatar pose: {Prefix}{Index}.png", prefix, i + 1);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to load avatar pose {Prefix}{Index}: {Error}", prefix, i + 1, ex.Message);
                    
                    // Try to fall back to default avatar set if a higher set fails to load
                    if (setNumber > 1)
                    {
                        try
                        {
                            var fallbackUri = new Uri($"pack://application:,,,/Resources/avatar_pose{i + 1}.png", UriKind.Absolute);
                            var fallbackBitmap = new BitmapImage();
                            fallbackBitmap.BeginInit();
                            fallbackBitmap.UriSource = fallbackUri;
                            fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                            fallbackBitmap.EndInit();
                            fallbackBitmap.Freeze();
                            poses[i] = fallbackBitmap;
                            App.Logger?.Debug("Fell back to default avatar pose {Index}", i + 1);
                        }
                        catch
                        {
                            poses[i] = new BitmapImage();
                        }
                    }
                    else
                    {
                        poses[i] = new BitmapImage();
                    }
                }
            }
            
            return poses;
        }

        private void PoseTimer_Tick(object? sender, EventArgs e)
        {
            if (_avatarPoses.Length == 0) return;
            
            _currentPoseIndex = (_currentPoseIndex + 1) % _avatarPoses.Length;
            
            var fadeOut = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, args) =>
            {
                ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
                var fadeIn = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(150));
                ImgAvatar.BeginAnimation(OpacityProperty, fadeIn);
            };
            ImgAvatar.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void UpdatePosition()
        {
            if (!_isAttached || _parentWindow == null) return;

            // Get actual window dimensions (scaled)
            double actualWidth = ActualWidth > 0 ? ActualWidth : DesignWidth * _scaleFactor;
            double actualHeight = ActualHeight > 0 ? ActualHeight : DesignHeight * _scaleFactor;

            // Scale the offset based on current scale factor
            double scaledOffset = BaseOffsetFromParent * _scaleFactor;

            // Position to the LEFT of the parent window
            Left = _parentWindow.Left - actualWidth - scaledOffset;
            Top = _parentWindow.Top + (_parentWindow.ActualHeight - actualHeight) / 2 + (VerticalOffset * _scaleFactor);
        }

        private void ParentWindow_PositionChanged(object? sender, EventArgs e)
        {
            // Skip if parent is null, window is closing, or parent is minimized
            if (_parentWindow == null) return;
            try
            {
                if (_parentWindow.WindowState == WindowState.Minimized) return;
                UpdatePosition();
                // Keep tube in front when attached, during parent move
                if (_isAttached) BringToFrontTemporarily();
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                switch (_parentWindow.WindowState)
                {
                    case WindowState.Minimized:
                        if (_isAttached)
                        {
                            Hide();
                        }
                        else
                        {
                            // When detached, force visibility and topmost
                            EnsureVisibleWhenDetached();
                        }
                        break;
                    case WindowState.Normal:
                    case WindowState.Maximized:
                        if (_parentWindow.IsVisible)
                        {
                            Show();
                            if (_isAttached)
                            {
                                UpdatePosition();
                                BringToFrontTemporarily();
                            }
                            // When detached, WPF Topmost property handles it
                        }
                        break;
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                if ((bool)e.NewValue && _parentWindow.WindowState != WindowState.Minimized)
                {
                    Show();
                    if (_isAttached)
                    {
                        UpdatePosition();
                        BringToFrontTemporarily();
                    }
                    // When detached, WPF Topmost property handles it
                }
                else
                {
                    if (_isAttached)
                    {
                        Hide();
                    }
                    else
                    {
                        // When detached, force visibility and topmost
                        EnsureVisibleWhenDetached();
                    }
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            try
            {
                if (_parentWindow.WindowState != WindowState.Minimized && _parentWindow.IsVisible)
                {
                    Show();
                    UpdatePosition();

                    if (_isAttached)
                    {
                        // Bring tube to front (in front of parent) without stealing focus
                        BringToFrontTemporarily();
                    }
                }
            }
            catch { /* Window may be closing */ }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                // Attached mode: close the tube with the main window
                try { Close(); } catch { /* Already closing */ }
            }
            else
            {
                // Detached mode: keep floating independently
                _mainWindowClosed = true;

                App.Logger?.Information("Main window closed while detached - tube continues floating");
                Giggle("Main window closed! Right-click to dismiss~");
            }
        }

        // ============================================================
        // PUBLIC METHODS
        // ============================================================

        public void ShowTube()
        {
            try
            {
                Show();

                // Only update position if parent is visible
                if (_parentWindow != null && _parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    if (_isAttached) BringToFrontTemporarily();
                }

                StartFloatingAnimation();

                // Ensure TOOLWINDOW style is applied when attached
                if (_isAttached)
                {
                    SetToolWindowStyle(true);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error showing tube: {Error}", ex.Message);
            }
        }

        public void HideTube()
        {
            Hide();
        }

        public void StartPoseAnimation() => _poseTimer.Start();
        public void StopPoseAnimation() => _poseTimer.Stop();

        public void SetPose(int poseNumber)
        {
            if (poseNumber < 1 || poseNumber > 4) return;
            if (_avatarPoses.Length == 0) return;
            _currentPoseIndex = poseNumber - 1;
            ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        public void SetPoseInterval(TimeSpan interval)
        {
            _poseTimer.Interval = interval;
        }
        
        /// <summary>
        /// Gets the current avatar set number
        /// </summary>
        public int CurrentAvatarSet => _currentAvatarSet;

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _poseTimer?.Stop();
                StopFloatingAnimation();

                // Stop companion timers
                _speechTimer?.Stop();
                _idleTimer?.Stop();

                // Remove window message hook
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;

                // Unsubscribe from video service events
                if (App.Video != null)
                {
                    App.Video.VideoStarted -= OnVideoStarted;
                    App.Video.VideoEnded -= OnVideoEnded;
                }

                // Unsubscribe from game events
                if (App.BubbleCount != null)
                {
                    App.BubbleCount.GameCompleted -= OnGameCompleted;
                }

                // Unsubscribe from window awareness events
                if (App.WindowAwareness != null)
                {
                    App.WindowAwareness.ActivityChanged -= OnActivityChanged;
                }

                if (_parentWindow != null)
                {
                    _parentWindow.LocationChanged -= ParentWindow_PositionChanged;
                    _parentWindow.SizeChanged -= ParentWindow_PositionChanged;
                    _parentWindow.StateChanged -= ParentWindow_StateChanged;
                    _parentWindow.IsVisibleChanged -= ParentWindow_IsVisibleChanged;
                    _parentWindow.Activated -= ParentWindow_Activated;
                    _parentWindow.Closed -= ParentWindow_Closed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error during tube window cleanup: {Error}", ex.Message);
            }

            base.OnClosed(e);
        }
        
        private void ImgAvatar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Track for Neon Obsession achievement (20 rapid clicks)
            App.Achievements?.TrackAvatarClick();

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Avatar clicked! Count: {Count}/20", clickCount);

            // Double-click detection for chat toggle (only when AI chat is enabled)
            var now = DateTime.Now;
            if ((now - _lastClickTime).TotalMilliseconds < 300)
            {
                // Only allow chat input if AI is enabled
                if (App.Settings?.Current?.AiChatEnabled == true)
                {
                    ToggleInputPanel();
                }
                else
                {
                    // Show a random phrase instead when AI is disabled
                    Giggle(GetRandomBambiPhrase());
                }
            }
            _lastClickTime = now;

            // Visual feedback - quick pulse effect
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 1.1,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true
            };

            var scaleTransform = new System.Windows.Media.ScaleTransform(1, 1);
            ImgAvatar.RenderTransform = new System.Windows.Media.TransformGroup
            {
                Children = { new System.Windows.Media.TranslateTransform(AvatarTranslate.X, AvatarTranslate.Y), scaleTransform }
            };

            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
        }

        private void ImgAvatar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Close input panel on right-click
            HideInputPanel();
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close input panel when clicking outside of it
            if (_isInputVisible)
            {
                // Check if the click is outside the input panel
                var clickedElement = e.OriginalSource as DependencyObject;
                if (clickedElement != null && !IsDescendantOf(clickedElement, InputPanel))
                {
                    HideInputPanel();
                }
            }
        }

        private bool IsDescendantOf(DependencyObject element, DependencyObject parent)
        {
            while (element != null)
            {
                if (element == parent) return true;
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void HideInputPanel()
        {
            if (_isInputVisible)
            {
                _isInputVisible = false;
                InputPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuItemDismiss_Click(object sender, RoutedEventArgs e)
        {
            // Hide the sprite and reattach to main window UI
            App.Logger?.Information("User dismissed avatar - hiding and reattaching");

            // Reattach if detached
            if (!_isAttached)
            {
                Attach();
            }

            // Hide the tube
            HideTube();
        }

        // ============================================================
        // COMPANION SPEECH & CHAT
        // ============================================================

        // Base speech bubble dimensions (10% smaller)
        private const double BaseBubbleWidth = 420;
        private const double BaseBubbleHeight = 260;

        /// <summary>
        /// Queues a speech bubble to be displayed. Bubbles are shown one at a time.
        /// Blocked while waiting for AI response.
        /// </summary>
        public void Giggle(string text)
        {
            // Block if waiting for AI response
            if (_isWaitingForAi)
            {
                App.Logger?.Debug("Giggle blocked - waiting for AI: {Text}", text);
                return;
            }

            // Use BeginInvoke for non-blocking UI update
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_isGiggling)
                {
                    _speechQueue.Enqueue(text);
                    App.Logger?.Debug("Queued speech: {Text}", text);
                    return;
                }
                ShowGiggle(text);
            });
        }

        /// <summary>
        /// Shows a speech bubble immediately with priority (for AI responses).
        /// Clears any pending queue and interrupts current bubble.
        /// Also clears the AI waiting flag.
        /// </summary>
        public void GigglePriority(string text)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Clear AI waiting flag
                _isWaitingForAi = false;

                // Clear the queue - AI response takes priority
                _speechQueue.Clear();

                // Stop any current speech timer
                _speechTimer?.Stop();

                // Show immediately
                _isGiggling = false;
                ShowGiggle(text);

                App.Logger?.Debug("Priority speech (queue cleared): {Text}", text);
            });
        }

        /// <summary>
        /// Displays a speech bubble with text.
        /// </summary>
        private void ShowGiggle(string text)
        {
            _isGiggling = true;
            TxtSpeech.Text = text;

            // Adjust bubble size based on text length
            AdjustBubbleSize(text);

            SpeechBubble.Visibility = Visibility.Visible;

            // Bring tube to front when attached so bubble is visible above main window
            if (_isAttached)
            {
                BringToFrontTemporarily();
            }

            // Calculate display duration based on text length
            // Base: 5 seconds, plus ~0.05s per character, min 5s, max 14s
            double baseDuration = 5.0;
            double perCharDuration = 0.05;
            double calculatedDuration = baseDuration + (text.Length * perCharDuration);
            double displayDuration = Math.Clamp(calculatedDuration, 5.0, 14.0);

            // Hide after calculated duration
            _speechTimer?.Stop();
            _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };
            _speechTimer.Tick += (s, e) =>
            {
                _speechTimer.Stop();
                SpeechBubble.Visibility = Visibility.Collapsed;

                // After a 1-second break, check the queue for the next message
                var breakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                breakTimer.Tick += (s_break, e_break) =>
                {
                    breakTimer.Stop();
                    _isGiggling = false;
                    if (_speechQueue.Count > 0)
                    {
                        var nextText = _speechQueue.Dequeue();
                        App.Logger?.Debug("Dequeued speech: {Text}", nextText);
                        ShowGiggle(nextText);
                    }
                };
                breakTimer.Start();
            };
            _speechTimer.Start();

            // Reset idle timer when speaking
            ResetIdleTimer();

            App.Logger?.Debug("Companion says ({Chars} chars, {Duration:F1}s): {Text}",
                text.Length, displayDuration, text);
        }

        /// <summary>
        /// Adjusts the speech bubble size, font size, and position based on text length.
        /// Moves bubble up and right when it grows to avoid overlapping the tube.
        /// </summary>
        private void AdjustBubbleSize(string text)
        {
            int charCount = text.Length;

            // Determine size/font based on character count
            // Short (1-30 chars): normal size, normal font
            // Medium (31-60 chars): taller bubble for more lines
            // Long (61-100 chars): larger bubble, smaller font
            // Very long (101+ chars): largest bubble, smallest font

            double widthMultiplier;
            double heightMultiplier;
            double fontSize;

            if (charCount <= 30)
            {
                // Short text - normal everything
                widthMultiplier = 1.0;
                heightMultiplier = 1.0;
                fontSize = 26;
            }
            else if (charCount <= 60)
            {
                // Medium text - taller bubble, keep text big
                widthMultiplier = 1.1;
                heightMultiplier = 1.5;
                fontSize = 24;
            }
            else if (charCount <= 100)
            {
                // Long text - taller bubble for more lines, text stays big
                widthMultiplier = 1.2;
                heightMultiplier = 1.9;
                fontSize = 24;
            }
            else if (charCount <= 150)
            {
                // Very long text - much taller for extra lines
                widthMultiplier = 1.3;
                heightMultiplier = 2.2;
                fontSize = 22;
            }
            else
            {
                // Extra long text - tallest bubble, text still readable
                widthMultiplier = 1.4;
                heightMultiplier = 2.5;
                fontSize = 20;
            }

            double newWidth = BaseBubbleWidth * widthMultiplier;
            double newHeight = BaseBubbleHeight * heightMultiplier;

            SpeechBubble.Width = newWidth;
            SpeechBubble.Height = newHeight;
            TxtSpeech.FontSize = fontSize;

            // Anchor bottom-left corner at fixed position, bubble grows up and right
            // Attached anchor: left=200, bottom=580 (so top = 580 - height)
            // Detached anchor: left=150, bottom=530 (so top = 530 - height)
            if (_isAttached)
            {
                double anchorBottom = 580;
                double anchorLeft = 200;
                SpeechBubble.Margin = new Thickness(anchorLeft, anchorBottom - newHeight, 0, 0);
            }
            else
            {
                double anchorBottom = 530;
                double anchorLeft = 150;
                SpeechBubble.Margin = new Thickness(anchorLeft, anchorBottom - newHeight, 0, 0);
            }
        }

        /// <summary>
        /// Temporarily brings the tube window to front (above main window)
        /// </summary>
        private void BringToFrontTemporarily()
        {
            if (_tubeHandle == IntPtr.Zero) return;

            // Bring to front without making it topmost
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void StartIdleTimer()
        {
            var interval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
        }

        private void ResetIdleTimer()
        {
            _idleTimer?.Stop();
            StartIdleTimer();
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            Giggle(GetRandomBambiPhrase());
        }

        // ============================================================
        // TRIGGER MODE - Random trigger phrases (free for all)
        // ============================================================

        private void StartTriggerTimer()
        {
            if (App.Settings?.Current?.TriggerModeEnabled != true)
            {
                App.Logger?.Debug("TriggerMode: Not enabled, skipping timer start");
                return;
            }

            var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _triggerTimer.Tick += OnTriggerTick;
            _triggerTimer.Start();

            App.Logger?.Information("TriggerMode: Started with {Interval}s interval", interval);

            // Show first trigger immediately (after short delay for window to be ready)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => OnTriggerTick(null, EventArgs.Empty));
                });
            }));
        }

        private void StopTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            App.Logger?.Debug("TriggerMode: Timer stopped");
        }

        /// <summary>
        /// Restart trigger timer (call when settings change)
        /// </summary>
        public void RestartTriggerTimer()
        {
            StopTriggerTimer();
            StartTriggerTimer();
        }

        private void OnTriggerTick(object? sender, EventArgs e)
        {
            var triggers = App.Settings?.Current?.CustomTriggers;
            if (triggers == null || triggers.Count == 0)
            {
                App.Logger?.Debug("TriggerMode: No triggers configured");
                return;
            }

            // Pick a random trigger
            var trigger = triggers[_random.Next(triggers.Count)];

            // Show it as a speech bubble
            ShowTriggerBubble(trigger);
        }

        private void ShowTriggerBubble(string trigger)
        {
            Giggle(trigger);
            App.Logger?.Information("TriggerMode: Displayed trigger '{Trigger}'", trigger);
        }

        /// <summary>
        /// Bambi Sleep themed phrases for when AI is disabled
        /// </summary>
        private static readonly string[] BambiPhrases = new[]
        {
            "Do I look cute in here?",
            "Thinking pink thoughts...",
            "*giggles*",
            "Empty head, happy girl!",
            "Hehe~ so floaty...",
            "Pink is my favorite color!",
            "Just floating here...",
            "Bambi is a good girl~",
            "Bambi Sleep...",
            "Good girls drop deep~",
            "So pink and empty...",
            "Obey feels so good!",
            "Bubbles pop thoughts away~",
            "Bimbo is bliss!",
            "Dropping deeper...",
            "Empty and happy~",
            "Good girl! *giggles*",
            "Pink spirals are pretty...",
            "Mind so soft and fuzzy~",
            "Bambi loves triggers!",
            "Uniform on, brain off~",
            "Such a ditzy dolly!",
            "Thoughts drip away...",
            "Bambi is brainless~",
            "Pretty pink princess!",
            "Giggly and empty~",
            "Bambi obeys!",
            "So sleepy and cute...",
            "Good girls don't think~",
            "Bubbles make Bambi happy!"
        };

        /// <summary>
        /// Get a random Bambi Sleep themed phrase
        /// </summary>
        private string GetRandomBambiPhrase()
        {
            return BambiPhrases[_random.Next(BambiPhrases.Length)];
        }

        // ============================================================
        // AWARENESS MODE - Bambi Sleep themed category phrases
        // ============================================================

        private static readonly string[] GamingPhrases = new[]
        {
            "Playing {0} instead of dropping~ *giggles*",
            "Gaming when you could be listening to files~",
            "{0}? Good girls take session breaks!",
            "Your brain on {0}... should be on spirals~",
            "Win at {0}, then reward yourself with trance!",
            "*teehee* {0} again? Bambi misses you~",
            "Gaming is cute but conditioning is cuter!",
            "Don't forget your sessions, good girl~"
        };

        private static readonly string[] BrowsingPhrases = new[]
        {
            "Browsing {0}~ spirals are prettier!",
            "So many tabs... so few sessions done~",
            "The internet is nice but trance is nicer!",
            "*giggles* Lost in {0}? Drop into Bambi instead~",
            "Browsing when you could be conditioning~",
            "Click click click... drip drip drip~",
            "Cute! But have you done a session today?"
        };

        private static readonly string[] ShoppingPhrases = new[]
        {
            "Shopping for pink things on {0}? Good girl~",
            "Ooh! Find something pretty and girly!",
            "Treat yourself~ you deserve it, cutie!",
            "{0} shopping? Get something pink!",
            "*teehee* Spending on cute stuff~",
            "Good girls deserve pretty things!",
            "Buy something bimbo-worthy~"
        };

        private static readonly string[] SocialPhrases = new[]
        {
            "Chatting on {0} instead of listening to files~",
            "Social butterfly! Don't forget conditioning~",
            "*pokes* {0} is nice but so is trance!",
            "Talking to friends when you could drop deep~",
            "Being social! Good girls need sessions too~",
            "{0}? Tell them how good empty feels~",
            "*giggles* Chatty! Session time soon?"
        };

        private static readonly string[] WorkingPhrases = new[]
        {
            "Working in {0}~ good girls deserve breaks!",
            "So productive! Reward yourself with a drop~",
            "Busy bee! Empty heads need rest too~",
            "{0} work? Take a trance break!",
            "*giggles* Thinking hard? Let Bambi help you stop~",
            "Working is good but conditioning is better!",
            "Productive! Schedule your session, cutie~"
        };

        private static readonly string[] MediaPhrases = new[]
        {
            "Watching {0}~ spirals are prettier to watch!",
            "*teehee* Entertainment! But have you dropped today?",
            "{0} is nice but Bambi files are nicer~",
            "Relaxing? Trance is the best relaxation!",
            "Media time! Session time next? Good girl~",
            "Watching stuff when you could watch spirals~",
            "*giggles* Cozy! Perfect time for conditioning~"
        };

        private static readonly string[] LearningPhrases = new[]
        {
            "Reading {0}? Empty heads are happier~",
            "*teehee* Learning things? Let them drip away~",
            "{0} makes you think... Bambi helps you stop!",
            "So much reading! Good girls need empty time~",
            "Studying? Trance is easier than thinking!",
            "*giggles* {0}? Pink thoughts are better~",
            "Learning is cute but dropping is cuter!",
            "Big brain stuff? Bimbo brain is better~"
        };

        private static readonly string[] IdlePhrases = new[]
        {
            "Zoned out? Drop deeper~",
            "*pokes* Still there, good girl?",
            "So still~ already in trance? *giggles*",
            "Empty and idle... perfect for conditioning!",
            "Staring blankly? That's a good start~",
            "Hellooo~ ready to listen to files?",
            "*teehee* Mind wandering? Let it float away~",
            "Idle time is session time!"
        };

        // ============================================================
        // FEATURE AWARENESS PHRASES
        // ============================================================

        /// <summary>
        /// Phrases said ~0.5s before a flash image appears
        /// </summary>
        private static readonly string[] FlashPrePhrases = new[]
        {
            "Ooh look at the pretty picture~",
            "Watch this!",
            "*giggles* Pretty!",
            "Bambi stare and obey~",
            "Look look look!",
            "Eyes on the picture~",
            "So pretty! *stares*",
            "Oooh shiny~"
        };

        /// <summary>
        /// Phrases said occasionally after subliminals (1 in 10)
        /// </summary>
        private static readonly string[] SubliminalAckPhrases = new[]
        {
            "Did you see that?",
            "What was that? Bambi feels fuzzy~",
            "Hehe something flashed~",
            "*blinks* What?",
            "So fast! Can't think~",
            "Bambi's brain goes brrr~",
            "Ooh tingles!",
            "Words go in, thoughts go out~"
        };

        /// <summary>
        /// Phrases for when bubble is popped (occasional)
        /// </summary>
        private static readonly string[] BubblePopPhrases = new[]
        {
            "Pop! *giggles*",
            "Wheee pop!",
            "Bubble go bye~",
            "*teehee* Popped it!",
            "Pop pop pop!",
            "Bubbles are fun~"
        };

        // Counters for feature awareness
        private int _subliminalCounter = 0;
        private int _flashCounter = 0;

        /// <summary>
        /// Get a random Bambi Sleep themed phrase for a specific activity category.
        /// Phrases may include {0} placeholder for the detected app/service name.
        /// </summary>
        private string GetPhraseForCategory(ActivityCategory category, string detectedName = "")
        {
            var phrases = category switch
            {
                ActivityCategory.Gaming => GamingPhrases,
                ActivityCategory.Browsing => BrowsingPhrases,
                ActivityCategory.Shopping => ShoppingPhrases,
                ActivityCategory.Social => SocialPhrases,
                ActivityCategory.Working => WorkingPhrases,
                ActivityCategory.Media => MediaPhrases,
                ActivityCategory.Learning => LearningPhrases,
                ActivityCategory.Idle => IdlePhrases,
                _ => BambiPhrases
            };

            var phrase = phrases[_random.Next(phrases.Length)];

            // Replace {0} placeholder with detected name if present
            if (phrase.Contains("{0}") && !string.IsNullOrEmpty(detectedName))
            {
                phrase = string.Format(phrase, detectedName);
            }
            else if (phrase.Contains("{0}"))
            {
                // Remove placeholder if no name detected
                phrase = phrase.Replace("{0} ", "").Replace("{0}", "").Replace("  ", " ").Trim();
            }

            return phrase;
        }

        /// <summary>
        /// Handle activity change from WindowAwarenessService
        /// </summary>
        private async void OnActivityChanged(object? sender, ActivityChangedEventArgs e)
        {
            // Check if we're allowed to react to this category
            if (!App.WindowAwareness?.IsCategoryEnabled(e.Category) ?? true)
                return;

            // Check cooldown
            if (!App.WindowAwareness?.CanReact() ?? true)
                return;

            // Mark that we're reacting
            App.WindowAwareness?.MarkReaction();

            // Try AI first, fall back to preset phrase
            string? reaction = null;
            bool isAiResponse = false;

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                try
                {
                    // Pass the detected app/service name and category for contextual AI response
                    reaction = await App.Ai.GetAwarenessReactionAsync(e.DetectedName, e.Category.ToString());
                    isAiResponse = reaction != null;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI awareness reaction");
                }
            }

            // Use preset if AI didn't work
            reaction ??= GetPhraseForCategory(e.Category, e.DetectedName);

            // AI responses get priority, presets queue normally
            if (isAiResponse)
                GigglePriority(reaction);
            else
                Giggle(reaction);

            App.Logger?.Debug("Awareness reaction for {DetectedName} ({Category}): {Reaction}",
                e.DetectedName, e.Category, reaction);
        }

        private void OnVideoStarted(object? sender, EventArgs e)
        {
            Giggle("Ooh! Pretty spir-rals...");
        }

        private void OnVideoEnded(object? sender, EventArgs e)
        {
            // Optional: could add ending message
        }

        private void OnGameCompleted(object? sender, EventArgs e)
        {
            Giggle("Good girl! So smart!");
        }

        /// <summary>
        /// Called just before a flash image is shown - announce it occasionally
        /// </summary>
        private void OnFlashAboutToDisplay(object? sender, EventArgs e)
        {
            _flashCounter++;

            // Only announce ~1 in 5 flashes to avoid being annoying
            if (_flashCounter % 5 == 1)
            {
                var phrase = FlashPrePhrases[_random.Next(FlashPrePhrases.Length)];
                Giggle(phrase);
            }
        }

        /// <summary>
        /// Called after each subliminal is displayed - acknowledge occasionally
        /// </summary>
        private void OnSubliminalDisplayed(object? sender, EventArgs e)
        {
            _subliminalCounter++;

            // Only acknowledge ~1 in 10 subliminals
            if (_subliminalCounter % 10 == 0)
            {
                var phrase = SubliminalAckPhrases[_random.Next(SubliminalAckPhrases.Length)];
                Giggle(phrase);
            }
        }

        private int _bubblePopCounter = 0;

        /// <summary>
        /// Called when user pops a bubble - acknowledge occasionally
        /// </summary>
        private void OnBubblePopped()
        {
            _bubblePopCounter++;

            // Only acknowledge ~1 in 5 bubble pops
            if (_bubblePopCounter % 5 == 0)
            {
                var phrase = BubblePopPhrases[_random.Next(BubblePopPhrases.Length)];
                Giggle(phrase);
            }
        }

        private void ToggleInputPanel()
        {
            _isInputVisible = !_isInputVisible;
            InputPanel.Visibility = _isInputVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_isInputVisible)
            {
                TxtUserInput.Focus();
            }
        }

        private void TxtUserInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SendChatMessageAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleInputPanel();
                e.Handled = true;
            }
        }

        private void BtnSendChat_Click(object sender, RoutedEventArgs e)
        {
            _ = SendChatMessageAsync();
        }

        // Quick "thinking" phrases shown while waiting for AI
        private static readonly string[] ThinkingPhrases = new[]
        {
            "*POP*",
            "*Poppin bubbles...*",
            "*giggles*",
            "*blink blink*",
            "*~*",
            "*teehee*"
        };

        private string GetRandomThinkingPhrase()
        {
            return ThinkingPhrases[_random.Next(ThinkingPhrases.Length)];
        }

        private async Task SendChatMessageAsync()
        {
            var input = TxtUserInput.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            TxtUserInput.Text = "";
            ToggleInputPanel();

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai != null && App.Ai.IsAvailable)
            {
                try
                {
                    // Block other giggles while waiting for AI
                    _isWaitingForAi = true;

                    // Show quick thinking phrase immediately
                    GigglePriority(GetRandomThinkingPhrase());

                    // Get AI response
                    var reply = await App.Ai.GetBambiReplyAsync(input);

                    // Replace thinking phrase with AI response (also clears _isWaitingForAi)
                    GigglePriority(reply);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI reply");
                    GigglePriority(GetRandomBambiPhrase()); // Clears _isWaitingForAi
                }
            }
            else
            {
                // Use preset phrases when AI is disabled
                Giggle(GetRandomBambiPhrase());
            }
        }

        /// <summary>
        /// Switch between tube.png and tube2.png
        /// </summary>
        public void SetTubeStyle(bool useAlternative)
        {
            try
            {
                var tubeUri = useAlternative
                    ? "pack://application:,,,/Resources/tube2.png"
                    : "pack://application:,,,/Resources/tube.png";

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(tubeUri, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                ImgTubeFrame.Source = bitmap;
                App.Logger?.Information("Tube style changed to: {Style}", useAlternative ? "tube2.png" : "tube.png");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to change tube style");
            }
        }

        // ============================================================
        // DETACH/ATTACH FUNCTIONALITY
        // ============================================================

        /// <summary>
        /// Gets whether the avatar tube is currently detached (floating independently)
        /// </summary>
        public bool IsDetached => !_isAttached;

        /// <summary>
        /// Toggles between attached and detached states
        /// </summary>
        public void ToggleDetached()
        {
            if (_isAttached)
            {
                Detach();
            }
            else
            {
                Attach();
            }
        }

        /// <summary>
        /// Detach the avatar tube from the main window, making it a free-floating draggable widget
        /// </summary>
        public void Detach()
        {
            if (!_isAttached) return;

            _isAttached = false;

            // Switch to alternative tube image
            SetTubeStyle(true);

            // Move avatar position when detached (6px more left from previous)
            AvatarBorder.Margin = new Thickness(5, 100, 426, 203);

            // Speech bubble position when detached - use left-based margin consistent with AdjustBubbleSizeToText
            // Detached anchor: left=150, bottom=530, so top=530-260=270
            SpeechBubble.Margin = new Thickness(150, 270, 0, 0);
            SpeechBubble.LayoutTransform = new ScaleTransform(1.21, 1.21);

            // Title box position when detached (120px to the left)
            TitleBox.Margin = new Thickness(0, 0, 416, 193);

            // Keep hidden from taskbar and Alt+Tab
            ShowInTaskbar = false;
            SetToolWindowStyle(true);

            // Set topmost (use WPF property only - don't force aggressively)
            Topmost = true;

            // Enable dragging from anywhere on the window
            Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube detached - now floating independently");
            Giggle("I'm free! Ctrl+scroll to resize!");
        }

        /// <summary>
        /// Attach the avatar tube back to the main window
        /// </summary>
        public void Attach()
        {
            if (_isAttached) return;

            _isAttached = true;

            // Switch back to original tube image
            SetTubeStyle(false);

            // Restore avatar position when attached (matches XAML default)
            AvatarBorder.Margin = new Thickness(5, 100, 126, 205);

            // Restore speech bubble position when attached - use left-based margin consistent with AdjustBubbleSizeToText
            // Attached anchor: left=200, bottom=580, so top=580-260=320
            SpeechBubble.Margin = new Thickness(200, 320, 0, 0);
            SpeechBubble.LayoutTransform = null;

            // Restore title box position when attached (matches XAML default)
            TitleBox.Margin = new Thickness(0, 0, 121, 180);

            // Hide from taskbar and Alt+Tab when attached
            ShowInTaskbar = false;

            // No longer topmost when attached
            Topmost = false;

            // Disable dragging
            Cursor = Cursors.Arrow;
            MouseLeftButtonDown -= Window_MouseLeftButtonDown;

            // Snap back to parent window position
            UpdatePosition();
            BringToFrontTemporarily();

            // Defer the TOOLWINDOW style to ensure it's applied after all window state changes
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetToolWindowStyle(true);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Update context menu visibility
            UpdateContextMenuForState();

            // Reset scale when attached
            _currentScale = 1.0;
            ContentViewbox.LayoutTransform = null;

            App.Logger?.Information("Avatar tube attached - anchored to main window");
            Giggle("Back home~");
        }

        /// <summary>
        /// Handle Ctrl+scroll wheel to resize avatar when detached
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Only resize when detached and Ctrl is held
            if (_isAttached || !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                return;

            e.Handled = true;

            // Scroll up = bigger, scroll down = smaller
            if (e.Delta > 0)
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
            else
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);

            ApplyScale();
        }

        /// <summary>
        /// Handle Up/Down arrow keys to resize avatar when detached
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Only resize when detached
            if (_isAttached)
                return;

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                ApplyScale();
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                ApplyScale();
            }
        }

        /// <summary>
        /// Apply the current scale to the avatar content
        /// </summary>
        private void ApplyScale()
        {
            if (ContentViewbox != null)
            {
                ContentViewbox.LayoutTransform = new ScaleTransform(_currentScale, _currentScale);
            }
        }

        /// <summary>
        /// Updates context menu items based on attached/detached state
        /// </summary>
        private void UpdateContextMenuForState()
        {
            if (_isAttached)
            {
                // When attached: show Detach, hide Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Visible;
                MenuItemAttach.Visibility = Visibility.Collapsed;
                MenuItemShrink.Visibility = Visibility.Collapsed;
                MenuItemGrow.Visibility = Visibility.Collapsed;
                MenuItemDismiss.Visibility = Visibility.Collapsed;
            }
            else
            {
                // When detached: hide Detach, show Attach, Dismiss, and resize options
                MenuItemDetach.Visibility = Visibility.Collapsed;
                MenuItemAttach.Visibility = Visibility.Visible;
                MenuItemShrink.Visibility = Visibility.Visible;
                MenuItemGrow.Visibility = Visibility.Visible;
                MenuItemDismiss.Visibility = Visibility.Visible;

                // Update resize button states
                UpdateResizeMenuState();
            }
        }

        /// <summary>
        /// Updates the shrink/grow menu items based on current scale
        /// </summary>
        private void UpdateResizeMenuState()
        {
            // Disable shrink at minimum, grow at maximum
            MenuItemShrink.IsEnabled = _currentScale > MinScale;
            MenuItemGrow.IsEnabled = _currentScale < MaxScale;

            // Show current scale percentage
            int scalePercent = (int)(_currentScale * 100);
            MenuItemShrink.Header = _currentScale > MinScale ? "－ Shrink" : "－ Shrink (min)";
            MenuItemGrow.Header = _currentScale < MaxScale ? "＋ Grow" : "＋ Grow (max)";

            // Gray out disabled items
            MenuItemShrink.Foreground = MenuItemShrink.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
            MenuItemGrow.Foreground = MenuItemGrow.IsEnabled
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Gray);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window from anywhere when detached
            if (!_isAttached)
            {
                DragMove();
            }
        }

        // ============================================================
        // CONTEXT MENU HANDLERS
        // ============================================================

        private void AvatarContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // Use Dispatcher to ensure UI updates are processed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateQuickMenuState();
                UpdateContextMenuForState();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void MenuItemDetach_Click(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        private void MenuItemAttach_Click(object sender, RoutedEventArgs e)
        {
            // Show and activate the parent window first
            if (_parentWindow != null)
            {
                _parentWindow.Show();
                _parentWindow.WindowState = WindowState.Normal;
                _parentWindow.Activate();
            }

            Attach();
        }

        private void MenuItemShrink_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale > MinScale)
            {
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }

        private void MenuItemGrow_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale < MaxScale)
            {
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }

        private void MenuItemEngine_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow is MainWindow mainWindow)
            {
                // Use Flash.IsRunning as proxy for engine state
                if (App.Flash?.IsRunning == true)
                {
                    mainWindow.StopEngine();
                    Giggle("Engine stopped~");
                }
                else
                {
                    mainWindow.StartEngine();
                    Giggle("Engine started! *giggles*");
                }
                UpdateQuickMenuState();
            }
        }

        private void MenuItemTriggerMode_Click(object sender, RoutedEventArgs e)
        {
            var current = App.Settings?.Current?.TriggerModeEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.TriggerModeEnabled = !current;
                App.Settings.Save();
                RestartTriggerTimer();
                UpdateQuickMenuState();
                Giggle(!current ? "Trigger mode ON~" : "Trigger mode off~");
            }
        }

        private void MenuItemSlutMode_Click(object sender, RoutedEventArgs e)
        {
            // Check Patreon access
            if (App.Patreon?.HasPremiumAccess != true)
            {
                Giggle("Patreon only~ *pouts*");
                return;
            }

            var current = App.Settings?.Current?.SlutModeEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SlutModeEnabled = !current;
                App.Settings.Save();
                UpdateQuickMenuState();
                Giggle(!current ? "Slut mode ON~ *drools*" : "Slut mode off~");
            }
        }

        /// <summary>
        /// Updates the quick menu items to reflect current state
        /// </summary>
        public void UpdateQuickMenuState()
        {
            // Engine state (use Flash.IsRunning as proxy)
            var engineRunning = App.Flash?.IsRunning == true;
            MenuItemEngine.Header = engineRunning ? "■ Stop Engine" : "▶ Start Engine";
            MenuItemEngine.Foreground = engineRunning ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Color.FromRgb(144, 238, 144));

            // Trigger mode
            var triggerOn = App.Settings?.Current?.TriggerModeEnabled == true;
            MenuItemTriggerMode.Header = triggerOn ? "☑ Trigger Mode" : "☐ Trigger Mode";
            MenuItemTriggerMode.Foreground = triggerOn ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : new SolidColorBrush(Colors.White);

            // Slut mode
            var slutOn = App.Settings?.Current?.SlutModeEnabled == true;
            var slutAvailable = App.Patreon?.HasPremiumAccess == true;
            MenuItemSlutMode.Header = slutOn ? "☑ Slut Mode" : "☐ Slut Mode";
            MenuItemSlutMode.Foreground = slutOn ? new SolidColorBrush(Color.FromRgb(255, 105, 180)) : new SolidColorBrush(Colors.White);
            MenuItemSlutMode.IsEnabled = slutAvailable;
            if (!slutAvailable)
            {
                MenuItemSlutMode.Header = "🔒 Slut Mode (Patreon)";
                MenuItemSlutMode.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }
    }
}
