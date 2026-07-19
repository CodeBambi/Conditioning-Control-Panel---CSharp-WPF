using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Persistence;

/// <summary>
/// Typed result of a store load (persistence-migration-contract §5). Expected outcomes are
/// values, never exceptions-as-control-flow. Anything other than <see cref="Loaded"/> means
/// the store runs on FLAGGED defaults — the flag is the outcome itself.
/// </summary>
public abstract record LoadOutcome
{
    private LoadOutcome() { }

    /// <summary>Document parsed, migrated, bound.</summary>
    public sealed record Loaded : LoadOutcome
    {
        public static readonly Loaded Instance = new();
    }

    /// <summary>No file exists; flagged fresh defaults. Not an error.</summary>
    public sealed record Missing : LoadOutcome
    {
        public static readonly Missing Instance = new();
    }

    /// <summary>
    /// The file existed but was unparseable or failed typed binding; the original bytes were
    /// moved aside preserved (never deleted, never overwritten). Defaults are flagged.
    /// Surfaces as Degraded (contract §5).
    /// </summary>
    public sealed record Quarantined(string BackupPath) : LoadOutcome;

    /// <summary>
    /// The document's schemaVersion is newer than this build knows. Flagged defaults,
    /// writes disabled — an older build never clobbers a newer document (contract §5).
    /// </summary>
    public sealed record NewerSchema(int FileVersion) : LoadOutcome;

    /// <summary>Typed Degraded surface (contract §5 rule 4): quarantine or newer-schema.</summary>
    public bool IsDegraded => this is Quarantined or NewerSchema;
}

/// <summary>
/// Failure-injection points for the atomic write path (contract §3 rule 2). Tests inject
/// throwing or reordering delegates; production uses the real file system.
/// </summary>
public sealed class AtomicWriteHooks
{
    /// <summary>Writes the full document to the temp path and flushes to disk (contract §3 rule 1).</summary>
    public Action<string, string> WriteTempFile { get; init; } = static (path, json) =>
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true); // before the writer's dispose closes the stream
        }
    };

    /// <summary>Atomically replaces the target with the temp file (contract §3 rule 1).</summary>
    public Action<string, string> Rename { get; init; } = static (tempPath, targetPath) =>
        File.Move(tempPath, targetPath, overwrite: true);
}

/// <summary>
/// The single persistence authority (persistence-migration-contract): schema-versioned
/// document, atomic temp+rename+flush writes, one serialized writer as SP-004 owned
/// operations, corruption quarantine with typed Degraded, unknown-member preservation,
/// migration journal, and whole-object replacement notification. An SP-003 background
/// participant: load runs in <see cref="StartAsync"/> (phase 3) before consumer
/// participants start; construction starts nothing.
/// </summary>
public sealed class PersistenceStore<TModel> : IBackgroundParticipant where TModel : class, new()
{
    /// <summary>Store-owned reserved DOM members (contract §1): never model members, never extension data.</summary>
    private const string SchemaVersionKey = "schemaVersion";
    private const string MigrationJournalKey = "migrationJournal";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly AsyncOperationOwner _owner;
    private readonly ILogSink _log;
    private readonly string _filePath;
    private readonly string _tempPath;
    private readonly IReadOnlyList<IDocumentMigration> _migrations;
    private readonly AtomicWriteHooks _hooks;

    private readonly object _mutationGate = new();
    private readonly object _writeGate = new();
    private TModel _current = new();
    private List<string> _journal = [];
    private bool _dirty;
    private int _mutationVersion;
    private Task<OperationOutcome> _tail = Task.FromResult<OperationOutcome>(OperationOutcome.Completed.Instance);

    public PersistenceStore(
        AsyncOperationOwner owner,
        ILogSink log,
        string filePath,
        int currentSchemaVersion,
        IReadOnlyList<IDocumentMigration>? migrations = null,
        AtomicWriteHooks? hooks = null)
    {
        _owner = owner;
        _log = log;
        _filePath = filePath;
        _tempPath = filePath + ".tmp";
        CurrentSchemaVersion = currentSchemaVersion;
        _migrations = migrations ?? [];
        _hooks = hooks ?? new AtomicWriteHooks();
    }

    public string Name => "Persistence";

    public bool Running { get; private set; }

    /// <summary>The schema version this build writes (contract §1).</summary>
    public int CurrentSchemaVersion { get; }

    /// <summary>The current settings object. Mutations MUST go through <see cref="Mutate"/>/<see cref="Replace"/> — direct property writes bypass dirty tracking (contract §5 rule 2).</summary>
    public TModel Current => _current;

    /// <summary>Typed outcome of the phase-3 load (contract §5). Null until started.</summary>
    public LoadOutcome? LastLoadOutcome { get; private set; }

    /// <summary>Writes disabled after a newer-than-app load (contract §4 rule 7).</summary>
    public bool WritesDisabled { get; private set; }

    /// <summary>Dirty only by explicit mutation or a migration write-through — never by loading defaults (contract §5 rule 2).</summary>
    public bool IsDirty
    {
        get { lock (_mutationGate) { return _dirty; } }
    }

