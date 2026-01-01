using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bouncing Text - DVD screensaver style text that bounces across screens
/// Unlocks at Level 60, awards 10 XP per bounce
/// </summary>
public class BouncingTextService : IDisposable
{
    private readonly Random _random = new();
    private readonly List<BouncingTextWindow> _windows = new();
    private DispatcherTimer? _animTimer;
    private bool _isRunning;
    
    // Current text state
    private string _currentText = "";
    private double _posX, _posY;
    private double _velX, _velY;
    private double _totalWidth, _totalHeight;
    private double _minX, _minY, _maxX, _maxY;
    private Color _currentColor;
    
    // Text size - base size that gets scaled by settings
    private const int BASE_FONT_SIZE = 48;
    private double _textWidth = 200;
    private double _textHeight = 60;
    private int _currentFontSize = BASE_FONT_SIZE;
    
    // Corner hit detection - tolerance in pixels (corners are hard to hit exactly)
    private const double CORNER_TOLERANCE = 15.0;
    
    public bool IsRunning => _isRunning;
    
    public event EventHandler? OnBounce;

    public void Start()
    {
        if (_isRunning) return;
        
        var settings = App.Settings.Current;
        
        // Check level requirement (Level 60)
        if (settings.PlayerLevel < 60)
        {
            App.Logger?.Information("BouncingTextService: Level {Level} is below 60, not available", settings.PlayerLevel);
            return;
        }
        
        // Note: We don't check BouncingTextEnabled here because Start() is called
        // explicitly when we want to start (either by toggle or by session)
        
        _isRunning = true;
        
        // Calculate font size based on settings (50-300% of base)
        _currentFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        
        // Get random text from pool
        SelectRandomText();
        
        // Measure actual text size
        MeasureTextSize();
        
        // Calculate screen bounds
        CalculateScreenBounds(settings.DualMonitorEnabled);
        
        // Random starting position (ensure text starts fully within bounds)
        _posX = _minX + _random.NextDouble() * Math.Max(1, (_maxX - _minX - _textWidth));
        _posY = _minY + _random.NextDouble() * Math.Max(1, (_maxY - _minY - _textHeight));
        
        // Random velocity (speed based on setting)
        var speed = settings.BouncingTextSpeed / 10.0; // 1-10 maps to 0.1-1.0 multiplier
        var baseSpeed = 3.0 + _random.NextDouble() * 2.0; // 3-5 base speed
        _velX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
        _velY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
        
        // Random starting color
        _currentColor = GetRandomColor();
        
        // Create windows for each screen
        CreateWindows(settings.DualMonitorEnabled);
        
        // Start animation timer (~60 FPS)
        _animTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animTimer.Tick += Animate;
        _animTimer.Start();
        
        App.Logger?.Information("BouncingTextService started - Text: {Text}, Size: {W}x{H}", 
            _currentText, _textWidth, _textHeight);
    }

    public void Stop()
    {
        _isRunning = false;
        
        _animTimer?.Stop();
        _animTimer = null;
        
        // Always close and clear windows, even if we thought we weren't running
        foreach (var window in _windows)
        {
            try { window.Close(); } catch { }
        }
        _windows.Clear();
        
        App.Logger?.Information("BouncingTextService stopped");
    }

    private void SelectRandomText()
    {
        var settings = App.Settings.Current;
        var enabledTexts = settings.BouncingTextPool
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        
        if (enabledTexts.Count == 0)
        {
            _currentText = "GOOD GIRL";
        }
        else
        {
            _currentText = enabledTexts[_random.Next(enabledTexts.Count)];
        }
    }

    /// <summary>
    /// Measure the actual rendered size of the current text
    /// </summary>
    private void MeasureTextSize()
    {
        try
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var formattedText = new FormattedText(
                _currentText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _currentFontSize,
                Brushes.White,
                new NumberSubstitution(),
                VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
            
            _textWidth = formattedText.Width;
            _textHeight = formattedText.Height;
            
            App.Logger?.Debug("Measured text '{Text}': {W}x{H}", _currentText, _textWidth, _textHeight);
        }
        catch (Exception ex)
        {
            // Fallback to estimation if measurement fails
            _textWidth = _currentFontSize * _currentText.Length * 0.6;
            _textHeight = _currentFontSize * 1.2;
            App.Logger?.Warning(ex, "Failed to measure text, using estimate: {W}x{H}", _textWidth, _textHeight);
        }
    }

