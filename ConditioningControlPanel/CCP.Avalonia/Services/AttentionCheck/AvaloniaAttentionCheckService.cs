using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Models;
using Animation = global::Avalonia.Animation.Animation;
using KeyFrame = global::Avalonia.Animation.KeyFrame;
using CorePoint = ConditioningControlPanel.Core.Platform.Point;

namespace ConditioningControlPanel.Avalonia.Services.AttentionCheck;

/// <summary>
/// Avalonia implementation of the "eye is watching" attention-check mechanic.
/// Schedules a calibration-style ring at a random screen position; the user must
/// fixate on it before the grace period elapses. Pass → XP, fail → configured penalty.
/// </summary>
public sealed class AvaloniaAttentionCheckService : IAttentionCheckService, IDisposable
{
    private const int TickMs = 33;
    private const int DwellTargetMs = 1000;
    private const double BoundsSlackDips = 30;
    private const double FirstFireMinSeconds = 30;
    private const double FirstFireMaxSeconds = 120;
    public const int PassXp = 20;
    public const int FailXpPenalty = 10;

    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IWebcamService _webcam;
    private readonly ISessionService _sessionService;
    private readonly IProgressionService _progression;
    private readonly ILockCardService _lockCard;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly ISfxPlayer? _sfxPlayer;
    private readonly ILogger<AvaloniaAttentionCheckService> _logger;
    private readonly Random _rng = new();

    private bool _isRunning;
    private bool _firstFireScheduled;
    private bool _gazeSubscribed;
    private DispatcherTimer? _scheduleTimer;
    private DispatcherTimer? _tickTimer;

    private readonly CompositorEngine? _compositor;
    private readonly AttentionCheckLayer? _layer;
    private bool _layerActive;
    private ConditioningControlPanel.Core.Platform.PixelRect _activeBounds;
    private DateTime _fireStartedAt;
    private double _dwellMs;
    private CorePoint? _lastGazePoint;
    private bool _disposed;

    public bool IsRunning => _isRunning;
    public event Action? OnPass;
    public event Action? OnFail;

    public AvaloniaAttentionCheckService(
        ISettingsService settings,
        IScreenProvider screens,
        IWebcamService webcam,
        ISessionService sessionService,
        IProgressionService progression,
        ILockCardService lockCard,
        IAudioPlayer? audioPlayer,
        ISfxPlayer? sfxPlayer,
        ILogger<AvaloniaAttentionCheckService> logger,
        CompositorEngine? compositor = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _webcam = webcam ?? throw new ArgumentNullException(nameof(webcam));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _lockCard = lockCard ?? throw new ArgumentNullException(nameof(lockCard));
        _audioPlayer = audioPlayer;
        _sfxPlayer = sfxPlayer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _compositor = compositor;
        if (compositor != null)
        {
            _layer = new AttentionCheckLayer();
            compositor.RegisterLayer(_layer);
        }
    }

    public void Start()
    {
        if (_isRunning || _disposed) return;
        _isRunning = true;
        _firstFireScheduled = false;
        _logger.LogDebug("AttentionCheck started");
        ScheduleNext();
    }

    public void Stop()
    {
        if (!_isRunning && !_layerActive) return;
        _isRunning = false;
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        Dispatcher.UIThread.Post(() => ResolveActive(passed: false, fireEvent: false));
        _logger.LogDebug("AttentionCheck stopped");
    }

    public void FireNow()
    {
        if (_disposed) return;
        if (!_isRunning) Start();
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        Dispatcher.UIThread.Post(Fire);
    }

    private void ScheduleNext()
    {
        if (!_isRunning || _disposed) return;

        var settings = _settings.Current;
        if (settings == null || !settings.AttentionCheckEnabled)
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            return;
        }

        TimeSpan interval;
        if (!_firstFireScheduled)
        {
            var seconds = FirstFireMinSeconds + _rng.NextDouble() * (FirstFireMaxSeconds - FirstFireMinSeconds);
            interval = TimeSpan.FromSeconds(seconds);
            _firstFireScheduled = true;
            _logger.LogDebug("AttentionCheck: first fire scheduled in {Seconds:F0}s", seconds);
        }
        else
        {
            var max = Math.Max(1, settings.AttentionCheckMaxPerSession);
            var min = Math.Max(1, Math.Min(max, settings.AttentionCheckMinPerSession));
            var minIntervalMin = 60.0 / max;
            var maxIntervalMin = 60.0 / min;
            var intervalMin = minIntervalMin + _rng.NextDouble() * (maxIntervalMin - minIntervalMin);
            interval = TimeSpan.FromMinutes(intervalMin);
            _logger.LogDebug("AttentionCheck: next fire scheduled in {Minutes:F1}min", intervalMin);
        }

