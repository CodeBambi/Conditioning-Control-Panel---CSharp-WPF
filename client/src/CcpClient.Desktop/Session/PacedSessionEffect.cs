using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The body every self-paced rack module has, written once.
///
/// <para><b>Why this class exists, and what it is really for (SP-101).</b> SP-098 built one effect
/// and SP-100 made it draw. Thirteen more modules follow the same shape, and the failure this
/// packet was created to catch is fourteen copies of <c>FlashImagesEffect</c> with the nouns
/// changed. Everything below is the part that was identical between Flash Images and Subliminals
/// once both were written: the idempotent arm, the owned generation and its parked completion, the
/// single one-shot on the injected clock re-armed at the tail of each firing, the three
/// stale-generation checks, the counter, the last firing, the derived dot, the synchronous stop,
/// and the one dispatch boundary the UI projection goes through. A module contributes its identity,
/// its dial, its interval and its payload — nothing else.</para>
///
/// <para><b>Where the two ported modules genuinely differ</b>, and therefore what the hooks had to
/// be able to express: <see cref="Compose"/> may return <c>null</c>. Flash Images counts a flash
/// over an empty pool and shows nothing (<c>Services/Flash/FlashService.cs:2589-2593</c>);
/// Subliminals over an empty phrase pool counts NOTHING and fires nothing, returning before its own
/// counter (<c>Services/Subliminal/SubliminalService.cs:207-212</c> vs <c>:611-612</c>) — while
/// still re-scheduling (<c>:189-201</c>). A base that assumed every due firing produced an event
/// would have been wrong on the second module, which is the whole reason a second module was built
/// before thirteen more.</para>
///
/// <para><b>Threading.</b> The schedule is a one-shot on an injected <see cref="ISessionClock"/>,
/// re-armed at the tail of every firing — WPF's shape (<c>FlashService.cs:566-573</c>,
/// <c>SubliminalService.cs:189-201</c>), and the reason no test here needs a wall-clock wait. The
/// firing runs on the clock's thread; the UI projection goes through the one dispatch boundary with
/// the liveness check inside the delegate (async-lifecycle-fault-contract §5.3/§5.5). The pending
/// slot holds a <see cref="ScheduledFire"/> identity and is manipulated with
/// <see cref="Interlocked"/>, never under <see cref="Gate"/>, so a cancellation callback can run
/// while another thread holds the gate without a lock cycle.</para>
/// </summary>
/// <typeparam name="TFiring">The module's own record of one firing. It carries whatever the UI
/// projection needs, which is why it is the module's type and not a shared one: Flash Images' firing
/// carries drawn file paths that must never reach a log or an event, so the record it hands to
/// subscribers and the record it hands to the surface are different halves of the same object.</typeparam>
public abstract class PacedSessionEffect<TFiring> : ISessionEffect
    where TFiring : class
{
    private readonly AsyncOperationOwner _owner;
    private readonly string _operationName;
    private readonly object _gate = new();

    private ScheduledFire? _pending;
    private int _generation = -1;
    private bool _armed;
    private int _fireCount;
    private TFiring? _last;

    /// <param name="owner">The module's operation owner: one generation per armed schedule.</param>
    /// <param name="signal">Where <see cref="Changed"/> and the UI projection are allowed to arrive.</param>
    /// <param name="clock">The injected clock the schedule paces on. Never <c>DateTime.Now</c>.</param>
    /// <param name="operationName">The registered operation's name, e.g. <c>flash-schedule</c>.</param>
    protected PacedSessionEffect(
        AsyncOperationOwner owner, EffectSignal signal, ISessionClock clock, string operationName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrEmpty(operationName);
        _owner = owner;
        _operationName = operationName;
        Signal = signal;
        Clock = clock;
    }

    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract string Title { get; }

    /// <inheritdoc/>
    public abstract bool Enabled { get; }

    /// <inheritdoc/>
    public abstract void SetEnabled(bool enabled);

    /// <inheritdoc/>
    public Task<OperationOutcome>? Completion { get; private set; }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The clock this module paces on. Subclasses read <c>UtcNow</c> from it and nowhere else.</summary>
    protected ISessionClock Clock { get; }

    /// <summary>The signal boundary, for a subclass that projects something of its own on stop.</summary>
    protected EffectSignal Signal { get; }

    /// <summary>The state lock. A subclass takes it only to read its own dials consistently with a
    /// firing; it must never be held across a draw or across a clock call.</summary>
    protected object Gate => _gate;

    /// <summary>Firings that have really come due since this module was first armed. What "really"
    /// means is the module's: a firing that produced nothing is not counted if upstream does not
    /// count it (<see cref="Compose"/>).</summary>
    public int FireCount
    {
        get { lock (_gate) { return _fireCount; } }
    }

    /// <summary>The most recent firing, or null if none has happened yet.</summary>
    public TFiring? LastFiring
    {
        get { lock (_gate) { return _last; } }
    }

    /// <summary>
    /// True while a next firing is actually on the clock. This — not a stored bool, and not the
    /// persisted dial — is what the row's dot is entitled to call "running": it is false the
    /// instant the schedule is torn down, whatever any flag still says.
    /// </summary>
    public bool ScheduleArmed => Volatile.Read(ref _pending) is not null;

    /// <inheritdoc/>
    public EffectDotState Dot
    {
        get
        {
            // Off beats everything: a module whose own dial is off will do nothing whether or
            // not a session is running, and saying "armed" there would be a lie about the
            // NEXT minute, which is what a dot is for.
            if (!Enabled)
            {
                return EffectDotState.Off;
            }

            lock (_gate)
            {
                // Live derives from the OPERATION authority (the StatusTickerParticipant
                // IsOperationLive precedent) and from the schedule really being on the clock.
                // A generation that has been cancelled reads dead here even if _armed has not
                // been cleared yet, which is the case teardown produces.
                return _armed && _owner.IsLive(_generation) && ScheduleArmed
                    ? EffectDotState.Live
                    : EffectDotState.Armed;
            }
        }
    }

    /// <summary>
    /// Arm the schedule. WPF's services are idempotent about their own running flag and schedule
    /// the first tick SYNCHRONOUSLY (<c>FlashService.cs:345-352</c>,
    /// <c>SubliminalService.cs:100-110</c>) — kept, and load-bearing: the caller's next observation
    /// of the clock must already see the pending firing, or a stop/advance ordering becomes a race.
    ///
    /// <para><b>One deliberate divergence, inherited from SP-098.</b> WPF's <c>Start()</c> returns
    /// at <c>if (_isRunning) return;</c>, so a module that was OFF when the engine started can never
    /// be armed by turning it on mid-session. Here the GENERATION is idempotent — arming twice
    /// starts no second generation, which is the re-entrant double-toggle guard — but the SCHEDULE
    /// is always re-evaluated, so switching a module on during a session does what the app's own
    /// onboarding text promises it does.</para>
    /// </summary>
    /// <returns>
    /// What this module can honestly say about the session it was just handed:
    /// <see cref="CapabilityState.Available"/> when a firing is on the clock,
    /// <see cref="CapabilityState.Unavailable"/> when the dial is off or the generation was already
    /// cancelled, and whatever <see cref="Ready"/> narrows that to for a module that runs but cannot
    /// show anything.
    /// </returns>
    public CapabilityState Arm()
    {
        bool firstArm;
        lock (_gate)
        {
            firstArm = !_armed;
            _armed = true;
        }

        if (firstArm)
        {
            // Begin() cancels the previous generation, which runs that generation's
            // cancellation callback; it is called OUTSIDE _gate so the callback can never wait
            // on a lock this thread is holding.
            var generation = _owner.Begin();
            lock (_gate)
            {
                _generation = generation;
            }

            Completion = _owner.RunAsync(_operationName, ParkUntilCancelledAsync);
        }

        var scheduled = ScheduleNext();
        RaiseChanged();
        return Ready(scheduled);
    }

    /// <summary>
    /// Disarm. WPF's stops clear the running flag, cancel the token and stop the scheduler timer
    /// (<c>FlashService.cs:367-380</c>, <c>SubliminalService.cs:113-118</c>).
    ///
    /// <para>The pending one-shot is disposed HERE, synchronously, rather than being left to the
    /// cancellation callback: after this returns, advancing the clock by any amount must fire
    /// nothing, and "the callback will get there" is not that guarantee.</para>
    /// </summary>
    public void Disarm()
    {
        bool wasArmed;
        lock (_gate)
        {
            wasArmed = _armed;
            _armed = false;
        }

        CancelPendingSchedule();
        OnDisarmed();
        if (!wasArmed)
        {
            return;
        }

        _owner.Cancel();
        RaiseChanged();
    }

    /// <summary>
    /// Re-pace the pending firing against the current dial — WPF <c>RefreshSchedule</c>
    /// (<c>FlashService.cs:527-531</c>), which its frequency slider calls so a change takes effect
    /// now instead of after the old interval expires. A no-op when nothing is armed, which is WPF's
    /// <c>if (!_isRunning) return;</c> (<c>:529</c>).
    /// </summary>
    public void RefreshSchedule()
    {
        ScheduleNext();
        RaiseChanged();
    }

    /// <summary>The interval until the next firing, read from this module's own dial. Called off the
    /// gate, so an implementation may read a persisted store.</summary>
    protected abstract TimeSpan NextInterval();

    /// <summary>
    /// Do the module's work for one firing and return what it produced, or <c>null</c> when nothing
    /// came of it and upstream would not have counted it. Runs OUTSIDE <see cref="Gate"/>: a pool
    /// may touch a filesystem, and a session must never be able to wedge its own state behind a slow
    /// directory. The ordinal and the timestamp are not this method's business — see
    /// <see cref="Stamp"/>.
    /// </summary>
    protected abstract TFiring? Compose();

    /// <summary>
    /// Stamp the ordinal and the moment onto a composed firing. Called UNDER <see cref="Gate"/>,
    /// after the post-draw liveness re-check, so the ordinal a subscriber sees is the one the
    /// counter really reached. On a record this is one <c>with</c> expression.
    /// </summary>
    protected abstract TFiring Stamp(TFiring firing, int ordinal, DateTimeOffset at);

    /// <summary>
    /// Put the firing where the user can experience it. Called on the signal thread, inside the
    /// projection, with the generation already re-checked — so an implementation may touch a native
    /// window and must not re-check liveness itself.
    /// </summary>
    protected abstract void Deliver(TFiring firing);

    /// <summary>Take anything this module put on screen back off. Called from
    /// <see cref="Disarm"/> and from nowhere else, deliberately: teardown reaches disarm too, so
    /// hiding from the cancellation callback as well would take every surface down twice per stop.</summary>
    protected virtual void OnDisarmed()
    {
    }

    /// <summary>
    /// A module's chance to narrow what its arm result claims. The default returns the schedule's
    /// own outcome unchanged; a module that is paced but cannot show anything narrows
    /// <see cref="CapabilityState.Available"/> to <see cref="CapabilityState.Degraded"/>, and a
    /// module whose device is missing will refuse outright here.
    /// </summary>
    protected virtual CapabilityState Ready(CapabilityState scheduled) => scheduled;

    /// <summary>Raise <see cref="Changed"/> through the signal boundary. Subclasses call this after
    /// writing a dial; nothing else may raise the event.</summary>
    protected void RaiseChanged() => Signal.Raise(() => Changed?.Invoke());

    /// <summary>
    /// The owned operation behind an armed schedule. It does no work: it exists so the schedule is a
    /// REGISTERED operation with a generation and a typed terminal outcome
    /// (async-lifecycle-fault-contract §1), so the host's one teardown entry point cancels and drains
    /// it like everything else, and so "the module is still running" is a question the registry can
    /// answer rather than this class.
    /// </summary>
    private async Task<OperationOutcome> ParkUntilCancelledAsync(CancellationToken token)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Lock-free callback on purpose: it may run on a teardown thread while another thread
        // holds _gate, so it must not need _gate.
        using var registration = token.Register(() =>
        {
            CancelPendingSchedule();
            stopped.TrySetResult();
        });

        await stopped.Task.ConfigureAwait(false);
        CancelPendingSchedule();
        return OperationOutcome.Cancelled.Instance;
    }

    private void CancelPendingSchedule() => Interlocked.Exchange(ref _pending, null)?.Dispose();

    /// <summary>
    /// WPF's <c>ScheduleNext</c> (<c>FlashService.cs:538-563</c>,
    /// <c>SubliminalService.cs:172-187</c>): nothing is scheduled while the service is stopped or
    /// while the module's own dial is off — in WPF the service still reports itself running in that
    /// state, and nothing ever fires. A previous pending one-shot is dropped first, exactly as WPF
    /// stops its timer before restarting it.
    /// </summary>
    private CapabilityState ScheduleNext()
    {
        int generation;
        lock (_gate)
        {
            if (!_armed || !Enabled)
            {
                CancelPendingSchedule();
                return new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.EffectDialOff,
                    $"the '{Id}' module's own dial is off, so the session armed it and nothing was scheduled"));
            }

            generation = _generation;
        }

        if (!_owner.IsLive(generation))
        {
            CancelPendingSchedule();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectGenerationCancelled,
                $"the '{Id}' module's generation was already cancelled, so nothing was scheduled"));
        }

        var due = NextInterval();

        // Published BEFORE the clock is asked, so a schedule that fires immediately finds its own
        // identity already in the slot; Attach closes the other half of that window.
        var token = new ScheduledFire();
        Interlocked.Exchange(ref _pending, token)?.Dispose();
        token.Attach(Clock.Schedule(due, () => Fire(generation, token)));

        // The generation may have been cancelled between the liveness check and the schedule.
        // Re-check and tear the handle straight back down: a stop must never leave a live
        // one-shot behind, and this is the only window in which one could exist.
        if (!_owner.IsLive(generation))
        {
            CancelPendingSchedule();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectGenerationCancelled,
                $"the '{Id}' module's generation was cancelled while the firing was being scheduled"));
        }

        return new CapabilityState.Available(
            $"the '{Id}' module is armed and the next firing is on the clock in {due.TotalSeconds:0.###}s");
    }

    /// <summary>
    /// One firing comes due. WPF's tick stops its own timer, fires only if the service is still
    /// running, and re-schedules at the tail (<c>FlashService.cs:566-573</c>,
    /// <c>SubliminalService.cs:189-201</c>).
    /// </summary>
    private void Fire(int generation, ScheduledFire token)
    {
        // The one-shot has fired and is spent. CompareExchange, not Exchange: clear the slot only
        // if it still holds THIS firing — and if it does NOT, this callback belongs to a schedule
        // that has already been superseded or cancelled, so it does nothing at all.
        //
        // Both halves matter. Blindly clearing the slot (what this was before SP-101) would null
        // out the LIVE timer's identity, leaving the dot reporting Armed with a firing on the clock
        // and, worse, leaving Disarm nothing to dispose. Letting the superseded callback proceed
        // would fire at the OLD pace immediately after the user moved the frequency slider, which
        // is the one thing RefreshSchedule exists to prevent.
        if (Interlocked.CompareExchange(ref _pending, null, token) != token)
        {
            return;
        }

        // A spent one-shot is still an undisposed timer. Dropping the handle here — which is what
        // this did before SP-101 — leaks one OS timer per firing for the life of a session, and at
        // the subliminal module's own default that is five an hour... per minute. Disposing from
        // inside a timer's own callback is documented as safe and is what makes the count of live
        // handles after a stop exactly zero rather than "however many fired".
        token.Dispose();

        // Stale-generation check next (contract §5.5): a callback that survives a cancel does
        // nothing and re-schedules nothing.
        if (!_owner.IsLive(generation))
        {
            return;
        }

        lock (_gate)
        {
            if (!_armed || _generation != generation || !Enabled)
            {
                return;
            }
        }

        // Outside the gate: the work may touch a filesystem.
        var composed = Compose();

        TFiring? fired = null;
        lock (_gate)
        {
            // Re-check after the work. A stop that landed mid-firing wins: nothing is counted and
            // nothing is re-scheduled.
            if (!_armed || _generation != generation)
            {
                return;
            }

            if (composed is not null)
            {
                _fireCount++;
                fired = Stamp(composed, _fireCount, Clock.UtcNow);
                _last = fired;
            }
        }

        ScheduleNext();
        if (fired is not null)
        {
            Project(generation, fired);
        }
    }

    /// <summary>
    /// The UI projection, and the DRAW. Skip-until-bound (contract §5.3): the module can be armed
    /// before the window exists, and a projection is skipped then, never faulted. The liveness
    /// re-check lives INSIDE the posted delegate (§5.5) so a post that lands during teardown is
    /// inert.
    /// </summary>
    private void Project(int generation, TFiring fired)
    {
        if (!Signal.IsBound || !_owner.IsLive(generation))
        {
            return;
        }

        Signal.Post(() =>
        {
            if (!_owner.IsLive(generation))
            {
                return;
            }

            Deliver(fired);
        });
    }
}
