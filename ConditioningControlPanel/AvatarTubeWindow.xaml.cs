using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

        // Companion speech and chat
        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _idleTimer;
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
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public AvatarTubeWindow(Window parentWindow)
        {
            InitializeComponent();

            _parentWindow = parentWindow;
            // Don't set Owner - it causes black window artifacts during minimize
            // We manage visibility manually via event handlers instead
            
            // Determine which avatar set to load based on player level
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            _currentAvatarSet = GetAvatarSetForLevel(playerLevel);
            
            // Load avatar poses for the appropriate set
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);
            
            // Set initial pose
            if (_avatarPoses.Length > 0)
            {
                ImgAvatar.Source = _avatarPoses[0];
            }
            
            // Setup pose switching timer
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
            
            // Keep z-order synced during any position change
            LocationChanged += (s, e) => SyncZOrder();

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

            // Start idle timer for random giggles
            StartIdleTimer();

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
            int newSet = GetAvatarSetForLevel(newLevel);
            
            if (newSet != _currentAvatarSet)
            {
                App.Logger?.Information("🎨 Avatar upgrade! Changing from set {OldSet} to set {NewSet} at level {Level}", 
                    _currentAvatarSet, newSet, newLevel);
                
                _currentAvatarSet = newSet;
                _avatarPoses = LoadAvatarPoses(newSet);
                
                // Reset to first pose of new set with a nice transition
                _currentPoseIndex = 0;
                
                if (_avatarPoses.Length > 0)
                {
                    // Fade transition to new avatar
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (s, args) =>
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                        ImgAvatar.BeginAnimation(OpacityProperty, fadeIn);
                    };
                    ImgAvatar.BeginAnimation(OpacityProperty, fadeOut);
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tubeHandle = new WindowInteropHelper(this).Handle;
            _parentHandle = new WindowInteropHelper(_parentWindow).Handle;

            // Hide from Alt+Tab by adding WS_EX_TOOLWINDOW style
            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // Calculate scale factor based on screen size and DPI
            CalculateScaleFactor();

            // Defer position update to ensure layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    SyncZOrder();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        private void SyncZOrder()
        {
            if (_tubeHandle == IntPtr.Zero || _parentHandle == IntPtr.Zero) return;

            // Place tube window directly AFTER (behind) the parent in z-order
            // This means: parent is in front, tube is immediately behind it
            // No other window can be between them
            SetWindowPos(_tubeHandle, _parentHandle, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Force the window to be always on top using Win32 API (more reliable than WPF Topmost)
        /// </summary>
        private void ForceTopmost(bool topmost)
        {
            if (_tubeHandle == IntPtr.Zero) return;

            var insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(_tubeHandle, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// Ensure the window is visible and on top when detached
        /// </summary>
        private void EnsureVisibleWhenDetached()
        {
            if (!_isAttached && _tubeHandle != IntPtr.Zero)
            {
                Show();
                Activate();
                ForceTopmost(true);
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
                SyncZOrder();
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
                                SyncZOrder();
                            }
                            else
                            {
                                ForceTopmost(true);
                            }
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
                        SyncZOrder();
                    }
                    else
                    {
                        ForceTopmost(true);
                    }
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
                    SyncZOrder();
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

                // Only update position and sync z-order if parent is visible
                if (_parentWindow != null && _parentWindow.IsVisible && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    SyncZOrder();
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

        // Base speech bubble dimensions (10% larger for better text containment)
        private const double BaseBubbleWidth = 310;
        private const double BaseBubbleHeight = 180;

        /// <summary>
        /// Display a speech bubble with text for 5 seconds
        /// </summary>
        public void Giggle(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TxtSpeech.Text = text;

                // Adjust bubble size based on text length
                AdjustBubbleSize(text);

                SpeechBubble.Visibility = Visibility.Visible;

                // Bring tube to front when attached so bubble is visible above main window
                if (_isAttached)
                {
                    BringToFrontTemporarily();
                }

                // Hide after 5 seconds
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _speechTimer.Tick += (s, e) =>
                {
                    _speechTimer.Stop();
                    SpeechBubble.Visibility = Visibility.Collapsed;

                    // Restore z-order behind main window when attached
                    if (_isAttached)
                    {
                        SyncZOrder();
                    }
                };
                _speechTimer.Start();

                // Reset idle timer when speaking
                ResetIdleTimer();

                App.Logger?.Debug("Companion says: {Text}", text);
            });
        }

        /// <summary>
        /// Adjusts the speech bubble size based on text length and estimated line count
        /// </summary>
        private void AdjustBubbleSize(string text)
        {
            // Estimate line count based on text length (rough approximation)
            // Using ~5 characters per line for shorter, more readable lines
            int charCount = text.Length;
            int estimatedLines = Math.Max(1, (int)Math.Ceiling(charCount / 5.0));

            // Cap at 5 lines max
            estimatedLines = Math.Min(estimatedLines, 5);

            // Scale bubble size based on line count
            double widthMultiplier = 1.0;
            double heightMultiplier = 1.0;

            switch (estimatedLines)
            {
                case 1:
                    widthMultiplier = 1.0;
                    heightMultiplier = 1.0;
                    break;
                case 2:
                    widthMultiplier = 1.1;
                    heightMultiplier = 1.2;
                    break;
                case 3:
                    widthMultiplier = 1.2;
                    heightMultiplier = 1.4;
                    break;
                case 4:
                    widthMultiplier = 1.3;
                    heightMultiplier = 1.6;
                    break;
                case 5:
                default:
                    widthMultiplier = 1.4;
                    heightMultiplier = 1.8;
                    break;
            }

            SpeechBubble.Width = BaseBubbleWidth * widthMultiplier;
            SpeechBubble.Height = BaseBubbleHeight * heightMultiplier;
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
                    Giggle("Thinking...");
                    var reply = await App.Ai.GetBambiReplyAsync(input);
                    Giggle(reply);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI reply");
                    Giggle(GetRandomBambiPhrase());
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

            // Move avatar to the left when detached (increase right margin)
            AvatarBorder.Margin = new Thickness(5, 100, 408, 175);

            // Speech bubble position when detached (150px higher than attached, 30px more left)
            SpeechBubble.Margin = new Thickness(0, 280, 160, 0);

            // Keep hidden from taskbar and Alt+Tab
            ShowInTaskbar = false;
            SetToolWindowStyle(true);

            // Bring window to front and keep it ALWAYS topmost when detached (use Win32 for reliability)
            Topmost = true;
            ForceTopmost(true);

            // Enable dragging from anywhere on the window
            Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Ensure we stay on top even when other windows are activated
            Deactivated += Window_Deactivated_StayOnTop;

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube detached - now floating independently");
            Giggle("I'm free! Drag me anywhere!");
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

            // Restore avatar position when attached (32px more to the right)
            AvatarBorder.Margin = new Thickness(5, 100, 126, 175);

            // Restore speech bubble position when attached (50px higher)
            SpeechBubble.Margin = new Thickness(0, 380, 30, 0);

            // Hide from taskbar and Alt+Tab when attached
            ShowInTaskbar = false;

            // No longer topmost when attached (use Win32 to clear topmost)
            Topmost = false;
            ForceTopmost(false);

            // Disable dragging
            Cursor = Cursors.Arrow;
            MouseLeftButtonDown -= Window_MouseLeftButtonDown;
            Deactivated -= Window_Deactivated_StayOnTop;

            // Snap back to parent window position
            UpdatePosition();
            SyncZOrder();

            // Defer the TOOLWINDOW style to ensure it's applied after all window state changes
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetToolWindowStyle(true);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Update context menu visibility
            UpdateContextMenuForState();

            App.Logger?.Information("Avatar tube attached - anchored to main window");
            Giggle("Back home~");
        }

        /// <summary>
        /// Updates context menu items based on attached/detached state
        /// </summary>
        private void UpdateContextMenuForState()
        {
            if (_isAttached)
            {
                // When attached: show Detach, hide Attach and Dismiss
                MenuItemDetach.Visibility = Visibility.Visible;
                MenuItemAttach.Visibility = Visibility.Collapsed;
                MenuItemDismiss.Visibility = Visibility.Collapsed;
            }
            else
            {
                // When detached: hide Detach, show Attach and Dismiss
                MenuItemDetach.Visibility = Visibility.Collapsed;
                MenuItemAttach.Visibility = Visibility.Visible;
                MenuItemDismiss.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window from anywhere when detached
            if (!_isAttached)
            {
                DragMove();
            }
        }

        private void Window_Deactivated_StayOnTop(object? sender, EventArgs e)
        {
            // Re-assert topmost when window loses focus to ensure it stays on top
            if (!_isAttached)
            {
                // Use Dispatcher to re-apply topmost after the deactivation completes
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isAttached)
                    {
                        ForceTopmost(true);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // ============================================================
        // CONTEXT MENU HANDLERS
        // ============================================================

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
    }
}
