namespace CcpClient.Desktop.Session;

/// <summary>
/// A scripted session RUNNING: upstream's <c>Services/Session/SessionEngine.cs:22</c>, the timed
/// session with named phases that sits <b>on top of</b> the ordinary <see cref="SessionEngine"/>.
///
/// <para>Slice 1 of that surface, and deliberately the runtime rather than the rack: the persisted
/// model (<see cref="ScriptedSession"/>), phases advancing on a clock, the live progress and
/// remaining readout, START and STOP with the settings snapshot restored at the end
/// (<see cref="ScriptedSessionDials"/>), and the clock-jump guard. <b>Not</b> in it, each recorded
/// rather than half-built: the session editor, the rack UI and its repaint, the Session Complete
/// recap and history, the media log, pause and its XP penalty, the XP award itself, the Gamer-Girl
/// corner-GIF window, scheduled bubble bursts, the ±3 min randomized ramp starts, the per-tick
/// ramping values (<c>UpdateRampingValues</c>, <c>Services/Session/SessionEngine.cs:564</c>) and
/// the delayed feature starts (<c>CheckDelayedFeatures</c>, <c>:663</c>). </para>
///
/// <para><b>It owns the ordinary engine from outside</b>, the way
/// <see cref="Scheduling.SessionScheduler"/> does, and for upstream's reason: starting a scripted
/// session starts the engine if it is not already running
/// (<c>MainWindow/MainWindow.Presets.cs:1509-1512</c>). Ending one does NOT stop it — upstream's
/// <c>StopSession</c> restores the settings and leaves the engine running
/// (<c>Services/Session/SessionEngine.cs:287-425</c>) — so the port re-arms it instead, which is
/// the port's equivalent of upstream's per-feature "stop the service, start the service" inside
/// <c>ApplySessionSettings</c> (<c>:1263</c>, <c>:1167-1171</c>). It has to: upstream's services
/// re-read <c>AppSettings</c> live, and this port's modules read their dials when they ARM.</para>
///
/// <para><b>Every clock is injected</b> (<see cref="IScriptedClock"/>). Nothing here reads
/// <c>DateTime.Now</c>, starts a <c>Stopwatch</c>, sleeps or delays, which is what makes the
/// clock-jump guard provable at all.</para>
/// </summary>
public sealed class ScriptedSessionRun
{
    /// <summary>
    /// The main timer's interval — upstream's <c>DispatcherTimer { Interval =
    /// TimeSpan.FromSeconds(1) }</c> (<c>Services/Session/SessionEngine.cs:222-227</c>). Every
    /// number a user reads is recomputed from the clock on each tick, so the interval sets how
    /// often the readout refreshes and never how far the session has got.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <b>The clock-jump guard's threshold: 30 seconds, in either direction.</b> Upstream
    /// <c>Services/Session/SessionEngine.cs:104</c> —
    /// <c>if (Math.Abs(divergence.TotalSeconds) > 30)</c>, whose own comment names both halves: a
    /// POSITIVE divergence guards against speed-hacking, a NEGATIVE one against a backward
    /// wall-clock jump (NTP, sleep/resume) "which otherwise makes RemainingTime balloon (e.g. '149
    /// minutes left' on a 30-minute session)" (<c>:100-103</c>, upstream issue #369).
    ///
    /// <para>Strictly greater, so a divergence of exactly 30 s still trusts the wall clock. That
    /// boundary is pinned by its own fact.</para>
    /// </summary>
    public const double ClockJumpToleranceSeconds = 30;

    private readonly SessionEngine _engine;
    private readonly ScriptedSessionDials _dials;
    private readonly IScriptedClock _clock;
    private readonly EffectSignal? _signal;
    private readonly object _gate = new();

    private ScriptedSession? _session;
    private ScriptedSessionDialSnapshot? _snapshot;
    private ScheduledFire? _pending;
    private bool _running;
    private DateTimeOffset _wallStart;
    private TimeSpan _monotonicStart;
    private int _phaseIndex;

