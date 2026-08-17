using System.Threading;

namespace CcpClient.Desktop.Audio;

/// <summary>Playback state of an <see cref="IAudioPlayer"/> (backend-agnostic).</summary>
public enum AudioPlayerState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// One audio player on a channel. Real = SoundFlow <c>SoundPlayer</c> on the playback
/// device's MasterMixer; tests = recording fake. Backend behavior facts (SP-017):
/// SoundFlow fires ZERO end events on explicit Stop (A2) — interruption stays
/// distinguishable from completion — but the generation-token filter in
/// <see cref="SoundArbitration"/> is still REQUIRED because other backends fire on stop
/// (NAudio <c>PlaybackStopped</c>, finding F2). Never assume per-backend.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Backend-emitted natural-completion event (never fires on explicit Stop on SoundFlow).</summary>
    event EventHandler? PlaybackEnded;

    AudioPlayerState State { get; }

    /// <summary>Decoder-reported position in seconds (evidence: freeze/position checks).</summary>
    double PositionSec { get; }

    /// <summary>Per-player gain 0..1 (volume = mechanism-only, SP-017 A7).</summary>
    float Volume { get; set; }

    void Play();

    void Pause();

    void Stop();
}

/// <summary>
/// The audio backend seam: device init with the F1 discipline + player construction.
/// Real = <see cref="SoundFlowAudioBackend"/> (SoundFlow 1.4.1, SP-017 selection);
/// tests = recording fake.
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>
    /// Fresh render-endpoint NAMEs (session facts — SP-017 A6 shape: "RDP Sink" class on
    /// WSLg; device names are hardware endpoints, never user data).
    /// </summary>
    IReadOnlyList<string> EnumerateDevices();

    /// <summary>
    /// Initialise the playback device. F1 discipline (SP-017, process-fatal crash class,
    /// observed 2× 2026-07-21): RE-ENUMERATE immediately before init, match the requested
    /// device by NAME, pass only a FRESH enumeration snapshot's DeviceInfo — never a stored
    /// one; callers persist the NAME, never the Id. null/unknown name → default device.
    /// </summary>
    bool TryInit(string? deviceName, out string? error);

    /// <summary>
    /// Create (not yet playing) a player for a local audio file at gain 0..1. Implementations
    /// MUST construct off-sync-context (<see cref="OffSyncContext"/>): SoundFlow 1.4.1's
    /// AssetDataProvider ctor is sync-over-async and deadlocks any thread carrying a
    /// SynchronizationContext (SP-025, dump-proven). SP-072: implementations MUST also bound
    /// the caller's wait and keep the orphan invariant (see <see cref="OrphanSafePlayerFactory{TPlayer}"/>):
    /// an abandoned construction never reaches the mixer, never plays, and is disposed exactly
    /// once, ordered against device teardown. Budget expiry throws
    /// <see cref="PlayerConstructionTimeoutException"/> — the typed no-player outcome callers
    /// already map to <see cref="SoundOutcome.Failed"/>.
    /// </summary>
    IAudioPlayer CreatePlayer(string path, float volume);
}

/// <summary>
/// The platform duck MECHANISM seam (what a duck actually does to audio outside the
/// arbitration core). q1 registers <see cref="UnavailableDuckSink"/> — the cross-app
/// session-volume sink (WASAPI session enumeration on Windows / PipeWire-Pulse policy on
/// Linux) is a separate pending-owner platform decision (audio-backend-spike.md named
/// limit 8; pre-approach consult binding 2026-07-22). The reference-counted machinery in
/// <see cref="SoundArbitration"/> is platform-independent and fully implemented against
/// this seam.
/// </summary>
public interface IAudioDuckSink
{
    /// <summary>Apply a duck of <paramref name="strength"/> (fraction REMOVED, 0..1). False = mechanism unavailable/failed (typed, never silent).</summary>
    bool TryApply(float strength, out string? error);

    /// <summary>Restore pre-duck state. Throwing keeps the duck recoverable (WPF AudioService.cs:1003-1016 — never a volume ratchet).</summary>
    void Restore();
}

/// <summary>
/// The q1 duck sink: no cross-app session-duck mechanism is admitted yet (named limit —
/// WASAPI/PipeWire policy = future row, owner-decided). Reports typed Unavailable so the
/// refcount machinery treats ducks as not-held (WPF symmetric-failure parity,
/// AudioService.cs:869-873) and callers get an honest outcome.
/// </summary>
public sealed class UnavailableDuckSink : IAudioDuckSink
{
    /// <inheritdoc/>
    public bool TryApply(float strength, out string? error)
    {
        error = "cross-app session ducking not admitted (platform policy pending-owner — audio-backend-spike.md named limit 8)";
        return false;
    }

