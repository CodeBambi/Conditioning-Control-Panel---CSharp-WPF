namespace CcpClient.Desktop.Lifecycle;

using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Persistence;

/// <summary>The minimal logger seam the panic path needs (contract §9). No levels, no framework.</summary>
public interface ILogSink
{
    void Log(string message);
}

/// <summary>Default sink: stderr + debug output. Carries no secrets or user content.</summary>
public sealed class DebugLogSink : ILogSink
{
    public void Log(string message)
    {
        Console.Error.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
    }
}

/// <summary>
/// The composition root: a plain object graph built by explicit constructor calls
/// (contract §4, §7). No DI container, no static accessor. Validation is an explicit
/// checklist over named registrations — a missing registration is a typed Fatal failure
/// before the window exists, never a null at first use.
/// </summary>
/// <summary>
/// The infrastructure participants are constructed against: the operation registry (async
/// contract §1) and the late-bound UI dispatch boundary (async contract §5). Created by
/// the composition root in <see cref="CompositionRoot.Build"/>; participants receive their
/// single owner and the boundary here, never by locating them later.
/// </summary>
public sealed record ParticipantInfrastructure(OperationRegistry Registry, UiDispatchBoundary UiDispatch, ILogSink Log)
{
    /// <summary>The single async-operation owner for one participant (async contract §1.1).</summary>
    public AsyncOperationOwner OwnerFor(string participantName) => Registry.OwnerFor(participantName);
}

/// <summary>A data-root override value that cannot be honored (relative or
/// drive-relative path, uncreatable/unusable directory). Typed + loud at startup — a bad
/// override NEVER degrades silently into the real user profile (a known hazard class:
/// APPDATA= does not move GetFolderPath(ApplicationData), so an unhonored "sandbox" writes
/// the owner's live data).</summary>
public sealed class DataRootOverrideException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

public sealed class CompositionRoot
{
    /// <summary>Flush bounded wait for teardown (persistence contract §11 rule 2): backstop only — the chained writer is the mechanism.</summary>
    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The registered name of the haptic sink's capability. A constant so the
    /// System page's row and the registration cannot be spelled two different ways.</summary>
    public const string HapticCapabilityName = "haptic-sink";

    /// <summary>HARNESS-ONLY isolation seam: when this environment variable names an
    /// absolute directory, it replaces the per-user data root (%APPDATA%\CcpClient /
    /// $XDG_CONFIG_HOME/CcpClient) for EVERY consumer — honored inside
    /// <see cref="DefaultSettingsPath"/>, the single choke point the whole product funnels
    /// through (census: record.md Step 1). Never a user-facing setting; defaults are
    /// byte-identical when unset.</summary>
    public const string DataRootOverrideVariable = "CCP_DATA_ROOT";

    /// <summary>Factory seam so tests can deliberately blank a registration (contract §4 test).</summary>
    public Func<ILogSink?> LogSinkFactory { get; init; } = () => new DebugLogSink();

    /// <summary>Slice b5 HARNESS-ONLY: a loopback route prefix that answers 403
    /// (blocked-route failure injection, W18 class). Null in product runs.</summary>
    public string? DtrhBlockedRoutePrefixHarness { get; init; }

    /// <summary>
    /// C7 product config seam: overrides the Ollama host for the companion's
    /// loopback provider (the host is already a LoopbackOllamaProviderOptions field).
    /// This can NEVER widen the network boundary: a non-loopback host classifies
    /// RemoteHostOllama and is rejected pre-socket by the admission policy AND the
    /// provider itself (defense in depth). Null = the default http://localhost:11434/.
    /// </summary>
    public string? AiOllamaHostOverride { get; init; }

    /// <summary>The capability registry the NEXT ParticipantsFactory call composes into (set by Build/Validate before invoking the factory).</summary>
    private CapabilityRegistry? _capabilitiesForParticipants;

