using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

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
            // The picked lock and nothing else: one lock is still the player's to pick.
            var pick = string.IsNullOrEmpty(chosenId) ? null : locks.FirstOrDefault(l => l.Id == chosenId);
            if (pick == null) return SetLock(null, locks.Count == 0 ? LockLookup.None : LockLookup.Ambiguous);
            NoteAddPermission(pick.Id, pick.WearerMayAddTime);
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

    /// <summary>The lock id whose keyholder does not let the wearer add time, learned from the
    /// lock's own permissions or from a 403 on the add. Forgotten on any link change, and when a
    /// read of that lock says adding is allowed again.</summary>
    private volatile string? _addsRefusedLockId;

    /// <summary>The chosen lock does not take time from this wearer: whatever the tab holds waits
    /// on the tab. Not an outage and not a dead link; the page says so in its own words.</summary>
    public bool AddsBlocked => IsLinked && AddsBlockedFor((_options() ?? ChasterOptions.Off).LockId);

    private bool AddsBlockedFor(string? lockId) =>
        !string.IsNullOrEmpty(lockId) && string.Equals(_addsRefusedLockId, lockId, StringComparison.Ordinal);

    /// <summary>Remember what Chaster said about the wearer adding time to <paramref name="lockId"/>.
    /// False marks it blocked; true clears a block on that lock; null (not said) changes nothing,
    /// so a 403 stays remembered until a read says otherwise.</summary>
    private void NoteAddPermission(string lockId, bool? mayAdd)
    {
        bool changed;
        lock (_gate)
        {
            var before = AddsBlockedFor(lockId);
            if (mayAdd == false) _addsRefusedLockId = lockId;
            else if (mayAdd == true && before) _addsRefusedLockId = null;
            changed = before != AddsBlockedFor(lockId);
        }
        if (!changed) return;
        Serilog.Log.Information("[Chaster] the chosen lock {State} the wearer adding time", mayAdd == false ? "refuses" : "allows");
        try { LockChanged?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex, "chaster lock changed listener"); }
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
