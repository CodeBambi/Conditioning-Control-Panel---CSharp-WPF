using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Central service for tracking achievement progress and unlocking achievements
/// </summary>
public class AchievementService : IDisposable
{
    private AchievementProgress _progress;
    private readonly string _progressPath;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _trackingTimer;
    private bool _isDirty;
    private DateTime _lastPinkFilterCheck;
    private DateTime _lastSpiralCheck;
    private DateTime _lastMindWipeCheck;
    
    public event EventHandler<Achievement>? AchievementUnlocked;
    
    public AchievementProgress Progress => _progress;
    
    public AchievementService()
    {
        _progressPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConditioningControlPanel",
            "achievements.json");
        
        _progress = LoadProgress();
        
        // Check daily streak on startup
        _progress.UpdateDailyStreak();
        _isDirty = true;
        
        // Auto-save every 30 seconds if dirty
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (s, e) =>
        {
            if (_isDirty)
            {
                Save();
                _isDirty = false;
            }
        };
        _saveTimer.Start();
        
        // Track time-based achievements every second
        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _trackingTimer.Tick += TrackTimeBasedProgress;
        _trackingTimer.Start();
        
        // Note: Level achievements are checked in App.xaml.cs AFTER event handler is wired
        // Check daily maintenance achievement
        CheckDailyMaintenance();
        
