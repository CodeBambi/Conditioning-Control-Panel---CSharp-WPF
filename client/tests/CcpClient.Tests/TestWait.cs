namespace CcpClient.Tests;

/// <summary>
/// The ONE approved bounded-window wait for tests (SP-059 timing discipline — the third
/// occurrence of the wall-clock-flake class, encoded: T-15 SP-041, T-16 SP-043). Every test
/// wait is either a deterministic signal or THIS helper; hard-coded deadline literals anywhere
/// else in <c>client/tests/**</c> fail <c>TestTimingGuardTests</c>.
///
/// On window expiry the failure LEADS with a greppable verdict token (it survives into TRX
/// failure names — the SP-058 land lesson) and appends environment + actor evidence, because
/// "the condition never became true" (a real product/test failure) and "this machine was slow"
/// are different verdicts and must be reported differently:
///
///   TIMING-VERDICT:CONDITION-NEVER-TRUE — the poll loop ran on schedule and the condition
///       stayed false for the whole window: treat as a REAL failure.
///   TIMING-VERDICT:ENVIRONMENT-STARVED — the wait loop itself was scheduler-starved: the
///       machine was too slow to observe the window; rerun or reduce load FIRST.
///
/// The verdict is a hypothesis; the EVIDENCE (polls, worst scheduler slip, thread-pool
/// backlog, caller-supplied actor-state snapshot) travels with it, because a cold-start flake
/// has the poll loop on schedule while the ACTOR is the starved side (pre-approach consult).
/// </summary>
public static class TestWait
{
    /// <summary>
    /// The tolerant default window: generous for cold/loaded machines. A genuine break costs
    /// up to this per wait site (failure path only). Never widen a per-test window to buy
    /// green — that is the banned fix this class exists to kill.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(20);

    private const int PollMs = 10;
    private const long StarvationSlipMs = 250;

    /// <summary>Monotonic milliseconds for elapsed-SUBJECT assertions (bounded-cancel, bounded-
    /// drain, retry-after-honored): the only wall-clock use permitted outside this helper. Using
    /// this names the intent (measurement, not a deadline) and keeps the raw token out of test
    /// files so the timing guard can ban it outright.</summary>
    public static long MonotonicNow() => Environment.TickCount64;

    /// <summary>Polls <paramref name="condition"/> until true or the window expires.</summary>
    /// <param name="what">Names the awaited condition; becomes the failure's subject.</param>
    /// <param name="state">Optional actor-progress snapshot (e.g. send/byte/hit counters) —
    /// the differential between "request never left" and "reached the actor, no reply".</param>
    public static async Task Until(
        Func<bool> condition, string what, Func<string>? state = null,
        TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var stats = await PumpAsync(condition, window ?? DefaultWindow, cancellationToken);
        if (stats.Met || condition())
        {
            return;
        }

        throw new Xunit.Sdk.XunitException(FailureMessage(what, state, stats));
    }

    /// <summary>Awaits a deterministic signal (task) inside the tolerant window.</summary>
    public static async Task Until(
        Task signal, string what, Func<string>? state = null, TimeSpan? window = null)
    {
        var effectiveWindow = window ?? DefaultWindow;
        using var timeoutCts = new CancellationTokenSource();
        var timeout = Task.Delay(effectiveWindow, timeoutCts.Token);
        // NO ConfigureAwait(false) anywhere in this helper: headless UI tests poll
        // dispatcher-owned state from the UI thread — continuations must resume on the
        // caller's context (AvaloniaFact runs the test on the dispatcher thread).
        var winner = await Task.WhenAny(signal, timeout);
        if (winner == signal)
        {
            await timeoutCts.CancelAsync(); // retire the window timer on early success
            await signal; // surface a faulted signal as itself
            return;
        }

        throw new Xunit.Sdk.XunitException(FailureMessage(what, state, Stats.ForSignal(effectiveWindow)));
    }

    /// <summary>Synchronous variant for fixtures that must pump a dispatcher inline.</summary>
    public static void UntilSync(
        Func<bool> condition, string what, Func<string>? state = null, TimeSpan? window = null)
    {
        var effectiveWindow = window ?? DefaultWindow;
        var started = Environment.TickCount64;
        var deadline = started + (long)effectiveWindow.TotalMilliseconds;
        var polls = 0;
        long worstSlip = 0;
        var last = started;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(PollMs);
            polls++;
            var now = Environment.TickCount64;
            worstSlip = Math.Max(worstSlip, now - last - PollMs);
            last = now;
        }

        if (condition())
        {
            return;
        }

        throw new Xunit.Sdk.XunitException(
            FailureMessage(what, state, new Stats(false, polls, worstSlip, Environment.TickCount64 - started, effectiveWindow)));
    }

    private static async Task<Stats> PumpAsync(Func<bool> condition, TimeSpan window, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        var deadline = started + (long)window.TotalMilliseconds;
        var polls = 0;
        long worstSlip = 0;
        var last = started;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(PollMs, cancellationToken);
            polls++;
            var now = Environment.TickCount64;
            worstSlip = Math.Max(worstSlip, now - last - PollMs);
            last = now;
        }

        return new Stats(false, polls, worstSlip, Environment.TickCount64 - started, window);
    }

    private static string FailureMessage(string what, Func<string>? state, Stats stats)
    {
        // The verdict is a hypothesis from THIS thread's scheduling; the evidence is what
        // lets the reader confirm or overturn it (a starved ACTOR leaves this loop on
        // schedule — the state snapshot is the differential).
        var starved = stats.WorstSlipMs > StarvationSlipMs
            || stats.Polls < stats.Window.TotalMilliseconds / PollMs / 10;
        var verdict = starved
            ? "TIMING-VERDICT:ENVIRONMENT-STARVED — the wait loop itself was scheduler-starved " +
              $"while waiting for {what}: the machine was too slow to observe the full window; " +
              "rerun or reduce load BEFORE treating this as a failure"
            : $"TIMING-VERDICT:CONDITION-NEVER-TRUE — waited the full {stats.Window.TotalSeconds:0.#}s " +
              $"for {what} and it never became true: treat as a REAL product/test failure";
        string snapshot;
        try
        {
            snapshot = state?.Invoke() ?? "none supplied";
        }
        catch (Exception ex)
        {
            snapshot = $"<state snapshot threw {ex.GetType().Name}: {ex.Message}>";
        }

        return $"{verdict}. EVIDENCE: polls={stats.Polls}, elapsed={stats.ElapsedMs}ms, " +
               $"worst-scheduler-slip={stats.WorstSlipMs}ms, threadpool-pending={ThreadPool.PendingWorkItemCount}, " +
               $"actor-state: {snapshot}";
    }

    private sealed record Stats(bool Met, int Polls, long WorstSlipMs, long ElapsedMs, TimeSpan Window)
    {
        public static Stats ForSignal(TimeSpan window) => new(false, -1, -1, -1, window);
    }
}