        _scheduleTimer?.Stop();
        _scheduleTimer = StartOneShotTimer(interval, () => Dispatcher.UIThread.Post(Fire));
    }

    private void Fire()
    {
        try
        {
            if (!_isRunning || _disposed) return;

            var settings = _settings.Current;
            if (settings == null || !settings.AttentionCheckEnabled)
            {
                ScheduleNext();
                return;
            }

            if (settings.AttentionCheckScope == AppSettings.AttentionCheckScopeKind.DuringSessionsOnly
                && !IsSessionRunning())
            {
                _logger.LogDebug("AttentionCheck: fire skipped — scope=DuringSessionsOnly but no session running");
                ScheduleNext();
                return;
            }

            if (!_webcam.IsRunning)
            {
                _logger.LogDebug("AttentionCheck: fire skipped — webcam tracking not running");
                ScheduleNext();
                return;
            }

            if (_layerActive)
            {
                _logger.LogDebug("AttentionCheck: fire skipped — popup already active");
                ScheduleNext();
                return;
            }

            if (_layer == null)
            {
                _logger.LogDebug("AttentionCheck: fire skipped — no compositor layer available");
                ScheduleNext();
                return;
            }

            var screen = _screens.GetPrimaryScreen()
                ?? _screens.GetAllScreens().FirstOrDefault();
            if (screen == null)
            {
                ScheduleNext();
                return;
            }

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var marginPx = (int)(120 * scaling);
            var sizePx = (int)(84 * scaling);
            var slackPx = (int)(BoundsSlackDips * scaling);

            var working = screen.WorkingArea;
            var availableWidth = Math.Max(1, working.Width - marginPx * 2 - sizePx);
            var availableHeight = Math.Max(1, working.Height - marginPx * 2 - sizePx);
            var x = (int)(working.X + marginPx + _rng.Next((int)availableWidth));
            var y = (int)(working.Y + marginPx + _rng.Next((int)availableHeight));

            _activeBounds = new ConditioningControlPanel.Core.Platform.PixelRect(
                x - slackPx,
                y - slackPx,
                sizePx + slackPx * 2,
                sizePx + slackPx * 2);

            // UCE: the gaze target is a compositor layer (migrated from a standalone
            // click-through Window). Show at the target rect in physical px; the layer
            // pulses intrinsically and starts at progress 0.
            _layer?.Show(new ConditioningControlPanel.Core.Platform.PixelRect(x, y, sizePx, sizePx));
            _layerActive = true;

            _fireStartedAt = DateTime.UtcNow;
            _dwellMs = 0;
            _lastGazePoint = null;

            if (!_gazeSubscribed)
            {
                _webcam.OnGazeMove += HandleGazeMove;
                _gazeSubscribed = true;
            }

            _tickTimer?.Stop();
            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
            _tickTimer.Tick += OnTick;
            _tickTimer.Start();

            TryPlayPing();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AttentionCheck Fire failed");
            ResolveActive(passed: false, fireEvent: false);
            ScheduleNext();
        }
    }

    private void HandleGazeMove(CorePoint p)
    {
        _lastGazePoint = p;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null || !_layerActive) { ResolveActive(passed: false); return; }

            var elapsed = (DateTime.UtcNow - _fireStartedAt).TotalMilliseconds;
            var graceMs = settings.AttentionCheckGraceMs;

            if (_lastGazePoint.HasValue && Contains(_activeBounds, _lastGazePoint.Value.X, _lastGazePoint.Value.Y))
            {
                _dwellMs += TickMs;
                _layer?.SetProgress(_dwellMs / DwellTargetMs);

                if (_dwellMs >= DwellTargetMs)
                {
                    ResolveActive(passed: true);
                    return;
                }
            }
            else
            {
                _dwellMs = Math.Max(0, _dwellMs - TickMs * 2);
                _layer?.SetProgress(_dwellMs / DwellTargetMs);
            }

            if (elapsed >= graceMs)
            {
                ResolveActive(passed: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("AttentionCheck tick error: {Error}", ex.Message);
            ResolveActive(passed: false);
        }
    }

    private void ResolveActive(bool passed, bool fireEvent = true)
    {
        _tickTimer?.Stop();
        _tickTimer = null;

        if (_gazeSubscribed)
        {
            _webcam.OnGazeMove -= HandleGazeMove;
            _gazeSubscribed = false;
        }

        // UCE: dismiss the gaze-target layer with the WPF 180 ms opacity fade.
        _layer?.Hide(180);
        _layerActive = false;

        if (fireEvent)
        {
            if (passed)
            {
                OnPass?.Invoke();
                _progression.AddXP(PassXp, XPSource.AttentionCheck);
                _logger.LogInformation("AttentionCheck passed — +{Xp} XP", PassXp);
            }
            else
            {
                OnFail?.Invoke();
                ApplyFailPenalty();
            }
        }

        if (_isRunning) ScheduleNext();
    }

    private void ApplyFailPenalty()
    {
        var mode = _settings.Current?.AttentionCheckFailMode ?? AppSettings.AttentionCheckFailModeKind.XpPenalty;
        switch (mode)
        {
            case AppSettings.AttentionCheckFailModeKind.XpPenalty:
                _progression.AddXP(-FailXpPenalty, XPSource.AttentionCheck);
                _logger.LogInformation("AttentionCheck failed — {Xp} XP penalty", FailXpPenalty);
                break;
            case AppSettings.AttentionCheckFailModeKind.LockCard:
                _lockCard.ShowLockCard(customPhrase: "PAY ATTENTION", customRepeats: 3, customStrict: true, isTest: false);
                _logger.LogInformation("AttentionCheck failed — lock card shown");
                break;
            default:
                _logger.LogInformation("AttentionCheck failed — no penalty");
                break;
        }
    }

    private bool IsSessionRunning()
    {
        // The attention-check scope is satisfied when any conditioning session is active.
        return _sessionService?.State == SessionState.Running;
    }

    private void TryPlayPing()
    {
        try
        {
            var master = (_settings.Current?.MasterVolume ?? 100) / 100.0;
            var volume = (float)(master * 0.7);
            _sfxPlayer?.Play("attention_ping", volume);
        }
        catch { }
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private static bool Contains(ConditioningControlPanel.Core.Platform.PixelRect rect, double x, double y)
    {
        return x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;
    }
}
