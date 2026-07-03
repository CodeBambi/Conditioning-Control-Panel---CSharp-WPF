using System;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Services.BlinkTrainer;

/// <summary>
/// Avalonia implementation of gaze dwell / blink-pop for bubbles and flash images.
/// Subscribes to the shared <see cref="IWebcamService"/> gaze stream.
/// </summary>
public sealed class AvaloniaGazeFocusService : IGazeFocusService, IDisposable
{
    private const int DefaultDwellMs = 1000;
    private const int CooldownMs = 250;
    private const int TickMs = 33; // ~30 FPS
    private const int GazeRectRadiusPx = 60;

    private readonly IWebcamService _webcam;
    private readonly IBubbleService _bubbles;
    private readonly IFlashService _flash;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaGazeFocusService>? _logger;

    private DispatcherTimer? _timer;
    private Point? _lastGazePoint;
    private bool _faceLost;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private DateTime _dwellStartedAt;
    private bool _dwelling;
    private bool _subscribed;

    public bool IsActive { get; private set; }
    public int DwellMs { get; set; } = DefaultDwellMs;

    public event Action<bool>? OnActiveChanged;
    public event Action? GazePopped;

    public AvaloniaGazeFocusService(
        IWebcamService webcam,
        IBubbleService bubbles,
        IFlashService flash,
        ISettingsService settings,
        IScreenProvider screens,
        ILogger<AvaloniaGazeFocusService>? logger = null)
    {
        _webcam = webcam;
        _bubbles = bubbles;
        _flash = flash;
        _settings = settings;
        _logger = logger;
    }

    public bool Start()
    {
        if (IsActive) return true;

        if (!_webcam.IsRunning)
        {
            _webcam.StartTracking();
            if (!_webcam.IsRunning)
            {
                _logger?.LogInformation("GazeFocusService: cannot start — webcam not running");
                return false;
            }
        }

        Subscribe();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += OnTick;
        _timer.Start();

        IsActive = true;
        try { OnActiveChanged?.Invoke(true); } catch { }
        _logger?.LogInformation("GazeFocusService: active");
        return true;
    }

    public void Stop()
    {
        if (!IsActive && !_subscribed) return;
        Unsubscribe();

        _timer?.Stop();
        if (_timer != null) _timer.Tick -= OnTick;
        _timer = null;

        _lastGazePoint = null;
        _faceLost = false;
        _dwelling = false;
        _cooldownUntil = DateTime.MinValue;

        IsActive = false;
        try { OnActiveChanged?.Invoke(false); } catch { }
        _logger?.LogInformation("GazeFocusService: inactive");
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _webcam.OnGazeMove += HandleGazeMove;
        _webcam.OnFaceLost += HandleFaceLost;
        _webcam.OnFaceFound += HandleFaceFound;
        _webcam.OnBlink += HandleBlink;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _webcam.OnGazeMove -= HandleGazeMove;
        _webcam.OnFaceLost -= HandleFaceLost;
        _webcam.OnFaceFound -= HandleFaceFound;
        _webcam.OnBlink -= HandleBlink;
        _subscribed = false;
    }

    private void HandleGazeMove(Point p)
    {
        _lastGazePoint = p;
    }

    private void HandleFaceLost() => _faceLost = true;
    private void HandleFaceFound() => _faceLost = false;

    private void HandleBlink()
    {
        try
        {
            if (DateTime.UtcNow < _cooldownUntil) return;
            if (_faceLost || !_lastGazePoint.HasValue) return;

            var rect = GazeRect(_lastGazePoint.Value);
            bool popped = false;

            try
            {
                if (_bubbles.PopBubblesInRect(rect) > 0)
                {
                    popped = true;
                    GazePopped?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Gaze blink-pop bubble failed");
            }

            try
            {
                if (_flash.GazePop(rect))
                    popped = true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Gaze blink-pop flash failed");
            }

            if (popped)
                _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GazeFocusService blink handler error");
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (DateTime.UtcNow < _cooldownUntil)
            {
                _dwelling = false;
                return;
            }

            if (_faceLost || !_lastGazePoint.HasValue)
            {
                _dwelling = false;
                return;
            }

            var rect = GazeRect(_lastGazePoint.Value);

            if (!_dwelling)
            {
                _dwelling = true;
                _dwellStartedAt = DateTime.UtcNow;
                return;
            }

            var elapsedMs = (DateTime.UtcNow - _dwellStartedAt).TotalMilliseconds;
            if (elapsedMs < DwellMs) return;

            bool popped = false;
            try
            {
                if (_bubbles.PopBubblesInRect(rect) > 0)
                {
                    popped = true;
                    GazePopped?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Gaze dwell bubble pop failed");
            }

            try
            {
                if (_flash.GazePop(rect))
                    popped = true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Gaze dwell flash pop failed");
            }

            _dwelling = false;
            if (popped)
                _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GazeFocusService tick error");
        }
    }

    /// <summary>
    /// The pop target around the gaze point, in PHYSICAL screen pixels — the gaze pipeline
    /// projects to physical coordinates and the compositor layers (BubbleLayer/FlashLayer)
    /// store physical virtual-desktop px (IAvaloniaLayer contract), so the rect is passed
    /// through with NO DIP conversion. The old divide-by-scaling step made gaze pops miss
    /// their targets on any monitor scaled above 100%.
    /// </summary>
    private PixelRect GazeRect(Point gazePx)
    {
        return new PixelRect(
            gazePx.X - GazeRectRadiusPx,
            gazePx.Y - GazeRectRadiusPx,
            GazeRectRadiusPx * 2,
            GazeRectRadiusPx * 2);
    }

    public void Dispose() => Stop();
}
