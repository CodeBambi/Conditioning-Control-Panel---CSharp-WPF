using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-040 slice c4 store tests (ai-companion-admission.md §4, §8 c4): the first
/// IAiMemoryStore on SP-005 machinery — round-trips, corrupt-document quarantine →
/// typed Degraded once at startup (b2 precedent), schemaVersion + migration journal,
/// unknown-member preserve, consent-gated writes (typed no-op on denial, never silent,
/// never throws), pair-cap MECHANISM (value owner-pending), both-answers schema shape
/// (disable flag + retention policy orthogonal; dormant marker representable from v1),
/// and the explicit-clear operation with file-content proof.
/// </summary>
public class AiMemoryStoreTests
{
    private static readonly AiMemoryTurn UserTurn = new(AiMemoryRole.User, "user-text");
    private static readonly AiMemoryTurn AssistantTurn = new(AiMemoryRole.Assistant, "assistant-text");

    [Fact]
    public async Task RoundTrip_PersistReload_TurnsSurviveOldestFirst()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        store.Append(UserTurn);
        store.Append(AssistantTurn);
        Assert.Equal(AiMemoryWriteAdmission.Admitted, store.LastWriteAdmission);
        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
        // Admitted ≠ persisted (consult C): the disk result is a separately observable typed outcome.
        Assert.NotNull(store.LastWriteCompletion);
        Assert.IsType<OperationOutcome.Completed>(await store.LastWriteCompletion);