    private void CalculateScreenBounds(bool dualMonitor)
    {
        var screens = dualMonitor 
            ? System.Windows.Forms.Screen.AllScreens 
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
        
        // Get DPI scale
        var dpiScale = GetDpiScale();
        
        // Find total bounds across all screens
        _minX = screens.Min(s => s.Bounds.X) / dpiScale;
        _minY = screens.Min(s => s.Bounds.Y) / dpiScale;
        _maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width) / dpiScale;
        _maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height) / dpiScale;
        
        _totalWidth = _maxX - _minX;
        _totalHeight = _maxY - _minY;
    }

    private void CreateWindows(bool dualMonitor)
    {
        var screens = dualMonitor 
            ? System.Windows.Forms.Screen.AllScreens 
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
        
        foreach (var screen in screens)
        {
            var window = new BouncingTextWindow(screen, _currentFontSize);
            window.Show();
            _windows.Add(window);
        }
        
        // Update text in all windows
        UpdateWindowsText();
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        
        // Move
        _posX += _velX;
        _posY += _velY;
        
        bool bouncedX = false;
        bool bouncedY = false;
        
        // Calculate the RIGHT and BOTTOM edges of the text
        double textRight = _posX + _textWidth;
        double textBottom = _posY + _textHeight;
        
        // Bounce off LEFT edge (text's left edge hits screen's left edge)
        if (_posX <= _minX)
        {
            _posX = _minX;
            _velX = Math.Abs(_velX);
            bouncedX = true;
        }
        // Bounce off RIGHT edge (text's right edge hits screen's right edge)
        else if (textRight >= _maxX)
        {
            _posX = _maxX - _textWidth;
            _velX = -Math.Abs(_velX);
            bouncedX = true;
        }
        
        // Bounce off TOP edge (text's top edge hits screen's top edge)
        if (_posY <= _minY)
        {
            _posY = _minY;
            _velY = Math.Abs(_velY);
            bouncedY = true;
        }
        // Bounce off BOTTOM edge (text's bottom edge hits screen's bottom edge)
        else if (textBottom >= _maxY)
        {
            _posY = _maxY - _textHeight;
            _velY = -Math.Abs(_velY);
            bouncedY = true;
        }
        
        bool bounced = bouncedX || bouncedY;
        
        // Check for corner hit (both X and Y bounce at the same time!)
        if (bouncedX && bouncedY)
        {
            App.Logger?.Information("🎯 CORNER HIT! Position: ({X}, {Y})", _posX, _posY);
            App.Achievements?.TrackCornerHit();
        }
        // Also check for "near corner" hits - when very close to a corner during a single-axis bounce
        else if (bounced)
        {
            bool nearCorner = IsNearCorner(_posX, _posY, textRight, textBottom);
            if (nearCorner)
            {
                App.Logger?.Information("🎯 NEAR-CORNER HIT! Position: ({X}, {Y})", _posX, _posY);
                App.Achievements?.TrackCornerHit();
            }
        }
        
        // On bounce: change color, award XP, maybe change text
        if (bounced)
        {
            _currentColor = GetRandomColor();
            App.Progression?.AddXP(25);
            OnBounce?.Invoke(this, EventArgs.Empty);
            
            // 10% chance to change text on bounce
            if (_random.NextDouble() < 0.1)
            {
                SelectRandomText();
                MeasureTextSize(); // Re-measure when text changes
            }
            
            UpdateWindowsText();
            App.Logger?.Debug("Bounce! +25 XP");
        }
        
        // Update position in all windows
        UpdateWindowsPosition();
    }

    /// <summary>
    /// Check if the text is near any corner within tolerance
    /// </summary>
    private bool IsNearCorner(double left, double top, double right, double bottom)
    {
        // Top-left corner
        bool nearTopLeft = left <= _minX + CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE;
        // Top-right corner
        bool nearTopRight = right >= _maxX - CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE;
        // Bottom-left corner
        bool nearBottomLeft = left <= _minX + CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE;
        // Bottom-right corner
        bool nearBottomRight = right >= _maxX - CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE;
        
        return nearTopLeft || nearTopRight || nearBottomLeft || nearBottomRight;
    }

    private void UpdateWindowsText()
    {
        foreach (var window in _windows)
        {
            window.UpdateText(_currentText, _currentColor);
        }
    }

    private void UpdateWindowsPosition()
    {
        foreach (var window in _windows)
        {
            window.UpdatePosition(_posX, _posY, _textWidth, _textHeight);
        }
    }

    private Color GetRandomColor()
    {
        // Bright, vibrant colors
        var colors = new[]
        {
            Color.FromRgb(255, 105, 180), // Hot Pink
            Color.FromRgb(255, 20, 147),  // Deep Pink
            Color.FromRgb(138, 43, 226),  // Blue Violet
            Color.FromRgb(255, 0, 255),   // Magenta
            Color.FromRgb(0, 255, 255),   // Cyan
            Color.FromRgb(255, 255, 0),   // Yellow
            Color.FromRgb(0, 255, 0),     // Lime
            Color.FromRgb(255, 165, 0),   // Orange
            Color.FromRgb(255, 69, 0),    // Red Orange
            Color.FromRgb(50, 205, 50),   // Lime Green
        };
        return colors[_random.Next(colors.Length)];
    }

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

    /// <summary>
    /// Refresh when settings change
    /// </summary>
    public void Refresh()
    {
        if (!_isRunning) return;
        
        var settings = App.Settings.Current;
        
        // Update speed
        var speed = settings.BouncingTextSpeed / 10.0;
        var currentSpeed = Math.Sqrt(_velX * _velX + _velY * _velY);
        var targetSpeed = (3.0 + _random.NextDouble() * 2.0) * speed;
        var scale = targetSpeed / Math.Max(0.1, currentSpeed);
        _velX *= scale;
        _velY *= scale;
        
        // Check if font size changed - if so, update and re-measure
        var newFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        if (newFontSize != _currentFontSize)
        {
            _currentFontSize = newFontSize;
            MeasureTextSize(); // Re-measure with new font size
            
            // Update font size in all windows
            foreach (var window in _windows)
            {
                window.UpdateFontSize(_currentFontSize);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Transparent window that displays the bouncing text
/// </summary>
internal class BouncingTextWindow : Window
{
    private readonly TextBlock _textBlock;
    private readonly System.Windows.Forms.Screen _screen;
    private readonly double _dpiScale;

    public BouncingTextWindow(System.Windows.Forms.Screen screen, int fontSize = 48)
    {
        _screen = screen;
        _dpiScale = GetDpiScale();
        
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        
        // Cover the entire screen
        Left = screen.Bounds.X / _dpiScale;
        Top = screen.Bounds.Y / _dpiScale;
        Width = screen.Bounds.Width / _dpiScale;
        Height = screen.Bounds.Height / _dpiScale;
        
        // Create text block
        _textBlock = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.HotPink,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 3
            }
        };
        
        // Canvas for positioning
        var canvas = new Canvas();
        canvas.Children.Add(_textBlock);
        Content = canvas;
        
        // Make click-through
        SourceInitialized += (s, e) => MakeClickThrough();
    }

    public void UpdateText(string text, Color color)
    {
        _textBlock.Text = text;
        _textBlock.Foreground = new SolidColorBrush(color);
    }

    public void UpdateFontSize(int fontSize)
    {
        _textBlock.FontSize = fontSize;
    }

    public void UpdatePosition(double x, double y, double textWidth, double textHeight)
    {
        // Convert global position to local screen position
        var localX = x - (_screen.Bounds.X / _dpiScale);
        var localY = y - (_screen.Bounds.Y / _dpiScale);
        
        // Check if any part of the text is visible on this screen
        bool isVisible = (localX + textWidth >= 0) && 
                         (localX < Width) && 
                         (localY + textHeight >= 0) && 
                         (localY < Height);
        
        if (isVisible)
        {
            Canvas.SetLeft(_textBlock, localX);
            Canvas.SetTop(_textBlock, localY);
            _textBlock.Visibility = Visibility.Visible;
        }
        else
        {
            _textBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void MakeClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

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

    #region Win32

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    #endregion
}
