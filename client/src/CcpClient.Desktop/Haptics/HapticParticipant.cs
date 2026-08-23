using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The haptic sink's APP-LIFETIME owner.
///
/// <para><b>Why this is a participant and not part of the session.</b> Upstream's
/// haptic service is a static on the application, constructed at startup
/// (<c>ConditioningControlPanel/App.xaml.cs:533</c>, <c>:2060</c>), auto-connected there
/// (<c>:2103-2105</c>), all-stopped at exit (<c>:4406</c>) and disposed last (<c>:4524</c>) — and
/// <c>grep App.Haptics MainWindow/MainWindow.StartStop.cs</c> returns <b>zero hits</b>: the engine
/// never starts it and never stops it. It outlives every session. So it is owned where the app is
/// owned.</para>
///
/// <para><b>No second lifetime model</b> (the scheduler's rule, applied to a sink instead of a driver).
/// This is the port's existing <see cref="IBackgroundParticipant"/>, registered LAST in
/// <c>CompositionRoot.DefaultParticipants</c>, which buys four properties from machinery that
/// already exists: phase 3 starts it after every store it might read; teardown stops it FIRST,
/// because participant stop is reverse order, so the sink and its limb are released before the
/// session that drives them (and the guarantee that nothing is left running does NOT rest
/// on that order, because <see cref="ShutdownStopAsync"/> has already all-stopped in the pre-drain
/// head slot below); its document flushes in the reserved pre-drain slot; and — the one that matters
/// most here — <see cref="ShutdownStopAsync"/> occupies the HEAD of that same pre-drain slot, which
/// is the port's analogue of upstream's own comment: <i>"Haptics FIRST and synchronously (bounded
/// ~2s): a Lovense level has no server-side watchdog, so a toy we don't countermand keeps running
/// after the app is gone. This cannot be left to Haptics.Dispose() further down"</i>
/// (<c>App.xaml.cs:4401-4407</c>).</para>
///
/// <para><b>Construction starts nothing</b> (the lifecycle contract §4.4), and start connects to nothing:
/// see <see cref="StartAsync"/>.</para>
/// </summary>
public sealed class HapticParticipant : IBackgroundParticipant
{
    private readonly ILogSink _log;
    private readonly PersistenceStore<HapticSettingsDocument> _store;
    private readonly Func<CancellationToken, Task<EntitlementOutcome>>? _resolveEntitlement;
    private readonly Func<long>? _sequence;

    private bool _running;
    private int _shutdownStopped;

    /// <param name="infra">The participant infrastructure: the operation owner and the log.</param>
    /// <param name="dataDirectory">Where the one setting lives, beside every other document.</param>
    /// <param name="sink">
    /// The sink this build owns. Defaults to the composite over the routes the user has ticked
    /// (<see cref="HapticSinkFactory.Create"/>), which refuses before touching a wire while that set
    /// is empty — and it is empty on a fresh install, because both route flags default false.
    /// A test substitutes a recording sink so the ownership, ordering and gate facts are about real
    /// calls; <b>nothing may inject a sink that claims Available on a product path</b>, which is the
    /// fake-available shape the capability contract bans (the same wording
    /// <c>CompositionRoot.EntitlementFactory</c> carries).
    /// </param>
    /// <param name="resolveEntitlement">
    /// The entitlement answer, asked ONCE at phase 3. Null means no authority was supplied, and the
    /// honest consequence is <c>Unavailable(tier-authority-absent)</c> — never an open gate.
    /// </param>
    /// <param name="sequence">
    /// A monotonic tick a test reads to prove ORDER — that the all-stop really ran before the things
    /// it must precede. Never used on a product path.
    /// </param>
    /// <param name="clock">
    /// The LIMB's timing seam. It belongs to the app-scoped haptic owner rather than to a
    /// session, which is upstream's own arrangement — the mixer's output loop is the haptic
    /// service's and is independent of every module's timer
    /// (<c>Services/Haptics/Core/HapticMixer.cs:224-247</c>). Null takes the real one.
    /// </param>
    public HapticParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        IHapticSink? sink = null,
        Func<CancellationToken, Task<EntitlementOutcome>>? resolveEntitlement = null,
        Func<long>? sequence = null,
        ISessionClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        _log = infra.Log;
        _resolveEntitlement = resolveEntitlement;
        _sequence = sequence;
        _store = new PersistenceStore<HapticSettingsDocument>(
            infra.OwnerFor("HapticSettings"), infra.Log,
            Path.Combine(dataDirectory, HapticSettingsDocument.FileName),
            HapticSettingsDocument.CurrentSchemaVersion);

