using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// <see cref="AchievementEngine"/> against a sandbox achievements.json, never the real one.
/// The rules mirror WPF AchievementService.TryUnlock / TryUnlockExclusive as they stood before
/// the move (unlock -> save -> event once; suppressed still persists; exclusive needs premium).
/// </summary>
public sealed class AchievementEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-ach-engine-").FullName;
    private string MainPath => Path.Combine(_dir, "achievements.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FreeId => Achievement.All.Values.First(a => !a.IsExclusive).Id;
    private static string ExclusiveId => Achievement.All.Values.First(a => a.IsExclusive).Id;

    private static bool OnDisk(string path, string id) =>
        new AchievementStore(path).Load().IsUnlocked(id);

    [Fact]
    public void UnlockPersistsThenRaisesExactlyOnce()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        var raised = new List<Achievement>();
        engine.Unlocked += (_, a) => { Assert.True(OnDisk(MainPath, a.Id)); raised.Add(a); }; // persisted BEFORE the event

        Assert.True(engine.TryUnlock(FreeId));

        Assert.Equal(FreeId, Assert.Single(raised).Id);
        Assert.True(OnDisk(MainPath, FreeId));
        Assert.False(engine.IsDirty);
    }

    [Fact]
    public void AlreadyUnlockedRaisesNothingAndWritesNothing()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        Assert.True(engine.TryUnlock(FreeId));
        File.Delete(MainPath); // any further write would recreate it
        var raised = 0;
        engine.Unlocked += (_, _) => raised++;

        Assert.False(engine.TryUnlock(FreeId));

        Assert.Equal(0, raised);
        Assert.False(File.Exists(MainPath));
        Assert.False(engine.IsDirty);
    }

    [Fact]
    public void SuppressedPersistsButRaisesNothing()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath)) { SuppressPopups = true };
        var raised = 0;
        engine.Unlocked += (_, _) => raised++;

        Assert.True(engine.TryUnlock(FreeId));

        Assert.Equal(0, raised);
        Assert.True(OnDisk(MainPath, FreeId));
    }

    [Fact]
    public void ExclusiveRefusedWithoutPremiumAndGrantedWithIt()
    {
        var previous = CoreEntitlement.HasPremiumProvider;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(MainPath));
            var raised = 0;
            engine.Unlocked += (_, _) => raised++;

            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(engine.TryUnlockExclusive(ExclusiveId));
            Assert.False(engine.Progress.IsUnlocked(ExclusiveId));
            Assert.False(File.Exists(MainPath));
            Assert.Equal(0, raised);

            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(engine.TryUnlockExclusive(ExclusiveId));
            Assert.True(OnDisk(MainPath, ExclusiveId));
            Assert.Equal(1, raised);
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = previous;
        }
    }

    [Fact]
    public async Task FailedWriteStaysDirtyAndTheNextAutosaveRetries()
    {
        // A FILE where the directory should be: CreateDirectory throws, Write returns false.
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "");
        var path = Path.Combine(blocker, "achievements.json");
        var engine = new AchievementEngine(new AchievementStore(path));

        Assert.True(engine.TryUnlock(FreeId));
        Assert.True(engine.IsDirty); // the failed save re-armed it

        File.Delete(blocker);
        await engine.SaveIfDirtyAsync();

        Assert.False(engine.IsDirty);
        Assert.True(OnDisk(path, FreeId));
    }
}
