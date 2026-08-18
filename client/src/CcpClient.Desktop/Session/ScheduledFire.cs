namespace CcpClient.Desktop.Session;

/// <summary>
/// One scheduled firing, as an IDENTITY rather than a bare timer handle.
///
/// <para><b>Why this exists (SP-101, hazard 3 from SP-098's review).</b> Before this, a paced effect
/// kept the raw handle in a <c>_pending</c> field and the callback opened with
/// <c>Interlocked.Exchange(ref _pending, null)</c> — clear the slot, whatever is in it. That is
/// wrong whenever the slot no longer holds the timer that is firing, which the product really can
/// produce: a frequency slider calls <c>RefreshSchedule</c>, the re-schedule replaces the handle,
/// and the OLD one-shot's callback can already be in flight on a pool thread. It then nulls the
/// slot belonging to the NEW timer. The consequences are not cosmetic — the effect's dot reports
/// <c>Armed</c> while a firing is genuinely on the clock, and, worse, the next <c>Disarm</c> finds
/// an empty slot and disposes nothing, leaving a live one-shot behind a stop. It survived only
/// because the stale-generation check downstream refuses to do the work.</para>
///
/// <para>With an identity, the callback can say "clear the slot <b>if it is still mine</b>" —
/// <c>Interlocked.CompareExchange(ref _pending, null, mine)</c> — which is exact, and it is in the
/// SHARED base, so every module that follows gets it rather than inheriting the race by
/// copy-paste.</para>
///
/// <para><b>Attach, and the window it closes.</b> The token is published to the pending slot BEFORE
/// the clock is asked for a handle, because a schedule due immediately can fire before
/// <see cref="ISessionClock.Schedule"/> even returns. Whoever wins, the outcome is the same: if the
/// token was cancelled while the clock was being asked, <see cref="Attach"/> disposes the handle the
/// instant it arrives, so a stop can never leave a timer nobody owns.</para>
/// </summary>
public sealed class ScheduledFire : IDisposable
{
    private IDisposable? _handle;
    private int _cancelled;

    /// <summary>True until this firing is cancelled or spent.</summary>
    public bool Alive => Volatile.Read(ref _cancelled) == 0;

    /// <summary>Hand the token the clock handle it stands for. Safe to call after cancellation: the
    /// handle is disposed immediately in that case.</summary>
    public void Attach(IDisposable handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Interlocked.Exchange(ref _handle, handle);
        if (!Alive)
        {
            Interlocked.Exchange(ref _handle, null)?.Dispose();
        }
    }

    /// <summary>Cancel this firing and drop its clock handle. Idempotent, lock-free, and safe from a
    /// cancellation callback on any thread.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _cancelled, 1);
        Interlocked.Exchange(ref _handle, null)?.Dispose();
    }
}
