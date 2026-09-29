using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The lock as last seen. Everything on screen that shows the lock (the rail chip's
/// clock, the page's hero) reads this and nothing else, so the app asks Chaster for it in one
/// place and on one cadence.</summary>
public sealed record LockSnapshot(string Id, string? Title, DateTime? EndsAtUtc, bool IsFrozen,
    bool TimerHidden, bool IsTestLock, DateTime FetchedAtUtc)
{
    /// <summary>When the lock started, for the calendar. Null when Chaster did not say; the
    /// page then counts from today.</summary>
    public DateTime? StartedAtUtc { get; init; }

    /// <summary>Time left on the lock, counted locally from the end date. Null when the timer
    /// is hidden or the lock has no end date. A frozen lock does not run down, so it reads what
    /// was left at the fetch.</summary>
    public TimeSpan? Remaining(DateTime utcNow)
    {
        if (TimerHidden || EndsAtUtc is not { } end) return null;
        var left = end - (IsFrozen ? FetchedAtUtc : utcNow);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}

/// <summary>Why <see cref="ChasterService.Lock"/> is what it is.</summary>
public enum LockLookup
{
    /// <summary>No account linked.</summary>
    Unlinked,
    /// <summary>Linked, but Chaster could not be asked just now; Lock holds the last answer.</summary>
    Away,
    /// <summary>Linked, no active lock.</summary>
    None,
    /// <summary>Linked, and the lock the player picked is active.</summary>
    Chosen,
    /// <summary>Linked, at least one lock, none picked (or the pick is gone).</summary>
    Ambiguous,
}
