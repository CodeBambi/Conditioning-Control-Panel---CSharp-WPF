using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// The persisted memory document (admission §4 rules 1–3), schema v1. Per-turn
/// user/assistant chat pairs ONLY, provider-neutral (contract §5 rule 3) — system prompts,
/// enrichment blocks, and awareness/ambient turns are NEVER persisted (WPF precedent:
/// stateless ambient path, LocalAiService.cs:476-502; persist skips system/enrichment,
/// LocalAiService.cs:107-135). Content is user data under the persistence contract's
/// authority; it NEVER flows into diagnostics (contract §12 rule 3) and is never a
/// secret (contract §10).
///
/// Both-answers-additively shape (admission §4 rule 3; §9.2 #3 owner-pending — NO value
/// is decided here): the disable flag and the retention policy are ORTHOGONAL fields, and
/// a dormant marker is representable from v1:
///   disable = retain-dormant → Disabled=true + DormantSinceUtc set + Turns retained;
///   disable = delete         → Disabled=true + Turns emptied.
/// RetentionMaxPairs stays NULL on disk until an owner decision records a value — a
/// persisted number would read as a decided policy (pre-approach consult, record.md §3.1).
/// Unknown members round-trip verbatim (persistence contract §6).
/// </summary>
public sealed class AiMemoryDocument
{
    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The remembered turns, oldest first. Only user/assistant pairs ever land here.</summary>
    public List<AiMemoryTurn> Turns { get; set; } = [];

    /// <summary>Disable flag — ORTHOGONAL to the retention policy. Placeholder from v1; c4 mechanics never consult it.</summary>
    public bool Disabled { get; set; }

    /// <summary>Retention policy field — null = NO policy recorded. Placeholder from v1; enforcement runs on the injected <see cref="AiMemoryRetention"/> mechanism record instead.</summary>
    public int? RetentionMaxPairs { get; set; }

    /// <summary>Dormant marker, representable from v1 (null = active). Placeholder; c4 mechanics never consult it.</summary>
    public DateTimeOffset? DormantSinceUtc { get; set; }

    /// <summary>Unknown-member preserve (persistence contract §6) — required on every persisted model type.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The pair-cap MECHANISM record (admission §4 rule 3). The VALUE is owner-pending
/// (§9.2 #3); <see cref="WpfBaselinePlaceholder"/> records the WPF baseline FACT
/// (LocalAiService.cs:92, MaxPersistedPairs = 50) exactly like c3's
/// AiEscalationThresholds.WpfBaselinePlaceholder — baseline, never decision.
/// </summary>
public sealed record AiMemoryRetention(int MaxPairs)
{
    /// <summary>WPF baseline FACT (50 pairs, LocalAiService.cs:92). Placeholder default; NOT an owner decision.</summary>
    public static readonly AiMemoryRetention WpfBaselinePlaceholder = new(50);
}

/// <summary>
/// The explicit typed memory-consent state (contract §5 rule 2; admission §4 rule 5).
/// Scope (global vs per-feature) and default are owner-pending (§9.2 #3); the placeholder
/// default is <see cref="Denied"/> — the conservative-posture precedent (c3's Empty
/// moderation policy, c6's none-admitted commands), deliberately stricter than the WPF
/// baseline FACT (ChatMemoryEnabled default true, CompanionPromptSettings.cs:120).
/// Recorded in record.md §3.1.
/// </summary>
public enum AiMemoryConsent
{
    Denied,
    Granted,
}

/// <summary>Typed write-admission outcome (admission §4 rule 5): a denied write is a typed no-op — never silent, never throws.</summary>
public enum AiMemoryWriteAdmission
{
    Admitted,
    ConsentDenied,

