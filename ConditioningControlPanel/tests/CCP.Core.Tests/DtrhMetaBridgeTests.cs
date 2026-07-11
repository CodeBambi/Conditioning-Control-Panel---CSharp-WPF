using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Coverage for the portable <c>DtrhMetaBridge</c> (slice S2b-2a of the DTRH web-game port):
/// the economy state machine that mirrors the WPF <c>DtrhMetaBridge</c> op handlers. Drives the
/// bridge with testMode:false against an in-memory <see cref="FakeChaosMetaStore"/> so assertions
/// land directly on <c>store.State</c>; the test-mode divergence (<see cref="AwardRun_TestMode_"/>
/// ...) confirms the deliberate first-fall/stat omission. All facts are deterministic.
/// </summary>
public class DtrhMetaBridgeTests
{
    // ---- in-memory test doubles ----

    private sealed class FakeChaosMetaStore : IChaosMetaStore
    {
        public ChaosMetaState State { get; set; } = new();
        public int RankIndex { get; set; }
        public int SaveCount;
        public void Save() => SaveCount++;
    }

    /// <summary>Records <see cref="IBarkService.NotifyChaosGiftGiven"/> + first-time calls;
    /// every other member is a default no-op (the interface provides defaults for most).</summary>
    private sealed class FakeBark : IBarkService
    {
        public int GiftGivenCount;
        public List<string> FirstTimes = new();

        public void NotifyChaosGiftGiven() => GiftGivenCount++;
        public void NotifyChaosFirstTime(string id) => FirstTimes.Add(id);

