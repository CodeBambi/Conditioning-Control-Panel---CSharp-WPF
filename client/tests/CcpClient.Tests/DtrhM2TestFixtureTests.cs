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

    /// <summary>The m2test.js meta-commands walk, verbatim from the READ-ONLY WPF payload
    /// (m2test.js:64-90) — the exact 26 ops the headed evidence run drives.</summary>
    private static readonly string[] M2TestOps =
    [
        "{\"op\":\"add-gold\",\"amount\":50}",
        "{\"op\":\"spend-gold\",\"amount\":20}",
        "{\"op\":\"purchase-dial\",\"id\":\"bubbleSize\",\"cost\":25}",
        "{\"op\":\"purchase-dial\",\"id\":\"hydra\",\"cost\":99999999}",
        "{\"op\":\"buy-pocket\",\"kind\":\"toy\",\"cost\":10}",
        "{\"op\":\"bench-purchase\",\"id\":\"stats_panel\",\"cost\":10}",
        "{\"op\":\"bench-buy\",\"id\":\"toy_pocket_1\",\"cost\":1}",
        "{\"op\":\"set-flag\",\"key\":\"seenDefuseTutorial\"}",
        "{\"op\":\"add-to-set\",\"set\":\"discoveredCodexIds\",\"id\":\"bubble:m2test\"}",
        "{\"op\":\"lesson-progress\",\"id\":\"m2test_lesson\",\"value\":3}",
        "{\"op\":\"set-num\",\"key\":\"lastRankSeen\",\"value\":1}",
        "{\"op\":\"equip-boon\",\"id\":\"m2test_boon\"}",
        "{\"op\":\"spend-gold\",\"amount\":99999999}",
        "{\"op\":\"definitely-not-an-op\"}",
        "{\"op\":\"material-add\",\"id\":\"chrome\",\"amount\":30}",
        "{\"op\":\"material-add\",\"id\":\"silicone\",\"amount\":5}",
        "{\"op\":\"material-add\",\"id\":\"pills\",\"amount\":10}",
        "{\"op\":\"craft\",\"id\":\"the_padlock\",\"cost\":{\"chrome\":8}}",
        "{\"op\":\"craft\",\"id\":\"the_cage\",\"cost\":{\"chrome\":8,\"silicone\":1}}",
        "{\"op\":\"craft\",\"id\":\"sugar_cube\",\"cost\":{\"pills\":4}}",
        "{\"op\":\"pin-boon\",\"id\":\"m2test_pin\"}",
        "{\"op\":\"set-denial\",\"on\":true}",
        "{\"op\":\"set-denial\",\"on\":true}",
        "{\"op\":\"consume-crafted\",\"id\":\"sugar_cube\"}",
        "{\"op\":\"consume-crafted\",\"id\":\"the_padlock\"}",
        "{\"op\":\"add-to-set\",\"set\":\"paperwallSketches\",\"id\":\"m2test_sketch\"}",
    ];

    [Fact]
    public async Task M2TestOpSequence_OffFixture_AppliesExactlyTheModeledEighteen()
    {
        // SP-057 pre-completion consult pin 1: the engine-side invariant behind the
        // headed m2test 7/8 explanation. Off the committed fixture, the engine applies
        // EXACTLY the 18 ops the payload's expectation model counts (m2test.js:97-100) —
        // the headed run's 19th rev bump is page-originated narrative traffic (record.md
        // Step 3), never an engine apply. If this count moves, the explanation is stale.
        using var dir = new TempDir();
        var slots = NewSlots(dir.Root);
        await slots.StartAsync(CancellationToken.None);
        try
        {
            var meta = new DtrhMeta(
                slots.StoreFor(1), slots.IndexStore,
                new DtrhAssetStats(slots.AssetStatsStore, _ => { }),
                _ => { }, _ => { },
                testMode: true, slots.SlotFilePath(1), DtrhM2TestFixture.Load());
            var rev0 = meta.Rev;
            var applied = 0;
            foreach (var op in M2TestOps)
            {
                if (meta.HandleMetaCommand(System.Text.Json.JsonDocument.Parse(op).RootElement))
                {
                    applied++;
                }
            }

            Assert.Equal(18, applied);
            Assert.Equal(18, meta.Rev - rev0);
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
