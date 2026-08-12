using System.Text.Json.Nodes;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Persistence contract §11: the store's flush occupies SP-003's reserved slot at the head
/// of the single guarded teardown — dirty settings are on disk BEFORE generations cancel
/// and before reverse-order participant stop; clean/never-loaded stores flush nothing;
/// repeated shutdown stays a no-op (SP-003 invariants intact).
/// </summary>
public class TeardownFlushTests
{
    [Fact]
    public async Task Shutdown_DirtySettings_FlushedBeforeReverseOrderParticipantStop()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var registry = new OperationRegistry();
        var log = new ListLogSink();
        var store = new PersistenceStore<DemoSettings>(
            registry.OwnerFor("Persistence"), log, path,
            DemoSettings.CurrentSchemaVersion, [new DemoMigrationV0ToV1()]);
        // Reverse order: the probe stops FIRST; the flush must already have run by then.
        var probe = new FileProbeParticipant(path);
        var host = new ApplicationHost(
            log, [store, probe], new StartupTrace(), registry, new UiDispatchBoundary(),
            preDrainFlush: () => store.FlushAsync(TimeSpan.FromSeconds(5)));
        await host.StartParticipantsAsync(CancellationToken.None);

        store.Mutate(m => m.Greeting = "dirty-at-shutdown");
        await host.ShutdownAsync();

        Assert.True(probe.FileExistedAtStop); // flush completed before reverse-order stop
        Assert.True(probe.ContentMatchedAtStop);
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("dirty-at-shutdown", document["greeting"]!.GetValue<string>());
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());

        // SP-003 invariant: repeated shutdown is a no-op.
        await host.ShutdownAsync();
        Assert.Equal(1, probe.StopCount);
    }

    [Fact]
    public async Task Shutdown_CleanStore_WritesNothing()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var registry = new OperationRegistry();
        var store = new PersistenceStore<DemoSettings>(
            registry.OwnerFor("Persistence"), new ListLogSink(), path,
            DemoSettings.CurrentSchemaVersion, [new DemoMigrationV0ToV1()]);
        var host = new ApplicationHost(
            new ListLogSink(), [store], new StartupTrace(), registry, new UiDispatchBoundary(),
            preDrainFlush: () => store.FlushAsync(TimeSpan.FromSeconds(5)));
        await host.StartParticipantsAsync(CancellationToken.None);
        Assert.False(store.IsDirty); // defaults loaded, never mutated

        await host.ShutdownAsync();

        Assert.False(File.Exists(path)); // contract §5 rule 2 / §11 rule 2: no auto-save
    }

    [Fact]
    public async Task RealCompositionRoot_DirtyAtShutdown_FlushesThroughWiredSlot()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var root = new CompositionRoot { SettingsPathFactory = () => path };
        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());
        await host.StartParticipantsAsync(CancellationToken.None);

        var store = Assert.IsType<PersistenceStore<DemoSettings>>(host.Participants[0]);
        store.Mutate(m => m.Volume = 77);
        await host.ShutdownAsync();

        // The composition root wired the flush into the host's reserved slot: the dirty
        // mutation is on disk after teardown, through the single guarded entry point.
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(77, document["volume"]!.GetValue<int>());
    }

    [Fact]
    public async Task Flush_ExceedsBoundedWait_LogsAndShutdownContinues()
    {
        using var dir = new TempDir();
        var path = dir.Path("settings.json");
        var log = new ListLogSink();
        var writeStarted = new ManualResetEventSlim();
        var releaseWrite = new ManualResetEventSlim();
        var hooks = new AtomicWriteHooks
        {
            // A wedged writer: starts, then blocks until released (panic-path hazard the
            // bounded wait exists for — the pre-approach consult's correction).
            WriteTempFile = (p, json) =>
            {
                writeStarted.Set();
                releaseWrite.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken); // wallclock-allow: subject instrument (a wedged writer) — the bound exists so the TEST can never hang, not to time the product
                new AtomicWriteHooks().WriteTempFile(p, json);
            },
        };
        var store = new PersistenceStore<DemoSettings>(
            new OperationRegistry().OwnerFor("Persistence"), log, path,
            DemoSettings.CurrentSchemaVersion, [new DemoMigrationV0ToV1()], hooks);
        await store.StartAsync(CancellationToken.None);
        store.Mutate(m => m.Greeting = "wedged");

        var flush = store.FlushAsync(TimeSpan.FromMilliseconds(100));
        Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), // wallclock-allow: deterministic signal, bounded — expiry means the wedged-write hook never fired (hook/product failure, not timing)
            "the wedged write never started within 5s — the flush never reached the write hook (hook/product failure, not timing)");
        await flush; // returns after the bounded wait while the write is still blocked

        Assert.Contains(log.Messages, m => m.Contains("exceeded its bounded wait"));

        // Unblock and drain: the wedged write completes, shutdown was never hung.
        releaseWrite.Set();
        var drained = await store.SaveImmediate();
        Assert.IsType<OperationOutcome.Completed>(drained);
        Assert.True(File.Exists(path));
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    /// <summary>Stops first (registered last) and observes whether the flush already landed.</summary>
    private sealed class FileProbeParticipant(string settingsPath) : IBackgroundParticipant
    {
        public string Name => "Probe";

        public bool Running { get; private set; }

        public int StopCount { get; private set; }

        public bool FileExistedAtStop { get; private set; }

        public bool ContentMatchedAtStop { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (!Running)
            {
                return Task.CompletedTask;
            }

            Running = false;
            StopCount++;
            FileExistedAtStop = File.Exists(settingsPath);
            ContentMatchedAtStop = FileExistedAtStop
                && File.ReadAllText(settingsPath).Contains("dirty-at-shutdown", StringComparison.Ordinal);
            return Task.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-flush-" + Guid.NewGuid().ToString("N"));

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