    /// <inheritdoc/>
    public void Restore() { /* never applied */ }
}

/// <summary>
/// Injectable clock/timer seam (pre-approach consult binding 2026-07-22: watchdog +
/// pacing delays must be unit-testable without real waits). Real = <see cref="SystemSoundClock"/>;
/// tests = manual-advance fake.
/// </summary>
public interface ISoundClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>One-shot scheduled callback. due &lt;= 0 fires as soon as possible. Dispose cancels; a disposed timer's callback never runs (best-effort on the real clock — callbacks re-check state under the arbitration gate).</summary>
    IDisposable Schedule(TimeSpan due, Action fire);
}

/// <summary>The real clock on <see cref="System.Threading.Timer"/>.</summary>
public sealed class SystemSoundClock : ISoundClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public IDisposable Schedule(TimeSpan due, Action fire)
    {
        var ms = Math.Max(0, (long)due.TotalMilliseconds);
        return new Timer(_ => fire(), null, ms, Timeout.Infinite);
    }
}

/// <summary>
/// SP-072 typed no-player outcome. TWO refusals now carry it, distinguished by MESSAGE, not by
/// type (every one of the five call sites catches Exception broadly and embeds the message, so a
/// second type would buy no caller behaviour and cost every caller a second concept):
///   (a) the caller's wait on a player construction EXPIRED (or the lifecycle lock stayed
///       unavailable within the budget because a wedged native teardown holds it), so the
///       construction was ABANDONED; and
///   (b) SP-083: the factory REFUSED at its outstanding-abandoned cap — no construction was
///       started at all, so no pool thread was taken.
/// The abandoned player never reaches the mixer, never plays, and is disposed exactly once by
/// <see cref="OrphanSafePlayerFactory{TPlayer}"/>. Callers map this to their existing refusal
/// vocabulary (SoundArbitration: SoundOutcome.Failed via the existing catches; DTRH effects: the
/// logged silent no-op). Never a null or placeholder player.
/// </summary>
public sealed class PlayerConstructionTimeoutException : Exception
{
    public PlayerConstructionTimeoutException(string message) : base(message) { }
}

