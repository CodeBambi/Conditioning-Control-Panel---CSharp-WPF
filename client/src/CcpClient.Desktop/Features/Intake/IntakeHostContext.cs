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
        string subjectId,
        IntakeServingRoots.IntakePayloadProbe payloadProbe)
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
        SubjectId = subjectId;
        PayloadProbe = payloadProbe;
    }

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

    /// <summary>The 4-digit local-fiction subject id (never transmitted).</summary>
    public string SubjectId { get; }

    /// <summary>The intake payload probe at start (SP-048 discipline — self-evidencing transcripts).</summary>
    public IntakeServingRoots.IntakePayloadProbe PayloadProbe { get; }

    /// <summary>Start the participant + stores, run the punch-card load repairs (saving a
    /// healed file), mint/load the subject id, and build the services + sinks.</summary>
    public static IntakeHostContext Start(ApplicationHost host, string? dataDirectory = null,
        IntakePassService.IIntakeEntitlementSource? entitlement = null)
    {
        var participant = new IntakeParticipant(new LogSinkAdapter(host), dataDirectory);
        var probe = participant.Start();
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
        var subjectId = IntakeSubjectId.LoadOrMint(Path.Combine(dataDir, "intake_subject.txt"), host.LogDiagnostic);

        return new IntakeHostContext(
            participant, settingsStore, punchStore, assetSelectionStore, pass, punchCard, loom, draftSink, saveImageSink,
            subjectId, probe);
    }

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
