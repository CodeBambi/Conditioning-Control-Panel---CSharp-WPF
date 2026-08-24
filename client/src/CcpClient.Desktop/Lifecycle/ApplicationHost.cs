namespace CcpClient.Desktop.Lifecycle;

using CcpClient.Desktop.Capabilities;

/// <summary>
/// The single owner of every background participant (contract §5.2): the only caller of
/// <see cref="IBackgroundParticipant.StartAsync"/> (phase 3) and
/// <see cref="IBackgroundParticipant.StopAsync"/> (teardown, §6).
/// </summary>
public sealed class ApplicationHost
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogSink _log;
    private readonly IReadOnlyList<IBackgroundParticipant> _participants;
    private readonly TimeSpan _drainTimeout;
    private readonly Func<Task>? _preDrainFlush;
    private int _shutdownStarted;

    /// <summary>
    /// Convenience for owner-less test participants; any participant that registers
    /// operations must share the host's registry (use <see cref="CompositionRoot.Build"/>
    /// or the full constructor).
    /// </summary>
    public ApplicationHost(ILogSink log, IReadOnlyList<IBackgroundParticipant> participants, StartupTrace trace)
        : this(log, participants, trace, new OperationRegistry(), new UiDispatchBoundary(), DefaultDrainTimeout)
    {
    }

    public ApplicationHost(
        ILogSink log,
        IReadOnlyList<IBackgroundParticipant> participants,
        StartupTrace trace,
        OperationRegistry registry,
        UiDispatchBoundary uiDispatch,
        TimeSpan? drainTimeout = null,
        Func<Task>? preDrainFlush = null,
        CapabilityRegistry? capabilities = null,
        CapabilityProbeRunner? probeRunner = null,
        Entitlement.HostLoginEntitlement? entitlement = null)
    {
        _log = log;
        _participants = participants;
        Trace = trace;
        Registry = registry;
        UiDispatch = uiDispatch;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        _preDrainFlush = preDrainFlush;
        Capabilities = capabilities;
        ProbeRunner = probeRunner;
        Entitlement = entitlement;
    }

    /// <summary>
    /// The entitlement capability, composed once by <see cref="CompositionRoot.Build"/>
    /// so the object the DTRH gate consults is the SAME object whose probe the System page
    /// reports. Null only in owner-less test hosts built through the convenience constructor;
    /// a UI surface that needs it says so loudly rather than degrading into an ungated door.
    /// </summary>
    public Entitlement.HostLoginEntitlement? Entitlement { get; }

    /// <summary>Capability states for the window's user-visible surface (capability contract §9). Null only in owner-less test hosts.</summary>
    public CapabilityRegistry? Capabilities { get; }

    /// <summary>Runs the CapabilityProbes phase body (capability contract §3 rule 2). Null only in owner-less test hosts.</summary>
    public CapabilityProbeRunner? ProbeRunner { get; }

    public IReadOnlyList<IBackgroundParticipant> Participants => _participants;

    /// <summary>The registry owning every participant's async operations (async contract §1).</summary>
    public OperationRegistry Registry { get; }

    /// <summary>The late-bound UI dispatch boundary (async contract §5).</summary>
    public UiDispatchBoundary UiDispatch { get; }

    /// <summary>Phase 4 binding of the real dispatch boundary (async contract §5.2). Never SynchronizationContext capture.</summary>
    public void BindUiDispatch(IUiDispatch dispatch) => UiDispatch.Bind(dispatch);

    /// <summary>Phase-outcome trace displayed by the placeholder window (contract §10.1).</summary>
    public StartupTrace Trace { get; }

    public bool IsShutdown => Volatile.Read(ref _shutdownStarted) != 0;

    /// <summary>
    /// Content-free diagnostic surface for headed verification harnesses (layout
    /// probe). Never carries user content or secrets.
    /// </summary>
    public void LogDiagnostic(string message) => _log.Log(message);

    /// <summary>
    /// Phase 3 body: explicitly start each registered participant in registration order.
    /// A start failure is a typed Fatal outcome, not an escaped exception.
    /// </summary>
    public async Task<StartupOutcome> StartParticipantsAsync(CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return StartupOutcome.Cancelled.Instance;
            }

            try
            {
                await participant.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StartupOutcome.Cancelled.Instance;
            }
            catch (Exception ex)
            {
                return new StartupOutcome.Failed(new InitFailure(
                    "CoreServices", InitFailureKind.Fatal, $"Participant '{participant.Name}' failed to start: {ex.Message}", ex));
            }
        }

        return StartupOutcome.Success.Instance;
    }

    /// <summary>
    /// The single idempotent teardown entry point (contract §6; async contract §6).
    /// Runs exactly once per process; every later invocation from any path is a no-op.
    /// Order: cancel every generation and drain every owned completion (bounded wait is a
    /// backstop only; cancellation terminates well-behaved operations), then stop
    /// participants in reverse start order. Never throws: unobserved operations are
    /// recorded in registry state, and one participant's stop failure is logged while
    /// teardown continues to the rest. The settings flush occupies the head slot the lifecycle
    /// contract reserved (persistence contract §11). There is no second teardown path for async work.
    ///
    /// <para><b>Every wait in here is bounded, and the last one to become so was the participant
    /// stop.</b> This method runs on the UI thread with that thread BLOCKED (<c>App.axaml.cs:95</c>
    /// calls it from the lifetime's Exit handler through <c>GetAwaiter().GetResult()</c>), so a
    /// teardown that never returns is an application that never exits — and on the ordinary path
    /// the native surfaces are destroyed by the OPERATING SYSTEM at process exit and by nothing
    /// else (<c>Session/SessionParticipant.cs:889-900</c>). A process that cannot end therefore
    /// leaves a topmost, input-blocking window on the user's desktop with nothing to close it.
    /// See the loop below for the bound, its backstop, and the wedge shape it cannot cover.</para>
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        // Persistence contract §11: the settings flush occupies the reserved head
        // slot — it completes BEFORE generations are cancelled/drained and before reverse-order
        // participant stop. Attempted on every path including panic (contract §11 rule 4);
        // bounded internally; guarded here too so teardown never throws (a teardown invariant).
        if (_preDrainFlush is not null)
        {
            try
            {
                await _preDrainFlush().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Log($"teardown: settings flush failed: {ex.Message}");
            }
        }

        // Async contract §6 steps 1-3: cancel generations, await owned completions,
        // record unobserved (never throw).
        await Registry.CancelAndDrainAsync(_log, _drainTimeout).ConfigureAwait(false);

        for (var i = _participants.Count - 1; i >= 0; i--)
        {
            try
            {
                var stop = _participants[i].StopAsync();

                // THE BOUND, AND WHY IT IS A SAFETY FIX RATHER THAN TIDINESS. Every native surface
                // this app puts on the desktop is destroyed by the OPERATING SYSTEM at process exit
                // and by nothing else on the ordinary path: the disposals go through the UI dispatch
                // boundary (Session/SessionParticipant.cs:889-900, :1040-1057) and the UI thread is
                // blocked inside this very call for the whole of teardown (App.axaml.cs:95), so no
                // post is ever delivered. That makes a surface's lifetime the PROCESS's — and this
                // loop was the one place left that could stop the process from ending. A stop that
                // never completes parks the UI thread inside the lifetime's Exit handler forever, so
                // the app never exits, and two of the six surfaces are deliberately NOT
                // click-through (Pointer/Win32PointerSurface.cs:850-852,
                // Input/Win32InputPresence.cs:1097-1099): the user is left with a topmost window
                // eating their clicks or their keyboard and nothing on screen to close it.
                //
                // So the observation is bounded with the SAME backstop the registry drain already
                // uses two lines above, for the same reason and with no new knob. The stop is not
                // cancelled or interfered with — it keeps running on its own thread and lands if it
                // ever can; only this teardown's WAIT on it ends. That is the shape
                // Audio/SoundArbitration.cs:1262-1270 already chose at the one native wedge this
                // port has measured ("a wedged native call never blocks process exit").
                //
                // NAMED LIMIT: this bounds an ASYNCHRONOUS wedge only. A StopAsync that blocks its
                // caller before returning a task (a hung native call on this very thread) never
                // reaches this line, and no bound taken on the blocked thread could help it. The
                // remedy there is termination, not patience.
                if (await Task.WhenAny(stop, Task.Delay(_drainTimeout)).ConfigureAwait(false) != stop)
                {
                    _log.Log($"teardown: participant '{_participants[i].Name}' stop exceeded " +
                        $"{_drainTimeout.TotalSeconds:0.#}s and was abandoned — teardown continues so the process " +
                        "can exit and the OS can reclaim any surface still up");
                    RecordItIfItEverFails(_participants[i].Name, stop);
                    continue;
                }

                await stop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Log($"teardown: participant '{_participants[i].Name}' stop failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// An abandoned stop is no longer awaited, so a failure it reaches later would reach nobody:
    /// unobserved by any caller (measured — <c>Task.WhenAny</c> does NOT observe the exception of
    /// the task that lost its race), and invisible in the log, which is the worse half. A
    /// participant that eventually failed to shut its device or its file down would leave no trace
    /// of it anywhere. So the abandonment is followed to its end and RECORDED, in the same log that
    /// already carries the abandonment itself — the shape
    /// <see cref="OperationRegistry.CancelAndDrainAsync"/> uses for the operations it gives up on.
    /// Reading the exception is also what retires it, so it can never resurface as an
    /// unobserved-task exception on the finalizer thread.
    /// </summary>
    private void RecordItIfItEverFails(string participant, Task abandoned) =>
        _ = abandoned.ContinueWith(
            failed => _log.Log(
                $"teardown: the abandoned stop of participant '{participant}' failed after teardown had moved on: "
                + failed.Exception!.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
