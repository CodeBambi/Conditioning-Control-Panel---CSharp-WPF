using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Session;

/// <summary>
/// A scripted session RUNNING: upstream's <c>Services/Session/SessionEngine.cs:22</c>, the timed
/// session with named phases that sits <b>on top of</b> the ordinary <see cref="SessionEngine"/>.
///
/// <para>Slices 1 and 2 of that surface, and deliberately the runtime rather than the rack: the
/// persisted model (<see cref="ScriptedSession"/>), phases advancing on a clock, the live progress
/// and remaining readout, START and STOP with the settings snapshot restored at the end
/// (<see cref="ScriptedSessionDials"/>), the clock-jump guard, the per-tick ramping values
/// (<c>UpdateRampingValues</c>, <c>Services/Session/SessionEngine.cs:564</c> —
/// <see cref="ScriptedSessionRamp"/>), the delayed feature starts (<c>CheckDelayedFeatures</c>,
/// <c>:663</c>) and the ±3 minute jitter on them (<c>RandomizeStartTimes</c>, <c>:777</c>).
/// <b>Not</b> in them, each recorded rather than half-built: the session editor, the rack UI and
/// its repaint, the Session Complete recap and history, the media log, pause and its XP penalty,
/// the XP award itself, the Gamer-Girl corner-GIF window and scheduled bubble bursts.</para>
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
    private readonly PersistenceStore<IntensityRampPresetDocument> _rampCurve;
    private readonly Random _random;
    private readonly EffectSignal? _signal;
    private readonly object _gate = new();

    private ScriptedSession? _session;
    private ScriptedSessionDialSnapshot? _snapshot;
    private ScheduledFire? _pending;
    private bool _running;
    private DateTimeOffset _wallStart;
    private TimeSpan _monotonicStart;
    private int _phaseIndex;
    private double _pinkStartMinute;
    private double _spiralStartMinute;
    private ScriptedSessionRamp _ramp;

    /// <param name="engine">The REAL ordinary engine, never a seam — the rule
    /// <see cref="Scheduling.SessionScheduler"/> states and the reason it gives: a double that
    /// diverges from the product is exactly where a defect hides.</param>
    /// <param name="dials">The user's settings, which this session borrows and gives back.</param>
    /// <param name="clock">The two clocks and the tick timer.</param>
    /// <param name="rampCurve">The user's own easing curve, READ every tick and never captured,
    /// applied or restored. Upstream has one global <c>AppSettings.RampCurve</c> shared by both of
    /// its ramp systems, resolved as <c>settings.RampCurve ?? App.Settings.Current.RampCurve</c>
    /// (<c>Services/Session/SessionEngine.cs:569</c>); in this port that dial belongs to the
    /// Intensity Ramp module's document, which is why a scripted session reaches for it here. It is
    /// re-read per tick rather than captured at START because upstream re-reads it per tick — the
    /// reason its combo box is the one control that stays live during a session
    /// (<c>Features/IntensityRampFeatureControl.xaml:84-86</c>).</param>
    /// <param name="signal">Where this class's own notifications are delivered. Its ticks arrive on
    /// a pool thread, so a UI consumer needs the same marshalling every module's notifications get.
    /// Omitting it raises inline, which is what a caller with no UI wants.</param>
    /// <param name="random">The jitter on a delayed feature's start minute
    /// (<c>Services/Session/SessionEngine.cs:777-805</c>). Injected for the same reason the clock
    /// is — a start time nothing can pin is a start time no fact can check — and defaulted the way
    /// every other module in this port defaults its randomness
    /// (<c>Effects/AudioCueEffect.cs:86</c>).</param>
    public ScriptedSessionRun(
        SessionEngine engine,
        ScriptedSessionDials dials,
        IScriptedClock clock,
        PersistenceStore<IntensityRampPresetDocument> rampCurve,
        EffectSignal? signal = null,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dials);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(rampCurve);
        _engine = engine;
        _dials = dials;
        _clock = clock;
        _rampCurve = rampCurve;
        _signal = signal;
        _random = random ?? new Random();
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

    /// <summary>
    /// <b>The values this session is currently moving, parked over the user's dials rather than
    /// written into them</b> — upstream's session flash overlay plus the two overlay opacities it
    /// drives directly (<c>Models/AppSettings.cs:908-913</c>,
    /// <c>Services/Session/SessionEngine.cs:604-633</c>). Recomputed on every tick and handed back
    /// at STOP (<see cref="ScriptedSessionRamp.None"/>, upstream's <c>ClearSessionFlashRamp</c> at
    /// <c>:343</c>).
    ///
    /// <para><b>A pull, and deliberately silent.</b> Upstream publishes no change notification for
    /// these so a running session does not drag the user's sliders around mid-ramp
    /// (<c>Models/AppSettings.cs:905</c>); its readers see the ramped number because the settings
    /// getters prefer the parked value. This property is that parked value, and it is why nothing
    /// here writes a ramped number into a persisted document — see
    /// <see cref="ScriptedSessionRamp"/> for the defect that rule exists to prevent.</para>
    ///
    /// <para><b>No module reads it in this build</b>, and that is named rather than hidden: the
    /// port's modules read their dials when they ARM and it has no sustained-overlay hold to park a
    /// live value in (<see cref="PinkFilterEffect"/>'s remarks). What a delayed feature comes up
    /// at IS the session's — <see cref="ScriptedSessionDials.ApplyDelayedPinkStart"/> writes the
    /// ramp's first sample — but the climb after that is published here and consumed by nobody
    /// until a module learns to prefer it.</para>
    /// </summary>
    public ScriptedSessionRamp Ramp
    {
        get { lock (_gate) { return _ramp; } }
    }

    /// <summary>
    /// The minute this session's PINK FILTER really begins, after the ±3 minute jitter
    /// (<c>Services/Session/SessionEngine.cs:781-790</c>). Equal to the file's own value when the
    /// filter starts with the session, because upstream jitters only a start greater than zero.
    /// Zero when nothing runs.
    /// </summary>
    public double PinkStartMinute
    {
        get { lock (_gate) { return _pinkStartMinute; } }
    }

    /// <summary>The same for the spiral (<c>:792-801</c>).</summary>
    public double SpiralStartMinute
    {
        get { lock (_gate) { return _spiralStartMinute; } }
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

            // The jitter is decided BEFORE the dials are imposed, exactly where upstream decides it
            // (:180 before :183), and it is what every later comparison against a start minute uses
            // — the ramp's and the delayed start's alike (:611, :690).
            _pinkStartMinute = ScriptedSessionRamp.JitterStartMinute(
                session.Settings.PinkFilterEnabled, session.Settings.PinkFilterStartMinute, _random);
            _spiralStartMinute = ScriptedSessionRamp.JitterStartMinute(
                session.Settings.SpiralEnabled, session.Settings.SpiralStartMinute, _random);
            _ramp = ScriptedSessionRamp.None;

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
            _pinkStartMinute = 0;
            _spiralStartMinute = 0;

            // The parked ramp is handed back BEFORE the dials are, and unconditionally, which is
            // upstream's own order and its own reason: a session overlay left parked would keep
            // overriding the user's values for the rest of the run, and the restore below returns
            // early when there is no snapshot (:340-346).
            _ramp = ScriptedSessionRamp.None;
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
    /// (<c>:520-524</c>), move the phase (<c>:527</c>), move the ramping values (<c>:530</c>) and
    /// then start whatever delayed feature has come due (<c>:533</c>).
    ///
    /// <para><b>That last order is behaviour.</b> The ramp runs FIRST, so a feature whose start
    /// minute arrives on this tick is armed with the dial the ramp's first sample just wrote rather
    /// than with the one it had a second ago (upstream's overlay is already carrying the value by
    /// the time its enable runs, for the same reason). The intermittent bubble bursts upstream
    /// handles last (<c>:536</c>) are not in this slice.</para>
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
        UpdateRampingValues(session, elapsedMinutes);
        CheckDelayedFeatures(session.Settings, elapsedMinutes);
    }

    /// <summary>
    /// The values that move continuously — upstream's <c>UpdateRampingValues</c>
    /// (<c>Services/Session/SessionEngine.cs:564-661</c>). The arithmetic is
    /// <see cref="ScriptedSessionRamp.Compute"/>; what happens to it is here.
    ///
    /// <para><b>Two destinations, and the split is upstream's.</b> The flash trio and the two
    /// overlay opacities are PARKED on <see cref="Ramp"/> and never written into a document, which
    /// is the whole point of the separation (<c>:604-610</c>, upstream issues #471/#476). The
    /// bubble frequency is the one value upstream really does write into the user's dial
    /// (<c>:644-647</c>), so it is written here too — through the module's own entry point, which
    /// re-times a field that is already spawning exactly as upstream's <c>RefreshFrequency()</c>
    /// does. The snapshot taken at START is what gives the user's rate back at the end.</para>
    /// </summary>
    private void UpdateRampingValues(ScriptedSession session, double elapsedMinutes)
    {
        var settings = session.Settings;

        // Upstream re-reads the curve every tick (:569): the per-session override when the file
        // sets one, the user's own dial otherwise.
        var curve = settings.RampCurve ?? _rampCurve.Current.Curve;
        var ramp = ScriptedSessionRamp.Compute(
            settings,
            elapsedMinutes,
            session.DurationMinutes,
            curve,
            PinkStartMinute,
            SpiralStartMinute);

        lock (_gate)
        {
            // A stop that landed while this tick was computing has already handed the ramp back;
            // parking a stale one over the user's dials again is the exact leak :340-343 warns of.
            if (_running)
            {
                _ramp = ramp;
            }
        }

        if (ScriptedSessionRamp.BubblesPerMinute(settings, elapsedMinutes) is { } perMinute)
        {
            // Outside the gate: this reaches a module, which raises its own notifications.
            _engine.Effects.OfType<BubblePopEffect>().FirstOrDefault()?.SetPerMinute(perMinute);
        }
    }

    /// <summary>
    /// The features that turn themselves on part-way through — upstream's
    /// <c>CheckDelayedFeatures</c> (<c>Services/Session/SessionEngine.cs:663-772</c>) for the three
    /// starts the shipped session files really use: the pink filter (<c>:687-697</c>), the spiral
    /// (<c>:699-728</c>) and the bubbles (<c>:730-738</c>).
    ///
    /// <para><b>This is what makes <see cref="ScriptedSessionDials.Apply"/>'s deliberate OFF at
    /// t=0 finish</b>: a session whose pink filter starts at minute 10 applies it off and nothing
    /// turned it on until now.</para>
    ///
    /// <para><b>Not ported, each with its reason.</b> The queue of deferred timeline starts
    /// (<c>:668-685</c>) is fed by per-feature <c>StartMinute</c>/<c>EndMinute</c> events the port's
    /// model does not carry and the editor that writes them is unported. The spiral's
    /// "are there any spiral files" probe (<c>:702-715</c>) is answered here by the module itself:
    /// <see cref="SpiralOverlayEffect"/> resolves its own library on every engage and its refusal
    /// is recorded verbatim in <see cref="SessionEngine.ArmOutcomes"/>, where upstream's version
    /// silently switched the session's dial off. The corner GIF (<c>:740-760</c>) has no window in
    /// this port, and brain drain (<c>:762-771</c>) is commented out upstream — porting dead code
    /// would be inventing behaviour, not preserving it.</para>
    /// </summary>
    private void CheckDelayedFeatures(ScriptedSessionSettings settings, double elapsedMinutes)
    {
        if (settings.PinkFilterEnabled && elapsedMinutes >= PinkStartMinute)
        {
            StartDelayedFeature(PinkFilterEffect.EffectId, () => _dials.ApplyDelayedPinkStart(settings));
        }

        if (settings.SpiralEnabled && elapsedMinutes >= SpiralStartMinute)
        {
            StartDelayedFeature(SpiralOverlayEffect.EffectId, () => _dials.ApplyDelayedSpiralStart(settings));
        }

        // Upstream's conjunction, verbatim (:731): a start minute of 0 was already turned on by
        // Apply, and an intermittent session's bubbles arrive as scheduled bursts instead.
        if (settings.BubblesEnabled
            && settings.BubblesStartMinute > 0
            && !settings.BubblesIntermittent
            && elapsedMinutes >= settings.BubblesStartMinute)
        {
            StartDelayedFeature(BubblePopEffect.EffectId, prepareDial: null);
        }
    }

    /// <summary>
    /// Turn one feature on NOW — upstream's pair of acts for each of the three
    /// (<c>Services/Session/SessionEngine.cs:692-693</c>): write the dial, then take the feature
    /// live rather than leaving it for the next START.
    ///
    /// <para>The guard is upstream's own — "the session wants it and the user's live dial has not
    /// got it yet" (<c>:688</c>, <c>:700</c>, <c>:731</c>) — which is what makes this idempotent
    /// across the ticks that follow, and what leaves a filter the user switched on by hand alone.
    /// <see cref="SessionEngine.QuickToggle"/> is the port's own version of the second act: it
    /// flips the persisted flag and arms the module when the engine is running, which is what
    /// upstream's <c>_mainWindow.EnableX(true)</c> does. Its save writes the flash preset, which
    /// this session's START already wrote (<see cref="ScriptedSessionDials"/>'s remarks) — no
    /// document reaches disk here that was not already going to.</para>
    ///
    /// <para>A composition whose rack has no such module does nothing at all, which is the honest
    /// answer for a feature this build does not have: in this port the dial belongs to the module,
    /// so there is no dial to move without one.</para>
    /// </summary>
    private void StartDelayedFeature(string effectId, Action? prepareDial)
    {
        var effect = _engine.Effects.FirstOrDefault(
            e => string.Equals(e.Id, effectId, StringComparison.Ordinal));
        if (effect is null || effect.Enabled)
        {
            return;
        }

        prepareDial?.Invoke();
        _engine.QuickToggle(effectId);
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
