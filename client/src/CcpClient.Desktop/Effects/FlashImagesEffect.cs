using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One flash that came due: how many images the draw actually produced, and when.
/// Content-free by construction — a COUNT, never a file name (the media-logging rule the DTRH
/// manifest already holds, <c>Features/Dtrh/DtrhUserMedia.cs</c>: counts only, never names).
/// </summary>
public sealed record FlashEvent(int Ordinal, int ImagesDrawn, DateTimeOffset At)
{
    /// <summary>True when the draw came back empty: the pool has nothing active in it.</summary>
    public bool PoolWasEmpty => ImagesDrawn == 0;
}

/// <summary>
/// <b>Flash Images</b> — WPF's first EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:484</c>), the first service <c>StartEngine</c> starts
/// (<c>MainWindow/MainWindow.StartStop.cs:178</c>) and the first one <c>StopEngineCore</c> stops
/// (<c>:305</c>, "Stop flash first").
///
/// <para><b>WHAT THIS PORTS.</b> WPF's flash has two halves. The first is a scheduler over the
/// user's image pool: an interval derived from a dial, a variance band, a floor, and a draw of N
/// images per firing. That half is pure, and it is ported here, exactly (SP-098). The second half
/// puts those images on the screen ABOVE every other application — one layered, always-on-top,
/// <c>WS_EX_TRANSPARENT</c> click-through window per flash, re-asserted to <c>HWND_TOPMOST</c> as
/// other layers fight it (<c>Services/Flash/FlashService.cs:3615</c>, <c>:3667-3668</c>,
/// <c>:3862-3868</c>, <c>:206-240</c>). That half is a platform capability with its own evidence
/// (SP-099's <see cref="Overlay.IOverlayPresence"/>), and SP-100 hands the drawn paths to it.</para>
///
/// <para><b>The drawing is downstream, and it never leads.</b> The pacing, the pool, the count and
/// the stop are exactly what they were before a surface existed, and they do not consult one: a
/// flash comes due, draws from the pool, counts, re-schedules and — if there is somewhere to draw —
/// appears. Where there is no overlay (every non-Windows build refuses one honestly, SP-099 D56)
/// the flash still comes due, still counts and still stops; it is a flash nobody sees, which is
/// neither a crash nor a refusal to run. Nothing in this class pretends the difference away: the
/// presenter keeps the surface's typed <c>CapabilityState</c> verbatim.</para>
///
/// <para><b>Threading.</b> The schedule is a one-shot on an injected
/// <see cref="ISessionClock"/>, re-armed at the tail of every firing — WPF's shape
/// (<c>FlashService.cs:566-573</c>), and the reason no test here needs a wall-clock wait. The
/// firing runs on the clock's thread; the UI projection goes through the one dispatch boundary
/// with the liveness check inside the delegate (async-lifecycle-fault-contract §5.3/§5.5). The
/// pending-handle field is manipulated with <see cref="Interlocked"/> and never under
/// <see cref="_gate"/>, so the cancellation callback can run while another thread holds the
/// gate without a lock cycle.</para>
/// </summary>
public sealed class FlashImagesEffect : ISessionEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:484</c>), and the same
    /// key its quick-toggle switches on (<c>MainWindow.Presets.cs:1250</c>).</summary>
    public const string EffectId = "flash";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:484</c>,
    /// confirmed in the live v6.8.1 rack survey — <c>wpf-surface-reachability.md</c> §8.3).</summary>
    public const string DisplayTitle = "Flash Images";

    private readonly AsyncOperationOwner _owner;
    private readonly UiDispatchBoundary _ui;
    private readonly ISessionClock _clock;
    private readonly IFlashImagePool _pool;
    private readonly PersistenceStore<SessionPresetDocument> _preset;
    private readonly IFlashSurface? _surface;
    private readonly Random _random;
    private readonly object _gate = new();

    private IDisposable? _pending;
    private int _generation = -1;
    private bool _armed;
    private int _flashCount;
    private FlashEvent? _last;

    public FlashImagesEffect(
        AsyncOperationOwner owner,
        UiDispatchBoundary ui,
        ISessionClock clock,
        IFlashImagePool pool,
        PersistenceStore<SessionPresetDocument> preset,
        Random? random = null,
        IFlashSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(preset);
        _owner = owner;
        _ui = ui;
        _clock = clock;
        _pool = pool;
        _preset = preset;
        _random = random ?? new Random();
        _surface = surface;
    }

    /// <inheritdoc/>
    public string Id => EffectId;

    /// <inheritdoc/>
    public string Title => DisplayTitle;

    /// <inheritdoc/>
    public bool Enabled => _preset.Current.FlashEnabled;

    /// <inheritdoc/>
    public Task<OperationOutcome>? Completion { get; private set; }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Flashes that have come due since this effect was first armed.</summary>
    public int FlashCount
    {
        get { lock (_gate) { return _flashCount; } }
    }

    /// <summary>The most recent firing, or null if none has happened yet.</summary>
    public FlashEvent? Last
    {
        get { lock (_gate) { return _last; } }
    }

    /// <summary>
    /// True while a next flash is actually on the clock. This — not a stored bool, and not the
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

    /// <inheritdoc/>
    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.FlashEnabled = enabled);
        Changed?.Invoke();
    }

    /// <summary>
    /// Arm the schedule. WPF's <c>FlashService.Start()</c> is idempotent about its own running
    /// flag and schedules the first tick SYNCHRONOUSLY (<c>FlashService.cs:345-352</c>) — kept,
    /// and load-bearing: the caller's next observation of the clock must already see the
    /// pending flash, or a stop/advance ordering becomes a race.
    ///
    /// <para><b>One deliberate divergence.</b> WPF's <c>Start()</c> returns at
    /// <c>if (_isRunning) return;</c> (<c>:347</c>), so a module that was OFF when the engine
    /// started can never be armed by turning it on mid-session: the service is already
    /// "running", nothing schedules, and the quick-toggle silently does nothing
    /// (<c>MainWindow.Presets.cs:1250</c>, <c>Features/FlashFeatureControl.xaml.cs:168-175</c>).
    /// Here the GENERATION is idempotent — arming twice starts no second generation, which is
    /// the re-entrant double-toggle guard — but the SCHEDULE is always re-evaluated, so
    /// switching a module on during a session does what the app's own onboarding text promises
    /// it does.</para>
    /// </summary>
    public void Arm()
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

            Completion = _owner.RunAsync("flash-schedule", ParkUntilCancelledAsync);
        }

        ScheduleNext();
        Changed?.Invoke();
    }

    /// <summary>
    /// Disarm. WPF's <c>FlashService.Stop()</c> clears its flag, cancels its token and stops
    /// the scheduler timer (<c>FlashService.cs:367-380</c>), then drops the cached images
    /// (<c>:378</c>).
    ///
    /// <para>The pending one-shot is disposed HERE, synchronously, rather than being left to
    /// the cancellation callback: after this returns, advancing the clock by any amount must
    /// fire nothing, and "the callback will get there" is not that guarantee.</para>
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
        HideSurfaces();
        if (!wasArmed)
        {
            return;
        }

        _owner.Cancel();
        Changed?.Invoke();
    }

    /// <summary>
    /// Take every surface off the screen. WPF's stop closes every live flash window
    /// (<c>FlashService.cs:3878-3884</c>), and that is the half of stop the user can see: a panic
    /// button that leaves pictures on the screen has not stopped anything.
    ///
    /// <para>It goes through the SAME dispatch boundary as the draw, because a native window
    /// belongs to the thread that made it, and disarm is reached from the UI thread (the button,
    /// the rack toggle) AND from a teardown thread. Skip-until-bound applies: with no UI there is
    /// no surface, because the draw could never have happened either.</para>
    ///
    /// <para><b>Called from <see cref="Disarm"/> and from nowhere else</b>, deliberately: teardown
    /// reaches disarm too (<c>SessionParticipant.StopAsync</c> -> <c>SessionEngine.Stop</c> ->
    /// <c>Disarm</c>), so hiding from the cancellation callback as well would take every surface
    /// down twice per stop. Once is the fact the surface facts assert.</para>
    /// </summary>
    private void HideSurfaces()
    {
        if (_surface is null || !_ui.IsBound)
        {
            return;
        }

        _ui.Post(_surface.HideAll);
    }

    /// <summary>
    /// The owned operation behind an armed schedule. It does no work: it exists so the
    /// schedule is a REGISTERED operation with a generation and a typed terminal outcome
    /// (async-lifecycle-fault-contract §1), so the host's one teardown entry point cancels and
    /// drains it like everything else, and so "the effect is still running" is a question the
    /// registry can answer rather than this class.
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
    /// WPF <c>ScheduleNextFlash</c> (<c>FlashService.cs:538-563</c>): nothing is scheduled while
    /// the service is stopped (<c>:539</c>) or while the module's own dial is off (<c>:541-546</c>)
    /// — in WPF the service still reports itself running in that state, and nothing ever fires.
    /// A previous pending one-shot is dropped first, exactly as WPF stops its timer before
    /// restarting it (<c>:557</c>).
    /// </summary>
    private void ScheduleNext()
    {
        int generation;
        int perHour;
        lock (_gate)
        {
            if (!_armed || !Enabled)
            {
                CancelPendingSchedule();
                return;
            }

            generation = _generation;
            perHour = _preset.Current.FlashesPerHour;
        }

        if (!_owner.IsLive(generation))
        {
            CancelPendingSchedule();
            return;
        }

        var due = FlashSchedule.NextInterval(perHour, _random);
        var handle = _clock.Schedule(due, () => Fire(generation));
        Interlocked.Exchange(ref _pending, handle)?.Dispose();

        // The generation may have been cancelled between the liveness check and the schedule.
        // Re-check and tear the handle straight back down: a stop must never leave a live
        // one-shot behind, and this is the only window in which one could exist.
        if (!_owner.IsLive(generation))
        {
            CancelPendingSchedule();
        }
    }

    /// <summary>
    /// One flash comes due. WPF's tick stops its own timer, fires only if the service is still
    /// running, and re-schedules at the tail (<c>FlashService.cs:566-573</c>).
    /// </summary>
    private void Fire(int generation)
    {
        // The one-shot has fired and is spent; drop the handle before anything else so
        // ScheduleArmed cannot report a timer that no longer exists.
        Interlocked.Exchange(ref _pending, null);

        // Stale-generation check first (contract §5.5): a callback that survives a cancel does
        // nothing and re-schedules nothing.
        if (!_owner.IsLive(generation))
        {
            return;
        }

        int perFlash;
        lock (_gate)
        {
            if (!_armed || _generation != generation || !Enabled)
            {
                return;
            }

            perFlash = _preset.Current.ImagesPerFlash;
        }

        // The draw is outside the gate: the product pool touches a filesystem, and a session
        // must never be able to wedge its own state behind a slow directory.
        var drawn = _pool.Draw(perFlash);

        FlashEvent fired;
        lock (_gate)
        {
            // Re-check after the draw. A stop that landed mid-draw wins: the flash is not
            // counted and nothing is re-scheduled.
            if (!_armed || _generation != generation)
            {
                return;
            }

            _flashCount++;
            fired = new FlashEvent(_flashCount, drawn.Count, _clock.UtcNow);
            _last = fired;
        }

        ScheduleNext();
        // The per-flash signal is Fired, and Fired ALONE, because Fired goes through the
        // dispatch boundary and Changed does not. Raising Changed from this thread would hand
        // a clock-thread callback to whatever UI subscribed to it.
        Project(generation, fired, drawn);
    }

    /// <summary>
    /// Re-pace the pending flash against the current dial — WPF <c>RefreshSchedule</c>
    /// (<c>FlashService.cs:527-531</c>), which its frequency slider calls so a change takes
    /// effect now instead of after the old interval expires
    /// (<c>Features/FlashFeatureControl.xaml.cs:186</c>). A no-op when nothing is armed, which
    /// is WPF's <c>if (!_isRunning) return;</c> (<c>:529</c>).
    /// </summary>
    public void RefreshSchedule()
    {
        ScheduleNext();
        Changed?.Invoke();
    }

    /// <summary>
    /// The UI projection, and — since SP-100 — the DRAW. Skip-until-bound (contract §5.3): the
    /// effect can be armed before the window exists, and a projection is skipped then, never
    /// faulted. The liveness re-check lives INSIDE the posted delegate (§5.5) so a post that lands
    /// during teardown is inert.
    ///
    /// <para><b>Why the drawn paths ride here and not on <see cref="FlashEvent"/>.</b> The event is
    /// content-free by construction — a COUNT, never a file name, which is the media-logging rule
    /// the whole port holds. The surface needs the paths themselves, so they are handed straight to
    /// it, on the one thread that may touch a native window, and they are never carried by anything
    /// a log or a UI can subscribe to.</para>
    ///
    /// <para>The surface is drawn FIRST and <see cref="Fired"/> raised second: the visible half of a
    /// flash is the user's outcome, and it must not be hostage to whatever a UI subscriber does. The
    /// order is a property a fact holds (<c>FlashSurfacePresenterTests</c>), not a comment — swapping
    /// these two lines reds the suite.</para>
    /// </summary>
    private void Project(int generation, FlashEvent fired, IReadOnlyList<string> drawn)
    {
        if (!_ui.IsBound || !_owner.IsLive(generation))
        {
            return;
        }

        _ui.Post(() =>
        {
            if (!_owner.IsLive(generation))
            {
                return;
            }

            _surface?.Show(drawn);
            Fired?.Invoke(fired);
        });
    }

    /// <summary>
    /// Raised on the UI thread, inside the dispatch boundary, once per flash that really came
    /// due. The surface binds this; nothing else may.
    /// </summary>
    public event Action<FlashEvent>? Fired;
}
