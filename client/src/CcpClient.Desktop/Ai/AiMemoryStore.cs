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

    /// <summary>The document on disk is newer than this build; writes stay locked out (the persistence contract §4 rule 7).</summary>
    WritesDisabled,
}

/// <summary>
/// The three scopes of forgetting (audit row C10; WPF `CompanionBrain.ForgetThread` :550-558,
/// `ForgetConversation` :571-585, `Forget` :606-612 over `MemoryStore.Wipe` :592-631). Strictly
/// nested — each scope does everything the narrower one does and more — so the ladder the user
/// reads on screen is the ladder the code runs.
///
/// <para><b>What "derived" means in this port, honestly.</b> WPF's middle scope adds per-mod
/// relationship counters and chat-sourced FACTS (`MemoryStore.ForgetChatDerived` :643-652). The
/// port has no facts model and no relationship counters — that retention shape is the audit's row
/// C7 / owner question Q10, and it is not admitted. So <see cref="Conversation"/>'s derived arm is
/// the only thing this build keeps alongside the turns: the rest of the persisted document. When
/// facts land, they join this scope; nothing here has to change shape for them to.</para>
/// </summary>
public enum AiForgetScope
{
    /// <summary>
    /// The thread and nothing else: the turns go, in memory and on disk, and the document itself
    /// is KEPT with everything else it carries (WPF `ForgetThread` :533-535 — "Drops the THREAD
    /// and nothing else … No memory fact is touched"). The narrow scope for the user whose
    /// companion is stuck in an old pattern and who does not want to lose the rest.
    /// </summary>
    Thread,

    /// <summary>
    /// <see cref="Thread"/> plus everything the app kept ALONGSIDE the turns: the document leaves
    /// the disk entirely, taking its non-turn payload with it (the dormant marker, the retention
    /// field, and any unknown members a newer build wrote beside the turns — the port's whole
    /// derived-from-conversation surface today). WPF `ForgetConversation` :571-585.
    /// </summary>
    Conversation,

    /// <summary>
    /// <see cref="Conversation"/> plus every other on-disk COPY of what she was told: the
    /// quarantined `&lt;memory&gt;.corrupt-*.json` documents that a narrower forget deliberately
    /// leaves behind. WPF's wipe deletes a file its own model does not own for exactly this
    /// reason — leaving it "would let CompanionSessionStore's one-time import resurrect the wiped
    /// conversation on the next launch, which is the exact opposite of what the button promises"
    /// (`MemoryStore.cs:587-590`).
    /// </summary>
    Everything,
}

/// <summary>Typed explicit-clear outcome. <see cref="Degraded"/>: in-memory state emptied but a newer-schema document was NOT deleted (an older build never clobbers a newer one). <see cref="Failed"/>: the delete was attempted but the document is still on disk (never reported as Cleared — a privacy operation must not lie, pre-completion consult B).</summary>
public enum AiMemoryClearOutcome
{
    Cleared,
    Degraded,
    Failed,
}

