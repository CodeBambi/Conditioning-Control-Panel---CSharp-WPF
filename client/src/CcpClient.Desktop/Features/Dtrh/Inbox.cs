namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The §3.3 host→page inbox (dtrh-admission.md): sequence-numbered retained delivery.
/// Every host→page message carries a monotonic seq; messages are RETAINED until the next
/// poll's `after` acknowledges them (exactly-once at the page, immune to a lost response);
/// a late-registering page re-reads from its last seq — preBuffer-replay equivalence
/// (§3.2). Long-poll: <see cref="PollAsync"/> hangs until at least one message exists or
/// the bounded timeout elapses, then returns empty. Thread-safe; the page is the only
/// poller (single-waiter by protocol, multi-waiter tolerated).
/// </summary>
public sealed class Inbox
{
    private readonly object _gate = new();
    private readonly List<(long Seq, string Body)> _retained = [];
    private long _nextSeq;
    private bool _released;
    private TaskCompletionSource _signal = NewSignal();

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Enqueue a host→page message body (JSON). Returns its seq. First seq is 1.</summary>
    public long Enqueue(string body)
    {
        TaskCompletionSource toRelease;
        lock (_gate)
        {
            var seq = ++_nextSeq;
            _retained.Add((seq, body));
            _released = false;
            toRelease = _signal;
            _signal = NewSignal();
        }

        toRelease.TrySetResult(); // release outside the gate: continuations never run under the lock
        return Volatile.Read(ref _nextSeq);
    }

    /// <summary>
    /// Long-poll: returns every retained message with seq &gt; <paramref name="after"/>,
    /// waiting up to <paramref name="timeout"/> for at least one to arrive. The `after`
    /// parameter IS the acknowledgment: messages with seq &lt;= after are purged.
    /// </summary>
    public async Task<IReadOnlyList<(long Seq, string Body)>> PollAsync(
        long after, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        for (;;)
        {
            Task wait;
            lock (_gate)
            {
                _retained.RemoveAll(m => m.Seq <= after); // the ack: purge acknowledged messages
                if (_retained.Count > 0)
                {
                    return _retained.ToArray();
                }

                if (_released)
                {
                    return []; // teardown released the poller — never loop back into a wait
                }

                wait = _signal.Task;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return [];
            }

            var delay = Task.Delay(remaining, cancellationToken);
            var completed = await Task.WhenAny(wait, delay).ConfigureAwait(false);
            if (completed == delay)
            {
                // Distinguish timeout from cancellation honestly: a cancelled delay that
                // wins the race must still throw.
                if (delay.IsCanceled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return [];
            }
        }
    }

    /// <summary>Teardown: release any hanging poller with an empty result (consult correction).
    /// Sticky until the next Enqueue: a released poller must never loop back into a wait.</summary>
    public void ReleaseAll()
    {
        TaskCompletionSource toRelease;
        lock (_gate)
        {
            _released = true;
            toRelease = _signal;
            _signal = NewSignal();
        }

        toRelease.TrySetResult();
    }
}