    /// <summary>
    /// Settings file location seam (persistence contract §4 rule 1). Production default is
    /// the per-user data path; tests substitute a temp path. The data-path authority itself
    /// is row 8/9's scope — this is the demonstrator's landing spot, not a product decision.
    /// </summary>
    public Func<string> SettingsPathFactory { get; init; } = DefaultSettingsPath;

    /// <summary>Each required participant is named here; deleting one is a compile error or a validation failure.</summary>
    public Func<ParticipantInfrastructure, IReadOnlyList<IBackgroundParticipant>?> ParticipantsFactory { get; init; }

    /// <summary>
    /// The entitlement capability seam. Product default is
    /// <see cref="Entitlement.HostLoginEntitlement.ForCurrentPlatform"/> — the real DPAPI read of
    /// the shipping app's login, over this build's real (unconfigured) authority. Tests inject
    /// their own reader/authority doubles here so no test ever touches the developer's real
    /// store; nothing may inject a stub that returns <c>Entitled</c> on a product path, which is
    /// the fake-available shape the capability contract bans.
    /// </summary>
    public Func<ILogSink, Entitlement.HostLoginEntitlement>? EntitlementFactory { get; init; }

    /// <summary>
    /// The conditioning session's clock seam. Product default is the real
    /// <see cref="Session.SystemSessionClock"/>; a headless test substitutes a manual clock so
    /// it can drive the REAL shell — press the real START button, advance, and watch the real
    /// effect fire — without a wall-clock wait anywhere in it. It is a TIMER source only: it can
    /// make an effect fire sooner, never make one available that is not.
    /// </summary>
    public Func<Session.ISessionClock>? SessionClockFactory { get; init; }

    /// <summary>
    /// The Flash Images pool seam. Product default reads the user's own
    /// <c>&lt;dataDir&gt;/assets/images</c>. Tests substitute an in-memory pool so the session
    /// spine's proofs never depend on a filesystem.
    /// </summary>
    public Func<Effects.IFlashImagePool>? FlashImagePoolFactory { get; init; }

    /// <summary>
    /// The SCHEDULER's clock seam, and it is a different clock from
    /// <see cref="SessionClockFactory"/> on purpose — <see cref="Scheduling.IScheduleClock"/> reads
    /// LOCAL time (the user typed a wall-clock window) where <see cref="Session.ISessionClock"/>
    /// reads UTC (a paced effect must not fire twice at 02:00 on a daylight-saving night). Product
    /// default is the real <see cref="Scheduling.SystemScheduleClock"/>; a test injects a manual one
    /// so nothing in the suite waits out the 60-second start-up grace on a wall clock.
    ///
    /// <para>Like the session clock it is a TIMER and a READING only: it can make the scheduler
    /// evaluate sooner, and it can move what "now" is, but it cannot make the scheduler start a
    /// session the predicate refuses.</para>
    /// </summary>
    public Func<Scheduling.IScheduleClock>? ScheduleClockFactory { get; init; }

    // Deliberately adds NO sink-factory seam here. The haptic sink is already injectable
    // through ParticipantsFactory — which is how the ownership, teardown-ordering and
    // entitlement-transition facts drive a recording sink — and a second seam that no test and no
    // product path ever set would be a configuration point nothing configures. Anything compiled
    // and never executed is unexecuted, and a seam is not exempt from that.

    /// <summary>The entitlement capability the NEXT ParticipantsFactory call composes against (set
    /// by Build before invoking the factory, the same way <see cref="_capabilitiesForParticipants"/>
    /// is). Null inside <see cref="Validate"/>, where no DPAPI read may happen.</summary>
    private Entitlement.HostLoginEntitlement? _entitlementForParticipants;

    public CompositionRoot()
    {
        // Instance default (needs SettingsPathFactory); init-only so tests can override.
        ParticipantsFactory = DefaultParticipants;
    }

