using System.Text.Json.Nodes;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// C10 (her-room-divergence-audit.md row C10, MERGE): the three forget scopes, and the proof that
/// they are three. Upstream has three buttons because they delete different things —
/// <c>CompanionBrain.ForgetThread</c> (Services/Companion/Brain/CompanionBrain.cs:550-558) drops
/// the thread and NOTHING else, <c>ForgetConversation</c> (:571-585) takes what was derived from
/// it too, and <c>Forget</c> (:606-612) over <c>MemoryStore.Wipe</c>
/// (Services/Companion/Brain/MemoryStore.cs:592-631) takes every copy so nothing can bring the
/// conversation back (:587-590).
///
/// <para><b>Why every fact here reads the FILE.</b> An over-broad delete is the worst failure this
/// row can produce, and it is invisible from in-memory state alone: a scope that quietly deleted
/// one file too many would still report Cleared and still read empty. So each scope is pinned by
/// what is left on disk afterwards, and the last fact pins what is left BESIDE the document.</para>
///
/// <para><b>Named limit.</b> The port has no facts store and no relationship counters (audit row
/// C7 / owner question Q10 — a new retention shape, not admitted), so
/// <see cref="AiForgetScope.Conversation"/>'s derived arm is the document's non-turn payload and
/// nothing more. That is the whole derived surface this build has.</para>
/// </summary>
public class AiMemoryForgetScopeTests
{
    private static readonly AiMemoryTurn UserTurn = new(AiMemoryRole.User, "remember-me-user");
    private static readonly AiMemoryTurn AssistantTurn = new(AiMemoryRole.Assistant, "remember-me-assistant");

    // ---- the ladder, one rung at a time, each proved by what SURVIVES ----

    [Fact]
    public async Task Thread_ForgetsTheConversation_AndLeavesTheDocumentItself()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var quarantine = dir.StageQuarantine();
        var store = await StartedStoreWithTurnsAsync(path);

        store.Forget(AiForgetScope.Thread);

        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.Empty(store.ReadRecent(10));

        // The thread is gone from the file, and the file is still there holding everything else —
        // WPF ForgetThread's promise verbatim: "Drops the THREAD and nothing else … No memory
        // fact is touched" (CompanionBrain.cs:533-535).
        Assert.True(File.Exists(path));
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Empty(document["turns"]!.AsArray());
        Assert.DoesNotContain("remember-me-user", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(42, document["futureMember"]!["nested"]!.GetValue<int>());

        // And the next launch agrees — this is not an in-memory illusion.
        var reloaded = NewStore(path);
        await reloaded.StartAsync(CancellationToken.None);
        Assert.Empty(reloaded.ReadRecent(10));

        // Everything the WIDER scopes take is still here.
        Assert.True(File.Exists(quarantine));
    }

    [Fact]
    public async Task Conversation_TakesTheDocument_AndLeavesEveryOtherCopyOfIt()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var quarantine = dir.StageQuarantine();
        var store = await StartedStoreWithTurnsAsync(path);

        store.Forget(AiForgetScope.Conversation);

        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.Empty(store.ReadRecent(10));

        // Wider than Thread: the document itself goes, with its non-turn payload.
        Assert.False(File.Exists(path));

