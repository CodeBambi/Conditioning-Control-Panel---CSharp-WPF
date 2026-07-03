using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Progression;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Regression coverage for the lot-7 AchievementService fixes:
/// atomic-write crash recovery (R1/R2/R12) and prestige monotonicity (A2/PS-3).
/// Self-contained fixtures so this file shares nothing with the other test files.
/// </summary>
public class AchievementServiceLot7Tests
{
    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-achievement-lot7-{Guid.NewGuid():N}");
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
    public void LoadProgress_RecoversFromTempFile_WhenMainMissing()
    {
        var env = new TestAppEnvironment();
        try
        {
            // First service writes a real, atomically-serialized achievements.json.
            var first = new AchievementService(env, new DebugLogger<AchievementService>());
            first.TryUnlock("plastic_initiation");
            Dispatcher.UIThread.RunJobs();
            first.Save();
            first.Dispose();

            var mainPath = Path.Combine(env.UserDataPath, "achievements.json");
            var tmpPath = mainPath + ".tmp";

            // Simulate a crash after the temp write but before the atomic move completed:
            // only achievements.json.tmp survives on disk.
            File.Move(mainPath, tmpPath, overwrite: true);
            Assert.False(File.Exists(mainPath));
            Assert.True(File.Exists(tmpPath));

            // A fresh service must recover the unlock from the temp file and restore the main file.
            var recovered = new AchievementService(env, new DebugLogger<AchievementService>());
            try
            {
                Assert.True(recovered.Progress.IsUnlocked("plastic_initiation"));
                Assert.True(File.Exists(mainPath));
            }
            finally
            {
                recovered.Dispose();
            }
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TrackSkillPointsSpent_And_Reconcile_AreMonotonic()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger<AchievementService>());
            try
            {
                Assert.Equal(0, service.Progress.LifetimeSkillPointsSpent);

                service.TrackSkillPointsSpent(5);
                Assert.Equal(5, service.Progress.LifetimeSkillPointsSpent);

                // Non-positive amounts are ignored.
                service.TrackSkillPointsSpent(0);
                service.TrackSkillPointsSpent(-3);
                Assert.Equal(5, service.Progress.LifetimeSkillPointsSpent);

                // Reconcile never lowers the local (monotonic) value.
                service.ReconcileLifetimePointsSpent(3);
                Assert.Equal(5, service.Progress.LifetimeSkillPointsSpent);

                // Reconcile adopts a higher authoritative server value.
                service.ReconcileLifetimePointsSpent(10);
                Assert.Equal(10, service.Progress.LifetimeSkillPointsSpent);
            }
            finally
            {
                service.Dispose();
            }
        }
        finally
        {
            env.Cleanup();
        }
    }
}
