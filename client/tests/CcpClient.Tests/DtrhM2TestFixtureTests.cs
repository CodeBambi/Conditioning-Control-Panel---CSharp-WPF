using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-057: the m2test declared-fixture discipline. The committed fixture round-trips,
/// carries its sentinel values (instantly recognizable as fixture-origin in any log or
/// screenshot — the SP-052 Run B class), malformed fixture JSON fails typed, and the
/// meta engine's test mode sources the fixture — NEVER the live slot document.
/// </summary>
public class DtrhM2TestFixtureTests
{
    [Fact]
    public void DefaultFixture_RoundTrips_WithSentinelValues()
    {
        var doc = DtrhM2TestFixture.Load();
        Assert.Equal(777, doc.Sparks);
        Assert.Equal(4242, doc.BestScore);
        Assert.Equal(3, doc.RunsCompleted);
        // Absent members keep the additive-only defaults (DtrhSlotDocument initializers).
        Assert.Equal(1, doc.ConsumableSlots);
        Assert.Empty(doc.PurchasedUpgrades);
    }

    [Fact]
    public void MalformedFixture_ThrowsTyped_NeverLiveDocFallback()
    {
        Assert.Throws<DtrhM2TestFixtureException>(() => DtrhM2TestFixture.Load("{ not json"));
        Assert.Throws<DtrhM2TestFixtureException>(() => DtrhM2TestFixture.Load("null"));
    }

    [Fact]
    public async Task TestMode_SourcesCommittedFixture_NotTheLiveDocument()
    {
        // A live slot document loaded with owner-ish state; test mode must NOT inherit it.
        using var dir = new TempDir();
        var slots = NewSlots(dir.Root);
        await slots.StartAsync(CancellationToken.None);
        try
        {
            slots.StoreFor(1).Mutate(d => { d.Sparks = 99999; d.RunsCompleted = 400; d.BestScore = 888888; });
            var meta = new DtrhMeta(
                slots.StoreFor(1), slots.IndexStore,
                new DtrhAssetStats(slots.AssetStatsStore, _ => { }),
                _ => { }, _ => { },
                testMode: true, slots.SlotFilePath(1));
            // The committed fixture's sentinels, not the live document's 99999/400/888888.
            var state = SnapshotState(meta);
            Assert.Equal(777, state.GetProperty("sparks").GetInt32());
            Assert.Equal(3, state.GetProperty("runsCompleted").GetInt32());
        }
        finally
        {
            await slots.StopAsync();
        }
    }

    [Fact]
    public async Task TestMode_ExplicitFixture_Wins_OverCommittedDefault()
    {
        using var dir = new TempDir();
        var slots = NewSlots(dir.Root);
        await slots.StartAsync(CancellationToken.None);
        try
        {
            var declared = new DtrhSlotDocument { Sparks = 5, RunsCompleted = 1 };
            var meta = new DtrhMeta(
                slots.StoreFor(1), slots.IndexStore,
                new DtrhAssetStats(slots.AssetStatsStore, _ => { }),
                _ => { }, _ => { },
                testMode: true, slots.SlotFilePath(1), declared);
            var state = SnapshotState(meta);
            Assert.Equal(5, state.GetProperty("sparks").GetInt32());
            Assert.Equal(1, state.GetProperty("runsCompleted").GetInt32());
        }
        finally
        {
            await slots.StopAsync();
        }
    }

    private static System.Text.Json.JsonElement SnapshotState(DtrhMeta meta) =>
        System.Text.Json.JsonSerializer.SerializeToElement(meta.SnapshotMessage()).GetProperty("state");

    private static DtrhSaveSlots NewSlots(string dir) =>
        new(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new DebugLogSink()), dir);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ccp-sp057-fx-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