        var reloaded = NewStore(path);
        await reloaded.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Loaded>(reloaded.LastLoadOutcome);
        Assert.Equal([UserTurn, AssistantTurn], reloaded.ReadRecent(10));
        Assert.Equal([AssistantTurn], reloaded.ReadRecent(1)); // most recent, oldest first
    }

    [Fact]
    public async Task Missing_OnFirstLoad_EmptyAndNoFileCreated()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        Assert.IsType<LoadOutcome.Missing>(store.LastLoadOutcome);
        Assert.False(store.IsDegraded);
        Assert.Empty(store.ReadRecent(10));
        Assert.False(File.Exists(path)); // empty memory stays file-less (SP-024 empty-slot discipline)
    }

    [Fact]
    public async Task CorruptDocument_QuarantinedOnceAtStartup_TypedDegraded_BytesPreserved()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        const string garbage = "not a memory document {{{";
        File.WriteAllText(path, garbage);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);

        var quarantined = Assert.IsType<LoadOutcome.Quarantined>(store.LastLoadOutcome);
        Assert.True(store.IsDegraded);
        Assert.True(File.Exists(quarantined.BackupPath));
        Assert.Equal(garbage, File.ReadAllText(quarantined.BackupPath)); // preserved, never deleted
        Assert.Empty(store.ReadRecent(10)); // flagged defaults, never silent

        // Recovery: a later consented write produces a clean file; the backup survives.
        store.Append(UserTurn);
        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
        Assert.True(File.Exists(path));
        Assert.Equal(garbage, File.ReadAllText(quarantined.BackupPath));
    }

    [Fact]
    public async Task Document_CarriesSchemaVersionAndMigrationJournal()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Append(UserTurn);
        await store.SaveImmediate();

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(AiMemoryDocument.CurrentSchemaVersion, document["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(document["migrationJournal"] as JsonArray);
        // No policy value on disk (record.md §3.1 #1): the retention field is null, never a persisted 50.
        Assert.Null(document["retentionMaxPairs"]);
        Assert.Equal("user-text", document["turns"]!.AsArray()[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownMember_Preserved_AcrossLoadAndSave()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        File.WriteAllText(path, """
            { "schemaVersion": 1, "migrationJournal": [], "turns": [], "futureMember": { "nested": 42 } }
            """);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);

        store.Append(UserTurn);
        await store.SaveImmediate();

        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(42, document["futureMember"]!["nested"]!.GetValue<int>());
    }

    [Fact]
    public async Task ConsentDenied_WriteIsTypedNoOp_NothingOnDisk_NeverThrows()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path, consent: () => AiMemoryConsent.Denied);
        await store.StartAsync(CancellationToken.None);

        store.Append(UserTurn); // denied: typed no-op

        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, store.LastWriteAdmission);
        Assert.Empty(store.ReadRecent(10));
        await store.FlushAsync(TimeSpan.FromSeconds(5)); // teardown must not persist a denied write
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task PlaceholderDefaultConsent_IsDenied_ConservativePosture()
    {
        using var dir = new TempDir();
        // Constructed WITHOUT the consent seam: exercises the store's own placeholder default.
        var store = new AiMemoryStore(new OperationRegistry().OwnerFor("AiMemory"), new ListLogSink(), dir.Path(AiMemoryStore.FileName));
        await store.StartAsync(CancellationToken.None);

        store.Append(UserTurn);

        // Placeholder default = Denied (record.md §3.1; WPF baseline FACT default true,
        // CompanionPromptSettings.cs:120 — deliberately stricter pending the owner, §9.2 #3).
        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, store.LastWriteAdmission);
        Assert.Empty(store.ReadRecent(10));
    }

    [Fact]
    public async Task ConsentRevokedAtRuntime_LaterWritesDenied_EarlierStateKept()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var consent = AiMemoryConsent.Granted;
        var store = NewStore(path, consent: () => consent);
        await store.StartAsync(CancellationToken.None);

        store.Append(UserTurn);
        Assert.Equal(AiMemoryWriteAdmission.Admitted, store.LastWriteAdmission);
        consent = AiMemoryConsent.Denied;
        store.Append(AssistantTurn);

        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, store.LastWriteAdmission);
        Assert.Equal([UserTurn], store.ReadRecent(10)); // the denied turn never entered memory
    }

    [Fact]
    public async Task PairCapMechanism_TrimsOldestFromFront_ValueInjected()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        // Mechanism proof with a test value (the owner-pending VALUE stays placeholder;
        // WPF baseline FACT = 50 pairs, LocalAiService.cs:92).
        var store = NewStore(path, retention: new AiMemoryRetention(MaxPairs: 1));
        await store.StartAsync(CancellationToken.None);

        store.Append(new AiMemoryTurn(AiMemoryRole.User, "old-user"));
        store.Append(new AiMemoryTurn(AiMemoryRole.Assistant, "old-assistant"));
        store.Append(new AiMemoryTurn(AiMemoryRole.User, "new-user"));
        store.Append(new AiMemoryTurn(AiMemoryRole.Assistant, "new-assistant"));
        await store.SaveImmediate();

        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "new-user"), new AiMemoryTurn(AiMemoryRole.Assistant, "new-assistant")],
            store.ReadRecent(10));

        var reloaded = NewStore(path, retention: new AiMemoryRetention(MaxPairs: 1));
        await reloaded.StartAsync(CancellationToken.None);
        Assert.Equal(2, reloaded.ReadRecent(10).Count); // the cap holds on disk too
    }

    [Fact]
    public async Task BothAnswersSchemaShape_DisableDormantAndDelete_RoundTripWithoutSchemaChange()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var dormantStamp = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        // Answer 1 — disable = retain-dormant: flag + dormant marker set, turns retained.
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            migrationJournal = Array.Empty<string>(),
            turns = new[] { new { role = "User", text = "kept" } },
            disabled = true,
            retentionMaxPairs = (int?)null,
            dormantSinceUtc = dormantStamp,
        }));

        var dormant = NewStore(path);
        await dormant.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Loaded>(dormant.LastLoadOutcome);
        Assert.True(dormant.Current.Disabled);
        Assert.Equal(dormantStamp, dormant.Current.DormantSinceUtc);
        Assert.Null(dormant.Current.RetentionMaxPairs);
        Assert.Single(dormant.ReadRecent(10)); // turns RETAINED under the dormant answer

        // Answer 2 — disable = delete: flag set, turns emptied. Same schema version.
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            migrationJournal = Array.Empty<string>(),
            turns = Array.Empty<object>(),
            disabled = true,
        }));

        var deleted = NewStore(path);
        await deleted.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Loaded>(deleted.LastLoadOutcome);
        Assert.True(deleted.Current.Disabled);
        Assert.Empty(deleted.ReadRecent(10));
    }

    [Fact]
    public async Task ExplicitClear_EmptiesStateAndDeletesDocument_FileContentProof()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Append(UserTurn);
        store.Append(AssistantTurn);
        await store.SaveImmediate();
        Assert.True(File.Exists(path));
        Assert.Contains("user-text", File.ReadAllText(path)); // content was on disk

        store.Clear();

        // File-content proof: document bytes GONE from disk, in-memory state empty.
        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp")); // orphaned-temp resurrection path closed
        Assert.Empty(store.ReadRecent(10));

        // Teardown flush cannot resurrect a cleared document (dirty was cleared by the empty write).
        await store.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(path));

        // A subsequent read on a FRESH store yields the empty state — never a resurrected document.
        var reloaded = NewStore(path);
        await reloaded.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.Missing>(reloaded.LastLoadOutcome);
        Assert.Empty(reloaded.ReadRecent(10));
    }

    [Fact]
    public async Task ExplicitClear_ThenAppend_Repersists_PointInTimeSemantics()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Append(UserTurn);
        await store.SaveImmediate();
        store.Clear();
        Assert.False(File.Exists(path));

        store.Append(AssistantTurn); // memory re-fills after clear (WPF point-in-time clear)
        await store.SaveImmediate();

        Assert.True(File.Exists(path));
        Assert.Equal([AssistantTurn], store.ReadRecent(10));
    }

    [Fact]
    public async Task NewerSchema_ClearKeepsDocument_EmptiesInMemory_TypedDegraded()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var newer = """{ "schemaVersion": 99, "migrationJournal": [], "turns": [ { "role": "User", "text": "newer-build-data" } ] }""";
        File.WriteAllText(path, newer);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.NewerSchema>(store.LastLoadOutcome);
        Assert.True(store.IsDegraded);

        store.Append(UserTurn); // writes locked out (SP-005 contract §4 rule 7)
        Assert.Equal(AiMemoryWriteAdmission.WritesDisabled, store.LastWriteAdmission);

        store.Clear();

        // An older build NEVER clobbers a newer document — the file survives; in-memory state emptied.
        Assert.Equal(AiMemoryClearOutcome.Degraded, store.LastClearOutcome);
        Assert.True(File.Exists(path));
        Assert.Contains("newer-build-data", File.ReadAllText(path));
        Assert.Empty(store.ReadRecent(10));
    }

    private static AiMemoryStore NewStore(
        string path, Func<AiMemoryConsent>? consent = null, AiMemoryRetention? retention = null) =>
        new(new OperationRegistry().OwnerFor("AiMemory"),
            new ListLogSink(),
            path,
            consent ?? (() => AiMemoryConsent.Granted),
            retention);

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-aimemory-" + Guid.NewGuid().ToString("N"));

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
