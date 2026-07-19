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

public sealed class CompositionRoot
{
    /// <summary>Flush bounded wait for teardown (persistence contract §11 rule 2): backstop only — the chained writer is the mechanism.</summary>
    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Factory seam so tests can deliberately blank a registration (contract §4 test).</summary>
    public Func<ILogSink?> LogSinkFactory { get; init; } = () => new DebugLogSink();

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
    /// Per-user settings path: %APPDATA%\\CcpClient on Windows, ~/.config/CcpClient on Linux.
    /// </summary>
    public static string DefaultSettingsPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, "CcpClient", "settings.json");
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
        if (ParticipantsFactory(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), log)) is null)
        {
            failure = new InitFailure("CompositionRoot", InitFailureKind.Fatal, "Missing registration: BackgroundParticipants");
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Manual construction only. Precondition: <see cref="Validate"/> passed.</summary>
    public ApplicationHost Build(StartupTrace trace)
    {
        var log = LogSinkFactory() ?? throw new InvalidOperationException("Validate must run before Build.");
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), log);
        var participants = ParticipantsFactory(infra) ?? throw new InvalidOperationException("Validate must run before Build.");
        // Persistence contract §11: the store's flush is wired into the host's reserved
        // pre-drain slot. A custom participants factory without a store gets no flush.
        var store = participants.OfType<PersistenceStore<DemoSettings>>().FirstOrDefault();

        // Capability contract §3: the demonstrator probes are registered at composition
        // (never run here) and execute as owned operations in the CapabilityProbes phase.
        // The fs probe targets the SAME data directory the store persists into.
        var capabilities = new CapabilityRegistry();
        var dataDirectory = Path.GetDirectoryName(SettingsPathFactory())!;
        capabilities.Register("display-session", _ =>
            Task.FromResult(SessionProbe.Probe(new RuntimeSessionEnvironment())));
        capabilities.Register("atomic-filesystem", token =>
            Task.Run(() => AtomicFileSystemProbe.Probe(dataDirectory, new ProcMountsTable()), token));
        var probeRunner = new CapabilityProbeRunner(infra.Registry.OwnerFor("CapabilityProbes"), capabilities);

        return new ApplicationHost(
            log, participants, trace, infra.Registry, infra.UiDispatch,
            preDrainFlush: store is null ? null : () => store.FlushAsync(DefaultFlushTimeout),
            capabilities: capabilities, probeRunner: probeRunner);
    }
}
