using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Services;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using BubbleCountService = ConditioningControlPanel.Avalonia.Services.BubbleCountService;

namespace ConditioningControlPanel.Avalonia.Services.BubbleCount;

/// <summary>
/// Cross-platform bubble-count service for the Avalonia head.
/// Runs a scheduled bubble-counting minigame on the desktop shell.
/// </summary>
public sealed class AvaloniaBubbleCountService : IBubbleCountService
{
    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IProgressionService _progression;
    private readonly ILogger<AvaloniaBubbleCountService>? _logger;
    private readonly IBubbleService? _bubbles;

    private readonly Random _random = new();

    private bool _isRunning;
    private bool _isBusy;
    private bool _isResetting;
    private DispatcherTimer? _scheduleTimer;
    private DateTime _lastXpAwardTime = DateTime.MinValue;

    private static readonly TimeSpan GameXpCooldown = TimeSpan.FromMinutes(3);
    private static readonly string[] ValidVideoExtensions = { ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv" };

    public bool IsRunning => _isRunning;
    public bool IsBusy => _isBusy;

    public AvaloniaBubbleCountService(
        ISettingsService settings,
        IAppEnvironment environment,
        IProgressionService progression,
        ILogger<AvaloniaBubbleCountService>? logger = null,
        IBubbleService? bubbles = null)
    {
        _settings = settings;
        _environment = environment;
        _progression = progression;
        _logger = logger;
        _bubbles = bubbles;
    }

    public void Start()
    {
        if (_isRunning) return;

        if (!_settings.Current.BubbleCountEnabled)
        {
            _logger?.LogDebug("BubbleCount: disabled in settings");
            return;
        }

        _isRunning = true;
        ScheduleNextGame();
        _logger?.LogInformation("BubbleCount started - {PerHour}/hour", _settings.Current.BubbleCountFrequency);
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        ResetBusyState();

        _logger?.LogInformation("BubbleCount stopped");
    }

    public void TriggerGame(bool forceTest = false)
    {
        if (!forceTest && (!_isRunning || _isBusy)) return;
        if (_isBusy) return;

        _isBusy = true;
        _bubbles?.PauseAndClear();

        try
        {
            var videoPath = PickRandomVideo();
            if (string.IsNullOrEmpty(videoPath))
            {
                _logger?.LogWarning("BubbleCount: no videos found");
                _isBusy = false;
                _bubbles?.Resume();
                return;
            }

            var settings = _settings.Current;
            var difficulty = (BubbleCountService.Difficulty)settings.BubbleCountDifficulty;

            BubbleCountWindow.ShowOnAllMonitors(
                videoPath,
                difficulty,
                settings.BubbleCountStrictLock,
                OnGameComplete);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCount: failed to start game");
            _isBusy = false;
            _bubbles?.Resume();
        }
    }

    public void RefreshSchedule()
    {
        if (!_isRunning) return;

        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        ScheduleNextGame();
    }

    public void ResetBusyState()
    {
        if (_isResetting) return;
        _isResetting = true;
        try
        {
            _isBusy = false;
            BubbleCountWindow.ForceCloseAll();
            // Also close any fullscreen result window (strict mode hides its Esc escape);
            // WPF StopEngine force-closes it too (MainWindow.StartStop.cs:328).
            // ForceCloseAll marks the window completed before Close, so the
            // onComplete(false) path is not re-triggered.
            BubbleCountResultWindow.ForceCloseAll();
            _bubbles?.Resume();
            _logger?.LogDebug("BubbleCount: busy state reset");
        }
        finally
        {
            _isResetting = false;
        }
    }

    private void ScheduleNextGame()
    {
        if (!_isRunning) return;

        var settings = _settings.Current;
        if (!settings.BubbleCountEnabled) return;

        var gamesPerHour = Math.Clamp(settings.BubbleCountFrequency, 1, 10);
        var baseInterval = 3600.0 / gamesPerHour;
        var variance = baseInterval * 0.2;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(60, interval);

        _scheduleTimer?.Stop();
        _scheduleTimer = StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (_isRunning && !_isBusy)
            {
                TriggerGame();
            }
            ScheduleNextGame();
        });

        _logger?.LogDebug("BubbleCount: next game in {Interval:F1}s", interval);
    }

    private string? PickRandomVideo()
    {
        try
        {
            var primaryDir = Path.Combine(_environment.EffectiveAssetsPath, "videos");
            var fallbackDir = Path.Combine(AppContext.BaseDirectory, "assets", "videos");
            var baseFallbackDir = Path.Combine(AppContext.BaseDirectory, "videos");

            var files = EnumerateVideos(primaryDir);
            if (!files.Any())
                files = EnumerateVideos(fallbackDir);
            if (!files.Any())
                files = EnumerateVideos(baseFallbackDir);

            if (!files.Any())
            {
                _logger?.LogWarning("BubbleCount: no videos found in {Primary} or {Fallback}", primaryDir, fallbackDir);
                return null;
            }

            var index = _random.Next(files.Length);
            var video = files[index];
            _logger?.LogDebug("BubbleCount: selected video {Path}", Path.GetFileName(video));
            return video;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCount: failed to pick video");
            return null;
        }
    }

    private string[] EnumerateVideos(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();

        return Directory
            .EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => ValidVideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();
    }

    private void OnGameComplete(bool success)
    {
        try
        {
            if (success)
            {
                var now = DateTime.UtcNow;
                if (now - _lastXpAwardTime >= GameXpCooldown)
                {
                    var xp = BubbleCountService.ScaleXpByDuration(100);
                    _progression.AddXP(xp, XPSource.BubbleCount);
                    _lastXpAwardTime = now;
                    _logger?.LogInformation("BubbleCount completed! +{Xp} XP", xp);
                }
                else
                {
                    _logger?.LogDebug("BubbleCount completed but XP on cooldown");
                }
            }
            else
            {
                _logger?.LogInformation("BubbleCount game failed/skipped");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCount: error handling game completion");
        }
        finally
        {
            _isBusy = false;
            _bubbles?.Resume();
        }
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
}
