using System;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia roadmap service. Mirrors the WPF <c>RoadmapService</c> behavior:
/// loads/saves <c>roadmap.json</c> under LocalApplicationData, copies submitted
/// photos into the shared <c>roadmap_diary</c> folder (storing only the relative
/// filename), auto-saves every 30 seconds when dirty, and persists immediately
/// on note updates.
/// </summary>
public sealed class AvaloniaRoadmapService : IRoadmapService, IDisposable
{
    private readonly string _progressPath;
    private readonly string _diaryFolderPath;
    private readonly ILogger<AvaloniaRoadmapService>? _logger;
    private readonly DispatcherTimer? _saveTimer;
    private bool _isDirty;
    private bool _disposed;

    private RoadmapProgress _progress;

    public RoadmapProgress Progress => _progress;

    public event EventHandler<RoadmapStepCompletedEventArgs>? StepCompleted;
    public event EventHandler<RoadmapTrack>? TrackUnlocked;

    public AvaloniaRoadmapService(ILogger<AvaloniaRoadmapService>? logger = null)
    {
        _logger = logger;
        _progressPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "roadmap.json");

        // Same folder name as the WPF head — both heads share the diary photos.
        _diaryFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "roadmap_diary");

        _progress = LoadProgress();
        EnsureDiaryFolderExists();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => SaveIfDirty();
        _saveTimer.Start();

        _logger?.LogInformation("AvaloniaRoadmapService initialized. Track1: {T1}, Track2: {T2}, Track3: {T3}",
            _progress.Track1Unlocked, _progress.Track2Unlocked, _progress.Track3Unlocked);
    }

    public bool IsTrackUnlocked(RoadmapTrack track) => _progress.IsTrackUnlocked(track);

    public bool IsStepCompleted(string stepId) => _progress.IsStepCompleted(stepId);

    public bool IsStepActive(string stepId)
    {
        var step = RoadmapStepDefinition.GetById(stepId);
        if (step == null) return false;
        if (!IsTrackUnlocked(step.Track)) return false;
        if (_progress.IsStepCompleted(stepId)) return false;

        var active = step.Track switch
        {
            RoadmapTrack.EmptyDoll => _progress.ActiveTrack1Step,
            RoadmapTrack.ObedientPuppet => _progress.ActiveTrack2Step,
            RoadmapTrack.SluttyBlowdoll => _progress.ActiveTrack3Step,
            _ => null
        };
        return active == stepId;
    }

    public RoadmapStepProgress? GetStepProgress(string stepId)
        => _progress.GetStepProgress(stepId);

    public (int completed, int total) GetTrackProgress(RoadmapTrack track)
        => _progress.GetTrackStats(track);

    public string? GetFullPhotoPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        // Absolute paths pass through (legacy Avalonia builds stored the raw picker
        // path); relative filenames resolve under roadmap_diary, matching WPF.
        if (Path.IsPathRooted(relativePath)) return relativePath;
        return Path.Combine(_diaryFolderPath, relativePath);
    }

    public void StartStep(string stepId)
    {
        // Local DateTime.Now everywhere, matching the WPF head — roadmap.json is
        // shared, so mixed UTC/local timestamps would skew durations and dates.
        if (!_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId) { StartedAt = DateTime.Now };
            _progress.CompletedSteps[stepId] = progress;
        }
        else if (progress.StartedAt == null)
        {
            progress.StartedAt = DateTime.Now;
        }

        if (_progress.JourneyStartedAt == null) _progress.JourneyStartedAt = DateTime.Now;
        _isDirty = true;
    }

    public void SubmitPhoto(string stepId, string photoPath, string? note)
    {
        var step = RoadmapStepDefinition.GetById(stepId);
        if (step == null) return;

        if (!_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress = new RoadmapStepProgress(stepId);
            _progress.CompletedSteps[stepId] = progress;
        }

        // Copy the photo into the shared diary folder and store the relative
        // filename, matching the WPF head's contract.
        var savedPhotoPath = SavePhotoToDiary(stepId, photoPath);

        progress.IsCompleted = true;
        progress.CompletedAt = DateTime.Now;
        progress.PhotoPath = savedPhotoPath;
        progress.UserNote = note;
        progress.TimeToCompleteMinutes = progress.StartedAt.HasValue
            ? (int)(DateTime.Now - progress.StartedAt.Value).TotalMinutes
            : 0;

        _progress.TotalStepsCompleted++;
        _progress.TotalPhotosSubmitted++;
        if (_progress.JourneyStartedAt == null) _progress.JourneyStartedAt = DateTime.Now;

        // Advance active step for this track
        var steps = RoadmapStepDefinition.GetStepsForTrack(step.Track);
        var next = steps.FirstOrDefault(s => s.StepNumber > step.StepNumber);
        _progress.SetActiveStep(step.Track, next?.Id);

        var unlockedNewTrack = false;
        if (step.StepType == RoadmapStepType.Boss)
        {
            var nextTrack = step.Track switch
            {
                RoadmapTrack.EmptyDoll => RoadmapTrack.ObedientPuppet,
                RoadmapTrack.ObedientPuppet => RoadmapTrack.SluttyBlowdoll,
                _ => (RoadmapTrack?)null
            };
            if (nextTrack.HasValue && !_progress.IsTrackUnlocked(nextTrack.Value))
            {
                _progress.UnlockTrack(nextTrack.Value);
                unlockedNewTrack = true;
                TrackUnlocked?.Invoke(this, nextTrack.Value);
            }
        }

        var earnedBadge = step.Track == RoadmapTrack.SluttyBlowdoll && step.StepType == RoadmapStepType.Boss;
        if (earnedBadge) _progress.HasCertifiedBlowdollBadge = true;

        StepCompleted?.Invoke(this, new RoadmapStepCompletedEventArgs(step, progress, unlockedNewTrack, earnedBadge));
        _isDirty = true;
        Save();
    }

    /// <summary>
    /// Copy a photo to the diary folder with a unique name. Returns the relative
    /// filename (empty string on failure), mirroring the WPF <c>RoadmapService</c>.
    /// </summary>
    private string SavePhotoToDiary(string stepId, string sourcePhotoPath)
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
            _logger?.LogError(ex, "Failed to save photo to diary: {Source}", sourcePhotoPath);
            return "";
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
            _logger?.LogError(ex, "Failed to create roadmap diary folder");
        }
    }

    public void UpdateStepNote(string stepId, string? note)
    {
        if (_progress.CompletedSteps.TryGetValue(stepId, out var progress))
        {
            progress.UserNote = note;
            _isDirty = true;
            Save();
            _logger?.LogInformation("Roadmap note updated for step {StepId}", stepId);
        }
    }

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
            _logger?.LogError(ex, "Failed to load roadmap progress");
        }

        return new RoadmapProgress();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_progressPath, json);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save roadmap progress");
        }
    }

    private void SaveIfDirty()
    {
        if (_isDirty) Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _saveTimer?.Stop(); } catch { }
        SaveIfDirty();
        _logger?.LogInformation("AvaloniaRoadmapService disposed");
    }
}
