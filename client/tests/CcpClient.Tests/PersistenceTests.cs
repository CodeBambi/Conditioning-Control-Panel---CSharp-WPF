using System.Text.Json.Nodes;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Persistence-migration-contract conformance: quarantine with preserved bytes, crash
/// recovery, unknown-member round-trip, migration idempotence, serialized concurrent
/// writes, replacement notification, flagged-defaults discipline, newer-schema write
/// lockout, and write-failure typed outcomes. Failure injection goes through
/// <see cref="AtomicWriteHooks"/> — real corruption is never manufactured.
/// </summary>
public class PersistenceTests
{
    private const int DemoSchemaVersion = 1;

    [Fact]
    public async Task CorruptFile_Quarantined_OriginalBytesPreserved_FlaggedDefaults()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        const string garbage = "this is not json {{{ truncated";
        File.WriteAllText(path, garbage);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        var quarantined = Assert.IsType<LoadOutcome.Quarantined>(store.LastLoadOutcome);
        Assert.True(store.LastLoadOutcome!.IsDegraded);
        // Original bytes preserved, never deleted (contract §5): moved aside, intact.
        Assert.True(File.Exists(quarantined.BackupPath));
        Assert.Equal(garbage, File.ReadAllText(quarantined.BackupPath));
        Assert.False(File.Exists(path)); // moved, not copied
        // Flagged defaults (the flag is the outcome), never silent.
        Assert.Equal("hello", store.Current.Greeting);

