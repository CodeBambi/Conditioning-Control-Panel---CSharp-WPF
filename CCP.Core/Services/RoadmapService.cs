using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Event args for roadmap step completion
/// </summary>
public class RoadmapStepCompletedEventArgs : EventArgs
{
    public RoadmapStepDefinition StepDefinition { get; }
    public RoadmapStepProgress StepProgress { get; }
    public bool UnlockedNewTrack { get; }
    public bool EarnedBadge { get; }

    public RoadmapStepCompletedEventArgs(RoadmapStepDefinition stepDef, RoadmapStepProgress progress,
        bool unlockedNewTrack, bool earnedBadge)
    {
        StepDefinition = stepDef;
        StepProgress = progress;
        UnlockedNewTrack = unlockedNewTrack;
        EarnedBadge = earnedBadge;
    }
}

/// <summary>
/// Service for managing the Transformation Roadmap feature
/// </summary>
public class RoadmapService : IDisposable
{
    private readonly string _progressPath;
    private readonly string _diaryFolderPath;
    private readonly Timer _saveTimer;
    private bool _isDirty;
    private bool _disposed;

    public RoadmapProgress Progress { get; private set; }
    public string DiaryFolderPath => _diaryFolderPath;

    public event EventHandler<RoadmapStepCompletedEventArgs>? StepCompleted;
    public event EventHandler<RoadmapTrack>? TrackUnlocked;
    public event EventHandler? BadgeEarned;

