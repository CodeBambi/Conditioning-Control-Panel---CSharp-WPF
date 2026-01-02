using System;
using System.Runtime.InteropServices;
using System.Windows;
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

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint GW_HWNDPREV = 3;

        public AvatarTubeWindow(Window parentWindow)
        {
            InitializeComponent();
            
            _parentWindow = parentWindow;
            Owner = parentWindow;
            
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

            // Calculate scale factor based on screen size and DPI
            CalculateScaleFactor();

            UpdatePosition();
            StartFloatingAnimation();
            SyncZOrder();
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
            UpdatePosition();
            SyncZOrder();
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            switch (_parentWindow.WindowState)
            {
                case WindowState.Minimized:
                    Hide();
                    break;
                case WindowState.Normal:
                case WindowState.Maximized:
                    if (_parentWindow.IsVisible)
                    {
                        Show();
                        UpdatePosition();
                        SyncZOrder();
                    }
                    break;
            }
        }

        private void ParentWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && _parentWindow.WindowState != WindowState.Minimized)
            {
                Show();
                UpdatePosition();
                SyncZOrder();
            }
            else
            {
                Hide();
            }
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            if (_parentWindow.WindowState != WindowState.Minimized && _parentWindow.IsVisible)
            {
                Show();
                UpdatePosition();
                SyncZOrder();
            }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            Close();
        }

        // ============================================================
        // PUBLIC METHODS
        // ============================================================

        public void ShowTube()
        {
            Show();
            UpdatePosition();
            SyncZOrder();
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
            _poseTimer.Stop();
            StopFloatingAnimation();
            
            if (_parentWindow != null)
            {
                _parentWindow.LocationChanged -= ParentWindow_PositionChanged;
                _parentWindow.SizeChanged -= ParentWindow_PositionChanged;
                _parentWindow.StateChanged -= ParentWindow_StateChanged;
                _parentWindow.IsVisibleChanged -= ParentWindow_IsVisibleChanged;
                _parentWindow.Activated -= ParentWindow_Activated;
                _parentWindow.Closed -= ParentWindow_Closed;
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
    }
}