    /// <summary>
    /// Per-user settings path: %APPDATA%\\CcpClient on Windows; $XDG_CONFIG_HOME/CcpClient when
    /// set, else ~/.config/CcpClient on Linux (.NET's Unix ApplicationData mapping — verified
    /// that the quarantine lands under XDG_CONFIG_HOME when it is set).
    /// When the <see cref="DataRootOverrideVariable"/> environment variable is set,
    /// it IS the data root (harness isolation) — validated per call by
    /// <see cref="ResolveDataRoot"/>; an unhonorable value throws typed, never falls back.
    /// Read per call, never cached: a cached static would freeze on whichever test/run
    /// touched it first (consult A2).
    /// </summary>
    public static string DefaultSettingsPath()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataRootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.Combine(ResolveDataRoot(overrideRoot), "settings.json");
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, "CcpClient", "settings.json");
    }

    /// <summary>The override value currently in force, or null when unset/whitespace.
    /// Program uses this for the pre-phase check + the override-active log line.</summary>
    public static string? ActiveDataRootOverride()
    {
        var value = Environment.GetEnvironmentVariable(DataRootOverrideVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Validate + normalize a data-root override value. Fully-qualified absolute paths only
    /// (<see cref="Path.IsPathFullyQualified"/> rejects relative AND drive-relative
    /// <c>C:foo</c> values, which <see cref="Path.IsPathRooted"/> wrongly accepts); the
    /// directory is created so a harness can point at a fresh root. Every failure is a
    /// typed <see cref="DataRootOverrideException"/> — there is NO fallback path.
    /// </summary>
    public static string ResolveDataRoot(string overrideValue)
    {
        if (!Path.IsPathFullyQualified(overrideValue))
        {
            throw new DataRootOverrideException(
                $"{DataRootOverrideVariable} must be a fully-qualified absolute path, got '{overrideValue}' — "
                + "refusing to run: an unhonored override would silently use the real user profile");
        }

        var full = Path.GetFullPath(overrideValue);
        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new DataRootOverrideException(
                $"{DataRootOverrideVariable} directory '{full}' cannot be created/used ({ex.GetType().Name}: {ex.Message}) — "
                + "refusing to run: an unhonored override would silently use the real user profile", ex);
        }

        return full;
    }

    private IReadOnlyList<IBackgroundParticipant> DefaultParticipants(ParticipantInfrastructure infra)
    {
        // Persistence contract §4 rule 1: the store starts first, so its phase-3 load
        // completes before any consumer participant starts.
        var store = new PersistenceStore<DemoSettings>(
            infra.OwnerFor("Persistence"), infra.Log, SettingsPathFactory(),
            DemoSettings.CurrentSchemaVersion, [new DemoMigrationV0ToV1()]);
        // The haptic participant is CONSTRUCTED here, above the session, because the session
        // is constructed AGAINST its limb — and construction starts nothing (contract §4.4),
        // so hoisting it changes no behaviour at all. It is still REGISTERED last, below, which is
        // what actually decides start and teardown order.
        //
        // A session that built its own sink instead would be a SECOND sink: harmless only while
        // HapticSinkFactory.AdmittedRoutes is empty, and two live clients against one server the day
        // a route is admitted.
        var haptics = new Haptics.HapticParticipant(
            infra, Path.GetDirectoryName(SettingsPathFactory())!,
            sink: null,
            _entitlementForParticipants is { } entitlement ? entitlement.ResolveAsync : null);
        // Built into a local first because the scheduler below is constructed AGAINST this
        // exact engine. A second SessionParticipant here would give the rack row and the scheduler
        // two different sessions, which is the shell-local-copy failure MainWindow already refuses.
        var session = new Session.SessionParticipant(
            infra, Path.GetDirectoryName(SettingsPathFactory())!,
            SessionClockFactory?.Invoke(), FlashImagePoolFactory?.Invoke(),
            haptics: haptics.Limb);
        return
        [
            store,
            new HeartbeatParticipant(infra.OwnerFor("Heartbeat"), infra.UiDispatch),
            // demo.status-ticker: registered AFTER the store — phase-3 start order
            // IS the restore-then-start ordering (its start reads the restored flag).
            new Features.StatusTickerParticipant(infra.OwnerFor("StatusTicker"), infra.UiDispatch, store),
            // AvatarTube demonstrator: construction starts nothing; the tube opens
            // on the user path (phase 4) via --avatartube-demo.
            new Features.AvatarTube.AvatarTubeParticipant(infra.OwnerFor("AvatarTubeDemo"), infra.UiDispatch, infra.Log),
            // DTRH host slice b2: the three local save slots (machinery per
            // slot + DTRH-owned active-slot index), started after the demo store.
            new Features.Dtrh.DtrhSaveSlots(infra, Path.GetDirectoryName(SettingsPathFactory())!),
            // DTRH host slice b1: owns the §4 loopback origins + §3.3 inbox + bridge
            // token; the web surface itself is phase-4, selected by probed capability states.
            // B4: the data directory threads through for the Loom store + user media.
            // B5: the HARNESS-ONLY blocked-route prefix threads through (Program
            // wiring amendment — failure-injection evidence).
            new Features.Dtrh.DtrhParticipant(infra.OwnerFor("DtrhHost"), infra.Log,
                Path.GetDirectoryName(SettingsPathFactory())!,
                DtrhBlockedRoutePrefixHarness is { Length: > 0 } blocked ? [blocked] : null),
            // AI companion slice c7: the product composition of the full AI chain
            // (pipeline + provider seam + moderation boundary + memory store + awareness
            // service + executor). Registered LAST: its memory store loads in phase-3
            // order; its provider probes register into the shared registry for the
            // CapabilityProbes phase. The host override is a loopback-only config seam.
            new Features.Companion.CompanionParticipant(
                infra, _capabilitiesForParticipants ?? new CapabilityRegistry(),
                Path.GetDirectoryName(SettingsPathFactory())!,
                AiOllamaHostOverride),
            // The conditioning session: the preset store, the effect rack and the
            // engine START drives. It starts NO session: WPF's engine runs only when the user
            // presses START (MainWindow/MainWindow.StartStop.cs:34,105).
            session,
            // The SCHEDULER, and it is the first participant here that can start a session
            // by itself. Registered AFTER the session for two reasons that both matter:
            // registration order is phase-3 START order, so the session's preset load completes
            // before the scheduler can evaluate anything; and participant stop is REVERSE order,
            // so at teardown the scheduler's poll dies BEFORE the session it drives.
            //
            // Construction still starts nothing. Nothing here can begin a session until phase 3
            // has started the participant AND the 60-second grace has elapsed
            // (MainWindow/MainWindow.xaml.cs:624-635).
            new Scheduling.SchedulerParticipant(
                infra, Path.GetDirectoryName(SettingsPathFactory())!, session.Engine,
                ScheduleClockFactory?.Invoke()),
            // The HAPTIC SINK, and it is the second participant here that belongs to the app
            // rather than to a session — upstream's is a static built at startup and never engine
            // started (App.xaml.cs:533, :2060; zero hits for App.Haptics in
            // MainWindow/MainWindow.StartStop.cs). Registered LAST for the same reason the scheduler
            // is registered after the session: participant stop is REVERSE order, so at teardown the
            // sink is released before anything that could still be driving it.
            //
            // It takes the entitlement capability the composition root already owns, so the object
            // the haptics gate consults is the SAME object whose probe the System page reports and
            // the same one the DTRH door consults. A second entitlement here would let one door
            // refuse a user the other let through.
            //
            // Construction connects to nothing, and neither does phase 3 while no provider route is
            // admitted: upstream guards its own auto-connect the same way and says why
            // (App.xaml.cs:2098-2105).
            //
            // It is CONSTRUCTED above the session (which takes its limb) and REGISTERED here,
            // last. Registration order is what decides phase-3 start and reverse-order teardown, so
            // the limb and its sink are still released before anything that could drive them.
            haptics,
        ];
    }

    /// <summary>
    /// Phase 2 body: fail fast with a typed error naming what is missing.
    /// </summary>
    public bool Validate(out InitFailure? failure)
    {
        var log = LogSinkFactory();
        if (log is null)
        {
            failure = new InitFailure("CompositionRoot", InitFailureKind.Fatal, "Missing registration: LogSink");
            return false;
        }

        // Probe with a scratch infrastructure (participant constructors are cheap, contract §4.4).
        _capabilitiesForParticipants = new CapabilityRegistry();
        if (ParticipantsFactory(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), log)) is null)
        {
            failure = new InitFailure("CompositionRoot", InitFailureKind.Fatal, "Missing registration: BackgroundParticipants");
            return false;
        }

        _capabilitiesForParticipants = null;

        failure = null;
        return true;
    }

    /// <summary>Manual construction only. Precondition: <see cref="Validate"/> passed.</summary>
    public ApplicationHost Build(StartupTrace trace)
    {
        var log = LogSinkFactory() ?? throw new InvalidOperationException("Validate must run before Build.");
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), log);
        // Capability contract §3: the registry exists BEFORE participants so feature
        // participants (companion AI chain) can register their probes at
        // construction; probes still execute only in the CapabilityProbes phase.
        var capabilities = new CapabilityRegistry();
        _capabilitiesForParticipants = capabilities;
        // The entitlement capability is CONSTRUCTED here, before the participants factory
        // runs, because the haptic participant is gated on it and a participant cannot be handed an
        // object that does not exist yet. Its capability REGISTRATION stays where it was put,
        // below, so the registry's name order is unchanged. Validate() leaves this field null and no
        // DPAPI read happens there.
        var entitlement = EntitlementFactory is { } entitlementFactory
            ? entitlementFactory(log)
            : Entitlement.HostLoginEntitlement.ForCurrentPlatform(log: log.Log);
        _entitlementForParticipants = entitlement;
        var participants = ParticipantsFactory(infra) ?? throw new InvalidOperationException("Validate must run before Build.");
        _capabilitiesForParticipants = null;
        _entitlementForParticipants = null;
        // Persistence contract §11: the store's flush is wired into the host's reserved
        // pre-drain slot. A custom participants factory without a store gets no flush.
        var store = participants.OfType<PersistenceStore<DemoSettings>>().FirstOrDefault();
        // The DTRH slot stores flush in the same reserved pre-drain slot.
        var slotStores = participants.OfType<Features.Dtrh.DtrhSaveSlots>().FirstOrDefault();
        // The companion's memory store flushes in the same slot (contract §11).
        var companion = participants.OfType<Features.Companion.CompanionParticipant>().FirstOrDefault();
        // The session preset flushes in the same slot. A dial the user moved on the way
        // out is a persisted setting like any other, and teardown's head slot is the ONE place
        // the port guarantees it reaches disk.
        var session = participants.OfType<Session.SessionParticipant>().FirstOrDefault();
        // The scheduler's ten settings flush in the same slot. A day box a user unticked
        // on the way out decides whether a session appears tomorrow, so it is the LAST setting the
        // port may lose (persistence contract §11).
        var scheduler = participants.OfType<Scheduling.SchedulerParticipant>().FirstOrDefault();
        // The haptic sink's ALL-STOP takes the HEAD of the same slot, ahead of every flush.
        // Upstream's ordering, and upstream's reason: "Haptics FIRST and synchronously (bounded ~2s):
        // a Lovense level has no server-side watchdog, so a toy we don't countermand keeps running
        // after the app is gone. This cannot be left to Haptics.Dispose() further down"
        // (ConditioningControlPanel/App.xaml.cs:4401-4407). A settings write that lost its race would
        // cost a dial; a level that lost its race is still running when the user has closed the app.
        var haptics = participants.OfType<Haptics.HapticParticipant>().FirstOrDefault();

        // Capability contract §3: the demonstrator probes are registered at composition
        // (never run here) and execute as owned operations in the CapabilityProbes phase.
        // The fs probe targets the SAME data directory the store persists into.
        var dataDirectory = Path.GetDirectoryName(SettingsPathFactory())!;
        capabilities.Register("display-session", _ =>
            Task.FromResult(SessionProbe.Probe(new RuntimeSessionEnvironment())));
        capabilities.Register("atomic-filesystem", token =>
            Task.Run(() => AtomicFileSystemProbe.Probe(dataDirectory, new ProcMountsTable()), token));
        // The DTRH host's two admitted surfaces (admission §5), probed by
        // exercising the exact dependency loads the WebView package performs (12.0.1
        // binary-verified); rendering is claimed only by headed evidence, never here.
        capabilities.Register(Features.Dtrh.DtrhCapabilityProbes.EmbeddedCapability, _ =>
            Task.FromResult(Features.Dtrh.DtrhCapabilityProbes.ProbeEmbedded()));
        capabilities.Register(Features.Dtrh.DtrhCapabilityProbes.DialogCapability, _ =>
            Task.FromResult(Features.Dtrh.DtrhCapabilityProbes.ProbeDialog()));
        // The tunnel backdrop's single admitted surface (Windows embedded WebView2).
        // Linux = typed Unavailable with the tunnel's OWN reasons (no page-side bridge
        // transport, no keep-below control — never a green dialog row for an unadmitted
        // surface, consult ruling 3b). Windows delegates to the DTRH embedded probe (the
        // dependency is literally the same engine load).
        capabilities.Register(Features.Chaos.ChaosTunnelCapabilityProbes.EmbeddedCapability, _ =>
            Task.FromResult(Features.Chaos.ChaosTunnelCapabilityProbes.ProbeEmbedded()));
        // Registers the entitlement capability, and registers it HERE rather than
        // letting the shell build its own, for a reason bigger than tidiness. Today this build
        // has no entitlement authority, so the honest answer for every user is
        // Unavailable(tier-authority-absent) and the DTRH door refuses everyone. A capability
        // that refuses everyone while staying invisible in the ONE place the port reports what
        // it cannot do — the System page, which renders every registered capability's typed
        // state — is exactly the shape the truthful-capability contract exists to prevent.
        // The probe answers a NARROWER question than the gate (can this environment read the
        // shipping app's login at all) and never claims a tier: a readable login with no
        // authority behind it is Degraded, never Available (HostLoginEntitlement.ProbeAsync).
        capabilities.Register(Entitlement.HostLoginEntitlement.CapabilityName, entitlement.ProbeAsync);
        // Registers the haptic sink for the same reason as the entitlement
        // capability: this build refuses every user, and a capability that refuses everyone while
        // staying invisible in the ONE place the port reports what it cannot do is exactly the shape
        // the truthful-capability contract exists to prevent. The probe asks the sink and classifies
        // the answer; it can never produce Available here, because the classification's first arm is
        // the admitted-provider question (Haptics/IHapticSink.cs, HapticServerObservation.Classify).
        if (haptics is not null)
        {
            capabilities.Register(HapticCapabilityName, async token =>
                (await haptics.Sink.ObserveAsync(token).ConfigureAwait(false)).Classify());
        }

        var probeRunner = new CapabilityProbeRunner(infra.Registry.OwnerFor("CapabilityProbes"), capabilities);

        return new ApplicationHost(
            log, participants, trace, infra.Registry, infra.UiDispatch,
            preDrainFlush: store is null && slotStores is null && companion is null && session is null
                && scheduler is null && haptics is null
                ? null
                : async () =>
                {
                    // FIRST, ahead of every flush: see the haptics comment above. Bounded by the
                    // sink itself and never throwing here — ApplicationHost already guards this
                    // slot, and a teardown that threw on the way to countermanding a device would
                    // be the worst possible place to lose the rest of the sequence.
                    if (haptics is not null) await haptics.ShutdownStopAsync().ConfigureAwait(false);
                    if (store is not null) await store.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (slotStores is not null) await slotStores.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (companion is not null) await companion.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (session is not null) await session.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (scheduler is not null) await scheduler.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (haptics is not null) await haptics.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                },
            capabilities: capabilities, probeRunner: probeRunner, entitlement: entitlement);
    }
}