        // Required members without interface default bodies:
        public void NotifyAvatarClicked() { }
        public void NotifyChaosDollhouseFirstOpen() { }
        public void NotifyChaosRevealFlash(string id) { }
        public void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
            int defused, int detonated, int bestCombo, string difficulty) { }
        public void NotifyChaosRankUp(string rankName) { }
        public void NotifyChaosDraftAutopick() { }
        public void NotifyChaosRunStarted(string difficulty) { }
        public void NotifyChaosFocusLow() { }
        public void NotifyChaosGoldFirst() { }
        public void NotifyChaosDuoDemo() { }
    }

    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-dtrhbridge-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
            Directory.CreateDirectory(UserDataPath);
        }

        public void Cleanup()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    // ---- command JObject builders (mirror the {op,id,cost,...} shape the page sends) ----

    private static JObject Cmd(string op) => new() { ["op"] = op };
    private static JObject Cmd(string op, string id) => new() { ["op"] = op, ["id"] = id };
    private static JObject Cmd(string op, string id, int cost) => new() { ["op"] = op, ["id"] = id, ["cost"] = cost };

    private static DtrhMetaBridge MakeBridge(FakeChaosMetaStore store, FakeBark? bark = null,
        bool testMode = false, TestAppEnvironment? env = null) =>
        new(store, env ?? new TestAppEnvironment(), NullLogger<DtrhMetaBridge>.Instance, _ => { }, testMode, bark);

    /// <summary>Reference copy of the (deliberately first-fall-omitting) test-mode spark formula,
    /// so the test-mode AwardRun divergence is asserted against the exact same arithmetic.</summary>
    private static int ComputeSparksOnlyRef(in ChaosRunRewardInput run)
    {
        double durationMin = Math.Max(0, run.RunDurationSec) / 60.0;
        double completionBonus = 35.0 * run.DifficultyMult * Math.Min(1.0, durationMin / 3.0);
        double scorePart = 1.5 * Math.Sqrt(Math.Max(0, run.Score));
        int sparks = (int)Math.Round((scorePart + completionBonus) * run.SparkGainMult);
        sparks += (int)Math.Max(0, run.TrickleDrops);
        if (run.DripFeedMaxed) sparks = (int)Math.Round(sparks * 1.10);
        return Math.Max(0, sparks);
    }

    // ============================ 1. purchase-upgrade ============================

    [Fact]
    public void PurchaseUpgrade_Succeeds_WhenAffordable()
    {
        var store = new FakeChaosMetaStore { State = { Sparks = 100 } };
        var bridge = MakeBridge(store);
        Assert.True(bridge.Handle(Cmd("purchase-upgrade", "focus_aura", 40)));
        Assert.Equal(60, store.State.Sparks);
        Assert.Contains("focus_aura", store.State.PurchasedUpgrades);
    }

    [Fact]
    public void PurchaseUpgrade_RejectsUnaffordable_ExtremeTier_AndIdempotent()
    {
        var store = new FakeChaosMetaStore { State = { Sparks = 10 } };
        var bridge = MakeBridge(store);
        // Unaffordable: no negative balance, no mutation, not applied.
        Assert.False(bridge.Handle(Cmd("purchase-upgrade", "x", 50)));
        Assert.Equal(10, store.State.Sparks);

        // extreme_tier: affordable, sets the Inescapable door.
        store.State.Sparks = 100;
        Assert.True(bridge.Handle(Cmd("purchase-upgrade", "extreme_tier", 50)));
        Assert.Equal(50, store.State.Sparks);
        Assert.True(store.State.ExtremeUnlocked);
        Assert.Contains("extreme_tier", store.State.PurchasedUpgrades);

        // Already owned: idempotent (rejected, no second debit).
        Assert.False(bridge.Handle(Cmd("purchase-upgrade", "extreme_tier", 1)));
        Assert.Equal(50, store.State.Sparks);
    }

    // ============================ 2. purchase-dial ============================

    [Fact]
    public void PurchaseDial_GoldDebited_FeralRankGated()
    {
        var store = new FakeChaosMetaStore { State = { Gold = 100 }, RankIndex = 0 };
        var bridge = MakeBridge(store);
        // Normal dial: gold-debited, added.
        Assert.True(bridge.Handle(Cmd("purchase-dial", "ripple", 30)));
        Assert.Equal(70, store.State.Gold);
        Assert.Contains("ripple", store.State.PurchasedDials);

        // Feral dial (hydra) below Entranced: rejected.
        Assert.False(bridge.Handle(Cmd("purchase-dial", "hydra", 10)));
        Assert.DoesNotContain("hydra", store.State.PurchasedDials);

        // At/above Entranced: feral allowed.
        store.RankIndex = (int)ChaosRank.Entranced;
        Assert.True(bridge.Handle(Cmd("purchase-dial", "hydra", 20)));
        Assert.Equal(50, store.State.Gold);
        Assert.Contains("hydra", store.State.PurchasedDials);
    }

    [Fact]
    public void PurchaseDial_HerGift_FiresOnceOnFirstShortDial()
    {
        var store = new FakeChaosMetaStore { State = { Gold = 0 } };   // short on the very first dial
        var bark = new FakeBark();
        var bridge = MakeBridge(store, bark);

        // First dial, can't afford -> HER GIFT covers it (once).
        Assert.True(bridge.Handle(Cmd("purchase-dial", "ripple", 30)));
        Assert.True(store.State.GiftGiven);
        Assert.Equal(0, store.State.Gold);
        Assert.Contains("ripple", store.State.PurchasedDials);
        Assert.Equal(1, bark.GiftGivenCount);

        // A later short dial: gift already given -> rejected, not re-fired.
        Assert.False(bridge.Handle(Cmd("purchase-dial", "breath", 30)));
        Assert.DoesNotContain("breath", store.State.PurchasedDials);
        Assert.Equal(1, bark.GiftGivenCount);
    }

    // ============================ 3. buy-consumable-slot ============================

    [Fact]
    public void BuyConsumableSlot_IncrementsToCapThenStops()
    {
        var store = new FakeChaosMetaStore { State = { Sparks = 100000, ConsumableSlots = 1 } };
        var bridge = MakeBridge(store);
        var slot = new JObject { ["op"] = "buy-consumable-slot", ["cost"] = 10 };
        for (int i = 0; i < 10; i++)
            bridge.Handle(slot);
        Assert.Equal(ChaosMetaState.MaxConsumableSlots, store.State.ConsumableSlots);   // capped at 5
        // Only 4 successful buys (1->2->3->4->5): Sparks debited exactly 4*10.
        Assert.Equal(100000 - 4 * 10, store.State.Sparks);
    }

    // ============================ 4. bench-buy ============================

    [Fact]
    public void BenchBuy_WhitelistGoldDebitedIdempotent()
    {
        var store = new FakeChaosMetaStore { State = { Gold = 1000 } };
        var bridge = MakeBridge(store);
        // Accepted console extras only.
        Assert.True(bridge.Handle(Cmd("bench-buy", BenchIds.StartMantra, 50)));
        Assert.Equal(950, store.State.Gold);
        Assert.Contains(BenchIds.StartMantra, store.State.BenchPurchases);

        Assert.True(bridge.Handle(Cmd("bench-buy", BenchIds.Diary, 30)));
        Assert.True(bridge.Handle(Cmd("bench-buy", BenchIds.StatsPanel, 40)));

        // Idempotent.
        Assert.False(bridge.Handle(Cmd("bench-buy", BenchIds.StartMantra, 50)));

        // Unknown / retired pocket id rejected (whitelist).
        Assert.False(bridge.Handle(Cmd("bench-buy", BenchIds.ToyPocket1, 10)));
        Assert.DoesNotContain(BenchIds.ToyPocket1, store.State.BenchPurchases);
    }

    // ============================ 5. first-time ============================

    [Fact]
    public void FirstTime_AwardsOnce_BarkFires_RejectsUnknown()
    {
        var store = new FakeChaosMetaStore();
        var bark = new FakeBark();
        var bridge = MakeBridge(store, bark);

        // Awards the Core ChaosFirstTimes.Amounts value once, bark fired.
        Assert.True(bridge.Handle(Cmd("first-time", ChaosFirstTimes.Taste)));
        Assert.Equal(ChaosFirstTimes.Amounts[ChaosFirstTimes.Taste], store.State.Sparks);   // 5
        Assert.Contains(ChaosFirstTimes.Taste, store.State.FirstTimesAwarded);
        Assert.Equal(new[] { ChaosFirstTimes.Taste }, bark.FirstTimes);

        // Second attempt is a no-op.
        Assert.False(bridge.Handle(Cmd("first-time", ChaosFirstTimes.Taste)));
        Assert.Equal(5, store.State.Sparks);

        // Unknown id rejected.
        Assert.False(bridge.Handle(Cmd("first-time", "first_nonexistent")));
    }

    // ============================ 6. set-lifetime-boon ============================

    [Fact]
    public void SetLifetimeBoon_ClimbsOnePaidStep_RejectsSkips()
    {
        var store = new FakeChaosMetaStore { State = { Sparks = 1000 } };
        var bridge = MakeBridge(store);

        // level 1 from 0, affordable -> climbs one step.
        var lvl1 = new JObject { ["op"] = "set-lifetime-boon", ["id"] = "boon_a", ["level"] = 1, ["cost"] = 50 };
        Assert.True(bridge.Handle(lvl1));
        Assert.Equal(1, store.State.LifetimeBoonLevels["boon_a"]);
        Assert.Equal(950, store.State.Sparks);

        // Skip to level 3 rejected (must be cur+1).
        var skip = new JObject { ["op"] = "set-lifetime-boon", ["id"] = "boon_a", ["level"] = 3, ["cost"] = 50 };
        Assert.False(bridge.Handle(skip));
        Assert.Equal(1, store.State.LifetimeBoonLevels["boon_a"]);

        // level 2 (cur+1) ok.
        var lvl2 = new JObject { ["op"] = "set-lifetime-boon", ["id"] = "boon_a", ["level"] = 2, ["cost"] = 60 };
        Assert.True(bridge.Handle(lvl2));
        Assert.Equal(2, store.State.LifetimeBoonLevels["boon_a"]);
        Assert.Equal(890, store.State.Sparks);
    }

    // ============================ 7. set-flag / add-to-set / remove-from-set ============================

    [Fact]
    public void SetFlag_IsOneWayFalseToTrueOnly()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);

        Assert.True(bridge.Handle(new JObject { ["op"] = "set-flag", ["key"] = "seenEcho" }));
        Assert.True(store.State.SeenEcho);
        // One-way: true -> true is not applied (returns false).
        Assert.False(bridge.Handle(new JObject { ["op"] = "set-flag", ["key"] = "seenEcho" }));
    }

    [Fact]
    public void AddToSet_AddsAndDedupses()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);

        Assert.True(bridge.Handle(new JObject { ["op"] = "add-to-set", ["set"] = "discoveredCodexIds", ["id"] = "bubble:x" }));
        Assert.Contains("bubble:x", store.State.DiscoveredCodexIds);
        // Duplicate add returns false.
        Assert.False(bridge.Handle(new JObject { ["op"] = "add-to-set", ["set"] = "discoveredCodexIds", ["id"] = "bubble:x" }));
    }

    [Fact]
    public void RemoveFromSet_OnlyPendingRevealsRemovable()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);

        // pendingReveals is removable.
        store.State.PendingReveals.Add("dollhouse");
        Assert.True(bridge.Handle(new JObject { ["op"] = "remove-from-set", ["set"] = "pendingReveals", ["id"] = "dollhouse" }));
        Assert.DoesNotContain("dollhouse", store.State.PendingReveals);

        // Any other set is not removable (no-op).
        store.State.DiscoveredCodexIds.Add("bubble:y");
        Assert.False(bridge.Handle(new JObject { ["op"] = "remove-from-set", ["set"] = "discoveredCodexIds", ["id"] = "bubble:y" }));
        Assert.Contains("bubble:y", store.State.DiscoveredCodexIds);
    }

    // ============================ 8. reset-onboarding ============================

    [Fact]
    public void ResetOnboarding_ReArmsFlags_ClearsLessons_PreservesGrants()
    {
        var store = new FakeChaosMetaStore();
        store.State.SeenEcho = true;
        store.State.SeenDollhouse = true;
        store.State.ForceScriptedRun = false;
        store.State.DiscoveredCodexIds.Add("bubble:x");
        store.State.DiscoveredCodexIds.Add("boon:shield");
        store.State.BubbleHintsLearned.Add("tap");
        store.State.SeenReveals.Add("dollhouse");
        store.State.PendingReveals.Add("dollhouse");
        // Grants that must survive.
        store.State.FirstTimesAwarded.Add(ChaosFirstTimes.Taste);
        store.State.GiftGiven = true;
        store.State.LessonProgress["lesson_a"] = 5;

        var bridge = MakeBridge(store);
        Assert.True(bridge.Handle(Cmd("reset-onboarding")));

        // Teaching/guide flags re-armed to false.
        Assert.False(store.State.SeenEcho);
        Assert.False(store.State.SeenDollhouse);
        Assert.True(store.State.ForceScriptedRun);

        // Non-boon discovery ledger + bubble hints cleared; boon discoveries preserved.
        Assert.DoesNotContain("bubble:x", store.State.DiscoveredCodexIds);
        Assert.Contains("boon:shield", store.State.DiscoveredCodexIds);
        Assert.Empty(store.State.BubbleHintsLearned);

        // Dollhouse station flash re-armed (pending + seen both dropped).
        Assert.DoesNotContain("dollhouse", store.State.SeenReveals);
        Assert.DoesNotContain("dollhouse", store.State.PendingReveals);

        // Grants preserved (not lessons).
        Assert.Contains(ChaosFirstTimes.Taste, store.State.FirstTimesAwarded);
        Assert.True(store.State.GiftGiven);
        Assert.Equal(5, store.State.LessonProgress["lesson_a"]);
    }

    // ============================ 9. real-bank AwardRun (testMode:false) ============================

    [Fact]
    public void AwardRun_RealBank_FirstFallOnceThenStatsBumped()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);   // testMode:false
        var input = new ChaosRunRewardInput(
            RunDurationSec: 180, DifficultyMult: 1.0, SparkGainMult: 1.0,
            Score: 1000, TrickleDrops: 0, DripFeedMaxed: false,
            BestCombo: 7, Defused: 12, ElapsedSec: 180);

        // First call: +25 first-fall, and all the lifetime-stat bumps.
        var sparks1 = bridge.AwardRun(input);
        var expected1 = ChaosEconomy.SparkReward(1000, 1.0, 180, 1.0, 0, false, true);
        Assert.Equal(expected1, sparks1);
        Assert.Equal(expected1, store.State.Sparks);
        Assert.Equal(1, store.State.RunsCompleted);
        Assert.Equal(1000, store.State.BestScore);
        Assert.Equal(7, store.State.BestCombo);
        Assert.Equal(12, store.State.TotalDefused);
        Assert.Equal(180, store.State.TotalRunSeconds);
        Assert.True(store.SaveCount >= 1);   // store.Save() called (banking persists internally)

        // Second call: first-fall does NOT re-apply.
        var sparks2 = bridge.AwardRun(input);
        var expected2 = ChaosEconomy.SparkReward(1000, 1.0, 180, 1.0, 0, false, false);
        Assert.Equal(expected2, sparks2);
        Assert.Equal(2, store.State.RunsCompleted);
        Assert.Equal(expected1 + expected2, store.State.Sparks);
    }

    // ============================ 10. TEST-MODE divergence (testMode:true) ============================

    [Fact]
    public void AwardRun_TestMode_NoFirstFall_NoStatBumps_WritesTestFile()
    {
        var env = new TestAppEnvironment();
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store, env: env, testMode: true);
        var input = new ChaosRunRewardInput(180, 1.0, 1.0, 1000, 0, false, 7, 12, 180);
        try
        {
            var sparks = bridge.AwardRun(input);
            bridge.FlushForTest();

            // ComputeSparksOnly deliberately omits the first-fall +25.
            Assert.Equal(ComputeSparksOnlyRef(input), sparks);
            // And it is strictly less than the real-bank first-fall haul.
            Assert.True(sparks < ChaosEconomy.SparkReward(1000, 1.0, 180, 1.0, 0, false, true));

            // The REAL store.State is never touched (the clone is what moved).
            Assert.Equal(0, store.State.Sparks);
            Assert.Equal(0, store.State.RunsCompleted);
            Assert.Equal(0, store.State.BestScore);
            Assert.Equal(0, store.State.BestCombo);          // not bumped in test path
            Assert.Equal(0, store.State.TotalDefused);       // not bumped in test path
            Assert.Equal(0, store.State.TotalRunSeconds);    // not bumped in test path
            Assert.Equal(0, store.SaveCount);                // SaveNow writes the test file, not store.Save()

            // The test-mode save file exists on disk.
            Assert.True(File.Exists(Path.Combine(env.UserDataPath, "chaos_meta.test.json")));
        }
        finally { env.Cleanup(); }
    }

    // ============================ 11. unknown op ============================

    [Fact]
    public void UnknownOp_ReturnsFalseNoMutationNoThrow()
    {
        var store = new FakeChaosMetaStore { State = { Sparks = 42 } };
        var bridge = MakeBridge(store);
        Assert.False(bridge.Handle(Cmd("totally-unknown-op")));
        Assert.Equal(42, store.State.Sparks);             // no mutation
        Assert.Equal(0, store.SaveCount);                 // no save (rejection path)
    }

    // ============================ 12. currency/clamp guards (audit follow-up) ============================

    [Fact]
    public void SpendGold_NeverDrivesBalanceNegative()
    {
        var store = new FakeChaosMetaStore { State = { Gold = 30 } };
        var bridge = MakeBridge(store);
        // amount > balance: rejected, no debit, no negative.
        Assert.False(bridge.Handle(new JObject { ["op"] = "spend-gold", ["amount"] = 50 }));
        Assert.Equal(30, store.State.Gold);
        // amount <= 0: rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "spend-gold", ["amount"] = 0 }));
        Assert.Equal(30, store.State.Gold);
        // affordable: debited exactly.
        Assert.True(bridge.Handle(new JObject { ["op"] = "spend-gold", ["amount"] = 30 }));
        Assert.Equal(0, store.State.Gold);
    }

    [Fact]
    public void AddGold_ClampsToUpperBoundAndRejectsNonPositive()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);
        // amount <= 0 rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "add-gold", ["amount"] = 0 }));
        Assert.Equal(0, store.State.Gold);
        // over the 100000 ceiling rejected (inflation cap).
        Assert.False(bridge.Handle(new JObject { ["op"] = "add-gold", ["amount"] = 100001 }));
        Assert.Equal(0, store.State.Gold);
        // exactly at the ceiling accepted.
        Assert.True(bridge.Handle(new JObject { ["op"] = "add-gold", ["amount"] = 100000 }));
        Assert.Equal(100000, store.State.Gold);
    }

    [Fact]
    public void SetNum_LastRankSeen_OnlyClimbsBelowSixtyFour()
    {
        var store = new FakeChaosMetaStore { State = { LastRankSeen = 3 } };
        var bridge = MakeBridge(store);
        // not greater than current: rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "set-num", ["key"] = "lastRankSeen", ["value"] = 3 }));
        Assert.Equal(3, store.State.LastRankSeen);
        // >= 64 out-of-range: rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "set-num", ["key"] = "lastRankSeen", ["value"] = 64 }));
        Assert.Equal(3, store.State.LastRankSeen);
        // in-range climb: accepted.
        Assert.True(bridge.Handle(new JObject { ["op"] = "set-num", ["key"] = "lastRankSeen", ["value"] = 5 }));
        Assert.Equal(5, store.State.LastRankSeen);
        // unknown key: rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "set-num", ["key"] = "somethingElse", ["value"] = 6 }));
        Assert.Equal(5, store.State.LastRankSeen);
    }

    [Fact]
    public void AddChannelSeconds_AccumulatesInRangeRejectsOutOfBounds()
    {
        var store = new FakeChaosMetaStore();
        var bridge = MakeBridge(store);
        // <= 0 rejected.
        Assert.False(bridge.Handle(new JObject { ["op"] = "add-channel-seconds", ["seconds"] = 0.0 }));
        Assert.Equal(0.0, store.State.TotalChannelSeconds);
        // >= 36000 rejected (10h single-report ceiling).
        Assert.False(bridge.Handle(new JObject { ["op"] = "add-channel-seconds", ["seconds"] = 36000.0 }));
        Assert.Equal(0.0, store.State.TotalChannelSeconds);
        // in-range accumulates.
        Assert.True(bridge.Handle(new JObject { ["op"] = "add-channel-seconds", ["seconds"] = 12.5 }));
        Assert.Equal(12.5, store.State.TotalChannelSeconds);
    }
}
