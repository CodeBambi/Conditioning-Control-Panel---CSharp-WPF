using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// A clickable bubble that spawns near the avatar and floats upward
    /// </summary>
    internal class AvatarRandomBubble
    {
        private readonly Window _window;
        private readonly DispatcherTimer _animTimer;
        private readonly Random _random;
        private readonly Action _onPop;

        private double _posX, _posY;
        private double _startX;
        private double _speed;
        private double _timeAlive;
        private double _wobbleOffset;
        private double _angle;
        private double _scale = 1.0;
        private double _fadeAlpha = 1.0;
        private int _animType;
        private bool _isPopping;
        private bool _isAlive = true;

        private readonly Image _bubbleImage;
        private readonly int _size;
        private readonly double _screenTop;

        public AvatarRandomBubble(Point avatarScreenPos, Random random, Action onPop)
        {
            _random = random;
            _onPop = onPop;

            // Bubble properties - slightly smaller than game bubbles
            _size = random.Next(100, 150);
            _speed = 1.0 + random.NextDouble() * 1.0; // 1.0 to 2.0 pixels per frame
            _animType = random.Next(4);
            _wobbleOffset = random.NextDouble() * 100;
            _angle = random.Next(360);

            // Get DPI scale
            var dpiScale = GetDpiScale();

            // Position - start to the right of the avatar, at avatar's vertical center
            _startX = avatarScreenPos.X / dpiScale + 50 + random.Next(-30, 30);
            _posX = _startX;
            _posY = avatarScreenPos.Y / dpiScale;
            _screenTop = -_size - 50; // Off top of screen

            // Create bubble image
            _bubbleImage = new Image
            {
                Width = _size,
                Height = _size,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Cursor = Cursors.Hand,
                IsHitTestVisible = true
            };

            // Load bubble image from resources
            try
            {
                var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                var bubbleImg = new BitmapImage();
                bubbleImg.BeginInit();
                bubbleImg.UriSource = resourceUri;
                bubbleImg.CacheOption = BitmapCacheOption.OnLoad;
                bubbleImg.EndInit();
                bubbleImg.Freeze();
                _bubbleImage.Source = bubbleImg;
            }
            catch
            {
                // Fallback - create simple ellipse
                var drawing = new DrawingGroup();
                using (var ctx = drawing.Open())
                {
                    var gradientBrush = new RadialGradientBrush(
                        Color.FromArgb(180, 200, 220, 255),
                        Color.FromArgb(80, 255, 255, 255));
                    ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2),
                        new Point(_size / 2, _size / 2), _size / 2 - 5, _size / 2 - 5);
                }
                _bubbleImage.Source = new DrawingImage(drawing);
            }

            // Transform for rotation and scale
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new RotateTransform(0));
            _bubbleImage.RenderTransform = transformGroup;

            // Make bubble clickable
            _bubbleImage.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            // Create container grid with the bubble
            var grid = new Grid
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = true,
                Children = { _bubbleImage }
            };

            // Grid click as backup
            grid.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };

            // Create window
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Width = _size + 40,
                Height = _size + 40,
                Left = _posX + 2,
                Top = _posY - 20,
                Content = grid,
                Cursor = Cursors.Hand,
                IsHitTestVisible = true
            };

            // Window click as final backup
            _window.MouseLeftButtonDown += (s, e) => Pop();

            // Show window
            _window.Show();

            // Hide from Alt+Tab
            HideFromAltTab();

            // Animation timer (~20 FPS)
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animTimer.Tick += Animate;
            _animTimer.Start();
        }

        private void Animate(object? sender, EventArgs e)
        {
            if (!_isAlive) return;

            if (_isPopping)
            {
                // Pop animation - expand and fade
                _scale += 0.06;
                _fadeAlpha -= 0.1;
                _angle += 3;

                if (_fadeAlpha <= 0)
                {
                    Destroy();
                    return;
                }
            }
            else
            {
                // Normal float animation
                _timeAlive += 0.03;
                _posY -= _speed;

                // Wobble based on animation type
                double offset = 0;
                switch (_animType)
                {
                    case 0:
                        offset = Math.Sin(_timeAlive * 2) * 25;
                        _angle = (_angle + 0.5) % 360;
                        break;
                    case 1:
                        offset = Math.Sin(_timeAlive * 2.5) * 30;
                        _angle = (_angle + 0.2) % 360;
                        break;
                    case 2:
                        offset = Math.Cos(_timeAlive * 1.8) * 25;
                        _angle = (_angle - 1.0) % 360;
                        break;
                    case 3:
                        offset = Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15;
                        _angle = (_angle + 0.8) % 360;
                        break;
                }
                _posX = _startX + offset;

                // Check if floated off screen (destroy without callback)
                if (_posY < _screenTop)
                {
                    Destroy();
                    return;
                }
            }

            // Update visuals
            try
            {
                // Update scale wobble
                var wobble = 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
                var currentScale = _scale + wobble;

                if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st)
                    {
                        st.ScaleX = currentScale;
                        st.ScaleY = currentScale;
                    }
                    if (tg.Children[1] is RotateTransform rt)
                    {
                        rt.Angle = _angle;
                    }
                }

                _window.Opacity = _fadeAlpha;
                _window.Left = _posX + 2;
                _window.Top = _posY - 20;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AvatarRandomBubble animate error: {Error}", ex.Message);
                Destroy();
            }
        }

        public void Pop()
        {
            if (!_isAlive || _isPopping) return;
            _isPopping = true;
            _onPop?.Invoke();
        }

        private void Destroy()
        {
            if (!_isAlive) return;
            _isAlive = false;
            _animTimer.Stop();

            try { _window.Close(); } catch { }
        }

        #region Win32

        private double GetDpiScale()
        {
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        private void HideFromAltTab()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
            catch { }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        #endregion
    }
}
