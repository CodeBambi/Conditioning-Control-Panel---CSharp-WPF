namespace CcpClient.Tests;

/// <summary>
/// The ONE approved bounded-window wait for tests (timing discipline — the third
/// occurrence of the wall-clock-flake class, encoded: T-15, T-16). Every test
/// wait is either a deterministic signal or THIS helper; hard-coded deadline literals anywhere
/// else in <c>client/tests/**</c> fail <c>TestTimingGuardTests</c>.
///
/// On window expiry the failure LEADS with a greppable verdict token (it survives into TRX
/// failure names — an earlier land lesson) and appends environment + actor evidence, because
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

    /// <summary>
    /// The owner decree ("just increase the amount of budgets by a lot"): the ONE shared
    /// deadline budget for tests whose subject is NOT the budget's elapsing (classification,
    /// round-trip, payload shape, cancellation). Injected into product options so a cold
    /// first-ever build (JIT + HttpListener warmup) can never race the assertion. 60 s is
    /// ~75× the old 800 ms budgets and far beyond any plausible cold start, while staying
    /// FINITE: a genuinely wedged lab or product hang fails loudly in a minute instead of
    /// hanging the test host forever (this suite has no per-test timeout). Budgets whose
    /// ELAPSING is the subject keep their own short, pinned literals (population 2).
    /// </summary>
    public static readonly TimeSpan InjectedBudget = TimeSpan.FromSeconds(60);

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
        var started = MonotonicNow();
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

        throw new Xunit.Sdk.XunitException(
            FailureMessage(what, state, Stats.ForSignal(effectiveWindow, MonotonicNow() - started)));
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

    /// <summary>
    /// The verdict selector, and the ONE place either token is chosen.
    ///
    /// <para><b>The bug this shape exists to make impossible.</b> Both branches used to be reached
    /// through the poll heuristic alone, and the SIGNAL overload has no poll loop: it filled
    /// <c>Polls</c> with the sentinel <c>-1</c>, which is below a tenth of any window's expected
    /// polls, so EVERY expired signal wait emitted
    /// <c>TIMING-VERDICT:ENVIRONMENT-STARVED — … rerun or reduce load BEFORE treating this as a
    /// failure</c>. That token is greppable and travels into the TRX failure text, so a genuine
    /// break of a deterministic signal told the next engineer to RE-RUN it, which is the one
    /// instruction this suite must never give. A signal wait therefore never reads as starved:
    /// there is no loop to starve, and this helper does not guess about what it did not
    /// measure.</para>
    ///
    /// <para>Internal rather than private so BOTH verdicts can be pinned against each other
    /// (<c>TestWaitVerdictTests</c>). Before that file neither was pinned anywhere, which is how the
    /// misclassification survived across ten call sites: the only evidence was a failure message
    /// nobody reads until something is already broken.</para>
    /// </summary>
    internal static string Verdict(
        string what, TimeSpan window, bool deterministicSignal, int polls, long worstSlipMs)
    {
        // A starved ACTOR leaves this loop on schedule, which is why the verdict is a hypothesis
        // and the evidence beside it is what lets a reader overturn it.
        var starved = !deterministicSignal
            && (worstSlipMs > StarvationSlipMs || polls < window.TotalMilliseconds / PollMs / 10);
        if (starved)
        {
            return "TIMING-VERDICT:ENVIRONMENT-STARVED — the wait loop itself was scheduler-starved " +
                $"while waiting for {what}: the machine was too slow to observe the full window; " +
                "rerun or reduce load BEFORE treating this as a failure";
        }

        var subject = deterministicSignal
            ? "the deterministic signal never completed"
            : "it never became true";
        return $"TIMING-VERDICT:CONDITION-NEVER-TRUE — waited the full {window.TotalSeconds:0.#}s " +
            $"for {what} and {subject}: treat as a REAL product/test failure";
    }

    private static string FailureMessage(string what, Func<string>? state, Stats stats)
    {
        var verdict = Verdict(what, stats.Window, stats.DeterministicSignal, stats.Polls, stats.WorstSlipMs);
        string snapshot;
        try
        {
            snapshot = state?.Invoke() ?? "none supplied";
        }
        catch (Exception ex)
        {
            snapshot = $"<state snapshot threw {ex.GetType().Name}: {ex.Message}>";
        }

        var evidence = stats.DeterministicSignal
            ? "deterministic signal wait, so there is NO poll loop here and this helper neither " +
              "observes nor claims anything about scheduler starvation on this path; " +
              $"elapsed={stats.ElapsedMs}ms against a {stats.Window.TotalMilliseconds:0}ms window"
            : $"polls={stats.Polls}, elapsed={stats.ElapsedMs}ms, worst-scheduler-slip={stats.WorstSlipMs}ms";

        return $"{verdict}. EVIDENCE: {evidence}, " +
               $"threadpool-pending={ThreadPool.PendingWorkItemCount}, " +
               $"actor-state: {snapshot}";
    }

    private sealed record Stats(
        bool Met, int Polls, long WorstSlipMs, long ElapsedMs, TimeSpan Window, bool DeterministicSignal = false)
    {
        /// <summary>The signal overload carries its OWN measured elapsed and says plainly
        /// that it had no poll loop, instead of filling the poll fields with sentinels the verdict
        /// selector then read as starvation.</summary>
        public static Stats ForSignal(TimeSpan window, long elapsedMs) =>
            new(false, -1, -1, elapsedMs, window, DeterministicSignal: true);
    }
}