    /// <summary>The document on disk is newer than this build; writes stay locked out (SP-005 contract §4 rule 7).</summary>
    WritesDisabled,
}

/// <summary>Typed explicit-clear outcome. <see cref="Degraded"/>: in-memory state emptied but a newer-schema document was NOT deleted (an older build never clobbers a newer one).</summary>
public enum AiMemoryClearOutcome
{
    Cleared,
    Degraded,
}

/// <summary>
/// The first <see cref="IAiMemoryStore"/> implementation (admission §8 slice c4), on
/// SP-005 machinery (admission §4 rule 2 — DECIDED, no new seam): ONE
/// <see cref="PersistenceStore{TModel}"/> of <see cref="AiMemoryDocument"/> in the
/// user-data root with its OWN named <see cref="AsyncOperationOwner"/> (the SP-024
/// lesson: N stores sharing one owner cancel each other's writes). schemaVersion +
/// migration journal, corrupt-document quarantine once at startup → typed Degraded
/// (the b2/b4 precedent), unknown-member preserve — all inherited from SP-005.
///
/// Consent is code-enforced at WRITE ADMISSION (contract §5 rule 2 — never a prompt
/// convention): a denied <see cref="Append"/> is a typed no-op observable via
/// <see cref="LastWriteAdmission"/>. Reads are NOT gated in c4 — WPF's
/// load-skip-under-disabled (LocalAiService.cs:113) belongs to the dormant-semantics
/// owner question (§9.2 #3); recorded, never silently extended (the SP-038
/// escalation-scope lesson).
///
/// NOT claimed here: nothing consumes memory as conversation context yet — prompt
/// assembly lands in c7 (pre-approach consult, record.md §3.1). c4's claim is exactly
/// "memory persists and clears".
/// </summary>
public sealed class AiMemoryStore : IAiMemoryStore, IBackgroundParticipant
{
    /// <summary>The document file name in the user-data root.</summary>
    public const string FileName = "ai_memory.json";

    private readonly PersistenceStore<AiMemoryDocument> _store;
    private readonly Func<AiMemoryConsent> _consent;
    private readonly AiMemoryRetention _retention;
    private readonly string _filePath;
    private readonly string _tempPath;
    private readonly object _gate = new();

    public AiMemoryStore(
        AsyncOperationOwner owner,
        ILogSink log,
        string filePath,
        Func<AiMemoryConsent>? consent = null,
        AiMemoryRetention? retention = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(filePath);
        _store = new PersistenceStore<AiMemoryDocument>(owner, log, filePath, AiMemoryDocument.CurrentSchemaVersion);
        // Placeholder default = Denied (conservative posture — see AiMemoryConsent).
        _consent = consent ?? (static () => AiMemoryConsent.Denied);
        _retention = retention ?? AiMemoryRetention.WpfBaselinePlaceholder;
        _filePath = filePath;
        _tempPath = filePath + ".tmp";
    }

    public string Name => "AiMemory";

    public bool Running => _store.Running;

    /// <summary>Typed outcome of the phase-3 load (SP-005 contract §5); quarantine/newer-schema surface as Degraded.</summary>
    public LoadOutcome? LastLoadOutcome => _store.LastLoadOutcome;

    /// <summary>Read-only inspection of the loaded document. Mutations MUST go through <see cref="Append"/>/<see cref="Clear"/> — direct writes bypass dirty tracking (SP-005 contract §5 rule 2).</summary>
    public AiMemoryDocument Current => _store.Current;

    /// <summary>Typed Degraded surface (the b2/b4 precedent): quarantine or newer-schema.</summary>
    public bool IsDegraded => _store.LastLoadOutcome?.IsDegraded == true;

    /// <summary>The last write's typed admission. Null before any write attempt.</summary>
    public AiMemoryWriteAdmission? LastWriteAdmission { get; private set; }

    /// <summary>The last explicit clear's typed outcome. Null before any clear.</summary>
    public AiMemoryClearOutcome? LastClearOutcome { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken) => _store.StartAsync(cancellationToken);

    public Task StopAsync() => _store.StopAsync();