    /// <param name="engine">The REAL ordinary engine, never a seam — the rule
    /// <see cref="Scheduling.SessionScheduler"/> states and the reason it gives: a double that
    /// diverges from the product is exactly where a defect hides.</param>
    /// <param name="dials">The user's settings, which this session borrows and gives back.</param>
    /// <param name="clock">The two clocks and the tick timer.</param>
    /// <param name="signal">Where this class's own notifications are delivered. Its ticks arrive on
    /// a pool thread, so a UI consumer needs the same marshalling every module's notifications get.
    /// Omitting it raises inline, which is what a caller with no UI wants.</param>
    public ScriptedSessionRun(
        SessionEngine engine,
        ScriptedSessionDials dials,
        IScriptedClock clock,
        EffectSignal? signal = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dials);
        ArgumentNullException.ThrowIfNull(clock);
        _engine = engine;
        _dials = dials;
        _clock = clock;
        _signal = signal;
    }

    /// <summary>A phase became current: the phase and its index. Upstream's <c>PhaseChanged</c>
    /// (<c>Services/Session/SessionEngine.cs:24</c>), raised for phase 0 at START as upstream
    /// raises it (<c>:264-267</c>).</summary>
    public event Action<ScriptedSessionPhase, int>? PhaseChanged;

    /// <summary>One tick's readout. Upstream's <c>ProgressUpdated</c> (<c>:25</c>,
    /// <c>:520-524</c>).</summary>
    public event Action<ScriptedSessionProgress>? ProgressUpdated;

    /// <summary>The session ended, by completion or by a stop. Upstream splits this into
    /// <c>SessionStopped</c> and <c>SessionCompleted</c> (<c>:26-28</c>); the port carries one
    /// event with the flag on it, because every consumer of either needs to know which
    /// happened.</summary>
    public event Action<ScriptedSessionOutcome>? Ended;

    /// <summary>Upstream's <c>IsRunning</c>
    /// (<c>Services/Session/SessionEngine.cs:81</c>).</summary>
    public bool Running
    {
        get { lock (_gate) { return _running; } }
    }

    /// <summary>The session on the clock, or null. Upstream's <c>CurrentSession</c>
    /// (<c>:91</c>).</summary>
    public ScriptedSession? Current
    {
        get { lock (_gate) { return _session; } }
    }

    /// <summary>Upstream's <c>CurrentPhaseIndex</c> (<c>:88</c>). Zero when nothing runs.</summary>
    public int CurrentPhaseIndex
    {
        get { lock (_gate) { return _phaseIndex; } }
    }

    /// <summary>The phase a user is in right now, or null when nothing runs.</summary>
    public ScriptedSessionPhase? CurrentPhase
    {
        get
        {
            lock (_gate)
            {
                return _session is not null && _phaseIndex < _session.Phases.Count
                    ? _session.Phases[_phaseIndex]
                    : null;
            }
        }
    }

    /// <summary>
    /// How long this session has really been running — upstream's <c>ElapsedTime</c>
    /// (<c>Services/Session/SessionEngine.cs:92-116</c>), <b>including its clock-jump guard</b>.
    /// Zero when nothing runs (<c>:95</c>). See <see cref="Reconcile"/> for the rule and
    /// <see cref="ReadElapsed"/> for which clock a given reading came from.
    /// </summary>
    public TimeSpan Elapsed => ReadElapsed().Elapsed;

    /// <summary>Upstream's <c>RemainingTime</c> (<c>:117-126</c>): the duration less
    /// <see cref="Elapsed"/>, floored at zero, and zero when nothing runs.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var (session, reading) = ReadState();
            if (session is null)
            {
                return TimeSpan.Zero;
            }

            var remaining = TimeSpan.FromMinutes(session.DurationMinutes) - reading.Elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>Upstream's <c>ProgressPercent</c> (<c>:128-130</c>): elapsed over duration as a
    /// percentage, capped at 100, and zero when nothing runs.</summary>
    public double ProgressPercent
    {
        get
        {
            var (session, reading) = ReadState();
            return session is null
                ? 0
                : Math.Min(100, reading.Elapsed.TotalMinutes / session.DurationMinutes * 100);
        }
    }

    /// <summary>
    /// <b>The clock-jump guard, as a pure function of the two readings</b> — upstream
    /// <c>Services/Session/SessionEngine.cs:96-115</c>.
    ///
    /// <para>Both arguments are elapsed spans since the session started: <paramref name="wall"/>
    /// from the wall clock, which can jump, and <paramref name="monotonic"/> from a clock that
    /// cannot. When they disagree by more than <see cref="ClockJumpToleranceSeconds"/> in EITHER
    /// direction the monotonic reading wins (<c>:104-110</c>). Otherwise the wall clock does, never
    /// below zero (<c>:114</c>) — upstream's floor for a small backward step that would otherwise
    /// report negative elapsed time.</para>
    /// </summary>
    public static ScriptedElapsedReading Reconcile(TimeSpan wall, TimeSpan monotonic)
    {
        var divergence = wall - monotonic;
        if (Math.Abs(divergence.TotalSeconds) > ClockJumpToleranceSeconds)
        {
            return new ScriptedElapsedReading(monotonic, wall, monotonic, UsedMonotonic: true);
        }

        var elapsed = wall < TimeSpan.Zero ? TimeSpan.Zero : wall;
        return new ScriptedElapsedReading(elapsed, wall, monotonic, UsedMonotonic: false);
    }

    /// <summary>
    /// <see cref="Elapsed"/> with the working shown: both clocks' readings and whether the guard
    /// took the monotonic one. It is a pure read — asking twice cannot change an answer or a count
    /// — and it is what a fact asserts against instead of inferring the guard from a number that
    /// happens to match.
    /// </summary>
    public ScriptedElapsedReading ReadElapsed() => ReadState().Reading;

    /// <summary>
    /// The three numbers a session's readout shows, taken together from one clock reading —
    /// upstream's <c>SessionProgressEventArgs</c> (<c>Services/Session/SessionEngine.cs:1994-2006</c>),
    /// which is built from one <c>ElapsedTime</c> read for the same reason.
    /// </summary>
    public ScriptedSessionProgress ReadProgress()
    {
        var (session, reading) = ReadState();
        if (session is null)
        {
            return new ScriptedSessionProgress(TimeSpan.Zero, TimeSpan.Zero, 0);
        }

        var remaining = TimeSpan.FromMinutes(session.DurationMinutes) - reading.Elapsed;
        return new ScriptedSessionProgress(
            reading.Elapsed,
            remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
            Math.Min(100, reading.Elapsed.TotalMinutes / session.DurationMinutes * 100));
    }

    /// <summary>
    /// START, in upstream's order (<c>Services/Session/SessionEngine.cs:148-270</c>,
    /// <c>MainWindow/MainWindow.Presets.cs:1509-1512</c>): snapshot the user's dials BEFORE
    /// anything
    /// is touched (<c>:173</c>), impose the session's (<c>:183</c>), take both clock readings
    /// (<c>:167-168</c>), put the tick on the clock (<c>:222-227</c>), and announce phase 0
    /// (<c>:264-267</c>).
    ///
    /// <para><b>The engine is (re)started AFTER the dials are applied</b>, where upstream starts it
    /// before. Not a reordering for its own sake: upstream's services re-read the live
    /// <c>AppSettings</c>, so it can apply dials into running services; this port's modules read
    /// their dials when they arm, so a module armed first would run the whole session on the user's
    /// settings instead of the session's. The user-visible outcome — every module running on the
    /// session's dials — is upstream's.</para>
    ///
    /// <para>Returns false when a session is already running. Upstream throws there
    /// (<c>:150-153</c>); the port answers as <see cref="SessionEngine.Start"/> does, because the
    /// outcome — you cannot start a second one — is the same and every other START in this port
    /// reports it this way.</para>
    /// </summary>
    public bool Start(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        ScriptedSessionPhase? firstPhase;
        lock (_gate)
        {
            if (_running)
            {
                return false;
            }

            _session = session;
            _snapshot = _dials.Capture();
            _dials.Apply(session.Settings);
            _wallStart = _clock.Now;
            _monotonicStart = _clock.Monotonic;
            _phaseIndex = 0;
            _running = true;
            firstPhase = session.Phases.Count > 0 ? session.Phases[0] : null;
            Arm(TickInterval);
        }

        // Outside the lock: the engine raises its own notifications, and holding this gate across
        // another component's event is how two locks become one deadlock.
        if (_engine.Running)
        {
            _engine.Stop();
        }

        _engine.Start();

        if (firstPhase is { } phase)
        {
            Raise(() => PhaseChanged?.Invoke(phase, 0));
        }

        return true;
    }

    /// <summary>
    /// STOP, in upstream's order (<c>Services/Session/SessionEngine.cs:287-425</c>): the final
    /// elapsed time is read BEFORE the running flag clears, because the flag is what makes
    /// <see cref="Elapsed"/> report zero (<c>:291-293</c> — upstream comments the same trap); the
    /// tick comes off the clock (<c>:309-313</c>); the user's dials come back (<c>:347</c>); and
    /// the end is announced.
    /// </summary>
    /// <param name="completed">The session reached its duration. Upstream's
    /// <c>StopSession(completed:)</c> (<c>:287</c>), which is what separates a finished session
    /// from an abandoned one.</param>
    /// <returns>False when nothing was running.</returns>
    public bool Stop(bool completed = false)
    {
        ScriptedSession session;
        TimeSpan finalElapsed;
        ScriptedSessionDialSnapshot? snapshot;
        lock (_gate)
        {
            if (!_running || _session is null)
            {
                return false;
            }

            finalElapsed = Reconcile(
                _clock.Now - _wallStart,
                _clock.Monotonic - _monotonicStart).Elapsed;
            session = _session;
            snapshot = _snapshot;
            _running = false;
            _session = null;
            _snapshot = null;
            _phaseIndex = 0;
            Interlocked.Exchange(ref _pending, null)?.Dispose();
        }

        if (snapshot is not null)
        {
            _dials.Restore(snapshot);
        }

        // Upstream leaves the engine running and lets its services re-read the restored settings
        // (:347-349). This port's modules read their dials at arm, so the restore only reaches them
        // through a re-arm. An engine the user stopped by hand mid-session stays stopped.
        if (_engine.Running)
        {
            _engine.Stop();
            _engine.Start();
        }

        Raise(() => Ended?.Invoke(new ScriptedSessionOutcome(session, finalElapsed, completed)));
        return true;
    }

    /// <summary>
    /// One tick — upstream's <c>MainTimer_Tick</c>
    /// (<c>Services/Session/SessionEngine.cs:504-537</c>), in its order:
    /// refuse when nothing runs (<c>:506</c>), end the session the moment elapsed reaches the
    /// duration and do nothing else that tick (<c>:512-517</c>), publish the readout
    /// (<c>:520-524</c>), then move the phase (<c>:527</c>).
    ///
    /// <para>Public because it is the decision, and the clock is only how it is delivered — the
    /// shape <see cref="Scheduling.SessionScheduler.Tick"/> already uses.</para>
    /// </summary>
    public void Tick()
    {
        var (session, reading) = ReadState();
        if (session is null)
        {
            return;
        }

        var elapsedMinutes = reading.Elapsed.TotalMinutes;
        if (elapsedMinutes >= session.DurationMinutes)
        {
            Stop(completed: true);
            return;
        }

        var remaining = TimeSpan.FromMinutes(session.DurationMinutes) - reading.Elapsed;
        var progress = new ScriptedSessionProgress(
            reading.Elapsed,
            remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
            Math.Min(100, elapsedMinutes / session.DurationMinutes * 100));
        Raise(() => ProgressUpdated?.Invoke(progress));

        CheckPhaseTransition(session, elapsedMinutes);
    }

    /// <summary>
    /// Which phase is current — upstream's <c>CheckPhaseTransition</c>
    /// (<c>Services/Session/SessionEngine.cs:540-562</c>): scan from the LAST phase back and take
    /// the first whose
    /// start minute has passed, falling back to phase 0. The comparison against the current index
    /// is
    /// upstream's <c>!=</c> and not <c>&gt;</c>, so a phase can also move BACK — which is exactly
    /// what happens on the tick after the clock-jump guard corrects a wall clock that had run
    /// ahead.
    /// </summary>
    private void CheckPhaseTransition(ScriptedSession session, double elapsedMinutes)
    {
        var phases = session.Phases;
        if (phases.Count == 0)
        {
            return;
        }

        var newIndex = 0;
        for (var i = phases.Count - 1; i >= 0; i--)
        {
            if (elapsedMinutes >= phases[i].StartMinute)
            {
                newIndex = i;
                break;
            }
        }

        ScriptedSessionPhase? changed = null;
        lock (_gate)
        {
            if (_running && newIndex != _phaseIndex)
            {
                _phaseIndex = newIndex;
                changed = phases[newIndex];
            }
        }

        if (changed is { } phase)
        {
            Raise(() => PhaseChanged?.Invoke(phase, newIndex));
        }
    }

    private (ScriptedSession? Session, ScriptedElapsedReading Reading) ReadState()
    {
        lock (_gate)
        {
            if (!_running || _session is null)
            {
                return (null, default);
            }

            return (_session, Reconcile(_clock.Now - _wallStart, _clock.Monotonic - _monotonicStart));
        }
    }

    /// <summary>
    /// Put the next tick on the clock. The token is published to the pending slot BEFORE the clock
    /// is asked, for the reason <see cref="ScheduledFire"/> gives: a schedule due immediately can
    /// fire before <see cref="IScriptedClock.Schedule"/> returns.
    /// </summary>
    private void Arm(TimeSpan due)
    {
        var token = new ScheduledFire();
        Interlocked.Exchange(ref _pending, token)?.Dispose();
        token.Attach(_clock.Schedule(due, () => OnDue(token)));

        // A stop can land between publishing the token and receiving the handle. Tear it straight
        // back down: a stopped session must never leave a live one-shot behind it.
        if (!Running)
        {
            Interlocked.Exchange(ref _pending, null)?.Dispose();
        }
    }

    /// <summary>
    /// A tick comes due. The next one goes on the clock BEFORE the decision runs — upstream's
    /// repeating <c>DispatcherTimer</c> re-arms itself whatever the tick body does
    /// (<c>Services/Session/SessionEngine.cs:222-227</c>), and a decision that threw would
    /// otherwise end the session's
    /// clock in silence. A tick that stops the session cancels the newly armed one on its way down.
    /// </summary>
    private void OnDue(ScheduledFire token)
    {
        // CompareExchange, not Exchange: clear the slot only if it still holds THIS tick. A
        // callback from a superseded or cancelled schedule does nothing at all (the identity rule).
        if (Interlocked.CompareExchange(ref _pending, null, token) != token)
        {
            return;
        }

        if (!Running)
        {
            return;
        }

        Arm(TickInterval);
        Tick();
    }

    private void Raise(Action action)
    {
        if (_signal is null)
        {
            action();
            return;
        }

        _signal.Raise(action);
    }
}

