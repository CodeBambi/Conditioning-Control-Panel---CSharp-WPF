using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bubble Counting Minigame - plays a video with bubbles to count, then asks for the total
/// Unlocks at Level 50
/// </summary>
public class BubbleCountService : IDisposable
{
    public enum Difficulty { Easy, Medium, Hard }
    
    private readonly Random _random = new();
    private DispatcherTimer? _schedulerTimer;
    private bool _isRunning;
    private bool _isBusy;
    private string _videosPath = "";
    
    public bool IsRunning => _isRunning;
    public bool IsBusy => _isBusy;
    
    public event EventHandler? GameCompleted;
    public event EventHandler? GameFailed;
    public event EventHandler? BubblePopped;

    public void Start()
    {
        if (_isRunning) return;
        
        var settings = App.Settings.Current;
        
        // Check level requirement (Level 50)
        if (settings.PlayerLevel < 50)
        {
            App.Logger?.Information("BubbleCountService: Level {Level} is below 50, not available", settings.PlayerLevel);
            return;
        }
        
        if (!settings.BubbleCountEnabled)
        {
            App.Logger?.Information("BubbleCountService: Disabled in settings");
            return;
        }
        
        _isRunning = true;
        _videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
        
        ScheduleNextGame();
        
        App.Logger?.Information("BubbleCountService started - {PerHour}/hour, Difficulty: {Diff}", 
            settings.BubbleCountFrequency, settings.BubbleCountDifficulty);
    }

    public void Stop()
    {
        _isRunning = false;
        _schedulerTimer?.Stop();
        _schedulerTimer = null;
        
        App.Logger?.Information("BubbleCountService stopped");
    }

    private void ScheduleNextGame()
    {
        if (!_isRunning) return;
        
        var settings = App.Settings.Current;
        if (!settings.BubbleCountEnabled) return;
        
        // Frequency is games per hour (1-10)
        var gamesPerHour = Math.Max(1, Math.Min(10, settings.BubbleCountFrequency));
        var baseInterval = 3600.0 / gamesPerHour;
        
        // Add ±20% variance
        var variance = baseInterval * 0.2;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(60, interval); // Minimum 1 minute between games
        
        _schedulerTimer?.Stop();
        _schedulerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _schedulerTimer.Tick += (s, e) =>
        {
            _schedulerTimer?.Stop();
            if (_isRunning && !_isBusy)
            {
                TriggerGame();
            }
            ScheduleNextGame();
        };
        _schedulerTimer.Start();
        
        App.Logger?.Debug("Next bubble count game in {Interval:F1} seconds", interval);
    }

    public void TriggerGame(bool forceTest = false)
    {
        // Allow forced test even when engine not running
        if (!forceTest && (!_isRunning || _isBusy)) return;
        if (_isBusy) return; // Still prevent double-triggering

        var settings = App.Settings.Current;

        // Level check - skip for forced tests
        if (!forceTest && settings.PlayerLevel < 50) return;

        // Check if another fullscreen interaction is active (video, lock card)
        // If so, queue this bubble count for later
        if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
        {
            App.InteractionQueue.TryStart(
                InteractionQueueService.InteractionType.BubbleCount,
                () => TriggerGame(forceTest),
                queue: true);
            return;
        }

        // Notify queue we're starting
        App.InteractionQueue?.TryStart(
            InteractionQueueService.InteractionType.BubbleCount,
            () => { }, // Already executing
            queue: false);

        _isBusy = true;

        // Ensure videos path is set (needed when testing without engine running)
        if (string.IsNullOrEmpty(_videosPath))
        {
            _videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
        }

        // Pause and clear bubble popping challenge to avoid confusion during counting
        App.Bubbles?.PauseAndClear();

        // Trigger Bambi Freeze subliminal+audio BEFORE bubble count game
        App.Subliminal?.TriggerBambiFreeze();

        // Small delay to let the freeze effect register before game starts
        Task.Delay(800).ContinueWith(_ =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Get a random video
                    var videoPath = GetRandomVideo();
                    if (string.IsNullOrEmpty(videoPath))
                    {
                        App.Logger?.Warning("BubbleCountService: No videos found");
                        _isBusy = false;
                        return;
                    }
                    
                    // Determine difficulty settings
                    var difficulty = (Difficulty)settings.BubbleCountDifficulty;
                    
                    // Show the game on all monitors
                    BubbleCountWindow.ShowOnAllMonitors(videoPath, difficulty, settings.BubbleCountStrictLock, OnGameComplete);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to start bubble count game");
                    _isBusy = false;
                }
            });
        });
    }

    private void OnGameComplete(bool success)
    {
        _isBusy = false;

        // Resume bubble popping challenge
        App.Bubbles?.Resume();

        // Notify InteractionQueue that bubble count is complete (triggers queued items)
        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);

        if (success)
        {
            App.Progression?.AddXP(100, XPSource.BubbleCount);
            GameCompleted?.Invoke(this, EventArgs.Empty);
            App.Logger?.Information("Bubble count game completed successfully! +100 XP");
        }
        else
        {
            GameFailed?.Invoke(this, EventArgs.Empty);
            App.Logger?.Information("Bubble count game failed");
        }
    }

    private string? GetRandomVideo()
    {
        try
        {
            if (!Directory.Exists(_videosPath)) return null;
            
            var videos = Directory.GetFiles(_videosPath)
                .Where(f => new[] { ".mp4", ".webm", ".avi", ".mkv" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();
            
            if (videos.Length == 0) return null;
            
            return videos[_random.Next(videos.Length)];
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to get random video");
            return null;
        }
    }

    /// <summary>
    /// Reset busy state - called when panic button force-closes windows
    /// </summary>
    public void ResetBusyState()
    {
        _isBusy = false;
        App.Logger?.Debug("BubbleCountService: Busy state reset");
    }

    /// <summary>
    /// Refresh schedule when settings change
    /// </summary>
    public void RefreshSchedule()
    {
        if (!_isRunning) return;
        ScheduleNextGame();
    }

    /// <summary>
    /// Refresh the videos path based on current settings.
    /// Call this after changing the custom assets path.
    /// </summary>
    public void RefreshVideosPath()
    {
        _videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
        Directory.CreateDirectory(_videosPath);
    }

    public void Dispose()
    {
        Stop();
    }
}
