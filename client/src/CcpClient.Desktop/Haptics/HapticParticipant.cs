using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The haptic sink's APP-LIFETIME owner.
///
/// <para><b>Why this is a participant and not part of the session (SP-117 §1).</b> Upstream's
/// haptic service is a static on the application, constructed at startup
/// (<c>ConditioningControlPanel/App.xaml.cs:533</c>, <c>:2060</c>), auto-connected there
/// (<c>:2103-2105</c>), all-stopped at exit (<c>:4406</c>) and disposed last (<c>:4524</c>) — and
/// <c>grep App.Haptics MainWindow/MainWindow.StartStop.cs</c> returns <b>zero hits</b>: the engine
/// never starts it and never stops it. It outlives every session. So it is owned where the app is
/// owned.</para>
///
/// <para><b>No second lifetime model</b> (SP-118 §3's rule, applied to a sink instead of a driver).
/// This is the port's existing <see cref="IBackgroundParticipant"/>, registered LAST in
/// <c>CompositionRoot.DefaultParticipants</c>, which buys four properties from machinery that
/// already exists: phase 3 starts it after every store it might read; teardown stops it FIRST,
/// because participant stop is reverse order, so the sink is gone before the session that would one
/// day drive it; its document flushes in the reserved pre-drain slot; and — the one that matters
/// most here — <see cref="ShutdownStopAsync"/> occupies the HEAD of that same pre-drain slot, which
/// is the port's analogue of upstream's own comment: <i>"Haptics FIRST and synchronously (bounded
/// ~2s): a Lovense level has no server-side watchdog, so a toy we don't countermand keeps running
/// after the app is gone. This cannot be left to Haptics.Dispose() further down"</i>
/// (<c>App.xaml.cs:4401-4407</c>).</para>
///
/// <para><b>Construction starts nothing</b> (SP-003 contract §4.4), and start connects to nothing:
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
    /// The sink this build owns. Defaults to <see cref="HapticSinkFactory.Create"/>, which refuses.
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
    public HapticParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        IHapticSink? sink = null,
        Func<CancellationToken, Task<EntitlementOutcome>>? resolveEntitlement = null,
        Func<long>? sequence = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        _log = infra.Log;
        _resolveEntitlement = resolveEntitlement;
        _sequence = sequence;
        Sink = sink ?? HapticSinkFactory.Create();

        _store = new PersistenceStore<HapticSettingsDocument>(
            infra.OwnerFor("HapticSettings"), infra.Log,
            Path.Combine(dataDirectory, HapticSettingsDocument.FileName),
            HapticSettingsDocument.CurrentSchemaVersion);
    }

    /// <inheritdoc/>
    public string Name => "Haptics";

    /// <inheritdoc/>
    public bool Running => _running;

    /// <summary>The one sink. The panel reads its last outcome; nothing else may touch it.</summary>
    public IHapticSink Sink { get; }

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
    /// How many times this participant asked a sink to connect. <b>Zero on every run of this
    /// build</b>, and it is evidence rather than a counter: a product that knocked on
    /// <c>ws://127.0.0.1:12345</c> with no client to speak the protocol would be making a network
    /// connection for no reason a user could benefit from.
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
    /// <see cref="HapticServerObservation.NotAsked"/>, whose first field is the admission question —
    /// so a build that never asked reports the admitted-provider gap and never a missing device.</para>
    /// </summary>
    public CapabilityState SinkState => (LastObservation ?? HapticServerObservation.NotAsked).Classify();

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
    /// <item><b>Live</b> — <b>UNREACHABLE, and that is D179 made visible on the rack.</b> Live would
    /// have to mean "something is being sent". Nothing is: the thirteen ported effect modules are
    /// silent to this sink, where upstream drives it from THIRTEEN sites in three of them
    /// (<c>Services/Flash/FlashService.cs:1453,1480,1516,1627,1915</c>;
    /// <c>Services/Video/VideoService.cs:2580,4585,4673,6580</c>;
    /// <c>Services/Subliminal/SubliminalService.cs:230,297,387,588</c> — the full enumeration and
    /// the four adjacent-but-not-output sites are on <see cref="IHapticSink"/>). Giving them a
    /// haptic limb is a later packet, and until then a lit dot here would claim a limb that does not
    /// exist.</item>
    /// </list>
    ///
    /// <para>Upstream passes a dot predicate for this row —
    /// <c>() =&gt; App.Settings?.Current?.Haptics?.Enabled</c>
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:520</c>) — so the row HAS a dot, unlike Visuals'
    /// <c>null</c> at <c>:496</c>. It is earned rather than read off the checkbox: the second
    /// conjunct asks the sink what the server last said, which is SP-109's fifth dot meaning (reach)
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
    /// <item>Connect, <b>if and only if a provider route is admitted.</b> Upstream guards its own
    /// auto-connect the same way and says why: <c>Settings.Current.Haptics.AutoConnect &amp;&amp;
    /// HasRealHapticProviderEnabled()</c> (<c>App.xaml.cs:2103</c>, predicate at <c>:3487-3495</c>),
    /// because auto-connecting a provider nobody really has <i>"would silently bring up three virtual
    /// toys and a stream of pink toasts at each launch"</i> (<c>:2098-2102</c>). This build admits no
    /// route, so it never asks — <see cref="ConnectAttempts"/> stays zero.</item>
    /// </list>
    /// <para>The fourth thing it does not do is open a socket to find out. There is no client here to
    /// speak either protocol, so a probe of <c>12345</c> or <c>20010</c> could only ever report
    /// whether some other program is listening, which is not a fact about this build's ability to do
    /// anything.</para>
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

        if (Sink.Route == HapticProviderRoute.None)
        {
            // The admitted-provider gap, logged as a classification exactly once per launch. Not a
            // warning: nothing failed. Nothing was attempted.
            _log.Log("haptics: no provider client is admitted in this build — nothing was attempted "
                + "(this is not \"no device found\")");
            return;
        }

        ConnectAttempts++;
        LastConnectOutcome = await Sink.ConnectAsync(cancellationToken).ConfigureAwait(false);
        LastObservation = await Sink.ObserveAsync(cancellationToken).ConfigureAwait(false);
        _log.Log($"haptics: connect → {LastConnectOutcome.GetType().Name}");
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
                // bearer itself, and this string is destined for a log (the SP-092 discipline).
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
        return Gate;
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
    /// process's (SP-003 §5): nothing in this port stops one and starts it again, and upstream's
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
        // everyone). Today that costs nothing — the unadmitted sink's Dispose is empty — but the day
        // a route is admitted the sink holds a WebSocket or an HttpClient, and leaking one on the
        // failure path is how a process keeps a connection open after it has given up.
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
        var state = await Sink.StopAllAsync().ConfigureAwait(false);
        // Logged as a classification, not as a failure: on this build the all-stop refuses because
        // nothing was ever driven, and a line that read like an error every run would train a reader
        // to ignore the one run where it is not.
        _log.Log($"haptics: all-stop → {state.GetType().Name}");
    }
}
