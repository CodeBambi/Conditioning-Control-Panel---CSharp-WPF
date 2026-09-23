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
    /// <summary>Linked, one lock chosen (or the only one there is).</summary>
    Chosen,
    /// <summary>Linked, several locks, none picked yet.</summary>
    Ambiguous,
}

public sealed partial class ChasterService
{
    private LockSnapshot? _lock;
    private LockLookup _lockLookup = LockLookup.Unlinked;

    /// <summary>The lock as last fetched, or null. See <see cref="LockLookup"/> for why.</summary>
    public LockSnapshot? Lock { get { lock (_gate) return _lock; } }

    public LockLookup LockLookup { get { lock (_gate) return _lockLookup; } }

    /// <summary>The lock snapshot changed (or its lookup did). Raised on whatever thread fetched it.</summary>
    public event Action? LockChanged;

    /// <summary>How long nothing adds because Panic or an emergency exit was used. Zero when no hold runs.</summary>
    public TimeSpan SafetyHoldRemaining
    {
        get
        {
            lock (_gate)
            {
                var left = _safetyUntilUtc - _utcNow();
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
    }

    /// <summary>Ask Chaster for the lock and remember the answer. Safe to call from any thread;
    /// never throws. Returns the snapshot now held (the old one when Chaster is away).</summary>
    public async Task<LockSnapshot?> RefreshLockAsync(CancellationToken ct = default)
    {
        try
        {
            if (!IsLinked) return SetLock(null, LockLookup.Unlinked);
            _ = EnsureProfileAsync(ct); // once per link; a no-op once the name is in hand
            var locks = await GetLocksAsync(ct).ConfigureAwait(false);
            if (locks == null) return SetLock(Lock, LockLookup.Away);
            var chosenId = (_options() ?? ChasterOptions.Off).LockId;
            var pick = locks.FirstOrDefault(l => l.Id == chosenId) ?? (locks.Count == 1 ? locks[0] : null);
            if (pick == null) return SetLock(null, locks.Count == 0 ? LockLookup.None : LockLookup.Ambiguous);
            var ends = pick.EndDate is { } end ? (end.Kind == DateTimeKind.Utc ? end : end.ToUniversalTime()) : (DateTime?)null;
            var started = pick.StartDate is { } from ? (from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime()) : (DateTime?)null;
            return SetLock(new LockSnapshot(pick.Id, pick.Title, ends, pick.IsFrozen, pick.TimerHidden, pick.IsTestLock, _utcNow()) { StartedAtUtc = started }, LockLookup.Chosen);
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "chaster lock refresh");
            return Lock;
        }
    }

    private LockSnapshot? SetLock(LockSnapshot? snapshot, LockLookup lookup)
    {
        bool changed;
        lock (_gate)
        {
            changed = !Equals(_lock, snapshot) || _lockLookup != lookup;
            _lock = snapshot;
            _lockLookup = lookup;
        }
        if (changed) LockChanged?.Invoke();
        return snapshot;
    }
}
