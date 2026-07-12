using System.Linq;
using System.Text.Json;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform achievement tracker. Mirrors the legacy WPF <see cref="ConditioningControlPanel.Services.AchievementService"/>
/// but uses the Core platform seams for timers, paths, dispatching and logging.
/// </summary>
public sealed class AchievementService : IAchievementService, IDisposable
{
    private readonly IAppEnvironment _environment;
    private readonly ILogger<AchievementService> _logger;
    private readonly IEnumerable<IAuthProvider> _authProviders;
    private readonly string _progressPath;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _trackingTimer;

    // Serializes every persistence path (synchronous Save() callers and the background
    // autosave tick) so writers never race, tear the file, or serialize _progress concurrently.
    private readonly object _saveLock = new();

    // Hot-path service refs resolved once (lazily) and cached, so the 1-second time tracker
    // never pays for DI resolution or reflection on every tick.
    private IQuestService? _questService;
    private bool _questServiceResolved;
    private IAutonomyService? _autonomyService;
    private bool _autonomyServiceResolved;

    private AchievementProgress _progress;
    private bool _isDirty;
    private DateTime _lastPinkFilterCheck = DateTime.Now;
    private DateTime _lastSpiralCheck = DateTime.Now;
    private DateTime _lastBrainDrainCheck = DateTime.Now;
    private DateTime _lastMindWipeCheck = DateTime.Now;
    private DateTime _lastDeeperCheck = DateTime.Now;
    private DateTime _lastAutonomyCheck = DateTime.Now;

    // Per-tick credit ceiling for Bambi Takeover (autonomy) quest time. The tracking timer fires
    // every ~1s, but when the app is backgrounded or busy with a fullscreen takeover the tick gets
    // starved and can slip well past that. Crediting min(elapsed, cap) still counts that
    // legitimately-active time (fixing the "15-min quest took an hour when backgrounded" report)
    // while a single long stall (sleep/resume, app suspended for minutes) can only ever add 10s.
    private const double AutonomyTickCreditCapMinutes = 10.0 / 60.0;
    private bool _isDisposed;

    public event EventHandler<Achievement>? AchievementUnlocked;

    /// <inheritdoc />
    public bool SuppressPopups { get; set; }

    /// <inheritdoc />
    public AchievementProgress Progress => _progress;

    /// <inheritdoc />
    public bool CanUnlockExclusive => _authProviders.Any(p =>
        string.Equals(p.ProviderName, "patreon", StringComparison.OrdinalIgnoreCase) && p.HasPremiumAccess);

    public AchievementService(IAppEnvironment environment, ILogger<AchievementService> logger, IEnumerable<IAuthProvider>? authProviders = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authProviders = authProviders ?? Enumerable.Empty<IAuthProvider>();

        _progressPath = Path.Combine(environment.UserDataPath, "achievements.json");
        _progress = LoadProgress();

        // Reset continuous/session-based counters on startup (these shouldn't persist)
        _progress.ContinuousSpiralMinutes = 0;
        _progress.ContinuousMindWipeSeconds = 0;
        _progress.AltTabPressedThisSession = false;
        _progress.AvatarClickCount = 0;
        _progress.AvatarClickStartTime = null;

        // Check daily streak on startup
        _progress.UpdateDailyStreak();
        _progress.SyncCurrentStreak();
        _isDirty = true;

        // Auto-save every 30 seconds if dirty (off UI thread)
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => OnAutoSaveTick();
        _saveTimer.Start();

        // Track time-based achievements every second
        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _trackingTimer.Tick += (_, _) => TrackTimeBasedProgress();
        _trackingTimer.Start();

        _logger.LogInformation("AchievementService initialized. {Count} achievements unlocked.",
            _progress.UnlockedAchievements.Count);
    }

    private AchievementProgress LoadProgress()
    {
        var tmpPath = _progressPath + ".tmp";

        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                var progress = JsonSerializer.Deserialize<AchievementProgress>(json);
                if (progress != null)
                {
                    return progress;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load achievement progress");
        }

        // Recover from an atomic-write temp file if the main file is missing or corrupt
        // (e.g. the process was killed after the tmp write but before the move completed).
        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var progress = JsonSerializer.Deserialize<AchievementProgress>(json);
                if (progress != null)
                {
                    _logger.LogWarning("Recovered achievement progress from temp file {Path}", tmpPath);
                    try { File.Move(tmpPath, _progressPath, overwrite: true); } catch { }
                    return progress;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover achievement progress from {Path}", tmpPath);
            }
        }

