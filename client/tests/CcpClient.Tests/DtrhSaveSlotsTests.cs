using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-024 slice b2: the three local DTRH save slots on SP-005 machinery. Lifecycle
/// (create/select/persist across store reloads), corruption → quarantine + flagged
/// defaults (never silent), ordering stability, empty-slot semantics, stitch-lock,
/// unknown-member preservation. WPF parity cites in SP-024 record Step 1.
/// </summary>
public class DtrhSaveSlotsTests
{
    [Fact]
    public async Task FreshStart_SummariesEmpty_InFixedSlotOrder_NoFilesCreated()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);

        var summaries = slots.Summaries();
        Assert.Equal([1, 2, 3], summaries.Select(s => s.Slot).ToArray()); // ordering stability
        Assert.All(summaries, s => Assert.False(s.Exists));
        Assert.All(summaries, s => Assert.Null(s.LastPlayedUtc));
        Assert.Equal(1, slots.ActiveSlot);

        // Empty slots stay file-less (WPF Exists=false; SP-005 Missing is not dirty) —
        // even a teardown flush must not create them.
        await slots.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(slots.SlotFilePath(1)));
        Assert.False(File.Exists(slots.SlotFilePath(2)));
        Assert.False(File.Exists(slots.SlotFilePath(3)));
        Assert.False(File.Exists(slots.IndexFilePath));
        await slots.StopAsync();
    }

    [Fact]
    public async Task DescendInto_EmptySlot_PersistsImmediately_AndSurvivesReload()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);

        var outcome = await slots.DescendInto(2);
        Assert.IsType<OperationOutcome.Completed>(outcome);
        Assert.Equal(2, slots.ActiveSlot);
        Assert.True(File.Exists(slots.SlotFilePath(2)));   // fresh document persisted at descend
        Assert.True(File.Exists(slots.IndexFilePath));     // active slot remembered
        Assert.False(File.Exists(slots.SlotFilePath(1)));  // untouched slots stay empty
        Assert.NotNull(slots.Summaries()[1].LastPlayedUtc);
        await slots.StopAsync();

        // Reload (app restart): the selection and the save survive.
        var reloaded = NewSlots(dir);
        await reloaded.StartAsync(CancellationToken.None);
        Assert.Equal(2, reloaded.ActiveSlot);
        var summary = reloaded.Summaries()[1];
        Assert.True(summary.Exists);
        Assert.False(summary.Degraded);
        await reloaded.StopAsync();
    }

    [Fact]
    public async Task SlotLifecycle_MutatePersist_RoundTripsAcrossReload()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);
        await slots.DescendInto(1);

        slots.StoreFor(1).Mutate(m =>
        {
            m.Sparks = 120;
            m.Gold = 45;
            m.RunsCompleted = 7;
            m.BestScore = 98765;
            m.CraftedItems["ragdoll"] = 1;
        });
        Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(1).SaveImmediate());
        await slots.StopAsync();

        var reloaded = NewSlots(dir);
        await reloaded.StartAsync(CancellationToken.None);
        var doc = reloaded.StoreFor(1).Current;
        Assert.Equal(120, doc.Sparks);
        Assert.Equal(45, doc.Gold);
        Assert.Equal(7, doc.RunsCompleted);
        Assert.Equal(98765, doc.BestScore);
        Assert.True(doc.CraftedItems.ContainsKey("ragdoll"));

        var summary = reloaded.Summaries()[0];
        Assert.True(summary.Exists);
        Assert.Equal(120, summary.Sparks);
        Assert.Equal(45, summary.Gold);
        Assert.Equal(7, summary.RunsCompleted);
        Assert.Equal(98765, summary.BestScore);
        Assert.True(summary.HasRagdoll);
        Assert.False(summary.HasPorcelain);
        await reloaded.StopAsync();
    }

    [Fact]
    public async Task Corruption_Quarantined_FlaggedDefaults_NeverSilent()
    {
        using var dir = new TempDir();
        var sink = new ListLogSink();
        var slots = NewSlots(dir, sink);
        var path = slots.SlotFilePath(2);
        const string garbage = "not a save file {{{ truncated";
        File.WriteAllText(path, garbage);

        await slots.StartAsync(CancellationToken.None);

        var outcome = Assert.IsType<LoadOutcome.Quarantined>(slots.SlotLoadOutcome(2));
        Assert.True(slots.SlotLoadOutcome(2)!.IsDegraded);
        Assert.True(File.Exists(outcome.BackupPath));                    // original preserved
        Assert.Equal(garbage, File.ReadAllText(outcome.BackupPath));
        Assert.False(File.Exists(path));                                 // moved aside, not copied
        Assert.Equal(0, slots.StoreFor(2).Current.Sparks);               // flagged defaults
        Assert.False(slots.StoreFor(2).IsDirty);                         // defaults are NOT dirty

        var summary = slots.Summaries()[1];
        Assert.True(summary.Degraded);                                   // the card surfaces the flag
        Assert.False(summary.Exists);                                    // diverges from WPF zeroed-exists (record Step 1)
        Assert.Contains(sink.Messages, m => m.Contains("slot 2 degraded"));
        await slots.StopAsync();
    }

    [Fact]
    public async Task UnknownMembers_PreservedAcrossLoadAndSave()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        File.WriteAllText(slots.SlotFilePath(3),
            """{ "schemaVersion": 1, "sparks": 9, "futureB4Field": { "nested": true } }""");

        await slots.StartAsync(CancellationToken.None);
        Assert.Equal(9, slots.StoreFor(3).Current.Sparks);
        Assert.True(slots.StoreFor(3).Current.ExtensionData?.ContainsKey("futureB4Field"));

        Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(3).SaveImmediate());
        var text = File.ReadAllText(slots.SlotFilePath(3));
        Assert.Contains("futureB4Field", text); // b4's members survive a b2 build's round-trip
        await slots.StopAsync();
    }

    [Fact]
    public async Task StitchLock_WpfSemantics()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);

        // Fresh profile: slots 2/3 stitched shut, slot 1 always open
        // (ChaosSlotPickerWindow.xaml.cs:68-80).
        var summaries = slots.Summaries();
        Assert.False(slots.IsSlotLocked(1, summaries));
        Assert.True(slots.IsSlotLocked(2, summaries));
        Assert.True(slots.IsSlotLocked(3, summaries));

        // Any save's Ragdoll craft opens slot 2 globally; slot 3 needs the Porcelain doll.
        await slots.DescendInto(1);
        slots.StoreFor(1).Mutate(m => m.CraftedItems["ragdoll"] = 1);
        Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(1).SaveImmediate());
        summaries = slots.Summaries();
        Assert.False(slots.IsSlotLocked(2, summaries));
        Assert.True(slots.IsSlotLocked(3, summaries));

        slots.StoreFor(1).Mutate(m => m.CraftedItems["porcelain"] = 1);
        Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(1).SaveImmediate());
        Assert.False(slots.IsSlotLocked(3, slots.Summaries()));
        await slots.StopAsync();
    }

    [Fact]
    public async Task StitchLock_PreExistingSaveKeepsItsSlotOpen()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        // A slot-2 save exists but no doll anywhere (back-compat with pre-craft slots,
        // ChaosSlotPickerWindow.xaml.cs:66-67).
        await slots.StartAsync(CancellationToken.None);
        await slots.DescendInto(2);
        Assert.False(slots.IsSlotLocked(2, slots.Summaries()));
        Assert.True(slots.IsSlotLocked(3, slots.Summaries()));
        await slots.StopAsync();
    }

    [Fact]
    public async Task DeleteSlot_RemovesFile_ReloadsFresh_DescendStartsOver()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);
        await slots.DescendInto(1);
        slots.StoreFor(1).Mutate(m => m.Sparks = 500);
        Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(1).SaveImmediate());

        Assert.True(slots.DeleteSlot(1));
        Assert.False(File.Exists(slots.SlotFilePath(1)));
        Assert.False(slots.Summaries()[0].Exists);
        Assert.Equal(0, slots.StoreFor(1).Current.Sparks); // in-memory state reloaded fresh too
        Assert.False(slots.DeleteSlot(1));                 // nothing left to remove

        // "Delete the active save, then descend into it" correctly starts fresh
        // (ChaosUpgrades.cs:224-229).
        Assert.IsType<OperationOutcome.Completed>(await slots.DescendInto(1));
        Assert.Equal(0, slots.StoreFor(1).Current.Sparks);
        Assert.True(File.Exists(slots.SlotFilePath(1)));
        await slots.StopAsync();
    }

    [Fact]
    public async Task ActiveSlot_OutOfRange_ClampsToOne()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        File.WriteAllText(slots.IndexFilePath, """{ "schemaVersion": 1, "activeSlot": 9 }""");
        await slots.StartAsync(CancellationToken.None);
        Assert.Equal(1, slots.ActiveSlot);
        await slots.StopAsync();
    }

    [Fact]
    public async Task SelectSlot_PersistsImmediately()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir);
        await slots.StartAsync(CancellationToken.None);

        Assert.IsType<OperationOutcome.Completed>(await slots.SelectSlot(3));
        var json = File.ReadAllText(slots.IndexFilePath);
        Assert.Contains("\"activeSlot\": 3", json);
        // The slot itself was only SELECTED, not descended: no save file yet
        // (picker select ≠ create; WPF creates on first meta flush / descend).
        Assert.False(File.Exists(slots.SlotFilePath(3)));
        await slots.StopAsync();
    }

    private static DtrhSaveSlots NewSlots(TempDir dir, ListLogSink? sink = null) =>
        new(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), sink ?? new ListLogSink()),
            dir.Root);

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "ccp-sp024-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }
}
