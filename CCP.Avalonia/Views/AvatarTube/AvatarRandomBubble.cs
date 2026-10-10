using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of WPF <c>AvatarTube/AvatarRandomBubble.cs</c>: a clickable bubble that spawns to the right
    /// of the avatar and floats up off the screen, popping on click. Same numbers: size 100-149,
    /// 1-2 DIP per 50 ms tick, four sway patterns, a 0.06 wobble, pop = +0.06 scale / -0.1 alpha /
    /// +3 degrees a tick. Windows are a fixed 200x200, pooled (max 6) and reused, as WPF.
    /// </summary>
    internal sealed class AvatarRandomBubble
    {
        private const int WindowDim = 200;
        private const int PoolMax = 6;
        private static readonly Stack<Window> Pool = new();

        private readonly Window _window;
        private readonly DispatcherTimer _animTimer;
        private readonly Action _onPop;
        private readonly Image _bubbleImage;
        private readonly ScaleTransform _scaleTf = new(1, 1);
        private readonly RotateTransform _rotateTf = new(0);
        private readonly double _scaling;
        private readonly int _size, _animType;
        private readonly double _startX, _speed, _wobbleOffset, _screenTop;
        private double _posX, _posY, _timeAlive, _angle, _scale = 1.0, _fadeAlpha = 1.0;
        private bool _isPopping, _isAlive = true;

        /// <param name="avatarCenterDip">The avatar's centre in desktop DIPs (screen px / scaling).</param>
        public AvatarRandomBubble(Point avatarCenterDip, double scaling, Random random, Action onPop)
        {
            _onPop = onPop;
            _scaling = scaling > 0 ? scaling : 1.0;
            _size = random.Next(100, 150);
            _speed = 1.0 + random.NextDouble() * 1.0;
            _animType = random.Next(4);
            _wobbleOffset = random.NextDouble() * 100;
            _angle = random.Next(360);
            _startX = avatarCenterDip.X + 50 + random.Next(-30, 30);
            _posX = _startX;
            _posY = avatarCenterDip.Y;
            _screenTop = -_size - 50;

            _bubbleImage = new Image
            {
                Width = _size,
                Height = _size,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = RelativePoint.Center,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                RenderTransform = new TransformGroup { Children = { _scaleTf, _rotateTf } },
            };
            Bitmap? art = null;
            try { art = Helpers.ModArt.TryLoad("bubble.png"); } catch { }
            if (art != null) _bubbleImage.Source = art;

            Control body = _bubbleImage;
            if (art == null)
            {
                _bubbleImage.RenderTransform = null;   // the transforms move to the fallback shape
                // WPF fallback drawing: a soft radial ellipse with a white rim.
                body = new global::Avalonia.Controls.Shapes.Ellipse
                {
                    Width = _size - 10,
                    Height = _size - 10,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(180, 200, 220, 255), 0),
                            new GradientStop(Color.FromArgb(80, 255, 255, 255), 1),
                        },
                    },
                    RenderTransformOrigin = RelativePoint.Center,
                    RenderTransform = new TransformGroup { Children = { _scaleTf, _rotateTf } },
                };
            }

            var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Children = { body } };
            grid.PointerPressed += (_, e) => { Pop(); e.Handled = true; };

            _window = RentWindow();
            _window.Content = grid;
            _window.Opacity = 1;
            UpdateWindowPos();
            _window.Show();

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _animTimer.Tick += Animate;
            _animTimer.Start();
        }

        private void Animate(object? sender, EventArgs e)
        {
            if (!_isAlive) return;
            if (_isPopping)
            {
                _scale += 0.06;
                _fadeAlpha -= 0.1;
                _angle += 3;
                if (_fadeAlpha <= 0) { Destroy(); return; }
            }
            else
            {
                _timeAlive += 0.03;
                _posY -= _speed;
                double offset = 0;
                switch (_animType)
                {
                    case 0: offset = Math.Sin(_timeAlive * 2) * 25; _angle = (_angle + 0.5) % 360; break;
                    case 1: offset = Math.Sin(_timeAlive * 2.5) * 30; _angle = (_angle + 0.2) % 360; break;
                    case 2: offset = Math.Cos(_timeAlive * 1.8) * 25; _angle = (_angle - 1.0) % 360; break;
                    case 3: offset = Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15; _angle = (_angle + 0.8) % 360; break;
                }
                _posX = _startX + offset;
                if (_posY < _screenTop) { Destroy(); return; }
            }

            try
            {
                var s = _scale + 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
                _scaleTf.ScaleX = s;
                _scaleTf.ScaleY = s;
                _rotateTf.Angle = _angle;
                _window.Opacity = Math.Max(0, _fadeAlpha);
                UpdateWindowPos();
            }
            catch (Exception ex)
            {
                Log.Debug("AvatarRandomBubble animate error: {Error}", ex.Message);
                Destroy();
            }
        }

        public void Pop()
        {
            if (!_isAlive || _isPopping) return;
            _isPopping = true;
            try { _onPop(); } catch (Exception ex) { Log.Debug(ex, "AvatarRandomBubble pop handler failed"); }
        }

        private void Destroy()
        {
            if (!_isAlive) return;
            _isAlive = false;
            _animTimer.Stop();
            ReturnWindow();
        }

        /// <summary>WPF: Left = posX + size/2 - 78, Top = posY + size/2 - 100 (DIPs).</summary>
        private void UpdateWindowPos() =>
            _window.Position = new PixelPoint((int)((_posX + _size / 2.0 - 78) * _scaling),
                                              (int)((_posY + _size / 2.0 - 100) * _scaling));

        private static Window RentWindow()
        {
            if (Pool.Count > 0) return Pool.Pop();
            return new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Focusable = false,
                Width = WindowDim,
                Height = WindowDim,
                Cursor = new Cursor(StandardCursorType.Hand),
                Title = "CCP bubble",
            };
        }

        private void ReturnWindow()
        {
            var w = _window;
            try { w.Content = null; w.Opacity = 1; w.Hide(); } catch { }
            if (Pool.Count < PoolMax && !Pool.Contains(w)) { Pool.Push(w); return; }
            try { w.Close(); } catch { }
        }
    }
}
