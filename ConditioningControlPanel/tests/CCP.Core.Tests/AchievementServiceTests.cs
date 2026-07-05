using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class AchievementServiceTests
{
    private class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-achievement-tests-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
        }

        public void Cleanup()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TryUnlock_KnownAchievement_AddsToProgressAndRaisesEvent()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            Achievement? raised = null;
            service.AchievementUnlocked += (_, a) => raised = a;

            var result = service.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();

            Assert.True(result);
            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.NotNull(raised);
            Assert.Equal("plastic_initiation", raised!.Id);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TryUnlock_AlreadyUnlocked_ReturnsFalse()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            service.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();

            var result = service.TryUnlock("plastic_initiation");

            Assert.False(result);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TryUnlock_UnknownAchievement_ReturnsFalse()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());

            var result = service.TryUnlock("not_a_real_achievement");

            Assert.False(result);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void SuppressPopups_SilentUnlock_DoesNotRaiseEvent()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            service.SuppressPopups = true;
            var raised = false;
            service.AchievementUnlocked += (_, _) => raised = true;

            service.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();

            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.False(raised);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TrackBubblePopped_MilestoneAwardsSkillPoint()
    {
        var env = new TestAppEnvironment();
        try
        {
            CoreApp.Settings = new TestSettingsService();
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            var startingPoints = CoreApp.Settings.Current.SkillPoints;

            for (int i = 0; i < 100; i++)
            {
                service.TrackBubblePopped();
            }

            Assert.Equal(startingPoints + 1, CoreApp.Settings.Current.SkillPoints);
        }
        finally
        {
            CoreApp.Settings = null;
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TrackSessionComplete_DeepSleep_Unlocks()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());

            service.TrackSessionComplete("Test", 200, false, false);

            Assert.True(service.Progress.IsUnlocked("deep_sleep"));
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void CheckLevelAchievements_LevelsUnlockAppropriately()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());

            service.CheckLevelAchievements(20);

            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.True(service.Progress.IsUnlocked("dumb_bimbo"));
            Assert.False(service.Progress.IsUnlocked("fully_synthetic"));
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void Save_PersistsProgress()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            service.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();

            service.Save();

            var json = File.ReadAllText(Path.Combine(env.UserDataPath, "achievements.json"));
            Assert.Contains("plastic_initiation", json);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public async Task DisposeAsync_SavesAndDisposes()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            service.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();

            await service.DisposeAsync();

            var json = File.ReadAllText(Path.Combine(env.UserDataPath, "achievements.json"));
            Assert.Contains("plastic_initiation", json);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void ComputeAutonomyTickCredit_CapsLongTick_AndCreditsRealTime()
    {
        // A starved/backgrounded tick (5 min since the last tick, well past the ~10s cap) must still
        // credit the cap, NOT drop to zero. This is the v6.2.9 regression: the old hard
        // "elapsed < 0.1 or drop it" guard silently threw away real active time, so a 15-min
        // Takeover quest crawled to an hour when the app was backgrounded.
        const double cap = 10.0 / 60.0;
        Assert.Equal(cap, AchievementService.ComputeAutonomyTickCredit(5.0), 6);

        // A normal sub-cap tick credits the full elapsed time.
        Assert.Equal(0.05, AchievementService.ComputeAutonomyTickCredit(0.05), 6);

        // Exactly at the cap credits the cap.
        Assert.Equal(cap, AchievementService.ComputeAutonomyTickCredit(cap), 6);

        // A non-positive interval credits nothing (no zero/negative time padding).
        Assert.Equal(0.0, AchievementService.ComputeAutonomyTickCredit(0.0), 6);
        Assert.Equal(0.0, AchievementService.ComputeAutonomyTickCredit(-1.0), 6);
    }

    private class TestSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
    }
}
