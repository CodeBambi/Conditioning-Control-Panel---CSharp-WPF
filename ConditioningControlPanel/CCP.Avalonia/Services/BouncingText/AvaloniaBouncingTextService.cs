using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.BouncingText;

/// <summary>
/// Avalonia implementation of the bouncing-text effect engine.
/// Renders a DVD-screensaver-style phrase via the unified compositor layer.
/// </summary>
public sealed class AvaloniaBouncingTextService : IBouncingTextService, IDisposable
{
    private const int BASE_FONT_SIZE = 72;
    private const double CORNER_TOLERANCE = 15.0;
    private static readonly TimeSpan BounceXpCooldown = TimeSpan.FromSeconds(2);
    private const int MaxBounceXpPerMinute = 150;

    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAchievementService _achievements;
    private readonly IProgressionService _progression;
    private readonly ILogger<AvaloniaBouncingTextService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly BouncingTextLayer? _bouncingTextLayer;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private string _currentText = "";
    private double _posX, _posY;
    private double _velX, _velY;
    private double _minX, _minY, _maxX, _maxY;
    private double _textWidth = 200;
    private double _textHeight = 60;
    private int _currentFontSize = BASE_FONT_SIZE;
    private int _currentOpacity = 100;
    private Color _currentColor = Colors.HotPink;
    private DateTime _lastBounceXpTime = DateTime.MinValue;
    private int _bounceXpThisMinute;
    private DateTime _bounceXpMinuteStart = DateTime.MinValue;

    public AvaloniaBouncingTextService(
        ISettingsService settings,
        IScreenProvider screens,
        IAchievementService achievements,
        IProgressionService progression,
        ILogger<AvaloniaBouncingTextService>? logger = null,
        CompositorEngine? compositor = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _logger = logger;
        _compositor = compositor;
        _bouncingTextLayer = compositor != null ? new BouncingTextLayer() : null;
        if (_bouncingTextLayer != null)
            _compositor?.RegisterLayer(_bouncingTextLayer);
        _timer.Tick += Animate;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? OnBounce;
    public event EventHandler? OnCornerHit;

    public void Start(IEnumerable<string>? textPool = null)
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaBouncingTextService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        var settings = _settings.Current;
        if (settings == null) return;

        IsRunning = true;
        _currentFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        _currentOpacity = settings.BouncingTextOpacity;

        SelectRandomText(textPool?.ToList());
        MeasureTextSize();
        CalculateScreenBounds(settings.DualMonitorEnabled);

        _posX = _minX + _random.NextDouble() * Math.Max(1, (_maxX - _minX - _textWidth));
        _posY = _minY + _random.NextDouble() * Math.Max(1, (_maxY - _minY - _textHeight));

        var speed = settings.BouncingTextSpeed / 10.0;
        var baseSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0;
        _velX = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);
        _velY = baseSpeed * speed * (_random.Next(2) == 0 ? 1 : -1);

        _currentColor = GetRandomColor();

        // Set layer bounds and add text
        if (_bouncingTextLayer != null)
        {
            _bouncingTextLayer.MinX = _minX;
            _bouncingTextLayer.MinY = _minY;
            _bouncingTextLayer.MaxX = _maxX;
            _bouncingTextLayer.MaxY = _maxY;
            _bouncingTextLayer.Clear();
            _bouncingTextLayer.AddText(_currentText, _currentColor, _currentFontSize, _currentOpacity / 100.0);
            _bouncingTextLayer.UpdatePosition(0, _posX, _posY);
        }

        _timer.Start();

