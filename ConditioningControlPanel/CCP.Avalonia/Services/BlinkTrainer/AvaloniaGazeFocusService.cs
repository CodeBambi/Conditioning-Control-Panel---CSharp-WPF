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

    // Cold-start readiness poll (W-3/P-7): StartTracking() is fire-and-forget and
    // IsRunning flips asynchronously on the UI thread, so an immediate check always
    // reports "not running" on the first activation. We poll IsRunning with a bounded
    // DispatcherTimer instead of failing the first click. ~10s upper bound.
    private const int StartPollIntervalMs = 100;
    private const int StartPollMaxAttempts = 100;

    private readonly IWebcamService _webcam;
    private readonly IBubbleService _bubbles;
    private readonly IFlashService _flash;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaGazeFocusService>? _logger;

    private DispatcherTimer? _timer;
    private DispatcherTimer? _startPollTimer;
    private int _startPollAttempts;
    private bool _starting;
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

    /// <summary>
    /// Try to start dwell processing. Requires a loaded calibration (WPF parity:
    /// GazeFocusService.cs:106-108 refuses without App.Webcam.Calibration) and a
    /// running webcam. Returns false only when there is no calibration to map gaze
    /// with — the caller surfaces a "calibrate first" message in that case.
    /// </summary>
    /// <remarks>
    /// Cold-start race (W-3/P-7): the WPF head blocks on the synchronous
    /// <c>WebcamTrackingService.Start()</c> (see MainWindow.BlinkTrainer.cs:196-210,
    /// which even pre-warms it off the UI thread). The Avalonia seam only exposes
    /// fire-and-forget <see cref="IWebcamService.StartTracking"/>, so checking
    /// <see cref="IWebcamService.IsRunning"/> immediately after always fails on the
    /// first click. When the webcam is not yet running we kick the start off and
    /// poll readiness with a bounded DispatcherTimer, surfacing a "starting…" state
    /// meanwhile; this method returns true (request accepted) and activation
    /// completes asynchronously once the webcam reports ready. No Thread.Sleep on
    /// the UI thread.
    /// </remarks>
    public bool Start()
    {
        if (IsActive) return true;

        // Calibration prerequisite — checked FIRST so we neither start the webcam
        // pointlessly nor bounce the toggle on a cold-start timing hiccup. The
        // IWebcamService seam has no Calibration property; GetCalibratedScreen()
        // returns null exactly when no calibration is loaded (or the calibrated
        // monitor is gone), which is a reflection-free, decoupled calibration-
        // presence gate. If a HasCalibration property lands on the seam later, it
        // can be swapped in here.
        if (!HasCalibration())
        {
            _logger?.LogInformation("GazeFocusService: cannot start — no calibration loaded");
            return false;
        }

        if (_webcam.IsRunning)
        {
            Activate();
            return true;
        }

        _webcam.StartTracking();
        BeginActivateWhenReady();
        return true; // accepted; activation completes when the webcam reports ready
    }

    /// <summary>
    /// Reflection-free, decoupled calibration-presence check. WPF gates on
    /// <c>App.Webcam.Calibration != null</c>; the seam's
    /// <see cref="IWebcamService.GetCalibratedScreen"/> returns null in exactly the
    /// no-calibration case (and also when the calibrated monitor is no longer
    /// connected, where gaze interaction would not work anyway).
    /// </summary>
    private bool HasCalibration()
    {
        try { return _webcam.GetCalibratedScreen() != null; }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GazeFocusService: calibration presence check failed");
            return false;
        }
    }

    private void BeginActivateWhenReady()
    {
        if (_starting) return;
        _starting = true;
        _startPollAttempts = 0;
        _logger?.LogInformation("GazeFocusService: starting — waiting for webcam readiness…");
        _startPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StartPollIntervalMs) };
        _startPollTimer.Tick += OnStartPollTick;
        _startPollTimer.Start();
    }

    private void OnStartPollTick(object? sender, EventArgs e)
    {
        _startPollAttempts++;
        if (_webcam.IsRunning)
        {
            StopStartPoll();
            _starting = false;
            Activate();
            return;
        }

        if (_startPollAttempts >= StartPollMaxAttempts)
        {
            _logger?.LogInformation(
                "GazeFocusService: webcam did not become ready within {Sec}s — aborting start",
                (StartPollMaxAttempts * StartPollIntervalMs) / 1000.0);
            StopStartPoll();
            _starting = false;
            // We never reached IsActive; surface the state so observers (e.g. the
            // toggle VM) can reconcile. Firing false is harmless when never true.
            try { OnActiveChanged?.Invoke(false); } catch { }
        }
    }

    private void StopStartPoll()
    {
        if (_startPollTimer != null)
        {
            _startPollTimer.Stop();
            _startPollTimer.Tick -= OnStartPollTick;
        }
        _startPollTimer = null;
    }

    private void Activate()
    {
        if (IsActive) return;
        Subscribe();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += OnTick;
        _timer.Start();

        IsActive = true;
        try { OnActiveChanged?.Invoke(true); } catch { }
        _logger?.LogInformation("GazeFocusService: active");
    }

    public void Stop()
    {
        StopStartPoll();
        _starting = false;

        if (!IsActive && !_subscribed) return;
        Unsubscribe();

        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
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