    /// <summary>Teardown flush (SP-005 contract §11) — passthrough to the store's bounded flush.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    /// <summary>Reads up to <paramref name="maxTurns"/> most recent turns, oldest first. Snapshot copy under the store gate.</summary>
    public IReadOnlyList<AiMemoryTurn> ReadRecent(int maxTurns)
    {
        lock (_gate)
        {
            var turns = _store.Current.Turns;
            var start = Math.Max(0, turns.Count - Math.Max(0, maxTurns));
            return turns.Skip(start).ToList();
        }
    }

    /// <summary>
    /// Appends one turn — ONLY under the explicit granted consent state (contract §5
    /// rule 2, code-enforced at write admission). A denied write is a typed no-op
    /// (<see cref="AiMemoryWriteAdmission.ConsentDenied"/>), never silent, never throws.
    /// The pair-cap mechanism trims from the front (WPF LocalAiService.cs:148-152 outcome;
    /// value owner-pending, placeholder via <see cref="AiMemoryRetention.WpfBaselinePlaceholder"/>).
    /// </summary>
    public void Append(AiMemoryTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        lock (_gate)
        {
            if (_store.WritesDisabled)
            {
                LastWriteAdmission = AiMemoryWriteAdmission.WritesDisabled;
                return;
            }

            if (_consent() != AiMemoryConsent.Granted)
            {
                LastWriteAdmission = AiMemoryWriteAdmission.ConsentDenied;
                return;
            }

            var maxTurns = _retention.MaxPairs * 2;
            _store.Mutate(m =>
            {
                m.Turns.Add(turn);
                if (m.Turns.Count > maxTurns)
                {
                    m.Turns.RemoveRange(0, m.Turns.Count - maxTurns);
                }
            });
            _ = _store.Save();
            LastWriteAdmission = AiMemoryWriteAdmission.Admitted;
        }
    }

    /// <summary>
    /// The explicit-clear operation (contract §5 rule 1; WPF ClearHistory,
    /// LocalAiService.cs:227-232): empties in-memory state AND deletes the persisted
    /// document. The chained writer snapshots the EMPTY state and clears dirty before the
    /// delete, so neither a queued write nor the teardown flush can resurrect the file;
    /// a stray orphaned temp is deleted too (SP-005 crash recovery would otherwise adopt
    /// it on the next load). Serialized against <see cref="Append"/> under the store gate
    /// (a racing append mid-clear must not recreate the file with only its turn).
    /// Sync-block is deadlock-safe: SP-004 RunAsync bodies run on Task.Run with
    /// ConfigureAwait(false) (OperationRegistry.cs:216-221), no captured context.
    /// A newer-schema document is NEVER deleted (an older build never clobbers a newer
    /// one, SP-005 contract §4 rule 7) — the clear empties in-memory state and surfaces
    /// typed <see cref="AiMemoryClearOutcome.Degraded"/>. Quarantined .corrupt-*.json
    /// backups are SP-005 contract-preserved (§5 rule 3) and are never deleted by clear.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _store.Mutate(m => m.Turns.Clear());
            if (_store.WritesDisabled)
            {
                LastClearOutcome = AiMemoryClearOutcome.Degraded;
                return;
            }

            if (_store.Running)
            {
                _ = _store.SaveImmediate().GetAwaiter().GetResult();
            }

            TryDelete(_filePath);
            TryDelete(_tempPath);
            LastClearOutcome = AiMemoryClearOutcome.Cleared;
        }
    }

    /// <summary>Test/pipeline quiescence: awaits the store's chained writer so previously enqueued writes have finished (SP-005 contract §4 rule 5).</summary>
    public Task<OperationOutcome> SaveImmediate() => _store.SaveImmediate();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort delete (WPF discipline, LocalAiService.cs:230-231): the in-memory
            // state is already empty; a surviving file is flagged on the next load, never silent.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort discipline.
        }
    }
}