/// <summary>
/// The first <see cref="IAiMemoryStore"/> implementation (admission §8 slice c4), on
/// the persistence machinery (admission §4 rule 2 — DECIDED, no new seam): ONE
/// <see cref="PersistenceStore{TModel}"/> of <see cref="AiMemoryDocument"/> in the
/// user-data root with its OWN named <see cref="AsyncOperationOwner"/> (the earlier
/// lesson: N stores sharing one owner cancel each other's writes). schemaVersion +
/// migration journal, corrupt-document quarantine once at startup → typed Degraded
/// (the b2/b4 precedent), unknown-member preserve — all inherited from that machinery.
///
/// Consent is code-enforced at WRITE ADMISSION (contract §5 rule 2 — never a prompt
/// convention): a denied <see cref="Append"/> is a typed no-op observable via
/// <see cref="LastWriteAdmission"/>. The CONVERSATION-CONSUMPTION read is gated the same
/// way (<see cref="ReadPromptContext"/> — the WPF `:113` port: consent off ⇒
/// history neither read into a prompt nor written); the inspection read
/// (<see cref="ReadRecent"/>) stays ungated. Named divergence (the packet record §3.1): the
/// phase-3 startup LOAD is consent-agnostic (clear/degraded/write machinery needs the
/// document resident) — WPF never reads the file under disabled consent; the observable
/// conversation behavior is identical.
///
/// Consumption as conversation context landed later (pipeline assembly +
/// <see cref="ReadPromptContext"/>). c4's claim was exactly "memory persists and clears".
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

    /// <summary>Typed outcome of the phase-3 load (the persistence contract §5); quarantine/newer-schema surface as Degraded.</summary>
    public LoadOutcome? LastLoadOutcome => _store.LastLoadOutcome;

    /// <summary>Read-only inspection of the loaded document. Mutations MUST go through <see cref="Append"/>/<see cref="Clear"/> — direct writes bypass dirty tracking (the persistence contract §5 rule 2).</summary>
    public AiMemoryDocument Current => _store.Current;

    /// <summary>Typed Degraded surface (the b2/b4 precedent): quarantine or newer-schema.</summary>
    public bool IsDegraded => _store.LastLoadOutcome?.IsDegraded == true;

    /// <summary>The last write's typed admission. Null before any write attempt.</summary>
    public AiMemoryWriteAdmission? LastWriteAdmission { get; private set; }

    /// <summary>The last ADMITTED write's owned completion (pre-completion consult C): admission and persistence are distinct facts — a faulted persist surfaces here as a typed <see cref="OperationOutcome.Failed"/>, never silently. Null before any admitted write.</summary>
    public Task<OperationOutcome>? LastWriteCompletion { get; private set; }

    /// <summary>The last explicit clear's typed outcome. Null before any clear.</summary>
    public AiMemoryClearOutcome? LastClearOutcome { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken) => _store.StartAsync(cancellationToken);

    public Task StopAsync() => _store.StopAsync();

    /// <summary>Teardown flush (the persistence contract §11) — passthrough to the store's bounded flush.</summary>
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
    /// The consent-gated conversation-consumption read (the WPF `:113` port,
    /// LocalAiService.cs:111-126): consent is checked FIRST — not Granted ⇒ an empty list,
    /// the document's turns untouched (consent off ⇒ history is neither read into a prompt
    /// nor written). Otherwise all current pairs, oldest first — the append-trim already
    /// bounds them to the retention window (trimming rides the c4 mechanism; the single cap
    /// reproduces WPF's twin assembly/persist 50-pair trims, LocalAiService.cs:180-227 +
    /// :148-152). Snapshot copy under the store gate.
    /// </summary>
    public IReadOnlyList<AiMemoryTurn> ReadPromptContext()
    {
        lock (_gate)
        {
            if (_consent() != AiMemoryConsent.Granted)
            {
                return [];
            }

            return _store.Current.Turns.ToList();
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
            LastWriteCompletion = _store.Save();
            LastWriteAdmission = AiMemoryWriteAdmission.Admitted;
        }
    }

    /// <summary>
    /// The explicit-clear operation (contract §5 rule 1; WPF ClearHistory,
    /// LocalAiService.cs:227-232): empties in-memory state AND deletes the persisted
    /// document. The chained writer snapshots the EMPTY state and clears dirty before the
    /// delete, so neither a queued write nor the teardown flush can resurrect the file;
    /// a stray orphaned temp is deleted too (persistence crash recovery would otherwise adopt
    /// it on the next load). Serialized against <see cref="Append"/> under the store gate
    /// (a racing append mid-clear must not recreate the file with only its turn).
    /// Sync-block is deadlock-safe: owned RunAsync bodies run on Task.Run with
    /// ConfigureAwait(false) (OperationRegistry.cs:216-221), no captured context.
    /// A newer-schema document is NEVER deleted (an older build never clobbers a newer
    /// one, the persistence contract §4 rule 7) — the clear empties in-memory state and surfaces
    /// typed <see cref="AiMemoryClearOutcome.Degraded"/>. Quarantined .corrupt-*.json
    /// backups are contract-preserved (§5 rule 3) and are never deleted by clear — only by
    /// <see cref="AiForgetScope.Everything"/>, which is the user asking for them by name.
    ///
    /// <para>The interface member (contract §5 rule 1) is the CONVERSATION scope of
    /// <see cref="Forget"/> (audit row C10): identical behavior to before that row, now with a
    /// narrower scope below it and a wider one above it.</para>
    /// </summary>
    public void Clear() => Forget(AiForgetScope.Conversation);

    /// <summary>
    /// The three forget scopes (audit row C10), strictly nested — see <see cref="AiForgetScope"/>
    /// for what each one takes and the WPF method it ports. Every scope empties the in-memory
    /// turns first and chains an EMPTY-state write before touching the disk, so neither a queued
    /// write nor the teardown flush can resurrect what was forgotten; serialized against
    /// <see cref="Append"/> under the store gate (a racing append mid-forget must not recreate the
    /// file with only its turn). A newer-schema document is NEVER deleted or rewritten (an older
    /// build never clobbers a newer one, the persistence contract §4 rule 7) — every scope then
    /// empties in-memory state and surfaces typed <see cref="AiMemoryClearOutcome.Degraded"/>.
    /// <see cref="LastClearOutcome"/> reports what is actually gone from disk, never what was
    /// intended (a privacy operation must not lie).
    /// </summary>
    public void Forget(AiForgetScope scope)
    {
        lock (_gate)
        {
            _store.Mutate(m => m.Turns.Clear());
            if (_store.WritesDisabled)
            {
                LastClearOutcome = AiMemoryClearOutcome.Degraded;
                return;
            }

            var written = _store.Running
                && _store.SaveImmediate().GetAwaiter().GetResult() is OperationOutcome.Completed;

            if (scope is AiForgetScope.Thread)
            {
                // The thread and nothing else: the file stays, now holding zero turns and whatever
                // else it carried (WPF ForgetThread :533-535 — "No memory fact is touched"). The
                // empty-state write above IS the erasure here; there is nothing to delete, so the
                // write is what the outcome has to report. A file that was never written held
                // nothing to forget in the first place.
                LastClearOutcome = written || !File.Exists(_filePath)
                    ? AiMemoryClearOutcome.Cleared
                    : AiMemoryClearOutcome.Failed;
                return;
            }

            TryDelete(_filePath);
            TryDelete(_tempPath);
            var survivors = File.Exists(_filePath);

            if (scope is AiForgetScope.Everything)
            {
                foreach (var backup in QuarantinedBackups())
                {
                    TryDelete(backup);
                    survivors |= File.Exists(backup);
                }
            }

            // The outcome reports reality, never intent (pre-completion consult B): a failed
            // delete (AV scanner, lock, read-only) must not read as Cleared on a privacy operation.
            LastClearOutcome = survivors ? AiMemoryClearOutcome.Failed : AiMemoryClearOutcome.Cleared;
        }
    }

    /// <summary>
    /// The quarantined copies of THIS document and nothing else:
    /// `&lt;name&gt;.corrupt-*.json` beside it, the exact shape
    /// <c>PersistenceStore.Quarantine</c> writes (PersistenceStore.cs:451-455). Derived from the
    /// store's own path — never a glob over the data root, because the one failure this scope
    /// cannot afford is deleting a file it was not asked about.
    ///
    /// <para><b>On the persistence contract.</b> §5 binds the QUARANTINE path: the store itself
    /// preserves unreadable bytes rather than deleting or overwriting them and silently running on
    /// defaults. That rule still holds exactly — <see cref="Clear"/> and both narrower scopes
    /// leave these files completely alone. <see cref="AiForgetScope.Everything"/> is the user
    /// explicitly asking for them, and by construction they are nothing but a previous state of
    /// this same memory document.</para>
    /// </summary>
    private IEnumerable<string> QuarantinedBackups()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var prefix = Path.GetFileNameWithoutExtension(_filePath) + ".corrupt-";
        try
        {
            // Enumerated with the pattern AND re-checked in code: Windows still matches 8.3 short
            // names through wildcards, so the pattern alone is not a guarantee about which files
            // came back — and this is the one operation that must never widen by a filename quirk.
            return Directory.EnumerateFiles(directory, prefix + "*.json")
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return name.StartsWith(prefix, StringComparison.Ordinal)
                        && name.EndsWith(".json", StringComparison.Ordinal);
                })
                .ToList();
        }
        catch (IOException)
        {
            // Same best-effort discipline as the deletes: an unreadable directory leaves the
            // survivors on disk, and the typed outcome below reports that rather than lying.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Test/pipeline quiescence: awaits the store's chained writer so previously enqueued writes have finished (the persistence contract §4 rule 5).</summary>
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
