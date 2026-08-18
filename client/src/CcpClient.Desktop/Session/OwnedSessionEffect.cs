using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The part of a rack module that has nothing to do with WHEN it works.
///
/// <para><b>Why this class exists (SP-105).</b> SP-101 wrote one shared body,
/// <see cref="PacedSessionEffect{TFiring}"/>, out of the two modules that existed — and both of them
/// were TIMED. Its own template verdict named the risk out loud: the spine might have been quietly
/// assumed to be a scheduler. SP-105 built a module that is simply <i>on</i> — Pink Filter, which
/// WPF drives with no timer, no tick and no interval at all
/// (<c>MainWindow/MainWindow.Presets.cs:1255</c> is <c>flag = !flag; App.Overlay?.RefreshOverlays();</c>
/// and that is the whole mechanism) — and the answer is in this file's existence:
/// <b><see cref="ISessionEffect"/> fits it unchanged; <see cref="PacedSessionEffect{TFiring}"/> does
/// not.</b> So the shared body split in two, and this is the half that survived the split.</para>
///
/// <para><b>What is here is exactly what both kinds of module do identically:</b> take the session
/// once (idempotent about the GENERATION, never about the work), own a registered operation with a
/// parked terminal outcome, refuse in type when the dial is off or the generation is already dead,
/// give the session back synchronously, raise <see cref="Changed"/> through one boundary, and derive
/// a dot that cannot lie. <b>What is NOT here is every word about timing</b> — no clock, no interval,
/// no firing, no counter, no re-arm. A subclass says what "engage" means for it, and what "the work
/// is really running" means for it, and those two answers are the entire difference between a paced
/// module and a continuous one.</para>
///
/// <para><b>The dot's third input is abstract, and that is the whole finding.</b>
/// <see cref="WorkIsRunning"/> is the clause that decides <see cref="EffectDotState.Live"/>. For a
/// paced module it is a claim about the CLOCK — a firing is genuinely scheduled — and Subliminals
/// over an empty phrase pool is correctly <c>Live</c> even though nothing will appear. For a
/// continuous module there is no clock between "armed" and "on screen", so the same clause can only
/// be a claim about the SCREEN: a Pink Filter whose overlay was refused has literally nothing
/// running, and <c>Live</c> there would be the confident half-truth <see cref="EffectDotState"/>
/// exists to refuse. Three states still describe reality; the AUTHORITY behind the third one is the
/// module's, not the base's.</para>
///
/// <para><b>Threading.</b> <see cref="Gate"/> guards the arm flag and the generation and nothing
/// slow. <see cref="ReleaseWork"/> is called from a cancellation callback on a teardown thread and
/// must therefore be lock-free and safe against a thread holding the gate.</para>
/// </summary>
public abstract class OwnedSessionEffect : ISessionEffect
{
    private readonly AsyncOperationOwner _owner;
    private readonly string _operationName;
    private readonly object _gate = new();

    private int _generation = -1;
    private bool _armed;

