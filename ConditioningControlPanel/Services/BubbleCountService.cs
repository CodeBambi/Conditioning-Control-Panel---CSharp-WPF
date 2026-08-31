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
using ConditioningControlPanel.Helpers;

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

    public void Start()
    {
        if (_isRunning) return;
        
        var settings = App.Settings.Current;

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

        // A defer that outlived the service must not coalesce (and so swallow) every future
        // trigger - same latch reset VideoService.Stop does for its #1073 defer.
        _feedDeferPending = false;
        _feedDeferDeadlineUtc = DateTime.MinValue;
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

    /// <summary>True while the For You feed is actually on the user's screen. A GHOSTED feed is
    /// deliberately NOT included (sister of #1073): ghost mode parks the real window off-screen and
    /// leaves a see-through, click-through DWM mirror, so nothing is competing for the screen and
    /// there is no reason to stand a bubble-count game down.</summary>
    private static bool FeedOwnsTheScreen =>
        Fyp.FypHostService.IsActive && !Fyp.FypHostService.IsGhosted;

    /// <summary>True while a game is parked waiting for the For You feed to leave the screen.
    /// One pending replay at a time — a feed session that swallows several triggers replays one
    /// game, not a backlog. Cleared by <see cref="Stop"/> so a defer that outlived the service
    /// cannot coalesce (and so swallow) every future trigger.</summary>
    private bool _feedDeferPending;

    /// <summary>Longest a trigger will wait out an on-screen feed. Deliberately short, and the same
    /// ceiling VideoService uses: the feed can stay open for hours (which is why the original guard
    /// refused to queue behind it at all), and a counting game that fires many minutes after the
    /// trigger that earned it is a surprise, not a reward. Past this, the trigger is given up on —
    /// never accumulated.</summary>
    private static readonly TimeSpan FeedDeferMaxWait = TimeSpan.FromSeconds(90);

    /// <summary>Absolute expiry of the CURRENT feed-defer chain, set on its first defer.
    /// <see cref="DateTime.MinValue"/> = no chain in flight.</summary>
    private DateTime _feedDeferDeadlineUtc = DateTime.MinValue;

    /// <summary>Hold a trigger the For You guard refused and replay it once the feed leaves the
    /// screen (closed, or ghosted away).</summary>
    private void DeferGamePastFeed(bool forceTest)
    {
        if (_feedDeferPending)
        {
            App.Logger?.Information("BubbleCountService: For You replay already pending - trigger coalesced");
            return;
        }

        // Ceiling measured from the FIRST defer of the chain, not per-hop: a replay that lands back
        // in front of the feed must not restart the clock.
        var now = DateTime.UtcNow;
        if (_feedDeferDeadlineUtc == DateTime.MinValue)
            _feedDeferDeadlineUtc = now + FeedDeferMaxWait;

        var remaining = _feedDeferDeadlineUtc - now;
        if (remaining <= TimeSpan.Zero)
        {
            App.Logger?.Information("BubbleCountService: For You replay dropped - past the {Sec:F0}s defer ceiling",
                FeedDeferMaxWait.TotalSeconds);
            _feedDeferDeadlineUtc = DateTime.MinValue;
            return;
        }

        _feedDeferPending = true;
        RunWhenFeedClear(
            () =>
            {
                _feedDeferPending = false;

                // Re-assert the preconditions at FIRE time. The defer can easily outlive the thing
                // that justified it - the engine stopped, the user turned bubble count off (#872) -
                // and replaying then opens a fullscreen game out of a feature that is switched off.
                // forceTest is the dashboard's own "play it now" and stays exempt, as it is above.
                if (!forceTest && (!_isRunning || App.Settings?.Current?.BubbleCountEnabled != true))
                {
                    App.Logger?.Information("BubbleCountService: For You replay abandoned - bubble count no longer running");
                    _feedDeferDeadlineUtc = DateTime.MinValue;
                    return;
                }

                TriggerGame(forceTest);

                // TriggerGame may have parked itself again (the feed came back). Only release the
                // ceiling when the chain really ended, so a re-defer inherits it instead of buying
                // itself another full window.
                if (!_feedDeferPending) _feedDeferDeadlineUtc = DateTime.MinValue;
            },
            remaining,
            onExpired: () =>
            {
                _feedDeferPending = false;
                _feedDeferDeadlineUtc = DateTime.MinValue;
            });
    }

    /// <summary>
    /// Run <paramref name="action"/> as soon as the For You feed is off the screen, and give up
    /// after <paramref name="maxWait"/> so nothing can be held forever. Same shape as
    /// VideoService.RunWhenFeedClear and for the same reason: there is no "feed closed" event to
    /// hang off, and ghost mode enters and leaves without one either, so the UI dispatcher is
    /// polled twice a second. Callers only reach here while the feed IS on screen, so there is no
    /// fire-immediately branch - the first tick handles the case where it has already gone.
    ///
    /// <para><paramref name="onExpired"/> is the UNCONDITIONAL give-up callback: it runs on every
    /// path where <paramref name="action"/> will not, including the "there is no dispatcher to poll
    /// on" early return. The caller latches a "defer pending" flag before calling in, and a silent
    /// early return here would latch it forever.</para>
    /// </summary>
    private static void RunWhenFeedClear(Action action, TimeSpan maxWait, Action? onExpired = null)
    {
        if (action == null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            // No dispatcher to poll on, so the action can never run - release the caller's latch.
            try { onExpired?.Invoke(); } catch { }
            return;
        }

        var deadline = DateTime.UtcNow + maxWait;
        // Normal, not Background: this project has a documented starvation issue where Background /
        // Loaded priority work is starved out under load, which would stall the poll that releases
        // the parked trigger.
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (FeedOwnsTheScreen && DateTime.UtcNow < deadline) return;   // still on screen, still within the window
                timer.Stop();
                if (FeedOwnsTheScreen)
                {
                    App.Logger?.Information("BubbleCountService: deferred game expired - For You feed still on screen after {Sec:F0}s",
                        maxWait.TotalSeconds);
                    onExpired?.Invoke();
                    return;
                }
                App.Logger?.Information("BubbleCountService: For You feed left the screen - firing deferred game");
                action();
            }
            catch (Exception ex)
            {
                try { timer.Stop(); } catch { }
                try { onExpired?.Invoke(); } catch { }
                App.Logger?.Debug("BubbleCountService.RunWhenFeedClear: {E}", ex.Message);
            }
        };
        timer.Start();
        App.Logger?.Information("BubbleCountService: deferring bubble count game until the For You feed leaves the screen");
    }

    public void TriggerGame(bool forceTest = false)
    {
        // Allow forced test even when engine not running
        if (!forceTest && (!_isRunning || _isBusy))
        {
            // A queued game can be dequeued AFTER the engine stopped (a stop mid-video now
            // releases the Video slot, which dispatches us). Hand the fresh claim back or it
            // blocks every interaction for the 5-minute stuck window. _isBusy claims stay: a
            // live game owns its slot and completes through the window teardown funnel.
            if (!_isBusy && App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.BubbleCount)
                App.InteractionQueue.Complete(InteractionQueueService.InteractionType.BubbleCount);
            return;
        }
        if (_isBusy) return; // Still prevent double-triggering

        // The feature can be switched OFF between the moment a game was scheduled/queued and the
        // moment it fires - loading a preset does exactly that (#872) - and nothing here re-read
        // the flag, so a pending trigger still opened the game the user had just turned off.
        // forceTest is the dashboard's own "play it now" button and stays exempt.
        if (!forceTest && App.Settings?.Current?.BubbleCountEnabled != true)
        {
            App.Logger?.Information("BubbleCountService: game dropped - bubble count is disabled in settings");
            if (App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.BubbleCount)
                App.InteractionQueue.Complete(InteractionQueueService.InteractionType.BubbleCount);
            return;
        }

        // The post-poisoning hold-off (#766: poisoning at 22:05, a bubble-count video built on the
        // wreckage at 22:18, a dispatcher that never drained again) is evaluated AFTER the video is
        // picked - see the skip inside the continuation below. It has to be: with the browser engine
        // on, whether this game touches the shared LibVLC instance at all depends on the FILE, and
        // no file has been chosen yet.

        // For You feed ON SCREEN: bubble count is a video-class interaction and stands down like
        // the mandatory video does. This guard was copied from the video one and inherited both of
        // its bugs, fixed there as #1073:
        //
        //  1. It gated on IsActive alone, which is just `_host != null`, and a GHOSTED feed is still
        //     "active". Ghost mode parks the real window off-screen and leaves a see-through,
        //     click-through DWM mirror - nothing is competing for the screen, so a game is exactly
        //     as appropriate as it would be with the feed closed. A user browsing ghosted had every
        //     bubble-count game silently eaten while seeing no feed at all. Hence FeedOwnsTheScreen.
        //  2. It DROPPED, so a game the scheduler (or a bubble payload) earned simply evaporated.
        //     The trigger is now held and replayed once the feed leaves the screen - closed, or
        //     ghosted away.
        //
        // The old comment's concern ("never queue: the feed outlives the stuck window") is still
        // honoured, twice over: the queue slot is RELEASED here rather than held across the wait,
        // and the wait itself is capped at FeedDeferMaxWait, so nothing parks behind an all-evening
        // feed session.
        if (FeedOwnsTheScreen)
        {
            App.Logger?.Information("BubbleCountService: game deferred - For You feed on screen");
            if (App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.BubbleCount)
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
            DeferGamePastFeed(forceTest);
            return;
        }

        var settings = App.Settings.Current;

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
            DispatcherHelper.RunOnUI(() =>
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

                    // Now the file is known, so the routing decision can be made - and only a clip
                    // that will actually use LibVLC cares about the poisoned shared instance. Skip
                    // the game OUTRIGHT (the original pre-branch behaviour): the scheduler brings
                    // the next one around on a rebuilt instance, and letting the window abort
                    // instead would land in the completion callback as a FAILED count, which in
                    // strict mode starts the WRONG! WATCH AGAIN retry bounce for the whole cooldown.
                    var poisonMs = Video.Browser.BrowserVideoGate.ShouldUseBrowser(videoPath)
                        ? 0
                        : VideoService.NativePoisonCooldownRemainingMs;
                    if (poisonMs > 0)
                    {
                        App.Logger?.Warning("BubbleCountService: skipping game - a wedged native Stop() poisoned the shared LibVLC ({Sec:F0}s of cooldown left)",
                            poisonMs / 1000.0);
                        VideoDiag.Log("BUBBLE", $"game skipped - native poison cooldown, {poisonMs}ms remaining");
                        _isBusy = false;
                        App.Bubbles?.Resume();
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                        return;
                    }

                    // Determine difficulty settings
                    var difficulty = (Difficulty)settings.BubbleCountDifficulty;

                    // Track game started
                    App.Achievements?.TrackBubbleCountGameStarted();

                    // Show the game on all monitors. The skip callback is the window's backstop for
                    // a cooldown that started in the last few milliseconds - same clean end as
                    // above, never a lost game.
                    BubbleCountWindow.ShowOnAllMonitors(videoPath, difficulty, settings.BubbleCountStrictLock, OnGameComplete,
                        onSkipped: () =>
                        {
                            App.Logger?.Warning("BubbleCountService: game skipped by the window (native poison cooldown)");
                            _isBusy = false;
                            App.Bubbles?.Resume();
                            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                        });

                    // Extend the stuck detection timeout to cover full video + counting phase.
                    // Typed: onSkipped above can resolve synchronously inside ShowOnAllMonitors,
                    // and its Complete() dispatches the next queued interaction before control
                    // returns here - a type-blind extension would stretch that unrelated
                    // interaction's stuck-recovery window with a stale duration.
                    var videoDuration = BubbleCountWindow.LastVideoDurationSeconds;
                    App.InteractionQueue?.ExtendTimeout(videoDuration + 120, InteractionQueueService.InteractionType.BubbleCount);
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
                    ShowFullscreenMessage(
                        App.Mods?.GetAttentionCheckMercyMessage() ?? "BAMBI GETS MERCY",
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
                    ShowFullscreenMessage(
                        App.Mods?.GetBubbleCountRetryMessage() ?? "WRONG!\nWATCH AGAIN",
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

        // Extend the stuck detection timeout to prevent InteractionQueue from
        // auto-completing BubbleCount during the retry gap, which would let queued
        // interactions (e.g. Video) start while the retry game plays.
        App.InteractionQueue?.ExtendTimeout(300);

        // The post-poisoning hold-off is evaluated after selection here too (see TriggerGame): with
        // the browser engine on, whether the retry clip touches LibVLC at all depends on the file.

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

            // End the retry loop outright rather than let it bounce off the cooldown every couple
            // of seconds for a minute. Verbatim the original resolution (mercy: the game ends, the
            // slot is handed back, GameFailed fires once), just taken with the file in hand.
            var poisonMs = Video.Browser.BrowserVideoGate.ShouldUseBrowser(videoPath)
                ? 0
                : VideoService.NativePoisonCooldownRemainingMs;
            if (poisonMs > 0)
            {
                App.Logger?.Warning("BubbleCountService: retry abandoned - shared LibVLC poisoned ({Sec:F0}s of cooldown left)", poisonMs / 1000.0);
                VideoDiag.Log("BUBBLE", $"retry abandoned - native poison cooldown, {poisonMs}ms remaining");
                _retryCount = 0;
                _isBusy = false;
                App.Bubbles?.Resume();
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                GameFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var difficulty = (Difficulty)settings.BubbleCountDifficulty;

            App.Achievements?.TrackBubbleCountGameStarted();

            BubbleCountWindow.ShowOnAllMonitors(videoPath, difficulty, true, OnGameComplete,
                onSkipped: () =>
                {
                    // Backstop skip: same mercy end as the cooldown branch above, never a loss.
                    App.Logger?.Warning("BubbleCountService: retry skipped by the window (native poison cooldown)");
                    _retryCount = 0;
                    _isBusy = false;
                    App.Bubbles?.Resume();
                    App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.BubbleCount);
                    GameFailed?.Invoke(this, EventArgs.Empty);
                });
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
                var dpiScale = BubbleCountWindow.GetDpiForScreen(screen);
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = (screen.Bounds.X + 100) / dpiScale,
                    Top = (screen.Bounds.Y + 100) / dpiScale,
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
                    DispatcherHelper.RunOnUI(() =>
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
                    // Same reason as VideoService.GetNextVideo: the decrypt path is a fresh GUID on
                    // every play, so the browser engine's unsafe cache needs the pack entry's own
                    // identity or a clip it has already failed on costs a full fallback each game.
                    Video.Browser.BrowserUnsafeVideoCache.RegisterStableKey(
                        tempPath, $"pack:{packVideo.PackId}|{packVideo.File.OriginalName}");
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

        // Get regular videos from filesystem (including subfolders for content pack organization)
        if (Directory.Exists(_videosPath))
        {
            var validExtensions = new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv" };
            var allFiles = Directory.GetFiles(_videosPath, "*.*", SearchOption.AllDirectories);
            var files = new List<string>();

            foreach (var file in allFiles)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!validExtensions.Contains(ext)) continue;

                // Security: validate path is within allowed directories
                if (!SecurityHelper.IsPathSafe(file, AppDomain.CurrentDomain.BaseDirectory)
                    && !SecurityHelper.IsPathSafe(file, App.UserDataPath)
                    && !SecurityHelper.IsPathSafe(file, App.EffectiveAssetsPath))
                    continue;

                var fileName = SecurityHelper.SanitizeFilename(Path.GetFileName(file));
                if (string.IsNullOrEmpty(fileName)) continue;

                files.Add(file);
            }

            // Filter out disabled assets — same case/separator normalization as Flash/Video.
            if (App.Settings?.Current?.DisabledAssetPaths.Count > 0)
            {
                var basePath = App.EffectiveAssetsPath;
                static string Norm(string p) => p.Replace('\\', '/');
                var disabled = new HashSet<string>(
                    App.Settings.Current.DisabledAssetPaths.Select(Norm),
                    StringComparer.OrdinalIgnoreCase);
                files = files.Where(f =>
                {
                    var relativePath = Norm(Path.GetRelativePath(basePath, f));
                    return !disabled.Contains(relativePath);
                }).ToList();
            }

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
    /// Force cleanup all bubble count state and windows.
    /// Called by InteractionQueue stuck detection to prevent lingering windows.
    /// </summary>
    public void ForceCleanup()
    {
        App.Logger?.Information("BubbleCountService: ForceCleanup called");
        _isBusy = false;
        _retryCount = 0;
        CloseMessageWindows();
        BubbleCountWindow.ForceCloseAll();
        // #633: ForceCleanup previously omitted the result window, leaving it orphaned
        // fullscreen/topmost with no escape (strict mode has no Esc). Close it too, matching
        // the manual paths (panic key, stop-all, remote) which close both windows.
        BubbleCountResultWindow.ForceCloseAll();
        App.Bubbles?.Resume();
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