/// <summary>
/// SP-072 orphan-safe bounded player construction. The real SoundFlow backends
/// (<see cref="SoundFlowAudioBackend"/>, SoundFlowDtrhAudio) have zero headless coverage (real
/// engine + device), so the MECHANISM lives here with injected delegates — headless facts bind
/// the abandonment decision, the exactly-once latch, the teardown ordering, and the typed
/// expiry; the single residual line verified by reading only is each backend's
/// <paramref name="attach"/> body (<c>_device.MasterMixer.AddComponent(...)</c>).
///
/// The orphan invariant:
///   1. An abandoned construction NEVER reaches the mixer — only the waiter attaches, under
///      the lifecycle lock, only when construction completed inside the budget and the backend
///      is not torn down. The completing thread can never attach — only dispose.
///   2. It NEVER plays — no reference escapes on the abandoned path.
///   3. It is disposed EXACTLY ONCE — the waiter-torndown path, the spawned disposer (P3), and
///      the completion continuation (P4) are mutually exclusive or arbitrated by the latch.
///   4. Its disposal is ORDERED against device teardown — orphan disposal and the
///      <see cref="Teardown"/> delegate both run under <see cref="_lifecycle"/>, strictly
///      serialized. Only pool threads ever wait on that lock unbounded (that wait IS the
///      ordering); every CALLER-side acquisition is bounded (Monitor.TryEnter) so a wedged
///      native teardown can never block a caller — the SP-071 class, pre-approach consult.
///   5. The ordinary path is observably unchanged — same object, same volume, attach before
///      return, same unwrapped exception surface, zero new log lines.
///   6. SP-083: the OUTSTANDING abandoned constructions are BOUNDED. An abandoned construction
///      keeps an OS pool thread that nothing in .NET can interrupt (no token cancels a blocked
///      native ctor), so the count of abandoned-and-still-running constructions is itself the
///      resource. At the cap this factory refuses a further construction with the same typed
///      no-player outcome BEFORE any thread is taken — never by making the caller wait again
///      (that is SP-072 reverted) and never by skipping the orphan's disposal (that trades a
///      bounded residue for an unbounded leak). The count is released the instant the native
///      call RETURNS, whatever the outcome — never at disposal, or one faulted construction
///      would refuse for the rest of the session. Only ABANDONED constructions are counted: an
///      ordinary in-flight construction must never consume the bound, or a burst of healthy
///      cues would silence a working device. The bound is PER FACTORY INSTANCE (these are
///      per-window-open, not singletons), which is a named limit, not a session bound.
///
/// SP-025 (dump-proven): construction ALWAYS runs on a Task.Run pool thread, which never
/// carries a SynchronizationContext — the AssetDataProvider sync-over-async ctor would
/// deadlock one. <see cref="_lifecycle"/> is a LEAF lock: nothing under it takes any other
/// managed lock, so no lock cycle is possible (SoundArbitration._gate → TryEnter is bounded;
/// SP-071's teardown thread takes _initLock, releases it, then reaches _lifecycle; SP-073 adds
/// a second teardown thread spawned by the post-release drain, which reaches _lifecycle
/// without ever taking _initLock — never nested on either path).
/// </summary>
public sealed class OrphanSafePlayerFactory<TPlayer> where TPlayer : class
{
    /// <summary>Default caller-wait budget: 2 s, the SP-071 TeardownBudget precedent (the local bounded-wait shape, DtrhHostWindow.axaml.cs:257-260). Healthy construction is milliseconds; this only bounds the wedged-endpoint case.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// SP-083 default cap on outstanding ABANDONED constructions per factory instance. Chosen
    /// literal, not a proof: the healthy path never counts at all, so any non-zero count already
    /// means constructions are blowing the 2 s budget — reaching 4 on one factory costs at least
    /// 8 caller-seconds of blown budgets, which no working endpoint produces. Deliberately &gt; 1,
    /// so a single transient slow file load cannot refuse the next cue.
    /// </summary>
    public const int DefaultMaxOutstandingAbandoned = 4;

    private const int Pending = 0;
    private const int Attached = 1;
    private const int Disposed = 2;

    // SP-083 accounting states — a SECOND, independent latch on the slot. Deliberately not
    // overloaded onto State: the pool THREAD is released when _construct returns, the PLAYER is
    // disposed later (possibly much later, behind _lifecycle) — two lifetimes that end at
    // different moments must not share one latch.
    private const int Uncounted = 0;
    private const int Counted = 1;
    private const int Settled = 2;

    private sealed class ConstructionSlot
    {
        public volatile bool Abandoned;
        public int State; // Pending / Attached / Disposed — orphan-disposal transitions by CAS (the latch)
        public int Accounting; // Uncounted / Counted / Settled — SP-083, exactly-once on BOTH sides
    }

    private readonly object _lifecycle = new();
    private readonly Func<string, float, TPlayer> _construct;
    private readonly Action<TPlayer> _attach;
    private readonly Action<TPlayer> _dispose;
    private readonly Action<string> _log;
    private readonly string _tag;
    private readonly TimeSpan _budget;
    private readonly int _maxOutstandingAbandoned;
    private volatile bool _tornDown;
    private int _outstandingAbandoned;

    public OrphanSafePlayerFactory(
        Func<string, float, TPlayer> construct,
        Action<TPlayer> attach,
        Action<TPlayer> dispose,
        Action<string> log,
        string tag,
        TimeSpan? budget = null,
        int? maxOutstandingAbandoned = null)
    {
        _construct = construct;
        _attach = attach;
        _dispose = dispose;
        _log = log;
        _tag = tag;
        _budget = budget ?? DefaultBudget;
        // Clamped: 0 would mean "refuse all audio forever" the first time anything is abandoned.
        _maxOutstandingAbandoned = Math.Max(1, maxOutstandingAbandoned ?? DefaultMaxOutstandingAbandoned);
    }

    /// <summary>
    /// SP-083: abandoned constructions whose native call has NOT returned — ONE parked pool
    /// thread each. Rises only at abandonment and falls the instant the native call returns
    /// (success or fault). Zero on the ordinary path, always.
    /// </summary>
    public int OutstandingAbandonedConstructions => Volatile.Read(ref _outstandingAbandoned);

