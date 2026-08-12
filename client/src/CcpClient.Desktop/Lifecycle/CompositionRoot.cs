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

/// <summary>SP-057: a data-root override value that cannot be honored (relative or
/// drive-relative path, uncreatable/unusable directory). Typed + loud at startup — a bad
/// override NEVER degrades silently into the real user profile (the SP-052 hazard class:
/// APPDATA= does not move GetFolderPath(ApplicationData), so an unhonored "sandbox" writes
/// the owner's live data).</summary>
public sealed class DataRootOverrideException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

public sealed class CompositionRoot
{
    /// <summary>Flush bounded wait for teardown (persistence contract §11 rule 2): backstop only — the chained writer is the mechanism.</summary>
    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>SP-057 HARNESS-ONLY isolation seam: when this environment variable names an
    /// absolute directory, it replaces the per-user data root (%APPDATA%\CcpClient /
    /// $XDG_CONFIG_HOME/CcpClient) for EVERY consumer — honored inside
    /// <see cref="DefaultSettingsPath"/>, the single choke point the whole product funnels
    /// through (census: record.md Step 1). Never a user-facing setting; defaults are
    /// byte-identical when unset.</summary>
    public const string DataRootOverrideVariable = "CCP_DATA_ROOT";

    /// <summary>Factory seam so tests can deliberately blank a registration (contract §4 test).</summary>
    public Func<ILogSink?> LogSinkFactory { get; init; } = () => new DebugLogSink();

    /// <summary>SP-027 b5 HARNESS-ONLY: a loopback route prefix that answers 403
    /// (blocked-route failure injection, W18 class). Null in product runs.</summary>
    public string? DtrhBlockedRoutePrefixHarness { get; init; }

    /// <summary>
    /// SP-046 c7 product config seam: overrides the Ollama host for the companion's
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

    public CompositionRoot()
    {
        // Instance default (needs SettingsPathFactory); init-only so tests can override.
        ParticipantsFactory = DefaultParticipants;
    }