        _logger?.LogInformation("AvaloniaBouncingTextService started - Text: {Text}, Size: {W}x{H}", _currentText, _textWidth, _textHeight);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _timer.Stop();
        _bouncingTextLayer?.Clear();
        _logger?.LogInformation("AvaloniaBouncingTextService stopped");
    }

    public void Refresh()
    {
        if (!IsRunning) return;
        var settings = _settings.Current;
        if (settings == null) return;

        var speed = settings.BouncingTextSpeed / 10.0;
        var currentSpeed = Math.Sqrt(_velX * _velX + _velY * _velY);
        var targetSpeed = (3.0 + _random.NextDouble() * 2.0) * 60.0 * speed;
        var scale = targetSpeed / Math.Max(0.1, currentSpeed);
        _velX *= scale;
        _velY *= scale;

        var newFontSize = (int)(BASE_FONT_SIZE * settings.BouncingTextSize / 100.0);
        if (newFontSize != _currentFontSize)
        {
            _currentFontSize = newFontSize;
            MeasureTextSize();
        }

        var newOpacity = settings.BouncingTextOpacity;
        if (newOpacity != _currentOpacity)
            _currentOpacity = newOpacity;
    }

    private void SelectRandomText(List<string>? pool = null)
    {
        var settings = _settings.Current;
        if (settings == null)
        {
            _currentText = "GOOD GIRL";
            return;
        }

        var enabledTexts = pool != null && pool.Count > 0
            ? pool
            : settings.BouncingTextPool
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

        _currentText = enabledTexts.Count > 0
            ? enabledTexts[_random.Next(enabledTexts.Count)]
            : "GOOD GIRL";
    }

    private void MeasureTextSize()
    {
        try
        {
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
            var ft = new FormattedText(
                _currentText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _currentFontSize,
                Brushes.White);

            _textWidth = ft.WidthIncludingTrailingWhitespace;
            _textHeight = ft.Height;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaBouncingTextService: failed to measure text, using estimate");
            _textWidth = _currentFontSize * _currentText.Length * 0.6;
            _textHeight = _currentFontSize * 1.2;
        }
    }

    private void CalculateScreenBounds(bool dualMonitor)
    {
        // PHYSICAL virtual-desktop pixels (IAvaloniaLayer contract): BouncingTextLayer items
        // live in the compositor's physical coordinate space, so the bounce box uses raw
        // screen bounds. The old divide-by-scaling produced a mixed-unit box that drifted
        // off-position on any monitor scaled above 100%.
        var screens = GetScreens(dualMonitor);
        _minX = screens.Min(s => s.Bounds.X);
        _minY = screens.Min(s => s.Bounds.Y);
        _maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
        _maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!IsRunning) return;
        const double dt = 1.0 / 60.0;

        _posX += _velX * dt;
        _posY += _velY * dt;

        var textRight = _posX + _textWidth;
        var textBottom = _posY + _textHeight;
        var bouncedX = false;
        var bouncedY = false;

        if (_posX <= _minX)
        {
            _posX = _minX;
            _velX = Math.Abs(_velX);
            bouncedX = true;
        }
        else if (textRight >= _maxX)
        {
            _posX = _maxX - _textWidth;
            _velX = -Math.Abs(_velX);
            bouncedX = true;
        }

        if (_posY <= _minY)
        {
            _posY = _minY;
            _velY = Math.Abs(_velY);
            bouncedY = true;
        }
        else if (textBottom >= _maxY)
        {
            _posY = _maxY - _textHeight;
            _velY = -Math.Abs(_velY);
            bouncedY = true;
        }

        var bounced = bouncedX || bouncedY;
        if (bouncedX && bouncedY)
        {
            _logger?.LogInformation("AvaloniaBouncingTextService: corner hit at ({X}, {Y})", _posX, _posY);
            _achievements.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);
        }
        else if (bounced && IsNearCorner(_posX, _posY, textRight, textBottom))
        {
            _logger?.LogInformation("AvaloniaBouncingTextService: near-corner hit at ({X}, {Y})", _posX, _posY);
            _achievements.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);
        }

        if (bounced)
        {
            _currentColor = GetRandomColor();
            var now = DateTime.UtcNow;
            if ((now - _bounceXpMinuteStart).TotalSeconds >= 60)
            {
                _bounceXpThisMinute = 0;
                _bounceXpMinuteStart = now;
            }
            if (now - _lastBounceXpTime >= BounceXpCooldown && _bounceXpThisMinute < MaxBounceXpPerMinute)
            {
                _progression.AddXP(15, XPSource.BouncingText);
                _lastBounceXpTime = now;
                _bounceXpThisMinute += 15;
            }
            OnBounce?.Invoke(this, EventArgs.Empty);

            if (_random.NextDouble() < 0.1)
            {
                SelectRandomText();
                MeasureTextSize();
                _bouncingTextLayer?.Clear();
                _bouncingTextLayer?.AddText(_currentText, _currentColor, _currentFontSize, _currentOpacity / 100.0);
            }
        }

        // Update compositor layer position
        _bouncingTextLayer?.UpdatePosition(0, _posX, _posY);
    }

    private bool IsNearCorner(double left, double top, double right, double bottom)
    {
        return (left <= _minX + CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE)
            || (right >= _maxX - CORNER_TOLERANCE && top <= _minY + CORNER_TOLERANCE)
            || (left <= _minX + CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE)
            || (right >= _maxX - CORNER_TOLERANCE && bottom >= _maxY - CORNER_TOLERANCE);
    }

    private Color GetRandomColor()
    {
        var colors = new[]
        {
            Colors.HotPink, Colors.DeepPink, Colors.BlueViolet,
            Colors.Magenta, Colors.Cyan, Colors.Yellow,
            Colors.Lime, Colors.Orange, Colors.OrangeRed, Colors.LimeGreen
        };
        return colors[_random.Next(colors.Length)];
    }

    private IReadOnlyList<ScreenInfo> GetScreens(bool dualMonitor)
    {
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };

            if (!dualMonitor)
            {
                var primary = _screens.GetPrimaryScreen() ?? all[0];
                return new[] { primary };
            }
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaBouncingTextService: could not enumerate screens: {Error}", ex.Message);
            return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