        App.Logger?.Information("AchievementService initialized. {Count} achievements unlocked.", 
            _progress.UnlockedAchievements.Count);
    }
    
    private AchievementProgress LoadProgress()
    {
        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<AchievementProgress>(json) ?? new AchievementProgress();
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to load achievement progress");
        }
        
        return new AchievementProgress();
    }
    
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_progressPath, json);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to save achievement progress");
        }
    }
    
    /// <summary>
    /// Track time-based progress (called every second)
    /// </summary>
    private void TrackTimeBasedProgress(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;
        var now = DateTime.Now;
        
        // Track Pink Filter time
        if (settings.PinkFilterEnabled)
        {
            var elapsed = (now - _lastPinkFilterCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 1) // Sanity check
            {
                _progress.TotalPinkFilterMinutes += elapsed;
                _isDirty = true;
                
                // Check Rose-Tinted Reality (10 hours = 600 minutes)
                if (_progress.TotalPinkFilterMinutes >= 600)
                {
                    TryUnlock("rose_tinted_reality");
                }
            }
        }
        _lastPinkFilterCheck = now;
        
        // Track Spiral time
        if (settings.SpiralEnabled)
        {
            var elapsed = (now - _lastSpiralCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 1)
            {
                _progress.TotalSpiralMinutes += elapsed;
                _progress.ContinuousSpiralMinutes += elapsed;
                _isDirty = true;
                
                // Check Spiral Eyes (20 minutes continuous)
                if (_progress.ContinuousSpiralMinutes >= 20)
                {
                    TryUnlock("spiral_eyes");
                }
            }
        }
        else
        {
            // Reset continuous spiral time when disabled
            _progress.ContinuousSpiralMinutes = 0;
        }
        _lastSpiralCheck = now;
        
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
        // Note: !PanicKeyEnabled means panic is disabled
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
    /// Check and unlock level-based achievements
    /// </summary>
    public void CheckLevelAchievements(int level)
    {
        if (level >= 10) TryUnlock("plastic_initiation");
        if (level >= 20) TryUnlock("dumb_bimbo");
        if (level >= 50) TryUnlock("fully_synthetic");
        if (level >= 100) TryUnlock("perfect_plastic_puppet");
    }
    
    /// <summary>
    /// Check Daily Maintenance achievement (7 days streak)
    /// </summary>
    public void CheckDailyMaintenance()
    {
        if (_progress.ConsecutiveDays >= 7)
        {
            TryUnlock("daily_maintenance");
        }
    }
    
    /// <summary>
    /// Track flash image shown
    /// </summary>
    public void TrackFlashImage()
    {
        _progress.TotalFlashImages++;
        _isDirty = true;
        
        if (_progress.TotalFlashImages >= 5000)
        {
            TryUnlock("retinal_burn");
        }
    }
    
    /// <summary>
    /// Track bubble popped
    /// </summary>
    public void TrackBubblePopped()
    {
        _progress.TotalBubblesPopped++;
        _isDirty = true;
        
        if (_progress.TotalBubblesPopped >= 1000)
        {
            TryUnlock("pop_the_thought");
        }
    }
    
    /// <summary>
    /// Track bubble count game result
    /// </summary>
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
        _isDirty = true;
    }
    
    /// <summary>
    /// Track Lock Card completion
    /// </summary>
    public void TrackLockCardCompletion(double seconds, int totalChars, int errors, int phrases)
    {
        _isDirty = true;
        
        // Check for perfect accuracy
        if (errors == 0)
        {
            _progress.HasPerfectLockCard = true;
            TryUnlock("typing_tutor");
        }
        
        // Check for speed (3 phrases in under 15 seconds)
        if (phrases >= 3 && seconds < 15)
        {
            if (seconds < _progress.FastestLockCardSeconds)
            {
                _progress.FastestLockCardSeconds = seconds;
            }
            TryUnlock("obedience_reflex");
        }
    }
    
    /// <summary>
    /// Track attention check failure
    /// </summary>
    public void TrackAttentionCheckFailed()
    {
        _progress.AttentionCheckFailures++;
        _isDirty = true;
        
        if (_progress.AttentionCheckFailures >= 3)
        {
            TryUnlock("mercy_beggar");
        }
    }
    
    /// <summary>
    /// Track Mind Wipe duration
    /// </summary>
    public void TrackMindWipeDuration(double seconds)
    {
        _progress.ContinuousMindWipeSeconds = seconds;
        _isDirty = true;
        
        if (seconds >= 60)
        {
            TryUnlock("clean_slate");
        }
    }
    
    /// <summary>
    /// Track bouncing text corner hit
    /// </summary>
    public void TrackCornerHit()
    {
        if (!_progress.HasHitCorner)
        {
            _progress.HasHitCorner = true;
            _isDirty = true;
            TryUnlock("corner_hit");
        }
    }
    
    /// <summary>
    /// Track avatar click
    /// </summary>
    public void TrackAvatarClick()
    {
        var clickCount = _progress.AvatarClickCount + 1;
        App.Logger?.Debug("TrackAvatarClick called. Current count will be: {Count}", clickCount);
        
        if (_progress.TrackAvatarClick())
        {
            App.Logger?.Information("🎯 20 clicks reached! Unlocking Neon Obsession...");
            TryUnlock("neon_obsession");
        }
        _isDirty = true;
    }
    
    /// <summary>
    /// Track Alt+Tab during session
    /// </summary>
    public void TrackAltTab()
    {
        _progress.AltTabPressedThisSession = true;
        _isDirty = true;
    }
    
    /// <summary>
    /// Track panic/ESC button press
    /// </summary>
    public void TrackPanicPressed()
    {
        _progress.LastPanicPressTime = DateTime.Now;
        _isDirty = true;
    }
    
    /// <summary>
    /// Track session start (check for Relapse achievement)
    /// </summary>
    public void TrackSessionStart()
    {
        _progress.ResetSessionTracking();
        
        // Check Relapse (started within 10 seconds of panic)
        if (_progress.LastPanicPressTime.HasValue)
        {
            var elapsed = (DateTime.Now - _progress.LastPanicPressTime.Value).TotalSeconds;
            if (elapsed <= 10)
            {
                TryUnlock("relapse");
            }
        }
        
        _isDirty = true;
    }
    
    /// <summary>
    /// Track session completion
    /// </summary>
    public void TrackSessionComplete(string sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled)
    {
        App.Logger?.Information("TrackSessionComplete called: Session={Name}, Duration={Duration:F1}min, NoPanic={NoPanic}, StrictLock={Strict}",
            sessionName, durationMinutes, noPanicEnabled, strictLockEnabled);
        
        _progress.CompletedSessions.Add(sessionName);
        
        // Update longest session
        if (durationMinutes > _progress.LongestSessionMinutes)
        {
            _progress.LongestSessionMinutes = durationMinutes;
        }
        
        // Deep Sleep Mode (3+ hours = 180 minutes)
        if (durationMinutes >= 180)
        {
            TryUnlock("deep_sleep");
        }
        
        // What Panic Button (completed with no panic enabled)
        if (noPanicEnabled)
        {
            App.Logger?.Information("No panic was enabled - unlocking 'what_panic_button'");
            _progress.CompletedSessionWithNoPanic = true;
            TryUnlock("what_panic_button");
        }
        
        // Session-specific achievements
        var sessionLower = sessionName.ToLowerInvariant();
        
        // Sofa Decor - Complete "The Distant Doll"
        if (sessionLower.Contains("distant doll"))
        {
            TryUnlock("sofa_decor");
        }
        
        // Look But Don't Touch - Complete "Good Girls Don't Cum" with Strict Lock
        if (sessionLower.Contains("good girls") && strictLockEnabled)
        {
            _progress.CompletedGoodGirlsWithStrictLock = true;
            TryUnlock("look_but_dont_touch");
        }
        
        // Morning Glory - Complete "Morning Drift" between 6-9 AM
        if (sessionLower.Contains("morning drift"))
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 6 && hour < 9)
            {
                _progress.CompletedMorningDriftInMorning = true;
                TryUnlock("morning_glory");
            }
        }
        
        // Player 2 Disconnected - Complete "Gamer Girl" without Alt+Tab
        if (sessionLower.Contains("gamer girl") && !_progress.AltTabPressedThisSession)
        {
            _progress.CompletedGamerGirlNoAltTab = true;
            TryUnlock("player_2_disconnected");
        }
        
        _isDirty = true;
    }
    
    /// <summary>
    /// Try to unlock an achievement (only fires event if not already unlocked)
    /// </summary>
    public bool TryUnlock(string achievementId)
    {
        App.Logger?.Debug("TryUnlock called for: {Id}", achievementId);
        
        if (_progress.IsUnlocked(achievementId))
        {
            App.Logger?.Debug("Achievement {Id} already unlocked", achievementId);
            return false; // Already unlocked
        }
        
        if (!Achievement.All.TryGetValue(achievementId, out var achievement))
        {
            App.Logger?.Warning("Unknown achievement ID: {Id}", achievementId);
            return false;
        }
        
        _progress.Unlock(achievementId);
        _isDirty = true;
        Save(); // Save immediately on unlock
        
        App.Logger?.Information("🏆 Achievement unlocked: {Name} (ID: {Id})", achievement.Name, achievementId);
        
        // Fire event to show popup
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                App.Logger?.Debug("Firing AchievementUnlocked event for: {Name}", achievement.Name);
                AchievementUnlocked?.Invoke(this, achievement);
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to fire achievement event");
        }
        
        return true;
    }
    
    /// <summary>
    /// Get unlock count
    /// </summary>
    public int GetUnlockedCount() => _progress.UnlockedAchievements.Count;
    
    /// <summary>
    /// Get total achievement count
    /// </summary>
    public int GetTotalCount() => Achievement.All.Count;
    
    public void Dispose()
    {
        _saveTimer.Stop();
        _trackingTimer.Stop();
        Save();
    }
}
