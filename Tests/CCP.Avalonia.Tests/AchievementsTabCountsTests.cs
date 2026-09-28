using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Achievements tab counters read the live engine (WPF MainWindow.AchievementsTab.cs:155-172).</summary>
public sealed class AchievementsTabCountsTests
{
    [Fact]
    public void CountersReadTheEngineAndKeepFreeAndPatronSeparate()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-ach-tab-").FullName;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            foreach (var a in Achievement.All.Values.Where(a => !a.IsExclusive && !a.IsHidden).Take(2))
                engine.TryUnlock(a.Id);

            var vm = new AchievementsTabViewModel(engine);

            Assert.Equal(2, vm.Unlocked);
            Assert.Equal(engine.GetTotalCount(exclusive: false), vm.Total);
            Assert.Equal(0, vm.PatronUnlocked);
            Assert.Equal(engine.GetTotalCount(exclusive: true), vm.PatronTotal);
            Assert.Equal(Loc.GetF("label_0_1_achievements_unlocked", 2, vm.Total), vm.LocUnlockedCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
