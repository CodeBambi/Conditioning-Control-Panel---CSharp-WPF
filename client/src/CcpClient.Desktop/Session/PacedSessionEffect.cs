using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The body every self-paced rack module has, written once.
///
/// <para><b>Why this class exists, and what it is really for.</b> One effect was built first,
/// and a later packet made it draw. Thirteen more modules follow the same shape, and the failure this
/// packet was created to catch is fourteen copies of <c>FlashImagesEffect</c> with the nouns
/// changed. Everything below is the part that was identical between Flash Images and Subliminals
/// once both were written: the single one-shot on the injected clock re-armed at the tail of each
/// firing, the three stale-generation checks, the counter, the last firing, and the one dispatch
/// boundary the UI projection goes through. A module contributes its identity, its dial, its
/// interval and its payload — nothing else.</para>
///
/// <para><b>What moved OUT of here, and why.</b> The arm/disarm pair, the owned generation
/// and its parked completion, the dial-off and generation-cancelled refusals, the change signal and
/// the dot now live in <see cref="OwnedSessionEffect"/>. That was not tidying: a later packet built the
/// first CONTINUOUS module — Pink Filter, which WPF drives with no timer at all
/// (<c>MainWindow/MainWindow.Presets.cs:1255</c>) — and every one of those parts was needed by it
/// verbatim while every part still in this file was meaningless to it. The split is the answer to
/// the shared body's own open question: the spine is <see cref="ISessionEffect"/>, and this class is one
/// implementation of it rather than the spine itself. <b>Nothing in this file's behaviour changed;
/// what changed is where the shared half lives.</b></para>
///
/// <para><b>Where the two paced modules genuinely differ</b>, and therefore what the hooks had to
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
/// <see cref="Interlocked"/>, never under <see cref="OwnedSessionEffect.Gate"/>, so a cancellation
/// callback can run while another thread holds the gate without a lock cycle.</para>
/// </summary>
/// <typeparam name="TFiring">The module's own record of one firing. It carries whatever the UI
/// projection needs, which is why it is the module's type and not a shared one: Flash Images' firing
/// carries drawn file paths that must never reach a log or an event, so the record it hands to
/// subscribers and the record it hands to the surface are different halves of the same object.</typeparam>
public abstract class PacedSessionEffect<TFiring> : OwnedSessionEffect
    where TFiring : class
{
    private ScheduledFire? _pending;
    private int _fireCount;
    private TFiring? _last;

    /// <param name="owner">The module's operation owner: one generation per armed schedule.</param>
    /// <param name="signal">Where <c>Changed</c> and the UI projection are allowed to arrive.</param>
    /// <param name="clock">The injected clock the schedule paces on. Never <c>DateTime.Now</c>.</param>
    /// <param name="operationName">The registered operation's name, e.g. <c>flash-schedule</c>.</param>
    protected PacedSessionEffect(
        AsyncOperationOwner owner, EffectSignal signal, ISessionClock clock, string operationName)
        : base(owner, signal, operationName)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
    }

    /// <summary>The clock this module paces on. Subclasses read <c>UtcNow</c> from it and nowhere else.</summary>
    protected ISessionClock Clock { get; }

    /// <summary>Firings that have really come due since this module was first armed. What "really"
    /// means is the module's: a firing that produced nothing is not counted if upstream does not
    /// count it (<see cref="Compose"/>).</summary>
    public int FireCount
    {
        get { lock (Gate) { return _fireCount; } }
    }

    /// <summary>The most recent firing, or null if none has happened yet.</summary>
    public TFiring? LastFiring
    {
        get { lock (Gate) { return _last; } }
    }

    /// <summary>
    /// True while a next firing is actually on the clock. This — not a stored bool, and not the
    /// persisted dial — is what the row's dot is entitled to call "running": it is false the
    /// instant the schedule is torn down, whatever any flag still says.
    /// </summary>
    public bool ScheduleArmed => Volatile.Read(ref _pending) is not null;

    /// <summary>
    /// A paced module's answer to "is the work really running": a firing is genuinely on the clock.
    /// It is a claim about the CLOCK, and it stays true over a firing that will show nothing —
    /// Subliminals with an empty pool is correctly <c>Live</c>. A continuous module cannot answer
    /// this way, which is why the clause is the module's and not the base's.
    ///
    /// <para><b>Overridable, and it was SEALED until the fifth module (that packet's only shared-code change,
    /// and it is one word).</b> An earlier packet made this clause abstract in
    /// <see cref="OwnedSessionEffect"/> on the finding that "the AUTHORITY behind the third one is
    /// the module's, not the base's" — and then this class immediately took that authority back for
    /// every paced module by sealing its answer. That held for four modules because for all four the
    /// clock was the whole story. It broke on the fifth: an AUDIO module's firing can be perfectly
    /// scheduled while the OS reports no render session for this process, and a dot that read
    /// <c>Live</c> there would claim a sound that reaches nobody — in the one medium where a user
    /// cannot check by looking.</para>
    ///
    /// <para><b>The contract for an override, and it is one-directional:</b> a subclass may only
    /// NARROW this — it must keep <see cref="ScheduleArmed"/> as a conjunct and add its own further
    /// condition. Widening it (returning true with no firing on the clock) would put back exactly the
    /// stored "I was told to start" bool that <see cref="OwnedSessionEffect.WorkIsRunning"/> exists to
    /// forbid. <see cref="ScheduleArmed"/> is public precisely so the narrowing is expressible, and
    /// so a test can pin both conjuncts separately.</para>
    /// </summary>
    protected override bool WorkIsRunning => ScheduleArmed;

    /// <summary>
    /// Re-pace the pending firing against the current dial — WPF <c>RefreshSchedule</c>
    /// (<c>FlashService.cs:527-531</c>), which its frequency slider calls so a change takes effect
    /// now instead of after the old interval expires. A no-op when nothing is armed, which is WPF's
    /// <c>if (!_isRunning) return;</c> (<c>:529</c>). The name is kept for its callers; the body is
    /// <see cref="OwnedSessionEffect.Refresh"/>, which the continuous modules share.
    /// </summary>
    public void RefreshSchedule() => Refresh();

    /// <summary>The interval until the next firing, read from this module's own dial. Called off the
    /// gate, so an implementation may read a persisted store.</summary>
    protected abstract TimeSpan NextInterval();

    /// <summary>
    /// Do the module's work for one firing and return what it produced, or <c>null</c> when nothing
    /// came of it and upstream would not have counted it. Runs OUTSIDE <see cref="OwnedSessionEffect.Gate"/>:
    /// a pool may touch a filesystem, and a session must never be able to wedge its own state behind
    /// a slow directory. The ordinal and the timestamp are not this method's business — see
    /// <see cref="Stamp"/>.
    /// </summary>
    protected abstract TFiring? Compose();

    /// <summary>
    /// Stamp the ordinal and the moment onto a composed firing. Called UNDER
    /// <see cref="OwnedSessionEffect.Gate"/>, after the post-draw liveness re-check, so the ordinal a
    /// subscriber sees is the one the counter really reached. On a record this is one <c>with</c>
    /// expression.
    /// </summary>
    protected abstract TFiring Stamp(TFiring firing, int ordinal, DateTimeOffset at);

    /// <summary>
    /// Put the firing where the user can experience it. Called on the signal thread, inside the
    /// projection, with the generation already re-checked — so an implementation may touch a native
    /// window and must not re-check liveness itself.
    /// </summary>
    protected abstract void Deliver(TFiring firing);

    /// <summary>
    /// WPF's <c>ScheduleNext</c> (<c>FlashService.cs:538-563</c>,
    /// <c>SubliminalService.cs:172-187</c>), minus the two guards the shared base now owns. A
    /// previous pending one-shot is dropped first, exactly as WPF stops its timer before restarting
    /// it.
    /// </summary>
    protected sealed override CapabilityState Engage(int generation)
    {
        var due = NextInterval();

        // Published BEFORE the clock is asked, so a schedule that fires immediately finds its own
        // identity already in the slot; Attach closes the other half of that window.
        var token = new ScheduledFire();
        Interlocked.Exchange(ref _pending, token)?.Dispose();
        token.Attach(Clock.Schedule(due, () => Fire(generation, token)));

        // The generation may have been cancelled between the liveness check and the schedule.
        // Re-check and tear the handle straight back down: a stop must never leave a live
        // one-shot behind, and this is the only window in which one could exist.
        if (!GenerationIsLive(generation))
        {
            ReleaseWork();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectGenerationCancelled,
                $"the '{Id}' module's generation was cancelled while the firing was being scheduled"));
        }

        return new CapabilityState.Available(
            $"the '{Id}' module is armed and the next firing is on the clock in {due.TotalSeconds:0.###}s");
    }

    /// <summary>Drop the pending one-shot. Lock-free: it runs from the generation's cancellation
    /// callback on a teardown thread.</summary>
    protected sealed override void ReleaseWork() => Interlocked.Exchange(ref _pending, null)?.Dispose();

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
        // Both halves matter. Blindly clearing the slot (what this was before) would null
        // out the LIVE timer's identity, leaving the dot reporting Armed with a firing on the clock
        // and, worse, leaving Disarm nothing to dispose. Letting the superseded callback proceed
        // would fire at the OLD pace immediately after the user moved the frequency slider, which
        // is the one thing RefreshSchedule exists to prevent.
        if (Interlocked.CompareExchange(ref _pending, null, token) != token)
        {
            return;
        }

        // A spent one-shot is still an undisposed timer. Dropping the handle here — which is what
        // this did before — leaks one OS timer per firing for the life of a session, and at
        // the subliminal module's own default that is five an hour... per minute. Disposing from
        // inside a timer's own callback is documented as safe and is what makes the count of live
        // handles after a stop exactly zero rather than "however many fired".
        token.Dispose();

        // Stale-generation check next (contract §5.5): a callback that survives a cancel does
        // nothing and re-schedules nothing.
        if (!GenerationIsLive(generation))
        {
            return;
        }

        lock (Gate)
        {
            if (!IsArmedFor(generation) || !Enabled)
            {
                return;
            }
        }

        // Outside the gate: the work may touch a filesystem.
        var composed = Compose();

        TFiring? fired = null;
        lock (Gate)
        {
            // Re-check after the work. A stop that landed mid-firing wins: nothing is counted and
            // nothing is re-scheduled.
            if (!IsArmedFor(generation))
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

        // The tail re-arm, NOT Refresh(): a firing that re-schedules itself has moved no dial, and
        // routing it through the public entry point would add one Changed notification per firing.
        EngageIfEligible();
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
        if (!Signal.IsBound || !GenerationIsLive(generation))
        {
            return;
        }

        Signal.Post(() =>
        {
            if (!GenerationIsLive(generation))
            {
                return;
            }

            Deliver(fired);
        });
    }
}