        // The store is built FIRST so the sink can read the user's per-route flags off it. The sink
        // holds a DELEGATE rather than a snapshot for two reasons: this runs before phase 3 has
        // loaded the file, and the flags are a live setting — upstream re-reads them at every connect
        // (Services/Haptics/Core/HapticDeviceManager.cs:102, :91-98) rather than capturing a set at
        // construction.
        Sink = sink ?? HapticSinkFactory.Create(() => _store.Current.EnabledRoutes());

        // The gate is the master toggle AND the entitlement decision, asked HERE and never
        // at a module, which is upstream's central refusal (HapticMixer.cs:191-204, :843) and the
        // reason D204's activity readout keeps firing for a user who can feel nothing. The device
        // roster is whatever the LAST observation named — null until a route the user ticked has
        // been asked and a server has answered, so until then the limb evaluates and has nothing to
        // address.
        Limb = new HapticLimb(
            Sink,
            clock ?? new SystemSessionClock(ex => infra.Log.Log(
                "haptic-limb: a scheduled evaluation faulted and was contained — "
                + $"{ex.GetType().Name}: {ex.Message}")),
            () => OutputAllowed && Enabled,
            () => LastObservation?.DeviceKeys ?? [],
            infra.Log.Log);
    }

    /// <inheritdoc/>
    public string Name => "Haptics";

    /// <inheritdoc/>
    public bool Running => _running;

    /// <summary>The one sink. The panel reads its last outcome; nothing else may touch it.</summary>
    public IHapticSink Sink { get; }

    /// <summary>
    /// The one limb, and the only thing that drives <see cref="Sink"/>.
    ///
    /// <para>It is owned HERE, beside the sink, for the reason this whole participant exists:
    /// upstream's mixer is a member of the app-scoped haptic service, constructed at startup and
    /// never engine-started, so it outlives every session and every module that commands it. The
    /// composition root hands this object to the session, which threads it to the five statements
    /// the census maps (<c>client/docs/haptic-limb-census.md</c> §3.1).</para>
    /// </summary>
    public HapticLimb Limb { get; }

    /// <summary>The persisted setting (public so the panel can save the box it was allowed to tick,
    /// the shape every module's store has).</summary>
    public PersistenceStore<HapticSettingsDocument> Preset => _store;

    /// <summary>The master toggle as it stands on disk.</summary>
    public bool Enabled => _store.Current.Enabled;

    /// <summary>
    /// The entitlement decision this run is operating under. Before <see cref="StartAsync"/> it is
    /// the honest "nothing has been asked" refusal rather than a permissive default: a gate that
    /// opened while it was still uninitialised would open exactly once per launch, in the window
    /// before anyone could see it close.
    /// </summary>
    public HapticGateDecision Gate { get; private set; } =
        new HapticGateDecision.RefusedUnverified(
            EntitlementReasonCodes.TierAuthorityAbsent,
            HapticGate.UnverifiedMessage(EntitlementReasonCodes.TierAuthorityAbsent));

    /// <summary>Whether the gate currently allows output at all.</summary>
    public bool OutputAllowed => Gate is HapticGateDecision.Allow;

    /// <summary>
    /// Whether an entitlement authority was supplied at all.
    ///
    /// <para>It exists so the composition can be ASSERTED rather than assumed. Both a missing
    /// authority and this build's unconfigured one refuse every user with the same outcome class,
    /// so without this the wiring that hands the participant the host's real entitlement capability
    /// would be invisible to every fact — and a root that quietly stopped passing it would look
    /// identical from outside on the day an authority is finally configured.</para>
    /// </summary>
    public bool HasEntitlementAuthority => _resolveEntitlement is not null;

    /// <summary>
    /// How many times this participant asked a sink to connect. <b>Zero on a fresh install</b>, and
    /// it is evidence rather than a counter: the master toggle is off and no route is ticked, and a
    /// product that knocked on <c>ws://127.0.0.1:12345</c> or <c>http://127.0.0.1:20010</c> for a
    /// feature nobody switched on would be making a network connection for no reason a user could
    /// benefit from.
    /// </summary>
    public int ConnectAttempts { get; private set; }

    /// <summary>The typed outcome of the last connect attempt, or null when none was made.</summary>
    public CapabilityState? LastConnectOutcome { get; private set; }

    /// <summary>The last thing the server said, or null when nothing was ever asked.</summary>
    public HapticServerObservation? LastObservation { get; private set; }

    /// <summary>
    /// The typed capability state behind this sink, right now.
    ///
    /// <para><b>It is the SAME expression the registered capability probe evaluates</b>
    /// (<c>Lifecycle/CompositionRoot.HapticCapabilityName</c>), so the panel and the System page
    /// cannot tell two different stories about one sink. With nothing ever asked it classifies
    /// <see cref="HapticServerObservation.NotAsked"/>, which earns
    /// <c>Unavailable(not-probed)</c> — a run that never asked reports that it never asked, and never
    /// a missing device.</para>
    /// </summary>
    public CapabilityState SinkState => (LastObservation ?? HapticServerObservation.NotAsked).Classify();

    /// <summary>
    /// The registered capability probe, in one place, <b>gated so it cannot contact a haptic server
    /// for a feature the user has not switched on.</b>
    ///
    /// <para><b>The gate is upstream's own auto-connect conjunction</b>, field for field:
    /// <c>Settings.Current.Haptics.AutoConnect &amp;&amp; HasRealHapticProviderEnabled()</c>
    /// (<c>App.xaml.cs:2176</c>), whose predicate is <c>lovense.Enabled || buttplug.Enabled</c>
    /// (<c>:3580-3589</c>). Here the first conjunct is <see cref="Enabled"/> and the second is
    /// <see cref="IHapticSink.Route"/> being anything but <see cref="HapticProviderRoute.None"/>,
    /// which for the composite sink IS "the user ticked at least one route".</para>
    ///
    /// <para><b>Why it is a method on the participant and not a lambda in the composition root.</b>
    /// <c>CapabilityRegistry.RunAllAsync</c> probes every registered capability at launch. An ungated
    /// registration therefore fired an HTTP GET at <c>http://127.0.0.1:20010/GetToys</c> — and would
    /// open a WebSocket to <c>ws://127.0.0.1:12345</c> — on EVERY launch of a default install, for a
    /// feature nobody had switched on. That is an unrequested outbound connection attempt, and it is
    /// the kind of thing that must have exactly one implementation that a fact can call.</para>
    ///
    /// <para><b>What it answers when it refuses.</b>
    /// <see cref="HapticServerObservation.NotAsked"/>, which classifies
    /// <c>Unavailable(not-probed)</c> — an EXISTING arm rather than a state invented for this
    /// branch, and the true one: a client IS admitted and nobody has asked it anything. It must never
    /// be a server-unreachable answer, which would be a claim about a socket this method
    /// deliberately did not open.</para>
    /// </summary>
    public async Task<CapabilityState> ProbeSinkAsync(CancellationToken cancellationToken)
    {
        if (!Enabled || Sink.Route == HapticProviderRoute.None)
        {
            return HapticServerObservation.NotAsked.Classify();
        }

        return (await Sink.ObserveAsync(cancellationToken).ConfigureAwait(false)).Classify();
    }

    /// <summary>How many times the all-stop really ran. One per teardown on every path.</summary>
    public int AllStops { get; private set; }

    /// <summary>The sequence tick at which the all-stop ran, or -1. Order evidence only.</summary>
    public long AllStopSequence { get; private set; } = -1;

    /// <summary>
    /// The row's dot, and it has only TWO reachable values.
    ///
    /// <list type="bullet">
    /// <item><b>Off</b> — the enable is off, OR nothing in this build can reach a device.</item>
    /// <item><b>Armed</b> — the enable is on and a device is really reachable.</item>
    /// <item><b>Live</b> — <b>NOT A STATE THIS ROW HAS.</b> Live would have to mean "something is
    /// being SENT", and this dot reports REACH. That is a decision about the dot's vocabulary and not
    /// a report on what this build can do: five statements in the effect spine command
    /// <see cref="Limb"/> at the right moments with upstream's own envelopes, both provider routes
    /// have a client (<see cref="HapticSinkFactory.AdmittedRoutes"/>), and a user who ticks a route
    /// and runs its server gets a confirmed <see cref="LastObservation"/> and real
    /// <see cref="HapticLimb.Sends"/>. <b>A limb does not light this dot and must not</b>: both
    /// conjuncts below are about what a SERVER said, and a limb touches neither.</item>
    /// </list>
    ///
    /// <para>Upstream passes a dot predicate for this row —
    /// <c>() =&gt; App.Settings?.Current?.Haptics?.Enabled</c>
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:520</c>) — so the row HAS a dot, unlike Visuals'
    /// <c>null</c> at <c>:496</c>. It is earned rather than read off the checkbox: the second
    /// conjunct asks the sink what the server last said, which is the fifth dot meaning (reach)
    /// applied to a resource in another process.</para>
    /// </summary>
    public EffectDotState Dot =>
        Enabled && LastObservation is { Confirmed: true } ? EffectDotState.Armed : EffectDotState.Off;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>Phase 3 does three things and deliberately not a fourth.</para>
    /// <list type="number">
    /// <item>Load the one setting.</item>
    /// <item>Ask the entitlement authority ONCE and hold the decision. Upstream re-reads a cached
    /// Patreon property on every 10 Hz mixer tick (<c>HapticMixer.cs:200</c>); a DPAPI read plus an
    /// authority call is not a 10 Hz operation, so the port asks at start. The divergence — a pledge
    /// that lapses mid-run is noticed at the next launch — is recorded rather than hidden.</item>
    /// <item>Connect, <b>if and only if the master toggle is on AND the user has ticked at least one
    /// provider route.</b> That conjunction is upstream's own auto-connect guard, field for field:
    /// <c>Settings.Current.Haptics.AutoConnect &amp;&amp; HasRealHapticProviderEnabled()</c>
    /// (<c>App.xaml.cs:2176</c>), where the predicate is <c>lovense.Enabled || buttplug.Enabled</c>
    /// (<c>:3580-3589</c>), because auto-connecting a provider nobody really has <i>"would silently
    /// bring up three virtual toys and a stream of pink toasts at each launch"</i>
    /// (<c>:2173-2174</c>). On a fresh install both conjuncts are false — the master toggle is off
    /// and both route flags default false — so <see cref="ConnectAttempts"/> stays zero.</item>
    /// </list>
    /// <para>The fourth thing it does not do is open a socket to find out ANYWAY. A probe of
    /// <c>12345</c> or <c>20010</c> behind a switched-off feature would report whether some other
    /// program is listening, which is not a fact about anything the user asked for — and it would be
    /// an unrequested outbound connection attempt at every launch. The registered capability probe
    /// carries the same guard for the same reason
    /// (<c>Lifecycle/CompositionRoot.HapticCapabilityName</c>).</para>
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_running)
        {
            return;
        }

        await _store.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_store.LastLoadOutcome is { IsDegraded: true } outcome)
        {
            // Typed Degraded, never silent. The default this falls back to is Enabled = false, which
            // is the safe direction: a quarantined file cannot switch haptics ON behind the gate.
            _log.Log($"haptic-settings: load → {outcome.GetType().Name} (typed Degraded — "
                + "haptics will run on the default setting, which is DISABLED)");
        }

        _running = true;

        await ApplyEntitlementAsync(cancellationToken).ConfigureAwait(false);

        // THE USER'S SETTING GATES THE CONNECT, and this is upstream's guard rather than an
        // optimisation. Upstream auto-connects only on
        // `Settings.Current.Haptics.AutoConnect && HasRealHapticProviderEnabled()`
        // (App.xaml.cs:2176, predicate at :3580-3589) because auto-connecting a provider nobody
        // really has "would silently bring up three virtual toys and a stream of pink toasts at each
        // launch" (:2173-2174).
        //
        // While no route was admitted this was invisible: Route was always None, so the branch below
        // returned first and no launch ever reached a socket. Admitting a route made the omission
        // real — every launch would knock on a Lovense server, including for the majority of users
        // who own no toy and have haptics switched off. That is a behaviour this port must not have,
        // and it is louder than a slow start: it is an unrequested outbound connection attempt.
        if (!Enabled)
        {
            _log.Log("haptics: disabled in settings — no provider was contacted");
            return;
        }

        if (Sink.Route == HapticProviderRoute.None)
        {
            // NO PROVIDER ROUTE IS ENABLED, logged as a classification exactly once per launch. Not a
            // warning: nothing failed, and nothing was contacted. This is where "refuse before
            // touching the wire" happens on the product path, which is the order upstream keeps —
            // its connect button refuses before the manager is called at all
            // (MainWindow/MainWindow.Haptics.cs:653-660) and the manager refuses again before
            // connecting anything (Services/Haptics/Core/HapticDeviceManager.cs:103-107).
            _log.Log("haptics: no provider route is enabled — nothing was contacted "
                + "(this is not \"no device found\")");
            return;
        }

        await ConnectAndRecordAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Ask the authority and apply the answer. Separated from <see cref="StartAsync"/> because the
    /// TRANSITION is the behaviour worth having: upstream's mixer stops the toys once when its gate
    /// goes open → closed (<c>HapticMixer.cs:253-262</c>), and that is the difference between a paid
    /// feature that ends when the pledge does and one that keeps running.
    /// </summary>
    public async Task ApplyEntitlementAsync(CancellationToken cancellationToken)
    {
        EntitlementOutcome outcome;
        if (_resolveEntitlement is null)
        {
            outcome = new EntitlementOutcome.Unavailable(new EntitlementReason(
                EntitlementReasonCodes.TierAuthorityAbsent,
                "no entitlement authority was supplied to the haptic capability in this build"));
        }
        else
        {
            try
            {
                outcome = await _resolveEntitlement(cancellationToken).ConfigureAwait(false)
                          ?? new EntitlementOutcome.Unavailable(new EntitlementReason(
                              EntitlementReasonCodes.TierAuthorityFault,
                              "the entitlement capability returned nothing"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Type name only: an authority's exception message can carry a URL, a header, or the
                // bearer itself, and this string is destined for a log (the entitlement-logging discipline).
                outcome = new EntitlementOutcome.Unavailable(new EntitlementReason(
                    EntitlementReasonCodes.TierAuthorityFault,
                    "the entitlement lookup failed: " + ex.GetType().Name));
            }
        }

        await ApplyGateAsync(HapticGate.Decide(outcome)).ConfigureAwait(false);
        _log.Log("haptics: entitlement → " + outcome.Describe());
    }

    /// <summary>
    /// Apply a decision, and stop everything if the gate just CLOSED. Public so the transition is
    /// drivable by a fact without a fake authority: the decision is the input to the behaviour, and
    /// the behaviour is what matters.
    /// </summary>
    public async Task ApplyGateAsync(HapticGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var wasOpen = OutputAllowed;
        Gate = decision;
        if (wasOpen && !OutputAllowed)
        {
            // Upstream's open → closed arm, verbatim in intent: drop everything and stop the toys
            // ONCE (HapticMixer.cs:253-262). A level already held on a device is the one piece of
            // this port's state that outlives the process, so it is the one that must not be left.
            await RunAllStopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The user asked for the master toggle to be in <paramref name="wanted"/>, and this is the
    /// gate's answer.
    ///
    /// <para><b>The ORDER is upstream's and it is load-bearing.</b>
    /// <c>MainWindow/MainWindow.Haptics.cs</c> tests the gate at <c>:489</c>, reverts the box at
    /// <c>:491</c>, tells the user at <c>:492-496</c> and <c>returns</c> at <c>:497</c> — so it
    /// reaches <c>HapticCfg.Enabled = isEnabled</c> at <c>:500</c> <b>only</b> when the gate allowed.
    /// A refused tick therefore writes NOTHING. Reversing those two statements would leave the box
    /// visually off and the setting on, which is the state that survives a restart.</para>
    ///
    /// <para>Switching OFF is never gated — upstream's condition is <c>isEnabled &amp;&amp; …</c>
    /// (<c>:489</c>), so a user whose pledge lapsed can still turn a feature off. Refusing that would
    /// trap somebody with a running toy.</para>
    /// </summary>
    /// <returns>The decision. <see cref="HapticGateDecision.Allow"/> means the write happened.</returns>
    public HapticGateDecision RequestEnable(bool wanted)
    {
        if (!wanted)
        {
            _store.Mutate(document => document.Enabled = false);
            return Gate;
        }

        if (Gate is not HapticGateDecision.Allow)
        {
            return Gate;
        }

        _store.Mutate(document => document.Enabled = true);

        // SWITCHING IT ON IS THE OTHER MOMENT A CONNECT IS OWED. Start only connects when the
        // setting is ALREADY on, which is upstream's auto-connect guard; without this, a user who
        // enables haptics mid-session would sit at an Off dot until they relaunched the app, because
        // nothing would ever ask a server whether a toy is there.
        //
        // Fire-and-forget with the outcome RECORDED, never awaited: this returns a gate decision to
        // a UI caller on its own thread, and a socket round trip does not belong on that thread. The
        // dot arms when the answer lands, which is the same way it arms at start.
        if (_running && Sink.Route != HapticProviderRoute.None)
        {
            PendingConnect = ConnectAndRecordAsync();
        }

        return Gate;
    }

    /// <summary>
    /// The user ticked or un-ticked ONE provider route.
    ///
    /// <para><b>Not gated, and that is upstream's shape.</b> Upstream's per-provider boxes write and
    /// save with no premium check at all (<c>MainWindow/MainWindow.Haptics.cs:580-595</c>); the gate
    /// sits on the master toggle (<c>:552-564</c>) and on the connect button (<c>:633-641</c>). A
    /// route flag on its own reaches nothing: the participant connects only when
    /// <see cref="Enabled"/> is also on, and that is what <see cref="HapticGate"/> guards.</para>
    ///
    /// <para><b>Ticking a route while haptics is already ON is the other moment a connect is owed</b>,
    /// for the same reason <see cref="RequestEnable"/> states: without it a user who enables their
    /// provider mid-session sits at an Off dot until they relaunch, because nothing would ever ask a
    /// server whether a toy is there. Fire-and-forget with the outcome recorded, never awaited — this
    /// returns to a UI caller on that caller's thread.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The route has no flag on this document. Louder
    /// than a silent no-op: a control bound to a route nothing persists would look like a working
    /// checkbox that forgets.</exception>
    public void SetRouteEnabled(HapticProviderRoute route, bool enabled)
    {
        if (route is not (HapticProviderRoute.Lovense or HapticProviderRoute.Buttplug))
        {
            throw new ArgumentOutOfRangeException(
                nameof(route), route, "no haptic settings flag exists for that route");
        }

        _store.Mutate(document =>
        {
            if (route == HapticProviderRoute.Lovense)
            {
                document.LovenseEnabled = enabled;
            }
            else
            {
                document.ButtplugEnabled = enabled;
            }
        });

        if (enabled && _running && Enabled && Sink.Route != HapticProviderRoute.None)
        {
            PendingConnect = ConnectAndRecordAsync();
        }
    }

    /// <summary>
    /// The in-flight connect started by <see cref="RequestEnable"/>, or null when none was started.
    ///
    /// <para>Exposed because the enable path cannot await its own socket — it returns a gate decision
    /// to a UI caller on that caller's thread. A test needs a DETERMINISTIC signal that the answer
    /// landed rather than a wall-clock wait for it, and this is that signal. It never faults: the
    /// helper records failures instead of throwing.</para>
    /// </summary>
    public Task? PendingConnect { get; private set; }

    /// <summary>The connect half of phase three, reusable by the enable path. Never throws into a
    /// caller: a provider that refuses is a recorded outcome, not an exception on a UI thread.</summary>
    private async Task ConnectAndRecordAsync()
    {
        try
        {
            ConnectAttempts++;
            LastConnectOutcome = await Sink.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            LastObservation = await Sink.ObserveAsync(CancellationToken.None).ConfigureAwait(false);
            _log.Log($"haptics: connect → {LastConnectOutcome.GetType().Name}");
        }
        catch (Exception ex)
        {
            _log.Log($"haptics: connect threw {ex.GetType().Name} — recorded, never rethrown to a caller");
        }
    }

    /// <summary>
    /// The teardown all-stop, and it runs in the RESERVED PRE-DRAIN HEAD SLOT rather than in
    /// <see cref="StopAsync"/>.
    ///
    /// <para>That placement is upstream's, and upstream states the reason at the call site: the stop
    /// <i>"cannot be left to Haptics.Dispose() further down (the providers get torn down in the same
    /// breath) nor to the ProcessExit watchdog — OnExit ends in TerminateProcess, which skips
    /// ProcessExit handlers by design"</i> (<c>App.xaml.cs:4401-4407</c>). The port's pre-drain slot
    /// is the one place with the same property: it completes before generations are cancelled and
    /// before any participant stops (<c>Lifecycle/ApplicationHost.ShutdownAsync</c>).</para>
    ///
    /// <para>One-shot, like upstream's own latch (<c>HapticMixer.cs:172-174,1122</c>): the head slot
    /// calls it and <see cref="StopAsync"/> would call it again, and a stop that burned its budget
    /// twice was a real defect upstream fixed rather than a hypothetical.</para>
    ///
    /// <para><b>The latch is never reset, and that is a decision.</b> A participant's life is the
    /// process's (the lifecycle contract §5): nothing in this port stops one and starts it again, and upstream's
    /// equivalent latch is process-lifetime too. If a restartable participant ever exists, this must
    /// be cleared in <see cref="StartAsync"/> — a stop that has already been "spent" would then
    /// silently decline to countermand a device on the second run, which is the one failure in this
    /// class a person feels.</para>
    /// </summary>
    public Task ShutdownStopAsync() =>
        Interlocked.Exchange(ref _shutdownStopped, 1) != 0 ? Task.CompletedTask : RunAllStopAsync();

    /// <inheritdoc/>
    /// <remarks>
    /// Reverse-order participant stop. The all-stop has normally already run in the pre-drain slot;
    /// this is the path for a host built without one (an owner-less test host, or a participants
    /// factory with no store), and the latch makes the two safe together.
    /// </remarks>
    public async Task StopAsync()
    {
        // ORDER FIRST, because getting it wrong here is upstream's own fixed defect: the all-stop
        // must REACH the sink BEFORE the sink is torn down (App.xaml.cs:4401-4407, and
        // HapticService.cs:957-962, where Dispose all-stops at :961 and only then disposes the mixer
        // at :962). Latched, so the pre-drain head slot having already run makes
        // this free.
        await ShutdownStopAsync().ConfigureAwait(false);

        // Then release the sink WHATEVER happened, and the early return is deliberately below this
        // line. A participant that never started still owns a sink: this one is registered LAST, so
        // any earlier participant's phase-3 failure leaves it constructed and un-started while
        // teardown runs (StartParticipantsAsync returns Failed and ShutdownAsync still stops
        // everyone). It costs nothing while the user has ticked no route, because the composite
        // builds a route's client lazily and has none to release — but the moment one is ticked the
        // sink holds a WebSocket or an HttpClient, and leaking one on the failure path is how a
        // process keeps a connection open after it has given up.
        //
        // The LIMB is released BEFORE the sink, and the order is the same one this method
        // has always been about. The limb holds scheduled one-shot wakes; a wake that fired after
        // the sink went away would be a level-set into a disposed object on a pool thread, which is
        // exactly the race upstream lost when its all-stop was only ever scheduled
        // (HapticService.cs:957-962).
        Limb.Dispose();
        Sink.Dispose();

        if (!_running)
        {
            return;
        }

        _running = false;
        await _store.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Teardown flush for the reserved pre-drain slot (persistence contract §11).</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    private async Task RunAllStopAsync()
    {
        AllStops++;
        AllStopSequence = _sequence?.Invoke() ?? -1;
        // The limb's own state goes FIRST and without touching the sink, which is upstream's
        // gate-close arm exactly — drop every transient and zero every layer (HapticMixer.cs:264,
        // ClearAll at :1044-1049), THEN stop the devices once (:265). A limb that all-stopped for
        // itself would spend the stop budget twice on the teardown path, which is the defect
        // upstream's one-shot latch fixed (:172-174).
        Limb.Clear();
        var state = await Sink.StopAllAsync().ConfigureAwait(false);
        // Logged as a classification, not as a failure: on this build the all-stop refuses because
        // nothing was ever driven, and a line that read like an error every run would train a reader
        // to ignore the one run where it is not.
        _log.Log($"haptics: all-stop → {state.GetType().Name}");
    }
}
