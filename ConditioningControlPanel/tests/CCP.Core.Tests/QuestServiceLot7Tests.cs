using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Regression coverage for the lot-7 QuestService fixes:
/// Q1 (streak advances once per day, not once per daily completion) and
/// R6 (FixStreakDay records the day and persists it to disk).
/// Self-contained fixtures so this file shares nothing with the other test files.
/// [AvaloniaFact] because the QuestService constructor creates DispatcherTimers.
/// </summary>
public class QuestServiceLot7Tests
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
            Root = Path.Combine(Path.GetTempPath(), $"ccp-quest-lot7-{Guid.NewGuid():N}");
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

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private sealed class FakeSkillTreeService : ISkillTreeService
    {
        public event EventHandler<string>? SkillUnlocked;
        public event EventHandler? PinkRushStarted;
        public int TotalPointsSpent => 0;
        public bool HasSkill(string skillId) => false;
        public double GetTotalXpMultiplier() => 1.0;
        public Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
            => Task.FromResult<(bool, string?)>((false, null));
        public void Start() { }
        public void Stop() { }
        public void TriggerPinkRush() { }
        public bool UseStreakShield() => false;
        public bool UseOopsieInsurance() => false;
        public int GetDailyStreakBonus(int consecutiveDays) => 0;
        public int GetDailyFreeRerolls() => 0;
        public void AddConditioningTime(double minutes) { }
        // GetRerollBonusMultiplier() (1.0) and CheckPerfectWeekBonus() (0) use interface defaults.
    }

    [AvaloniaFact]
    public void CompletingThreeDailies_AdvancesStreakOnce_NotThreeTimes()
    {
        var env = new TestAppEnvironment();
        try
        {
            var settingsService = new FakeSettingsService();
            var settings = settingsService.Current;
            settings.DailyQuestStreak = 5;
            var yesterday = DateTime.Today.AddDays(-1);
            // LastDailyQuestDate is not < yesterday, so the streak-shield branch never fires.
            settings.LastDailyQuestDate = yesterday;

            var service = new QuestService(settingsService, new FakeSkillTreeService(), env);
            try
            {
                // Seed yesterday as completed so the streak "continues" — this is the path
                // that triple-counted before the fix (every completion re-advanced the streak).
                service.Progress.DailyQuestCompletionDates.Add(yesterday);

                for (int i = 0; i < QuestService.MaxDailyQuestsPerDay; i++)
                {
                    service.CompleteDailyQuest();
                }

                Assert.Equal(QuestService.MaxDailyQuestsPerDay, service.Progress.GetDailyQuestsCompletedToday());
                // Fixed: streak advanced exactly once (5 -> 6). Bug advanced once per completion (5 -> 8).
                Assert.Equal(6, settings.DailyQuestStreak);
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

    [AvaloniaFact]
    public void FixStreakDay_RecordsDate_AndPersistsToDisk()
    {
        var env = new TestAppEnvironment();
        try
        {
            var settingsService = new FakeSettingsService();
            var skill = new FakeSkillTreeService();
            var missed = DateTime.Today.AddDays(-3);

            var service = new QuestService(settingsService, skill, env);
            try
            {
                Assert.DoesNotContain(service.Progress.DailyQuestCompletionDates, d => d.Date == missed.Date);

                service.FixStreakDay(missed);

                Assert.Contains(service.Progress.DailyQuestCompletionDates, d => d.Date == missed.Date);
            }
            finally
            {
                service.Dispose();
            }

            // A fresh service must load the fixed date from disk (write survived).
            var reloaded = new QuestService(settingsService, skill, env);
            try
            {
                Assert.Contains(reloaded.Progress.DailyQuestCompletionDates, d => d.Date == missed.Date);
            }
            finally
            {
                reloaded.Dispose();
            }
        }
        finally
        {
            env.Cleanup();
        }
    }

    [AvaloniaFact]
    public void TrackBubblesPopped_AdvancesBubbleQuestByBatch()
    {
        // DtRH web run reports its total popped bubbles on completion; TrackBubblesPopped advances a
        // bubble quest by the full batch in one call (not one at a time).
        var env = new TestAppEnvironment();
        try
        {
            var settingsService = new FakeSettingsService();
            var service = new QuestService(settingsService, new FakeSkillTreeService(), env);
            // Seed an active daily Bubbles quest (pop_parade_d: Pop 40 bubbles).
            service.Progress.DailyQuest = new ActiveQuest("pop_parade_d");

            service.TrackBubblesPopped(10);

            Assert.Equal(10, service.Progress.DailyQuest.CurrentProgress);
            Assert.False(service.Progress.DailyQuest.IsCompleted);

            // zero/negative is a no-op.
            service.TrackBubblesPopped(0);
            Assert.Equal(10, service.Progress.DailyQuest.CurrentProgress);
        }
        finally
        {
            env.Cleanup();
        }
    }
}