    /// <summary>
    /// Raised on whole-object replacement BEFORE persisting (contract §8): synchronously on
    /// the caller's context, inside the store lock. Handlers must not touch UI directly —
    /// UI projection goes through IUiDispatch.Post with a generation check (SP-004 §5.5).
    /// Throwing handlers are isolated per-handler.
    /// </summary>
    public event Action? SettingsReplaced;

    /// <summary>Phase-3 start: begin the operation generation, then load (contract §5).</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Running)
        {
            return Task.CompletedTask;
        }

        Running = true;
        _owner.Begin();
        Load();
        return Task.CompletedTask;
    }

    /// <summary>Idempotent stop: cancels the operation generation (SP-003 §5.3).</summary>
    public Task StopAsync()
    {
        if (!Running)
        {
            return Task.CompletedTask;
        }

        Running = false;
        _owner.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Apply an explicit mutation and mark dirty (contract §5 rule 2).</summary>
    public void Mutate(Action<TModel> mutation)
    {
        lock (_mutationGate)
        {
            mutation(_current);
            _dirty = true;
            _mutationVersion++;
        }
    }

    /// <summary>
    /// Whole-object replacement (contract §8): swaps Current, marks dirty, raises
    /// <see cref="SettingsReplaced"/> BEFORE the save is enqueued, then persists. Returns
    /// the owned completion of the enqueued write.
    /// </summary>
    public Task<OperationOutcome> Replace(TModel newModel)
    {
        ArgumentNullException.ThrowIfNull(newModel);
        lock (_mutationGate)
        {
            _current = newModel;
            _dirty = true;
            _mutationVersion++;
            foreach (Action handler in SettingsReplaced?.GetInvocationList() ?? [])
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    _log.Log($"persistence: SettingsReplaced handler failed (isolated): {ex.Message}");
                }
            }
        }

        return Save();
    }

    /// <summary>
    /// Enqueues a serialized write of the latest state (contract §4). The write chains onto
    /// the previous write's completion — concurrent saves never interleave. Post-teardown
    /// saves terminate typed Cancelled, never fault, never silently succeed (contract §11 rule 5).
    /// </summary>
    public Task<OperationOutcome> Save()
    {
        lock (_writeGate)
        {
            if (WritesDisabled)
            {
                return Task.FromResult<OperationOutcome>(new OperationOutcome.Failed(
                    InitFailureKind.Degraded, "writes disabled: document schema is newer than this build"));
            }

            var previous = _tail;
            _tail = _owner.RunAsync("settings-write", async token =>
            {
                // Chained serialization (contract §4 rule 3): previous completions never fault.
                await previous.ConfigureAwait(false);
                return WriteOnce(token);
            }, ClassifyWriteFault);
            return _tail;
        }
    }

    /// <summary>Enqueues a write and awaits quiescence: when this returns, every previously enqueued write has finished (contract §4 rule 5).</summary>
    public async Task<OperationOutcome> SaveImmediate() => await Save().ConfigureAwait(false);

    /// <summary>
    /// Teardown flush (contract §11): no-op when never started, clean, or writes-disabled;
    /// otherwise enqueues the final write and awaits quiescence with a bounded wait (backstop
    /// only — the chained writer is the mechanism). Never throws; outcomes are logged.
    /// </summary>
    public async Task FlushAsync(TimeSpan boundedWait)
    {
        Task<OperationOutcome>? tail = null;
        lock (_writeGate)
        {
            if (Running && !WritesDisabled && IsDirty)
            {
                tail = Save();
            }
        }

        if (tail is null)
        {
            return;
        }

        var completed = await Task.WhenAny(tail, Task.Delay(boundedWait)).ConfigureAwait(false);
        if (completed != tail)
        {
            _log.Log("persistence: teardown flush exceeded its bounded wait; shutdown continues");
            return;
        }

        if (tail.Result is OperationOutcome.Failed failure)
        {
            _log.Log($"persistence: teardown flush failed ({failure.Kind}): {failure.Reason}");
        }
    }

    private void Load()
    {
        try
        {
            // Crash recovery (contract §5 rule 1): adopt an orphaned temp (interrupted
            // save); delete a stale temp beside a valid main.
            if (File.Exists(_tempPath) && !File.Exists(_filePath))
            {
                _log.Log("persistence: recovering settings from interrupted save (temp file)");
                File.Move(_tempPath, _filePath);
            }
            else if (File.Exists(_tempPath))
            {
                try
                {
                    File.Delete(_tempPath);
                }
                catch (Exception ex)
                {
                    _log.Log($"persistence: could not delete stale temp file: {ex.Message}");
                }
            }

            if (!File.Exists(_filePath))
            {
                SetDefaults();
                LastLoadOutcome = LoadOutcome.Missing.Instance;
                return;
            }

            var text = File.ReadAllText(_filePath);
            JsonObject? document;
            try
            {
                document = JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException)
            {
                document = null;
            }

            if (document is null)
            {
                Quarantine();
                SetDefaults();
                return;
            }

            var fileVersion = document[SchemaVersionKey]?.GetValue<int>() ?? 0;
            if (fileVersion > CurrentSchemaVersion)
            {
                WritesDisabled = true;
                LastLoadOutcome = new LoadOutcome.NewerSchema(fileVersion);
                SetDefaults();
                return;
            }

            _journal = document[MigrationJournalKey] is JsonArray journalNode
                ? journalNode.Select(entry => entry!.GetValue<string>()).ToList()
                : [];
            // Reserved store-owned members are not model members and must not land in extension data.
            document.Remove(SchemaVersionKey);
            document.Remove(MigrationJournalKey);

            // Contract §7 rule 2: run when not journaled AND target exceeds the document version.
            var migrated = false;
            foreach (var migration in _migrations.OrderBy(m => m.TargetVersion))
            {
                if (migration.TargetVersion > fileVersion && !_journal.Contains(migration.Id))
                {
                    migration.Migrate(document);
                    _journal.Add(migration.Id);
                    migrated = true;
                }
            }

            TModel? model = null;
            try
            {
                model = document.Deserialize<TModel>(JsonOptions);
            }
            catch (JsonException)
            {
                // Bind failure → quarantine + Degraded (contract §2 tolerance stack step 3).
            }

            if (model is null)
            {
                Quarantine();
                SetDefaults();
                return;
            }

            _current = model;
            LastLoadOutcome = LoadOutcome.Loaded.Instance;
            if (migrated)
            {
                // Write-through of version+journal with the document (contract §1 rule 3):
                // a real document was migrated — this is not the banned defaults auto-save.
                lock (_mutationGate)
                {
                    _dirty = true;
                    _mutationVersion++;
                }
            }
        }
        catch (Exception ex)
        {
            // Unexpected I/O failure: preserve whatever exists, run on flagged defaults,
            // surface the typed Degraded outcome. Never silent defaults (contract §5).
            _log.Log($"persistence: load failed: {ex.Message}");
            Quarantine();
            SetDefaults();
        }
    }

    /// <summary>Move the unreadable original aside preserved; never delete, never overwrite (contract §5 rule 3).</summary>
    private void Quarantine()
    {
        if (!File.Exists(_filePath))
        {
            LastLoadOutcome = new LoadOutcome.Quarantined(string.Empty);
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = _filePath.Replace(".json", $".corrupt-{stamp}.json", StringComparison.Ordinal);
        for (var suffix = 2; File.Exists(backupPath); suffix++)
        {
            backupPath = _filePath.Replace(".json", $".corrupt-{stamp}-{suffix}.json", StringComparison.Ordinal);
        }

        try
        {
            File.Move(_filePath, backupPath);
            _log.Log($"persistence: settings file unreadable; preserved at {backupPath} before falling back to flagged defaults");
        }
        catch (Exception ex)
        {
            _log.Log($"persistence: could not quarantine unreadable file: {ex.Message}");
            backupPath = string.Empty;
        }

        LastLoadOutcome = new LoadOutcome.Quarantined(backupPath);
    }

    /// <summary>Flagged defaults: the flag is <see cref="LastLoadOutcome"/>; dirty is NOT set (contract §5 rule 2).</summary>
    private void SetDefaults()
    {
        lock (_mutationGate)
        {
            _current = new TModel();
            _dirty = false;
        }
    }

    private OperationOutcome WriteOnce(CancellationToken token)
    {
        // The generation token is observed before I/O, never passed into it (contract §4
        // rule 6): a cancel arriving mid-write must not abort the rename.
        if (token.IsCancellationRequested)
        {
            return OperationOutcome.Cancelled.Instance;
        }

        string json;
        int versionAtSerialize;
        lock (_mutationGate)
        {
            json = BuildDocumentJson();
            versionAtSerialize = _mutationVersion;
        }

        _hooks.WriteTempFile(_tempPath, json);
        _hooks.Rename(_tempPath, _filePath);

        lock (_mutationGate)
        {
            if (_mutationVersion == versionAtSerialize)
            {
                _dirty = false;
            }
        }

        return OperationOutcome.Completed.Instance;
    }

    /// <summary>Serializes the latest model and stamps store-owned version + journal (contract §1 rule 3).</summary>
    private string BuildDocumentJson()
    {
        var document = JsonSerializer.SerializeToNode(_current, JsonOptions)!.AsObject();
        document[SchemaVersionKey] = CurrentSchemaVersion;
        var journal = new JsonArray();
        foreach (var id in _journal)
        {
            journal.Add(id);
        }

        document[MigrationJournalKey] = journal;
        return document.ToJsonString(JsonOptions);
    }

    /// <summary>Write faults: I/O-style failures are Recoverable (store alive, in-memory state intact); anything else stays Fatal (SP-004 §4).</summary>
    private static InitFailureKind ClassifyWriteFault(Exception ex) =>
        ex is IOException or UnauthorizedAccessException ? InitFailureKind.Recoverable : InitFailureKind.Fatal;
}

/// <summary>
/// Secret-exclusion seam (persistence-migration-contract §9): the settings file never
/// carries secrets; a persisted model stores an opaque name, never a value. DECLARED ONLY —
/// the first row with a secret consumer implements this against a platform store and
/// records the admission. No implementation exists in this slice.
/// </summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}
