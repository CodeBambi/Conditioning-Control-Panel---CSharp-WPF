using System;

namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Cross-platform seam for the awareness engine that monitors the user's active
/// window and categorizes their activity. Privacy-focused: only categorizes,
/// never logs or stores window titles.
/// </summary>
/// <remarks>
/// Threading contract: the portable engine polls on a <see cref="System.Threading.Timer"/>
/// (not a UI dispatcher timer), so <see cref="ActivityChanged"/> and
/// <see cref="StillOnActivity"/> fire on a background thread-pool thread. Consumers must
/// marshal to their UI thread before touching UI state (the WPF consumers already marshal,
/// so the consumer-side contract is unchanged by the port).
/// </remarks>
public interface IAwarenessService
{
    /// <summary>Raised when the active activity category changes. May fire on a background thread.</summary>
    event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    /// <summary>Raised periodically when the user remains on a recognized activity. May fire on a background thread.</summary>
    event EventHandler<ActivityChangedEventArgs>? StillOnActivity;

    /// <summary>Current detected activity category.</summary>
    ActivityCategory CurrentActivity { get; }

    /// <summary>Current detected app/service display name.</summary>
    string CurrentDetectedName { get; }

    /// <summary>Current detected service/platform name.</summary>
    string CurrentServiceName { get; }

    /// <summary>Current page/content title, if any.</summary>
    string CurrentPageTitle { get; }

    /// <summary>How long the user has been on the current activity.</summary>
    TimeSpan CurrentActivityDuration { get; }

    /// <summary>Whether the service is actively monitoring.</summary>
    bool IsRunning { get; }

    /// <summary>Starts monitoring if enabled and consented.</summary>
    void Start();

    /// <summary>Stops monitoring and clears state.</summary>
    void Stop();

    /// <summary>True if enough time has passed since the last reaction.</summary>
    bool CanReact();

    /// <summary>True if enough time has passed since the last "still on" reaction.</summary>
    bool CanStillOnReact();

    /// <summary>Marks that a reaction was just shown.</summary>
    void MarkReaction();

    /// <summary>Marks that a "still on" comment was just shown.</summary>
    void MarkStillOnReaction();
}
