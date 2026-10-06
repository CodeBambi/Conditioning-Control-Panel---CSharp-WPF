using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    public void AttentionTrackersCountAndUnlockAtWpfThresholds()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        var p = engine.Progress;
        engine.TrackAttentionCheckPassed();
        engine.TrackAttentionCheckPassed();
        engine.TrackAttentionCheckPassed(isVideo: true);
        Assert.Equal((3, 1), (p.TotalAttentionChecksPassed, p.VideoAttentionChecksPassed));
        engine.TrackVideoAttentionCheckFailed();
        Assert.Equal(1, p.VideoAttentionChecksFailed);

        engine.TrackAttentionCheckFailed(); engine.TrackAttentionCheckFailed();
        Assert.False(p.IsUnlocked("mercy_beggar"));
        engine.TrackAttentionCheckFailed();                  // 3rd failure
        Assert.Equal(3, p.AttentionCheckFailures);
        Assert.True(OnDisk(MainPath, "mercy_beggar"));

        while (p.TotalAttentionChecksPassed < 99) engine.TrackAttentionCheckPassed();
        Assert.False(p.IsUnlocked("eyes_front"));
        engine.TrackAttentionCheckPassed();                  // 100th pass
        Assert.True(OnDisk(MainPath, "eyes_front"));
    }

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
    public void StoreResolvesTheProgressInsideItsLock()
    {
        var store = new AchievementStore(MainPath);
        var held = false;

        Assert.True(store.Write(() => { held = Monitor.IsEntered(store._saveLock); return new AchievementProgress(); }));

        Assert.True(held);
    }

    [Fact]
    public async Task AutosaveQueuedBeforeResetDoesNotResurrectTheOldProgress()
    {
        var store = new AchievementStore(MainPath);
        var engine = new AchievementEngine(store);
        Assert.True(engine.TryUnlock(FreeId));
        engine.IsDirty = true;

        Task autosave;
        lock (store._saveLock)
        {
            autosave = engine.SaveIfDirtyAsync(); // blocks on the lock we hold
            // ponytail: a sleep, not a hook - a slow scheduler can only make this pass vacuously, never fail spuriously.
            Thread.Sleep(200);
            engine.Reset(); // re-entrant on this thread: writes the empty progress now
        }
        await autosave;

        Assert.False(OnDisk(MainPath, FreeId));
    }

    [Fact]
    public void ResetSavesAnEmptyFileAndIsClean()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        Assert.True(engine.TryUnlock(FreeId));
        engine.IsDirty = true;

        engine.Reset();

        Assert.False(engine.IsDirty);
        Assert.Empty(engine.Progress.UnlockedAchievements);
        Assert.Empty(new AchievementStore(MainPath).Load().UnlockedAchievements);
    }

    [Fact]
    public void UnknownIdIsRefusedWithoutWriteOrEvent()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        var raised = 0;
        engine.Unlocked += (_, _) => raised++;

        Assert.False(engine.TryUnlock("no_such_achievement_" + Guid.NewGuid().ToString("N")));

        Assert.Equal(0, raised);
        Assert.False(File.Exists(MainPath));
        Assert.False(engine.IsDirty);
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

    [Fact]
    public void BubbleCountStreakUnlocksAtTheRuleAndAWrongAnswerResetsIt()
    {
        var engine = new AchievementEngine(new AchievementStore(MainPath));
        for (var i = 1; i < AchievementRules.MathematiciansNightmareStreak; i++) engine.TrackBubbleCountResult(true);
        engine.TrackBubbleCountResult(false);
        Assert.Equal(0, engine.Progress.BubbleCountCorrectStreak);
        for (var i = 1; i < AchievementRules.MathematiciansNightmareStreak; i++) engine.TrackBubbleCountResult(true);
        Assert.False(engine.Progress.IsUnlocked("mathematicians_nightmare"));

        engine.TrackBubbleCountResult(true);

        Assert.True(OnDisk(MainPath, "mathematicians_nightmare"));
        Assert.Equal(2 * AchievementRules.MathematiciansNightmareStreak - 1, engine.Progress.TotalBubbleCountCorrect);
        Assert.Equal(1, engine.Progress.TotalBubbleCountFailed);
    }

    // Literal catalogue numbers (Achievement.All): 73 ids = 60 free (2 parked, 4 premium-program, 4 invite-reward) + 13 exclusive.
    // A catalogue change must update these on purpose.
    [Theory]
    [InlineData(false, 50, 13, 50)]
    [InlineData(true, 58, 13, 71)]
    public void FreeAndPatronCountsAreSeparateAndFollowPremium(bool premium, int freeTotal, int patronTotal, int reachableTotal)
    {
        var previous = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => premium;
            var engine = new AchievementEngine(new AchievementStore(MainPath));
            engine.TryUnlock(FreeId);

            Assert.Equal(1, engine.GetUnlockedCount(exclusive: false));
            Assert.Equal(0, engine.GetUnlockedCount(exclusive: true));
            Assert.Equal(freeTotal, engine.GetTotalCount(exclusive: false));
            Assert.Equal(patronTotal, engine.GetTotalCount(exclusive: true));
            Assert.Equal((1, reachableTotal), engine.GetReachableCounts());
        }
        finally { CoreEntitlement.HasPremiumProvider = previous; }
    }

    /// <summary>WPF GamificationBridge.OnQuizCompleted / OnQuizAbandoned, now Core so the Avalonia intake fires them.</summary>
    [Fact]
    public void GradedRunsFeedTheQuizAchievementsLikeTheWpfBridge()
    {
        var previous = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            var engine = new AchievementEngine(new AchievementStore(MainPath)) { SuppressPopups = true };
            engine.TrackQuizAbandoned();
            engine.TrackQuizAbandoned();
            engine.TrackQuizCompleted(passed: true, perfect: false, "bambi");   // a finish breaks the walk-out streak
            Assert.Equal(0, engine.Progress.IntakeQuitStreak);
            for (int i = 0; i < 3; i++) engine.TrackQuizAbandoned();
            Assert.True(engine.Progress.IsUnlocked("held_back"));

            engine.TrackQuizCompleted(true, perfect: true, "bambi");
            Assert.True(engine.Progress.IsUnlocked("top_of_the_class"));
            engine.TrackQuizCompleted(true, true, "drone");
            Assert.False(engine.Progress.IsUnlocked("honor_roll"));
            engine.TrackQuizCompleted(true, true, "sissy");
            Assert.True(engine.Progress.IsUnlocked("honor_roll"));
            for (int i = 0; i < 5; i++) engine.TrackQuizCompleted(true, false, null);   // nine passes so far
            Assert.False(engine.Progress.IsUnlocked("teachers_pet"));
            engine.TrackQuizCompleted(true, false, null);   // the tenth pass
            Assert.True(engine.Progress.IsUnlocked("teachers_pet"));
            Assert.True(engine.IsDirty);
        }
        finally { CoreEntitlement.HasPremiumProvider = previous; }
    }
}