        // A later save writes a clean file; the quarantined original survives untouched.
        store.Mutate(m => m.Greeting = "after-quarantine");
        var outcome = await store.SaveImmediate();
        Assert.IsType<OperationOutcome.Completed>(outcome);
        Assert.True(File.Exists(path));
        Assert.Equal(garbage, File.ReadAllText(quarantined.BackupPath));
    }

    [Fact]
    public async Task StructurallyValidButUnbindable_Quarantined_NotSilentDefaults()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "greeting": 12345 }"""); // number where string belongs

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Quarantined>(store.LastLoadOutcome);
        Assert.True(store.LastLoadOutcome!.IsDegraded);
    }

    [Fact]
    public async Task CrashMidRename_OrphanedTemp_AdoptedOnNextLoad()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        // Simulated crash after temp write, before rename: temp exists, main does not.
        File.WriteAllText(path + ".tmp", """{ "schemaVersion": 1, "greeting": "recovered" }""");

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal("recovered", store.Current.Greeting);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task StaleTemp_BesideValidMain_Deleted()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "greeting": "main" }""");
        File.WriteAllText(path + ".tmp", "partial garbage");

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal("main", store.Current.Greeting);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task UnknownMember_RoundTrips_Verbatim_PreserveNeverStrip()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 1, "migrationJournal": [], "greeting": "hi",
              "futureFeature": { "enabled": true, "level": 3 } }
            """);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);

        var outcome = await store.SaveImmediate();
        Assert.IsType<OperationOutcome.Completed>(outcome);

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.NotNull(document["futureFeature"]);
        Assert.True(document["futureFeature"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(3, document["futureFeature"]!["level"]!.GetValue<int>());
        // Reserved store-owned members did not leak into extension data round-trips.
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Migration_RunsOnce_Idempotent_OneJournalEntry()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 0, "greeting_text": "migrated!" }"""); // v0 document

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal("migrated!", store.Current.Greeting);
        Assert.True(store.IsDirty); // migration write-through of version+journal (contract §1 rule 3)

        await store.SaveImmediate();
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
        Assert.Null(document["greeting_text"]);
        Assert.Equal("migrated!", document["greeting"]!.GetValue<string>());
        Assert.Equal(new[] { "demo.v0-to-v1" }, document["migrationJournal"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());

        // Second load: migration must NOT re-run (journaled + version current) — same
        // document, still exactly one journal entry, no write-through dirty.
        var second = NewStore(path);
        await second.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Loaded>(second.LastLoadOutcome);
        Assert.False(second.IsDirty);
        await second.SaveImmediate();
        var again = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(new[] { "demo.v0-to-v1" }, again["migrationJournal"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal("migrated!", again["greeting"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentWrites_Serialized_NoInterleave_LatestStateWins()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            store.Mutate(m => m.Volume = i);
            await store.Save();
        })).ToArray();
        await Task.WhenAll(tasks);
        await store.SaveImmediate();

        // The final file parses cleanly (no interleaved/partial content) and carries the
        // latest state: the last chained write executes after every mutation (contract §4).
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(store.Current.Volume, document["volume"]!.GetValue<int>());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Replace_RaisesNotification_BeforePersisting_IsolatesThrowingHandlers()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var log = new ListLogSink();
        var store = NewStore(path, log);
        await store.StartAsync(CancellationToken.None);

        var calls = new List<string>();
        store.SettingsReplaced += () =>
        {
            // Delivery context (contract §8): raised BEFORE the save is enqueued — the new
            // value must not be on disk yet (nothing is, on a fresh install).
            Assert.False(File.Exists(path) && File.ReadAllText(path).Contains("replaced"));
            calls.Add("first");
        };
        store.SettingsReplaced += () => throw new InvalidOperationException("handler boom");
        store.SettingsReplaced += () => calls.Add("second");

        var outcome = await store.Replace(new DemoSettings { Greeting = "replaced" });

        Assert.IsType<OperationOutcome.Completed>(outcome);
        Assert.Equal(new[] { "first", "second" }, calls); // throwing handler isolated
        Assert.Contains(log.Messages, m => m.Contains("SettingsReplaced handler failed"));
        Assert.Equal("replaced", store.Current.Greeting);
        Assert.Contains("replaced", File.ReadAllText(path)); // persisted after notification
    }

    [Fact]
    public async Task Defaults_NeverAutoSaved_LoadingDefaultsLeavesNoFile()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Missing>(store.LastLoadOutcome);
        Assert.False(store.IsDirty);

        await store.FlushAsync(TimeSpan.FromSeconds(1)); // teardown shape: clean → no-op

        Assert.False(File.Exists(path)); // contract §5 rule 2: no defaults auto-save
    }

    [Fact]
    public async Task NewerSchema_WritesDisabled_OlderBuildNeverClobbersNewerFile()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        const string future = """{ "schemaVersion": 99, "greeting": "from-the-future" }""";
        File.WriteAllText(path, future);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        var newer = Assert.IsType<LoadOutcome.NewerSchema>(store.LastLoadOutcome);
        Assert.Equal(99, newer.FileVersion);
        Assert.True(store.LastLoadOutcome!.IsDegraded);
        Assert.True(store.WritesDisabled);

        var outcome = await store.Save();
        var failed = Assert.IsType<OperationOutcome.Failed>(outcome);
        Assert.Equal(InitFailureKind.Degraded, failed.Kind);

        Assert.Equal(future, File.ReadAllText(path)); // untouched
    }

    [Fact]
    public async Task WriteFailure_TypedRecoverableOutcome_MainFileUntouched()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var hooks = new AtomicWriteHooks
        {
            WriteTempFile = (_, _) => throw new IOException("injected disk failure"),
        };
        var store = NewStore(path, hooks: hooks);
        await store.StartAsync(CancellationToken.None);

        store.Mutate(m => m.Greeting = "doomed");
        var outcome = await store.SaveImmediate();

        var failed = Assert.IsType<OperationOutcome.Failed>(outcome);
        Assert.Equal(InitFailureKind.Recoverable, failed.Kind);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CrashMidWrite_PartialTemp_AdoptedThenQuarantined_BytesPreserved()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        // Simulated crash mid-WRITE (before rename): the orphaned temp is partial. Adoption
        // must surface it through the quarantine path, never silently default over it.
        const string partial = "{ \"schemaVersion\": 1, \"greeting\": \"cut-off";
        File.WriteAllText(path + ".tmp", partial);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        var quarantined = Assert.IsType<LoadOutcome.Quarantined>(store.LastLoadOutcome);
        Assert.True(File.Exists(quarantined.BackupPath));
        Assert.Equal(partial, File.ReadAllText(quarantined.BackupPath));
        Assert.Equal("hello", store.Current.Greeting); // flagged defaults
    }

    [Fact]
    public async Task Save_AfterTeardown_CompletesTypedCancelled_NeverFaults()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Mutate(m => m.Greeting = "before-stop");
        await store.SaveImmediate();

        await store.StopAsync(); // generation cancelled — the teardown shape

        // Contract §11 rule 5: a post-teardown save terminates typed Cancelled — it does
        // not fault and does not silently succeed.
        var outcome = await store.Save();
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
    }

    private static PersistenceStore<DemoSettings> NewStore(
        string path, ListLogSink? log = null, AtomicWriteHooks? hooks = null) =>
        new(new OperationRegistry().OwnerFor("Persistence"),
            log ?? new ListLogSink(),
            path,
            DemoSchemaVersion,
            [new DemoMigrationV0ToV1()],
            hooks);

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-persist-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue.
            }
        }
    }
}