        // Narrower than Everything: the quarantined copy is exactly what this scope leaves, and
        // its bytes are untouched (persistence contract §5 — the store never deletes them).
        Assert.True(File.Exists(quarantine));
        Assert.Equal(TempDir.QuarantineBytes, File.ReadAllText(quarantine));
    }

    [Fact]
    public async Task Everything_TakesTheQuarantinedCopiesToo_SoNothingCanBringItBack()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var first = dir.StageQuarantine();
        var second = dir.StageQuarantine("20260102-030405");
        var store = await StartedStoreWithTurnsAsync(path);

        store.Forget(AiForgetScope.Everything);

        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.Empty(store.ReadRecent(10));
        Assert.False(File.Exists(path));

        // WPF's wipe deletes a file its own model does not own for precisely this reason: leaving
        // it behind "would let CompanionSessionStore's one-time import resurrect the wiped
        // conversation on the next launch" (MemoryStore.cs:587-590).
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    /// <summary>
    /// The over-broad-delete guard, and it is the fact this row most needs. The widest scope is
    /// scoped to THIS document's own family, derived from the store's own path — a neighbour file,
    /// another feature's quarantined document, and a similarly-named file all survive it.
    /// </summary>
    [Fact]
    public async Task Everything_TouchesNothingOutsideThisDocumentsFamily()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);

        var neighbour = dir.Path("settings.json");
        var foreignQuarantine = dir.Path("settings.corrupt-20260101-000000.json");
        var lookalike = dir.Path("ai_memory_notes.txt");
        var prefixed = dir.Path("ai_memory-export.json");
        var bystanders = new[] { neighbour, foreignQuarantine, lookalike, prefixed };
        Assert.NotEmpty(bystanders); // framing (c): an emptied source would silence the loop below
        foreach (var file in bystanders)
        {
            File.WriteAllText(file, "not this document");
        }

        var store = await StartedStoreWithTurnsAsync(path);
        store.Forget(AiForgetScope.Everything);

        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.False(File.Exists(path));
        foreach (var file in bystanders)
        {
            Assert.True(File.Exists(file), $"the widest forget scope deleted {Path.GetFileName(file)}, which it was never asked about");
            Assert.Equal("not this document", File.ReadAllText(file));
        }
    }

    // ---- the ladder is a ladder: every scope forgets the conversation ----

    [Theory]
    [InlineData(AiForgetScope.Thread)]
    [InlineData(AiForgetScope.Conversation)]
    [InlineData(AiForgetScope.Everything)]
    public async Task EveryScope_ForgetsTheTurns_AndTheNextPromptCarriesNone(AiForgetScope scope)
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var store = await StartedStoreWithTurnsAsync(path);
        Assert.Equal(2, store.ReadPromptContext().Count); // the consent-gated read had them

        store.Forget(scope);

        // In memory AND in what the next prompt would carry. What is left on DISK is the thing
        // the three scopes differ about, and each of them pins that separately above; a
        // File.Exists-conditional read here would silence itself on a wrong path.
        Assert.Empty(store.ReadRecent(10));
        Assert.Empty(store.ReadPromptContext());
    }

    [Theory]
    [InlineData(AiForgetScope.Thread)]
    [InlineData(AiForgetScope.Conversation)]
    [InlineData(AiForgetScope.Everything)]
    public async Task NoScope_ClobbersANewerDocument_EveryOneReportsDegraded(AiForgetScope scope)
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        const string newer = """{ "schemaVersion": 99, "migrationJournal": [], "turns": [ { "role": "User", "text": "newer-build-data" } ] }""";
        File.WriteAllText(path, newer);
        var quarantine = dir.StageQuarantine();

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        Assert.IsType<LoadOutcome.NewerSchema>(store.LastLoadOutcome);

        store.Forget(scope);

        // An older build never clobbers a newer one (persistence contract §4 rule 7) — and that
        // applies to the WIDEST scope too, which is where it would be easiest to lose.
        Assert.Equal(AiMemoryClearOutcome.Degraded, store.LastClearOutcome);
        Assert.True(File.Exists(path));
        Assert.Contains("newer-build-data", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(File.Exists(quarantine));
        Assert.Empty(store.ReadRecent(10)); // in-memory state emptied all the same
    }

    /// <summary>
    /// The interface member is unchanged behaviour, now named: <see cref="IAiMemoryStore.Clear"/>
    /// is the CONVERSATION scope (contract §5 rule 1). A build that quietly re-pointed it at the
    /// widest scope would delete quarantined bytes on the c7 button, which no copy on that button
    /// promises.
    /// </summary>
    [Fact]
    public async Task Clear_IsTheConversationScope_AndStillLeavesQuarantinedBytes()
    {
        using var dir = new TempDir();
        var path = dir.Path(AiMemoryStore.FileName);
        var quarantine = dir.StageQuarantine();
        var store = await StartedStoreWithTurnsAsync(path);

        ((IAiMemoryStore)store).Clear();

        Assert.Equal(AiMemoryClearOutcome.Cleared, store.LastClearOutcome);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(quarantine));
    }

    private static async Task<AiMemoryStore> StartedStoreWithTurnsAsync(string path)
    {
        // A document with a turn payload AND a non-turn payload, so Thread-versus-Conversation is
        // observable in the file rather than only in the method name.
        File.WriteAllText(path, """
            { "schemaVersion": 1, "migrationJournal": [], "turns": [], "futureMember": { "nested": 42 } }
            """);

        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Append(UserTurn);
        store.Append(AssistantTurn);
        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
        Assert.Contains("remember-me-user", File.ReadAllText(path), StringComparison.Ordinal);
        return store;
    }

    private static AiMemoryStore NewStore(string path) =>
        new(new OperationRegistry().OwnerFor("AiMemory"),
            new ListLogSink(),
            path,
            () => AiMemoryConsent.Granted);

    private sealed class ListLogSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class TempDir : IDisposable
    {
        /// <summary>What a quarantined copy holds: a previous state of the very document under test.</summary>
        public const string QuarantineBytes = """{ "turns": [ { "role": "User", "text": "remember-me-user" } ] } truncated""";

        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-forget-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        /// <summary>
        /// The exact shape <c>PersistenceStore.Quarantine</c> writes beside the document
        /// (client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:451-455).
        /// </summary>
        public string StageQuarantine(string stamp = "20260101-010101")
        {
            var file = Path(System.IO.Path.GetFileNameWithoutExtension(AiMemoryStore.FileName) + $".corrupt-{stamp}.json");
            File.WriteAllText(file, QuarantineBytes);
            return file;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }
}