    /// <param name="owner">The module's operation owner: one generation per armed module.</param>
    /// <param name="signal">Where <see cref="Changed"/> and any UI projection are allowed to arrive.</param>
    /// <param name="operationName">The registered operation's name, e.g. <c>flash-schedule</c>.</param>
    protected OwnedSessionEffect(AsyncOperationOwner owner, EffectSignal signal, string operationName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentException.ThrowIfNullOrEmpty(operationName);
        _owner = owner;
        _operationName = operationName;
        Signal = signal;
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

    /// <summary>The signal boundary: the one place a module's state change or projection is
    /// allowed to arrive.</summary>
    protected EffectSignal Signal { get; }

    /// <summary>The state lock. A subclass takes it only to read its own dials consistently with
    /// the arm flag; it must never be held across a draw or across a clock call.</summary>
    protected object Gate => _gate;

    /// <summary>True between an arm and the disarm that answers it. Read under <see cref="Gate"/>.</summary>
    protected bool IsArmed
    {
        get { lock (_gate) { return _armed; } }
    }

    /// <summary>
    /// True while this module is armed AND <paramref name="generation"/> is still the generation it
    /// was armed under. The two are read TOGETHER, under <see cref="Gate"/>, because a callback that
    /// checked them separately could see a torn pair across a stop-then-start and do the previous
    /// session's work in the new one. <see cref="Gate"/> is re-entrant, so a caller that already
    /// holds it to write its own state may call this inside that block.
    /// </summary>
    protected bool IsArmedFor(int generation)
    {
        lock (_gate)
        {
            return _armed && _generation == generation;
        }
    }

    /// <summary>
    /// True while this module's own work is genuinely running — the third and last input to
    /// <see cref="EffectDotState.Live"/>, and the one thing a continuous module answers differently
    /// from a paced one. It must never be a stored "I was told to start" bool: it is what the row's
    /// dot claims, and a dot that reports a module nobody can see is the failure the whole
    /// three-state design exists to prevent.
    /// </summary>
    protected abstract bool WorkIsRunning { get; }

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
                // IsOperationLive precedent) and from the module's work really being up. A
                // generation that has been cancelled reads dead here even if _armed has not been
                // cleared yet, which is the case teardown produces.
                return _armed && _owner.IsLive(_generation) && WorkIsRunning
                    ? EffectDotState.Live
                    : EffectDotState.Armed;
            }
        }
    }

    /// <summary>
    /// Take ownership of a live generation and start the module's work. WPF's services are
    /// idempotent about their own running flag and do their first work SYNCHRONOUSLY —
    /// <c>FlashService.cs:345-352</c>, <c>SubliminalService.cs:100-110</c>, and the continuous pair's
    /// <c>OverlayService.cs:362-395</c>, whose <c>Start</c> puts the layers up inside a
    /// <c>DispatcherHelper.RunOnUISync</c> before it returns. Kept, and load-bearing for both kinds:
    /// the caller's next observation must already see the work, or a stop/advance ordering becomes a
    /// race for a paced module and a stop/redraw ordering becomes one for a continuous module.
    ///
    /// <para><b>One deliberate divergence, inherited from SP-098.</b> WPF's <c>Start()</c> returns
    /// at <c>if (_isRunning) return;</c>, so a module that was OFF when the engine started can never
    /// be armed by turning it on mid-session. Here the GENERATION is idempotent — arming twice
    /// starts no second generation, which is the re-entrant double-toggle guard — but the WORK is
    /// always re-evaluated, so switching a module on during a session does what the app's own
    /// onboarding text promises it does.</para>
    /// </summary>
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

        var engaged = EngageIfEligible();
        RaiseChanged();
        return Ready(engaged);
    }

    /// <summary>
    /// Stop the work. WPF's stops clear the running flag, cancel the token and drop whatever the
    /// module had live (<c>FlashService.cs:367-380</c>, <c>SubliminalService.cs:113-118</c>,
    /// <c>OverlayService.cs:398-409</c>).
    ///
    /// <para>The work is released HERE, synchronously, rather than being left to the cancellation
    /// callback: after this returns, nothing this module scheduled may fire and nothing it put on
    /// screen may still be claimed, and "the callback will get there" is not that guarantee.</para>
    /// </summary>
    public void Disarm()
    {
        bool wasArmed;
        lock (_gate)
        {
            wasArmed = _armed;
            _armed = false;
        }

        ReleaseWork();
        OnDisarmed();
        if (!wasArmed)
        {
            return;
        }

        _owner.Cancel();
        RaiseChanged();
    }

    /// <summary>
    /// Re-evaluate the module's work against its current dials, and say what came of it.
    ///
    /// <para>This is WPF's own second entry point, and both kinds of module have one: the paced
    /// pair's <c>RefreshSchedule</c> (<c>FlashService.cs:527-531</c>), which the frequency slider
    /// calls so a change takes effect now instead of after the old interval expires, and the
    /// continuous pair's <c>RefreshOverlays</c> (<c>OverlayService.cs:419-452</c>), which the
    /// opacity slider and the quick-toggle call for exactly the same reason
    /// (<c>Features/PinkFilterFeatureControl.xaml.cs:94,107</c>). A no-op when nothing is armed,
    /// which is WPF's <c>if (!_isRunning) return;</c> on both paths (<c>FlashService.cs:529</c>,
    /// <c>OverlayService.cs:421</c>).</para>
    /// </summary>
    public CapabilityState Refresh()
    {
        var engaged = EngageIfEligible();
        RaiseChanged();
        return engaged;
    }

    /// <summary>
    /// Start or re-evaluate this module's work, with the dial and the generation already checked.
    /// Called on the caller's thread — the UI thread on every product path, because both entry
    /// points are gestures — and never under <see cref="Gate"/>: a paced module reads a persisted
    /// dial here and a continuous one touches a native window.
    /// </summary>
    /// <param name="generation">The live generation this work belongs to. An implementation that
    /// hands a callback to anything must carry this into it and re-check it (contract §5.5).</param>
    /// <returns>What the module can honestly claim about the work it just started.</returns>
    protected abstract CapabilityState Engage(int generation);

    /// <summary>
    /// Drop whatever this module has running, now. Called from <see cref="Disarm"/>, from the
    /// generation's cancellation callback on a teardown thread, and from the eligibility gate when a
    /// dial went off — so it must be idempotent and lock-free, and it must never need
    /// <see cref="Gate"/>.
    /// </summary>
    protected abstract void ReleaseWork();

    /// <summary>Take anything this module put on screen back off. Called from
    /// <see cref="Disarm"/> and from nowhere else, deliberately: teardown reaches disarm too, so
    /// hiding from the cancellation callback as well would take every surface down twice per stop.</summary>
    protected virtual void OnDisarmed()
    {
    }

    /// <summary>
    /// A module's chance to narrow what its arm result claims. The default returns the engagement's
    /// own outcome unchanged; a module that is running but cannot show anything narrows
    /// <see cref="CapabilityState.Available"/> to <see cref="CapabilityState.Degraded"/>, and a
    /// module whose device is missing will refuse outright here.
    /// </summary>
    protected virtual CapabilityState Ready(CapabilityState engaged) => engaged;

    /// <summary>Raise <see cref="Changed"/> through the signal boundary. Subclasses call this after
    /// writing a dial; nothing else may raise the event.</summary>
    protected void RaiseChanged() => Signal.Raise(() => Changed?.Invoke());

    /// <summary>True while <paramref name="generation"/> is the module's live one. The registry is
    /// the authority, never a field here.</summary>
    protected bool GenerationIsLive(int generation) => _owner.IsLive(generation);

    /// <summary>
    /// The shared eligibility gate, and the two refusals every module produces the same way.
    ///
    /// <para>Upstream has this test twice, once per kind, and it says the same thing both times:
    /// nothing is started while the service is stopped or the module's own dial is off
    /// (<c>FlashService.cs:538-546</c>; <c>OverlayService.cs:421,427,434-437</c>, where the reconcile
    /// returns at <c>!_isRunning</c> and then stops the layer when the flag is clear). In WPF the
    /// service still reports itself running in that state and nothing ever happens; here it is a
    /// typed refusal instead, so a session can NAME the modules it did not really take.</para>
    ///
    /// <para><b>It does not raise <c>Changed</c>.</b> The two PUBLIC entry points do
    /// (<see cref="Arm"/>, <see cref="Refresh"/>), because a gesture moved something; a module
    /// re-evaluating its own work in the tail of a firing has not, and raising there would put one
    /// extra notification on the boundary per firing for the life of a session.</para>
    /// </summary>
    protected CapabilityState EngageIfEligible()
    {
        int generation;
        lock (_gate)
        {
            if (!_armed || !Enabled)
            {
                ReleaseWork();
                return new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.EffectDialOff,
                    $"the '{Id}' module's own dial is off, so the session armed it and nothing was started"));
            }

            generation = _generation;
        }

        if (!_owner.IsLive(generation))
        {
            ReleaseWork();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectGenerationCancelled,
                $"the '{Id}' module's generation was already cancelled, so nothing was started"));
        }

        return Engage(generation);
    }

    /// <summary>
    /// The owned operation behind an armed module. It does no work: it exists so the module is a
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
            ReleaseWork();
            stopped.TrySetResult();
        });

        await stopped.Task.ConfigureAwait(false);
        ReleaseWork();
        return OperationOutcome.Cancelled.Instance;
    }
}
