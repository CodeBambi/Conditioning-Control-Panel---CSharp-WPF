using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The hub shelves' purchases (train / unlock / deepen / equip / habit switch / bench)
/// through Core's ChaosMeta and ChaosBench: prices, balances and what lands in the save file.
/// CorePaths.UserData is a temp dir in this project.</summary>
public class ChaosShelvesTests
{
    private static ChaosMetaState Disk() => ChaosMetaStore.Load(2);

    [Fact]
    public void Shelf_purchases_spend_the_right_price_and_persist_to_the_active_slot()
    {
        var old = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Delete(2);
            ChaosMetaStore.Save(new ChaosMetaState
            {
                RunsCompleted = 10, Sparks = 1000, Gold = 30,
                LessonsComplete = new HashSet<string> { "slow_fuses" },
            }, 2);
            ChaosMeta.SwitchSlot(2);

            // train a habit: 120 sparks, owned, switched on; the lesson gate held until complete
            Assert.True(ChaosMeta.TryPurchase("slow_fuses"));
            Assert.False(ChaosMeta.TryPurchase("slow_fuses"));             // already owned
            Assert.False(ChaosMeta.TryPurchase("silk_touch"));             // lesson-blocked
            Assert.Equal(880, Disk().Sparks);
            Assert.Contains("slow_fuses", Disk().PurchasedUpgrades);

            // habit switch persists
            ChaosMeta.SetUpgradeActive("slow_fuses", false);
            Assert.Contains("slow_fuses", Disk().DisabledUpgrades);

            // unlock a charm (120) and a lessonless accessory (300); no accessory pocket yet
            Assert.True(ChaosMeta.TryUnlockBoon("blank_eyes"));
            Assert.True(ChaosMeta.TryUnlockBoon("the_spanker"));
            Assert.Equal(460, Disk().Sparks);
            Assert.False(ChaosMeta.IsBoonActive("the_spanker"));           // pockets full (0)

            // deepen: 450 for level 2; the capstone (level 3) wants Devoted
            Assert.True(ChaosMeta.TryUpgradeBoon("the_spanker"));
            Assert.Equal((10, 2), (Disk().Sparks, Disk().LifetimeBoonLevels["the_spanker"]));
            Assert.False(ChaosMeta.TryUpgradeBoon("the_spanker"));

            // bench: short on gold for the first toy pocket -> her one gift, gold zeroed
            Assert.Equal(ChaosBenchBuy.Gift, ChaosBench.TryBuy(BenchIds.ToyPocket1));
            Assert.Equal((0, 1, true), (Disk().Gold, Disk().ToyPockets, Disk().GiftGiven));
            Assert.Equal(ChaosBenchBuy.Denied, ChaosBench.TryBuy(BenchIds.AccPocket1));   // 0 < 150, no second gift
            Assert.DoesNotContain(BenchIds.AccPocket1, Disk().BenchPurchases);

            ChaosMeta.AddGold(150);
            Assert.Equal(ChaosBenchBuy.Bought, ChaosBench.TryBuy(BenchIds.AccPocket1));
            Assert.Equal((0, 1), (Disk().Gold, Disk().AccessoryPockets));
            Assert.Contains(BenchIds.AccPocket1, Disk().BenchPurchases);

            // equip into the new pocket, then the pocket is full for anything else
            Assert.True(ChaosMeta.SetBoonActive("the_spanker", true));
            Assert.Contains("the_spanker", Disk().ActiveLifetimeBoons);
            Assert.False(ChaosMeta.HasFreePocket(ChaosBoonCategory.Accessory));

            // switching slots reloads from disk: the purchases are in the save, not only in memory
            ChaosMeta.SwitchSlot(1);
            ChaosMeta.SwitchSlot(2);
            Assert.True(ChaosMeta.IsOwned("slow_fuses"));
            Assert.Equal(2, ChaosMeta.BoonLevel("the_spanker"));
        }
        finally
        {
            ChaosMetaStore.Delete(2);
            CoreSettings.Current.ChaosActiveSlot = old;
            ChaosMeta.Init();
        }
    }
}
