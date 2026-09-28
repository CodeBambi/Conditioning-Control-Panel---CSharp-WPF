using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// achievements.json is the user's lifetime progress. Its shape is whatever System.Text.Json
/// makes of <see cref="AchievementProgress"/>, so this pins the exact persisted contract (name,
/// CLR type, declaration order) taken from the WPF model before it moved to Core. A renamed,
/// retyped, added or newly [JsonIgnore]d property fails here until the list is updated on purpose.
/// </summary>
public sealed class AchievementProgressShapeTests
{
    private static readonly (string Name, Type Type)[] Persisted =
    {
        ("UnlockedAchievements", typeof(HashSet<string>)),
        ("TotalPinkFilterMinutes", typeof(double)),
        ("TotalSpiralMinutes", typeof(double)),
        ("ContinuousSpiralMinutes", typeof(double)),
        ("TotalFlashImages", typeof(int)),
        ("ConsecutiveDays", typeof(int)),
        ("LastLaunchDate", typeof(DateTime)),
        ("AltTabPressedThisSession", typeof(bool)),
        ("LastPanicPressTime", typeof(DateTime?)),
        ("LongestSessionMinutes", typeof(double)),
        ("TotalBubblesPopped", typeof(int)),
        ("BubbleCountCorrectStreak", typeof(int)),
        ("BubbleCountBestStreak", typeof(int)),
        ("AttentionCheckFailures", typeof(int)),
        ("ContinuousMindWipeSeconds", typeof(double)),
        ("HasPerfectLockCard", typeof(bool)),
        ("FastestLockCardSeconds", typeof(double)),
        ("TotalVideoMinutes", typeof(double)),
        ("TotalLockCardsCompleted", typeof(int)),
        ("HasHitCorner", typeof(bool)),
        ("TotalAttentionChecksPassed", typeof(int)),
        ("VideoAttentionChecksPassed", typeof(int)),
        ("VideoAttentionChecksFailed", typeof(int)),
        ("TotalBubbleCountGames", typeof(int)),
        ("TotalBubbleCountCorrect", typeof(int)),
        ("TotalBubbleCountFailed", typeof(int)),
        ("TotalSessionsStarted", typeof(int)),
        ("TotalSessionsAbandoned", typeof(int)),
        ("TotalXPEarned", typeof(double)),
        ("TotalSkillPointsEarned", typeof(int)),
        ("LifetimeSkillPointsSpent", typeof(long)),
        ("AvatarClickCount", typeof(int)),
        ("AvatarClickStartTime", typeof(DateTime?)),
        ("NeedyDollClickCount", typeof(int)),
        ("NeedyDollClickStartTime", typeof(DateTime?)),
        ("CompletedSessions", typeof(HashSet<string>)),
        ("CompletedGoodGirlsWithStrictLock", typeof(bool)),
        ("CompletedMorningDriftInMorning", typeof(bool)),
        ("CompletedGamerGirlNoAltTab", typeof(bool)),
        ("CompletedSessionWithNoPanic", typeof(bool)),
        ("HasTotalLockdown", typeof(bool)),
        ("HasSystemOverload", typeof(bool)),
        ("EnhancementsPlayed", typeof(int)),
        ("DeeperMinutes", typeof(double)),
        ("EnhancementsBuilt", typeof(int)),
        ("ModsInstalled", typeof(int)),
        ("ActivatedModIds", typeof(HashSet<string>)),
        ("CommunityModIds", typeof(HashSet<string>)),
        ("PerfectedQuizCategories", typeof(HashSet<string>)),
        ("KeywordTriggersFired", typeof(int)),
        ("CompanionMessages", typeof(int)),
        ("CompanionChatBackfilled", typeof(bool)),
        ("QuizzesPassed", typeof(int)),
        ("QuizFailStreak", typeof(int)),
        ("IntakeQuitStreak", typeof(int)),
        ("BlinkTrainerBlinks", typeof(int)),
        ("GazePops", typeof(int)),
    };

    [Fact]
    public void PersistedPropertiesMatchTheWpfShape()
    {
        var info = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            .GetTypeInfo(typeof(AchievementProgress));
        var actual = info.Properties.Where(p => p.Get != null).Select(p => (p.Name, p.PropertyType)).ToArray();

        Assert.Equal(Persisted, actual);

        // Written is not enough: every persisted property must also load back, with no custom
        // converter or number handling changing what the bytes mean.
        Assert.All(info.Properties.Where(p => p.Get != null), p =>
        {
            Assert.NotNull(p.Set);
            Assert.Null(p.CustomConverter);
            Assert.Null(p.NumberHandling);
        });
        Assert.Equal(double.MaxValue, new AchievementProgress().FastestLockCardSeconds);
    }
}