    public RoadmapService()
    {
        _progressPath = Path.Combine(
            CorePaths.UserData,
            "roadmap.json");

        _diaryFolderPath = Path.Combine(
            CorePaths.UserData,
            "roadmap_diary");

        Progress = LoadProgress();
        EnsureDiaryFolderExists();

        // Auto-save every 30 seconds if dirty. Was a WPF DispatcherTimer, so the tick ran on the
        // UI thread and Save() could never serialize Progress while the UI mutated it. A
        // System.Threading.Timer ticks on the thread pool, so the body hops back through
        // CoreDispatch to keep that guarantee where a head has seeded a UI thread; unseeded (the
        // Avalonia head today, the smoke runner, the tests) Post runs it in place, which is the
        // same contract every other Core caller already has.
        _saveTimer = new Timer(_ => CoreDispatch.Post(() =>
        {
            if (_isDirty)
            {
                Save();
                _isDirty = false;
            }
        }), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        Log.Information("RoadmapService initialized. Track1: {T1}, Track2: {T2}, Track3: {T3}",
            Progress.Track1Unlocked, Progress.Track2Unlocked, Progress.Track3Unlocked);
    }

    #region Persistence

    private RoadmapProgress LoadProgress()
    {
        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<RoadmapProgress>(json) ?? new RoadmapProgress();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load roadmap progress");
        }

        return new RoadmapProgress();
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

            var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_progressPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save roadmap progress");
        }
    }

    private void EnsureDiaryFolderExists()
    {
        try
        {
            if (!Directory.Exists(_diaryFolderPath))
            {
                Directory.CreateDirectory(_diaryFolderPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create roadmap diary folder");
        }
    }

    #endregion

    #region Track & Step Status

    /// <summary>
    /// Check if a track is unlocked
    /// </summary>
    public bool IsTrackUnlocked(RoadmapTrack track)
    {
        return Progress.IsTrackUnlocked(track);
    }

    /// <summary>
    /// Check if a step is the currently active step (next to complete)
    /// </summary>
    public bool IsStepActive(string stepId)
    {
        var stepDef = RoadmapStepDefinition.GetById(stepId);
        if (stepDef == null) return false;

        var activeStep = Progress.GetActiveStep(stepDef.Track);
        return activeStep == stepId;
    }

    /// <summary>
    /// Check if a step is completed
    /// </summary>
    public bool IsStepCompleted(string stepId)
    {
        return Progress.IsStepCompleted(stepId);
    }

    /// <summary>
    /// Check if a step is locked (not active and not completed)
    /// </summary>
    public bool IsStepLocked(string stepId)
    {
        return !IsStepActive(stepId) && !IsStepCompleted(stepId);
    }

    /// <summary>
    /// Get progress for a specific step
    /// </summary>
    public RoadmapStepProgress? GetStepProgress(string stepId)
    {
        return Progress.GetStepProgress(stepId);
    }

    /// <summary>
    /// Get completion stats for a track
    /// </summary>
    public (int completed, int total) GetTrackProgress(RoadmapTrack track)
    {
        return Progress.GetTrackStats(track);
    }

    #endregion

    #region Step Actions

    /// <summary>
    /// Start working on a step (records StartedAt time)
    /// </summary>
    public void StartStep(string stepId)
    {
        if (!IsStepActive(stepId)) return;

        if (!Progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId);
            Progress.CompletedSteps[stepId] = progress;
        }

        if (progress.StartedAt == null)
        {
            progress.StartedAt = DateTime.Now;

            // Record journey start time if this is the first step ever
            if (Progress.JourneyStartedAt == null)
            {
                Progress.JourneyStartedAt = DateTime.Now;
            }

            _isDirty = true;
            Log.Information("Roadmap step started: {StepId}", stepId);
        }
    }

    /// <summary>
    /// Submit a photo to complete a step
    /// </summary>
    public void SubmitPhoto(string stepId, string sourcePhotoPath, string? note)
    {
        if (!IsStepActive(stepId))
        {
            Log.Warning("Attempted to submit photo for non-active step: {StepId}", stepId);
            return;
        }

        var stepDef = RoadmapStepDefinition.GetById(stepId);
        if (stepDef == null) return;

        // Get or create progress
        if (!Progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId);
            Progress.CompletedSteps[stepId] = progress;
        }

        // Copy photo to diary folder
        var savedPhotoPath = SavePhotoToDiary(stepId, sourcePhotoPath);

        // Calculate time taken
        int timeMinutes = 0;
        if (progress.StartedAt.HasValue)
        {
            timeMinutes = (int)(DateTime.Now - progress.StartedAt.Value).TotalMinutes;
        }

        // Update progress
        progress.IsCompleted = true;
        progress.CompletedAt = DateTime.Now;
        progress.PhotoPath = savedPhotoPath;
        progress.UserNote = note;
        progress.TimeToCompleteMinutes = timeMinutes;

        // Update statistics
        Progress.TotalStepsCompleted++;
        Progress.TotalPhotosSubmitted++;

        // Advance to next step
        var nextStepId = RoadmapStepDefinition.GetNextStepId(stepId);
        Progress.SetActiveStep(stepDef.Track, string.IsNullOrEmpty(nextStepId) ? null : nextStepId);

        // Check for track unlock (boss completion)
        bool unlockedNewTrack = false;
        bool earnedBadge = false;

        if (stepDef.StepType == RoadmapStepType.Boss)
        {
            switch (stepDef.Track)
            {
                case RoadmapTrack.EmptyDoll:
                    if (!Progress.Track2Unlocked)
                    {
                        Progress.UnlockTrack(RoadmapTrack.ObedientPuppet);
                        unlockedNewTrack = true;
                        TrackUnlocked?.Invoke(this, RoadmapTrack.ObedientPuppet);
                        Log.Information("Track 2 (Obedient Puppet) unlocked!");
                    }
                    break;

                case RoadmapTrack.ObedientPuppet:
                    if (!Progress.Track3Unlocked)
                    {
                        Progress.UnlockTrack(RoadmapTrack.SluttyBlowdoll);
                        unlockedNewTrack = true;
                        TrackUnlocked?.Invoke(this, RoadmapTrack.SluttyBlowdoll);
                        Log.Information("Track 3 (Slutty Blowdoll) unlocked!");
                    }
                    break;

                case RoadmapTrack.SluttyBlowdoll:
                    if (!Progress.HasCertifiedBlowdollBadge)
                    {
                        Progress.HasCertifiedBlowdollBadge = true;
                        Progress.JourneyCompletedAt = DateTime.Now;
                        earnedBadge = true;
                        BadgeEarned?.Invoke(this, EventArgs.Empty);
                        Log.Information("Certified Blowdoll badge earned!");
                    }
                    break;
            }
        }

        _isDirty = true;
        Save(); // Immediate save on completion

        // Fire completion event
        StepCompleted?.Invoke(this, new RoadmapStepCompletedEventArgs(stepDef, progress, unlockedNewTrack, earnedBadge));

        Log.Information("Roadmap step completed: {StepId}, Time: {Minutes} mins",
            stepId, timeMinutes);
    }

    /// <summary>
    /// Copy a photo to the diary folder with a unique name
    /// </summary>
    public string SavePhotoToDiary(string stepId, string sourcePhotoPath)
    {
        try
        {
            EnsureDiaryFolderExists();

            var extension = Path.GetExtension(sourcePhotoPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{stepId}_{timestamp}{extension}";
            var destPath = Path.Combine(_diaryFolderPath, fileName);

            File.Copy(sourcePhotoPath, destPath, overwrite: true);

            return fileName; // Return relative filename
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save photo to diary: {Source}", sourcePhotoPath);
            return "";
        }
    }

    /// <summary>
    /// Get full path to a diary photo
    /// </summary>
    public string GetFullPhotoPath(string relativePhotoPath)
    {
        if (string.IsNullOrEmpty(relativePhotoPath)) return "";
        return Path.Combine(_diaryFolderPath, relativePhotoPath);
    }

    /// <summary>
    /// Update the note for a completed step
    /// </summary>
    public void UpdateStepNote(string stepId, string? note)
    {
        if (Progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress.UserNote = note;
            _isDirty = true;
            Save();
        }
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _saveTimer.Dispose();

        if (_isDirty)
        {
            Save();
        }

        Log.Information("RoadmapService disposed");
    }

    #endregion
}
