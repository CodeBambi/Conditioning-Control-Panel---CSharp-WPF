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
        private readonly BitmapImage[] _avatarPoses;
        private int _currentPoseIndex = 0;
        private bool _isAttached = true;
        private Storyboard? _floatStoryboard;
        private IntPtr _tubeHandle;
        private IntPtr _parentHandle;

        // ============================================================
        // POSITIONING & SCALING - ADJUST THESE VALUES AS NEEDED
        // ============================================================
        
        // Gap between tube window and main window (negative = overlap)
        private const double OffsetFromParent = -350;
        
        // Vertical offset from center (positive = lower, negative = higher)
        private const double VerticalOffset = 20;
        
        // Floating animation settings
        private const double FloatDistance = 8;
        private const double FloatDuration = 2.0;

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
            
            // Load avatar poses
            _avatarPoses = LoadAvatarPoses();
            
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
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tubeHandle = new WindowInteropHelper(this).Handle;
            _parentHandle = new WindowInteropHelper(_parentWindow).Handle;
            
            UpdatePosition();
            StartFloatingAnimation();
            SyncZOrder();
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

        private BitmapImage[] LoadAvatarPoses()
        {
            var poses = new BitmapImage[4];
            
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Resources/avatar_pose{i + 1}.png", UriKind.Absolute);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    poses[i] = bitmap;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to load avatar pose {Index}: {Error}", i + 1, ex.Message);
                    poses[i] = new BitmapImage();
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
            
            // Position to the LEFT of the parent window
            Left = _parentWindow.Left - Width - OffsetFromParent;
            Top = _parentWindow.Top + (_parentWindow.ActualHeight - Height) / 2 + VerticalOffset;
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