    /// <summary>
    /// Construct, attach, and return a player — or abandon the construction with a typed
    /// <see cref="PlayerConstructionTimeoutException"/> when the budget expires (or the
    /// lifecycle lock stays unavailable, which means a wedged native teardown holds it).
    /// </summary>
    public TPlayer Create(string path, float volume)
    {
        if (_tornDown)
        {
            throw new InvalidOperationException($"{_tag}: backend torn down — player construction refused");
        }

        // SP-083 C1: refuse at the cap BEFORE any Task.Run — the refusal must not take the very
        // resource it exists to bound. Read-compare-throw only: no wait, no lock, no token. The
        // cap is SOFT by construction (K callers concurrently past the read can all construct),
        // so the real bound is cap + concurrently-in-flight callers per factory — still a bound,
        // and still the removal of the one-per-cue growth this exists for.
        var outstanding = Volatile.Read(ref _outstandingAbandoned);
        if (outstanding >= _maxOutstandingAbandoned)
        {
            _log($"{_tag}: player construction refused — {outstanding} abandoned construction(s) still parked (cap {_maxOutstandingAbandoned}); no pool thread started");
            throw new PlayerConstructionTimeoutException(
                $"{_tag}: player construction refused at the outstanding-parked cap ({_maxOutstandingAbandoned}) — typed no-player outcome, no construction started");
        }

        var slot = new ConstructionSlot();
        // SP-025: pool thread — never a SynchronizationContext. SP-083: the finally is where the
        // parked thread is RELEASED, so the count falls at the native return, whatever the outcome.
        var task = Task.Run(() =>
        {
            try { return _construct(path, volume); }
            finally { SettleAccounting(slot); }
        });
        // P4: the late-completion disposer — fires inline on the completing thread, exactly
        // once per task, gated by the abandonment check + the latch.
        _ = task.ContinueWith(
            t => DisposeOrphan(t.Result, slot),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        // A faulted construction produced no player; observe the exception so an abandoned
        // fault never surfaces as an unobserved-task fault.
        _ = task.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        bool completed;
        try
        {
            completed = task.Wait(_budget);
        }
        catch (AggregateException)
        {
            // Preserve the pre-SP-072 exception surface (OffSyncContext.Run's
            // GetAwaiter().GetResult() — callers see the INNER exception, unwrapped).
            task.GetAwaiter().GetResult(); // always throws
            throw new System.Diagnostics.UnreachableException();
        }

        if (completed)
        {
            var player = task.GetAwaiter().GetResult();
            // BOUNDED: a wedged native device teardown (SP-071's backgrounded
            // _backend.Dispose → Teardown) can hold _lifecycle — a caller must never wait on
            // it unbounded (pre-approach consult finding).
            if (Monitor.TryEnter(_lifecycle, _budget))
            {
                try
                {
                    if (_tornDown)
                    {
                        slot.State = Disposed; // under the lock; no competitor (never abandoned)
                        SafeDispose(player);
                        throw new InvalidOperationException($"{_tag}: backend torn down during player construction");
                    }

                    slot.State = Attached; // only the waiter ever attaches
                    _attach(player);
                    return player;
                }
                finally
                {
                    Monitor.Exit(_lifecycle);
                }
            }
            // Lock unavailable within the budget → abandon (below); the completed player goes
            // to a pool disposer — the caller never blocks unbounded.
        }

        // ABANDONMENT — the decision is made HERE, before the player can ever reach the mixer.
        // The volatile write precedes the completed-check: a completion before this point was
        // seen by P4 (skipped — not yet abandoned) and is caught by the check (P3 spawned); a
        // completion after it is seen by P4 (Abandoned visible via the volatile + task
        // completion barriers). If both fire, the latch decides.
        slot.Abandoned = true;
        // SP-083 C2: this construction is now abandoned-and-still-running — one parked pool
        // thread. Counted HERE, at the abandonment, and never for an ordinary construction.
        // Placed between the volatile write and the log so the LOAD-BEARING order below is
        // unmoved relative to the completed-check.
        CountAbandoned(slot);
        // LOAD-BEARING ORDER (pre-completion consult, SP-072): the log line must precede the
        // completed-check below. This is the only window in which BOTH orphan disposers can
        // be armed for one slot (P4 fires at completion, P3 at the check) —
        // Construction_CompletionRacesAbandonment_DisposedExactlyOnce rendezvous on this
        // line to force that race. Moving the log after the check makes that pin pass
        // without ever exercising the latch (the SP-067/SP-070 vacuous class).
        _log($"{_tag}: player construction abandoned after {(int)_budget.TotalMilliseconds}ms — orphan disposed, never attached");
        if (task.IsCompletedSuccessfully)
        {
            SpawnDisposer(task, slot);
        }

        throw new PlayerConstructionTimeoutException($"{_tag}: player construction exceeded {(int)_budget.TotalMilliseconds}ms — abandoned (typed no-player outcome)");
    }

    /// <summary>
    /// Device teardown, serialized against orphan disposal under the lifecycle lock. Runs the
    /// native teardown delegate while HOLDING the lock — only pool-thread orphan disposers
    /// ever wait on it (that wait IS the ordering); caller paths use bounded TryEnter.
    /// Idempotent.
    /// </summary>
    public void Teardown(Action deviceTeardown)
    {
        lock (_lifecycle)
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
            deviceTeardown();
        }
    }