    /// <summary>
    /// Per-user settings path: %APPDATA%\\CcpClient on Windows; $XDG_CONFIG_HOME/CcpClient when
    /// set, else ~/.config/CcpClient on Linux (.NET's Unix ApplicationData mapping — verified
    /// SP-010: the quarantine lands under XDG_CONFIG_HOME when it is set).
    /// SP-057: when the <see cref="DataRootOverrideVariable"/> environment variable is set,
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
        return
        [
            store,
            new HeartbeatParticipant(infra.OwnerFor("Heartbeat"), infra.UiDispatch),
            // demo.status-ticker (SP-007): registered AFTER the store — phase-3 start order
            // IS the restore-then-start ordering (its start reads the restored flag).
            new Features.StatusTickerParticipant(infra.OwnerFor("StatusTicker"), infra.UiDispatch, store),
            // SP-015 AvatarTube demonstrator: construction starts nothing; the tube opens
            // on the user path (phase 4) via --avatartube-demo.
            new Features.AvatarTube.AvatarTubeParticipant(infra.OwnerFor("AvatarTubeDemo"), infra.UiDispatch, infra.Log),
            // SP-024 DTRH host slice b2: the three local save slots (SP-005 machinery per
            // slot + DTRH-owned active-slot index), started after the demo store.
            new Features.Dtrh.DtrhSaveSlots(infra, Path.GetDirectoryName(SettingsPathFactory())!),
            // SP-023 DTRH host slice b1: owns the §4 loopback origins + §3.3 inbox + bridge
            // token; the web surface itself is phase-4, selected by probed capability states.
            // SP-026 b4: the data directory threads through for the Loom store + user media.
            // SP-027 b5: the HARNESS-ONLY blocked-route prefix threads through (Program
            // wiring amendment — failure-injection evidence).
            new Features.Dtrh.DtrhParticipant(infra.OwnerFor("DtrhHost"), infra.Log,
                Path.GetDirectoryName(SettingsPathFactory())!,
                DtrhBlockedRoutePrefixHarness is { Length: > 0 } blocked ? [blocked] : null),
            // SP-046 AI companion slice c7: the product composition of the full AI chain
            // (pipeline + provider seam + moderation boundary + memory store + awareness
            // service + executor). Registered LAST: its memory store loads in phase-3
            // order; its provider probes register into the shared registry for the
            // CapabilityProbes phase. The host override is a loopback-only config seam.
            new Features.Companion.CompanionParticipant(
                infra, _capabilitiesForParticipants ?? new CapabilityRegistry(),
                Path.GetDirectoryName(SettingsPathFactory())!,
                AiOllamaHostOverride),
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
        // participants (SP-046 companion AI chain) can register their probes at
        // construction; probes still execute only in the CapabilityProbes phase.
        var capabilities = new CapabilityRegistry();
        _capabilitiesForParticipants = capabilities;
        var participants = ParticipantsFactory(infra) ?? throw new InvalidOperationException("Validate must run before Build.");
        _capabilitiesForParticipants = null;
        // Persistence contract §11: the store's flush is wired into the host's reserved
        // pre-drain slot. A custom participants factory without a store gets no flush.
        var store = participants.OfType<PersistenceStore<DemoSettings>>().FirstOrDefault();
        // SP-024: the DTRH slot stores flush in the same reserved pre-drain slot.
        var slotStores = participants.OfType<Features.Dtrh.DtrhSaveSlots>().FirstOrDefault();
        // SP-046: the companion's memory store flushes in the same slot (SP-005 contract §11).
        var companion = participants.OfType<Features.Companion.CompanionParticipant>().FirstOrDefault();

        // Capability contract §3: the demonstrator probes are registered at composition
        // (never run here) and execute as owned operations in the CapabilityProbes phase.
        // The fs probe targets the SAME data directory the store persists into.
        var dataDirectory = Path.GetDirectoryName(SettingsPathFactory())!;
        capabilities.Register("display-session", _ =>
            Task.FromResult(SessionProbe.Probe(new RuntimeSessionEnvironment())));
        capabilities.Register("atomic-filesystem", token =>
            Task.Run(() => AtomicFileSystemProbe.Probe(dataDirectory, new ProcMountsTable()), token));
        // SP-023: the DTRH host's two admitted surfaces (admission §5), probed by
        // exercising the exact dependency loads the WebView package performs (12.0.1
        // binary-verified); rendering is claimed only by headed evidence, never here.
        capabilities.Register(Features.Dtrh.DtrhCapabilityProbes.EmbeddedCapability, _ =>
            Task.FromResult(Features.Dtrh.DtrhCapabilityProbes.ProbeEmbedded()));
        capabilities.Register(Features.Dtrh.DtrhCapabilityProbes.DialogCapability, _ =>
            Task.FromResult(Features.Dtrh.DtrhCapabilityProbes.ProbeDialog()));
        // SP-061: the tunnel backdrop's single admitted surface (Windows embedded WebView2).
        // Linux = typed Unavailable with the tunnel's OWN reasons (no page-side bridge
        // transport, no keep-below control — never a green dialog row for an unadmitted
        // surface, consult ruling 3b). Windows delegates to the DTRH embedded probe (the
        // dependency is literally the same engine load).
        capabilities.Register(Features.Chaos.ChaosTunnelCapabilityProbes.EmbeddedCapability, _ =>
            Task.FromResult(Features.Chaos.ChaosTunnelCapabilityProbes.ProbeEmbedded()));
        var probeRunner = new CapabilityProbeRunner(infra.Registry.OwnerFor("CapabilityProbes"), capabilities);

        return new ApplicationHost(
            log, participants, trace, infra.Registry, infra.UiDispatch,
            preDrainFlush: store is null && slotStores is null && companion is null
                ? null
                : async () =>
                {
                    if (store is not null) await store.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (slotStores is not null) await slotStores.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                    if (companion is not null) await companion.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);
                },
            capabilities: capabilities, probeRunner: probeRunner);
    }
}
