using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using Screen = System.Windows.Forms.Screen;

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

    // Pack video support
    private List<string> _regularVideos = new();
    private List<(string PackId, PackFileEntry File)> _packVideos = new();
    private readonly List<string> _tempPackFiles = new();  // Track temp files for cleanup

    // Mercy/retry system (strict mode)
    private int _retryCount = 0;
    private readonly List<Window> _messageWindows = new();

    // Anti-exploit: cooldown between XP-awarding completions
    private DateTime _lastXpAwardTime = DateTime.MinValue;
    private static readonly TimeSpan GameXpCooldown = TimeSpan.FromMinutes(3);

    /// <summary>Minimum video duration (seconds) for full XP. Shorter videos scale proportionally.</summary>
    private const double FullXpVideoDurationSeconds = 60.0;
    
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
        if (!settings.IsLevelUnlocked(50))
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
        _retryCount = 0;
        _schedulerTimer?.Stop();
        _schedulerTimer = null;
        CloseMessageWindows();
        CleanupTempPackFiles();

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
        if (!forceTest && !settings.IsLevelUnlocked(50)) return;

        // Check if another fullscreen interaction is active (video, lock card)
        // If so, queue this bubble count for later
        // Note: If CurrentInteraction is already BubbleCount, the queue dequeued us — proceed normally
        var alreadyActive = App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.BubbleCount;
        if (!alreadyActive && App.InteractionQueue != null && !App.InteractionQueue.CanStart)
        {
            App.InteractionQueue.TryStart(
                InteractionQueueService.InteractionType.BubbleCount,
                () => TriggerGame(forceTest),
                queue: true);
            return;
        }

        // Notify queue we're starting (skip if queue already set us as active)
        if (!alreadyActive)
        {
            App.InteractionQueue?.TryStart(
                InteractionQueueService.InteractionType.BubbleCount,
                () => { }, // Already executing
                queue: false);
        }

        _isBusy = true;
        _retryCount = 0;

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
                        App.Bubbles?.Resume();
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                        return;
                    }
                    
                    // Determine difficulty settings
                    var difficulty = (Difficulty)settings.BubbleCountDifficulty;

                    // Track game started
                    App.Achievements?.TrackBubbleCountGameStarted();

                    // Show the game on all monitors
                    BubbleCountWindow.ShowOnAllMonitors(videoPath, difficulty, settings.BubbleCountStrictLock, OnGameComplete);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to start bubble count game");
                    _isBusy = false;
                    App.Bubbles?.Resume();
                    App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                }
            });
        });
    }

    /// <summary>
    /// Calculate XP scaled by video duration. Videos under 60s give proportionally less XP.
    /// </summary>
    internal static int ScaleXpByDuration(int baseXp)
    {
        var duration = BubbleCountWindow.LastVideoDurationSeconds;
        if (duration >= FullXpVideoDurationSeconds) return baseXp;
        var scale = Math.Max(0.1, duration / FullXpVideoDurationSeconds);
        return Math.Max(1, (int)(baseXp * scale));
    }

    private void OnGameComplete(bool success)
    {
        if (success)
        {
            _retryCount = 0;
            _isBusy = false;
            App.Bubbles?.Resume();
            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);

            var now = DateTime.UtcNow;
            if (now - _lastXpAwardTime >= GameXpCooldown)
            {
                var xp = ScaleXpByDuration(100);
                App.Progression?.AddXP(xp, XPSource.BubbleCount);
                _lastXpAwardTime = now;
                App.Logger?.Information("Bubble count game completed! +{Xp} XP (video {Duration:F0}s)", xp, BubbleCountWindow.LastVideoDurationSeconds);
            }
            else
            {
                App.Logger?.Debug("Bubble count completed but XP on cooldown ({Remaining:F0}s remaining)",
                    (GameXpCooldown - (now - _lastXpAwardTime)).TotalSeconds);
            }

            App.Quests?.TrackBubbleCountCompleted();
            GameCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var settings = App.Settings.Current;

            // Strict mode: retry the video (rewatch) with mercy escape
            if (settings.BubbleCountStrictLock)
            {
                _retryCount++;

                if (_retryCount >= 3 && settings.MercySystemEnabled)
                {
                    // Mercy after 3 retries - let them go
                    App.Logger?.Information("Bubble count mercy after {Retries} retries", _retryCount);
                    var mode = App.Settings?.Current?.ContentMode ?? Models.ContentMode.BambiSleep;
                    ShowFullscreenMessage(
                        Models.ContentModeConfig.GetAttentionCheckMercyMessage(mode),
                        2500,
                        () =>
                        {
                            _retryCount = 0;
                            _isBusy = false;
                            App.Bubbles?.Resume();
                            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                            GameFailed?.Invoke(this, EventArgs.Empty);
                        });
                }
                else
                {
                    // Replay - show message then start new video
                    App.Logger?.Information("Bubble count retry {Count} (mercy at 3)", _retryCount);
                    var mode = App.Settings?.Current?.ContentMode ?? Models.ContentMode.BambiSleep;
                    ShowFullscreenMessage(
                        Models.ContentModeConfig.GetBubbleCountRetryMessage(mode),
                        2000,
                        RetryGame);
                }
            }
            else
            {
                // Non-strict: just end the game
                _retryCount = 0;
                _isBusy = false;
                App.Bubbles?.Resume();
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                GameFailed?.Invoke(this, EventArgs.Empty);
                App.Logger?.Information("Bubble count game failed");
            }
        }
    }

    private void RetryGame()
    {
        // Check if panic button was pressed during message
        if (!_isBusy) return;

        try
        {
            var settings = App.Settings.Current;
            var videoPath = GetRandomVideo();
            if (string.IsNullOrEmpty(videoPath))
            {
                App.Logger?.Warning("BubbleCountService: No videos for retry, granting mercy");
                _retryCount = 0;
                _isBusy = false;
                App.Bubbles?.Resume();
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                GameFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var difficulty = (Difficulty)settings.BubbleCountDifficulty;

            App.Achievements?.TrackBubbleCountGameStarted();

            BubbleCountWindow.ShowOnAllMonitors(videoPath, difficulty, true, OnGameComplete);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BubbleCountService: Failed to retry game");
            _retryCount = 0;
            _isBusy = false;
            App.Bubbles?.Resume();
            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
            GameFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowFullscreenMessage(string text, int durationMs, Action then)
    {
        try
        {
            var settings = App.Settings.Current;
            var screens = settings.DualMonitorEnabled
                ? App.GetAllScreensCached()
                : new[] { Screen.PrimaryScreen };

            if (screens == null || screens.Length == 0 || screens[0] == null)
            {
                App.Logger?.Warning("BubbleCountService.ShowMessage: No screens, executing callback");
                then();
                return;
            }

            foreach (var screen in screens)
            {
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = screen.Bounds.X + 100,
                    Top = screen.Bounds.Y + 100,
                    Width = 400,
                    Height = 300,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Impact"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                win.Show();
                win.WindowState = WindowState.Maximized;
                _messageWindows.Add(win);
            }

            Task.Delay(durationMs).ContinueWith(_ =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        CloseMessageWindows();
                        then();
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("BubbleCountService.ShowMessage callback failed: {Error}", ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BubbleCountService: Failed to show fullscreen message");
            then();
        }
    }

    private void CloseMessageWindows()
    {
        foreach (var w in _messageWindows.ToList())
        {
            try { w.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("BubbleCountService: Failed to close message window: {Error}", ex.Message);
            }
        }
        _messageWindows.Clear();
    }

    private string? GetRandomVideo()
    {
        try
        {
            // Refill lists if both are empty
            if (_regularVideos.Count == 0 && _packVideos.Count == 0)
            {
                RefillVideoLists();
            }

            // If still empty after refill, no videos available
            if (_regularVideos.Count == 0 && _packVideos.Count == 0)
            {
                App.Logger?.Warning("BubbleCountService: No videos found (regular or pack)");
                return null;
            }

            // Randomly choose between regular and pack videos based on availability
            bool usePackVideo = false;
            if (_regularVideos.Count > 0 && _packVideos.Count > 0)
            {
                // Both available - pick randomly weighted by count
                var totalCount = _regularVideos.Count + _packVideos.Count;
                usePackVideo = _random.Next(totalCount) >= _regularVideos.Count;
            }
            else if (_packVideos.Count > 0)
            {
                usePackVideo = true;
            }

            if (usePackVideo && _packVideos.Count > 0)
            {
                // Get random pack video
                var index = _random.Next(_packVideos.Count);
                var packVideo = _packVideos[index];
                _packVideos.RemoveAt(index);

                // Decrypt pack video to temp file
                var tempPath = App.ContentPacks?.GetPackFileTempPath(packVideo.PackId, packVideo.File);
                if (!string.IsNullOrEmpty(tempPath))
                {
                    _tempPackFiles.Add(tempPath);
                    App.Logger?.Debug("BubbleCountService: Using pack video from '{Pack}': {File}",
                        packVideo.PackId, packVideo.File.OriginalName);
                    return tempPath;
                }
                else
                {
                    App.Logger?.Warning("BubbleCountService: Failed to decrypt pack video");
                    // Fall through to try regular video
                }
            }

            // Use regular video
            if (_regularVideos.Count > 0)
            {
                var index = _random.Next(_regularVideos.Count);
                var video = _regularVideos[index];
                _regularVideos.RemoveAt(index);
                App.Logger?.Debug("BubbleCountService: Using regular video: {Path}", Path.GetFileName(video));
                return video;
            }

            return null;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to get random video");
            return null;
        }
    }

    /// <summary>
    /// Refill the video lists from filesystem and content packs
    /// </summary>
    private void RefillVideoLists()
    {
        _regularVideos.Clear();
        _packVideos.Clear();

        // Get regular videos from filesystem
        if (Directory.Exists(_videosPath))
        {
            var files = Directory.GetFiles(_videosPath)
                .Where(f => new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            _regularVideos = files.OrderBy(_ => _random.Next()).ToList();
        }

        // Get pack videos from active content packs
        var packVideos = App.ContentPacks?.GetAllActivePackVideos() ?? new List<(string, PackFileEntry)>();
        _packVideos = packVideos.OrderBy(_ => _random.Next()).ToList();

        App.Logger?.Information("BubbleCountService: Video lists refilled - {RegularCount} regular, {PackCount} pack videos",
            _regularVideos.Count, _packVideos.Count);
    }

    /// <summary>
    /// Reset busy state - called when panic button force-closes windows
    /// </summary>
    public void ResetBusyState()
    {
        _isBusy = false;
        _retryCount = 0;
        CloseMessageWindows();
        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
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

        // Clear video lists so they get refilled with new path
        _regularVideos.Clear();
        _packVideos.Clear();
        CleanupTempPackFiles();
    }

    /// <summary>
    /// Reload video assets (e.g., when pack activation changes)
    /// </summary>
    public void ReloadAssets()
    {
        _regularVideos.Clear();
        _packVideos.Clear();
        CleanupTempPackFiles();
        App.Logger?.Information("BubbleCountService: Assets reloaded - cleared video lists");
    }

    /// <summary>
    /// Cleans up temporary pack video files
    /// </summary>
    private void CleanupTempPackFiles()
    {
        foreach (var tempFile in _tempPackFiles)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BubbleCountService: Failed to delete temp pack file: {Error}", ex.Message);
            }
        }
        _tempPackFiles.Clear();
    }

    public void Dispose()
    {
        Stop();
        CleanupTempPackFiles();
    }
}