    private void SpawnDisposer(Task<TPlayer> task, ConstructionSlot slot)
    {
        // P3: pool thread — never the caller. The task is already complete when this is
        // spawned; the Wait is the belt for the straddle race.
        Task.Run(() =>
        {
            try { task.Wait(); } catch { /* faulted: nothing was constructed */ }
            if (task.IsCompletedSuccessfully)
            {
                DisposeOrphan(task.Result, slot);
            }
        });
    }

    /// <summary>
    /// SP-083: claim the slot for the outstanding count, exactly once. Runs on the CALLER thread
    /// at the abandonment decision. The CAS loses to a construction that already returned and
    /// settled (Settled), which is correct: nothing is parked, so nothing is counted.
    /// </summary>
    private void CountAbandoned(ConstructionSlot slot)
    {
        if (Interlocked.CompareExchange(ref slot.Accounting, Counted, Uncounted) == Uncounted)
        {
            Interlocked.Increment(ref _outstandingAbandoned);
        }
    }

    /// <summary>
    /// SP-083: the native call returned, so the pool thread is released — release its count too,
    /// exactly once, and only if this slot was ever counted. Runs on the CONSTRUCTION thread, in
    /// the Task.Run finally, so it fires on success AND on fault. Takes no lock: it must never be
    /// able to block behind a wedged teardown holding <see cref="_lifecycle"/>.
    /// </summary>
    private void SettleAccounting(ConstructionSlot slot)
    {
        if (Interlocked.Exchange(ref slot.Accounting, Settled) == Counted)
        {
            Interlocked.Decrement(ref _outstandingAbandoned);
        }
    }

    private void DisposeOrphan(TPlayer player, ConstructionSlot slot)
    {
        if (!slot.Abandoned)
        {
            return; // the abandonment CHECK — ordinary constructions are never touched
        }

        if (Interlocked.CompareExchange(ref slot.State, Disposed, Pending) != Pending)
        {
            return; // the single-dispose LATCH — exactly one orphan disposer wins
        }

        // The ORDERING guard: serialized against device teardown. Pool threads only — this
        // wait IS the ordering, never a caller's block.
        lock (_lifecycle)
        {
            SafeDispose(player);
        }
    }

    private void SafeDispose(TPlayer player)
    {
        try { _dispose(player); }
        catch { /* best-effort — the backends' pre-SP-072 disposal discipline */ }
    }
}

/// <summary>
/// Off-sync-context construction marshal (SP-025, dump-proven 2026-07-22): SoundFlow
/// 1.4.1's AssetDataProvider ctor is SYNC-OVER-ASYNC (GetResult on an async metadata
/// read); on any thread carrying a SynchronizationContext (the Avalonia UI thread) the
/// continuation can never run and the dispatcher wedges silently. The SP-017 console
/// spike never saw it (no sync context). Rule (port-lessons 2026-07-22, binding): any
/// SoundFlow player/provider construction runs off-sync-context — SP-072: PLAYER
/// construction now runs through <see cref="OrphanSafePlayerFactory{TPlayer}"/> (always on a
/// pool thread, plus the orphan invariant); this marshal remains for any other
/// context-carrying SoundFlow call and keeps its own regression pins
/// (SoundArbitrationTests OffSyncContext_*).
/// </summary>
public static class OffSyncContext
{
    /// <summary>Run <paramref name="work"/> inline when no SynchronizationContext is present, else on a thread-pool thread (no context) and block for the result.</summary>
    public static T Run<T>(Func<T> work)
    {
        return SynchronizationContext.Current is null
            ? work()
            : Task.Run(work).GetAwaiter().GetResult();
    }
}
