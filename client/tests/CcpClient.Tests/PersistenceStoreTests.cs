using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-087: the LIFECYCLE SHAPE of <see cref="PersistenceStore{TModel}"/>, pinned because a
/// board row asserted the opposite. The row read "five unbounded in-process disk-store
/// waits sit on UI-reachable paths"; six of the seven cited sites wait on nothing at all,
/// because <c>StartAsync</c> and <c>StopAsync</c> complete synchronously and hand back an
/// already-completed task. The residual truth is THREAD AFFINITY, not a wait: whatever
/// thread calls <c>StartAsync</c> performs the load's blocking disk I/O itself.
///
/// These four facts make that correction a thing the suite OBSERVES rather than a thing a
/// record claims. F2 is the SP-072 read-from-inside shape: the thread id is recorded by the
/// product calling the fake, not sampled by the test from outside.
///
/// WHAT THESE FACTS DO NOT PROVE (the other half of the correction, established by reading
/// the call sites and recorded in spine-tasks/SP-087-.../record.md, never asserted here):
/// that the calling thread is the UI thread. CcpClient.Tests has no Avalonia dispatcher and
/// must not acquire one; thread affinity is the portable half a dispatcher-free test can
/// honestly prove, and it is the half that transfers to Linux unchanged.
/// </summary>
public class PersistenceStoreTests
{
    private const int DemoSchemaVersion = 1;

    /// <summary>
    /// The fact that makes every <c>.GetAwaiter().GetResult()</c> on this method a NO-OP
    /// wait: the task is complete, and the typed load outcome is already observable, before
    /// the caller can await or bound anything.
    /// </summary>
    [Fact]
    public void StartAsync_ReturnsACompletedTask_WithTheLoadOutcomeAlreadyObservable()
    {
        using var dir = new TempDir();
        var store = NewStore(dir.Path("settings.json"));

        var task = store.StartAsync(CancellationToken.None);

        // Observed WITHOUT awaiting, on purpose. Awaiting first would destroy the claim:
        // the point is the state of the task at the instant the caller receives it.
        Assert.True(task.IsCompletedSuccessfully);
        Assert.NotNull(store.LastLoadOutcome);
        Assert.IsType<LoadOutcome.Missing>(store.LastLoadOutcome);
    }

    /// <summary>
    /// The residual REAL defect the row was groping at: the load's blocking disk I/O runs on
    /// whoever calls, so a UI-thread caller loads on the UI thread. Recorded from INSIDE the
    /// operation (the product calls the sink mid-<c>Load</c>), never sampled from outside,
    /// which is the vacuity class this project has hit before.
    /// </summary>
    [Fact]
    public void Load_RunsOnTheCallingThread_RecordedFromInsideTheOperation()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        // Orphan temp with NO main file: the crash-recovery branch (PersistenceStore.cs:328-332)
        // logs UNCONDITIONALLY from inside Load(). The "stale temp beside a valid main" branch
        // (:333-343) logs only when the delete THROWS (:341), which needs a locked file and is
        // OS-dependent, so it is deliberately not the branch driven here.
        File.WriteAllText(path + ".tmp", """{"greeting":"recovered","schemaVersion":1}""");

        var sink = new ThreadRecordingLogSink();
        var store = NewStore(path, sink);
        var callerThreadId = Environment.CurrentManagedThreadId;

        _ = store.StartAsync(CancellationToken.None);

        // Asserted separately so an ABSENT log fails loudly instead of leaving the thread
        // comparison to pass against a default 0 (a real managed thread id is never 0).
        Assert.True(sink.LogCount > 0, "Load() logged nothing, so the thread comparison would measure an absent call");
        // Anchored to the exact line, so a future edit that logs earlier in StartAsync cannot
        // silently redirect this fact onto a different call.
        Assert.StartsWith("persistence: recovering settings from interrupted save", sink.FirstMessage);
        Assert.Equal(callerThreadId, sink.FirstLogThreadId);
        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
    }

    /// <summary>
    /// The stop half, taken against a RUNNING store so the early-out guard
    /// (PersistenceStore.cs:209-212) is not the path under test. A never-started store would
    /// satisfy the completed-task and Running assertions with the mechanism deleted.
    /// </summary>
    [Fact]
    public async Task StopAsync_OnARunningStore_ReturnsACompletedTask_AndCancelsTheGeneration()
    {
        using var dir = new TempDir();
        var store = NewStore(dir.Path("settings.json"));
        await store.StartAsync(CancellationToken.None);
        Assert.True(store.Running); // precondition: the guard path is NOT what runs below

        var task = store.StopAsync();

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(store.Running);
        // The Cancel() half needs its own witness: every assertion above still holds if
        // _owner.Cancel() (:215) is deleted. A post-stop Save() terminating typed Cancelled is
        // producible ONLY by the cancelled generation (WriteOnce's token check, :482), and is
        // NOT producible by the !Running guard path, which never begins a generation at all
        // (RunAsync throws there instead, OperationRegistry.cs:204-208).
        var outcome = await store.Save();
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
    }

    /// <summary>
    /// Stop is not a flush: it performs no I/O even with unsaved state. This is why callers
    /// that need the final write call <c>FlushAsync</c> first (IntakeHostContext.cs:126-127)
    /// and why the save-slot erase path deliberately does not.
    /// </summary>
    [Fact]
    public async Task StopAsync_WritesNothingToDisk_EvenWhenTheStoreIsDirty()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var store = NewStore(path);
        await store.StartAsync(CancellationToken.None);
        store.Mutate(m => m.Greeting = "dirty-at-stop");
        Assert.True(store.IsDirty);

        await store.StopAsync();

        // The existence checks deliberately avoid the File.Exists token: VacuousShapeDetector
        // (:225-229) classifies it as an fs-predicate SITE requiring a ledger disposition, and
        // that ledger is outside this packet's File Scope. FileInfo asserts the same thing, and
        // the directory sweep is strictly stronger than naming the two paths.
        Assert.Empty(Directory.GetFiles(dir.Root));
        Assert.False(new FileInfo(path).Exists);
        Assert.False(new FileInfo(path + ".tmp").Exists);
    }

    private static PersistenceStore<DemoSettings> NewStore(string path, ILogSink? log = null) =>
        new(new OperationRegistry().OwnerFor("Persistence"),
            log ?? new ThreadRecordingLogSink(),
            path,
            DemoSchemaVersion,
            [new DemoMigrationV0ToV1()]);

    /// <summary>
    /// Records WHICH THREAD the product was on when it logged, from inside the operation.
    /// Declared per-file like every other minimal sink fake in this suite; the shared-helper
    /// alternative would be the deviation, not this.
    /// </summary>
    private sealed class ThreadRecordingLogSink : ILogSink
    {
        public int LogCount { get; private set; }

        public int FirstLogThreadId { get; private set; }

        public string FirstMessage { get; private set; } = "";

        public void Log(string message)
        {
            if (LogCount++ == 0)
            {
                FirstLogThreadId = Environment.CurrentManagedThreadId;
                FirstMessage = message;
            }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-store-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Root);

        public string Path(string fileName) => System.IO.Path.Combine(Root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