/// <summary>
/// One reading of how long a session has run, with both clocks' answers and which one was used.
/// </summary>
/// <param name="Elapsed">The answer: what <see cref="ScriptedSessionRun.Elapsed"/> reports.</param>
/// <param name="Wall">The wall clock's elapsed span since the session started.</param>
/// <param name="Monotonic">The monotonic clock's elapsed span since the session started.</param>
/// <param name="UsedMonotonic">True when the two disagreed by more than
/// <see cref="ScriptedSessionRun.ClockJumpToleranceSeconds"/> and the monotonic reading
/// won.</param>
public readonly record struct ScriptedElapsedReading(
    TimeSpan Elapsed, TimeSpan Wall, TimeSpan Monotonic, bool UsedMonotonic)
{
    /// <summary>How far the wall clock is ahead of (positive) or behind (negative) the monotonic
    /// one — upstream's <c>divergence</c> (<c>Services/Session/SessionEngine.cs:103</c>).</summary>
    public TimeSpan Divergence => Wall - Monotonic;
}

/// <summary>The readout a running session shows — upstream's <c>SessionProgressEventArgs</c>
/// (<c>Services/Session/SessionEngine.cs:1994-2006</c>).</summary>
/// <param name="Elapsed">Time served.</param>
/// <param name="Remaining">Time left, never negative.</param>
/// <param name="Percent">Progress, capped at 100.</param>
public readonly record struct ScriptedSessionProgress(
    TimeSpan Elapsed, TimeSpan Remaining, double Percent);

/// <summary>How a scripted session ended — upstream's <c>SessionCompletedEventArgs</c>
/// (<c>Services/Session/SessionEngine.cs:2008-2020</c>) without the XP members, which are not in
/// this slice.</summary>
/// <param name="Session">The session that ended.</param>
/// <param name="Elapsed">Its final elapsed time, read before the running flag cleared.</param>
/// <param name="Completed">True when it reached its duration; false when it was stopped
/// early.</param>
public sealed record ScriptedSessionOutcome(
    ScriptedSession Session, TimeSpan Elapsed, bool Completed);