        return new AchievementProgress();
    }

    /// <inheritdoc />
    public void Save() => WriteProgressAtomic();

    /// <summary>
    /// Single lock-guarded atomic writer for <see cref="_progress"/>. Every persistence path —
    /// the synchronous <see cref="Save"/> callers (TryUnlock/TrackVideoWatched/TrackLockCardCompletion)
    /// and the background autosave tick — funnels through here so writers never race each other,
    /// never tear the file, and never serialize the same object graph concurrently. Serialization
    /// happens inside the lock, then the JSON is written to a temp file and atomically moved into
    /// place, so a crash mid-write can never corrupt achievements.json. Mirrors QuestService.SaveProgress.
    /// </summary>
    private void WriteProgressAtomic()
    {
        lock (_saveLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_progressPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
                var tmpPath = _progressPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _progressPath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save achievement progress");
            }
        }
    }

    private void OnAutoSaveTick()
    {
        if (!_isDirty) return;

        _isDirty = false;
        // May offload to a worker thread, but the serialize + write both run inside
        // WriteProgressAtomic's lock, so this can never race a synchronous Save() caller.
        _ = Task.Run(WriteProgressAtomic);
    }

    /// <inheritdoc />
    public void TrackTimeProgress() => TrackTimeBasedProgress();

    /// <summary>
    /// Track time-based progress (called every second).
    /// </summary>
    private void TrackTimeBasedProgress()
    {
        if (_isDisposed) return;

        var settings = CoreApp.Settings?.Current;
        if (settings == null) return;

        var now = DateTime.Now;
        var overlayRunning = IsOverlayRunning();

        // Track total conditioning time for skill tree (when overlay is running = session active)
        if (overlayRunning)
        {
            CoreApp.SkillTree?.AddConditioningTime(1.0 / 60.0);
        }

        // Track Pink Filter time - only when overlay is actually running
        var isPinkFilterActive = settings.PinkFilterEnabled && overlayRunning;
        if (isPinkFilterActive)
        {
            var elapsed = (now - _lastPinkFilterCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1) // Sanity check - max 6 seconds between ticks
            {
                _progress.TotalPinkFilterMinutes += elapsed;
                _isDirty = true;

                if (_progress.TotalPinkFilterMinutes >= 600)
                {
                    TryUnlock("rose_tinted_reality");
                }

                TryQuestTrack("TrackPinkFilterMinutes", elapsed);
            }
            _lastPinkFilterCheck = now;
        }
        else
        {
            _lastPinkFilterCheck = now;
        }

        // Track Spiral time - only when overlay is actually running
        var isSpiralActive = settings.SpiralEnabled && overlayRunning;
        if (isSpiralActive)
        {
            var elapsed = (now - _lastSpiralCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                _progress.TotalSpiralMinutes += elapsed;
                _progress.ContinuousSpiralMinutes += elapsed;
                _isDirty = true;

                if (_progress.ContinuousSpiralMinutes >= 20)
                {
                    TryUnlock("spiral_eyes");
                }

                TryQuestTrack("TrackSpiralMinutes", elapsed);
            }
            _lastSpiralCheck = now;
        }
        else
        {
            _progress.ContinuousSpiralMinutes = 0;
            _lastSpiralCheck = now;
        }

        // Track BrainDrain time - only when overlay is actually running
        var isBrainDrainActive = settings.BrainDrainEnabled && overlayRunning;
        if (isBrainDrainActive)
        {
            var elapsed = (now - _lastBrainDrainCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                TryQuestTrack("TrackBrainDrainMinutes", elapsed);
            }
            _lastBrainDrainCheck = now;
        }
        else
        {
            _lastBrainDrainCheck = now;
        }

        // Track Deeper player time — only while an enhancement is actively playing.
        if (IsDeeperActivelyPlaying())
        {
            var elapsed = (now - _lastDeeperCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                _progress.DeeperMinutes += elapsed;
                _isDirty = true;
                if (_progress.DeeperMinutes >= 600)
                {
                    TryUnlock("permanent_resident");
                }
            }
            _lastDeeperCheck = now;
        }
        else
        {
            _lastDeeperCheck = now;
        }

        // Track Bambi Takeover (autonomy) active time for Patreon quests — only while
        // autonomy is enabled/running. Mirrors the spiral/pink accumulation pattern.
        // Resolved lazily via CoreApp.Services to avoid a circular DI dependency
        // (AvaloniaAutonomyService -> IFlashService -> IAchievementService).
        var autonomy = ResolveAutonomyService();
        if (autonomy?.IsEnabled == true)
        {
            var elapsed = (now - _lastAutonomyCheck).TotalMinutes;
            // Credit the elapsed time, capping a single tick so a starved/backgrounded tick still
            // counts (the old hard "< 6s or drop it" guard silently threw away real active time,
            // which is why the quest crawled) while a long stall can't dump minutes in at once.
            var credit = ComputeAutonomyTickCredit(elapsed);
            if (credit > 0)
            {
                TryQuestTrack("TrackAutonomyMinutes", credit);
            }
            _lastAutonomyCheck = now;
        }
        else
        {
            _lastAutonomyCheck = now;
        }

        // Check System Overload (Bubbles + Bouncing Text + Spiral all active)
        if (settings.BubblesEnabled && settings.BouncingTextEnabled && settings.SpiralEnabled)
        {
            if (!_progress.HasSystemOverload)
            {
                _progress.HasSystemOverload = true;
                _isDirty = true;
                TryUnlock("system_overload");
            }
        }

        // Check Total Lockdown (Strict Lock + No Panic + Pink Filter)
        if (settings.StrictLockEnabled && !settings.PanicKeyEnabled && settings.PinkFilterEnabled)
        {
            if (!_progress.HasTotalLockdown)
            {
                _progress.HasTotalLockdown = true;
                _isDirty = true;
                TryUnlock("total_lockdown");
            }
        }
    }

    /// <summary>
    /// Per-tick Bambi Takeover (autonomy) quest credit: elapsed minutes since the last tick clamped
    /// to <see cref="AutonomyTickCreditCapMinutes"/> (so a starved/backgrounded tick still counts real
    /// active time, while a long stall can't dump minutes in at once); 0 for a non-positive interval.
    /// Internal for regression tests.
    /// </summary>
    internal static double ComputeAutonomyTickCredit(double elapsedMinutes)
        => elapsedMinutes > 0 ? Math.Min(elapsedMinutes, AutonomyTickCreditCapMinutes) : 0.0;

    /// <inheritdoc />
    public void CheckLevelAchievements(int level)
    {
        if (level >= 10) TryUnlock("plastic_initiation");
        if (level >= 20) TryUnlock("dumb_bimbo");
        if (level >= 50) TryUnlock("fully_synthetic");
        if (level >= 75) TryUnlock("docile_cow");
        if (level >= 100) TryUnlock("perfect_plastic_puppet");
        if (level >= 125) TryUnlock("brainwashed_slavedoll");
        if (level >= 150) TryUnlock("platinum_puppet");
    }

    /// <inheritdoc />
    public void CheckDailyMaintenance()
    {
        if (_progress.ConsecutiveDays >= 7)
        {
            TryUnlock("daily_maintenance");
        }
    }

    /// <inheritdoc />
    public void TrackFlashImage()
    {
        _progress.TotalFlashImages++;
        _isDirty = true;

        if (_progress.TotalFlashImages >= 5000)
        {
            TryUnlock("retinal_burn");
        }

        TryQuestTrack("TrackFlashImage");
    }

    /// <inheritdoc />
    public void TrackBubblePopped()
    {
        _progress.TotalBubblesPopped++;
        _isDirty = true;

        if (_progress.TotalBubblesPopped >= 1000)
        {
            TryUnlock("pop_the_thought");
        }

        // Award 1 sparkle point every 100 bubbles
        if (_progress.TotalBubblesPopped % 100 == 0)
        {
            var settings = CoreApp.Settings?.Current;
            if (settings != null)
            {
                settings.SkillPoints += 1;
                CoreApp.Settings?.Save();
                _logger.LogInformation("Bubble milestone! {Total} bubbles popped — awarded 1 sparkle point (total: {Points})",
                    _progress.TotalBubblesPopped, settings.SkillPoints);
                ShowBubbleMilestoneNotification(_progress.TotalBubblesPopped);
            }
        }

        TryQuestTrack("TrackBubblePopped");
    }

    /// <inheritdoc />
    /// <remarks>Ported from WPF <c>AchievementService.TrackBubblesPopped</c>
    /// (Services/Progression/AchievementService.cs:378-418). One batch increment; every
    /// 100-bubble sparkle-point milestone crossed is granted in a single save; at most one
    /// milestone popup fires so a big Rabbit Hole run can't spam notifications.
    /// <para>NOTE: the WPF method ends with <c>App.Quests?.TrackBubblesPopped(count)</c> — that
    /// forward is deliberately OMITTED here. <see cref="DtrhHostOrchestrator"/> calls
    /// <see cref="IAchievementService"/> AND <see cref="IQuestService"/> directly at run-end,
    /// so forwarding here too would double-count quest progress. Net behavior is identical to
    /// WPF (single credit); the quest dispatch just moved from inside the achievement service
    /// to the orchestrator (the port's split-call seam).</para></remarks>
    public void TrackBubblesPopped(int count)
    {
        if (count <= 0) return;

        int before = _progress.TotalBubblesPopped;
        _progress.TotalBubblesPopped += count;
        int after = _progress.TotalBubblesPopped;
        _isDirty = true;

        if (before < 1000 && after >= 1000)
        {
            TryUnlock("pop_the_thought");
        }

        // 1 sparkle point per 100 bubbles — award every milestone crossed in one go.
        int milestones = after / 100 - before / 100;
        if (milestones > 0)
        {
            var settings = CoreApp.Settings?.Current;
            if (settings != null)
            {
                settings.SkillPoints += milestones;
                CoreApp.Settings?.Save();
                _logger.LogInformation("Bubble milestone (batch)! {Total} bubbles popped — awarded {N} sparkle point(s) (total: {Points})",
                    after, milestones, settings.SkillPoints);
                // one popup for the highest 100-boundary reached this run
                ShowBubbleMilestoneNotification(after - after % 100);
            }
        }
    }

    private void ShowBubbleMilestoneNotification(int totalBubbles)
    {
        try
        {
            var fakeAchievement = new Achievement
            {
                Id = "bubble_milestone",
                Name = Core.Localization.Loc.GetF("achievement_bubble_milestone_name", totalBubbles),
                FlavorText = Core.Localization.Loc.Get("achievement_bubble_milestone_flavor"),
                ImageName = "bubble_pop.png",
                Category = AchievementCategory.Minigames
            };

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _logger.LogDebug("Firing bubble milestone achievement event for: {Total}", totalBubbles);
                    AchievementUnlocked?.Invoke(this, fakeAchievement);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to show bubble milestone popup");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show bubble milestone notification");
        }
    }

    /// <inheritdoc />
    public void TrackBubbleCountResult(bool correct)
    {
        if (correct)
        {
            _progress.BubbleCountCorrectStreak++;
            if (_progress.BubbleCountCorrectStreak > _progress.BubbleCountBestStreak)
            {
                _progress.BubbleCountBestStreak = _progress.BubbleCountCorrectStreak;
            }

            if (_progress.BubbleCountCorrectStreak >= 5)
            {
                TryUnlock("mathematicians_nightmare");
            }
        }
        else
        {
            _progress.BubbleCountCorrectStreak = 0;
        }

        TrackBubbleCountGameResult(correct);
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackLockCardCompletion(double seconds, int totalChars, int errors, int phrases)
    {
        _progress.TotalLockCardsCompleted++;
        _isDirty = true;
        _logger.LogInformation("Lock card tracked! Total lock cards completed: {Count}", _progress.TotalLockCardsCompleted);
        Save();

        TryQuestTrack("TrackLockCardCompleted");

        if (errors == 0)
        {
            _progress.HasPerfectLockCard = true;
            TryUnlock("typing_tutor");
        }

        if (phrases >= 3 && seconds < 15)
        {
            if (seconds < _progress.FastestLockCardSeconds)
            {
                _progress.FastestLockCardSeconds = seconds;
            }
            TryUnlock("obedience_reflex");
        }
    }

    /// <inheritdoc />
    public void TrackVideoWatched(double durationSeconds)
    {
        if (durationSeconds <= 0) return;

        var minutes = durationSeconds / 60.0;
        _progress.TotalVideoMinutes += minutes;
        _isDirty = true;
        _logger.LogInformation("Video watched: {Duration}s ({Minutes:F2} min). Total: {Total:F1} minutes",
            durationSeconds, minutes, _progress.TotalVideoMinutes);
        Save();

        TryQuestTrack("TrackVideoMinutes", minutes);
    }

    /// <inheritdoc />
    public void TrackAttentionCheckFailed()
    {
        _progress.AttentionCheckFailures++;
        _isDirty = true;

        if (_progress.AttentionCheckFailures >= 3)
        {
            TryUnlock("mercy_beggar");
        }
    }

    /// <inheritdoc />
    public void TrackMindWipeDuration(double seconds)
    {
        _progress.ContinuousMindWipeSeconds = seconds;
        _isDirty = true;

        if (seconds >= 60)
        {
            TryUnlock("clean_slate");
        }
    }

    /// <inheritdoc />
    public void TrackCornerHit()
    {
        if (!_progress.HasHitCorner)
        {
            _progress.HasHitCorner = true;
            _isDirty = true;
            TryUnlock("corner_hit");
        }
    }

    /// <inheritdoc />
    public void TrackAvatarClick()
    {
        var clickCount = _progress.AvatarClickCount + 1;
        _logger.LogDebug("TrackAvatarClick called. Current count will be: {Count}", clickCount);

        if (_progress.TrackAvatarClick())
        {
            _logger.LogInformation("20 clicks reached! Unlocking Neon Obsession...");
            TryHapticAvatarEasterEggPattern();
            TryUnlock("neon_obsession");
        }
        if (_progress.TrackNeedyDollClick())
        {
            _logger.LogInformation("150 clicks in 60s! Unlocking Needy Doll...");
            TryUnlock("needy_doll");
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackAltTab()
    {
        _progress.AltTabPressedThisSession = true;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackPanicPressed()
    {
        _progress.LastPanicPressTime = DateTime.Now;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionStart()
    {
        _progress.ResetSessionTracking();
        TrackSessionStarted();
        CheckRelapse();
        _isDirty = true;
    }

    /// <inheritdoc />
    public void CheckRelapse()
    {
        if (_progress.LastPanicPressTime.HasValue)
        {
            var elapsed = (DateTime.Now - _progress.LastPanicPressTime.Value).TotalSeconds;
            if (elapsed <= 10)
            {
                TryUnlock("relapse");
            }
        }
    }

    /// <inheritdoc />
    public void TrackSessionComplete(string sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled)
    {
        _logger.LogInformation("TrackSessionComplete called: Session={Name}, Duration={Duration:F1}min, NoPanic={NoPanic}, StrictLock={Strict}",
            sessionName, durationMinutes, noPanicEnabled, strictLockEnabled);

        _progress.CompletedSessions.Add(sessionName);

        if (durationMinutes > _progress.LongestSessionMinutes)
        {
            _progress.LongestSessionMinutes = durationMinutes;
        }

        if (durationMinutes >= 180)
        {
            _logger.LogInformation("Deep Sleep check: Session duration {Duration:F1}min >= 180min, unlocking!", durationMinutes);
            TryUnlock("deep_sleep");
        }
        else if (durationMinutes >= 60)
        {
            _logger.LogDebug("Session {Duration:F1}min completed - need 180min for Deep Sleep achievement", durationMinutes);
        }

        if (noPanicEnabled)
        {
            _logger.LogInformation("No panic was enabled - unlocking 'what_panic_button'");
            _progress.CompletedSessionWithNoPanic = true;
            TryUnlock("what_panic_button");
        }

        var sessionLower = sessionName.ToLowerInvariant();

        if (sessionLower.Contains("distant doll"))
        {
            TryUnlock("sofa_decor");
        }

        if (sessionLower.Contains("good girls") && strictLockEnabled)
        {
            _progress.CompletedGoodGirlsWithStrictLock = true;
            TryUnlock("look_but_dont_touch");
        }

        if (sessionLower.Contains("morning drift"))
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 6 && hour < 9)
            {
                _progress.CompletedMorningDriftInMorning = true;
                TryUnlock("morning_glory");
            }
        }

        if (sessionLower.Contains("gamer girl") && !_progress.AltTabPressedThisSession)
        {
            _progress.CompletedGamerGirlNoAltTab = true;
            TryUnlock("player_2_disconnected");
        }

        TryQuestTrack("TrackSessionCompleted");
        _isDirty = true;
    }

    /// <inheritdoc />
    public bool TryUnlock(string achievementId)
    {
        _logger.LogDebug("TryUnlock called for: {Id}", achievementId);

        if (_progress.IsUnlocked(achievementId))
        {
            _logger.LogDebug("Achievement {Id} already unlocked", achievementId);
            return false;
        }

        if (!Achievement.All.TryGetValue(achievementId, out var achievement))
        {
            _logger.LogWarning("Unknown achievement ID: {Id}", achievementId);
            return false;
        }

        _progress.Unlock(achievementId);
        _isDirty = true;
        Save();

        _logger.LogInformation("Achievement unlocked: {Name} (ID: {Id}){Suppressed}", achievement.Name, achievementId,
            SuppressPopups ? " (popup suppressed)" : "");

        if (SuppressPopups) return true;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _logger.LogDebug("Firing AchievementUnlocked event for: {Name}", achievement.Name);
                    AchievementUnlocked?.Invoke(this, achievement);
                    TryHapticAchievementPattern();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fire achievement event");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post achievement event");
        }

        return true;
    }

    /// <inheritdoc />
    public void TrackAttentionCheckPassed(bool isVideo = false)
    {
        _progress.TotalAttentionChecksPassed++;
        if (isVideo)
        {
            _progress.VideoAttentionChecksPassed++;
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackVideoAttentionCheckFailed()
    {
        _progress.VideoAttentionChecksFailed++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackBubbleCountGameStarted()
    {
        _progress.TotalBubbleCountGames++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackBubbleCountGameResult(bool success)
    {
        if (success)
        {
            _progress.TotalBubbleCountCorrect++;
        }
        else
        {
            _progress.TotalBubbleCountFailed++;
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionStarted()
    {
        _progress.TotalSessionsStarted++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionAbandoned()
    {
        _progress.TotalSessionsAbandoned++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackXPEarned(double amount)
    {
        _progress.TotalXPEarned += amount;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSkillPointsEarned(int amount)
    {
        _progress.TotalSkillPointsEarned += amount;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSkillPointsSpent(int amount)
    {
        if (amount <= 0) return;
        _progress.LifetimeSkillPointsSpent += amount;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void ReconcileLifetimePointsSpent(long serverValue)
    {
        if (serverValue <= _progress.LifetimeSkillPointsSpent) return;
        _progress.LifetimeSkillPointsSpent = serverValue;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void MarkDirty() => _isDirty = true;

    /// <inheritdoc />
    public void ResetProgress()
    {
        _progress = new AchievementProgress();
        _isDirty = false;
        Save();
        _logger.LogInformation("AchievementService progress reset");
    }

    /// <inheritdoc />
    public int GetUnlockedCount() => _progress.UnlockedAchievements.Count;

    /// <inheritdoc />
    public int GetTotalCount()
    {
        var count = 0;
        foreach (var a in Achievement.All.Values)
            if (!a.IsHidden) count++;
        return count;
    }

    /// <inheritdoc />
    public int GetUnlockedCount(bool exclusive)
    {
        var count = 0;
        foreach (var id in _progress.UnlockedAchievements)
        {
            if (Achievement.All.TryGetValue(id, out var a) && a.IsExclusive == exclusive)
                count++;
        }
        return count;
    }

    /// <inheritdoc />
    public int GetTotalCount(bool exclusive)
    {
        var count = 0;
        foreach (var a in Achievement.All.Values)
        {
            if (a.IsHidden) continue;
            if (a.IsExclusive == exclusive) count++;
        }
        return count;
    }

    /// <inheritdoc />
    public bool TryUnlockExclusive(string achievementId)
    {
        if (_progress.IsUnlocked(achievementId)) return false;
        if (!CanUnlockExclusive)
        {
            _logger.LogDebug("Exclusive achievement {Id} withheld — user not entitled", achievementId);
            return false;
        }
        return TryUnlock(achievementId);
    }

    /// <inheritdoc />
    public void TrackFeatureUsed(string featureId, double amount = 1)
    {
        switch (featureId.ToLowerInvariant())
        {
            case "flashimage":
            case "flash_image":
                for (int i = 0; i < (int)amount; i++) TrackFlashImage();
                break;

            case "bubblepopped":
            case "bubble_popped":
                for (int i = 0; i < (int)amount; i++) TrackBubblePopped();
                break;

            case "avatarclick":
            case "avatar_click":
                TrackAvatarClick();
                break;

            case "cornerhit":
            case "corner_hit":
                TrackCornerHit();
                break;

            case "videowatched":
            case "video_watched":
                TrackVideoWatched(amount);
                break;

            default:
                _logger.LogDebug("TrackFeatureUsed: unmapped feature '{FeatureId}' amount {Amount}", featureId, amount);
                break;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _saveTimer.Stop();
        _trackingTimer.Stop();
        Save();
    }

    private static bool IsOverlayRunning()
    {
        try
        {
            // The Core service only runs under the Avalonia heads, where the overlay is an
            // IOverlaySurface. Typed check avoids the DLR (no `dynamic`) on the 1-second tick.
            return CoreApp.Overlay is IOverlaySurface surface && surface.IsVisible;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDeeperActivelyPlaying()
    {
        try
        {
            // Typed cast instead of `dynamic` — CoreApp.DeeperHost is the Core EnhancementHostService
            // (or null) under the Avalonia heads that consume this service.
            return CoreApp.DeeperHost is EnhancementHostService host && host.IsActivelyPlaying;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the shared <see cref="IQuestService"/> once (lazily) and caches it so the
    /// per-second tracker pays neither DI resolution nor reflection on the hot path. Prefers the
    /// DI-registered singleton and falls back to the legacy <see cref="CoreApp.Quests"/> locator.
    /// </summary>
    private IQuestService? ResolveQuestService()
    {
        if (_questServiceResolved) return _questService;
        _questService = CoreApp.Services?.GetService<IQuestService>() ?? CoreApp.Quests as IQuestService;
        if (_questService != null) _questServiceResolved = true;
        return _questService;
    }

    /// <summary>
    /// Resolves the <see cref="IAutonomyService"/> once (lazily) and caches it, replacing the
    /// per-tick <c>GetService</c> call in the time tracker.
    /// </summary>
    private IAutonomyService? ResolveAutonomyService()
    {
        if (_autonomyServiceResolved) return _autonomyService;
        _autonomyService = CoreApp.Services?.GetService<IAutonomyService>();
        if (_autonomyService != null) _autonomyServiceResolved = true;
        return _autonomyService;
    }

    /// <summary>
    /// Forwards achievement-side events into the quest tracker via strongly typed calls.
    /// Replaces the previous <c>MethodInfo.Invoke</c> reflection dispatch so the 1-second
    /// tracking tick stays reflection-free.
    /// </summary>
    private void TryQuestTrack(string methodName, params object[] args)
    {
        var quests = ResolveQuestService();
        if (quests == null) return;

        try
        {
            switch (methodName)
            {
                case "TrackPinkFilterMinutes": quests.TrackPinkFilterMinutes((double)args[0]); break;
                case "TrackSpiralMinutes": quests.TrackSpiralMinutes((double)args[0]); break;
                case "TrackBrainDrainMinutes": quests.TrackBrainDrainMinutes((double)args[0]); break;
                case "TrackAutonomyMinutes": quests.TrackAutonomyMinutes((double)args[0]); break;
                case "TrackVideoMinutes": quests.TrackVideoMinutes((double)args[0]); break;
                case "TrackFlashImage": quests.TrackFlashImage(); break;
                case "TrackBubblePopped": quests.TrackBubblePopped(); break;
                case "TrackLockCardCompleted": quests.TrackLockCardCompleted(); break;
                case "TrackSessionCompleted": quests.TrackSessionCompleted(); break;
                default:
                    _logger.LogDebug("Unmapped quest track call {MethodName}", methodName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Quest track call {MethodName} failed", methodName);
        }
    }

    private void TryHapticAchievementPattern()
    {
        try
        {
            var haptics = CoreApp.Haptics;
            if (haptics == null) return;

            var method = haptics.GetType().GetMethod("AchievementPatternAsync");
            if (method == null) return;

            _ = method.Invoke(haptics, null);
            _ = method.Invoke(haptics, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Achievement haptic pattern failed");
        }
    }

    private void TryHapticAvatarEasterEggPattern()
    {
        try
        {
            var haptics = CoreApp.Haptics;
            if (haptics == null) return;

            var method = haptics.GetType().GetMethod("AvatarEasterEggPatternAsync");
            if (method == null) return;

            _ = method.Invoke(haptics, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Avatar easter-egg haptic pattern failed");
        }
    }
}
