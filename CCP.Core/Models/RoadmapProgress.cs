using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Tracks user progress through the Transformation Roadmap
/// </summary>
public class RoadmapProgress
{
    // Track unlock states
    public bool Track1Unlocked { get; set; } = true;   // Default available
    public bool Track2Unlocked { get; set; } = false;  // Unlock after Track 1 boss
    public bool Track3Unlocked { get; set; } = false;  // Unlock after Track 2 boss

    // Completed steps dictionary (stepId -> progress data)
    public Dictionary<string, RoadmapStepProgress> CompletedSteps { get; set; } = new();

    // Currently active step per track (the next step to complete)
    public string? ActiveTrack1Step { get; set; } = "t1_step1";
    public string? ActiveTrack2Step { get; set; }
    public string? ActiveTrack3Step { get; set; }

    // Badge earned upon completing Track 3
    public bool HasCertifiedBlowdollBadge { get; set; } = false;

    // Statistics
    public int TotalStepsCompleted { get; set; }
    public int TotalPhotosSubmitted { get; set; }
    public DateTime? JourneyStartedAt { get; set; }
    public DateTime? JourneyCompletedAt { get; set; }

    /// <summary>
    /// Check if a track is unlocked
    /// </summary>
    public bool IsTrackUnlocked(RoadmapTrack track)
    {
        return track switch
        {
            RoadmapTrack.EmptyDoll => Track1Unlocked,
            RoadmapTrack.ObedientPuppet => Track2Unlocked,
            RoadmapTrack.SluttyBlowdoll => Track3Unlocked,
            _ => false
        };
    }

    /// <summary>
    /// Get the currently active step for a track
    /// </summary>
    public string? GetActiveStep(RoadmapTrack track)
    {
        return track switch
        {
            RoadmapTrack.EmptyDoll => ActiveTrack1Step,
            RoadmapTrack.ObedientPuppet => ActiveTrack2Step,
            RoadmapTrack.SluttyBlowdoll => ActiveTrack3Step,
            _ => null
        };
    }

    /// <summary>
    /// Set the active step for a track
    /// </summary>
    public void SetActiveStep(RoadmapTrack track, string? stepId)
    {
        switch (track)
        {
            case RoadmapTrack.EmptyDoll:
                ActiveTrack1Step = stepId;
                break;
            case RoadmapTrack.ObedientPuppet:
                ActiveTrack2Step = stepId;
                break;
            case RoadmapTrack.SluttyBlowdoll:
                ActiveTrack3Step = stepId;
                break;
        }
    }

    /// <summary>
    /// Unlock a track
    /// </summary>
    public void UnlockTrack(RoadmapTrack track)
    {
        switch (track)
        {
            case RoadmapTrack.EmptyDoll:
                Track1Unlocked = true;
                break;
            case RoadmapTrack.ObedientPuppet:
                Track2Unlocked = true;
                ActiveTrack2Step = RoadmapStepDefinition.GetFirstStepId(RoadmapTrack.ObedientPuppet);
                break;
            case RoadmapTrack.SluttyBlowdoll:
                Track3Unlocked = true;
                ActiveTrack3Step = RoadmapStepDefinition.GetFirstStepId(RoadmapTrack.SluttyBlowdoll);
                break;
        }
    }

    /// <summary>
    /// Check if a step is completed
    /// </summary>
    public bool IsStepCompleted(string stepId)
    {
        return CompletedSteps.TryGetValue(stepId, out var progress) && progress.IsCompleted;
    }

    /// <summary>
    /// Get progress for a specific step
    /// </summary>
    public RoadmapStepProgress? GetStepProgress(string stepId)
    {
        return CompletedSteps.TryGetValue(stepId, out var progress) ? progress : null;
    }

    /// <summary>
    /// Get completion stats for a track
    /// </summary>
    public (int completed, int total) GetTrackStats(RoadmapTrack track)
    {
        var steps = RoadmapStepDefinition.GetStepsForTrack(track);
        var completed = 0;
        foreach (var step in steps)
        {
            if (IsStepCompleted(step.Id))
                completed++;
        }
        return (completed, steps.Count);
    }
}

/// <summary>
/// Progress data for a single roadmap step
/// </summary>
public class RoadmapStepProgress
{
    public string StepId { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? PhotoPath { get; set; }      // Relative path within diary folder
    public string? UserNote { get; set; }
    public int TimeToCompleteMinutes { get; set; }

    public RoadmapStepProgress() { }

    public RoadmapStepProgress(string stepId)
    {
        StepId = stepId;
    }
}
