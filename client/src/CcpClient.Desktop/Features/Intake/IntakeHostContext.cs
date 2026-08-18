using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the coordinator-owned intake session context (survives a watchdog relaunch —
/// the DtrhLaunchCoordinator watchdog-ownership pattern): the transport participant, the
/// two SP-005 stores, the services (pass / punch card), the SHARED loom store handle, the
/// subject id, and the sinks. Constructed host-locally (the DtrhHostWindow bark-pipeline
/// precedent — CompositionRoot is out of this packet's File Scope; the app-wide lift is a
/// future row). Store owner names are unique in the OperationRegistry.
/// </summary>
public sealed class IntakeHostContext : IDisposable
{
    private IntakeHostContext(
        IntakeParticipant participant,
        PersistenceStore<IntakeSettingsDocument> settingsStore,
        PersistenceStore<IntakePunchCardDocument> punchStore,
        PersistenceStore<AssetSelectionDocument> assetSelectionStore,
        IntakePassService pass,
        IntakePunchCard punchCard,
        DtrhLoom loom,
        IntakeDraftSink draftSink,
        IntakeSaveImageSink saveImageSink,
        string subjectIdPath,
        Action<string> log)
    {
        Participant = participant;
        SettingsStore = settingsStore;
        PunchStore = punchStore;
        AssetSelectionStore = assetSelectionStore;
        Pass = pass;
        PunchCard = punchCard;
        Loom = loom;
        DraftSink = draftSink;
        SaveImageSink = saveImageSink;
        _subjectIdPath = subjectIdPath;
        _log = log;
    }

    private readonly string _subjectIdPath;
    private readonly Action<string> _log;
    private string? _subjectId;

    private IntakeServingRoots.IntakePayloadProbe? _payloadProbe;

    public IntakeParticipant Participant { get; }

    public PersistenceStore<IntakeSettingsDocument> SettingsStore { get; }

    public PersistenceStore<IntakePunchCardDocument> PunchStore { get; }

    /// <summary>SP-055: the persisted asset selection (read-only this row — the
    /// Assets-tree row owns the write path) feeding the ONE active-pool definition.</summary>
    public PersistenceStore<AssetSelectionDocument> AssetSelectionStore { get; }

    public IntakePassService Pass { get; }

    public IntakePunchCard PunchCard { get; }

    /// <summary>The SHARED b4 loom store handle (&lt;dataDir&gt;/Spirals — never a second root).</summary>
    public DtrhLoom Loom { get; }

    public IntakeDraftSink DraftSink { get; }

    public IntakeSaveImageSink SaveImageSink { get; }

    /// <summary>
    /// The 4-digit local-fiction subject id (never transmitted), minted on FIRST READ.
    ///
    /// <para>Lazy since SP-095, and not as an optimisation: its only reader is the page's boot
    /// config (<c>IntakeHostWindow.axaml.cs:612</c>), so a launch the pass gate REFUSES must not
    /// mint one. A user who has never taken an intake should not find an intake subject id
    /// sitting in their data directory because they once pressed a button and were told no.</para>
    /// </summary>
    public string SubjectId => _subjectId ??= IntakeSubjectId.LoadOrMint(_subjectIdPath, _log);

    /// <summary>The intake payload probe, taken when the transport BINDS (SP-048 discipline —
    /// self-evidencing transcripts). Reading it before <see cref="StartTransport"/> is a bug, not
    /// a default: SP-095 split the bind out of construction so a refused launch never opens a
    /// loopback origin, and a probe invented for that window would describe nothing.</summary>
    public IntakeServingRoots.IntakePayloadProbe PayloadProbe => _payloadProbe
        ?? throw new InvalidOperationException(
            "the intake payload probe is taken at transport bind — call StartTransport() first");

    /// <summary>Start the participant + stores, run the punch-card load repairs (saving a
    /// healed file), mint/load the subject id, and build the services + sinks — then bind the
    /// transport. <see cref="Prepare"/> plus <see cref="StartTransport"/>, for callers that want
    /// both in one step.</summary>
    public static IntakeHostContext Start(ApplicationHost host, string? dataDirectory = null,
        IntakePassService.IIntakeEntitlementSource? entitlement = null)
    {
        var context = Prepare(host, dataDirectory, entitlement);
        context.StartTransport();
        return context;
    }

    /// <summary>
    /// Everything <see cref="Start"/> does EXCEPT binding the loopback origin: the stores, the
    /// punch-card repairs, the subject id, the services and the sinks.
    ///
    /// <para>SP-095 split this out because the weekly-pass gate has to be answered BEFORE a run
    /// opens (WPF checks <c>App.IntakePass.CanStartIntake</c> at
    /// <c>MainWindow/MainWindow.Lab.cs:124</c>, and its pass service is app-wide so the check
    /// costs nothing). The port's pass lives on this context, so a launch that is going to be
    /// refused must be able to read it without standing up an HTTP origin nobody will talk to.
    /// The participant's data directory is fixed at construction, so nothing here needs the
    /// bind.</para>
    /// </summary>
    public static IntakeHostContext Prepare(ApplicationHost host, string? dataDirectory = null,
        IntakePassService.IIntakeEntitlementSource? entitlement = null)
    {
        var participant = new IntakeParticipant(new LogSinkAdapter(host), dataDirectory);
        var dataDir = participant.DataDirectory;

        var settingsStore = new PersistenceStore<IntakeSettingsDocument>(
            host.Registry.OwnerFor("IntakeSettings"),
            new LogSinkAdapter(host),
            Path.Combine(dataDir, "intake_settings.json"),
            IntakeSettingsDocument.CurrentSchemaVersion);
        settingsStore.StartAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        if (settingsStore.LastLoadOutcome is not LoadOutcome.Loaded and not LoadOutcome.Missing)
        {
            host.LogDiagnostic($"intake: settings load → {settingsStore.LastLoadOutcome?.GetType().Name} (typed Degraded — flagged defaults)");
        }

        var punchStore = new PersistenceStore<IntakePunchCardDocument>(
            host.Registry.OwnerFor("IntakePunchCard"),
            new LogSinkAdapter(host),
            Path.Combine(dataDir, "intake_punchcard.json"),
            IntakePunchCardDocument.CurrentSchemaVersion);
        punchStore.StartAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        if (punchStore.LastLoadOutcome is not LoadOutcome.Loaded and not LoadOutcome.Missing)
        {
            host.LogDiagnostic($"intake: punch-card load → {punchStore.LastLoadOutcome?.GetType().Name} (typed Degraded — flagged defaults)");
        }
        else if (IntakePunchCard.Repair(punchStore.Current, DateTimeOffset.UtcNow))
        {
            // The repairs must reach disk or the file re-repairs every launch (WPF :305-314).
            _ = punchStore.Save();
            host.LogDiagnostic("intake: punch-card load repairs applied — file heals on this save");
        }

        // SP-055: the asset-selection store beside the other two (same shared <dataDir> —
        // one asset_selection.json per install; the DTRH host opens its own reader).
        var assetSelectionStore = Persistence.AssetSelectionStore.Start(host, dataDir, "IntakeAssetSelection");

        var pass = new IntakePassService(settingsStore, entitlement, null, host.LogDiagnostic);
        var punchCard = new IntakePunchCard(punchStore, null, host.LogDiagnostic);
        var loom = new DtrhLoom(participant.SpiralsRoot, host.LogDiagnostic);
        var draftSink = new IntakeDraftSink(participant.DraftedSessionsRoot, host.LogDiagnostic);
        var saveImageSink = new IntakeSaveImageSink(participant.IntakeSpiralsRoot, null, host.LogDiagnostic);
        // NOT minted here — see the SubjectId property. Prepare() runs for a press the gate may
        // refuse, and minting an identity for a refused user is a side effect nobody asked for.
        var subjectIdPath = Path.Combine(dataDir, "intake_subject.txt");

        return new IntakeHostContext(
            participant, settingsStore, punchStore, assetSelectionStore, pass, punchCard, loom, draftSink, saveImageSink,
            subjectIdPath, host.LogDiagnostic);
    }

    /// <summary>Bind the loopback origin and take the payload probe (idempotent — the
    /// participant's own start latch does the deduplication). Called immediately before a host
    /// window opens, never on a refused launch.</summary>
    public void StartTransport() => _payloadProbe = Participant.Start();

    /// <summary>Flush both stores (bounded) and stop the transport (idempotent).</summary>
    public void Dispose()
    {
        try { SettingsStore.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3)); } catch { /* best-effort */ }
        try { PunchStore.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3)); } catch { /* best-effort */ }
        try { SettingsStore.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        try { PunchStore.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        try { AssetSelectionStore.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        try { Participant.Dispose(); } catch { /* best-effort */ }
    }

    /// <summary>Adapts the host's diagnostic log to the persistence/transport contracts' ILogSink.</summary>
    private sealed class LogSinkAdapter(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
