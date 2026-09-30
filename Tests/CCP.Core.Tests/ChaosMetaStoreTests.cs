using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The save-slot contract both heads' pickers read (CorePaths.UserData is a temp dir here).</summary>
public class ChaosMetaStoreTests
{
    [Fact]
    public void Slot_save_summary_delete_and_active_slot_round_trip()
    {
        var old = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Delete(3);
            Assert.False(ChaosMetaStore.AllSummaries()[2].Exists);

            ChaosMetaStore.Save(new ChaosMetaState { Sparks = 42, Gold = 7, RunsCompleted = 10 }, 3);
            var s = ChaosMetaStore.AllSummaries()[2];
            Assert.Equal((3, true, 42, 7, 10), (s.Slot, s.Exists, s.Sparks, s.Gold, s.RunsCompleted));

            Assert.Equal(3, ChaosMetaStore.SetActiveSlot(3));
            Assert.Equal(3, CoreSettings.Current.ChaosActiveSlot);
            Assert.Equal(42, ChaosMetaStore.Load().Sparks);
            Assert.Equal(1, ChaosMetaStore.SetActiveSlot(9));   // out of range clamps to 1, as WPF

            Assert.True(ChaosMetaStore.Delete(3));
            Assert.False(File.Exists(ChaosMetaStore.SlotFilePath(3)));
            Assert.False(ChaosMetaStore.ReadSummary(3).Exists);
        }
        finally { CoreSettings.Current.ChaosActiveSlot = old; }
    }

    [Fact]
    public void Legacy_file_migrates_into_slot_1_once_and_both_files_survive()
    {
        var legacy = Path.Combine(CorePaths.UserData, "chaos_meta.json");
        try
        {
            ChaosMetaStore.Delete(1);
            Directory.CreateDirectory(CorePaths.UserData);
            File.WriteAllText(legacy, Newtonsoft.Json.JsonConvert.SerializeObject(
                new ChaosMetaState { Sparks = 77, RunsCompleted = 4 }));

            Assert.Equal(77, ChaosMetaStore.Load(1).Sparks);
            Assert.Equal(77, ChaosMetaStore.Load(1).Sparks);   // second load is idempotent

            Assert.True(File.Exists(legacy));
            var slot1 = ChaosMetaStore.SlotFilePath(1);
            Assert.True(File.Exists(slot1));
            Assert.Equal(File.ReadAllText(legacy), File.ReadAllText(slot1));
        }
        finally { ChaosMetaStore.Delete(1); }
    }

    [Fact]
    public void Ranks_and_specifics_match_the_wpf_copy()
    {
        Assert.Equal("Slipping", ChaosRanks.Name(ChaosRanks.For(10)));
        Assert.Equal(ChaosRank.Tempted, ChaosRanks.For(9));
        Assert.Equal("unlocks at Devoted: 50 descents finished. you've finished 14.",
            ChaosRanks.RankSpecifics(ChaosRank.Devoted, 14));
    }
}
