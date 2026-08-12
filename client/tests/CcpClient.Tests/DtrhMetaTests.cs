using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-026 slice b4: the meta-progression engine on the b2 slot documents (SP-005
/// machinery, no parallel save file, no schema bump). Op matrix (each op's mutation +
/// clamp/validation vs WPF DtrhMetaBridge.cs:89-397), payout math (DtrhHostService.cs:510-613
/// + ChaosUpgrades.cs:578-610), payout-result round-trip, request-run persist→init
/// round-trip, run-config deal, asset-stats, rev/snapshot discipline, tolerance (unknown/
/// malformed never throws), absent-member flagging, media-logging presence+shape rule.
/// </summary>
public class DtrhMetaTests
{
    private static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class Harness : IDisposable
    {
        public TempDir Dir { get; } = new();
        public DtrhSaveSlots Slots { get; }
        public DtrhAssetStats Stats { get; }
        public List<string> Log { get; } = [];
        public List<JsonElement> Broadcasts { get; } = [];
        public DtrhMeta Meta { get; private set; } = null!;

        public Harness()
        {
            Slots = new DtrhSaveSlots(
                new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new Sink(Log)),
                Dir.Root);
            Slots.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Stats = new DtrhAssetStats(Slots.AssetStatsStore, m => Log.Add(m));
            ResetMeta(testMode: false);
        }

        public void ResetMeta(bool testMode, DtrhSlotDocument? testFixture = null)
        {
            // SP-057: test mode declares its starting document EXPLICITLY (the fixture
            // argument); omitting it falls to the committed sentinel fixture — the live
            // slot document is never the clone source.
            Meta = new DtrhMeta(
                Slots.StoreFor(Slots.ActiveSlot), Slots.IndexStore, Stats,
                msg => Broadcasts.Add(JsonSerializer.SerializeToElement(msg)),
                m => Log.Add(m), testMode, Slots.SlotFilePath(Slots.ActiveSlot), testFixture);
        }

        public void Seed(Action<DtrhSlotDocument> mutate) =>
            Slots.StoreFor(Slots.ActiveSlot).Mutate(mutate);

        public DtrhSlotDocument Doc => Slots.StoreFor(Slots.ActiveSlot).Current;

        public void Dispose()
        {
            Slots.StopAsync().GetAwaiter().GetResult();
            Dir.Dispose();
        }

        private sealed class Sink(List<string> log) : ILogSink
        {
            public void Log(string message) => log.Add(message);
        }

        public sealed class TempDir : IDisposable
        {
            public TempDir()
            {
                Root = Path.Combine(Path.GetTempPath(), "ccp-sp026-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public void Dispose()
            {
                try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    private static JsonElement LastSnapshot(Harness h)
    {
        var last = h.Broadcasts[^1];
        Assert.Equal("meta", last.GetProperty("type").GetString());
        return last;
    }

    // ============================ op matrix ============================

    [Fact]
    public void PurchaseUpgrade_Affordable_Unowned_Applies_AndExtremeTierSetsFlag()
    {
        using var h = new Harness();
        h.Seed(d => d.Sparks = 400);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-upgrade\",\"id\":\"slow_fuses\",\"cost\":120}")));
        Assert.Equal(280, h.Doc.Sparks);
        Assert.Contains("slow_fuses", h.Doc.PurchasedUpgrades);
        Assert.False(h.Doc.ExtremeUnlocked);

        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-upgrade\",\"id\":\"extreme_tier\",\"cost\":200}")));
        Assert.True(h.Doc.ExtremeUnlocked); // :104 — the Inescapable door opens at purchase time

        // Rejected: already owned / insufficient — no mutation, no rev bump.
        var rev = h.Meta.Rev;
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-upgrade\",\"id\":\"slow_fuses\",\"cost\":1}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-upgrade\",\"id\":\"draft4\",\"cost\":9999}")));
        Assert.Equal(rev, h.Meta.Rev);
    }

    [Fact]
    public void ToggleUpgrade_SetSemantics_ChangeOnly()
    {
        using var h = new Harness();
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"toggle-upgrade\",\"id\":\"slow_fuses\",\"on\":false}")));
        Assert.Contains("slow_fuses", h.Doc.DisabledUpgrades);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"toggle-upgrade\",\"id\":\"slow_fuses\",\"on\":false}"))); // unchanged
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"toggle-upgrade\",\"id\":\"slow_fuses\",\"on\":true}")));
        Assert.DoesNotContain("slow_fuses", h.Doc.DisabledUpgrades);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"toggle-upgrade\",\"on\":true}"))); // no id
    }

    [Fact]
    public void PurchaseDial_GoldPath_RankGate_AndHerGift()
    {
        using var h = new Harness();
        // Rank gate: hydra needs Entranced (3 runs is Tempted — rejected).
        h.Seed(d => { d.Gold = 100; d.RunsCompleted = 1; });
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"hydra\",\"cost\":10}")));
        Assert.Empty(h.Doc.PurchasedDials);

        // Gold path: affordable unranked dial.
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"bubbleSize\",\"cost\":25}")));
        Assert.Equal(75, h.Doc.Gold);
        Assert.Contains("bubbleSize", h.Doc.PurchasedDials);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"bubbleSize\",\"cost\":1}"))); // owned

        // Rank gate opens at Entranced (25 runs; thresholds {0,3,10,25,50,100}).
        h.Seed(d => d.RunsCompleted = 25);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"hydra\",\"cost\":10}")));
        Assert.Contains("hydra", h.Doc.PurchasedDials);
    }

    [Fact]
    public void PurchaseDial_HerGift_CoversShortBalance_OnFirstDialOnly()
    {
        using var h = new Harness();
        h.Seed(d => d.Gold = 5); // short of the 25 cost; first dial ever
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"bubbleSize\",\"cost\":25}")));
        Assert.True(h.Doc.GiftGiven);
        Assert.Equal(0, h.Doc.Gold); // the gift zeroes the balance
        Assert.Contains("bubbleSize", h.Doc.PurchasedDials);

        // Second dial: gift already given — short balance rejects.
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"purchase-dial\",\"id\":\"autoplay\",\"cost\":25}")));
        Assert.DoesNotContain("autoplay", h.Doc.PurchasedDials);
    }

    [Fact]
    public void SetLifetimeBoon_ClimbOnlyPaidStep_AndActiveHalf()
    {
        using var h = new Harness();
        h.Seed(d => d.Sparks = 100);
        // Level must be exactly cur+1.
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":\"collar\",\"level\":2,\"cost\":10}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":\"collar\",\"level\":1,\"cost\":10}")));
        Assert.Equal(90, h.Doc.Sparks);
        Assert.Equal(1, h.Doc.LifetimeBoonLevels["collar"]);
        // Active half independent.
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":\"collar\",\"active\":true}")));
        Assert.Contains("collar", h.Doc.ActiveLifetimeBoons);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":\"collar\",\"active\":true}"))); // unchanged
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":\"collar\",\"active\":false}")));
        Assert.DoesNotContain("collar", h.Doc.ActiveLifetimeBoons);
    }

    [Fact]
    public void GoldOps_Bounds()
    {
        using var h = new Harness();
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":0}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":100001}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":50}")));
        Assert.Equal(50, h.Doc.Gold);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"spend-gold\",\"amount\":99999999}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"spend-gold\",\"amount\":20}")));
        Assert.Equal(30, h.Doc.Gold);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"equip-boon\",\"id\":\"m2test_boon\"}")));
        Assert.Equal("m2test_boon", h.Doc.EquippedStartBoon);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"equip-boon\",\"id\":\"\"}")));
        Assert.Null(h.Doc.EquippedStartBoon);
    }

    [Fact]
    public void Crafting_MaterialAdd_Craft_ConsumeDiscipline()
    {
        using var h = new Harness();
        // Whitelist + amount bounds.
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"material-add\",\"id\":\"unobtanium\",\"amount\":5}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"material-add\",\"id\":\"chrome\",\"amount\":31}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"material-add\",\"id\":\"chrome\",\"amount\":30}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"material-add\",\"id\":\"silicone\",\"amount\":5}")));
        Assert.Equal(30, h.Doc.Materials["chrome"]);

        // Craft: shape validation (bad material, cell bounds, affordability).
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"the_padlock\",\"cost\":{\"unobtanium\":1}}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"the_padlock\",\"cost\":{\"chrome\":10}}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"not_a_recipe\",\"cost\":{\"chrome\":1}}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"the_padlock\",\"cost\":{\"chrome\":8}}")));
        Assert.Equal(22, h.Doc.Materials["chrome"]);
        Assert.Contains("the_padlock", h.Doc.DiscoveredRecipes);
        Assert.Equal(1, h.Doc.CraftedItems["the_padlock"]);

        // pin-boon now unlocked by the padlock.
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"pin-boon\",\"id\":\"m2test_pin\"}")));
        Assert.Equal("m2test_pin", h.Doc.PinnedBoon);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"pin-boon\",\"id\":null}")));
        Assert.Null(h.Doc.PinnedBoon);

        // consume-crafted: permanents never spendable; consumables decrement then drop.
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"consume-crafted\",\"id\":\"the_padlock\"}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"material-add\",\"id\":\"pills\",\"amount\":10}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"sugar_cube\",\"cost\":{\"pills\":4}}")));
        Assert.Equal(1, h.Doc.CraftedItems["sugar_cube"]);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"sugar_cube\",\"cost\":{\"pills\":4}}")));
        Assert.Equal(2, h.Doc.CraftedItems["sugar_cube"]);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"consume-crafted\",\"id\":\"sugar_cube\"}")));
        Assert.Equal(1, h.Doc.CraftedItems["sugar_cube"]);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"consume-crafted\",\"id\":\"sugar_cube\"}")));
        Assert.DoesNotContain("sugar_cube", h.Doc.CraftedItems.Keys);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"consume-crafted\",\"id\":\"sugar_cube\"}"))); // none held
    }

    [Fact]
    public void SetDenial_TwoWay_CageGated()
    {
        using var h = new Harness();
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-denial\",\"on\":true}"))); // no cage
        h.Seed(d => d.CraftedItems["the_cage"] = 1);
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-denial\",\"on\":true}")));
        Assert.True(h.Doc.DenialArmed);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-denial\",\"on\":true}"))); // unchanged
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-denial\",\"on\":false}"))); // disarm is free
        Assert.False(h.Doc.DenialArmed);
    }

    [Fact]
    public void BuyConsumableSlot_AndBenchBuy_Whitelists()
    {
        using var h = new Harness();
        h.Seed(d => { d.Sparks = 1000; d.Gold = 100; d.ConsumableSlots = 4; });
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"buy-consumable-slot\",\"cost\":50}")));
        Assert.Equal(5, h.Doc.ConsumableSlots);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"buy-consumable-slot\",\"cost\":50}"))); // cap 5

        // bench-buy: only the three console extras; pocket ids rejected (gold cutover).
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"bench-buy\",\"id\":\"toy_pocket_1\",\"cost\":1}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"bench-buy\",\"id\":\"stats_panel\",\"cost\":10}")));
        Assert.Contains("stats_panel", h.Doc.BenchPurchases);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"bench-buy\",\"id\":\"stats_panel\",\"cost\":1}"))); // owned
    }

    [Fact]
    public void FirstTime_HostOwnedAmounts_OnceEver()
    {
        using var h = new Harness();
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"first-time\",\"id\":\"not_a_first\"}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"first-time\",\"id\":\"first_snap\"}")));
        Assert.Equal(10, h.Doc.Sparks);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"first-time\",\"id\":\"first_snap\"}"))); // once ever
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"first-time\",\"id\":\"first_yes\"}")));
        Assert.Equal(25, h.Doc.Sparks);
    }

    [Fact]
    public void LessonProgress_ClimbOnly_WithComplete()
    {
        using var h = new Harness();
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"lesson-progress\",\"id\":\"slow_fuses\",\"value\":3}")));
        Assert.Equal(3, h.Doc.LessonProgress["slow_fuses"]);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"lesson-progress\",\"id\":\"slow_fuses\",\"value\":2}"))); // climb-only
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"lesson-progress\",\"id\":\"slow_fuses\",\"value\":9,\"complete\":true}")));
        Assert.Equal(9, h.Doc.LessonProgress["slow_fuses"]);
        Assert.Contains("slow_fuses", h.Doc.LessonsComplete);
    }

    [Fact]
    public void SetFlag_OneWay_OnlyBools_UnknownKeyRejected()
    {
        using var h = new Harness();
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-flag\",\"key\":\"seenDefuseTutorial\"}")));
        Assert.True(h.Doc.SeenDefuseTutorial);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-flag\",\"key\":\"seenDefuseTutorial\"}"))); // already true
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-flag\",\"key\":\"sparks\"}")));      // not a bool
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-flag\",\"key\":\"notAFlag\"}")));      // unknown
    }

    [Fact]
    public void SetOps_AddAnySet_RemovePendingRevealsOnly()
    {
        using var h = new Harness();
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-to-set\",\"set\":\"discoveredCodexIds\",\"id\":\"bubble:m2test\"}")));
        Assert.Contains("bubble:m2test", h.Doc.DiscoveredCodexIds);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-to-set\",\"set\":\"discoveredCodexIds\",\"id\":\"bubble:m2test\"}"))); // dupe
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-to-set\",\"set\":\"paperwallSketches\",\"id\":\"m2test_sketch\"}")));
        Assert.Contains("m2test_sketch", h.Doc.PaperwallSketches);

        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"remove-from-set\",\"set\":\"discoveredCodexIds\",\"id\":\"bubble:m2test\"}"))); // not removable
        h.Seed(d => d.PendingReveals.Add("dollhouse"));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"remove-from-set\",\"set\":\"pendingReveals\",\"id\":\"dollhouse\"}")));
        Assert.DoesNotContain("dollhouse", h.Doc.PendingReveals);
    }

    [Fact]
    public void SetNum_MapSet_ChannelSeconds_ClimbRules()
    {
        using var h = new Harness();
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"lastRankSeen\",\"value\":1}")));
        Assert.Equal(1, h.Doc.LastRankSeen);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"lastRankSeen\",\"value\":1}")));  // climb-only
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"lastRankSeen\",\"value\":64}"))); // < 64
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"tutorialStage\",\"value\":6}")));
        Assert.Equal(6, h.Doc.TutorialStage);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"tutorialStage\",\"value\":17}"))); // <= 16
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"sparks\",\"value\":999}")));        // unknown key

        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"map-set\",\"map\":\"narrativeCooldownEnds\",\"id\":\"cheshire:greeting\",\"value\":12345}")));
        Assert.Equal(12345, h.Doc.NarrativeCooldownEnds["cheshire:greeting"]);
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"map-set\",\"map\":\"materials\",\"id\":\"chrome\",\"value\":5}")));

        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-channel-seconds\",\"seconds\":0}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-channel-seconds\",\"seconds\":36000}")));
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-channel-seconds\",\"seconds\":120.5}")));
        Assert.Equal(120.5, h.Doc.TotalChannelSeconds);
    }

    [Fact]
    public void ResetOnboarding_RearmsTeaching_KeepsRewards()
    {
        using var h = new Harness();
        h.Seed(d =>
        {
            d.SeenDefuseTutorial = true;
            d.SeenWarrenWelcome = true;
            d.TutorialStage = 6;
            d.Sparks = 500;
            d.FirstTimesAwarded.Add("first_snap");
            d.GiftGiven = true;
            d.DiscoveredCodexIds.Add("bubble:x");
            d.DiscoveredCodexIds.Add("boon:y");
            d.SeenNarrativeLines.Add("cheshire:a");
            d.SeenNarrativeLines.Add("madam:b");
            d.NarrativeCooldownEnds["cheshire:a"] = 1;
            d.NarrativeCooldownEnds["madam:b"] = 2;
        });
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"reset-onboarding\"}")));
        Assert.False(h.Doc.SeenDefuseTutorial);
        Assert.False(h.Doc.SeenWarrenWelcome);
        Assert.Equal(0, h.Doc.TutorialStage);
        Assert.True(h.Doc.ForceScriptedRun); // the classroom re-arms
        // Rewards stay.
        Assert.Equal(500, h.Doc.Sparks);
        Assert.Contains("first_snap", h.Doc.FirstTimesAwarded);
        Assert.True(h.Doc.GiftGiven);
        // Boons stay discovered; bubbles re-discover; cheshire rows purged, madam kept.
        Assert.DoesNotContain("bubble:x", h.Doc.DiscoveredCodexIds);
        Assert.Contains("boon:y", h.Doc.DiscoveredCodexIds);
        Assert.DoesNotContain("cheshire:a", h.Doc.SeenNarrativeLines);
        Assert.Contains("madam:b", h.Doc.SeenNarrativeLines);
        Assert.False(h.Doc.NarrativeCooldownEnds.ContainsKey("cheshire:a"));
        Assert.Equal(2, h.Doc.NarrativeCooldownEnds["madam:b"]);
    }

    [Fact]
    public void UnknownOp_LoggedIgnored_NeverThrows_NoRevBump()
    {
        using var h = new Harness();
        var rev = h.Meta.Rev;
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"definitely-not-an-op\"}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"buy-pocket\",\"kind\":\"toy\",\"cost\":10}")));      // retired
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"bench-purchase\",\"id\":\"stats_panel\",\"cost\":10}"))); // retired
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"noOp\":true}")));
        Assert.Equal(rev, h.Meta.Rev);
        Assert.Contains(h.Log, m => m.Contains("unknown op"));
    }

    [Fact]
    public void AppliedOp_BumpsRev_BroadcastsSnapshot_CamelCase_NoExtensionData()
    {
        using var h = new Harness();
        h.Seed(d => d.ExtensionData = new Dictionary<string, JsonElement> { ["foreignThing"] = Raw("42") });
        var rev0 = h.Meta.Rev;
        var bc0 = h.Broadcasts.Count;
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":50}")));
        Assert.Equal(rev0 + 1, h.Meta.Rev);
        Assert.Equal(bc0 + 1, h.Broadcasts.Count);
        var snap = LastSnapshot(h);
        Assert.Equal(h.Meta.Rev, snap.GetProperty("rev").GetInt32());
        Assert.False(snap.GetProperty("testMode").GetBoolean());
        var state = snap.GetProperty("state");
        Assert.Equal(50, state.GetProperty("gold").GetInt32());
        Assert.True(state.TryGetProperty("purchasedDials", out _));
        Assert.True(state.TryGetProperty("seenDefuseTutorial", out _));
        Assert.True(state.TryGetProperty("forceScriptedRun", out _));
        Assert.True(state.TryGetProperty("tutorialStage", out _));
        Assert.True(state.TryGetProperty("craftedItems", out _));
        Assert.Equal(1, state.GetProperty("consumableSlots").GetInt32());
        // Extension-data members never reach the page (consult item 1).
        Assert.False(state.TryGetProperty("foreignThing", out _));
        Assert.False(state.TryGetProperty("extensionData", out _));
    }

    // ============================ payout ============================

    [Fact]
    public void Payout_M2TestCase_XpCapAndSparkFormula_MatchWpf()
    {
        using var h = new Harness();
        // The m2test.js payout round-trip case (score 12000, 180s, mult 1.0, trickle 5).
        var payout = h.Meta.OnRunEnded(Raw(
            "{\"score\":12000,\"durationSec\":180,\"elapsedSec\":180,\"difficulty\":\"Gentle\",\"difficultyMult\":1.0,"
            + "\"sparkGainMult\":1.0,\"bestCombo\":14,\"defused\":9,\"trickleDrops\":5,\"dripFeedMaxed\":false}"));
        Assert.Equal(750, payout.BaseXp);      // capBase = 250 * 3min * 1.0 (m2test.js:143)
        Assert.Equal(1.0, payout.SkillMult);   // no skill tree — WPF's own ?? 1.0 fallback (:526)
        Assert.Equal(750, payout.FinalXp);
        // sparks: round((1.5*sqrt(12000) + 35*1*min(1,3/3)) * 1) + 5 trickle = 199 + 5 = 204,
        // + FIRST_FALL_BONUS 25 (RunsCompleted was 0, real mode) = 229.
        Assert.Equal(229, payout.SparksEarned);
        Assert.Equal(0, payout.PreviousBest);
        Assert.Null(payout.RankUp);
        Assert.False(payout.DryRun);
        // Banking: all six fields (ChaosUpgrades.cs:602-608).
        Assert.Equal(229, h.Doc.Sparks);
        Assert.Equal(1, h.Doc.RunsCompleted);
        Assert.Equal(12000, h.Doc.BestScore);
        Assert.Equal(14, h.Doc.BestCombo);
        Assert.Equal(9, h.Doc.TotalDefused);
        Assert.Equal(180, h.Doc.TotalRunSeconds);
    }

    [Fact]
    public void Payout_BelowCap_UsesScore_ClampsInputs()
    {
        using var h = new Harness();
        var payout = h.Meta.OnRunEnded(Raw(
            "{\"score\":100,\"durationSec\":0,\"elapsedSec\":99999,\"difficultyMult\":99,\"sparkGainMult\":0.01}"));
        // durationSec clamps to >=1 (:517); diffMult clamps to 5.0 (:519); sparkGainMult to 0.5 (:520).
        // capBase = 250 * (1/60) * 5.0 = 20.83; baseXp = min(100, 20.83).
        Assert.Equal(250.0 / 60 * 5.0, payout.BaseXp, 3);
        // elapsedSec clamps to 2x duration (:518) → banked run seconds = 2.
        Assert.Equal(2, h.Doc.TotalRunSeconds);
    }

    [Fact]
    public void Payout_DripFeedAndShotMult_Stack()
    {
        using var h = new Harness();
        h.Seed(d => { d.RunsCompleted = 5; d.CraftedItems["the_shot"] = 10; }); // shot mult 1.4
        var payout = h.Meta.OnRunEnded(Raw(
            "{\"score\":400,\"durationSec\":180,\"difficultyMult\":1.0,\"sparkGainMult\":1.0,\"trickleDrops\":0,\"dripFeedMaxed\":true}"));
        // base: round((1.5*20 + 35) * 1) = 65; dripfeed: round(65*1.10) = 72; shot: round(72*1.4) = 101.
        Assert.Equal(101, payout.SparksEarned);
    }

    [Fact]
    public void Payout_PreviousBest_ReadBeforeBanking_RankUpAfterBanking()
    {
        using var h = new Harness();
        h.Seed(d => { d.BestScore = 5000; d.RunsCompleted = 2; }); // next run is the 3rd → Tempted
        var payout = h.Meta.OnRunEnded(Raw("{\"score\":100,\"durationSec\":60,\"difficultyMult\":1.0}"));
        Assert.Equal(5000, payout.PreviousBest);
        Assert.Equal(5000, h.Doc.BestScore); // the old best survives a smaller score
        Assert.Equal(3, h.Doc.RunsCompleted);
        Assert.Equal("Tempted", payout.RankUp); // evaluated AFTER banking (:563-566)
        // The page acknowledges the rank card via set-num; until then the host keeps
        // reporting the pending rank-up (WPF: rankUp = nowRank > LastRankSeen).
        Assert.True(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-num\",\"key\":\"lastRankSeen\",\"value\":1}")));
        var payout2 = h.Meta.OnRunEnded(Raw("{\"score\":100,\"durationSec\":60,\"difficultyMult\":1.0}"));
        Assert.Null(payout2.RankUp);
    }

    [Fact]
    public void Payout_TestMode_DryRun_CloneOnlyBanking_RealSaveUntouched()
    {
        using var h = new Harness();
        h.Seed(d => { d.Sparks = 7; d.RunsCompleted = 3; d.BestScore = 42; });
        // SP-057: the test run's starting state is DECLARED (fixture argument), never the
        // seeded live document — the values mirror the old clone so the payout math below
        // pins the same lines.
        h.ResetMeta(testMode: true, testFixture: new DtrhSlotDocument { Sparks = 7, RunsCompleted = 3, BestScore = 42 });
        var payout = h.Meta.OnRunEnded(Raw(
            "{\"score\":12000,\"durationSec\":180,\"elapsedSec\":180,\"difficultyMult\":1.0,\"sparkGainMult\":1.0,\"trickleDrops\":5}"));
        Assert.True(payout.DryRun);
        Assert.Equal(0, payout.PreviousBest);   // test mode reports 0 (:529)
        Assert.Null(payout.RankUp);             // rank detection is non-test only (:546-573)
        Assert.Equal(204, payout.SparksEarned); // the mirror omits FIRST_FALL_BONUS (:446-456)
        // The REAL save never moves.
        Assert.Equal(7, h.Doc.Sparks);
        Assert.Equal(3, h.Doc.RunsCompleted);
        Assert.Equal(42, h.Doc.BestScore);
    }

    [Fact]
    public void AwardRun_TestMirror_BanksOnlyThreeFields()
    {
        using var h = new Harness();
        h.ResetMeta(testMode: true, testFixture: new DtrhSlotDocument()); // SP-057: declared fresh fixture
        var sparks = h.Meta.AwardRun(new DtrhMeta.RunRewardInput(
            RunDurationSec: 180, DifficultyMult: 1.0, SparkGainMult: 1.0, Score: 400,
            TrickleDrops: 0, DripFeedMaxed: false, BestCombo: 12, Defused: 7, ElapsedSec: 180));
        Assert.Equal(65, sparks); // round(1.5*20 + 35) — no first-fall in the mirror
        // Real doc untouched entirely.
        Assert.Equal(0, h.Doc.Sparks);
        Assert.Equal(0, h.Doc.RunsCompleted);
    }

    // ============================ request-run / run-config ============================

    [Fact]
    public void RequestRun_FirstRun_DealsScriptedClassroom()
    {
        using var h = new Harness();
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.True(cfg.GetProperty("scriptedFirstRun").GetBoolean());
        Assert.Equal("Easy", cfg.GetProperty("difficulty").GetString());
        Assert.Equal(1.0, cfg.GetProperty("difficultyMult").GetDouble());
        Assert.Equal(180, cfg.GetProperty("durationSec").GetInt32());
        Assert.Equal(0.6, cfg.GetProperty("spawnRateMult").GetDouble()); // R1_SPAWN_RATE_MULT
        Assert.False(cfg.GetProperty("boonDraftEnabled").GetBoolean());
        Assert.False(cfg.GetProperty("allowCurses").GetBoolean());
        Assert.False(cfg.GetProperty("dartersEnabled").GetBoolean());
        Assert.Equal(0.0, cfg.GetProperty("sinChance").GetDouble());
        var variants = cfg.GetProperty("enabledVariants").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Assert.Equal(["flash", "subliminal"], variants); // treats only (ChaosHappyPath.cs:77-89)
    }

    [Fact]
    public void RequestRun_NonOwner_PersistsSetup_ClampsToPresetCeiling_AndInitRoundTrips()
    {
        // SP-052: this b4 test asserted the UNCONDITIONAL 1200 clamp (the defect). Updated,
        // not weakened: the harness owns nothing, so 1200 is now the NON-OWNER ceiling —
        // the exact main line it matches is cited per assertion; the owner branch has its
        // own test below (RequestRun_Owner_PersistsSetup_HourglassCeiling_RoundTrips).
        using var h = new Harness();
        h.Seed(d => d.RunsCompleted = 5); // past the scripted classroom
        var setup = Raw(
            "{\"difficulty\":\"Hard\",\"durationSec\":99999,\"waveCount\":0,\"motion\":\"RainDown\","
            + "\"effectIntensity\":9.0,\"colorFlashes\":false,\"boonDraftEnabled\":false,"
            + "\"allowCurses\":false,\"dartersEnabled\":false,\"key1\":\"A\",\"key2\":\"S\","
            + "\"enabledVariants\":[\"flash\",\"gif\"],\"endless\":true}");
        h.Meta.OnRequestRun(setup);
        var idx = h.Slots.IndexStore.Current;
        Assert.Equal("Hard", idx.Difficulty);
        // Non-owner branch of the ownership-gated ceiling (DtrhHostService.cs:474-475).
        Assert.Equal(1200, idx.DurationSec);
        Assert.Equal(1, idx.WaveCount);        // clamp (:476)
        // Stale-page refusal: endless never sticks for a non-owner (DtrhHostService.cs:478-480).
        Assert.False(idx.Endless);
        Assert.Equal("RainDown", idx.Motion);
        Assert.Equal(1.5, idx.EffectIntensity); // clamp (:435)
        Assert.False(idx.ColorFlashes);
        Assert.False(idx.BoonDraftEnabled);
        Assert.False(idx.AllowCurses);
        Assert.False(idx.DartersEnabled);
        Assert.Equal("A", idx.Key1);
        Assert.Equal("S", idx.Key2);
        Assert.Equal(["flash", "gif"], idx.EnabledVariants!.ToArray());

        // The lower bound is shared by both branches (60, :475) — the non-owner asserts it too.
        h.Meta.OnRequestRun(Raw("{\"durationSec\":10}"));
        Assert.Equal(60, h.Slots.IndexStore.Current.DurationSec);
        h.Meta.OnRequestRun(Raw("{\"durationSec\":99999}")); // restore the ceiling cell for the round-trips below
        Assert.Equal(1200, h.Slots.IndexStore.Current.DurationSec);

        // init's runSetup reads the raw saved values back (BuildRunSetup :447-478).
        var initSetup = DtrhMeta.BuildRunSetupPayload(idx);
        Assert.Equal("Hard", initSetup.Difficulty);
        Assert.Equal(1200, initSetup.DurationSec);
        Assert.False(initSetup.Endless); // init carries the saved toggle (DtrhHostService.cs:509)
        Assert.Equal("RainDown", initSetup.Motion);
        Assert.Equal("A", initSetup.Key1);
        Assert.Equal(["flash", "gif"], initSetup.EnabledVariants!.ToArray());

        // The dealt config carries the parsed difficulty + its mult (ChaosModels.cs:276-283).
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal("Hard", cfg.GetProperty("difficulty").GetString());
        Assert.Equal(1.7, cfg.GetProperty("difficultyMult").GetDouble());
        Assert.Equal("RainDown", cfg.GetProperty("motionOverride").GetString());
        // Non-owner deal branch of the ownership-gated ceiling (ChaosModels.cs:203-204).
        Assert.Equal(1200, cfg.GetProperty("durationSec").GetInt32());
        // Deal-time ownership re-check: no endless without endless_mode (ChaosModels.cs:206).
        Assert.False(cfg.GetProperty("endless").GetBoolean());
        Assert.False(cfg.GetProperty("scriptedFirstRun").GetBoolean());
        // Sin ramp at 5 runs: 0.25 + (0.25 * 3/8) (ChaosModels.cs:226-237).
        Assert.Equal(0.34375, cfg.GetProperty("sinChance").GetDouble(), 5);
    }

    [Fact]
    public void RequestRun_Owner_PersistsSetup_HourglassCeiling_RoundTrips()
    {
        // The Hourglass: a custom_duration owner's ceiling is 2h at BOTH host points
        // (persist DtrhHostService.cs:474-475; deal ChaosModels.cs:203-204).
        using var h = new Harness();
        h.Seed(d => { d.RunsCompleted = 5; d.PurchasedUpgrades.Add("custom_duration"); });

        // Owner boundary matrix at persist: 1201 survives, 7200 survives, 7201 clamps 7200.
        h.Meta.OnRequestRun(Raw("{\"durationSec\":1201}"));
        Assert.Equal(1201, h.Slots.IndexStore.Current.DurationSec);
        h.Meta.OnRequestRun(Raw("{\"durationSec\":7200}"));
        Assert.Equal(7200, h.Slots.IndexStore.Current.DurationSec);
        h.Meta.OnRequestRun(Raw("{\"durationSec\":7201}"));
        Assert.Equal(7200, h.Slots.IndexStore.Current.DurationSec);
        h.Meta.OnRequestRun(Raw("{\"durationSec\":99999}"));
        Assert.Equal(7200, h.Slots.IndexStore.Current.DurationSec);

        // The lower bound is unchanged by the unlock (60 both branches, :475).
        h.Meta.OnRequestRun(Raw("{\"durationSec\":10}"));
        Assert.Equal(60, h.Slots.IndexStore.Current.DurationSec);

        // A >20min owner value round-trips RAW through init's runSetup (:447-478, :509).
        h.Meta.OnRequestRun(Raw("{\"durationSec\":1500}"));
        Assert.Equal(1500, DtrhMeta.BuildRunSetupPayload(h.Slots.IndexStore.Current).DurationSec);

        // ... and deals >=1201s in the run-config (owner deal branch, ChaosModels.cs:203-204).
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal(1500, cfg.GetProperty("durationSec").GetInt32());
    }

    [Fact]
    public void RequestRun_DealTime_RechecksOwnership_AgainstStaleIndex()
    {
        // The cross-slot shape (pre-approach consult addition 1): the index doc is
        // app-global, ownership is per-slot — an owner persists 7200 / endless, then the
        // CURRENT owner lacks the unlocks (modeled by removal; the deal gate reads the
        // current doc's PurchasedUpgrades either way). Deal re-clamps both (ChaosModels.cs:203-206).
        using var h = new Harness();
        h.Seed(d =>
        {
            d.RunsCompleted = 5;
            d.PurchasedUpgrades.Add("custom_duration");
            d.PurchasedUpgrades.Add("endless_mode");
        });
        h.Meta.OnRequestRun(Raw("{\"durationSec\":7200,\"endless\":true}"));
        Assert.Equal(7200, h.Slots.IndexStore.Current.DurationSec);
        Assert.True(h.Slots.IndexStore.Current.Endless);

        h.Seed(d => { d.PurchasedUpgrades.Remove("custom_duration"); d.PurchasedUpgrades.Remove("endless_mode"); });
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal(1200, cfg.GetProperty("durationSec").GetInt32()); // deal re-clamp (:203-204)
        Assert.False(cfg.GetProperty("endless").GetBoolean());         // deal re-check (:206)
    }

    [Fact]
    public void RequestRun_Endless_OwnedToggle_RoundTrips_AndStaysOffHabitRail()
    {
        // The Bottomless Fall end-to-end (SP-052 defect 2): gated persist
        // (DtrhHostService.cs:478-480), init carry (:509), run-config carry (:1043),
        // deal re-check (ChaosModels.cs:206), habit-rail exclusion (:1073-1074).
        using var h = new Harness();
        h.Seed(d =>
        {
            d.RunsCompleted = 5;
            d.PurchasedUpgrades.Add("endless_mode");
            d.PurchasedUpgrades.Add("custom_duration");
            d.PurchasedUpgrades.Add("slow_fuses");
        });

        h.Meta.OnRequestRun(Raw("{\"endless\":true}"));
        Assert.True(h.Slots.IndexStore.Current.Endless);

        // init's runSetup carries the saved toggle (:509).
        Assert.True(DtrhMeta.BuildRunSetupPayload(h.Slots.IndexStore.Current).Endless);

        // The dealt run-config carries rc.endless (:1043) for the owner.
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.True(cfg.GetProperty("endless").GetBoolean());

        // Setup-shape unlocks stay off the HUD rail (:1073-1074): only slow_fuses lists.
        var habits = cfg.GetProperty("ownedHabitIds").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Assert.Equal(["slow_fuses"], habits);

        // Absent-key discipline: a setup WITHOUT "endless" never clears the saved toggle
        // (main mutates only when setup["endless"] != null, :480).
        h.Meta.OnRequestRun(Raw("{\"difficulty\":\"Medium\"}"));
        Assert.True(h.Slots.IndexStore.Current.Endless);

        // An explicit false clears it for the owner (false && owned = false, :480).
        h.Meta.OnRequestRun(Raw("{\"endless\":false}"));
        Assert.False(h.Slots.IndexStore.Current.Endless);
    }

    [Fact]
    public void RequestRun_EnabledVariantsNull_PersistsNull()
    {
        using var h = new Harness();
        h.Seed(d => d.RunsCompleted = 1);
        h.Meta.OnRequestRun(Raw("{\"enabledVariants\":null}"));
        Assert.Null(h.Slots.IndexStore.Current.EnabledVariants);
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal(JsonValueKind.Null, cfg.GetProperty("enabledVariants").ValueKind);
    }

    [Fact]
    public void RequestRun_ForceScriptedRun_ConsumedAtDealTime_WithRebroadcast()
    {
        using var h = new Harness();
        h.Seed(d => { d.RunsCompleted = 7; d.ForceScriptedRun = true; });
        var bc0 = h.Broadcasts.Count;
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.True(cfg.GetProperty("scriptedFirstRun").GetBoolean()); // the one-shot fires
        Assert.False(h.Doc.ForceScriptedRun);                          // consumed at DEAL time (:403-411)
        Assert.True(h.Broadcasts.Count > bc0);                         // rebroadcast so JS agrees
        // Second request deals the NORMAL config (a missed clear can't deal a 2nd classroom).
        var cfg2 = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.False(cfg2.GetProperty("scriptedFirstRun").GetBoolean());
    }

    [Fact]
    public void RequestRun_OwnedUpgradeMultipliers_Apply_AndHabitFilter()
    {
        using var h = new Harness();
        h.Seed(d =>
        {
            d.RunsCompleted = 5;
            d.PurchasedUpgrades.Add("slow_fuses");
            d.PurchasedUpgrades.Add("silk_touch");
            d.PurchasedUpgrades.Add("extreme_tier");
            d.PurchasedUpgrades.Add("draft4");
            d.DisabledUpgrades.Add("draft4"); // switched off — no effect, not a habit this run
        });
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal(1.15, cfg.GetProperty("fuseTimeMult").GetDouble(), 5);
        Assert.Equal(1.25, cfg.GetProperty("hitboxScale").GetDouble(), 5);
        Assert.True(cfg.GetProperty("magnetEnabled").GetBoolean());
        Assert.Equal(3, cfg.GetProperty("draftChoices").GetInt32()); // draft4 disabled
        var habits = cfg.GetProperty("ownedHabitIds").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("slow_fuses", habits);
        Assert.Contains("silk_touch", habits);
        Assert.DoesNotContain("extreme_tier", habits); // unlock flag only — filtered (:998-1002)
        Assert.DoesNotContain("draft4", habits);
    }

    [Fact]
    public void RequestRun_MetaDerivedFields_RideTheConfig()
    {
        using var h = new Harness();
        h.Seed(d =>
        {
            d.RunsCompleted = 26;
            d.EquippedStartBoon = "boon_x";
            d.ConsumableSlots = 3;
            d.LifetimeBoonLevels["collar"] = 2;
            d.DiscoveredCodexIds.Add("bubble:a");
            d.CraftedItems["the_padlock"] = 1;
            d.PinnedBoon = "boon_pin";
            d.DenialArmed = true;
            d.SeenDefuseTutorial = true;
        });
        var cfg = JsonSerializer.SerializeToElement(h.Meta.OnRequestRun(null));
        Assert.Equal(26, cfg.GetProperty("runsCompleted").GetInt32());
        Assert.Equal(3, cfg.GetProperty("rankIndex").GetInt32()); // Entranced at 26 (threshold 25)
        Assert.Equal("boon_x", cfg.GetProperty("equippedStartBoon").GetString());
        Assert.Equal(3, cfg.GetProperty("consumableSlots").GetInt32());
        Assert.Equal(2, cfg.GetProperty("levels").GetProperty("collar").GetInt32());
        Assert.Equal(1, cfg.GetProperty("craftedItems").GetProperty("the_padlock").GetInt32());
        Assert.Equal("boon_pin", cfg.GetProperty("pinnedBoonId").GetString());
        Assert.True(cfg.GetProperty("denialArmed").GetBoolean());
        Assert.True(cfg.GetProperty("flags").GetProperty("seenDefuseTutorial").GetBoolean());
        Assert.Equal(0, cfg.GetProperty("thoughtTexts").GetArrayLength()); // page default kicks in
    }

    // ============================ asset-stats ============================

    [Fact]
    public void AssetStats_MergeClamps_Ranks_CaseInsensitive_PresenceOnlyLogs()
    {
        using var h = new Harness();
        var merged = h.Meta.OnAssetStats;
        merged(Raw("{\"stats\":["
            + "{\"name\":\"Alpha.png\",\"kind\":\"image\",\"seconds\":3,\"weighted\":10,\"grabs\":2,\"pops\":1},"
            + "{\"name\":\"beta.webm\",\"kind\":\"video\",\"seconds\":-5,\"weighted\":-1,\"grabs\":-2},"
            + "{\"name\":\"" + new string('x', 300) + "\",\"weighted\":99},"
            + "{\"name\":\"alpha.PNG\",\"weighted\":5}" // case-insensitive same row
            + "]}"));
        var stats = h.Slots.AssetStatsStore.Current.Stats;
        Assert.Equal(2, stats.Count); // the 300-char name is refused; alpha merges case-insensitively
        Assert.Equal(3, stats["alpha.png"].Seconds);
        Assert.Equal(15, stats["alpha.png"].Weighted);
        Assert.Equal(2, stats["alpha.png"].Grabs);
        Assert.Equal(0, stats["beta.webm"].Weighted); // negative deltas clamp to 0

        // Ranking: Weighted + Grabs*8 + Pops*2 — alpha (15+16+2=33) beats beta (0).
        var favorites = h.Meta.FavoritesSeed();
        Assert.Equal("Alpha.png", favorites[0]);

        // Media-logging rule (packet framing c): merge logs carry row counts, NEVER names.
        Assert.Contains(h.Log, m => m.Contains("asset-stats merged"));
        Assert.DoesNotContain(h.Log, m => m.Contains("Alpha.png") || m.Contains("beta.webm"));
    }

    [Fact]
    public void AssetStats_Malformed_NeverThrows_ZeroTouched()
    {
        using var h = new Harness();
        h.Meta.OnAssetStats(Raw("{\"notStats\":true}"));
        h.Meta.OnAssetStats(Raw("{\"stats\":\"nope\"}"));
        h.Meta.OnAssetStats(Raw("{\"stats\":[42,null,{\"noName\":1}]}"));
        Assert.Empty(h.Slots.AssetStatsStore.Current.Stats);
    }

    // ============================ tolerance + flags ============================

    [Fact]
    public void MalformedOps_NeverThrow()
    {
        using var h = new Harness();
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":42}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":\"fifty\"}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"craft\",\"id\":\"the_padlock\",\"cost\":[1,2]}")));
        Assert.False(h.Meta.HandleMetaCommand(Raw("{\"op\":\"set-lifetime-boon\",\"id\":null}")));
    }

    [Fact]
    public async Task AbsentProgressionMembers_FlaggedOnce_NotSilent()
    {
        using var dir = new Harness.TempDir();
        // A b2-era slot file: only the b2 members inside the SP-005 envelope.
        File.WriteAllText(Path.Combine(dir.Root, "dtrh_slot1.json"),
            "{\"schemaVersion\":1,\"migrationJournal\":[],\"sparks\":10,\"gold\":5,\"runsCompleted\":2,"
            + "\"bestScore\":100,\"craftedItems\":{\"ragdoll\":1}}");
        var log = new List<string>();
        var slots = new DtrhSaveSlots(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new HarnessSink(log)),
            dir.Root);
        await slots.StartAsync(CancellationToken.None);
        var stats = new DtrhAssetStats(slots.AssetStatsStore, m => log.Add(m));
        var meta = new DtrhMeta(slots.StoreFor(1), slots.IndexStore, stats, _ => { }, m => log.Add(m),
            testMode: false, slots.SlotFilePath(1));
        Assert.Contains(log, m => m.Contains("predates progression members"));
        // …and the additive defaults are live (b2 values preserved).
        var doc = slots.StoreFor(1).Current;
        Assert.Equal(10, doc.Sparks);
        Assert.Equal(2, doc.RunsCompleted);
        Assert.Equal(1, doc.ConsumableSlots); // the WPF load clamp outcome (ChaosMetaStore.cs:71)
        Assert.True(doc.CraftedItems.ContainsKey("ragdoll"));
        await slots.StopAsync();
    }

    [Fact]
    public async Task UnknownMemberPreserve_SurvivesMetaMutationRoundTrip()
    {
        using var dir = new Harness.TempDir();
        File.WriteAllText(Path.Combine(dir.Root, "dtrh_slot1.json"),
            "{\"schemaVersion\":1,\"migrationJournal\":[],\"sparks\":1,\"gold\":0,\"runsCompleted\":0,"
            + "\"bestScore\":0,\"craftedItems\":{},\"futureB5Field\":{\"nested\":true}}");
        var slots = new DtrhSaveSlots(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new HarnessSink([])),
            dir.Root);
        await slots.StartAsync(CancellationToken.None);
        var stats = new DtrhAssetStats(slots.AssetStatsStore, _ => { });
        var meta = new DtrhMeta(slots.StoreFor(1), slots.IndexStore, stats, _ => { }, _ => { },
            testMode: false, slots.SlotFilePath(1));
        Assert.True(meta.HandleMetaCommand(Raw("{\"op\":\"add-gold\",\"amount\":5}")));
        await slots.StoreFor(1).SaveImmediate();
        var text = File.ReadAllText(Path.Combine(dir.Root, "dtrh_slot1.json"));
        Assert.Contains("futureB5Field", text); // unknown-member preserve honored (no schema bump)
        Assert.Contains("\"gold\": 5", text);
        await slots.StopAsync();
    }

    private sealed class HarnessSink(List<string> log) : ILogSink
    {
        public void Log(string message) => log.Add(message);
    }
}
